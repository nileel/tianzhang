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
    public sealed class GuanzhongCombatActionPreviewPlayModeTests
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
        private CombatRandom random;
        private AdventureSpawnSet spawned;
        private readonly System.Collections.Generic.List<string> layoutFailures = new System.Collections.Generic.List<string>();

        [SetUp]
        public void ResetLayoutFailures() => layoutFailures.Clear();

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
            saveDirectory = Path.Combine(Application.temporaryCachePath, "TianZhang-Preview-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saveDirectory));
            CharacterCreationController creation = Find<CharacterCreationController>();
            creation.Open();
            Assert.AreEqual("origin_loose", creation.Draft.OriginId);
            Assert.IsTrue(creation.SelectOrigin("origin_minor_clan"));
            Assert.That(new[] { creation.Draft.Innate.RootBone, creation.Draft.Innate.Soul, creation.Draft.Innate.DivineSense,
                creation.Draft.Innate.Aptitude, creation.Draft.Innate.Fortune }, Is.EqualTo(new[] { 8, 8, 8, 8, 8 }));
            creation.Submit("formal-preview", "行动预览验证");
            yield return SceneReady(GameplaySceneNames.World);
            bool ap2 = TestContext.CurrentContext.Test.Name.StartsWith("Ap2", StringComparison.Ordinal);
            bool unarmed = TestContext.CurrentContext.Test.Name.Contains("Unarmed");
            if (!unarmed) GuanzhongBasicAttackPlayModeTests.PrepareEquippedPlayerFixture();
            else
            {
                // New games now equip a sword. Explicitly construct this isolated unarmed fixture.
                var player = GameBootstrap.RequireRuntime().Player;
                var state = player.Capture();
                player.Restore(new TianZhang.Character.CharacterStateSnapshot(state.Identity, state.Attributes,
                    state.Resources, state.AbilityLoadout, state.Progression, state.CombatModifiers,
                    null, state.UnarmedBasicAttackProfileId, state.AppearanceProfileId,
                    state.VisibleRootId, state.VisibleRootGrade, state.VisibleRootElement,
                    unarmedAttackFamilyId: state.UnarmedAttackFamilyId));
                Assert.That(player.MainEquipmentBasicAttackProfileId, Is.Null.Or.Empty);
            }
            if (ap2)
                GameBootstrap.RequireRuntime().Player.RestoreProgression(new TianZhang.Character.CharacterProgressionSnapshot(
                    "", unarmed ? "练气" : "金丹", unarmed ? 1.5f : 6f));
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
            random = new CombatRandom();
            encounter.SetCombatResolutionRandomSource(random);
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            session = Get<CombatSession>(encounter, "session");
            spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            if (TestContext.CurrentContext.Test.Name.StartsWith("Ap2", StringComparison.Ordinal))
            {
                // Explicit temporary miss fixture keeps the production-priced repeat sequence alive.
                // Neither source template nor production random behavior is changed.
                random.MissAll = true;
                spawned.Player.DodgeRate = spawned.Enemy.DodgeRate = 100f;
            }
            else GuanzhongBasicAttackPlayModeTests.ArmEnemyOutcomeFixture(spawned.Enemy);
            for (int frame = 0; frame < 600 && !presenter.CanInspect; frame++) yield return null;
            Assert.IsTrue(presenter.CanInspect);
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
            // Keep task validation save evidence.
        }

        [UnityTest]
        public IEnumerator Ap2WeaponRepeatQuoteAndCooldownAtBothResolutions()
        {
            foreach (int width in new[] { 1920, 1366 })
            {
                if (width == 1366) { yield return Cleanup(); yield return EnterFormalEncounter(); }
                SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                var bar = Find<CombatActionBarView>();
                var input = Find<CombatCommandInput>();
                var view = Find<CombatActionPreviewView>();
                Assert.That(Get<Text>(bar, "movementLabel").text, Does.Contain("1000/1000"));
                for (int use = 0; use < 4; use++)
                {
                    if (use == 2)
                    {
                        bar.SwapEquipmentButton.onClick.Invoke();
                        Assert.That(input.Preview.CanConfirm, Is.True, input.Preview.Reason);
                        input.ConfirmCandidate();
                        yield return Ready();
                    }
                    bar.BasicAttackButton.onClick.Invoke();
                    yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);
                    AssertAp2Quote(input.Preview, CombatCommandKind.BasicAttack);
                    Assert.That(input.Preview.CommittedUseCount, Is.EqualTo(use));
                    Assert.That(input.Preview.RepeatMultiplierPermille, Is.EqualTo(use < 2 ? 1000 : use == 2 ? 1250 : 1500));
                    Assert.That(input.Preview.BasicAttackSourceKind, Is.EqualTo("main_equipment"));
                    Assert.That(input.Preview.UnarmedRealmId, Is.Empty);
                    Assert.That(input.Preview.PhysicalAttackCapacity, Is.EqualTo(120));
                    yield return CaptureAp2("weapon-" + width + "-" + (use + 1));
                    string before = State();
                    int cost = input.Preview.ActionPointCostUnits, ap = spawned.Player.ActionPointsRemainingUnits;
                    view.CancelButton.onClick.Invoke();
                    Assert.That(State(), Is.EqualTo(before));
                    bar.BasicAttackButton.onClick.Invoke();
                    yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);
                    Assert.That(input.Preview.ActionPointCostUnits, Is.EqualTo(cost));
                    Assert.That(input.Preview.CanConfirm, Is.True, input.Preview.Reason);
                    input.ConfirmCandidate();
                    Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(ap - cost));
                    yield return Ready();
                }
                for (int use = 0; use < 4; use++)
                {
                    bar.ArtButton.onClick.Invoke();
                    if (input.IsSelectingAttack) yield return GuanzhongCombatTargetSelectionPlayModeTests.PointArt(session, spawned.Enemy.Id);
                    AssertAp2Quote(input.Preview, CombatCommandKind.Art);
                    Assert.That(input.Preview.CommittedUseCount, Is.EqualTo(use));
                    Assert.That(input.Preview.RepeatMultiplierPermille, Is.EqualTo(use < 2 ? 1000 : use == 2 ? 1250 : 1500));
                    Assert.That(input.Preview.CanConfirm, Is.True, input.Preview.Reason);
                    int ap = spawned.Player.ActionPointsRemainingUnits, cost = input.Preview.ActionPointCostUnits;
                    yield return CaptureAp2("spell-" + width + "-" + (use + 1));
                    input.ConfirmCandidate();
                    Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(ap - cost));
                    yield return Ready();
                }
                bar.SecondArtButton.onClick.Invoke();
                AssertAp2Quote(input.Preview, CombatCommandKind.Art);
                input.ConfirmCandidate();
                yield return Ready();
                var cooling = presenter.Snapshot.ArtActions[1].Targets[0].Quote;
                Assert.That(cooling.RemainingCooldownTicks, Is.GreaterThan(0));
                AssertDisabledActionIsInert(bar.SecondArtButton);
                view.Present(cooling);
                yield return CaptureAp2("cooldown-" + width);
                encounter.RequestEndTurn("player");
                yield return Ready();
                bar.BasicAttackButton.onClick.Invoke();
                yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);
                Assert.That(input.Preview.CommittedUseCount, Is.Zero);
                Assert.That(input.Preview.RepeatMultiplierPermille, Is.EqualTo(1000));
            }
            Assert.That(layoutFailures, Is.Empty, string.Join("\n", layoutFailures));
        }

        [UnityTest]
        public IEnumerator Ap2UnarmedGrowthAndRejectedQuoteAtBothResolutions()
        {
            foreach (int width in new[] { 1920, 1366 })
            {
                if (width == 1366) { yield return Cleanup(); yield return EnterFormalEncounter(); }
                SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                var bar = Find<CombatActionBarView>();
                var input = Find<CombatCommandInput>();
                yield return InspectUnarmedPointerTargets();
                for (int use = 0; use < 2; use++)
                {
                    bar.BasicAttackButton.onClick.Invoke();
                    yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);
                    AssertAp2Quote(input.Preview, CombatCommandKind.BasicAttack);
                    Assert.That(input.Preview.ActionName, Is.EqualTo("拳脚"));
                    Assert.That(input.Preview.BasicAttackSourceKind, Is.EqualTo("unarmed_body"));
                    Assert.That(input.Preview.UnarmedRealmId, Is.EqualTo("realm_lianqi"));
                    Assert.That(input.Preview.PhysicalAttackCapacity, Is.EqualTo(120));
                    Assert.That(input.Preview.ActionPointCostUnits, Is.EqualTo(25000));
                    Assert.That(input.Preview.CanConfirm, Is.True, input.Preview.Reason);
                    yield return CaptureAp2("unarmed-" + width + "-" + use);
                    input.ConfirmCandidate();
                    yield return Ready();
                }
                bar.BasicAttackButton.onClick.Invoke();
                yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);
                var stale = input.Candidate;
                GameBootstrap.RequireRuntime().Player.RestoreProgression(
                    new TianZhang.Character.CharacterProgressionSnapshot("", "筑基", 3f));
                encounter.ConfirmAction(stale);
                int remaining = spawned.Player.ActionPointsRemainingUnits;
                Assert.That(remaining, Is.EqualTo(50000), "Growth neither refills AP nor confirms an old quote.");
                input.SetContext(presenter.Snapshot);
                AssertDisabledActionIsInert(bar.BasicAttackButton);
                var rejected = encounter.PreviewAction(new CombatActionCandidate(CombatActionKind.BasicAttack,
                    "player", spawned.Enemy.Id, null, presenter.Snapshot.CommandContextVersion));
                AssertAp2Quote(rejected, CombatCommandKind.BasicAttack);
                Assert.That(rejected.UnarmedRealmId, Is.EqualTo("realm_zhuji"));
                Assert.That(rejected.PhysicalAttackCapacity, Is.EqualTo(500));
                Assert.That(rejected.CommittedUseCount, Is.EqualTo(2));
                Assert.That(rejected.ActionPointCostUnits, Is.EqualTo(93750));
                Assert.That(rejected.CanConfirm, Is.False);
                Assert.That(rejected.Reason, Does.Contain("行动力不足"));
                string before = State();
                encounter.ConfirmAction(rejected.Candidate);
                Assert.That(State(), Is.EqualTo(before));
                yield return CaptureHud("ap2-growth-rejected-" + width);
                input.CancelCandidate();
                encounter.RequestEndTurn("player");
                yield return Ready();
                bar.BasicAttackButton.onClick.Invoke();
                yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);
                Assert.That(input.Preview.CommittedUseCount, Is.Zero);
                Assert.That(input.Preview.ActionPointCostUnits, Is.EqualTo(75000));
                Assert.That(input.Preview.CanConfirm, Is.True, input.Preview.Reason);
                yield return CaptureAp2("growth-next-turn-" + width);
            }
            Assert.That(layoutFailures, Is.Empty, string.Join("\n", layoutFailures));
        }

        private void AssertAp2Quote(CombatActionPreviewSnapshot quote, CombatCommandKind kind)
        {
            var authority = new CombatCommandService().Preview(session,
                new CombatCommand(kind, quote.Candidate.ActorId, quote.Candidate.TargetId, quote.Candidate.ProfileId,
                    attackDirection: quote.Candidate.AttackDirection));
            Assert.That(quote.ActionPointCostUnits, Is.EqualTo(authority.ActionPointCostUnits));
            Assert.That(quote.CommittedUseCount, Is.EqualTo(authority.CommittedUseCount));
            Assert.That(quote.RepeatMultiplierPermille, Is.EqualTo(authority.RepeatMultiplierPermille));
            Assert.That(quote.RuleSetVersion, Is.EqualTo(authority.RuleSetVersion));
            Assert.That(quote.RuleSetSourceDigest, Is.EqualTo(authority.RuleSetSourceDigest));
            Assert.That(quote.AbilityTierId, Is.EqualTo(authority.AbilityTierId));
            TestContext.WriteLine($"AP2 {Screen.width}: {kind} {quote.Candidate.ProfileId}, uses={quote.CommittedUseCount}, multiplier={quote.RepeatMultiplierPermille}, AP={quote.ActionPointCostUnits}, capacity={quote.PhysicalAttackCapacity}, legal={quote.CanConfirm}");
        }

        private IEnumerator InspectUnarmedPointerTargets()
        {
            var input = Find<CombatCommandInput>();
            var move = Find<CombatMovementView>();
            Transform board = GameObject.Find("GuanzhongBattlefield").transform;
            string before = State();
            Find<CombatActionBarView>().BasicAttackButton.onClick.Invoke();
            int legal = 0, rejected = 0;
            foreach (var enemy in spawned.Enemies)
            {
                Transform tile = board.Find("GuanzhongHex_" + enemy.Position.Q + "_" + enemy.Position.R);
                Vector2 point = Camera.main.WorldToScreenPoint(tile.position);
                Assert.IsTrue(move.TryPickCell(point, out var picked));
                Assert.AreEqual(enemy.Position.Q, picked.Q);
                Assert.AreEqual(enemy.Position.R, picked.R);
                yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(point);
                Assert.AreEqual(enemy.Id, input.Candidate.TargetId);
                Assert.IsNull(input.Candidate.AttackDirection, "Single attacks never carry a direction.");
                var authority = new CombatCommandService().Preview(session,
                    new CombatCommand(CombatCommandKind.BasicAttack, "player", enemy.Id, spawned.Player.BasicAttackProfileId));
                Assert.AreEqual(authority.IsLegal, input.Preview.CanConfirm);
                if (authority.IsLegal) legal++;
                else { rejected++; Assert.IsNotEmpty(input.Preview.Reason); }
                Assert.IsFalse(Find<CombatActionPreviewView>().ConfirmButton.interactable);
                input.ConfirmCandidate();
                Assert.AreEqual(before, State(), "Hover cannot commit either legal or rejected targets.");
            }
            Assert.Greater(legal, 0);
            Assert.Greater(rejected, 0, "The second enemy exercises the unarmed range boundary.");
            var empty = move.Highlights.Keys.First(key =>
            {
                var cell = new HexCoord(key.x, key.y);
                return session.Combatants.All.All(unit => unit.Position != cell) &&
                    move.TryPickCell(Camera.main.WorldToScreenPoint(board.Find("GuanzhongHex_" + key.x + "_" + key.y).position), out _);
            });
            yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(Camera.main.WorldToScreenPoint(
                board.Find("GuanzhongHex_" + empty.x + "_" + empty.y).position), true);
            Assert.IsFalse(input.Preview.CanConfirm);
            Assert.IsFalse(input.IsAttackLocked);
            Assert.IsNotEmpty(input.Preview.Reason);
            input.ConfirmCandidate();
            Assert.AreEqual(before, State());
            input.CancelCandidate();
        }

        [UnityTest]
        public IEnumerator Ap2FourteenCastsReportActualInputAndPresentationTime()
        {
            foreach (int width in new[] { 1920, 1366 })
            {
                if (width == 1366) { yield return Cleanup(); yield return EnterFormalEncounter(); }
                SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                var bar = Find<CombatActionBarView>();
                var input = Find<CombatCommandInput>();
                long turn = session.TurnSequence;
                int tick = session.TurnScheduler.CurrentTick, mp = spawned.Player.CurrentSpirit, paid = 0;
                float started = Time.realtimeSinceStartup, presentationSeconds = 0;
                for (int use = 0; use < 14; use++)
                {
                    bar.ArtButton.onClick.Invoke();
                    if (input.IsSelectingAttack) yield return GuanzhongCombatTargetSelectionPlayModeTests.PointArt(session, spawned.Enemy.Id);
                    Assert.That(input.Preview.CanConfirm, Is.True, input.Preview.Reason);
                    Assert.That(input.Preview.CommittedUseCount, Is.EqualTo(use));
                    AssertAp2Quote(input.Preview, CombatCommandKind.Art);
                    paid += input.Preview.ActionPointCostUnits;
                    float actionStarted = Time.realtimeSinceStartup;
                    input.ConfirmCandidate();
                    yield return Ready();
                    presentationSeconds += Time.realtimeSinceStartup - actionStarted;
                }
                Assert.That(paid, Is.EqualTo(987500));
                Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(12500));
                Assert.That(spawned.Player.CurrentSpirit, Is.EqualTo(mp - 168));
                Assert.That(session.TurnSequence, Is.EqualTo(turn));
                Assert.That(session.TurnScheduler.CurrentTick, Is.EqualTo(tick));
                AssertDisabledActionIsInert(bar.ArtButton);
                var rejected = presenter.Snapshot.ArtActions[0].Targets[0].Quote;
                Assert.That(rejected.ActionPointCostUnits, Is.EqualTo(75000));
                Find<CombatActionPreviewView>().Present(rejected);
                TestContext.WriteLine($"SCENE_INPUT width={width}, explicitJindanSample=true, casts=14, selectAndConfirmInvocations=28, basicAttacks=0, paidAP=987.5, remainingAP=12.5, paidMP=168, presentationAndFrameWaitSeconds={presentationSeconds:F3}, totalOperationSeconds={Time.realtimeSinceStartup - started:F3}, turn={turn}, tick={tick}");
                yield return CaptureAp2("fourteen-casts-" + width);
            }
            Assert.That(layoutFailures, Is.Empty, string.Join("\n", layoutFailures));
        }

        private IEnumerator CaptureAp2(string label)
        {
#if UNITY_EDITOR
            for (float deadline = Time.realtimeSinceStartup + 120f; UnityEditor.ShaderUtil.anythingCompiling && Time.realtimeSinceStartup < deadline;) yield return null;
            Assert.IsFalse(UnityEditor.ShaderUtil.anythingCompiling);
#endif
            for (int frame = 0; frame < 8; frame++) yield return null;
            Canvas.ForceUpdateCanvases();
            Text text = Get<Text>(Find<CombatActionPreviewView>(), "summary");
            var viewport = text.GetComponentInParent<ScrollRect>().viewport;
            if (text.preferredHeight > viewport.rect.height + .1f)
                layoutFailures.Add(label + ": viewport=" + viewport.rect + "; " + TextGeometry(text));
            TestContext.WriteLine(label + ": viewport=" + viewport.rect + "; " + TextGeometry(text));
            yield return CaptureHud("ap2-" + label);
        }

        [UnityTest]
        public IEnumerator HudShowsAuthoritativeCostsAndInterleavedActionsAtBothResolutions()
        {
            foreach (int width in new[] { 1920, 1366 })
            {
                if (width == 1366) { yield return Cleanup(); yield return EnterFormalEncounter(); }
                SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.That(Screen.width, Is.EqualTo(width));
                var bar = Find<CombatActionBarView>();
                var input = Find<CombatCommandInput>();
                var view = Find<CombatActionPreviewView>();
                var move = Find<CombatMovementView>();
                var service = new CombatCommandService();
                long turn = session.TurnSequence;
                int tick = session.TurnScheduler.CurrentTick;
                string initial = State();
                Assert.That(presenter.Snapshot.Movement.RemainingPoints, Is.EqualTo(spawned.Player.ActionPointsRemainingUnits));
                Assert.That(Get<Text>(bar, "movementLabel").text, Does.Contain("AP 100/100"));
                Assert.That(bar.BasicAttackButton.interactable && bar.GuardButton.interactable && bar.ArtButton.interactable, Is.True);
                Assert.That(bar.WaitButton.interactable && presenter.Snapshot.CanEndTurn, Is.True);
                bar.ArtButton.onClick.Invoke();
                if (input.IsSelectingAttack) yield return GuanzhongCombatTargetSelectionPlayModeTests.PointArt(session, spawned.Enemy.Id);
                CombatCommandPreview authority = service.Preview(session, new CombatCommand(CombatCommandKind.Art,
                    "player", "enemy", input.Candidate.ProfileId));
                Assert.That(input.Preview.ActionPointCostUnits, Is.EqualTo(authority.ActionPointCostUnits));
                Assert.That(input.Preview.SpiritCost, Is.EqualTo(authority.SpiritCost));
                Assert.That(input.Preview.CtPenalty, Is.EqualTo(authority.CtPenalty));
                Assert.That(input.Preview.ReuseCooldownTicks, Is.EqualTo(authority.ReuseCooldownTicks));
                Assert.That(Get<Text>(view, "summary").text, Does.Contain(
                    CombatActionPreviewView.FormatActionPoints(authority.ActionPointCostUnits) + " AP / " + authority.SpiritCost + " MP"));
                Assert.That(Get<Text>(timeline, "predictionText").text, Does.Contain("完成并结束"));
                Assert.That(input.Preview.Threshold, Is.EqualTo(100));
                yield return CaptureHud("initial-" + width);
                view.CancelButton.onClick.Invoke();
                bar.MoveButton.onClick.Invoke();
                Assert.That(move.IsSelecting, Is.True);
                bar.CancelMoveButton.onClick.Invoke();
                Assert.That(State(), Is.EqualTo(initial), "All preview/selection/cancel operations must be read-only.");
                CombatActionCandidate oldMove = presenter.Snapshot.Movement.Options.First().Candidate;

                if (width == 1366) yield return MoveThroughHud();
                bar.SecondArtButton.onClick.Invoke();
                Assert.That(input.Preview.ActionPointCostUnits, Is.EqualTo(25000));
                Assert.That(input.Preview.SpiritCost, Is.Zero);
                int ap = spawned.Player.ActionPointsRemainingUnits;
                var oldAction = input.Candidate;
                input.ConfirmCandidate();
                Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(ap - 25000));
                Assert.That(presenter.Snapshot.IsPresenting, Is.True);
                Assert.That(bar.ArtButton.interactable || bar.GuardButton.interactable || bar.MoveButton.interactable, Is.False);
                string busy = State();
                input.ConfirmCandidate(); encounter.ConfirmAction(oldAction); encounter.ConfirmAction(oldMove);
                Assert.That(State(), Is.EqualTo(busy));
                yield return Ready();
                AssertDisabledActionIsInert(bar.SecondArtButton);
                Assert.That(presenter.Snapshot.ArtActions[1].Targets[0].Quote.RemainingCooldownTicks, Is.EqualTo(16));
                if (width == 1920) yield return MoveThroughHud();
                Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(50000));
                Assert.That(Get<Text>(bar, "movementLabel").text, Does.Contain("AP 50/100"));
                Assert.That(presenter.Snapshot.PendingCtPenalty, Is.EqualTo(5));
                Assert.That(bar.BasicAttackButton.interactable, Is.True, "Aiming remains available even when the default direction is empty.");
                Assert.That(bar.GuardButton.interactable, Is.True);
                Assert.That(bar.WaitButton.interactable, Is.True, "End turn legality must not disable or enable other actions.");
                Assert.That(spawned.Player.GetCooldown("spell_julingshu"), Is.EqualTo(16));
                Assert.That(session.TurnSequence, Is.EqualTo(turn));
                Assert.That(session.TurnScheduler.CurrentTick, Is.EqualTo(tick));
                string settledActions = State();
                encounter.ConfirmAction(oldMove); encounter.ConfirmAction(oldAction);
                Assert.That(State(), Is.EqualTo(settledActions), "Old action and movement confirmations cannot be repriced/submitted.");
                bar.WaitButton.onClick.Invoke();
                Assert.That(input.Preview.ActionPointCostUnits, Is.Zero);
                Assert.That(input.Preview.Threshold, Is.EqualTo(105));
                yield return CaptureHud("after-actions-" + width);
                view.CancelButton.onClick.Invoke();
                Assert.That(State(), Is.EqualTo(settledActions));
                TestContext.WriteLine($"AP HUD {Screen.width}x{Screen.height}: turn={turn}, tick={tick}, AP=50000, CT penalty=5, cooldown=16; busy/cancel/stale/repeated inputs unchanged.");
            }
            Assert.That(layoutFailures, Is.Empty, string.Join("\n", layoutFailures));
        }

        private IEnumerator MoveThroughHud()
        {
            var bar = Find<CombatActionBarView>();
            var move = Find<CombatMovementView>();
            HexCoord destination = GuanzhongBasicAttackPlayModeTests.AdjacentMeleeStep(session);
            var option = presenter.Snapshot.Movement.Options.Single(value => value.Destination.Q == destination.Q && value.Destination.R == destination.R);
            CombatCommandPreview quote = new CombatCommandService().Preview(session,
                new CombatCommand(CombatCommandKind.Move, "player", destination: destination));
            Assert.That(option.Cost, Is.EqualTo(quote.ActionPointCostUnits));
            Assert.That(option.Candidate.MovementPath.Select(p => new HexCoord(p.Q, p.R)), Is.EqualTo(quote.MovementPath));
            string before = State();
            int ap = spawned.Player.ActionPointsRemainingUnits;
            bar.MoveButton.onClick.Invoke();
            Vector3 world = Get<System.Collections.Generic.Dictionary<Vector2Int, Vector3>>(move, "centers")[new Vector2Int(destination.Q, destination.R)];
            Vector2 point = Get<Camera>(move, "battlefieldCamera").WorldToScreenPoint(world);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            Assert.That(move.Selected, Is.SameAs(option));
            Assert.That(Get<Text>(bar, "movementLabel").text, Does.Contain("消耗 25 AP"));
            Assert.That(State(), Is.EqualTo(before));
            bar.ConfirmMoveButton.onClick.Invoke(); bar.ConfirmMoveButton.onClick.Invoke();
            Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(ap - quote.ActionPointCostUnits));
            Assert.That(spawned.Player.Position, Is.EqualTo(destination));
            yield return Ready();
        }

        [UnityTest]
        public IEnumerator Q1ResourceGatesAndInputStayConsistentAtBothResolutions()
        {
            foreach (int width in new[] { 1920, 1366 })
            {
                if (width == 1366) { yield return Cleanup(); yield return EnterFormalEncounter(); }
                SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Assert.AreEqual(width, Screen.width);
                Assert.AreEqual(width == 1920 ? 1080 : 768, Screen.height);
                var bar = Find<CombatActionBarView>();
                var input = Find<CombatCommandInput>();
                var initial = presenter.Snapshot;
                var boundaries = new[]
                {
                    (bar.BasicAttackButton, initial.FindPreferredAction(CombatActionKind.BasicAttack)),
                    (bar.GuardButton, initial.FindAction(CombatActionKind.Guard)),
                    (bar.SwapEquipmentButton, initial.FindPreferredAction(CombatActionKind.SwapEquipment)),
                    (bar.UseItemButton, initial.FindPreferredAction(CombatActionKind.UseItem)),
                    (bar.ArtButton, initial.ArtActions[0].Targets.First(target => target.CanExecute).Quote),
                    (bar.SecondArtButton, initial.ArtActions[1].Targets.First(target => target.CanExecute).Quote),
                };
                foreach (var (button, quote) in boundaries)
                {
                    Assert.IsTrue(quote.CanConfirm, quote.Reason);
                    Set(spawned.Player, "<ActionPointsRemainingUnits>k__BackingField", quote.ActionPointCostUnits - 1);
                    Reproject();
                    AssertDisabledActionIsInert(button);
                    Set(spawned.Player, "<ActionPointsRemainingUnits>k__BackingField", quote.ActionPointCostUnits);
                    Reproject();
                    Assert.IsTrue(button.interactable, button.name + " must accept the exact authoritative cost.");
                    Assert.IsTrue(encounter.PreviewAction(new CombatActionCandidate(quote.Candidate.Kind, "player",
                        quote.Candidate.TargetId, quote.Candidate.ProfileId, presenter.Snapshot.CommandContextVersion,
                        equipmentSlotId: quote.Candidate.EquipmentSlotId, itemId: quote.Candidate.ItemId,
                        instanceId: quote.Candidate.InstanceId, attackDirection: quote.Candidate.AttackDirection)).CanConfirm);
                }
                int moveCost = initial.Movement.Options.Min(option => option.Cost);
                Set(spawned.Player, "<ActionPointsRemainingUnits>k__BackingField", moveCost - 1);
                Reproject(); AssertDisabledActionIsInert(bar.MoveButton);
                Set(spawned.Player, "<ActionPointsRemainingUnits>k__BackingField", moveCost);
                Reproject(); Assert.IsTrue(bar.MoveButton.interactable);

                Set(spawned.Player, "<ActionPointsRemainingUnits>k__BackingField", 100000);
                int mp = spawned.Player.CurrentSpirit;
                int spiritCost = initial.ArtActions[0].Targets.First(target => target.CanExecute).SpiritCost;
                spawned.Player.SetSpirit(spawned.Player.MaximumSpirit, spiritCost - 1);
                Reproject(); AssertDisabledActionIsInert(bar.ArtButton);
                spawned.Player.SetSpirit(spawned.Player.MaximumSpirit, spiritCost);
                Reproject(); Assert.IsTrue(bar.ArtButton.interactable);
                spawned.Player.SetSpirit(spawned.Player.MaximumSpirit, 0);
                Reproject();
                Assert.IsTrue(bar.SecondArtButton.interactable, "The zero-MP self target remains legal.");
                Assert.IsTrue(bar.SecondArtTargetButton.interactable, "Target selection has no action cost.");
                bar.SecondArtTargetButton.onClick.Invoke();
                Assert.IsTrue(bar.SecondArtButton.interactable, "An unaffordable selected target cannot hide the legal self target.");
                bar.SecondArtButton.onClick.Invoke();
                Assert.AreEqual("player", input.Candidate.TargetId);
                Assert.IsTrue(input.Preview.CanConfirm);
                input.CancelCandidate();
                spawned.Player.SetSpirit(spawned.Player.MaximumSpirit, mp);
                Reproject();

                // Reach the reported 10 AP through three real, quoted, confirmed item actions.
                for (int use = 0; use < 3; use++)
                {
                    bar.UseItemButton.onClick.Invoke();
                    Assert.IsTrue(input.Preview.CanConfirm);
                    input.ConfirmCandidate();
                    yield return Ready();
                }
                Assert.AreEqual(10000, spawned.Player.ActionPointsRemainingUnits);
                Assert.AreEqual(0, GameBootstrap.RequireRuntime().GetItemQuantity("item_huoxue_dan"));
                string before = State(), saved = GameBootstrap.RequireRuntime().CaptureSaveJson();
                string log = Get<Text>(Find<CombatLogView>(), "logText").text;
                foreach (Button button in new[] { bar.BasicAttackButton, bar.ArtButton, bar.SecondArtButton,
                    bar.GuardButton, bar.MoveButton, bar.SwapSpellButton, bar.SwapEquipmentButton, bar.UseItemButton })
                {
                    AssertDisabledActionIsInert(button);
                    yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(
                        RectTransformUtility.WorldToScreenPoint(null, button.transform.position), true);
                    EventSystem.current.SetSelectedGameObject(button.gameObject);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter)); yield return null; yield return null;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); yield return null; yield return null;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                    Assert.IsNull(input.Candidate, button.name);
                    Assert.IsFalse(Find<CombatMovementView>().IsSelecting);
                    Assert.AreEqual(before, State());
                    Assert.AreEqual(saved, GameBootstrap.RequireRuntime().CaptureSaveJson());
                    Assert.AreEqual(log, Get<Text>(Find<CombatLogView>(), "logText").text);
                }
                Assert.IsTrue(bar.WaitButton.interactable);
                yield return CaptureHud("q1-disabled-10ap-" + width);
                var png = new Texture2D(2, 2);
                Assert.IsTrue(png.LoadImage(File.ReadAllBytes(Path.Combine(Application.dataPath,
                    "../Logs/ap-hud/q1-disabled-10ap-" + width + ".png"))));
                Assert.AreEqual(Screen.width, png.width); Assert.AreEqual(Screen.height, png.height);
                Object.Destroy(png);
                EventSystem.current.SetSelectedGameObject(bar.WaitButton.gameObject);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter)); yield return null; yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                Assert.AreEqual(CombatActionKind.EndTurn, input.Candidate?.Kind, "The same keyboard path can open an enabled action.");
                input.CancelCandidate();
            }
            Assert.That(layoutFailures, Is.Empty, string.Join("\n", layoutFailures));
        }

        [UnityTest]
        public IEnumerator Q1UnarmedUsesALegalAlternativeTarget()
        {
            spawned = new AdventureSpawnSet(spawned.Player, spawned.Enemies[1], spawned.EnemyData,
                spawned.PlayerBasicProfileId, spawned.EnemyBasicProfileId, spawned.PlayerDivineProfileIds,
                new[] { spawned.Enemy });
            Set(encounter, "spawned", spawned);
            Reproject();
            Assert.IsFalse(presenter.Snapshot.FindAction(CombatActionKind.BasicAttack).CanConfirm);
            Assert.IsTrue(presenter.Snapshot.FindPreferredAction(CombatActionKind.BasicAttack).CanConfirm);
            var input = Find<CombatCommandInput>();
            var button = Find<CombatActionBarView>().BasicAttackButton;
            Assert.IsTrue(button.interactable);
            string before = State();
            button.onClick.Invoke();
            yield return GuanzhongCombatTargetSelectionPlayModeTests.PointArt(session, spawned.Enemies[1].Id);
            Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
            Assert.AreEqual(spawned.Enemies[1].Id, input.Candidate.TargetId);
            input.CancelCandidate();
            Assert.AreEqual(before, State());
        }

        [UnityTest]
        public IEnumerator Q1DivineUiContractUsesExecutableQuoteAndHonorsPresentationLock()
        {
            // No production divine is equipped: exercise only the existing UI contract, not a new skill or combat rule.
            var input = Find<CombatCommandInput>();
            var bar = Find<CombatActionBarView>();
            var recorder = new DivinePreviewRecorder();
            var live = presenter.Snapshot;
            input.ConfigurePreview(recorder, Find<CombatActionPreviewView>(), timeline);
            foreach (var state in new[] { (legal: true, busy: false), (legal: false, busy: false), (legal: true, busy: true) })
            {
                input.SetContext(new CombatHudSnapshot(live.Player, live.Enemy, "合同验证", true,
                    Array.Empty<string>(), new[] { "test_divine" }, isPresenting: state.busy,
                    commandContextVersion: live.CommandContextVersion, actions: new[]
                    {
                        DivinePreviewRecorder.Quote(new CombatActionCandidate(CombatActionKind.Divine, "player",
                            "enemy", "test_divine", live.CommandContextVersion), false),
                        DivinePreviewRecorder.Quote(new CombatActionCandidate(CombatActionKind.Divine, "player",
                            "enemy_2", "test_divine", live.CommandContextVersion), state.legal),
                    }));
                int requests = recorder.Requests;
                Assert.AreEqual(state.legal && !state.busy, bar.DivineButton.interactable);
                bar.DivineButton.onClick.Invoke();
                if (state.legal && !state.busy)
                {
                    Assert.AreEqual("enemy_2", input.Candidate.TargetId);
                    Assert.AreEqual(requests + 1, recorder.Requests);
                }
                else
                {
                    Assert.IsNull(input.Candidate);
                    Assert.AreEqual(requests, recorder.Requests);
                }
            }
            input.ConfigurePreview(encounter, Find<CombatActionPreviewView>(), timeline);
            input.SetContext(live);
            yield return null;
        }

        private sealed class DivinePreviewRecorder : ICombatActionPreviewHandler
        {
            public int Requests { get; private set; }
            public CombatActionPreviewSnapshot PreviewAction(CombatActionCandidate candidate)
            { Requests++; return Quote(candidate, true); }
            public void ConfirmAction(CombatActionCandidate candidate) => Assert.Fail("This fixture must never commit a divine.");
            public static CombatActionPreviewSnapshot Quote(CombatActionCandidate candidate, bool legal) =>
                new CombatActionPreviewSnapshot(candidate, "神通合同", "player", candidate.TargetId, legal,
                    legal ? string.Empty : "不可执行", 5, true, 0, 100, 0, actionPointCostUnits: 12000);
        }

        private void Reproject() => typeof(EncounterCoordinator).GetMethod("Present", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(encounter, new object[] { "你的行动", true });

        private void AssertDisabledActionIsInert(Button button)
        {
            var input = Find<CombatCommandInput>();
            input.CancelCandidate();
            Assert.IsFalse(button.interactable);
            string before = State();
            button.onClick.Invoke();
            Assert.IsNull(input.Candidate, "Programmatic events obey the same disabled gate as UI input.");
            input.ConfirmCandidate();
            Assert.AreEqual(before, State(), "Disabled input cannot consume resources or advance combat.");
        }

        private IEnumerator CaptureHud(string label)
        {
            Canvas.ForceUpdateCanvases();
            var bar = Find<CombatActionBarView>();
            var preview = Find<CombatActionPreviewView>();
            var texts = new[] { Get<Text>(bar, "movementLabel"), Get<Text>(bar, "actionStatusLabel"),
                bar.BasicAttackButton.GetComponentInChildren<Text>(), bar.GuardButton.GetComponentInChildren<Text>(),
                bar.WaitButton.GetComponentInChildren<Text>(), bar.ArtButton.GetComponentInChildren<Text>(),
                bar.SecondArtButton.GetComponentInChildren<Text>(), Get<Text>(timeline, "predictionText") };
            foreach (Text text in texts.Where(text => text.gameObject.activeInHierarchy))
                if (text.preferredHeight > text.rectTransform.rect.height + .1f)
                    layoutFailures.Add(Screen.width + ": " + TextGeometry(text));
            string directory = Path.Combine(Application.dataPath, "../Logs/ap-hud");
            Directory.CreateDirectory(directory);
            string stem = Path.Combine(directory, label);
            File.WriteAllText(stem + ".txt", "formal scene=" + SceneManager.GetActiveScene().name +
                "; resolution=" + Screen.width + "x" + Screen.height + "; state=" + State() + "\n" +
                string.Join("\n", new[] { bar.BasicAttackButton, bar.ArtButton, bar.SecondArtButton, bar.DivineButton,
                    bar.GuardButton, bar.WaitButton, bar.MoveButton, bar.SwapSpellButton, bar.SwapEquipmentButton, bar.UseItemButton }
                    .Select(button => button.name + ": active=" + button.gameObject.activeInHierarchy + ", interactable=" + button.interactable)) + "\n" +
                string.Join("\n", texts.Select(TextGeometry)) + "\npreview=" + Get<Text>(preview, "summary").text);
            ScreenCapture.CaptureScreenshot(stem + ".png");
            for (int frame = 0; frame < 4; frame++) yield return null;
        }

        private static string TextGeometry(Text text)
        {
            string hierarchy = text.name;
            for (Transform parent = text.transform.parent; parent != null; parent = parent.parent) hierarchy = parent.name + "/" + hierarchy;
            var corners = new Vector3[4]; text.rectTransform.GetWorldCorners(corners);
            var settings = text.GetGenerationSettings(Vector2.zero);
            float compactWidth = text.cachedTextGeneratorForLayout.GetPreferredWidth("防御50AP", settings) / text.pixelsPerUnit;
            return hierarchy + "=" + text.text + "; rect=" + text.rectTransform.rect + "; preferredHeight=" + text.preferredHeight +
                "; preferredWidth=" + text.preferredWidth + "; font=" + text.font.name + "; fontSize=" + text.fontSize +
                "; compactGuardWidth=" + compactWidth + "; canvas=" + text.canvas.name + "/" + text.canvas.renderMode +
                "; scale=" + text.canvas.scaleFactor + "; screenCorners=" + string.Join("/", corners.Select(corner => corner.ToString("F2")));
        }

        public static void SetGameSize(int width, int height)
        {
#if UNITY_EDITOR
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = sizesType.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            var size = Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize"),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new object[] { Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeType"), 1), width, height, "AP HUD validation" }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
            var game = UnityEditor.EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
            game.GetType().GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, count - 1);
#else
            Screen.SetResolution(width, height, false);
#endif
        }

        [UnityTest]
        public IEnumerator AoeAuthorityChangesRejectOldQuotesBeforeRandomOrPayment()
        {
            var input = Find<CombatCommandInput>();
            var bar = Find<CombatActionBarView>();
            var move = Find<CombatMovementView>();
            IEnumerator SelectArea()
            {
                // Republish authority after each direct domain-field mutation in this stale-quote fixture.
                encounter.ConfirmAction(null);
                bar.BasicAttackButton.onClick.Invoke();
                yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);

                Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
                Assert.IsNotNull(input.Preview.AttackArea);
            }
            yield return SelectArea();
            int facing = spawned.Player.Facing;
            spawned.Player.Facing = (facing + 1) % 6;
            string before = State();
            input.ConfirmCandidate();
            Assert.AreEqual(before, State());
            Assert.IsNull(input.Candidate);
            spawned.Player.Facing = facing;

            yield return SelectArea();
            HexCoord actorOrigin = spawned.Player.Position;
            spawned.Player.SetPosition(new HexCoord(5, 5));
            before = State(); input.ConfirmCandidate(); Assert.AreEqual(before, State());
            spawned.Player.SetPosition(actorOrigin);

            yield return SelectArea();
            var candidate = input.Candidate;
            var targetId = input.Preview.AttackArea.Targets[0].Id;
            session.Combatants.TryGet(targetId, out var enemy);
            HexCoord origin = enemy.Position;
            // Isolated stale-quote fixture, separate from the ordinary formal coverage test.
            enemy.SetPosition(new HexCoord(5, 5));
            before = State(); input.ConfirmCandidate(); Assert.AreEqual(before, State());
            enemy.SetPosition(origin);
            yield return SelectArea();
            enemy.ReceiveDamage(enemy.CurrentHealth);
            before = State(); input.ConfirmCandidate(); Assert.AreEqual(before, State());
            encounter.ConfirmAction(candidate); Assert.AreEqual(before, State());

            bar.BasicAttackButton.onClick.Invoke();
            yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);
            var hud = presenter.Snapshot;
            input.SetContext(new CombatHudSnapshot(hud.Player, hud.Enemy, hud.TurnText, true,
                hud.ArtProfileIds, hud.DivineProfileIds, hud.Timeline, hud.CanEndTurn, hud.Movement,
                isPresenting: true, commandContextVersion: hud.CommandContextVersion, actions: hud.Actions));
            before = State();
            yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);
            input.ConfirmCandidate(); Assert.AreEqual(before, State());
            Assert.IsNull(input.Candidate);
            Assert.IsFalse(move.Highlights.Values.Any(ring => ring.gameObject.activeSelf));
            input.SetContext(hud);
            bar.BasicAttackButton.onClick.Invoke();
            yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);
            bar.SwapEquipmentButton.onClick.Invoke();
            Assert.AreEqual(CombatActionKind.SwapEquipment, input.Candidate.Kind);
            Assert.IsFalse(input.IsSelectingAttack);
            Assert.IsFalse(move.Highlights.Values.Any(ring => ring.gameObject.activeSelf));
            input.CancelCandidate();
            bar.BasicAttackButton.onClick.Invoke();
            yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);
            input.SetContext(null);
            Assert.IsNull(input.Candidate);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AoeCameraAndUiPointerKeepOneHighlightAndInputOwner()
        {
            SetGameSize(1366, 768);
            for (int frame = 0; frame < 8; frame++) yield return null;
            Assert.AreEqual(1366, Screen.width); Assert.AreEqual(768, Screen.height);
            var input = Find<CombatCommandInput>();
            var bar = Find<CombatActionBarView>();
            var move = Find<CombatMovementView>();
            bar.BasicAttackButton.onClick.Invoke();
            yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);

            Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
            Canvas.ForceUpdateCanvases();
            Vector3 cameraBefore = Camera.main.transform.position;
            var ringPositions = move.Highlights.Where(pair => pair.Value.gameObject.activeSelf)
                .ToDictionary(pair => pair.Key, pair => pair.Value.GetPosition(0));
            foreach (var cell in input.Preview.AttackArea.Cells)
            {
                if (!move.Highlights.TryGetValue(new Vector2Int(cell.Position.Q, cell.Position.R), out var ring)) continue;
                Transform tile = GameObject.Find("GuanzhongBattlefield").transform.Find("GuanzhongHex_" + cell.Position.Q + "_" + cell.Position.R);
                Vector3 center = Enumerable.Range(0, 6).Select(ring.GetPosition).Aggregate(Vector3.zero, (sum, point) => sum + point) / 6f;
                Assert.That(center.x, Is.EqualTo(tile.position.x).Within(.001f));
                Assert.That(center.z, Is.EqualTo(tile.position.z).Within(.001f));
            }
            string before = State();
            var button = Find<CombatActionPreviewView>().CancelButton;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
            Assert.IsFalse(move.TryPickCell(point, out _), "UI owns the preview-button pointer.");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            for (int frame = 0; frame < 6; frame++) yield return null;
            Assert.AreEqual(cameraBefore, Camera.main.transform.position);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(Screen.width * .5f, Screen.height * .52f) });
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            for (int frame = 0; frame < 6; frame++) yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            Assert.Greater(Vector3.Distance(cameraBefore, Camera.main.transform.position), .01f);
            foreach (var pair in ringPositions) Assert.AreEqual(pair.Value, move.Highlights[pair.Key].GetPosition(0));
            Debug.Log("[AOE camera] " + cameraBefore + "->" + Camera.main.transform.position + "; world rings unchanged");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left)); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            Assert.IsNull(input.Candidate);
            Assert.AreEqual(before, State());
            Assert.IsFalse(move.IsSelecting);
            input.CancelCandidate();
            bar.ArtButton.onClick.Invoke();
            yield return GuanzhongCombatTargetSelectionPlayModeTests.PointArt(session, spawned.Enemy.Id);
            Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
        }

        [UnityTest]
        public IEnumerator CandidateCancelAndTargetChangesNeverMutateAuthority()
        {
            CombatCommandInput input = Find<CombatCommandInput>();
            CombatActionBarView bar = Find<CombatActionBarView>();
            CombatActionPreviewView view = Find<CombatActionPreviewView>();
            string before = State();
            foreach (Button button in new[] { bar.BasicAttackButton, bar.ArtButton, bar.SecondArtButton, bar.GuardButton, bar.WaitButton })
            {
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    button.onClick.Invoke();
                    if (button == bar.BasicAttackButton) yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);
                    else if (input.IsSelectingAttack) yield return GuanzhongCombatTargetSelectionPlayModeTests.PointArt(session, spawned.Enemy.Id);
                    Assert.IsNotNull(input.Candidate);
                    Assert.IsTrue(view.IsVisible);
                    Assert.IsTrue(input.Preview.CanConfirm);
                    Assert.IsNotNull(timeline.Prediction);
                    presenter.InspectTarget(spawned.Enemy.Id);
                    Assert.AreEqual("player", input.Candidate.ActorId);
                    view.CancelButton.onClick.Invoke();
                    Assert.IsNull(input.Candidate);
                    Assert.IsNull(timeline.Prediction);
                    Assert.AreEqual(before, State());
                }
            }
            bar.SecondArtButton.onClick.Invoke();
            Assert.AreEqual("player", input.Candidate.TargetId);
            Assert.AreEqual(0, input.Preview.SpiritCost);
            bar.SecondArtTargetButton.onClick.Invoke();
            Assert.AreEqual("enemy", input.Candidate.TargetId);
            Assert.AreEqual(20, input.Preview.SpiritCost);
            Assert.AreEqual(before, State());
            view.CancelButton.onClick.Invoke();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ConfirmOnceAndRejectStaleOrUnaffordableCandidates()
        {
            CombatCommandInput input = Find<CombatCommandInput>();
            CombatActionBarView bar = Find<CombatActionBarView>();
            CombatActionPreviewView view = Find<CombatActionPreviewView>();
            int mp = spawned.Player.CurrentSpirit, rolls = random.Count;
            bar.ArtButton.onClick.Invoke();
            if (input.IsSelectingAttack) yield return GuanzhongCombatTargetSelectionPlayModeTests.PointArt(session, spawned.Enemy.Id);
            CombatActionCandidate old = input.Candidate;
            var hud = Get<CombatHudSnapshot>(input, "snapshot");
            Assert.IsNotNull(input.Preview, "AP-start: actor=" + session.CurrentActorId +
                "; AP=" + spawned.Player.ActionPointsRemainingUnits + "; accepts=" + hud.AcceptsCommands +
                "; canEnd=" + hud.CanEndTurn + "; artEnabled=" + bar.ArtButton.interactable);
            Assert.AreEqual(12, input.Preview.SpiritCost);
            Assert.AreEqual(100, input.Preview.Threshold);
            view.ConfirmButton.onClick.Invoke();
            view.ConfirmButton.onClick.Invoke();
            Assert.AreEqual(mp - 12, spawned.Player.CurrentSpirit);
            Assert.AreEqual(rolls + 5, random.Count);
            Assert.AreEqual(CombatTurnPhase.Active, session.TurnPhase);
            Assert.IsNull(input.Candidate);
            Assert.IsNull(timeline.Prediction);
            string after = State();
            encounter.ConfirmAction(old);
            Assert.AreEqual(after, State());
            yield return Ready();
            bar.WaitButton.onClick.Invoke();
            Assert.AreEqual(CombatActionKind.EndTurn, input.Candidate.Kind);
            CombatActionPreviewSnapshot end = input.Preview;
            view.ConfirmButton.onClick.Invoke();
            Assert.AreEqual(CombatTurnPhase.Finished, session.TurnPhase);
            var settled = session.TurnScheduler.GetSnapshot(session.Combatants.All).Units.Single(u => u.Id == "player");
            Assert.AreEqual(end.SettledCt, settled.ChargeTime);
            Assert.AreEqual(end.Threshold, settled.NextActionThreshold);
            yield return Ready();
            bar.SecondArtButton.onClick.Invoke();
            Assert.AreEqual("player", input.Candidate.TargetId);
            mp = spawned.Player.CurrentSpirit; rolls = random.Count;
            view.ConfirmButton.onClick.Invoke();
            view.ConfirmButton.onClick.Invoke();
            Assert.Greater(spawned.Player.CurrentSpirit, mp);
            Assert.AreEqual(rolls, random.Count);
            yield return Ready();
            encounter.RequestEndTurn("player");
            yield return Ready();
            // The production recovery cooldown is 16 ticks, not the historical five.
            for (int turns = 0; spawned.Player.GetCooldown("spell_julingshu") > 0 && turns < 16; turns++)
            {
                encounter.RequestEndTurn("player");
                yield return Ready();
            }
            Assert.Zero(spawned.Player.GetCooldown("spell_julingshu"));
            bar.SecondArtTargetButton.onClick.Invoke();
            bar.SecondArtButton.onClick.Invoke();
            Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
            CombatActionCandidate costly = input.Candidate;
            Assert.IsNotNull(costly);
            spawned.Player.TryConsumeSpirit(spawned.Player.CurrentSpirit);
            after = State();
            Assert.IsFalse(encounter.PreviewAction(costly).CanConfirm);
            view.ConfirmButton.onClick.Invoke();
            Assert.AreEqual(after, State());
            Assert.IsNull(input.Candidate);
        }

        [UnityTest]
        public IEnumerator MoveAndLifecycleClearOnlyUncommittedCandidates()
        {
            CombatCommandInput input = Find<CombatCommandInput>();
            CombatActionBarView bar = Find<CombatActionBarView>();
            CombatMovementView move = Find<CombatMovementView>();
            string before = State();
            bar.ArtButton.onClick.Invoke();
            if (input.IsSelectingAttack) yield return GuanzhongCombatTargetSelectionPlayModeTests.PointArt(session, spawned.Enemy.Id);
            bar.MoveButton.onClick.Invoke();
            Assert.IsNull(input.Candidate);
            Assert.IsTrue(move.IsSelecting);
            bar.SecondArtButton.onClick.Invoke();
            Assert.IsFalse(move.IsSelecting);
            Assert.IsNotNull(input.Candidate);
            input.enabled = false;
            bar.ArtButton.onClick.Invoke();
            if (input.IsSelectingAttack) yield return GuanzhongCombatTargetSelectionPlayModeTests.PointArt(session, spawned.Enemy.Id);
            bar.MoveButton.onClick.Invoke();
            input.ConfirmCandidate();
            Assert.IsFalse(move.IsSelecting);
            Assert.IsNull(input.Candidate);
            Assert.IsNull(timeline.Prediction);
            input.enabled = true;
            input.SetContext(presenter.Snapshot);
            bar.ArtButton.onClick.Invoke();
            if (input.IsSelectingAttack) yield return GuanzhongCombatTargetSelectionPlayModeTests.PointArt(session, spawned.Enemy.Id);
            presenter.enabled = false;
            Assert.IsNull(input.Candidate);
            presenter.enabled = true;
            Assert.AreEqual(before, State());
            yield return null;
        }

        [UnityTest]
        public IEnumerator HideEndTurnAndDeadTargetUseRealValidation()
        {
            CombatCommandInput input = Find<CombatCommandInput>();
            CombatActionBarView bar = Find<CombatActionBarView>();
            CombatHudSnapshot prior = presenter.Snapshot;
            bar.ArtButton.onClick.Invoke();
            if (input.IsSelectingAttack) yield return GuanzhongCombatTargetSelectionPlayModeTests.PointArt(session, spawned.Enemy.Id);
            string before = State();
            presenter.Hide();
            Assert.IsNull(input.Candidate);
            Assert.IsNull(timeline.Prediction);
            Assert.AreEqual(before, State());
            presenter.Present(prior);
            bar.WaitButton.onClick.Invoke();
            Assert.AreEqual(CombatActionKind.EndTurn, input.Candidate.Kind);
            var expected = input.Preview;
            Assert.IsNotNull(timeline.Prediction);
            StringAssert.Contains("顺位未定", Get<Text>(timeline, "predictionText").text);
            input.ConfirmCandidate();
            var settled = session.TurnScheduler.GetSnapshot(session.Combatants.All).Units.Single(u => u.Id == "player");
            Assert.AreEqual(expected.SettledCt, settled.ChargeTime);
            Assert.AreEqual(expected.Threshold, settled.NextActionThreshold);
            Assert.AreEqual(CombatTurnPhase.Finished, session.TurnPhase);
            yield return Ready();
            bar.ArtButton.onClick.Invoke();
            if (input.IsSelectingAttack) yield return GuanzhongCombatTargetSelectionPlayModeTests.PointArt(session, spawned.Enemy.Id);
            var attack = input.Candidate;
            Assert.IsNotNull(attack);
            Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
            spawned.Enemy.ReceiveDamage(spawned.Enemy.CurrentHealth);
            before = State();
            Assert.IsFalse(encounter.PreviewAction(attack).CanConfirm);
            input.ConfirmCandidate();
            Assert.IsNull(input.Candidate);
            Assert.IsNull(timeline.Prediction);
            Assert.AreEqual(before, State());
        }

        private IEnumerator Ready()
        {
            for (float deadline = Time.realtimeSinceStartup + 15f; Time.realtimeSinceStartup < deadline &&
                (session.CurrentActorId != "player" || session.TurnPhase == CombatTurnPhase.Finished || encounter.IsPresenting);) yield return null;
            yield return null;
            Assert.AreEqual("player", session.CurrentActorId);
            Assert.IsFalse(encounter.IsPresenting);
        }

        private string State()
        {
            var turns = session.TurnScheduler.GetSnapshot(session.Combatants.All);
            return session.CurrentActorId + ":" + session.TurnPhase + ":" + session.PendingCtPenalty + ":" +
                turns.CurrentTick + ":" + random.Count + ":" +
                string.Join(";", turns.Units.Select(u => u.Id + ":" + u.ChargeTime + ":" + u.NextActionThreshold + ":" + u.IsReady)) +
                string.Join(",", turns.ReadyQueueIds) + string.Join(",", turns.ChargingUnitIds) +
                string.Join(";", session.Combatants.All.Select(u => u.Id + ":" + u.CurrentHealth + ":" + u.CurrentSpirit + ":" +
                    u.ActionPointsRemainingUnits + ":" + u.Position + ":" + u.Facing + ":" + u.IsGuarding + ":" +
                    string.Join(",", u.EquippedArtProfileIds.Select(u.GetCooldown))));
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
            public int Count { get; private set; }
            public bool MissAll { get; set; }
            public float NextPercent() => Count++ % 5 == 0 && !MissAll ? 0f : 99f;
        }
#if UNITY_EDITOR
        [UnityEditor.MenuItem("天章/验证/记录行动预览现场 %&p")]
        public static void CaptureVisiblePreview()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Requires formal Play Mode.");
            var input = Find<CombatCommandInput>();
            var hud = Find<CombatHudPresenter>();
            var preview = Find<CombatActionPreviewView>();
            var summary = Get<Text>(preview, "summary");
            var coordinator = Find<EncounterCoordinator>();
            if (Get<ICombatResolutionRandomSource>(coordinator, "combatRandomSource").GetType() != typeof(SystemCombatResolutionRandomSource))
                throw new InvalidOperationException("Requires ordinary random source.");
            CombatSession live = Get<CombatSession>(coordinator, "session");
            var value = input.Preview;
            var evidence = new VisibleEvidence
            {
                width = Screen.width, height = Screen.height, tick = hud.Snapshot.Timeline.CurrentTick,
                actor = live.CurrentActorId, phase = live.TurnPhase.ToString(), viewed = hud.InspectedTargetId,
                candidate = value == null ? null : value.Candidate.Kind + ":" + value.Candidate.ProfileId + ":" + value.Candidate.TargetId,
                cost = value?.SpiritCost ?? -1, ct = value?.SettledCt ?? -1, threshold = value?.Threshold ?? -1,
                ticks = value?.TicksUntilReady ?? -1, summary = summary.text,
                textSize = summary.rectTransform.rect.size, preferredHeight = summary.preferredHeight,
                resources = string.Join(";", live.Combatants.All.Select(u => u.Id + ":HP" + u.CurrentHealth + ":MP" + u.CurrentSpirit + ":move" + u.ActionPointsRemainingUnits)),
                turns = string.Join(";", live.TurnScheduler.GetSnapshot(live.Combatants.All).Units.Select(u => u.Id + ":" + u.ChargeTime + "/" + u.NextActionThreshold)),
                previewCorners = new Vector3[4], confirmCorners = new Vector3[4], cancelCorners = new Vector3[4],
                canvasScale = summary.canvas.scaleFactor,
            };
            Get<GameObject>(preview, "root").GetComponent<RectTransform>().GetWorldCorners(evidence.previewCorners);
            preview.ConfirmButton.GetComponent<RectTransform>().GetWorldCorners(evidence.confirmCorners);
            preview.CancelButton.GetComponent<RectTransform>().GetWorldCorners(evidence.cancelCorners);
            string directory = Path.Combine(Application.dataPath, "../Logs/preview");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "visible-preview-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(evidence, true));
            Debug.Log("Read-only preview evidence: " + path);
        }
        [Serializable]
        private sealed class VisibleEvidence
        {
            public int width, height, tick, cost;
            public long ticks;
            public float ct, threshold, preferredHeight, canvasScale;
            public string actor, phase, viewed, candidate, summary, resources, turns;
            public Vector2 textSize;
            public Vector3[] previewCorners, confirmCorners, cancelCorners;
        }
#endif
    }
}
