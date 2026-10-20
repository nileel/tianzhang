using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public static class EnemyAIProfileResolver
    {
        public const string MeleeProfileId = "ai_melee";
        public const string UnknownProfileReason = "formal_enemy_ai_profile_unknown";

        public static bool TryResolveCombatActionPolicy(
            string aiProfileId,
            out ICombatActionPolicy policy,
            out string reason)
        {
            if (string.Equals(aiProfileId, MeleeProfileId, StringComparison.Ordinal))
            {
                policy = new LegalActionAI();
                reason = string.Empty;
                return true;
            }

            policy = null;
            reason = UnknownProfileReason;
            return false;
        }
    }

    /// <summary>AI receives only commands already admitted by Combat's legal-action service.</summary>
    public interface ICombatActionPolicy
    {
        /// <summary>
        /// Chooses one admitted command. The session is read-only observation used to rank
        /// approach steps; it is never mutated and never bypasses the legal-action boundary.
        /// </summary>
        CombatCommand ChooseAction(CombatSession session, IReadOnlyList<CombatCommand> legalActions);
    }

    /// <summary>
    /// Production melee policy: take a legal attack first, otherwise advance along the true
    /// path distance to the nearest living hostile and never wander away from it.
    /// </summary>
    public sealed class LegalActionAI : ICombatActionPolicy
    {
        // Only a ranking bound; QueryMovement never spends AP here and the board is tiny.
        private const int ApproachBudgetUnits = int.MaxValue / 4;

        public CombatCommand ChooseAction(CombatSession session, IReadOnlyList<CombatCommand> legalActions)
        {
            if (legalActions == null || legalActions.Count == 0)
                return null;

            CombatCommand attack = Pick(legalActions, CombatCommandKind.Art)
                ?? Pick(legalActions, CombatCommandKind.Divine)
                ?? Pick(legalActions, CombatCommandKind.BasicAttack);
            if (attack != null)
                return attack;

            CombatCommand advance = ChooseAdvance(session, legalActions);
            if (advance != null)
                return advance;

            return Pick(legalActions, CombatCommandKind.Guard)
                ?? Pick(legalActions, CombatCommandKind.Wait)
                ?? Pick(legalActions, CombatCommandKind.EndTurn)
                ?? Pick(legalActions, CombatCommandKind.SwapSpell)
                ?? legalActions[0];
        }

        private static CombatCommand Pick(IReadOnlyList<CombatCommand> actions, CombatCommandKind kind) =>
            actions.Where(action => action.Kind == kind)
                .OrderBy(action => action.ProfileId, StringComparer.Ordinal)
                .ThenBy(action => action.TargetId, StringComparer.Ordinal)
                .ThenBy(action => action.Destination.HasValue ? action.Destination.Value.Q : int.MaxValue)
                .ThenBy(action => action.Destination.HasValue ? action.Destination.Value.R : int.MaxValue)
                .FirstOrDefault();

        private static CombatCommand ChooseAdvance(CombatSession session, IReadOnlyList<CombatCommand> legalActions)
        {
            CombatCommand[] moves = legalActions
                .Where(action => action.Kind == CombatCommandKind.Move && action.Destination.HasValue)
                .ToArray();
            if (session == null || moves.Length == 0)
                return null;
            if (!session.Combatants.TryGet(moves[0].ActorId, out CombatantSnapshot actor) || !actor.IsAlive)
                return null;

            CombatantSnapshot[] enemies = session.Combatants.All
                .Where(unit => unit.IsAlive && unit.Team != actor.Team)
                .ToArray();
            if (enemies.Length == 0)
                return null;

            var targets = new List<ApproachTarget>(enemies.Length);
            foreach (CombatantSnapshot enemy in enemies)
            {
                // Other living units block the approach; the target's own cell does not.
                HexCoord[] blocked = session.Combatants.All
                    .Where(unit => unit.IsAlive && unit.Id != actor.Id && unit.Id != enemy.Id)
                    .Select(unit => unit.Position)
                    .ToArray();
                targets.Add(new ApproachTarget(enemy, blocked));
            }

            int current = PathDistanceToNearest(session, actor, actor.Position, targets);
            if (current == int.MaxValue)
                return null;

            CombatCommand best = null;
            int bestDistance = current;
            foreach (CombatCommand move in moves)
            {
                int distance = PathDistanceToNearest(session, actor, move.Destination.Value, targets);
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                best = move;
            }
            return best;
        }

        private sealed class ApproachTarget
        {
            public ApproachTarget(CombatantSnapshot enemy, HexCoord[] blocked)
            {
                Enemy = enemy;
                Blocked = blocked;
            }

            public CombatantSnapshot Enemy { get; }
            public HexCoord[] Blocked { get; }
        }

        /// <summary>
        /// Hop count of the authoritative path to the closest living hostile. Terrain and every
        /// other living unit block the route; only the target's own cell stays measurable.
        /// </summary>
        private static int PathDistanceToNearest(
            CombatSession session,
            CombatantSnapshot actor,
            HexCoord origin,
            IReadOnlyList<ApproachTarget> targets)
        {
            int best = int.MaxValue;
            foreach (ApproachTarget target in targets)
            {
                CombatMovementQueryResult path = session.SpatialQuery.QueryMovement(
                    origin, target.Enemy.Position, ApproachBudgetUnits, actor.MovementEfficiencyMilliUnits,
                    target.Blocked);
                int distance = path.IsReachable && path.Path.Count > 0 ? path.Path.Count : int.MaxValue;
                if (distance < best)
                    best = distance;
            }
            return best;
        }
    }
}
