using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.CombatPresentation;
using TianZhang.Features.Settlement;
using TianZhang.Features.WorldMap;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    /// <summary>Production entry, quotes, AI and settlement; only the encounter/combat random inputs are fixed.</summary>
    public sealed class F4FormalFootprintPlayModeTests
    {
        private string saveDirectory;
        private Mouse mouse;
        private InputSettings.BackgroundBehavior background;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        private static string Evidence => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/f4-formal"));

        [UnitySetUp]
        public IEnumerator CreateFormalPlayer()
        {
            background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            mouse = InputSystem.AddDevice<Mouse>();
            var old = Object.FindFirstObjectByType<GameBootstrap>();
            if (old != null) { Object.Destroy(old.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            yield return null;
            saveDirectory = Path.Combine(Application.temporaryCachePath, "TianZhang-F4-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saveDirectory));
            var creation = Find<CharacterCreationController>();
            creation.Open();
            Assert.IsTrue(creation.SelectOrigin("origin_minor_clan"));
            creation.Submit("f4-formal", "双格验收");
            yield return SceneReady(GameplaySceneNames.World);
            GuanzhongBasicAttackPlayModeTests.PrepareEquippedPlayerFixture();
            var world = Find<WorldMapController>();
            Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            Directory.CreateDirectory(Evidence);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            InputSystem.settings.backgroundBehavior = background;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
#endif
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null) { Object.Destroy(bootstrap.gameObject); yield return null; }
            Scene previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("F4FormalCleanup"));
            yield return SceneManager.UnloadSceneAsync(previous);
            if (saveDirectory != null && Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator ShijiahouEncounterAt1920() => Verify("shijiahou_encounter", 1920, 1080);
        [UnityTest, Timeout(600000)]
        public IEnumerator ShijiahouEncounterAt1366() => Verify("shijiahou_encounter", 1366, 768);
        [UnityTest, Timeout(600000)]
        public IEnumerator LieshijiaEncounterAt1920() => Verify("lieshijia_encounter", 1920, 1080);
        [UnityTest, Timeout(600000)]
        public IEnumerator LieshijiaEncounterAt1366() => Verify("lieshijia_encounter", 1366, 768);

#if UNITY_EDITOR
        [UnityTest, Timeout(600000)]
        public IEnumerator CsvNarrowPassageStopsRankTwoAiWhileRankOneCrossesThroughTheCoordinator()
        {
            GuanzhongCombatEquipmentPlayModeTests.SetGameSize(1366, 768);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            var adventure = Find<AdventureController>();
            for (int frame = 0; frame < 300 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            var original = Field<EnvironmentProfileAsset>(adventure, "environmentProfile");
            string[] csv = File.ReadAllLines(Path.Combine(Application.dataPath, "DataConfig/EnvironmentProfiles.csv"));
            string header = csv.First(line => line.StartsWith("profileId,", StringComparison.Ordinal));
            string row = csv.Single(line => line.StartsWith(original.profileId + ",", StringComparison.Ordinal));
            string narrowEdges = string.Join("|", original.directedEdges
                .Where(edge => (edge.fromQ < 2) != (edge.toQ < 2))
                .Select(edge => edge.fromQ + ":" + edge.fromR + ">" + edge.toQ + ":" + edge.toR + "@1"));
            Assert.IsNotEmpty(narrowEdges);
            row = row.Substring(0, row.LastIndexOf(',') + 1) + narrowEdges;
            Type importer = Type.GetType("TianZhang.Editor.WorldContentImporter, TianZhang.Editor", true);
            var definitions = (EnvironmentProfileDefinition[])importer.GetMethod("ParseEnvironmentProfiles")
                .Invoke(null, new object[] { new[] { header, row }, "F4-formal-clearance-fixture.csv" });
            var fixture = ScriptableObject.CreateInstance<EnvironmentProfileAsset>();
            try
            {
                fixture.Apply(definitions.Single());
                Assert.IsTrue(fixture.TryCreateDefinition(out _, out string reason), reason);
                Set(adventure, "environmentProfile", fixture);
                adventure.SetEncounterRandomSource(new EncounterRandom());
                // Both first-encounter enemies start on the far side of this fixture's q=1/2 boundary.
                Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
                var encounter = Find<EncounterCoordinator>();
                var session = Field<CombatSession>(encounter, "session");
                var spawned = Field<AdventureSpawnSet>(encounter, "spawned");
                var provider = Find<Static3DCombatUnitPresentationProvider>();
                var hud = Find<CombatHudPresenter>();
                var observer = new ObservedPresentation(provider, session, spawned.Player);
                Set(encounter, "unitPresentation", observer);
                encounter.SetCombatResolutionRandomSource(new GuanzhongBasicAttackPlayModeTests.OutcomeFixtureRandom(session, true));
                var rejectedActors = new HashSet<string>();
                var service = new CombatCommandService();
                observer.OnFrame = () =>
                {
                    foreach (CombatantSnapshot enemy in spawned.Enemies)
                        Assert.IsTrue(enemy.OccupiedCells.All(cell => cell.Q >= 2), "Rank 2 must not cross the narrow boundary.");
                    if (!session.Combatants.TryGet(session.CurrentActorId, out var actor) || actor.Team == CombatTeam.Player ||
                        actor.ActionPointsRemainingUnits <= 0 || rejectedActors.Contains(actor.Id)) return;
                    var command = new CombatCommand(CombatCommandKind.Move, actor.Id, destination: new HexCoord(1, 5));
                    CombatCommandPreview quote = service.Preview(session, command);
                    Assert.IsFalse(quote.IsLegal);
                    Assert.IsNotEmpty(quote.RejectionReason);
                    Assert.IsEmpty(quote.MovementSteps);
                    // The formal board also has rocks; the board may report the first rock rejection.
                    // The obstacle-free EditMode CSV fixture checks the precise clearance reason.
                    SpatialPose pose = actor.Pose;
                    int ap = actor.ActionPointsRemainingUnits, events = observer.Events.Count;
                    Assert.IsTrue(provider.TryGetPresentedPose(actor.Id, out var shown));
                    // This is the same execution method called by the production AI, during its actual turn.
                    var rejected = (CombatActionResult)typeof(EncounterCoordinator).GetMethod("ExecuteCommand",
                        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(encounter, new object[] { command, quote });
                    Assert.IsFalse(rejected.Succeeded);
                    Assert.AreEqual(quote.RejectionReason, rejected.RejectionReason);
                    Assert.AreEqual(pose, actor.Pose);
                    Assert.AreEqual(ap, actor.ActionPointsRemainingUnits);
                    Assert.AreEqual(events, observer.Events.Count, "A rejected move must never reach the presentation port.");
                    Assert.IsTrue(provider.TryGetPresentedPose(actor.Id, out var after));
                    Assert.AreEqual(shown, after);
                    rejectedActors.Add(actor.Id);
                };
                yield return Ready(encounter, observer);
                // Cross the same boundary before enemies approach, then return to its left bank.
                foreach (HexCoord destination in new[] { new HexCoord(2, 0), new HexCoord(1, 0) })
                {
                    var candidate = new CombatActionCandidate(CombatActionKind.Move, spawned.Player.Id, null, null,
                        contextVersion: hud.Snapshot.CommandContextVersion, destination: Hex(destination));
                    CombatActionPreviewSnapshot quote = encounter.PreviewAction(candidate);
                    Assert.IsTrue(quote.CanConfirm, quote.Reason);
                    int ap = spawned.Player.ActionPointsRemainingUnits;
                    encounter.ConfirmAction(quote.Candidate);
                    Assert.AreEqual(ap - quote.ActionPointCostUnits, spawned.Player.ActionPointsRemainingUnits);
                    Assert.AreEqual(destination, spawned.Player.Position);
                    yield return Ready(encounter, observer);
                    if (destination.Q == 2) yield return Capture("clearance-rank1-crossed-1366");
                }
                for (int turn = 0; turn < 3; turn++)
                {
                    encounter.RequestEndTurn(spawned.Player.Id);
                    yield return Ready(encounter, observer);
                }
                CollectionAssert.AreEquivalent(spawned.Enemies.Select(enemy => enemy.Id), rejectedActors);
                foreach (CombatUnitPresentationEventProjection movement in observer.Events.Where(value =>
                    value.ActorCombatantId != spawned.Player.Id && value.PresentationEvent == CombatUnitPresentationEvent.Move))
                    Assert.IsTrue(movement.MovementSteps.All(step => step.To.OccupiedCells.All(cell => cell.Q >= 2)));
                observer.AssertSettledPoses();
                yield return Capture("clearance-rank2-stopped-1366");
                File.WriteAllLines(Path.Combine(Evidence, "clearance-1366.txt"), new[] {
                    "source=WorldContentImporter.ParseEnvironmentProfiles -> EnvironmentProfileAsset.Apply -> CombatEntryAdapter",
                    "rank1=actual coordinator move crossed q=1/2", "rank2=rejected without AP/pose/projection changes: " + string.Join(",", rejectedActors) });
                adventure.RequestReturn();
                adventure.ConfirmWithdrawal();
                yield return SceneReady(GameplaySceneNames.Settlement);
            }
            finally { Object.Destroy(fixture); }
        }
#endif

        private IEnumerator Verify(string node, int width, int height)
        {
            GuanzhongCombatEquipmentPlayModeTests.SetGameSize(width, height);
            for (int frame = 0; frame < 60 && (Screen.width != width || Screen.height != height); frame++) yield return null;
            Assert.AreEqual(width, Screen.width); Assert.AreEqual(height, Screen.height);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            var adventure = Find<AdventureController>();
            for (int frame = 0; frame < 300 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            adventure.SetEncounterRandomSource(new EncounterRandom());
            Assert.IsTrue(Find<AdventureInputController>().SelectNode(node));
            var encounter = Find<EncounterCoordinator>();
            var session = Field<CombatSession>(encounter, "session");
            var spawned = Field<AdventureSpawnSet>(encounter, "spawned");
            var provider = Find<Static3DCombatUnitPresentationProvider>();
            var picker = Find<CombatTargetInput>();
            var hud = Find<CombatHudPresenter>();
            var environment = Field<EnvironmentProfileAsset>(adventure, "environmentProfile");
            var observer = new ObservedPresentation(provider, session, spawned.Player);
            Set(encounter, "unitPresentation", observer);
            encounter.SetCombatResolutionRandomSource(new GuanzhongBasicAttackPlayModeTests.OutcomeFixtureRandom(session, true));
            // Capture before yielding: a faster enemy may take the first scheduled turn.
            Assert.AreEqual(SpatialFootprintKind.Single, spawned.Player.Footprint);
            Assert.AreEqual(2, spawned.Enemies.Count);
            Assert.AreEqual(3, provider.ActiveCombatantCount);
            var occupied = new HashSet<HexCoord>();
            var initialPoses = session.Combatants.All.ToDictionary(actor => actor.Id, actor => actor.Pose);
            foreach (CombatantSnapshot actor in session.Combatants.All)
            {
                Assert.AreEqual(actor.Team == CombatTeam.Player ? 1 : 2, actor.OccupiedCells.Count);
                if (actor.Team != CombatTeam.Player)
                { Assert.AreEqual(SpatialFootprintKind.AdjacentPair, actor.Footprint); Assert.AreEqual(2, actor.Facing); }
                Assert.IsTrue(provider.TryGetPresentedPose(actor.Id, out var shown));
                Assert.AreEqual(Hex(actor.Position), shown.Position);
                foreach (HexCoord cell in actor.OccupiedCells)
                {
                    Assert.IsTrue(occupied.Add(cell), "Formal spawn footprints overlap at " + cell);
                    EnvironmentBattlefieldCell[] tiles = environment.battlefieldCells.Where(value => value.q == cell.Q && value.r == cell.R).ToArray();
                    Assert.AreEqual(1, tiles.Length, "Spawn body leaves the formal board: " + cell);
                    Assert.IsFalse(tiles[0].blocksGroundMove, "Spawn body intersects a formal rock: " + cell);
                }
            }
            yield return Ready(encounter, observer);
            Vector3 cameraPosition = Camera.main.transform.position;
            foreach (CombatantSnapshot actor in session.Combatants.All)
            {
                foreach (HexCoord cell in actor.OccupiedCells)
                {
                    Assert.AreEqual(actor.Id, picker.FindUnitAtCell(Hex(cell)), "Both cells must resolve the same unit ID.");
                    // Camera framing is fixture setup; selection still traverses the real pointer input owner.
                    Vector3 worldPoint = Static3DCombatUnitPresentationProvider.HexToWorld(Hex(cell));
                    Ray center = Camera.main.ViewportPointToRay(new Vector3(.5f, .5f));
                    Assert.IsTrue(new Plane(Vector3.up, worldPoint).Raycast(center, out float distance));
                    Camera.main.transform.position += worldPoint - center.GetPoint(distance);
                    yield return null;
                    Vector2 screenPoint = Camera.main.WorldToScreenPoint(worldPoint);
                    bool mapped = picker.TryGetCell(screenPoint, out var mappedCell);
                    picker.TryPick(screenPoint, out string pickedId);
                    string evidenceLabel = node + "-" + width + "-" + actor.Id + "-" + cell.Q + "-" + cell.R;
                    if (!mapped || !mappedCell.Equals(Hex(cell)) || pickedId != actor.Id)
                    {
                        string diagnostic = DescribeCellPick(actor, cell, screenPoint, picker, provider, session, out string nearestBody);
                        TestContext.WriteLine(diagnostic);
                        File.WriteAllText(Path.Combine(Evidence, evidenceLabel + "-center-blocked.txt"), diagnostic);
                        yield return Capture(evidenceLabel + "-center-blocked");
                        Assert.IsTrue(mapped, diagnostic);
                        Assert.AreEqual(Hex(cell), mappedCell, "A wrong ground mapping is a production error. " + diagnostic);
                        Assert.AreEqual(actor.Id, picker.FindUnitAtCell(mappedCell), diagnostic);
                        Assert.IsNotNull(pickedId, diagnostic);
                        Assert.AreNotEqual(actor.Id, pickedId, diagnostic);
                        Assert.AreEqual(pickedId, nearestBody,
                            "Only a nearer neighbour body may explain a different center pick. " + diagnostic);
                    }
                    Vector3 framedCameraPosition = Camera.main.transform.position;
                    bool found = TryFindVisibleCellPoint(actor.Id, Hex(cell), picker, provider, session, out screenPoint);
                    if (!found)
                    {
                        string diagnostic = DescribeCellPick(actor, cell, Camera.main.WorldToScreenPoint(worldPoint),
                            picker, provider, session, out _);
                        File.WriteAllText(Path.Combine(Evidence, evidenceLabel + "-no-visible-point.txt"), diagnostic);
                        yield return Capture(evidenceLabel + "-no-visible-point");
                        Assert.Fail("No visible, unblocked point remains inside the occupied hex. " + diagnostic);
                    }
                    Assert.AreEqual(framedCameraPosition, Camera.main.transform.position,
                        "Finding a visible ground point must not move the camera.");
                    Assert.IsTrue(picker.TryGetCell(screenPoint, out mappedCell));
                    Assert.AreEqual(Hex(cell), mappedCell);
                    Assert.IsTrue(picker.TryPick(screenPoint, out pickedId));
                    Assert.AreEqual(actor.Id, pickedId);
                    TestContext.WriteLine("cell-click actor=" + actor.Id + ";cell=" + cell + ";screen=" + screenPoint);
                    yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(screenPoint, true);
                    Assert.AreEqual(actor.Id, hud.InspectedTargetId, "Front and rear clicks inspect the same unit.");
                    Assert.AreEqual(actor.OccupiedCells.Count, Find<CombatMovementView>().InspectionRings.Count(ring => ring.gameObject.activeSelf));
                }
            }
            Camera.main.transform.position = cameraPosition;
            hud.ClearInspectedTarget();
            observer.AssertSettledPoses();
            yield return Capture(node + "-" + width + "-spawn");

            var discovery = new CombatLegalActionService();
            var policy = new LegalActionAI();
            int playerMoves = 0, playerAttacks = 0, decisions = 0;
            bool capturedContact = false;
            string decisionsPath = Path.Combine(Evidence, node + "-" + width + "-decisions.txt");
            File.WriteAllText(decisionsPath, "node=" + node + ";resolution=" + width + "x" + height +
                ";started=" + DateTime.UtcNow.ToString("O") + Environment.NewLine);
            while (encounter != null && encounter.IsRunning && decisions++ < 300)
            {
                yield return Ready(encounter, observer);
                if (encounter == null || !encounter.IsRunning) break;
                var admitted = discovery.GetLegalActions(session, spawned.Player.Id)
                    .Where(command => command.Kind is CombatCommandKind.BasicAttack or CombatCommandKind.Move or CombatCommandKind.EndTurn).ToArray();
                CombatCommand command = policy.ChooseAction(session, admitted);
                File.AppendAllText(decisionsPath,
                    "decision=" + decisions + ";turn=" + session.TurnSequence + ";tick=" + session.TurnScheduler.CurrentTick +
                    ";currentActor=" + session.CurrentActorId + ";phase=" + session.TurnPhase +
                    ";presenting=" + encounter.IsPresenting + ";admittedBasic=" + admitted.Count(value => value.Kind == CombatCommandKind.BasicAttack) +
                    ";admittedMove=" + admitted.Count(value => value.Kind == CombatCommandKind.Move) +
                    ";chosen=" + (command == null ? "<none>" : command.Kind + ":actor=" + command.ActorId +
                        ":target=" + command.TargetId + ":profile=" + command.ProfileId + ":destination=" + command.Destination +
                        ":facing=" + command.DestinationFacing + ":direction=" + command.AttackDirection) +
                    ";deferAttackForEnemyObservation=" + (command != null && command.Kind == CombatCommandKind.BasicAttack &&
                        spawned.Enemies.Any(enemy => !observer.OffensiveIds.Contains(enemy.Id))) +
                    ";offensiveIds=" + string.Join(",", observer.OffensiveIds.OrderBy(value => value, StringComparer.Ordinal)) + Environment.NewLine +
                    string.Join(Environment.NewLine, session.Combatants.All.Select(unit =>
                        "unit=" + unit.Id + ";anchor=" + unit.Position + ";facing=" + unit.Facing +
                        ";footprint=" + unit.Footprint + ";rank=" + unit.BodyScaleRank +
                        ";cells=" + string.Join("/", unit.OccupiedCells) + ";hp=" + unit.CurrentHealth + "/" + unit.MaximumHealth +
                        ";ap=" + unit.ActionPointsRemainingUnits + "/" + unit.ActionPointMaximumUnits)) + Environment.NewLine);
                Assert.IsNotNull(command, "The production player must be able to act or end the turn.");
                // Let both production enemy policies demonstrate an offensive action before lethal player attacks.
                // Deterministic rolls keep the observation reproducible without injecting health or stats.
                if (command.Kind == CombatCommandKind.EndTurn || command.Kind == CombatCommandKind.BasicAttack &&
                    spawned.Enemies.Any(enemy => !observer.OffensiveIds.Contains(enemy.Id)))
                {
                    encounter.RequestEndTurn(spawned.Player.Id);
                    yield return null;
                    continue;
                }
                var candidate = new CombatActionCandidate(command.Kind == CombatCommandKind.Move ? CombatActionKind.Move : CombatActionKind.BasicAttack,
                    command.ActorId, command.TargetId, command.ProfileId, hud.Snapshot.CommandContextVersion,
                    destination: command.Destination.HasValue ? Hex(command.Destination.Value) : (CombatUnitPresentationHex?)null,
                    attackDirection: command.AttackDirection, destinationFacing: command.DestinationFacing);
                CombatActionPreviewSnapshot quote = encounter.PreviewAction(candidate);
                Assert.IsTrue(quote.CanConfirm, quote.Reason);
                int ap = spawned.Player.ActionPointsRemainingUnits;
                HexCoord start = spawned.Player.Position;
                encounter.ConfirmAction(quote.Candidate);
                Assert.AreEqual(spawned.Enemies.All(enemy => !enemy.IsAlive) ? 0 : ap - quote.ActionPointCostUnits, spawned.Player.ActionPointsRemainingUnits,
                    "Quoted AP is paid once for the committed action.");
                if (command.Kind == CombatCommandKind.Move)
                {
                    playerMoves++;
                    Assert.AreNotEqual(start, spawned.Player.Position);
                    Assert.AreEqual(command.Destination.Value, spawned.Player.Position);
                    var movement = observer.Events.Last(value => value.ActorCombatantId == "player" && value.PresentationEvent == CombatUnitPresentationEvent.Move);
                    CollectionAssert.AreEqual(quote.Candidate.MovementSteps, movement.MovementSteps);
                }
                else playerAttacks++;
                yield return Ready(encounter, observer);
                if (encounter != null && encounter.IsRunning && playerAttacks > 0 && !capturedContact)
                {
                    capturedContact = true;
                    yield return Capture(node + "-" + width + "-contact");
                }
            }
            Assert.Less(decisions, 301, "Formal combat did not complete within its explicit command budget.");
            Assert.Greater(playerMoves, 0);
            Assert.Greater(playerAttacks, 0);
            Assert.IsTrue(spawned.Enemies.All(enemy => !enemy.IsAlive), "Both production enemies must die through actual attacks.");
            CollectionAssert.AreEquivalent(spawned.Enemies.Select(enemy => enemy.Id), observer.Deaths);
            Assert.IsTrue(observer.Events.Any(value => value.ActorCombatantId != "player" && value.PresentationEvent == CombatUnitPresentationEvent.Move));
            CollectionAssert.AreEquivalent(spawned.Enemies.Select(enemy => enemy.Id), observer.OffensiveIds);
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.AreEqual("guanzhong_city", GameBootstrap.RequireRuntime().Navigation.SettlementId);
            Assert.IsNull(GameBootstrap.RequireRuntime().Navigation.AdventureId);
            File.WriteAllLines(Path.Combine(Evidence, node + "-" + width + ".txt"), observer.Events.Select(value =>
                value.ActorCombatantId + ":" + value.PresentationEvent + ":" + value.EndPosition.Q + "," + value.EndPosition.R +
                ":facing=" + value.Facing + ":steps=" + value.MovementSteps.Count).Concat(initialPoses.Select(value =>
                    "spawn=" + value.Key + ":" + value.Value.Anchor + ":facing=" + value.Value.Facing + ":cells=" + string.Join("/", value.Value.OccupiedCells))).Concat(new[] {
                "playerMoves=" + playerMoves + ";playerAttacks=" + playerAttacks + ";deaths=" + string.Join(",", observer.Deaths),
                "settlement=" + SceneManager.GetActiveScene().name + ";resolution=" + width + "x" + height }));
        }

        private static bool TryFindVisibleCellPoint(string actorId, CombatUnitPresentationHex cell,
            CombatTargetInput picker, Static3DCombatUnitPresentationProvider provider, CombatSession session, out Vector2 point)
        {
            point = default;
            Vector3 center = Static3DCombatUnitPresentationProvider.HexToWorld(cell);
            var candidates = new List<(Vector2 Screen, bool HitsBody, float Offset)>();
            var uiHits = new List<RaycastResult>();
            // Fixed ground samples, strictly inside the target hex; no camera or world-state mutation.
            for (int x = -5; x <= 5; x++)
            for (int z = -5; z <= 5; z++)
            {
                Vector3 offset = new Vector3(x * .09f, 0f, z * .1f);
                if (Mathf.Abs(.5f * offset.x + .8660254f * offset.z) >= .49f ||
                    Mathf.Abs(.5f * offset.x - .8660254f * offset.z) >= .49f) continue;
                Vector3 projected = Camera.main.WorldToScreenPoint(center + offset);
                if (projected.z <= 0f || projected.x < 4f || projected.x > Screen.width - 4f ||
                    projected.y < 4f || projected.y > Screen.height - 4f) continue;
                Vector2 screen = projected;
                if (!picker.TryGetCell(screen, out var mapped) || !mapped.Equals(cell) ||
                    !picker.TryPick(screen, out string picked) || picked != actorId) continue;
                uiHits.Clear();
                if (EventSystem.current != null)
                    EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, uiHits);
                if (uiHits.Count != 0) continue;
                Ray ray = Camera.main.ScreenPointToRay(screen);
                bool hitsBody = session.Combatants.All.Any(unit => unit.IsAlive &&
                    provider.TryGetVisibleBounds(unit.Id, out Bounds bounds) && bounds.IntersectRay(ray));
                candidates.Add((screen, hitsBody, offset.sqrMagnitude));
            }
            if (candidates.Count == 0) return false;
            // Prefer exposed ground; a ray through this unit's own body still selects the same occupied hex.
            point = candidates.OrderBy(candidate => candidate.HitsBody).ThenBy(candidate => candidate.Offset).First().Screen;
            return true;
        }

        private static string DescribeCellPick(CombatantSnapshot actor, HexCoord cell, Vector2 point,
            CombatTargetInput picker, Static3DCombatUnitPresentationProvider provider, CombatSession session, out string nearestBody)
        {
            bool mapped = picker.TryGetCell(point, out var groundCell);
            picker.TryPick(point, out string picked);
            var lines = new List<string> { "actor=" + actor.Id + ";cell=" + cell + ";screen=" + point +
                ";mapped=" + mapped + ":" + groundCell.Q + "," + groundCell.R + ";picked=" + picked +
                ";cellOwner=" + picker.FindUnitAtCell(Hex(cell)), "camera=" + Camera.main.transform.position +
                ";rotation=" + Camera.main.transform.eulerAngles + ";orthographicSize=" + Camera.main.orthographicSize };
            Ray ray = Camera.main.ScreenPointToRay(point);
            float closest = float.PositiveInfinity;
            nearestBody = null;
            foreach (CombatantSnapshot unit in session.Combatants.All.Where(value => value.IsAlive))
            {
                Assert.IsTrue(provider.TryGetPresentedPose(unit.Id, out var shown));
                Assert.IsTrue(provider.TryGetVisibleBounds(unit.Id, out Bounds bounds));
                bool intersects = bounds.IntersectRay(ray, out float distance);
                if (intersects && distance < closest) { closest = distance; nearestBody = unit.Id; }
                lines.Add(unit.Id + ";kernel=" + unit.Position + "/" + unit.Facing + "/" + unit.Footprint +
                    ";cells=" + string.Join("/", unit.OccupiedCells) + ";presented=" + shown.Position.Q + "," + shown.Position.R +
                    "/" + shown.Facing + "/double=" + shown.IsDoubleCell + ";bounds=" + bounds + ";rayHit=" + intersects +
                    ";distance=" + (intersects ? distance.ToString("F5") : "none"));
            }
            lines.Add("nearestBody=" + nearestBody + ";ray=" + ray);
            return string.Join(Environment.NewLine, lines);
        }

        private static IEnumerator Ready(EncounterCoordinator encounter, ObservedPresentation observer)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            do
            {
                yield return null;
                observer.AssertSettledPoses();
                if (encounter == null || !encounter.IsRunning || encounter.AcceptsPlayerCommand && !encounter.IsPresenting) yield break;
            } while (Time.realtimeSinceStartup < deadline);
            Assert.Fail("Formal coordinator failed to complete presentation and return control.");
        }

        private static IEnumerator Capture(string label)
        {
            Canvas.ForceUpdateCanvases();
            for (int frame = 0; frame < 8; frame++) yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(Evidence, label + ".png"));
            for (int frame = 0; frame < 8; frame++) yield return null;
        }

        private static IEnumerator SceneReady(string name)
        {
            for (int frame = 0; frame < 300 && SceneManager.GetActiveScene().name != name; frame++) yield return null;
            Assert.AreEqual(name, SceneManager.GetActiveScene().name);
            yield return null;
        }
        private static CombatUnitPresentationHex Hex(HexCoord cell) => new CombatUnitPresentationHex(cell.Q, cell.R);
        private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();
        private sealed class EncounterRandom : IFormalEncounterRandomSource { public int NextPercent() => 0; }

        private sealed class ObservedPresentation : ICombatUnitPresentationPort, ICombatPresentationActivity
        {
            private readonly Static3DCombatUnitPresentationProvider provider;
            private readonly CombatSession session;
            private readonly CombatantSnapshot player;
            public readonly List<CombatUnitPresentationEventProjection> Events = new List<CombatUnitPresentationEventProjection>();
            public readonly HashSet<string> OffensiveIds = new HashSet<string>();
            public readonly List<string> Deaths = new List<string>();
            public Action OnFrame;
            public ObservedPresentation(Static3DCombatUnitPresentationProvider provider, CombatSession session, CombatantSnapshot player)
            { this.provider = provider; this.session = session; this.player = player; }
            public bool IsPresenting => provider != null && provider.IsPresenting;
            public void Prepare(IReadOnlyList<CombatUnitPresentationDescriptor> values) => provider.Prepare(values);
            public void Spawn(CombatUnitPresentationDescriptor value) => provider.Spawn(value);
            public void Remove(string id) => provider.Remove(id);
            public void Clear() => provider.Clear();
            public void Present(CombatUnitPresentationEventProjection value)
            {
                Events.Add(value);
                provider.Present(value);
                if (value.ActorCombatantId != "player" && value.PresentationEvent is CombatUnitPresentationEvent.Attack or CombatUnitPresentationEvent.Cast)
                {
                    OffensiveIds.Add(value.ActorCombatantId);
                    Assert.IsTrue(session.Combatants.TryGet(value.ActorCombatantId, out var actor));
                    Assert.IsTrue(provider.TryGetPresentedPose(actor.Id, out var shown));
                    Assert.AreEqual(actor.Facing, shown.Facing, "An enemy offensive action must preserve its authoritative body facing.");
                    Assert.IsTrue(session.TryGetProfile(value.ProfileId, out var profile));
                    Assert.IsTrue(session.SpatialQuery.QueryRange(actor.Pose, player.Pose, profile.MinimumRange, profile.MaximumRange).IsInRange,
                        "Both enemy policies must attack from their actual closest contact distance.");
                }
                if (value.PresentationEvent != CombatUnitPresentationEvent.Death) return;
                Assert.IsFalse(Deaths.Contains(value.ActorCombatantId), "A two-cell enemy must die exactly once.");
                Deaths.Add(value.ActorCombatantId);
                Assert.IsTrue(session.Combatants.TryGet(value.ActorCombatantId, out var dead));
                var occupied = (IReadOnlyCollection<HexCoord>)typeof(CombatSession).GetMethod("GetOccupiedPositionsExcept",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(session, new object[] { player.Id });
                foreach (HexCoord cell in dead.OccupiedCells)
                {
                    Assert.IsFalse(occupied.Contains(cell), "Death must release both cells from the real combat occupancy query.");
                    Assert.IsTrue(session.SpatialQuery.QueryMovement(player.Pose, cell, int.MaxValue / 4,
                        player.MovementEfficiencyMilliUnits, occupied).IsReachable, "The player's full-body path may enter the released cell: " + cell);
                }
            }
            public void AssertSettledPoses()
            {
                OnFrame?.Invoke();
                if (provider == null || provider.IsPresenting || provider.ActiveCombatantCount == 0) return;
                var occupied = new HashSet<HexCoord>();
                foreach (CombatantSnapshot actor in session.Combatants.All.Where(unit => unit.IsAlive))
                {
                    Assert.IsTrue(provider.TryGetPresentedPose(actor.Id, out var shown));
                    Assert.AreEqual(Hex(actor.Position), shown.Position);
                    Assert.AreEqual(actor.Footprint == SpatialFootprintKind.AdjacentPair, shown.IsDoubleCell);
                    if (shown.IsDoubleCell) Assert.AreEqual(actor.Facing, shown.Facing);
                    Assert.IsTrue(provider.TryGetPresentedUnit(actor.Id, out var root));
                    Assert.Less(Vector3.Distance(root.transform.position, Static3DCombatUnitPresentationProvider.PoseToWorld(shown)), .001f);
                    foreach (HexCoord cell in actor.OccupiedCells) Assert.IsTrue(occupied.Add(cell), "Settled footprints overlap at " + cell);
                }
            }
        }
    }
}
