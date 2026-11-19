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
using TianZhang.Spatial;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed partial class GuanzhongCombatTargetSelectionPlayModeTests
    {
        private string saveDirectory;
        private Mouse mouse;
        private Keyboard keyboard;
        private InputSettings.BackgroundBehavior background;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        private CombatHudPresenter presenter;
        private CombatTargetView target;
        private CombatTimelineView timeline;
        private Static3DCombatUnitPresentationProvider provider;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private AdventureSpawnSet spawned;

        [UnitySetUp]
        public IEnumerator EnterFormalEncounter()
        {
            background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            GameBootstrap stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return SceneReady(GameplaySceneNames.StartMenu);
            saveDirectory = Path.Combine(Application.temporaryCachePath, "TianZhang-Target-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saveDirectory));
            CharacterCreationController creation = Find<CharacterCreationController>();
            Get<Button>(Find<StartMenuView>(), "newPlayerButton").onClick.Invoke();
            Assert.AreEqual("origin_loose", creation.Draft.OriginId);
            CharacterCreationView creationView = Find<CharacterCreationView>();
            creationView.GetOriginButton("origin_minor_clan").onClick.Invoke();
            Assert.AreEqual("origin_minor_clan", creation.Draft.OriginId);
            Assert.That(new[] { creation.Draft.Innate.RootBone, creation.Draft.Innate.Soul, creation.Draft.Innate.DivineSense,
                creation.Draft.Innate.Aptitude, creation.Draft.Innate.Fortune }, Is.EqualTo(new[] { 8, 8, 8, 8, 8 }));
            Get<InputField>(creationView, "slotIdInput").text = "formal-target";
            Get<InputField>(creationView, "characterNameInput").text = "目标联动验证";
            Get<Button>(creationView, "createButton").onClick.Invoke();
            yield return SceneReady(GameplaySceneNames.World);
            Assert.AreEqual("练气", GameBootstrap.RequireRuntime().Player.Progression.RealmStage);
            Assert.AreEqual("basic_iron_sword", GameBootstrap.RequireRuntime().Player.MainEquipmentBasicAttackProfileId);
            WorldMapController world = Find<WorldMapController>();
            Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            yield return EnterAdventure();
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard = InputSystem.AddDevice<Keyboard>();
        }

        private IEnumerator EnterAdventure()
        {
            SettlementController settlement = Find<SettlementController>();
            BountyBoardView board = Find<BountyBoardView>();
            board.Show(Get<ContentCatalogData>(settlement, "contentCatalog"), "guanzhong_city", GameBootstrap.RequireRuntime().Bounties);
            board.SubmitAccept("bounty_guanzhong_shijiahou");
            Assert.IsTrue(settlement.EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            AdventureController adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            presenter = Find<CombatHudPresenter>();
            target = Find<CombatTargetView>();
            timeline = Find<CombatTimelineView>();
            provider = Find<Static3DCombatUnitPresentationProvider>();
            Assert.IsNull(presenter.InspectedTargetId);
            adventure.SetEncounterRandomSource(new EncounterRandom());
            encounter = Find<EncounterCoordinator>();
            encounter.SetCombatResolutionRandomSource(new CombatRandom());
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            session = Get<CombatSession>(encounter, "session");
            spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            for (float deadline = Time.realtimeSinceStartup + 10f;
                !presenter.CanInspect && Time.realtimeSinceStartup < deadline;) yield return null;
            Assert.IsTrue(presenter.CanInspect);
            // Movement and target inspection are range-independent; this case must not block on melee contact.
            if (TestContext.CurrentContext.Test.Name.StartsWith("ActionPointMovement", StringComparison.Ordinal) ||
                TestContext.CurrentContext.Test.Name.StartsWith("G2Empty", StringComparison.Ordinal)) yield break;
            yield return GuanzhongBasicAttackPlayModeTests.WaitForMeleeContact(encounter, session);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            InputSystem.settings.backgroundBehavior = background;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
#endif
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return null;
            if (saveDirectory != null && Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
        }

        [UnityTest]
        public IEnumerator GuanshijiEntersPointerSelectionWithoutAnAutomaticTarget()
        {
            var input = Find<CombatCommandInput>();
            var bar = Find<CombatActionBarView>();
            int ap = spawned.Player.ActionPointsRemainingUnits, mp = spawned.Player.CurrentSpirit;
            bar.ArtButton.onClick.Invoke();
            TestContext.WriteLine("Before pointer: selecting=" + input.IsSelectingAttack +
                "; target=" + input.Candidate?.TargetId + "; legal=" + input.Preview?.CanConfirm +
                "; enemies=" + string.Join("/", spawned.Enemies.Select(enemy => enemy.Id + "@" + enemy.Position)));
            Assert.IsTrue(input.IsSelectingAttack, "Guanshiji must enter the existing pointer selection chain.");
            Assert.IsNull(input.Candidate.TargetId);
            Assert.IsFalse(input.Preview.CanConfirm);
            StringAssert.Contains("50 AP · 12 MP", Get<Text>(Find<CombatActionPreviewView>(), "compactQuote").text);
            input.ConfirmCandidate();
            Assert.AreEqual(ap, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(mp, spawned.Player.CurrentSpirit);
            yield return PointArt(session, spawned.Enemy.Id, click: true);
            AssertAffectedTarget(input, spawned.Enemy.Id);
            Assert.IsTrue(input.IsTargetLocked);
            Assert.AreEqual(50000, input.Preview.ActionPointCostUnits);
            Assert.AreEqual(12, input.Preview.SpiritCost);
            RefreshAuthority();
            Assert.IsNull(input.Candidate);
            var view = Find<CombatActionPreviewView>();
            StringAssert.Contains("战况已变", Get<Text>(view, "actionHint").text);
            Assert.IsFalse(view.ConfirmButton.gameObject.activeSelf);
            input.ConfirmCandidate();
            Assert.AreEqual(ap, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(mp, spawned.Player.CurrentSpirit);
            bar.ArtButton.onClick.Invoke();
            yield return PointArt(session, spawned.Enemy.Id, click: true);
            yield return GuanzhongBasicAttackPlayModeTests.ConfirmLockedTarget();
            Assert.AreEqual(ap - 50000, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(mp - 12, spawned.Player.CurrentSpirit);
        }

        [UnityTest]
        public IEnumerator Q1LegalAlternativeDirectionAndTargetKeepEntryAvailable()
        {
            var input = Find<CombatCommandInput>();
            var bar = Find<CombatActionBarView>();
            var service = new CombatCommandService();
            // Reverse enumeration only; live positions, IDs and their presented units stay aligned.
            // Species data stays aligned with the reversed enemy list, as production guarantees.
            EnemyData firstData = spawned.EnemyDataList[0], secondData = spawned.EnemyDataList[1];
            spawned = new AdventureSpawnSet(spawned.Player, spawned.Enemies[1], secondData,
                spawned.PlayerBasicProfileId, spawned.EnemyBasicProfileId, spawned.PlayerDivineProfileIds,
                new[] { spawned.Enemy }, additionalEnemyData: new[] { firstData });
            Set(encounter, "spawned", spawned);
            spawned.Player.Facing = Enumerable.Range(0, 6).First(direction => session.QueryAttackArea("player",
                spawned.Player.BasicAttackProfileId, direction).TargetIds.Count == 0);
            typeof(EncounterCoordinator).GetMethod("Present", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(encounter, new object[] { "合法替代候选验证", true, null });
            Assert.IsTrue(presenter.Snapshot.FindAction(CombatActionKind.BasicAttack).CanOpen,
                "A facing with no targets must still allow opening a tile attack.");
            var firstArt = presenter.Snapshot.ArtActions[0];
            string artDiagnostic = string.Join("; ", firstArt.Targets.Select(t =>
            {
                session.Combatants.TryGet(t.Id, out CombatantSnapshot unit);
                return t.Id + "/" + t.DisplayName + "@" + unit?.Position + "=" + t.CanExecute + ":" + t.RejectionReason;
            }));
            // The formal contact keeps one species in range and one out of range; enumeration order
            // follows the session, so assert the offered alternatives rather than a fixed index.
            Assert.IsTrue(firstArt.Targets.Any(t => t.CanExecute), artDiagnostic);
            Assert.IsTrue(firstArt.Targets.Any(t => !t.CanExecute), artDiagnostic);
            string legalArtTarget = firstArt.Targets.First(t => t.CanExecute).Id;
            Assert.IsTrue(bar.BasicAttackButton.interactable);
            Assert.IsTrue(bar.ArtButton.interactable);
            int ap = spawned.Player.ActionPointsRemainingUnits, mp = spawned.Player.CurrentSpirit;
            bar.BasicAttackButton.onClick.Invoke();
            yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session,
                presenter.Snapshot.FindPreferredAction(CombatActionKind.BasicAttack).Candidate.AttackDirection);
            Assert.IsTrue(input.Preview.CanConfirm);
            input.CancelCandidate();
            bar.ArtButton.onClick.Invoke();
            yield return PointArt(session, legalArtTarget);
            Assert.IsTrue(input.Preview.CanConfirm);
            AssertAffectedTarget(input, legalArtTarget);
            input.CancelCandidate();
            Assert.AreEqual(ap, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(mp, spawned.Player.CurrentSpirit);
        }

        public static IEnumerator PointArt(CombatSession combat, string targetId, bool click = false)
        {
            Assert.IsTrue(combat.Combatants.TryGet(targetId, out var actor));
            var input = Find<CombatCommandInput>();
            var service = new CombatCommandService();
            HexCoord cell = actor.OccupiedCells.OrderByDescending(value => service.Preview(combat,
                new CombatCommand(CombatCommandKind.Art, "player", profileId: input.Candidate.ProfileId,
                    targetCell: value)).IsLegal).First();
            Assert.IsTrue(TryFindCastingPoint(cell, out var point), "No visible casting point for " + targetId + " at " + cell);
            yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(point, click);
        }

        public static bool TryFindCastingPoint(HexCoord cell, out Vector2 point, bool? requireBody = null)
        {
            point = default;
            var movement = Find<CombatMovementView>();
            var units = Find<Static3DCombatUnitPresentationProvider>();
            var wanted = new CombatUnitPresentationHex(cell.Q, cell.R);
            Vector3 center = Static3DCombatUnitPresentationProvider.HexToWorld(wanted);
            // Test screen coordinates against the real body and ground owners. Never substitute
            // a unit anchor when the cursor actually maps to a different occupied cell.
            var samples = new List<Vector3>();
            if (requireBody == true)
            {
                var targetInput = Find<CombatTargetInput>();
                string id = targetInput.FindUnitAtCell(wanted);
                if (id == null || !units.TryGetVisibleBounds(id, out Bounds bounds)) return false;
                for (int x = -10; x <= 10; x++)
                for (int y = -10; y <= 10; y++)
                    samples.Add(bounds.center + Camera.main.transform.right * (x * bounds.size.magnitude / 24f) +
                        Camera.main.transform.up * (y * bounds.size.magnitude / 24f));
            }
            else
            {
                samples.Add(center);
                for (int x = -5; x <= 5; x++)
                for (int z = -5; z <= 5; z++)
                    samples.Add(center + new Vector3(x * .09f, 0f, z * .09f));
            }
            foreach (Vector3 sample in samples)
            {
                Vector3 screen = Camera.main.WorldToScreenPoint(sample);
                if (screen.z <= 0 || screen.x < 2 || screen.y < 2 || screen.x >= Screen.width - 2 || screen.y >= Screen.height - 2 ||
                    movement.UiOwnsPointer(screen)) continue;
                bool hitBody = units.TryPickBodyPoint(Camera.main.ScreenPointToRay(screen), out Vector3 hit);
                if (requireBody.HasValue && hitBody != requireBody.Value) continue;
                bool mapped = hitBody ? movement.TryGetCellAtWorld(hit, out var mappedCell) :
                    movement.TryPickCell(screen, out mappedCell);
                if (!mapped || !mappedCell.Equals(wanted)) continue;
                point = screen;
                return true;
            }
            return false;
        }

        internal static void AssertAffectedTarget(CombatCommandInput input, string id)
        {
            Assert.IsTrue(string.IsNullOrEmpty(input.Candidate.TargetId), "The tile command must retain a cell, not collapse to a unit ID.");
            Assert.IsTrue(input.Candidate.TargetCell.HasValue);
            CollectionAssert.AreEqual(new[] { id }, input.Preview.AttackArea.Targets.Select(unit => unit.Id));
        }

        [UnityTest]
        public IEnumerator GuanshijiPointerInvalidatesHoverAndClearsOnCancelMovementPresentationAndExit()
        {
            var input = Find<CombatCommandInput>();
            var bar = Find<CombatActionBarView>();
            var preview = Find<CombatActionPreviewView>();
            var movement = Find<CombatMovementView>();
            int ap = spawned.Player.ActionPointsRemainingUnits, mp = spawned.Player.CurrentSpirit;
            int hp = spawned.Enemy.CurrentHealth, tick = session.TurnScheduler.CurrentTick;
            string inspected = presenter.InspectedTargetId;
            foreach (int width in new[] { 1920, 1366 })
            {
                GuanzhongCombatActionPreviewPlayModeTests.SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                bar.ArtButton.onClick.Invoke();
                yield return PointArt(session, spawned.Enemy.Id, false);
                Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
                Assert.IsFalse(preview.ConfirmButton.interactable);
                input.ConfirmCandidate();
                foreach (Vector2 rejected in new[]
                {
                    CellPoint(spawned.Enemies[1].Position), CellPoint(spawned.Player.Position),
                    new Vector2(-50, -50),
                    RectTransformUtility.WorldToScreenPoint(null, Get<Text>(preview, "summary").transform.position)
                })
                {
                    yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(rejected, true);
                    Assert.IsTrue(input.IsSelectingAttack);
                    Assert.IsFalse(input.Preview.CanConfirm, input.Preview.Reason);
                    Assert.AreNotEqual(spawned.Enemy.Id, input.Candidate.TargetId);
                    Assert.IsFalse(preview.ConfirmButton.interactable);
                    AssertSpellHighlights(session);
                    input.ConfirmCandidate();
                    yield return PointArt(session, spawned.Enemy.Id, false);
                }
                yield return PointArt(session, spawned.Enemy.Id);
                Assert.IsTrue(input.IsSelectingAttack);
                Assert.AreEqual(inspected, presenter.InspectedTargetId);
                Assert.IsFalse(preview.ConfirmButton.gameObject.activeSelf);
                yield return CaptureArtEvidence("q3-first-" + width, session);
                Vector2 confirm = RectTransformUtility.WorldToScreenPoint(null, Get<Text>(preview, "summary").transform.position);
                yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(confirm);
                Assert.IsFalse(input.Preview.CanConfirm, "Moving onto UI clears the hover quote.");
                InputSystem.QueueStateEvent(mouse, new MouseState { position = confirm }.WithButton(MouseButton.Right));
                yield return null; yield return null;
                Assert.IsNull(input.Candidate);
                InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null;
                bar.ArtButton.onClick.Invoke();
                yield return PointArt(session, spawned.Enemy.Id);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
                yield return null; yield return null;
                Assert.IsNull(input.Candidate);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            }
            Assert.AreEqual(ap, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(mp, spawned.Player.CurrentSpirit);
            Assert.AreEqual(hp, spawned.Enemy.CurrentHealth);
            Assert.AreEqual(tick, session.TurnScheduler.CurrentTick);
            bar.ArtButton.onClick.Invoke();
            yield return PointArt(session, spawned.Enemy.Id);
            bar.MoveButton.onClick.Invoke();
            Assert.IsNull(input.Candidate);
            Assert.IsTrue(movement.IsSelecting);
            bar.ArtButton.onClick.Invoke();
            Assert.IsFalse(movement.IsSelecting);
            Assert.IsNull(input.Candidate.TargetId);
            bar.GuardButton.onClick.Invoke();
            Assert.IsFalse(input.IsSelectingAttack);
            Assert.AreEqual(CombatActionKind.Guard, input.Candidate.Kind);
            Assert.IsTrue(preview.ConfirmButton.gameObject.activeSelf);
            input.CancelCandidate();
            bar.ArtButton.onClick.Invoke();
            yield return PointArt(session, spawned.Enemy.Id);
            int cost = input.Preview.ActionPointCostUnits, mana = input.Preview.SpiritCost;
            yield return GuanzhongBasicAttackPlayModeTests.ConfirmPointedAttack();
            input.ConfirmCandidate();
            Assert.AreEqual(ap - cost, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(mp - mana, spawned.Player.CurrentSpirit);
            Assert.Less(spawned.Enemy.CurrentHealth, hp);
            Assert.IsTrue(encounter.IsPresenting);
            Assert.IsNull(input.Candidate);
            Assert.IsFalse(movement.Highlights.Values.Any(ring => ring.gameObject.activeSelf));
            yield return CommandsReady();
            bar.ArtButton.onClick.Invoke();
            yield return PointArt(session, spawned.Enemy.Id);
            presenter.Hide();
            Assert.IsNull(input.Candidate);
            Assert.IsFalse(input.IsSelectingAttack);
            Assert.IsFalse(movement.Highlights.Values.Any(ring => ring.gameObject.activeSelf));
        }

        [UnityTest]
        public IEnumerator GuanshijiSelectsEitherEnemyAfterRealMovementAndIgnoresEnumerationAndDeath()
        {
            var input = Find<CombatCommandInput>();
            var bar = Find<CombatActionBarView>();
            // Use the formal reachable/quoted route to stand beside both existing enemies.
            CombatMovementOption option = presenter.Snapshot.Movement.Options.First(move => spawned.Enemies.All(enemy =>
                session.SpatialQuery.QueryRange(
                    spawned.Player.Pose.WithAnchor(new HexCoord(move.Destination.Q, move.Destination.R)), enemy.Pose, 1, 1).IsInRange));
            bar.ArtButton.onClick.Invoke();
            yield return PointArt(session, spawned.Enemy.Id);
            encounter.RequestMove(spawned.Player.Id, option.Destination.Q, option.Destination.R);
            Assert.IsNull(input.Candidate);
            yield return CommandsReady();
            Assert.AreEqual(spawned.Player.Position.Q, presenter.Snapshot.Movement.Origin.Q);
            Assert.AreEqual(spawned.Player.Position.R, presenter.Snapshot.Movement.Origin.R);
            encounter.RequestEndTurn(spawned.Player.Id);
            yield return null;
            yield return CommandsReady();
            Assert.IsTrue(presenter.Snapshot.ArtActions[0].Targets.All(value => value.CanExecute));
            // Reverse only enumeration, keeping live authority, stable IDs, positions and resources.
            // Species data follows the reversed enemy list so the presentation stays aligned.
            EnemyData firstData = spawned.EnemyDataList[0], secondData = spawned.EnemyDataList[1];
            spawned = new AdventureSpawnSet(spawned.Player, spawned.Enemies[1], secondData,
                spawned.PlayerBasicProfileId, spawned.EnemyBasicProfileId, spawned.PlayerDivineProfileIds,
                new[] { spawned.Enemies[0] }, additionalEnemyData: new[] { firstData });
            Set(encounter, "spawned", spawned);
            RefreshAuthority();
            foreach (var enemy in spawned.Enemies)
            {
                bar.ArtButton.onClick.Invoke();
                yield return PointArt(session, enemy.Id, false);
                AssertAffectedTarget(input, enemy.Id);
                Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
                yield return PointArt(session, enemy.Id);
                Assert.IsTrue(input.IsSelectingAttack);
                input.CancelCandidate();
            }
            // Controlled death input: killing the first entry must not replace the chosen second ID.
            bar.ArtButton.onClick.Invoke();
            yield return PointArt(session, spawned.Enemy.Id);
            spawned.Enemy.ReceiveDamage(spawned.Enemy.CurrentHealth);
            RefreshAuthority();
            Assert.IsNull(input.Candidate);
            bar.ArtButton.onClick.Invoke();
            yield return PointArt(session, spawned.Enemy.Id, false);
            Assert.IsTrue(input.Preview.CanConfirm, "A dead unit leaves a legal empty cell.");
            Assert.IsEmpty(input.Preview.AttackArea.Targets);
            var chosen = spawned.Enemies[1];
            yield return PointArt(session, chosen.Id);
            AssertAffectedTarget(input, chosen.Id);
            Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
            int hp = chosen.CurrentHealth, mp = spawned.Player.CurrentSpirit, ap = spawned.Player.ActionPointsRemainingUnits;
            int cost = input.Preview.ActionPointCostUnits, mana = input.Preview.SpiritCost;
            yield return GuanzhongBasicAttackPlayModeTests.ConfirmPointedAttack();
            input.ConfirmCandidate();
            Assert.Less(chosen.CurrentHealth, hp);
            Assert.AreEqual(0, spawned.Enemy.CurrentHealth);
            Assert.AreEqual(ap - cost, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(mp - mana, spawned.Player.CurrentSpirit);
        }

        [UnityTest]
        public IEnumerator G2EmptyCastsAt1920() => VerifyG2EmptyCasts(1920, 1080);

        [UnityTest]
        public IEnumerator G2EmptyCastsAt1366() => VerifyG2EmptyCasts(1366, 768);

        private IEnumerator VerifyG2EmptyCasts(int width, int height)
        {
            GuanzhongCombatActionPreviewPlayModeTests.SetGameSize(width, height);
            for (int frame = 0; frame < 8; frame++) yield return null;
            yield return CommandsReady();
            var input = Find<CombatCommandInput>();
            var bar = Find<CombatActionBarView>();
            var view = Find<CombatActionPreviewView>();
            var random = new G2CountingRandom();
            encounter.SetCombatResolutionRandomSource(random);
            Assert.IsTrue(presenter.Snapshot.ArtActions[2].Targets.All(target => !target.CanExecute),
                "This evidence starts before movement, with every enemy outside the five-cell range.");
            Assert.IsTrue(bar.BasicAttackButton.interactable);
            Assert.IsTrue(bar.ArtButton.interactable);
            Assert.IsTrue(bar.ThirdArtButton.interactable);

            // Inspect and cancel with the real mouse before any cast. UI, board exterior and
            // a spatially rejected cell must preserve resources and never add an orange cell.
            yield return Click(RectTransformUtility.WorldToScreenPoint(null, bar.ThirdArtButton.transform.position));
            AssertSpellHighlights(session);
            yield return CaptureG2("open-no-targets-" + width);
            int ap = spawned.Player.ActionPointsRemainingUnits, mp = spawned.Player.CurrentSpirit;
            HexCoord far = spawned.Enemy.Position;
            if (TryFindCastingPoint(far, out var farPoint))
            {
                yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(farPoint, true);
                Assert.IsFalse(input.Preview.CanConfirm);
                AssertSpellHighlights(session);
            }
            yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(new Vector2(-40, -40), true);
            Assert.IsFalse(input.Preview.CanConfirm);
            Assert.IsNull(input.Candidate.TargetCell);
            yield return Click(RectTransformUtility.WorldToScreenPoint(null, view.CancelButton.transform.position));
            Assert.IsNull(input.Candidate);
            Assert.AreEqual(ap, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(mp, spawned.Player.CurrentSpirit);

            // Two real 50 AP casts consume the complete turn budget and retain the turn.
            yield return CastEmpty(bar.ThirdArtButton, "spell_feixingzhi", 50000, 10, "star-first-" + width, random);
            yield return CastEmpty(bar.ThirdArtButton, "spell_feixingzhi", 50000, 10, "star-second-" + width, random);
            Assert.AreEqual(0, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual("player", session.CurrentActorId);
            Assert.AreEqual(CombatTurnPhase.Active, session.TurnPhase);
            Assert.IsTrue(bar.WaitButton.interactable);
            var repeat = new CombatCommandService().Preview(session, new CombatCommand(CombatCommandKind.Art,
                "player", profileId: "spell_feixingzhi", targetCell: new HexCoord(1, 0)));
            Assert.AreEqual(2, repeat.CommittedUseCount);
            Assert.Greater(repeat.RepeatMultiplierPermille, 1000);
            Assert.IsFalse(bar.ThirdArtButton.interactable);
            yield return CaptureG2("zero-ap-" + width);

            encounter.RequestEndTurn("player");
            yield return null;
            yield return CommandsReady();
            // Negative fixture changes only cooldown; the ordinary entry and pointer submit
            // still run through the production UI, quote, and coordinator.
            spawned.Player.SetCooldown("spell_feixingzhi", 60);
            RefreshAuthority();
            Assert.IsTrue(bar.ThirdArtButton.interactable);
            yield return Click(RectTransformUtility.WorldToScreenPoint(null, bar.ThirdArtButton.transform.position));
            HexCoord cooldownCell = EmptyCell("spell_feixingzhi");
            Assert.IsTrue(TryFindCastingPoint(cooldownCell, out var cooldownPoint));
            ap = spawned.Player.ActionPointsRemainingUnits; mp = spawned.Player.CurrentSpirit;
            int sampled = random.Count;
            yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(cooldownPoint, true);
            Assert.IsFalse(input.Preview.CanConfirm);
            Assert.Greater(input.Preview.RemainingCooldownTicks, 0);
            Assert.IsNotEmpty(input.Preview.Reason);
            Assert.AreEqual(ap, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(mp, spawned.Player.CurrentSpirit);
            Assert.AreEqual(sampled, random.Count);
            yield return CaptureG2("cooldown-open-" + width);
            input.CancelCandidate();
            spawned.Player.SetCooldown("spell_feixingzhi", 0);
            RefreshAuthority();
            yield return CastEmpty(bar.BasicAttackButton, spawned.Player.BasicAttackProfileId, 40000, 0, "sword-" + width, random);
            yield return CastEmpty(bar.ArtButton, "spell_guanshiji", 50000, 12, "strike-" + width, random);
            encounter.RequestEndTurn("player");
            yield return null;
            yield return CommandsReady();
            yield return CastEmpty(bar.SecondArtButton, "spell_julingshu", 25000, 20, "recovery-" + width, random);
            yield return Click(RectTransformUtility.WorldToScreenPoint(null, bar.SecondArtButton.transform.position));
            yield return PointArt(session, "player");
            Assert.AreEqual(25000, input.Preview.ActionPointCostUnits);
            Assert.AreEqual(0, input.Preview.SpiritCost, "Only the actor's occupied cell uses the self branch.");
            AssertAffectedTarget(input, "player");
            input.CancelCandidate();
        }

        private HexCoord EmptyCell(string profileId)
        {
            var service = new CombatCommandService();
            bool basic = profileId == spawned.Player.BasicAttackProfileId;
            foreach (var key in Find<CombatMovementView>().Highlights.Keys.OrderBy(key => key.x).ThenBy(key => key.y))
            {
                var cell = new HexCoord(key.x, key.y);
                if (cell.Equals(spawned.Player.Position) || !TryFindCastingPoint(cell, out _)) continue;
                var quote = service.Preview(session, new CombatCommand(basic ? CombatCommandKind.BasicAttack : CombatCommandKind.Art,
                    "player", profileId: profileId, targetCell: cell,
                    attackDirection: basic ? spawned.Player.Position.DirectionToward(cell) : (int?)null));
                if (quote.AttackArea != null && quote.AttackArea.Cells.Any(value => value.Coord.Equals(cell) && value.IsLegal) &&
                    quote.AttackArea.TargetIds.Count == 0) return cell;
            }
            Assert.Fail("No visible spatially valid empty cell for " + profileId);
            return default;
        }

        private IEnumerator CastEmpty(Button button, string profileId, int expectedAp, int expectedMp,
            string label, G2CountingRandom random)
        {
            var input = Find<CombatCommandInput>();
            Assert.IsTrue(button.interactable, profileId);
            yield return Click(RectTransformUtility.WorldToScreenPoint(null, button.transform.position));
            HexCoord cell = EmptyCell(profileId);
            Assert.IsTrue(TryFindCastingPoint(cell, out var point));
            yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(point);
            var quote = input.Preview;
            Assert.IsTrue(quote.CanConfirm, quote.Reason);
            Assert.AreEqual(expectedAp, quote.ActionPointCostUnits);
            Assert.AreEqual(expectedMp, quote.SpiritCost);
            Assert.IsEmpty(quote.AttackArea.Targets);
            StringAssert.Contains("空放", Get<Text>(Find<CombatActionPreviewView>(), "summary").text);
            int ap = spawned.Player.ActionPointsRemainingUnits, mp = spawned.Player.CurrentSpirit;
            int ct = session.PendingCtPenalty, samples = random.Count;
            var health = session.Combatants.All.ToDictionary(unit => unit.Id, unit => unit.CurrentHealth);
            var spirits = spawned.Enemies.ToDictionary(unit => unit.Id, unit => unit.CurrentSpirit);
            yield return CaptureG2(label + "-preview");
            yield return GuanzhongBasicAttackPlayModeTests.ConfirmPointedAttack();
            Assert.AreEqual(ap - expectedAp, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(mp - expectedMp, spawned.Player.CurrentSpirit);
            Assert.AreEqual(ct + quote.CtPenalty, session.PendingCtPenalty);
            Assert.AreEqual(quote.ReuseCooldownTicks, spawned.Player.GetCooldown(profileId));
            Assert.AreEqual(samples, random.Count, "Empty casting must not sample a fabricated target.");
            foreach (var unit in session.Combatants.All) Assert.AreEqual(health[unit.Id], unit.CurrentHealth);
            foreach (var unit in spawned.Enemies) Assert.AreEqual(spirits[unit.Id], unit.CurrentSpirit);
            Assert.IsNull(input.Candidate);
            var after = new CombatCommandService().Preview(session, new CombatCommand(
                profileId == spawned.Player.BasicAttackProfileId ? CombatCommandKind.BasicAttack : CombatCommandKind.Art,
                "player", profileId: profileId, targetCell: cell, attackDirection: quote.Candidate.AttackDirection));
            Assert.AreEqual(quote.ReuseCooldownTicks == 0 ? quote.CommittedUseCount + 1 : quote.CommittedUseCount,
                after.CommittedUseCount);
            // The consumed candidate cannot be replayed, even before the presentation unlocks.
            encounter.ConfirmAction(quote.Candidate);
            Assert.AreEqual(ap - expectedAp, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(samples, random.Count);
            if (profileId == "spell_feixingzhi")
            {
                var effect = GameObject.Find(CombatAttackVfx.ProjectileObjectName);
                Assert.IsNotNull(effect, "An empty star cast still needs its real projectile.");
                Vector3 endpoint = Get<Vector3>(effect.GetComponent<CombatAttackVfx>(), "to");
                Vector3 expected = Static3DCombatUnitPresentationProvider.HexToWorld(new CombatUnitPresentationHex(cell.Q, cell.R)) + Vector3.up * .75f;
                Assert.Less(Vector3.Distance(endpoint, expected), .001f);
                Assert.IsTrue(provider.TryGetPresentedUnit("player", out var root));
                Vector3 direction = expected - root.transform.position; direction.y = 0;
                Assert.Greater(Vector3.Dot(root.transform.forward, direction.normalized), .35f);
            }
            yield return CaptureG2(label + "-committed");
            yield return CommandsReady();
        }

        private IEnumerator CaptureG2(string label)
        {
            Canvas.ForceUpdateCanvases();
            var input = Find<CombatCommandInput>();
            var movement = Find<CombatMovementView>();
            string cellText = input.Candidate?.TargetCell is CombatUnitPresentationHex selected
                ? selected.Q + "," + selected.R : "none";
            string orange = string.Join("/", movement.Highlights.Where(pair => pair.Value.gameObject.activeSelf &&
                Mathf.Abs(pair.Value.widthMultiplier - .06f) < .0005f).Select(pair => pair.Key.x + "," + pair.Key.y));
            string directory = Path.Combine(Application.dataPath, "../Logs/g2-tile-casting");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, label + ".txt"),
                "resolution=" + Screen.width + "x" + Screen.height + "; mouse=" + Mouse.current.position.ReadValue() +
                "; camera=" + Camera.main.transform.position + "; cell=" + cellText + "; orange=" + orange +
                "; affected=" + string.Join("/", input.Preview?.AttackArea?.Targets.Select(unit => unit.Id) ?? Array.Empty<string>()) +
                "; AP=" + spawned.Player.ActionPointsRemainingUnits + "; MP=" + spawned.Player.CurrentSpirit +
                "; CT=" + session.PendingCtPenalty + "; summary=" + Get<Text>(Find<CombatActionPreviewView>(), "summary").text +
                "; borders=" + string.Join("/", movement.Highlights.Where(pair => pair.Value.gameObject.activeSelf).Select(pair =>
                    pair.Key + ":width=" + pair.Value.widthMultiplier + ":vertices=" + string.Join("|",
                        Enumerable.Range(0, 6).Select(index => pair.Value.GetPosition(index).ToString("F4"))))));
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, label + ".png"));
            yield return null; yield return null;
        }

        private sealed class G2CountingRandom : ICombatResolutionRandomSource
        {
            public int Count { get; private set; }
            public float NextPercent() { Count++; return 99; }
        }

        [UnityTest]
        public IEnumerator BodyPickingUsesCurrentPoseBeforeTheNextPhysicsStep()
        {
            Assert.IsFalse(Physics.autoSyncTransforms);
            Assert.IsTrue(TryFindCastingPoint(spawned.Enemy.Position, out var point, true));
            Ray ray = Camera.main.ScreenPointToRay(point);
            Assert.IsTrue(provider.TryPickBodyPoint(ray, out Vector3 before));
            Assert.IsTrue(provider.TryGetPresentedUnit(spawned.Enemy.Id, out GameObject root));
            Vector3 original = root.transform.position;
            Vector3 offset = new Vector3(20f, 0f, 0f);
            try
            {
                root.transform.position += offset;
                Assert.IsTrue(provider.TryPickBodyPoint(new Ray(ray.origin + offset, ray.direction), out Vector3 after),
                    "The first query after a transform change must hit its current visible pose.");
                Assert.That(Vector3.Distance(before + offset, after), Is.LessThan(.002f));
                TestContext.WriteLine("Same-frame body pick: before=" + before.ToString("F4") +
                    "; after=" + after.ToString("F4") + "; expected=" + (before + offset).ToString("F4"));
            }
            finally
            {
                root.transform.position = original;
                Physics.SyncTransforms();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SpellSelectableFootprintsStaySeparateFromEffectsAtBothResolutions()
        {
            var input = Find<CombatCommandInput>();
            var bar = Find<CombatActionBarView>();
            var movement = Find<CombatMovementView>();
            var preview = Find<CombatActionPreviewView>();
            int ap = spawned.Player.ActionPointsRemainingUnits, mp = spawned.Player.CurrentSpirit;
            foreach (int width in new[] { 1920, 1366 })
            {
                GuanzhongCombatActionPreviewPlayModeTests.SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                yield return Click(RectTransformUtility.WorldToScreenPoint(null, bar.ThirdArtButton.transform.position));
                Assert.IsNull(input.Candidate.TargetCell);
                Assert.IsFalse(input.Preview.CanConfirm);
                AssertSpellHighlights(session);
                StringAssert.Contains("可施放地块", Get<Text>(preview, "legend").text);
                yield return CaptureArtEvidence("g2-selectable-open-" + width, session);
                foreach (var enemy in spawned.Enemies)
                foreach (HexCoord cell in enemy.OccupiedCells)
                foreach (bool body in new[] { false, true })
                {
                    Assert.IsTrue(TryFindCastingPoint(cell, out Vector2 point, body),
                        "Need a real " + (body ? "model" : "ground") + " point for " + enemy.Id + " " + cell);
                    yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(point);
                    Assert.AreEqual(new CombatUnitPresentationHex(cell.Q, cell.R), input.Candidate.TargetCell);
                    Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
                    AssertAffectedTarget(input, enemy.Id);
                    CollectionAssert.AreEqual(new[] { input.Candidate.TargetCell.Value },
                        input.Preview.AttackArea.Cells.Where(value => value.IsLegal).Select(value => value.Position));
                    AssertSpellHighlights(session);
                    yield return CaptureArtEvidence("g2-" + enemy.Id + "-" + cell.Q + "-" + cell.R +
                        (body ? "-body-" : "-ground-") + width, session);
                }
                HexCoord empty = EmptyCell("spell_feixingzhi");
                Assert.IsTrue(TryFindCastingPoint(empty, out var emptyPoint));
                yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(emptyPoint);
                Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
                Assert.IsEmpty(input.Preview.AttackArea.Targets);
                AssertSpellHighlights(session);
                yield return CaptureArtEvidence("g2-selectable-empty-" + width, session);
                yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(
                    RectTransformUtility.WorldToScreenPoint(null, Get<Text>(preview, "summary").transform.position), true);
                Assert.IsFalse(input.Preview.CanConfirm);
                Assert.IsNull(input.Candidate.TargetCell);
                AssertSpellHighlights(session);
                yield return Click(RectTransformUtility.WorldToScreenPoint(null, bar.SecondArtButton.transform.position));
                Assert.AreEqual("spell_julingshu", input.Candidate.ProfileId);
                AssertSpellHighlights(session);
                yield return Click(RectTransformUtility.WorldToScreenPoint(null, preview.CancelButton.transform.position));
                Assert.IsFalse(movement.Highlights.Values.Any(ring => ring.gameObject.activeSelf));
                Assert.AreEqual(ap, spawned.Player.ActionPointsRemainingUnits);
                Assert.AreEqual(mp, spawned.Player.CurrentSpirit);
            }
            // Death changes affected units, not the set of legal terrain cells.
            var dead = spawned.Enemy;
            HexCoord deadCell = dead.Position;
            dead.ReceiveDamage(dead.CurrentHealth);
            RefreshAuthority();
            yield return Click(RectTransformUtility.WorldToScreenPoint(null, bar.ThirdArtButton.transform.position));
            Assert.IsTrue(TryFindCastingPoint(deadCell, out var deadPoint));
            yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(deadPoint);
            Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
            Assert.IsEmpty(input.Preview.AttackArea.Targets);
            AssertSpellHighlights(session);
            input.CancelCandidate();
            Assert.IsFalse(movement.Highlights.Values.Any(ring => ring.gameObject.activeSelf));
        }

        public static void AssertSpellHighlights(CombatSession combat)
        {
            var input = Find<CombatCommandInput>();
            var movement = Find<CombatMovementView>();
            Assert.IsTrue(input.IsSelectingAttack);
            Assert.AreEqual(CombatActionKind.Art, input.Candidate.Kind);
            var action = Find<CombatHudPresenter>().Snapshot.ArtActions.Single(art => art.ProfileId == input.Candidate.ProfileId);
            var selectable = action.CastableCells.Select(cell => new Vector2Int(cell.Q, cell.R)).ToArray();
            var effects = new HashSet<Vector2Int>();
            if (input.Candidate.TargetCell.HasValue && input.Preview.AttackArea != null)
                effects.UnionWith(input.Preview.AttackArea.Cells.Where(cell => cell.IsLegal)
                    .Select(cell => new Vector2Int(cell.Position.Q, cell.Position.R)));
            var active = movement.Highlights.Where(pair => pair.Value.gameObject.activeSelf).ToArray();
            CollectionAssert.AreEquivalent(selectable.Concat(effects).Distinct(), active.Select(pair => pair.Key),
                "Borders must be legal cast cells plus the quoted effect, never the entire affected unit footprint.");
            foreach (var pair in active)
            {
                bool effect = effects.Contains(pair.Key);
                var properties = new MaterialPropertyBlock();
                pair.Value.GetPropertyBlock(properties);
                Color expected = effect ? new Color32(0xFF, 0x9F, 0x3C, 0xFF) : new Color32(0x23, 0x8C, 0xFF, 0xFF);
                Assert.That(properties.GetColor("_BaseColor"), Is.EqualTo(expected), pair.Value.name);
                Assert.That(pair.Value.widthMultiplier, Is.EqualTo(effect ? .06f : .035f).Within(.0005f), pair.Value.name);
            }
        }

        public static IEnumerator CaptureArtEvidence(string label, CombatSession combat)
        {
            var input = Find<CombatCommandInput>();
            var movement = Find<CombatMovementView>();
            var bar = Find<CombatActionBarView>();
            Canvas.ForceUpdateCanvases();
            foreach (Text text in new[] { bar.ArtButton, bar.SecondArtButton }
                .Select(button => button.GetComponentInChildren<Text>()))
            {
                Assert.LessOrEqual(text.preferredWidth, text.rectTransform.rect.width + 1f, text.text);
                Assert.LessOrEqual(text.preferredHeight, text.rectTransform.rect.height + 1f, text.text);
            }
            AssertSpellHighlights(combat);
            var rings = movement.Highlights.Where(pair => pair.Value.gameObject.activeSelf).ToArray();
            string summary = Get<Text>(Find<CombatActionPreviewView>(), "summary").text;
            StringAssert.Contains("作用", summary);
#if UNITY_EDITOR
            for (float deadline = Time.realtimeSinceStartup + 120f; UnityEditor.ShaderUtil.anythingCompiling && Time.realtimeSinceStartup < deadline;) yield return null;
            Assert.IsFalse(UnityEditor.ShaderUtil.anythingCompiling);
#endif
            for (int frame = 0; frame < 8; frame++) yield return null;
            string directory = Path.Combine(Application.dataPath, "../Logs/art-target");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, label + ".txt"), "resolution=" + Screen.width + "x" + Screen.height +
                "; targetCell=" + input.Candidate.TargetCell + "; targets=" +
                string.Join("/", input.Preview.AttackArea?.Targets.Select(unit => unit.Id) ?? Array.Empty<string>()) +
                "; profile=" + input.Candidate.ProfileId +
                "; context=" + input.Candidate.ContextVersion + "; AP=" + input.Preview.ActionPointCostUnits +
                "; MP=" + input.Preview.SpiritCost + "; castRange=" + input.Preview.MinimumRange + "-" + input.Preview.MaximumRange +
                "; rings=" + string.Join("/", rings.Select(pair =>
                {
                    LineRenderer ring = pair.Value;
                    var properties = new MaterialPropertyBlock(); ring.GetPropertyBlock(properties);
                    return ring.name + ":#" + ColorUtility.ToHtmlStringRGB(properties.GetColor("_BaseColor")) +
                        ":width=" + ring.widthMultiplier + ":parent=" + ring.transform.parent.name + ":world=" + ring.useWorldSpace +
                        ":points=" + string.Join("|", Enumerable.Range(0, 6).Select(index => ring.GetPosition(index).ToString("F4")));
                })) +
                "; pointer=" + Mouse.current.position.ReadValue() +
                "; camera=" + Camera.main.transform.position + "; legal=" + input.Preview.CanConfirm +
                "; summary=" + summary.Replace("\n", "\\n") +
                "; labels=" + string.Join("/", new[] { bar.ArtButton, bar.SecondArtButton }.Select(button =>
                {
                    Text text = button.GetComponentInChildren<Text>();
                    return text.text + ":rect=" + text.rectTransform.rect.size + ":preferred=" + text.preferredWidth + "," + text.preferredHeight;
                })));
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, label + ".png"));
            for (int frame = 0; frame < 8; frame++) yield return null;
        }

        private Vector2 CellPoint(HexCoord cell) => Camera.main.WorldToScreenPoint(GameObject.Find("GuanzhongBattlefield")
            .transform.Find("GuanzhongHex_" + cell.Q + "_" + cell.R).position);

        private void RefreshAuthority() => typeof(EncounterCoordinator).GetMethod("Present", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(encounter, new object[] { "目标选取夹具刷新", true, null });

        private IEnumerator CommandsReady()
        {
            for (float deadline = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < deadline &&
                (session.CurrentActorId != "player" || session.TurnPhase != CombatTurnPhase.Active || encounter.IsPresenting);) yield return null;
            yield return null;
            Assert.IsFalse(encounter.IsPresenting);
            Assert.AreEqual("player", session.CurrentActorId);
            Assert.AreEqual(CombatTurnPhase.Active, session.TurnPhase);
        }

        private static void AssertBasicHighlights(CombatCommandInput input, CombatMovementView movement)
        {
            var castable = Find<CombatHudPresenter>().Snapshot.Actions.Where(action =>
                action.Candidate.Kind == CombatActionKind.BasicAttack && action.AttackArea != null)
                .SelectMany(action => action.AttackArea.Cells).Where(cell => cell.IsLegal)
                .Select(cell => new Vector2Int(cell.Position.Q, cell.Position.R)).Distinct().ToArray();
            var effects = input.Preview.AttackArea != null && input.Candidate.TargetCell.HasValue &&
                input.Preview.AttackArea.Cells.Any(cell => cell.IsLegal && cell.Position.Equals(input.Candidate.TargetCell.Value))
                ? input.Preview.AttackArea.Cells.Where(cell => cell.IsLegal)
                    .Select(cell => new Vector2Int(cell.Position.Q, cell.Position.R)).ToArray() : Array.Empty<Vector2Int>();
            CollectionAssert.AreEquivalent(castable.Concat(effects).Distinct(),
                movement.Highlights.Where(pair => pair.Value.gameObject.activeSelf).Select(pair => pair.Key));
            CollectionAssert.AreEquivalent(effects, movement.Highlights.Where(pair => pair.Value.gameObject.activeSelf &&
                Mathf.Abs(pair.Value.widthMultiplier - .06f) < .0005f).Select(pair => pair.Key));
        }

        [UnityTest]
        public IEnumerator BasicPointerHoverCancelLockAndConfirmAtBothResolutions()
        {
            var input = Find<CombatCommandInput>();
            var bar = Find<CombatActionBarView>();
            var preview = Find<CombatActionPreviewView>();
            var movement = Find<CombatMovementView>();
            foreach (int width in new[] { 1920, 1366 })
            {
                GuanzhongCombatActionPreviewPlayModeTests.SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Assert.AreEqual(width, Screen.width);
                // A distant hover chooses the blade direction, never an additional effect cell.
                Camera camera = Camera.main;
                Vector2Int selfKey = new Vector2Int(spawned.Player.Position.Q, spawned.Player.Position.R);
                presenter.InspectTarget(spawned.Enemy.Id);
                bar.BasicAttackButton.onClick.Invoke();
                Transform board = GameObject.Find("GuanzhongBattlefield").transform;
                Vector2Int farKey = movement.Highlights.Keys.First(key =>
                {
                    int q = key.x - selfKey.x, r = key.y - selfKey.y;
                    if ((Math.Abs(q) + Math.Abs(r) + Math.Abs(q + r)) / 2 <= 2) return false;
                    Vector2 point = camera.WorldToScreenPoint(board.Find("GuanzhongHex_" + key.x + "_" + key.y).position);
                    return movement.TryPickCell(point, out var picked) && picked.Q == key.x && picked.R == key.y;
                });
                Vector2 farPoint = camera.WorldToScreenPoint(board.Find("GuanzhongHex_" + farKey.x + "_" + farKey.y).position);
                yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(farPoint);
                Assert.IsNotNull(input.Preview.AttackArea);
                string effectDirectory = Path.Combine(Application.dataPath, "../Logs/attack-highlight");
                Directory.CreateDirectory(effectDirectory);
                ScreenCapture.CaptureScreenshot(Path.Combine(effectDirectory, "effect-only-basic-" + width + ".png"));
                for (int frame = 0; frame < 8; frame++) yield return null;
                AssertBasicHighlights(input, movement);
                Assert.IsFalse(input.Preview.CanConfirm, "An explicit far tile cannot be replaced by a nearer direction cell.");
                Assert.IsFalse(movement.Highlights[farKey].gameObject.activeSelf);
                Assert.IsFalse(movement.InspectionRings.Any(ring => ring.gameObject.activeSelf),
                    "Inspection borders must not add unrelated cells while aiming.");
                Assert.AreEqual(spawned.Enemy.Id, presenter.InspectedTargetId);
                input.CancelCandidate();
                yield return null;
                Assert.AreEqual(2, movement.InspectionRings.Count(ring => ring.gameObject.activeSelf),
                    "Cancelling restores the inspected double-cell unit's borders.");
                presenter.ClearInspectedTarget();

                string inspected = presenter.InspectedTargetId;
                int ap = spawned.Player.ActionPointsRemainingUnits, mp = spawned.Player.CurrentSpirit;
                int hp = spawned.Enemy.CurrentHealth, tick = session.TurnScheduler.CurrentTick;
                HexCoord origin = spawned.Player.Position;
                bar.BasicAttackButton.onClick.Invoke();
                Assert.IsTrue(input.IsSelectingAttack);
                Assert.IsFalse(preview.ConfirmButton.gameObject.activeSelf);
                yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session, click: false);
                Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
                Assert.IsFalse(preview.ConfirmButton.interactable);
                AssertBasicHighlights(input, movement);
                input.ConfirmCandidate();
                Assert.AreEqual(ap, spawned.Player.ActionPointsRemainingUnits);
                Vector2 confirm = RectTransformUtility.WorldToScreenPoint(null, Get<Text>(preview, "summary").transform.position);
                yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(confirm);
                Assert.IsFalse(input.Preview.CanConfirm);
                AssertBasicHighlights(input, movement);
                yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session, click: false);
                yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(new Vector2(-50, -50));
                Assert.IsFalse(input.Preview.CanConfirm);
                yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);
                Assert.IsTrue(input.IsSelectingAttack);
                Assert.IsFalse(preview.ConfirmButton.gameObject.activeSelf);
                Assert.AreEqual(inspected, presenter.InspectedTargetId, "Aiming must not inspect a piece.");
                Assert.IsFalse(movement.IsSelecting);
                Assert.AreEqual(origin, spawned.Player.Position);
                Assert.AreEqual(ap, spawned.Player.ActionPointsRemainingUnits);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                yield return null; yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                Assert.AreEqual(mp, spawned.Player.CurrentSpirit);
                Assert.AreEqual(hp, spawned.Enemy.CurrentHealth);
                Assert.AreEqual(tick, session.TurnScheduler.CurrentTick);
#if UNITY_EDITOR
                for (float deadline = Time.realtimeSinceStartup + 120f; UnityEditor.ShaderUtil.anythingCompiling && Time.realtimeSinceStartup < deadline;) yield return null;
                Assert.IsFalse(UnityEditor.ShaderUtil.anythingCompiling);
#endif
                for (int frame = 0; frame < 8; frame++) yield return null;
                string directory = Path.Combine(Application.dataPath, "../Logs/q3-direct-cast");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "pointer-" + width + ".txt"),
                    "pointer=" + mouse.position.ReadValue() + "; origin=" + origin +
                    "; area=" + string.Join("/", input.Preview.AttackArea.Cells.Select(c => c.Position.Q + "," + c.Position.R)));
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, "pointer-" + width + ".png"));
                for (int frame = 0; frame < 8; frame++) yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = confirm }.WithButton(MouseButton.Right));
                yield return null; yield return null;
                Assert.IsFalse(input.IsSelectingAttack);
                Assert.IsNull(input.Candidate);
                InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null;
                bar.BasicAttackButton.onClick.Invoke();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
                yield return null; yield return null;
                Assert.IsNull(input.Candidate);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                bar.BasicAttackButton.onClick.Invoke();
                bar.MoveButton.onClick.Invoke();
                Assert.IsFalse(input.IsSelectingAttack);
                Assert.IsTrue(movement.IsSelecting);
                bar.BasicAttackButton.onClick.Invoke();
                Assert.IsFalse(movement.IsSelecting);
                input.CancelCandidate();
            }
            yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);
            int before = spawned.Player.ActionPointsRemainingUnits, cost = input.Preview.ActionPointCostUnits;
            yield return GuanzhongBasicAttackPlayModeTests.ClickPointedAttack();
            Assert.IsTrue(input.IsTargetLocked);
            Assert.AreEqual(before, spawned.Player.ActionPointsRemainingUnits);
            yield return GuanzhongBasicAttackPlayModeTests.ConfirmLockedTarget();
            Assert.AreEqual(before - cost, spawned.Player.ActionPointsRemainingUnits);
            Assert.IsFalse(input.IsSelectingAttack);
            Assert.IsNull(input.Candidate);
            Assert.IsFalse(movement.Highlights.Values.Any(ring => ring.gameObject.activeSelf));
        }

        [UnityTest]
        public IEnumerator WorldAxisAndKeyboardKeepStableIdentityWithoutChangingCommands()
        {
            string actor = session.CurrentActorId;
            CombatCommandInput commands = Find<CombatCommandInput>();
            string commandTarget = Get<string>(commands, "targetId");
            CollectionAssert.AreEqual(spawned.Player.EquippedArtProfileIds,
                presenter.Snapshot.ArtActions.Select(action => action.ProfileId));
            int tick = session.TurnScheduler.CurrentTick, hp = spawned.Player.CurrentHealth, mp = spawned.Player.CurrentSpirit;
            yield return Click(WorldPoint(spawned.Enemy.Id));
            AssertSelection(spawned.Enemy.Id);
            Assert.AreEqual("敌方", target.FactionText);
            StringAssert.Contains("伤害：未探明", target.DetailsText);
            AssertInspectionRing(spawned.Enemy.Id);
            Button[] entries = Get<Button[]>(timeline, "entryButtons");
            int playerIndex = presenter.Snapshot.Timeline.Entries.ToList().FindIndex(value => value.Id == spawned.Player.Id);
            yield return Click(EntryCenter(entries[playerIndex]));
            AssertSelection(spawned.Player.Id);
            Assert.AreEqual("我方", target.FactionText);
            CombatTimelineSnapshot original = presenter.Snapshot.Timeline;
            CombatHudSnapshot hud = presenter.Snapshot;
            presenter.Present(new CombatHudSnapshot(hud.Player, hud.Enemy, hud.TurnText, hud.AcceptsCommands,
                hud.ArtProfileIds, hud.DivineProfileIds,
                new CombatTimelineSnapshot(original.CurrentTick, original.CurrentActorId, original.Entries.Reverse().ToArray()),
                hud.CanEndTurn, hud.Movement, hud.IsPresenting, hud.ArtActions,
                commandContextVersion: hud.CommandContextVersion, actions: hud.Actions, enemies: hud.Enemies));
            int newPlayerIndex = original.Entries.Count - 1 - playerIndex;
            Assert.AreSame(entries[newPlayerIndex].gameObject, EventSystem.current.currentSelectedGameObject);
            AssertSelection(spawned.Player.Id);
            // Keyboard navigation changes peek only; submit changes inspection.
            // Pointer hover deliberately takes priority over keyboard peek.
            yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(new Vector2(-50, -50));
            // The compact action order is vertical; navigation follows screen geometry.
            bool goDown = newPlayerIndex < original.Entries.Count - 1;
            Selectable adjacent = goDown ? entries[newPlayerIndex].FindSelectableOnDown() : entries[newPlayerIndex].FindSelectableOnUp();
            int adjacentIndex = Array.FindIndex(entries, entry => entry == adjacent);
            Assert.That(adjacentIndex, Is.GreaterThanOrEqualTo(0));
            string adjacentId = original.Entries.Reverse().ToArray()[adjacentIndex].Id;
            Assert.That(adjacentId, Is.Not.EqualTo(spawned.Player.Id));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(goDown ? Key.DownArrow : Key.UpArrow));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            Assert.AreEqual(adjacentId, timeline.FocusedCombatantId);
            AssertSelection(spawned.Player.Id);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            AssertSelection(adjacentId);
            Assert.AreEqual(actor, session.CurrentActorId);
            Assert.AreEqual(commandTarget, Get<string>(commands, "targetId"));
            Assert.AreEqual(tick, session.TurnScheduler.CurrentTick);
            Assert.AreEqual(hp, spawned.Player.CurrentHealth);
            Assert.AreEqual(mp, spawned.Player.CurrentSpirit);
        }

        [UnityTest]
        public IEnumerator UiMovementAndPresentationArbitrateWithoutDoubleTrigger()
        {
            yield return Click(WorldPoint(spawned.Player.Id));
            AssertSelection(spawned.Player.Id);
            yield return Click(RectTransformUtility.WorldToScreenPoint(null, Get<Text>(target, "details").transform.position));
            AssertSelection(spawned.Player.Id);
            yield return Click(RectTransformUtility.WorldToScreenPoint(null, target.CloseButton.transform.position));
            AssertSelection(null);
            yield return Click(WorldPoint(spawned.Enemy.Id));
            yield return Click(new Vector2(Screen.width * 0.5f, Screen.height * 0.95f));
            AssertSelection(null);
        }

        [UnityTest]
        public IEnumerator ActionPointMovementAndPresentationArbitrateWithoutDoubleTrigger()
        {
            yield return Click(WorldPoint(spawned.Player.Id));
            AssertSelection(spawned.Player.Id);
            CombatMovementView movement = Find<CombatMovementView>();
            CombatActionBarView bar = Find<CombatActionBarView>();
            HexCoord before = spawned.Player.Position;
            int points = spawned.Player.ActionPointsRemainingUnits;
            bar.MoveButton.onClick.Invoke();
            yield return Click(WorldPoint(spawned.Enemy.Id));
            Assert.IsTrue(movement.IsSelecting);
            AssertSelection(spawned.Player.Id);
            Assert.AreEqual(before, spawned.Player.Position);
            CombatMovementOption option = presenter.Snapshot.Movement.Options.First(value => value.Cost == 50000);
            Vector3 tile = GameObject.Find("GuanzhongBattlefield").transform.Find("GuanzhongHex_" + option.Destination.Q + "_" + option.Destination.R).position;
            Vector2 tilePoint = Camera.main.WorldToScreenPoint(tile);
            yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(tilePoint, false);
            Assert.NotNull(movement.Selected, "Hover must preview the destination without committing.");
            Button[] entries = Get<Button[]>(timeline, "entryButtons");
            entries[1].onClick.Invoke();
            Assert.NotNull(movement.Selected, "Inspecting must not call SetContext and erase the movement candidate.");
            Find<CombatActionPreviewView>().CancelButton.onClick.Invoke();
            Assert.IsFalse(movement.IsSelecting);
            Assert.AreEqual(before, spawned.Player.Position);
            Assert.AreEqual(points, spawned.Player.ActionPointsRemainingUnits);
            bar.MoveButton.onClick.Invoke();
            yield return Click(tilePoint);
            Assert.IsTrue(movement.IsLocked);
            yield return GuanzhongBasicAttackPlayModeTests.ConfirmLockedTarget();
            Assert.IsTrue(encounter.IsPresenting, "A confirmed move must start presentation.");
            Assert.IsFalse(movement.IsSelecting);
            Assert.IsFalse(presenter.CanInspect);
            entries[1].onClick.Invoke();
            yield return Click(WorldPoint(spawned.Enemy.Id));
            AssertSelection(spawned.Player.Id);
            for (float deadline = Time.realtimeSinceStartup + 10f; Time.realtimeSinceStartup < deadline && encounter.IsPresenting;) yield return null;
            Assert.IsFalse(encounter.IsPresenting);
            Assert.AreEqual(new HexCoord(option.Destination.Q, option.Destination.R), spawned.Player.Position);
            Assert.AreEqual(points - option.Cost, spawned.Player.ActionPointsRemainingUnits);
            yield return null;
            AssertInspectionRing(spawned.Player.Id);
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator V7StructuredDetailsAndSingleGroundSelectionAtBothResolutions()
        {
            string directory = Path.Combine(Application.dataPath, "../Logs/v7-details");
            Directory.CreateDirectory(directory);
            CombatTargetInput pick = Find<CombatTargetInput>();
            CombatActionBarView bar = Find<CombatActionBarView>();
            CombatActionPreviewView preview = Find<CombatActionPreviewView>();
            yield return CommandsReady();
            foreach (int width in new[] { 1920, 1366 })
            {
                GuanzhongCombatActionPreviewPlayModeTests.SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.AreEqual(width, Screen.width);

                // Model click binds the primary enemy's own portrait, name, faction and resources.
                yield return Click(WorldPoint(spawned.Enemy.Id));
                AssertSelection(spawned.Enemy.Id);
                CombatantHudSnapshot primary = presenter.FindInspectable(spawned.Enemy.Id);
                Assert.AreEqual("enemy_shijiahou", primary.PortraitKey);
                Sprite shijiahouPortrait = target.PortraitSprite;
                Assert.NotNull(shijiahouPortrait);
                Assert.AreEqual(primary.DisplayName, target.NameText);
                Assert.AreEqual("敌方", target.FactionText);
                Assert.That(target.HealthFillAmount,
                    Is.EqualTo((float)primary.CurrentHealth / primary.MaximumHealth).Within(.001f));
                Assert.That(target.SpiritFillAmount,
                    Is.EqualTo((float)primary.CurrentSpirit / primary.MaximumSpirit).Within(.001f));
                yield return CaptureV7("v7-details-primary-" + width, directory);

                // The second enemy is pickable from its body and keeps its own portrait identity.
                CombatantHudSnapshot second = presenter.FindInspectable(spawned.Enemies[1].Id);
                Assert.NotNull(second, "The second enemy must stay inspectable.");
                Assert.IsTrue(pick.TryPick(WorldPoint(spawned.Enemies[1].Id), out string pickedId));
                Assert.AreEqual(spawned.Enemies[1].Id, pickedId, "TryPick must cover every enemy body.");
                yield return Click(WorldPoint(spawned.Enemies[1].Id));
                AssertSelection(spawned.Enemies[1].Id);
                Assert.AreEqual("enemy_lieshijia", second.PortraitKey);
                Assert.NotNull(target.PortraitSprite);
                Assert.AreNotSame(shijiahouPortrait, target.PortraitSprite,
                    "The two enemy portraits must stay distinct.");
                Assert.AreEqual(second.DisplayName, target.NameText);
                yield return CaptureV7("v7-details-second-" + width, directory);

                // The unit's own cell maps back to it, so a cell click inspects the same unit.
                Assert.IsTrue(provider.TryGetPresentedPosition(spawned.Enemies[1].Id,
                    out CombatUnitPresentationHex secondCell));
                Vector3 cellCenter = Static3DCombatUnitPresentationProvider.HexToWorld(secondCell);
                Vector2 cellPoint = Camera.main.WorldToScreenPoint(cellCenter + new Vector3(0.30f, 0, 0));
                Assert.IsTrue(pick.TryGetCell(cellPoint, out CombatUnitPresentationHex mappedCell),
                    "The formal grid must resolve the unit's cell.");
                Assert.That(mappedCell, Is.EqualTo(secondCell));
                Assert.AreEqual(spawned.Enemies[1].Id, pick.FindUnitAtCell(mappedCell),
                    "A cell click resolves the unit standing there.");
                Assert.AreEqual(spawned.Enemies[1].Id,
                    pick.TryPick(cellPoint, out string cellPicked) ? cellPicked : null);
                yield return Click(cellPoint);
                AssertSelection(spawned.Enemies[1].Id);
                yield return CaptureV7("v7-details-cell-pick-" + width, directory);

                // Timeline selection presents the same unit and the player's own portrait.
                Button[] entries = Get<Button[]>(timeline, "entryButtons");
                int playerIndex = presenter.Snapshot.Timeline.Entries.ToList().FindIndex(value => value.Id == spawned.Player.Id);
                Assert.That(playerIndex, Is.GreaterThanOrEqualTo(0));
                entries[playerIndex].onClick.Invoke();
                AssertSelection(spawned.Player.Id);
                Assert.AreEqual("我方", target.FactionText);
                Assert.AreEqual("player", presenter.FindInspectable(spawned.Player.Id).PortraitKey);
                Assert.AreNotSame(shijiahouPortrait, target.PortraitSprite,
                    "The player portrait must differ from the enemy portraits.");
                yield return CaptureV7("v7-details-player-" + width, directory);

                // Details and the action preview coexist without covering each other's controls.
                presenter.InspectTarget(spawned.Enemies[1].Id);
                bar.GuardButton.onClick.Invoke();
                Canvas.ForceUpdateCanvases();
                Assert.IsTrue(preview.IsVisible);
                Rect targetRect = ScreenRect(Get<GameObject>(target, "root").GetComponent<RectTransform>());
                Rect previewRect = ScreenRect(Get<GameObject>(preview, "root").GetComponent<RectTransform>());
                Assert.IsFalse(targetRect.Overlaps(previewRect), "Details and the action preview must not overlap.");
                AssertButtonReachable(target.CloseButton);
                AssertButtonReachable(preview.CancelButton);
                AssertButtonReachable(preview.ConfirmButton);
                yield return CaptureV7("v7-details-and-preview-" + width, directory);
                preview.CancelButton.onClick.Invoke();
                Assert.IsFalse(preview.IsVisible);

                // Closing clears the panel, the ground ring and the rail frame together.
                target.CloseButton.onClick.Invoke();
                AssertSelection(null);
                yield return null;
            }

            // Death and scene exit clear the same selection without stale rings.
            presenter.InspectTarget(spawned.Enemies[1].Id);
            AssertSelection(spawned.Enemies[1].Id);
            spawned.Enemies[1].ReceiveDamage(spawned.Enemies[1].CurrentHealth);
            typeof(EncounterCoordinator).GetMethod("Present", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(encounter, new object[] { "V7死亡清理", true, null });
            yield return null; yield return null;
            AssertSelection(null);
            presenter.InspectTarget(spawned.Enemy.Id);
            AssertSelection(spawned.Enemy.Id);
            presenter.Hide();
            AssertSelection(null);
        }

        private IEnumerator CaptureV7(string label, string directory)
        {
#if UNITY_EDITOR
            for (float deadline = Time.realtimeSinceStartup + 120f; UnityEditor.ShaderUtil.anythingCompiling && Time.realtimeSinceStartup < deadline;) yield return null;
            Assert.IsFalse(UnityEditor.ShaderUtil.anythingCompiling, "Shader compilation must finish before visual evidence.");
#endif
            for (int frame = 0; frame < 8; frame++) yield return null;
            Canvas.ForceUpdateCanvases();
            CombatMovementView movement = Find<CombatMovementView>();
            CombatActionPreviewView preview = Find<CombatActionPreviewView>();
            File.WriteAllText(Path.Combine(directory, label + ".txt"),
                "resolution=" + Screen.width + "x" + Screen.height +
                "; selected=" + presenter.InspectedTargetId +
                "; name=" + target.NameText + "; faction=" + target.FactionText +
                "; health=" + target.HealthText + "; spirit=" + target.SpiritText +
                "; portrait=" + (target.PortraitSprite == null ? "none" : target.PortraitSprite.name) +
                "; hpFill=" + target.HealthFillAmount.ToString("0.###") +
                "; mpFill=" + target.SpiritFillAmount.ToString("0.###") +
                "; details=" + target.DetailsText.Replace("\n", "\\n") +
                "; ring=" + movement.InspectionRing.gameObject.activeSelf +
                "; ringCell=" + movement.InspectionCell?.Q + "," + movement.InspectionCell?.R +
                "; targetRect=" + ScreenRect(Get<GameObject>(target, "root").GetComponent<RectTransform>()) +
                "; previewVisible=" + preview.IsVisible);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, label + ".png"));
            for (int frame = 0; frame < 6; frame++) yield return null;
        }

        private static Vector2 EntryCenter(Button entry)
        {
            RectTransform rect = (RectTransform)entry.transform;
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }

        private static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static bool Hits(Vector2 point)
        {
            EventSystem system = EventSystem.current;
            if (system == null) return false;
            var results = new List<RaycastResult>();
            system.RaycastAll(new PointerEventData(system) { position = point }, results);
            return results.Count > 0;
        }

        private static void AssertButtonReachable(Button button)
        {
            Assert.IsTrue(button != null && button.gameObject.activeInHierarchy);
            var corners = new Vector3[4];
            ((RectTransform)button.transform).GetWorldCorners(corners);
            Assert.That(corners[0].x, Is.GreaterThanOrEqualTo(0f));
            Assert.That(corners[0].y, Is.GreaterThanOrEqualTo(0f));
            Assert.That(corners[2].x, Is.LessThanOrEqualTo(Screen.width + .5f));
            Assert.That(corners[2].y, Is.LessThanOrEqualTo(Screen.height + .5f));
        }

        [UnityTest]
        public IEnumerator HideRealDeathAndReentryClearTheShortLivedSelection()
        {
            presenter.InspectTarget(spawned.Enemy.Id);
            AssertSelection(spawned.Enemy.Id);
            CombatHudSnapshot last = presenter.Snapshot;
            presenter.Hide();
            AssertSelection(null);
            presenter.Present(last);
            AssertSelection(null);
            presenter.InspectTarget(spawned.Enemy.Id);
            AssertSelection(spawned.Enemy.Id);
            // Controlled death precondition only; the command, presentation and scene return are real.
            // There are now two enemies: leave only the inspected enemy alive for the final-kill scenario.
            foreach (var enemy in spawned.Enemies.Where(value => value != spawned.Enemy))
                enemy.ReceiveDamage(enemy.CurrentHealth);
            spawned.Enemy.ReceiveDamage(spawned.Enemy.CurrentHealth - 1);
            encounter.SetCombatResolutionRandomSource(new GuanzhongBasicAttackPlayModeTests.OutcomeFixtureRandom(session, true));
            yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);
            yield return GuanzhongBasicAttackPlayModeTests.ConfirmPointedAttack();
            Assert.AreEqual(0, spawned.Enemy.CurrentHealth);
            AssertSelection(null);
            yield return SceneReady(GameplaySceneNames.Settlement);
            yield return EnterAdventure();
            AssertSelection(null);
            yield return Click(WorldPoint(spawned.Enemy.Id));
            AssertSelection(spawned.Enemy.Id);
        }

        private void AssertSelection(string id)
        {
            Assert.AreEqual(id, presenter.InspectedTargetId);
            Assert.AreEqual(id, timeline.InspectedTargetId);
            Assert.AreEqual(id, target.TargetId);
            Assert.AreEqual(id != null, Get<GameObject>(target, "root").activeSelf);
            CombatMovementView movement = Find<CombatMovementView>();
            if (id == null)
            {
                Assert.IsFalse(movement.InspectionRing.gameObject.activeSelf);
                Assert.IsNull(movement.InspectionCell);
                Assert.IsEmpty(target.DetailsText);
                Assert.IsEmpty(target.NameText);
                Assert.IsEmpty(target.HealthText);
                Assert.IsEmpty(target.SpiritText);
                Assert.That(Get<Text[]>(timeline, "entryLabels").All(label => !label.text.Contains("◆")), Is.True);
            }
            else
            {
                CombatantHudSnapshot unit = presenter.FindInspectable(id);
                Assert.AreEqual(unit.DisplayName, target.NameText);
                Assert.AreEqual(id == "player" ? "我方" : "敌方", target.FactionText);
                StringAssert.Contains("伤害：未探明", target.DetailsText);
                StringAssert.Contains("命中：未探明", target.DetailsText);
                StringAssert.Contains("能力：未探明", target.DetailsText);
                StringAssert.Contains("生命 " + unit.CurrentHealth + "/" + unit.MaximumHealth, target.HealthText);
                StringAssert.Contains("灵力 " + unit.CurrentSpirit + "/" + unit.MaximumSpirit, target.SpiritText);
                AssertInspectionRing(id);
            }
        }

        private Vector2 WorldPoint(string id)
        {
            Assert.IsTrue(provider.TryGetVisibleBounds(id, out Bounds bounds));
            return Camera.main.WorldToScreenPoint(bounds.center);
        }

        /// <summary>Every occupied cell gets one ground ring at the tile body's top.</summary>
        private void AssertInspectionRing(string id)
        {
            CombatMovementView movement = Find<CombatMovementView>();
            Assert.AreEqual(id, presenter.InspectedTargetId);
            Assert.IsTrue(provider.TryGetPresentedPose(id, out CombatUnitPresentationPose pose));
            Assert.That(movement.InspectionCell, Is.EqualTo(pose.Position));
            Assert.AreEqual(pose.IsDoubleCell ? 2 : 1, movement.InspectionRings.Count(ring => ring.gameObject.activeSelf));
            for (int index = 0; index < pose.OccupiedCells.Count; index++)
            {
                CombatUnitPresentationHex cell = pose.OccupiedCells[index];
                Transform tile = GameObject.Find("GuanzhongBattlefield").transform.Find("GuanzhongHex_" + cell.Q + "_" + cell.R);
                Assert.NotNull(tile);
                Vector3 center = Enumerable.Range(0, 6).Select(movement.InspectionRings[index].GetPosition)
                    .Aggregate(Vector3.zero, (sum, point) => sum + point) / 6f;
                Assert.That(center.x, Is.EqualTo(tile.position.x).Within(.001f));
                Assert.That(center.z, Is.EqualTo(tile.position.z).Within(.001f));
                Assert.That(center.y, Is.EqualTo(TileBodyTop(tile) + .01f).Within(.001f),
                    "The inspection ring must sit on the tile body, not the unit or an obstacle.");
                Assert.AreEqual(id, Find<CombatTargetInput>().FindUnitAtCell(cell));
            }
        }

        /// <summary>Tile body top only: the unit carrier and rocks share the cell root.</summary>
        private static float TileBodyTop(Transform tile)
        {
            Transform body = tile.Find("MeasuredFbxAlignment");
            Renderer[] renderers = body != null
                ? body.GetComponentsInChildren<Renderer>()
                : tile.GetComponents<Renderer>();
            float top = tile.position.y;
            foreach (Renderer renderer in renderers) top = Mathf.Max(top, renderer.bounds.max.y);
            return top;
        }

        private IEnumerator Click(Vector2 point)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            yield return null;
        }

        private static IEnumerator SceneReady(string scene)
        {
            for (float deadline = Time.realtimeSinceStartup + 15f; Time.realtimeSinceStartup < deadline && SceneManager.GetActiveScene().name != scene;) yield return null;
            Assert.AreEqual(scene, SceneManager.GetActiveScene().name);
            yield return null;
        }
        private static T Find<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private sealed class EncounterRandom : IFormalEncounterRandomSource { public int NextPercent() => 0; }
        private sealed class CombatRandom : ICombatResolutionRandomSource
        {
            private int count;
            public float NextPercent() => count++ % 5 == 0 ? 0f : 99f;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("天章/验证/记录目标联动现场 %&y")]
        private static void CaptureVisibleTargetEvidence()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Requires ordinary live Play Mode.");
            CombatHudPresenter hud = Find<CombatHudPresenter>();
            CombatTargetView view = Find<CombatTargetView>();
            Static3DCombatUnitPresentationProvider units = Find<Static3DCombatUnitPresentationProvider>();
            CombatCommandInput input = Find<CombatCommandInput>();
            CombatMovementView movement = Find<CombatMovementView>();
            Text detailText = Get<Text>(view, "details");
            var evidence = new VisibleEvidence
            {
                width = Screen.width, height = Screen.height, selected = hud.InspectedTargetId,
                axis = Find<CombatTimelineView>().InspectedTargetId, focus = Find<CombatTimelineView>().FocusedCombatantId,
                actor = hud.Snapshot?.Timeline?.CurrentActorId, commandTarget = Get<string>(input, "targetId"),
                details = view.DetailsText, name = view.NameText, faction = view.FactionText,
                health = view.HealthText, spirit = view.SpiritText, portrait = view.PortraitSprite?.name,
                canInspect = hud.CanInspect, selecting = movement.IsSelecting,
                ringActive = movement.InspectionRing.gameObject.activeSelf,
                ringCell = movement.InspectionCell?.Q + "," + movement.InspectionCell?.R,
                tick = hud.Snapshot?.Timeline?.CurrentTick ?? -1
            };
            evidence.textSize = detailText.rectTransform.rect.size;
            evidence.preferredHeight = detailText.preferredHeight;
            evidence.selectedButton = EventSystem.current?.currentSelectedGameObject?.name;
            evidence.focusedEditor = UnityEditor.EditorWindow.focusedWindow?.GetType().Name;
            if (movement.InspectionRing.gameObject.activeSelf)
                evidence.ringCenter = Enumerable.Range(0, 6).Select(movement.InspectionRing.GetPosition)
                    .Aggregate(Vector3.zero, (sum, point) => sum + point) / 6f;
            if (evidence.selected != null && units.TryGetPresentedUnit(evidence.selected, out GameObject root))
            {
                evidence.root = root.name; evidence.rootPosition = root.transform.position;
                units.TryGetVisibleBounds(evidence.selected, out Bounds bounds);
                evidence.boundsCenter = bounds.center; evidence.boundsSize = bounds.size;
            }
            string directory = Path.Combine(Application.dataPath, "../Logs/target");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "visible-target-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(evidence, true));
            Debug.Log("Read-only target evidence: " + path);
        }
        [Serializable]
        private sealed class VisibleEvidence
        {
            public int width, height, tick;
            public string selected, axis, focus, actor, commandTarget, details, root;
            public string name, faction, health, spirit, portrait, ringCell;
            public bool canInspect, selecting, ringActive;
            public string selectedButton, focusedEditor;
            public Vector2 textSize;
            public float preferredHeight;
            public Vector3 rootPosition, boundsCenter, boundsSize, ringCenter;
        }
#endif
    }
}
