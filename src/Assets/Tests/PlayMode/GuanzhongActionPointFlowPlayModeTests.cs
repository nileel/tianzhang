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
            Assert.That(spawned.Enemy.ActionPointMaximumUnits, Is.EqualTo(100000));
            Assert.That(spawned.Enemy.MovementEfficiencyMilliUnits, Is.EqualTo(4000));
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
            port.Actions.Clear();
            long playerTurn = session.TurnSequence;
            encounter.RequestEndTurn("player");
            yield return WaitForNextPlayer(playerTurn);
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
            Assert.That(actions.Length, Is.LessThanOrEqualTo(spawned.Enemy.ActionPointMaximumUnits));
        }

        [UnityTest]
        public IEnumerator ControlledFormalEnemyEndsWithResidualPointsWhenNoBenefitIsLegal()
        {
            yield return EnterFormalCombat();
            // Runtime-only fixture: remove attacks and immobilize the production enemy; assets remain unchanged.
            spawned.Enemy.BasicAttackProfileId = string.Empty;
            spawned.Enemy.ApplyStatus(CombatStatusKind.Immobilize, 2);
            long playerTurn = session.TurnSequence;
            encounter.RequestEndTurn("player");
            yield return WaitForNextPlayer(playerTurn);
            ActionSample[] actions = port.Actions.Where(value => value.Actor == "enemy").ToArray();
            Assert.That(actions.Length, Is.EqualTo(1), "Guard is legal once, then no positive-cost benefit remains.");
            Assert.That(actions[0].Points, Is.EqualTo(50000));
            Assert.That(spawned.Enemy.IsGuarding, Is.True);
            TestContext.WriteLine($"Controlled formal enemy: one Guard, residualAP=50000, deterministic EndTurn, nextPlayerTurn={session.TurnSequence}; no production input or asset change.");
        }

        private CombatHudSnapshot Snapshot => Get<CombatHudSnapshot>(input, "snapshot");

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
