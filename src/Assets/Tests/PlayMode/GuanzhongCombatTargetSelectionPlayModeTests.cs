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
    public sealed class GuanzhongCombatTargetSelectionPlayModeTests
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
            if (TestContext.CurrentContext.Test.Name.StartsWith("ActionPointMovement", StringComparison.Ordinal)) yield break;
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
            input.ConfirmCandidate();
            Assert.AreEqual(ap, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(mp, spawned.Player.CurrentSpirit);
            yield return PointArt(session, spawned.Enemy.Id);
            Assert.AreEqual(spawned.Enemy.Id, input.Candidate.TargetId);
            Assert.IsTrue(input.IsSelectingAttack);
            input.CancelCandidate();
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
            spawned.Player.Facing = Enumerable.Range(0, 6).First(direction => !service.Preview(session,
                new CombatCommand(CombatCommandKind.BasicAttack, "player", profileId: spawned.Player.BasicAttackProfileId,
                    attackDirection: direction)).IsLegal);
            typeof(EncounterCoordinator).GetMethod("Present", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(encounter, new object[] { "合法替代候选验证", true, null });
            Assert.IsFalse(presenter.Snapshot.FindAction(CombatActionKind.BasicAttack).CanConfirm);
            Assert.IsTrue(presenter.Snapshot.FindPreferredAction(CombatActionKind.BasicAttack).CanConfirm);
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
            Assert.AreEqual(legalArtTarget, input.Candidate.TargetId);
            input.CancelCandidate();
            Assert.AreEqual(ap, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(mp, spawned.Player.CurrentSpirit);
        }

        public static IEnumerator PointArt(CombatSession combat, string targetId, bool click = false)
        {
            Assert.IsTrue(combat.Combatants.TryGet(targetId, out var actor));
            Transform tile = GameObject.Find("GuanzhongBattlefield").transform.Find(
                "GuanzhongHex_" + actor.Position.Q + "_" + actor.Position.R);
            Assert.NotNull(tile);
            yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(Camera.main.WorldToScreenPoint(tile.position), click);
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
                    CellPoint(new HexCoord(1, 0)), new Vector2(-50, -50),
                    RectTransformUtility.WorldToScreenPoint(null, Get<Text>(preview, "summary").transform.position)
                })
                {
                    yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(rejected, true);
                    Assert.IsTrue(input.IsSelectingAttack);
                    Assert.IsFalse(input.Preview.CanConfirm, input.Preview.Reason);
                    Assert.AreNotEqual(spawned.Enemy.Id, input.Candidate.TargetId);
                    Assert.IsFalse(preview.ConfirmButton.interactable);
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
            yield return GuanzhongBasicAttackPlayModeTests.ClickPointedAttack();
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
                Assert.AreEqual(enemy.Id, input.Candidate.TargetId);
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
            Assert.IsFalse(input.Preview.CanConfirm);
            Assert.IsTrue(string.IsNullOrEmpty(input.Candidate.TargetId));
            var chosen = spawned.Enemies[1];
            yield return PointArt(session, chosen.Id);
            Assert.AreEqual(chosen.Id, input.Candidate.TargetId);
            Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
            int hp = chosen.CurrentHealth, mp = spawned.Player.CurrentSpirit, ap = spawned.Player.ActionPointsRemainingUnits;
            int cost = input.Preview.ActionPointCostUnits, mana = input.Preview.SpiritCost;
            yield return GuanzhongBasicAttackPlayModeTests.ClickPointedAttack();
            input.ConfirmCandidate();
            Assert.Less(chosen.CurrentHealth, hp);
            Assert.AreEqual(0, spawned.Enemy.CurrentHealth);
            Assert.AreEqual(ap - cost, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(mp - mana, spawned.Player.CurrentSpirit);
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
            var rings = movement.Highlights.Where(pair => pair.Value.gameObject.activeSelf).ToArray();
            Assert.Greater(rings.Length, 1, "The selected skill must show its cast range plus the target's effect cell.");
            Assert.NotNull(input.Candidate.TargetId);
            Assert.IsTrue(combat.Combatants.TryGet(input.Candidate.TargetId, out var targetUnit));
            var effectRing = rings.Single(pair => pair.Value.name ==
                "Reachable_" + targetUnit.Position.Q + "_" + targetUnit.Position.R);
            string summary = Get<Text>(Find<CombatActionPreviewView>(), "summary").text;
            StringAssert.Contains("施法范围", summary);
            StringAssert.Contains("作用范围", summary);
#if UNITY_EDITOR
            for (float deadline = Time.realtimeSinceStartup + 120f; UnityEditor.ShaderUtil.anythingCompiling && Time.realtimeSinceStartup < deadline;) yield return null;
            Assert.IsFalse(UnityEditor.ShaderUtil.anythingCompiling);
#endif
            for (int frame = 0; frame < 8; frame++) yield return null;
            string directory = Path.Combine(Application.dataPath, "../Logs/art-target");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, label + ".txt"), "resolution=" + Screen.width + "x" + Screen.height +
                "; target=" + input.Candidate.TargetId + "; profile=" + input.Candidate.ProfileId +
                "; context=" + input.Candidate.ContextVersion + "; AP=" + input.Preview.ActionPointCostUnits +
                "; MP=" + input.Preview.SpiritCost + "; castRange=" + input.Preview.MinimumRange + "-" + input.Preview.MaximumRange +
                "; rings=" + string.Join("/", rings.Select(pair => pair.Value.name)) +
                "; pointer=" + Mouse.current.position.ReadValue() +
                "; camera=" + Camera.main.transform.position + "; ring=" + effectRing.Value.name +
                "; parent=" + effectRing.Value.transform.parent.name + "; world=" + effectRing.Value.useWorldSpace +
                "; points=" + string.Join("/", Enumerable.Range(0, 6).Select(index => effectRing.Value.GetPosition(index))) +
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

        [UnityTest]
        public IEnumerator BasicPointerHoverCancelAndDirectClickAtBothResolutions()
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
                input.ConfirmCandidate();
                Assert.AreEqual(ap, spawned.Player.ActionPointsRemainingUnits);
                Vector2 confirm = RectTransformUtility.WorldToScreenPoint(null, Get<Text>(preview, "summary").transform.position);
                yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(confirm);
                Assert.IsFalse(input.Preview.CanConfirm);
                Assert.IsFalse(movement.Highlights.Values.Any(ring => ring.gameObject.activeSelf));
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
            input.ConfirmCandidate();
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
            Assert.IsTrue(encounter.IsPresenting, "One legal click must commit the move immediately.");
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
            yield return GuanzhongBasicAttackPlayModeTests.ClickPointedAttack();
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
