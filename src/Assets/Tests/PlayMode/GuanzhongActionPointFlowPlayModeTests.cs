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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongActionPointFlowPlayModeTests
    {
        private string saveDirectory;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private AdventureSpawnSet spawned;
        private RecordingUnitPort port;
        private CombatCommandInput input;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return null;
            if (saveDirectory != null && Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
        }

        [UnityTest]
        public IEnumerator FormalPlayerMovesThenCastsInTheSameTurnAfterPresentation()
        {
            yield return EnterFormalCombat(qualifiedCaster: true);
            var service = new CombatCommandService();
            CombatantSnapshot player = spawned.Player;
            Assert.That(player.ActionPointMaximumUnits, Is.EqualTo(100000));
            Assert.That(player.MovementEfficiencyMilliUnits, Is.EqualTo(4000));
            // R4 halves every production enemy combat attribute, including AP and movement efficiency.
            Assert.That(spawned.Enemy.ActionPointMaximumUnits, Is.EqualTo(50000));
            Assert.That(spawned.Enemy.MovementEfficiencyMilliUnits, Is.EqualTo(2000));
            long turn = session.TurnSequence;
            int tick = session.TurnScheduler.CurrentTick;
            var stale = new CombatActionCandidate(CombatActionKind.Art, "player", "player",
                "spell_julingshu", Snapshot.CommandContextVersion);
            var move = new CombatCommand(CombatCommandKind.Move, "player", destination: new HexCoord(0, 1));
            CombatCommandPreview moveQuote = service.Preview(session, move);
            Assert.That(moveQuote.IsLegal, Is.True, moveQuote.Validation.RejectionReason);
            encounter.RequestMove("player", 0, 1);
            Assert.That(encounter.IsPresenting, Is.True);
            int afterMove = player.ActionPointsRemainingUnits;
            Assert.That(afterMove, Is.EqualTo(100000 - moveQuote.ActionPointCostUnits));
            long version = session.StateVersion;
            encounter.ConfirmAction(stale);
            encounter.RequestArt("player", "player", "spell_julingshu");
            encounter.RequestEndTurn("player");
            Assert.That(session.StateVersion, Is.EqualTo(version));
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(afterMove));
            Assert.That(Snapshot.AcceptsCommands, Is.False);
            yield return WaitForPlayer(turn);
            Assert.That(session.TurnScheduler.CurrentTick, Is.EqualTo(tick));
            Assert.That(Snapshot.CommandContextVersion, Is.GreaterThan(stale.ContextVersion));
            encounter.ConfirmAction(stale);
            Assert.That(session.StateVersion, Is.EqualTo(version));
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(afterMove));

            var candidate = new CombatActionCandidate(CombatActionKind.Art, "player", "player",
                "spell_julingshu", Snapshot.CommandContextVersion);
            Assert.That(encounter.PreviewAction(candidate).CanConfirm, Is.True, encounter.PreviewAction(candidate).Reason);
            encounter.ConfirmAction(candidate);
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(afterMove - 25000));
            Assert.That(session.TurnSequence, Is.EqualTo(turn));
            Assert.That(session.TurnScheduler.CurrentTick, Is.EqualTo(tick));
            Assert.That(session.PendingCtPenalty, Is.EqualTo(5));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(encounter.IsPresenting, Is.True);
            version = session.StateVersion;
            encounter.ConfirmAction(candidate);
            encounter.RequestMove("player", 0, 0);
            Assert.That(session.StateVersion, Is.EqualTo(version));
            yield return WaitForPlayer(turn);
            Assert.That(service.Validate(session, new CombatCommand(CombatCommandKind.Art, "player", "player",
                "spell_julingshu")).RejectionReason, Is.EqualTo("attack_profile_cooldown_active"));
            encounter.RequestEndTurn("player");
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Finished));
            TestContext.WriteLine($"Formal player: turn={turn}, tick={tick}, moveAP={moveQuote.ActionPointCostUnits}, castAP=25000, residualAP={afterMove - 25000}; busy and stale inputs rejected.");
        }

        [UnityTest]
        public IEnumerator FormalEnemyRecomputesCandidatesAndCommitsMultiplePositiveCostActions()
        {
            yield return EnterFormalCombat();
            yield return GuanzhongBasicAttackPlayModeTests.WaitForMeleeContact(encounter, session);
            yield return RunFormalEnemyTurn("enemy");
            ActionSample[] actions = port.Actions.Where(value => value.Actor == "enemy").ToArray();
            Assert.That(actions.Length, Is.GreaterThanOrEqualTo(2), "Production enemy must spend its remaining legal AP after its first action.");
            Assert.That(actions.Select(value => value.Turn).Distinct().Count(), Is.EqualTo(1));
            Assert.That(actions.Select(value => value.Tick).Distinct().Count(), Is.EqualTo(1));
            Assert.That(actions.All(value => !value.WasPresenting), Is.True, "Every enemy command waits for the previous presentation.");
            int previous = spawned.Enemy.ActionPointMaximumUnits;
            foreach (ActionSample action in actions)
            {
                Assert.That(action.Points, Is.LessThan(previous));
                previous = action.Points;
                TestContext.WriteLine($"Formal enemy: turn={action.Turn}, tick={action.Tick}, event={action.Event}, AP={action.Points}, busyBefore={action.WasPresenting}");
            }
            Assert.That(actions.Last().Points, Is.Zero,
                "The enemy spends its final AP and then ends through its own EndTurn decision.");
        }

        [UnityTest]
        public IEnumerator ControlledFormalEnemyEndsWithResidualPointsWhenNoBenefitIsLegal()
        {
            yield return EnterFormalCombat();
            // Runtime-only fixture: remove attacks and immobilize the production enemy; assets remain unchanged.
            spawned.Enemy.BasicAttackProfileId = string.Empty;
            spawned.Enemy.ApplyStatus(CombatStatusKind.Immobilize, 2);
            yield return RunFormalEnemyTurn("enemy");
            ActionSample[] actions = port.Actions.Where(value => value.Actor == "enemy").ToArray();
            Assert.That(actions.Length, Is.EqualTo(1), "Guard is legal once, then no positive-cost benefit remains.");
            Assert.That(actions[0].Points, Is.Zero);
            Assert.That(spawned.Enemy.IsGuarding, Is.True);
            TestContext.WriteLine($"Controlled formal enemy: one Guard, residualAP=0, deterministic EndTurn, nextPlayerTurn={session.TurnSequence}; no production input or asset change.");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator FormalPlayerMovesToZeroApKeepsTurnAndHudUntilManualEndAtBothResolutions()
        {
            yield return EnterFormalCombat(qualifiedCaster: true);
            var service = new CombatCommandService();
            CombatantSnapshot player = spawned.Player;
            CombatActionBarView bar = Find<CombatActionBarView>();
            CombatActionPreviewView preview = Find<CombatActionPreviewView>();
            CombatHudPresenter presenter = Find<CombatHudPresenter>();
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(100000));
            long turn = session.TurnSequence;
            int tick = session.TurnScheduler.CurrentTick;
            int ends = player.OwnTurnsEnded;
            HexCoord origin = player.Position;
            HexCoord first = FindOneEdgeStep(25000);
            yield return MoveTo(first, 25000);
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(75000));
            yield return MoveTo(origin, 25000);
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(50000));
            yield return MoveTo(first, 25000);
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(25000));

            CombatActionCandidate finishingMove = new CombatActionCandidate(CombatActionKind.Move, "player", null,
                null, Snapshot.CommandContextVersion, destination: new CombatUnitPresentationHex(origin.Q, origin.R));
            CombatActionPreviewSnapshot finishingPreview = encounter.PreviewAction(finishingMove);
            Assert.That(finishingPreview.CanConfirm, Is.True, finishingPreview.Reason);
            Assert.That(finishingPreview.RemainsInTurn, Is.True);
            preview.Present(finishingPreview, player.ActionPointsRemainingUnits);
            string finishingText = Get<Text>(preview, "summary").text;
            StringAssert.Contains("预计 0 AP", finishingText);
            StringAssert.DoesNotContain("本回合结束", finishingText);
            preview.CancelButton.onClick.Invoke();

            CombatCommand last = new CombatCommand(CombatCommandKind.Move, "player", destination: origin);
            CombatCommandPreview lastQuote = service.Preview(session, last);
            Assert.That(lastQuote.IsLegal, Is.True, lastQuote.RejectionReason);
            Assert.That(lastQuote.ActionPointCostUnits, Is.EqualTo(25000));
            encounter.RequestMove("player", origin.Q, origin.R);
            Assert.That(encounter.IsPresenting, Is.True);
            Assert.That(player.ActionPointsRemainingUnits, Is.Zero);
            Assert.That(encounter.AcceptsPlayerCommand, Is.False);
            encounter.RequestEndTurn("player");
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(player.OwnTurnsEnded, Is.EqualTo(ends));
            yield return WaitForPresentation();
            yield return WaitForPlayer(turn);

            Assert.That(player.ActionPointsRemainingUnits, Is.Zero);
            Assert.That(session.CurrentActorId, Is.EqualTo("player"));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(session.TurnSequence, Is.EqualTo(turn));
            Assert.That(session.TurnScheduler.CurrentTick, Is.EqualTo(tick));
            Assert.That(player.OwnTurnsEnded, Is.EqualTo(ends));
            yield return AssertZeroApWindowAndEnd(service, player, bar, preview, presenter,
                "f1-move-zero-ap", turn, tick, ends);
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator FormalPlayerCastsToZeroApKeepsTurnAndHudUntilManualEndAtBothResolutions()
        {
            yield return EnterFormalCombat(qualifiedCaster: true);
            yield return GuanzhongBasicAttackPlayModeTests.WaitForMeleeContact(encounter, session);
            yield return WaitForPlayer(session.TurnSequence);
            var service = new CombatCommandService();
            CombatantSnapshot player = spawned.Player;
            CombatActionBarView bar = Find<CombatActionBarView>();
            CombatActionPreviewView preview = Find<CombatActionPreviewView>();
            CombatHudPresenter presenter = Find<CombatHudPresenter>();
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(100000));
            long turn = session.TurnSequence;
            int tick = session.TurnScheduler.CurrentTick;
            int ends = player.OwnTurnsEnded;

            CombatCommand cast = new CombatCommand(CombatCommandKind.Art, "player", "enemy", "spell_guanshiji");
            CombatCommandPreview castQuote = service.Preview(session, cast);
            Assert.That(castQuote.IsLegal, Is.True, castQuote.RejectionReason);
            Assert.That(castQuote.ActionPointCostUnits, Is.EqualTo(50000));
            encounter.RequestArt("player", "enemy", "spell_guanshiji");
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(50000));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(session.TurnSequence, Is.EqualTo(turn));
            Assert.That(session.TurnScheduler.CurrentTick, Is.EqualTo(tick));
            Assert.That(player.OwnTurnsEnded, Is.EqualTo(ends));
            yield return WaitForPresentation();
            yield return WaitForPlayer(turn);

            castQuote = service.Preview(session, cast);
            Assert.That(castQuote.IsLegal, Is.True, castQuote.RejectionReason);
            Assert.That(castQuote.ActionPointCostUnits, Is.EqualTo(50000));
            encounter.RequestArt("player", "enemy", "spell_guanshiji");
            Assert.That(player.ActionPointsRemainingUnits, Is.Zero);
            Assert.That(encounter.IsPresenting, Is.True);
            Assert.That(encounter.AcceptsPlayerCommand, Is.False);
            encounter.RequestEndTurn("player");
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(player.OwnTurnsEnded, Is.EqualTo(ends));
            yield return WaitForPresentation();
            yield return WaitForPlayer(turn);

            Assert.That(player.ActionPointsRemainingUnits, Is.Zero);
            Assert.That(session.CurrentActorId, Is.EqualTo("player"));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(session.TurnSequence, Is.EqualTo(turn));
            Assert.That(session.TurnScheduler.CurrentTick, Is.EqualTo(tick));
            Assert.That(player.OwnTurnsEnded, Is.EqualTo(ends));
            yield return AssertZeroApWindowAndEnd(service, player, bar, preview, presenter,
                "f1-cast-zero-ap", turn, tick, ends);
        }

        private CombatHudSnapshot Snapshot => Get<CombatHudSnapshot>(input, "snapshot");

        private IEnumerator AssertZeroApWindowAndEnd(CombatCommandService service, CombatantSnapshot player,
            CombatActionBarView bar, CombatActionPreviewView preview, CombatHudPresenter presenter,
            string label, long turn, int tick, int ends)
        {
            Assert.That(Snapshot.AcceptsCommands, Is.True);
            Assert.That(Snapshot.CanEndTurn, Is.True);
            Assert.That(bar.WaitButton.interactable, Is.True);
            StringAssert.Contains("结束行动", bar.WaitButton.GetComponentInChildren<Text>().text);
            Assert.That(bar.MoveButton.interactable, Is.False);
            Assert.That(presenter.CanInspect, Is.True);
            presenter.InspectTarget(spawned.Enemy.Id);
            Assert.That(presenter.InspectedTargetId, Is.EqualTo(spawned.Enemy.Id));
            presenter.ClearInspectedTarget();
            Assert.That(presenter.InspectedTargetId, Is.Null);

            string before = F1State(player);
            bar.WaitButton.onClick.Invoke();
            Assert.That(input.Candidate?.Kind, Is.EqualTo(CombatActionKind.EndTurn));
            Assert.That(preview.IsVisible, Is.True);
            Assert.That(preview.ConfirmButton.gameObject.activeSelf, Is.True);
            yield return CaptureF1ZeroApEvidence(label, player, bar, preview, presenter);
            preview.CancelButton.onClick.Invoke();
            Assert.That(input.Candidate, Is.Null);
            Assert.That(F1State(player), Is.EqualTo(before));

            CombatCommandPreview guard = service.Preview(session, new CombatCommand(CombatCommandKind.Guard, player.Id));
            Assert.That(guard.IsLegal, Is.False);
            Assert.That(guard.RejectionReason, Is.EqualTo("action_points_insufficient"));
            encounter.RequestGuard(player.Id);
            Assert.That(F1State(player), Is.EqualTo(before));
            Assert.That(session.CurrentActorId, Is.EqualTo("player"));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(session.TurnSequence, Is.EqualTo(turn));
            Assert.That(session.TurnScheduler.CurrentTick, Is.EqualTo(tick));
            Assert.That(player.OwnTurnsEnded, Is.EqualTo(ends));

            bar.WaitButton.onClick.Invoke();
            Assert.That(input.Candidate?.Kind, Is.EqualTo(CombatActionKind.EndTurn));
            Assert.That(input.Preview?.CanConfirm, Is.True);
            preview.ConfirmButton.onClick.Invoke();
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Finished));
            Assert.That(player.OwnTurnsEnded, Is.EqualTo(ends + 1));
            var settled = session.TurnScheduler.GetSnapshot(session.Combatants.All).Units.Single(unit => unit.Id == player.Id);
            Assert.That(settled.ChargeTime, Is.Zero);
            Assert.That(settled.NextActionThreshold, Is.EqualTo(100));
            Assert.That(session.PendingCtPenalty, Is.Zero);
            string directory = Path.Combine(Application.dataPath, "../Logs/f1-ap-zero");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, label + "-ended.txt"),
                "actor=" + session.CurrentActorId + "; phase=" + session.TurnPhase + "; turn=" + session.TurnSequence +
                "; tick=" + session.TurnScheduler.CurrentTick + "; ownTurnsEnded=" + player.OwnTurnsEnded +
                "; AP=" + player.ActionPointsRemainingUnits + "; pendingCt=" + session.PendingCtPenalty +
                "; threshold=" + settled.NextActionThreshold + "; chargeTime=" + settled.ChargeTime);
            encounter.RequestEndTurn(player.Id);
            Assert.That(player.OwnTurnsEnded, Is.EqualTo(ends + 1));
            yield return WaitForNextPlayer(turn);
            Assert.That(player.OwnTurnsEnded, Is.EqualTo(ends + 1));
        }

        private IEnumerator CaptureF1ZeroApEvidence(string label, CombatantSnapshot player, CombatActionBarView bar,
            CombatActionPreviewView preview, CombatHudPresenter presenter)
        {
#if UNITY_EDITOR
            for (float deadline = Time.realtimeSinceStartup + 120f; UnityEditor.ShaderUtil.anythingCompiling &&
                Time.realtimeSinceStartup < deadline;) yield return null;
            Assert.That(UnityEditor.ShaderUtil.anythingCompiling, Is.False,
                "Shader compilation must finish before visual evidence.");
#endif
            string directory = Path.Combine(Application.dataPath, "../Logs/f1-ap-zero");
            Directory.CreateDirectory(directory);
            foreach (int width in new[] { 1920, 1366 })
            {
                GuanzhongCombatActionPreviewPlayModeTests.SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.That(Screen.width, Is.EqualTo(width));
                string path = Path.Combine(directory, label + "-" + width);
                File.WriteAllText(path + ".txt",
                    "resolution=" + Screen.width + "x" + Screen.height +
                    "; scene=" + SceneManager.GetActiveScene().name +
                    "; actor=" + session.CurrentActorId + "; phase=" + session.TurnPhase +
                    "; turn=" + session.TurnSequence + "; tick=" + session.TurnScheduler.CurrentTick +
                    "; ownTurnsEnded=" + player.OwnTurnsEnded + "; AP=" + player.ActionPointsRemainingUnits +
                    "; accepts=" + Snapshot.AcceptsCommands + "; canEndTurn=" + Snapshot.CanEndTurn +
                    "; waitText=" + bar.WaitButton.GetComponentInChildren<Text>().text +
                    "; waitInteractable=" + bar.WaitButton.interactable +
                    "; moveInteractable=" + bar.MoveButton.interactable +
                    "; preview=" + preview.IsVisible + "; candidate=" + input.Candidate?.Kind +
                    "; inspected=" + presenter.InspectedTargetId);
                ScreenCapture.CaptureScreenshot(path + ".png");
                for (int frame = 0; frame < 6; frame++) yield return null;
            }
        }

        private HexCoord FindOneEdgeStep(int expectedCost)
        {
            CombatantSnapshot player = spawned.Player;
            var occupied = session.Combatants.All.Where(unit => unit.IsAlive && unit.Id != player.Id)
                .Select(unit => unit.Position).ToArray();
            var service = new CombatCommandService();
            foreach (var pair in session.SpatialQuery.FindReachable(player.Position, player.ActionPointsRemainingUnits,
                         player.MovementEfficiencyMilliUnits, occupied)
                         .Where(pair => pair.Key != player.Position && pair.Value == expectedCost)
                         .OrderBy(pair => pair.Key.Q).ThenBy(pair => pair.Key.R))
            {
                CombatCommandPreview quote = service.Preview(session,
                    new CombatCommand(CombatCommandKind.Move, player.Id, destination: pair.Key));
                if (quote.IsLegal) return pair.Key;
            }
            Assert.Fail("No one-edge move quoting " + expectedCost + " AP is legal from " + player.Position + ".");
            return default;
        }

        private IEnumerator MoveTo(HexCoord destination, int expectedCost)
        {
            var service = new CombatCommandService();
            CombatCommandPreview quote = service.Preview(session,
                new CombatCommand(CombatCommandKind.Move, "player", destination: destination));
            Assert.That(quote.IsLegal, Is.True, quote.RejectionReason);
            Assert.That(quote.ActionPointCostUnits, Is.EqualTo(expectedCost));
            int before = spawned.Player.ActionPointsRemainingUnits;
            encounter.RequestMove("player", destination.Q, destination.R);
            Assert.That(spawned.Player.Position, Is.EqualTo(destination));
            Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(before - expectedCost));
            yield return WaitForPresentation();
            yield return WaitForPlayer(session.TurnSequence);
        }

        private IEnumerator WaitForPresentation()
        {
            for (float deadline = Time.realtimeSinceStartup + 10f;
                 Time.realtimeSinceStartup < deadline && encounter != null && encounter.IsPresenting;) yield return null;
            Assert.That(encounter, Is.Not.Null);
            Assert.That(encounter.IsPresenting, Is.False, "Presentation did not release its command lock.");
            yield return null;
        }

        private string F1State(CombatantSnapshot player) => session.CurrentActorId + ":" + session.TurnPhase + ":" +
            session.TurnSequence + ":" + session.TurnScheduler.CurrentTick + ":" + player.OwnTurnsEnded + ":" +
            player.ActionPointsRemainingUnits + ":" + session.PendingCtPenalty;

        private IEnumerator EnterFormalCombat(bool qualifiedCaster = false)
        {
            GameBootstrap stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return SceneReady(GameplaySceneNames.StartMenu);
            GameBootstrap bootstrap = GameBootstrap.RequireInstance();
            saveDirectory = Path.Combine(Application.temporaryCachePath, "TianZhang-AP-Flow-" + Guid.NewGuid().ToString("N"));
            Set(bootstrap, "slotStore", new GameSaveSlotStore(saveDirectory));
            CharacterCreationController creation = Find<CharacterCreationController>();
            creation.Open();
            Assert.That(creation.SelectOrigin("origin_minor_clan"), Is.True);
            creation.Submit("ap-flow", "行动力流程验证");
            yield return SceneReady(GameplaySceneNames.World);
            // Casting coverage requires the existing spell's realm qualification; production creation stays mortal.
            if (qualifiedCaster)
                GameBootstrap.RequireRuntime().Player.RestoreProgression(
                    new TianZhang.Character.CharacterProgressionSnapshot("", "练气", 1.5f));
            WorldMapController world = Find<WorldMapController>();
            Assert.That(world.SelectNode("guanzhong_hub"), Is.True);
            world.EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            SettlementController settlement = Find<SettlementController>();
            Find<BountyBoardView>().Show(Get<ContentCatalogData>(settlement, "contentCatalog"),
                "guanzhong_city", GameBootstrap.RequireRuntime().Bounties);
            Find<BountyBoardView>().SubmitAccept("bounty_guanzhong_shijiahou");
            Assert.That(settlement.EnterAdventure("guanzhong_wild"), Is.True);
            yield return SceneReady(GameplaySceneNames.Adventure);
            AdventureController adventure = Find<AdventureController>();
            for (int frame = 0; frame < 120 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.That(adventure.CurrentState, Is.EqualTo(AdventureSceneState.Exploration));
            encounter = Find<EncounterCoordinator>();
            Static3DCombatUnitPresentationProvider provider = Find<Static3DCombatUnitPresentationProvider>();
            Assert.That(Get<ICombatUnitPresentationPort>(encounter, "unitPresentation"), Is.SameAs(provider));
            port = new RecordingUnitPort(provider, () => Get<CombatSession>(encounter, "session"));
            Set(encounter, "unitPresentation", port);
            encounter.SetCombatResolutionRandomSource(new SystemCombatResolutionRandomSource(1729));
            adventure.SetEncounterRandomSource(new GuaranteedEncounter());
            Assert.That(Find<AdventureInputController>().SelectNode("shijiahou_encounter"), Is.True);
            session = Get<CombatSession>(encounter, "session");
            spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            input = Find<CombatCommandInput>();
            yield return WaitForPlayer(session.TurnSequence);
            port.Actions.Clear();
            TestContext.WriteLine($"Owner: AdventureScene/AdventureSceneInstaller/AdventureController/EncounterCoordinator/CombatSession; provider={provider.GetType().Name}; project={Application.dataPath}; Unity={Application.unityVersion}");
        }

        private IEnumerator RunFormalEnemyTurn(string enemyId)
        {
            port.Actions.Clear();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline)
            {
                Assert.That(encounter.IsRunning, Is.True, "Combat ended before the formal enemy turn began.");
                if (session.CurrentActorId == enemyId && session.TurnPhase == CombatTurnPhase.Active)
                    break;
                if (session.CurrentActorId == "player" && session.TurnPhase == CombatTurnPhase.Active &&
                    !encounter.IsPresenting && Snapshot.AcceptsCommands)
                    encounter.RequestEndTurn("player");
                yield return null;
            }
            Assert.That(session.CurrentActorId, Is.EqualTo(enemyId), "The formal enemy turn did not begin.");
            long enemyTurn = session.TurnSequence;
            bool ended = false;
            deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (session.TurnSequence > enemyTurn || session.TurnPhase == CombatTurnPhase.Finished)
                { ended = true; break; }
                Assert.That(encounter.IsRunning, Is.True, "Combat ended during the formal enemy turn.");
                yield return null;
            }
            Assert.That(ended, Is.True,
                "The enemy must end its turn through a legal command, not stall at zero AP.");
        }

        private IEnumerator WaitForNextPlayer(long previousTurn)
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline && session.TurnSequence <= previousTurn) yield return null;
            yield return WaitForPlayer(session.TurnSequence);
            Assert.That(session.TurnSequence, Is.GreaterThan(previousTurn));
        }

        private IEnumerator WaitForPlayer(long minimumTurn)
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline)
            {
                Assert.That(encounter.IsRunning, Is.True, "Combat ended before the scenario completed.");
                if (session.CurrentActorId == "player" && session.TurnSequence >= minimumTurn &&
                    !encounter.IsPresenting && Snapshot.AcceptsCommands) yield break;
                yield return null;
            }
            Assert.Fail("Formal control loop did not yield an accepting player turn.");
        }

        private static IEnumerator SceneReady(string name)
        {
            for (int frame = 0; frame < 180 && SceneManager.GetActiveScene().name != name; frame++) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(name));
            yield return null;
        }

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        private static T Get<T>(object owner, string name) => (T)owner.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);

        private sealed class GuaranteedEncounter : IFormalEncounterRandomSource
        {
            public int NextPercent() => 0;
        }

        private sealed class ActionSample
        {
            public string Actor;
            public long Turn;
            public int Tick, Points;
            public CombatUnitPresentationEvent Event;
            public bool WasPresenting;
        }

        private sealed class RecordingUnitPort : ICombatUnitPresentationPort, ICombatPresentationActivity
        {
            private readonly Static3DCombatUnitPresentationProvider actual;
            private readonly Func<CombatSession> getSession;
            public readonly List<ActionSample> Actions = new List<ActionSample>();
            public RecordingUnitPort(Static3DCombatUnitPresentationProvider actual, Func<CombatSession> getSession)
            { this.actual = actual; this.getSession = getSession; }
            public bool IsPresenting => actual.IsPresenting;
            public void Prepare(IReadOnlyList<CombatUnitPresentationDescriptor> values) => actual.Prepare(values);
            public void Spawn(CombatUnitPresentationDescriptor value) => actual.Spawn(value);
            public void Present(CombatUnitPresentationEventProjection value)
            {
                CombatSession current = getSession();
                if (current.Combatants.TryGet(value.ActorCombatantId, out CombatantSnapshot actor))
                    Actions.Add(new ActionSample { Actor = actor.Id, Turn = current.TurnSequence,
                        Tick = current.TurnScheduler.CurrentTick, Points = actor.ActionPointsRemainingUnits,
                        Event = value.PresentationEvent, WasPresenting = IsPresenting });
                actual.Present(value);
            }
            public void Remove(string id) => actual.Remove(id);
            public void Clear() => actual.Clear();
        }
    }
}
