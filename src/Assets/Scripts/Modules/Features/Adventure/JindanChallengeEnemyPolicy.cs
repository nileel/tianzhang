using System.Collections.Generic;
using System.Linq;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Features.Adventure
{
    /// <summary>The player rehearsal adds only its current opponent's tactical orders to the usual legal melee policy.</summary>
    internal sealed class JindanChallengeEnemyPolicy : ICombatActionPolicy
    {
        private readonly JindanChallengeProgress challenge;
        private readonly LegalActionAI ordinary = new LegalActionAI();
        private readonly CombatCommandService commands = new CombatCommandService();
        private int lastTideTurn = -1, lastSiegeTurn = -1;
        private CombatSealInstance contestedGate;
        private bool approachedGate, passedGate, waitedForConcealment;

        public JindanChallengeEnemyPolicy(JindanChallengeProgress challenge) => this.challenge = challenge;

        public CombatCommand ChooseAction(CombatSession session, IReadOnlyList<CombatCommand> legal)
        {
            if (legal.Count == 0) return null;
            session.Combatants.TryGet(session.CurrentActorId, out var actor);
            if (challenge.SegmentIndex == 0)
            {
                var takeoff = legal.FirstOrDefault(c => c.Kind == CombatCommandKind.Flight && c.FlightAction == CombatFlightAction.Takeoff);
                if (takeoff != null) return takeoff;
                var attack = legal.FirstOrDefault(c => c.Kind is CombatCommandKind.Art or CombatCommandKind.BasicAttack);
                if (attack != null) return attack;
                var approach = Toward(actor, legal, new HexCoord(7, 12), JindanChallengeProgress.HighGoal);
                if (approach != null) return approach;
                // Hold the flying defender on the high terrace while the attacker approaches the objective.
                return legal.FirstOrDefault(c => c.Kind == CombatCommandKind.Guard) ??
                    legal.First(c => c.Kind == CombatCommandKind.EndTurn);
            }
            if (challenge.FireDefender)
            {
                var fire = session.Jindan.Seats.First(s => s.EffectId == CombatFireDomainRules.Effect).FireDomain;
                if (fire.Main != null && actor.EquippedArtProfileIds.Contains("demo_20261026_polei") && lastSiegeTurn != actor.OwnTurnsStarted)
                {
                    var strike = new CombatCommand(CombatCommandKind.Art, actor.Id, profileId: "demo_20261026_polei",
                        targetCell: fire.Support.Cell);
                    var quote = commands.Preview(session, strike);
                    if (quote.IsLegal && quote.AttackArea.EnvironmentTargetIds.Contains(fire.Support.Id))
                    { lastSiegeTurn = actor.OwnTurnsStarted; return strike; }
                }
                var water = session.Jindan.Seats.FirstOrDefault(s => s.Definition.ActorId == actor.Id && s.EffectId == "WATER_TIDE");
                if (water != null && lastTideTurn != actor.OwnTurnsStarted)
                    foreach (var burning in session.Surfaces.Cells.Where(c => c.Kind == CombatSurfaceKind.Burning)
                        .OrderBy(c => c.Cell.TopologicalDistanceTo(actor.Position)).ThenBy(c => c.Cell.Q).ThenBy(c => c.Cell.R))
                    {
                        var tide = new CombatCommand(CombatCommandKind.JindanAbility, actor.Id, profileId: water.EffectId,
                            jindan: new CombatJindanRequest(water.Definition.SeatId, tideAction: CombatTideAction.Rise, tideSource: burning.Cell));
                        var quote = commands.Preview(session, tide);
                        if (quote.IsLegal && quote.Tide.Changes.Any(c => c.Before == CombatSurfaceKind.Burning && c.After == CombatSurfaceKind.Wet))
                        { lastTideTurn = actor.OwnTurnsStarted; return tide; }
                    }
            }
            if (challenge.YinDefender)
            {
                var conceal = session.Jindan.Seats.First(s => s.EffectId == CombatVisibility.Yin);
                if (!waitedForConcealment && conceal.ActiveVisibility == null &&
                    session.Combatants.TryGet(conceal.Definition.ActorId, out var opponent) &&
                    session.Visibility.IsVisible(actor.Id, opponent.Id) &&
                    !legal.Any(c => c.Kind is CombatCommandKind.Art or CombatCommandKind.BasicAttack) &&
                    opponent.Position.TopologicalDistanceTo(session.Jindan.Ability(conceal.EffectId).Visibility.Center)
                        <= session.Jindan.Ability(conceal.EffectId).Range)
                {
                    // The visible opponent can reach its moon altar. Retain CT once instead of spending Yang before concealment.
                    var wait = legal.FirstOrDefault(c => c.Kind == CombatCommandKind.Wait);
                    if (wait != null) { waitedForConcealment = true; return wait; }
                }
                var yang = legal.FirstOrDefault(c => c.Kind == CombatCommandKind.JindanAbility && c.ProfileId == CombatVisibility.Yang);
                if (yang != null) return yang;
                var seal = session.Jindan.Seats.First(s => s.EffectId == "SPACE_SEAL").ActiveSeal;
                if (seal != contestedGate) { contestedGate = seal; approachedGate = passedGate = false; }
                if (seal != null && !passedGate)
                {
                    approachedGate |= actor.Position == seal.Definition.From;
                    passedGate = approachedGate && actor.Position == seal.Definition.To;
                    var approach = Toward(actor, legal, approachedGate ? seal.Definition.To : seal.Definition.From);
                    if (!passedGate && approach != null) return approach;
                }
            }
            return ordinary.ChooseAction(session, legal);
        }

        private static CombatCommand Toward(CombatantSnapshot actor, IReadOnlyList<CombatCommand> legal,
            HexCoord target, HexCoord? reserved = null) =>
            legal.Where(c => c.Kind == CombatCommandKind.Move && c.Destination != reserved &&
                    c.Destination.Value.TopologicalDistanceTo(target) < actor.Position.TopologicalDistanceTo(target))
                .OrderBy(c => c.Destination.Value.TopologicalDistanceTo(target))
                .ThenBy(c => c.Destination.Value.Q).ThenBy(c => c.Destination.Value.R).FirstOrDefault();
    }
}
