#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;
using UnityEditor;

namespace TianZhang.Tests.PlayMode
{
    public sealed class D12ChallengeGoalTests
    {
        private readonly CombatCommandService commands = new CombatCommandService();
        private CombatSession session;
        private JindanChallengeProgress challenge;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void MaintainedFireAndArrivalWithoutOppositionDoNotWinDefense()
        {
            Start(1, JindanDemoCatalogData.PlayerFirePresetId);
            Until("player");
            Actor("player").SetPosition(new HexCoord(7, 11));
            Ability(CombatFireDomainRules.Effect);
            for (int i = 0; i < 4 && Seat(CombatFireDomainRules.Effect).MaintenancePayments == 0; i++)
            {
                Execute(new CombatCommand(CombatCommandKind.EndTurn, "player"));
                Until("player");
            }
            var fire = Seat(CombatFireDomainRules.Effect);
            Assert.Greater(fire.MaintenancePayments, 0);
            Assert.IsNotNull(fire.FireDomain.Main);
            Assert.IsNotEmpty(fire.FireDomain.Main.Cells);
            Execute(new CombatCommand(CombatCommandKind.Move, "player", destination: JindanChallengeProgress.HighGoal));
            Assert.AreEqual(JindanChallengeProgress.HighGoal, Actor("player").Position);
            Assert.AreEqual(CombatSessionOutcome.Ongoing, challenge.Outcome, "Maintenance and walking alone caused no hostile response.");
            // A later siege receipt while the defender stands still is opposition, but not a subsequent transfer.
            var support = fire.FireDomain.Support;
            var damage = new[] { new CombatSceneObjectChange(support.Id, "stone_support", support.Durability, 225) };
            var receipt = (CombatActionResult)typeof(CombatActionResult).GetMethod("WithSceneObjects", Private)
                .Invoke(CombatActionResult.Success(), new object[] { damage });
            Observe(new CombatCommand(CombatCommandKind.Art, JindanDemoCatalogData.EarthPresetId,
                profileId: "demo_20261026_polei", targetCell: support.Cell), receipt);
            Assert.AreEqual(CombatSessionOutcome.Ongoing, challenge.Outcome, "The defender arrived before the opposition.");
        }

        [Test]
        public void EnemyAttackWithoutADetourDoesNotCountAsASealedCrossing()
        {
            Start(2, JindanDemoCatalogData.YinPresetId);
            Until("player");
            Ability("SPACE_SEAL");
            Assert.IsNotNull(Seat("SPACE_SEAL").ActiveSeal);
            // Supply an enemy attack receipt to isolate the distinction from moving around the edge.
            var hostile = Actor(JindanDemoCatalogData.YangPresetId);
            var original = hostile.Position;
            hostile.SetPosition(Actor("player").Position.Step(1));
            var hit = new CombatCommand(CombatCommandKind.BasicAttack, hostile.Id, "player", hostile.BasicAttackProfileId);
            Observe(hit, CombatActionResult.AttackSuccess(0, new CombatDamageResult(10, true, false, false, false)));
            Assert.IsFalse((bool)typeof(JindanChallengeProgress).GetField("sealDelayed", Private).GetValue(challenge));
            Assert.AreEqual(CombatSessionOutcome.Ongoing, challenge.Outcome);
            // The opponent, not the concealed player, leaves close range. A later move from far to far is not a retreat.
            hostile.SetPosition(original);
            Observe(null, null);
            typeof(JindanChallengeProgress).GetField("sealDelayed", Private).SetValue(challenge, true);
            Execute(new CombatCommand(CombatCommandKind.Move, "player", destination: new HexCoord(8, 13)));
            Assert.IsFalse((bool)typeof(JindanChallengeProgress).GetField("retreated", Private).GetValue(challenge));
        }

        [Test]
        public void ClosedScaleBeforeAVisibleYangConflictDoesNotWinBreachingRoute()
        {
            Start(2, JindanDemoCatalogData.YangPresetId);
            Until(JindanDemoCatalogData.YinPresetId);
            Ability(CombatVisibility.Yin);
            Until("player");
            var scale = Seat("SPACE_SCALE");
            var start = Actor("player").Position;
            Execute(new CombatCommand(CombatCommandKind.JindanAbility, "player", profileId: scale.EffectId,
                jindan: new CombatJindanRequest(scale.Definition.SeatId, new[] { start, new HexCoord(5, 14) }, 1)));
            Execute(new CombatCommand(CombatCommandKind.CloseJindan, "player", profileId: scale.EffectId,
                jindan: new CombatJindanRequest(scale.Definition.SeatId)));
            Assert.Greater(scale.SuccessfulDeployments, 0);
            Assert.IsNull(scale.ActiveScale);
            // The separate route tests cover seven strikes and a real crossing; here assume those have already succeeded.
            typeof(JindanChallengeProgress).GetField("crossedBroken", Private).SetValue(challenge, true);
            Ability(CombatVisibility.Yang);
            var conflict = session.Visibility.Current;
            Assert.IsNotNull(conflict);
            session.Visibility.Skip(conflict.EventId, false);
            Observe(null, null);
            Assert.IsTrue(conflict.Visible);
            Assert.AreEqual(CombatSessionOutcome.Ongoing, challenge.Outcome, "The observation happened after scale had closed.");
        }

        private void Start(int segment, string preset)
        {
            var content = AssetDatabase.LoadAssetAtPath<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset");
            var demo = AssetDatabase.LoadAssetAtPath<JindanDemoCatalogData>("Assets/Data/JindanDemo/JindanDemoCatalog.asset");
            var environment = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_jindan_demo.asset");
            var profiles = AssetDatabase.FindAssets("t:AttackProfileData", new[] { "Assets/Data/AttackProfiles" })
                .Select(g => AssetDatabase.LoadAssetAtPath<AttackProfileData>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            var runtime = new GameRuntime();
            runtime.ConfigureGrowth(content.CombatRuleSet);
            runtime.BeginJindanDemoGame("检验", demo, preset);
            var actors = JindanRehearsalFactory.CreateForPlayer(demo, "EARTH_GRAVITY", id => id,
                runtime.Player.Capture(), segment, preset);
            var ordinary = new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0),
                new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) };
            var entry = new CombatEntryAdapter(ordinary, content.CombatRuleSet);
            Assert.IsTrue(entry.TryCreateSession(actors, profiles, environment, out session, out string reason, demo), reason);
            session.ConfigureJindan(JindanRehearsalFactory.CreateLoadout(demo, actors, "EARTH_GRAVITY"));
            session.ConfigureFlight(JindanRehearsalFactory.CreateFlight(demo, actors));
            Assert.IsTrue(environment.TryCreateDefinition(out var terrain, out reason), reason);
            session.ConfigureSurfaces(JindanRehearsalFactory.CreateSurfaces(demo, terrain, segment,
                preset == JindanDemoCatalogData.PlayerFirePresetId));
            challenge = new JindanChallengeProgress(segment, preset);
        }

        private CombatJindanSeatState Seat(string effect) => session.Jindan.Seats.Single(s => s.EffectId == effect);
        private CombatantSnapshot Actor(string id) { Assert.IsTrue(session.Combatants.TryGet(id, out var actor)); return actor; }

        private void Until(string actorId)
        {
            for (int i = 0; i < 12; i++)
            {
                if (session.TurnPhase == CombatTurnPhase.Finished || string.IsNullOrEmpty(session.CurrentActorId))
                {
                    commands.AdvanceUntilAction(session);
                    Observe(null, null);
                }
                if (session.CurrentActorId == actorId) return;
                Execute(new CombatCommand(CombatCommandKind.EndTurn, session.CurrentActorId));
            }
            Assert.Fail("Missing turn for " + actorId);
        }

        private void Ability(string effect)
        {
            var seat = Seat(effect);
            Execute(new CombatCommand(CombatCommandKind.JindanAbility, seat.Definition.ActorId, profileId: effect,
                jindan: new CombatJindanRequest(seat.Definition.SeatId)));
        }

        private void Execute(CombatCommand command)
        {
            var result = commands.Execute(session, command);
            Assert.IsTrue(result.Succeeded, command.ProfileId + " " + result.RejectionReason);
            Observe(command, result);
        }

        private void Observe(CombatCommand command, CombatActionResult result) =>
            typeof(JindanChallengeProgress).GetMethod("Observe", Private).Invoke(challenge, new object[] { session, command, result, 0 });
    }
}
#endif
