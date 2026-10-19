using System;
using System.Collections;
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
            spawned = new AdventureSpawnSet(spawned.Player, spawned.Enemies[1], spawned.EnemyData,
                spawned.PlayerBasicProfileId, spawned.EnemyBasicProfileId, spawned.PlayerDivineProfileIds,
                new[] { spawned.Enemy });
            Set(encounter, "spawned", spawned);
            spawned.Player.Facing = Enumerable.Range(0, 6).First(direction => !service.Preview(session,
                new CombatCommand(CombatCommandKind.BasicAttack, "player", profileId: spawned.Player.BasicAttackProfileId,
                    attackDirection: direction)).IsLegal);
            typeof(EncounterCoordinator).GetMethod("Present", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(encounter, new object[] { "合法替代候选验证", true, null });
            Assert.IsFalse(presenter.Snapshot.FindAction(CombatActionKind.BasicAttack).CanConfirm);
            Assert.IsTrue(presenter.Snapshot.FindPreferredAction(CombatActionKind.BasicAttack).CanConfirm);
            Assert.IsFalse(presenter.Snapshot.ArtActions[0].Targets[0].CanExecute);
            Assert.IsTrue(presenter.Snapshot.ArtActions[0].Targets[1].CanExecute);
            Assert.IsTrue(bar.BasicAttackButton.interactable);
            Assert.IsTrue(bar.ArtButton.interactable);
            int ap = spawned.Player.ActionPointsRemainingUnits, mp = spawned.Player.CurrentSpirit;
            bar.BasicAttackButton.onClick.Invoke();
            yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session,
                presenter.Snapshot.FindPreferredAction(CombatActionKind.BasicAttack).Candidate.AttackDirection);
            Assert.IsTrue(input.Preview.CanConfirm);
            input.CancelCandidate();
            bar.ArtButton.onClick.Invoke();
            yield return PointArt(session, spawned.Enemies[1].Id);
            Assert.IsTrue(input.Preview.CanConfirm);
            Assert.AreEqual(spawned.Enemies[1].Id, input.Candidate.TargetId);
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
                new HexCoord(move.Destination.Q, move.Destination.R).Distance(enemy.Position) == 1));
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
            spawned = new AdventureSpawnSet(spawned.Player, spawned.Enemies[1], spawned.EnemyData,
                spawned.PlayerBasicProfileId, spawned.EnemyBasicProfileId, spawned.PlayerDivineProfileIds,
                new[] { spawned.Enemies[0] });
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
            Assert.AreEqual(2, presenter.Snapshot.ArtActions.Count);
            int tick = session.TurnScheduler.CurrentTick, hp = spawned.Player.CurrentHealth, mp = spawned.Player.CurrentSpirit;
            yield return Click(WorldPoint(spawned.Enemy.Id));
            AssertSelection(spawned.Enemy.Id);
            StringAssert.Contains("敌方", target.DetailsText);
            StringAssert.Contains("伤害：未探明", target.DetailsText);
            AssertMarker();
            Button[] entries = Get<Button[]>(timeline, "entryButtons");
            int playerIndex = presenter.Snapshot.Timeline.Entries.ToList().FindIndex(value => value.Id == spawned.Player.Id);
            yield return Click(RectTransformUtility.WorldToScreenPoint(null, entries[playerIndex].transform.position));
            AssertSelection(spawned.Player.Id);
            StringAssert.Contains("我方", target.DetailsText);
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
            // CT positions use stable horizontal lanes; navigation follows screen geometry, not array order.
            Selectable adjacent = entries[newPlayerIndex].FindSelectableOnRight();
            int adjacentIndex = Array.FindIndex(entries, entry => entry == adjacent);
            Assert.That(adjacentIndex, Is.GreaterThanOrEqualTo(0));
            string adjacentId = original.Entries.Reverse().ToArray()[adjacentIndex].Id;
            Assert.That(adjacentId, Is.Not.EqualTo(spawned.Player.Id));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightArrow));
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
            AssertMarker();
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
            if (id == null)
            {
                Assert.IsFalse(target.WorldMarker.gameObject.activeSelf);
                Assert.IsEmpty(target.DetailsText);
                Assert.That(Get<Text[]>(timeline, "entryLabels").All(label => !label.text.Contains("◆")), Is.True);
            }
            else StringAssert.Contains(presenter.FindInspectable(id).DisplayName, target.DetailsText);
        }

        private Vector2 WorldPoint(string id)
        {
            Assert.IsTrue(provider.TryGetVisibleBounds(id, out Bounds bounds));
            return Camera.main.WorldToScreenPoint(bounds.center);
        }

        private void AssertMarker()
        {
            Assert.IsTrue(provider.TryGetVisibleBounds(target.TargetId, out Bounds bounds));
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
            for (int index = 0; index < 8; index++)
            {
                Vector3 screen = Camera.main.WorldToScreenPoint(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((index & 1) == 0 ? -1 : 1, (index & 2) == 0 ? -1 : 1, (index & 4) == 0 ? -1 : 1)));
                min = Vector2.Min(min, screen); max = Vector2.Max(max, screen);
            }
            var corners = new Vector3[4];
            target.WorldMarker.GetWorldCorners(corners);
            Assert.That(Vector2.Distance(min, corners[0]), Is.LessThan(1f));
            Assert.That(Vector2.Distance(max, corners[2]), Is.LessThan(1f));
            Assert.IsTrue(target.WorldMarker.gameObject.activeSelf);
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
            Text detailText = Get<Text>(view, "details");
            var evidence = new VisibleEvidence
            {
                width = Screen.width, height = Screen.height, selected = hud.InspectedTargetId,
                axis = Find<CombatTimelineView>().InspectedTargetId, focus = Find<CombatTimelineView>().FocusedCombatantId,
                actor = hud.Snapshot?.Timeline?.CurrentActorId, commandTarget = Get<string>(input, "targetId"),
                details = view.DetailsText,
                canInspect = hud.CanInspect, selecting = Find<CombatMovementView>().IsSelecting,
                markerActive = view.WorldMarker.gameObject.activeSelf,
                markerCorners = new Vector3[4], tick = hud.Snapshot?.Timeline?.CurrentTick ?? -1
            };
            evidence.textSize = detailText.rectTransform.rect.size;
            evidence.preferredHeight = detailText.preferredHeight;
            evidence.selectedButton = EventSystem.current?.currentSelectedGameObject?.name;
            evidence.focusedEditor = UnityEditor.EditorWindow.focusedWindow?.GetType().Name;
            view.WorldMarker.GetWorldCorners(evidence.markerCorners);
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
            public bool canInspect, selecting, markerActive;
            public string selectedButton, focusedEditor;
            public Vector2 textSize;
            public float preferredHeight;
            public Vector3 rootPosition, boundsCenter, boundsSize;
            public Vector3[] markerCorners;
        }
#endif
    }
}
