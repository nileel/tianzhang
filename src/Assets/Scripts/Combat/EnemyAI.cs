using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public static class EnemyAIProfileResolver
    {
        public const string MeleeProfileId = "ai_melee";
        public const string RangedProfileId = "ai_ranged";
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
            if (string.Equals(aiProfileId, RangedProfileId, StringComparison.Ordinal))
            {
                policy = new RangedActionAI();
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
        public CombatCommand ChooseAction(CombatSession session, IReadOnlyList<CombatCommand> legalActions)
        {
            if (legalActions == null || legalActions.Count == 0)
                return null;

            CombatCommand attack = CombatAIQuery.Pick(legalActions, CombatCommandKind.Art)
                ?? CombatAIQuery.Pick(legalActions, CombatCommandKind.Divine)
                ?? CombatAIQuery.Pick(legalActions, CombatCommandKind.BasicAttack);
            if (attack != null)
                return attack;

            CombatCommand advance = CombatAIQuery.ChooseStrictAdvance(session, legalActions);
            if (advance != null)
                return advance;

            return CombatAIQuery.Fallback(legalActions);
        }
    }

    /// <summary>
    /// Ranged policy: shoot from its own art range, step back when the target closes inside the
    /// minimum range, and otherwise close the gap without stopping inside the dead zone.
    /// </summary>
    public sealed class RangedActionAI : ICombatActionPolicy
    {
        public CombatCommand ChooseAction(CombatSession session, IReadOnlyList<CombatCommand> legalActions)
        {
            if (legalActions == null || legalActions.Count == 0)
                return null;

            CombatCommand art = CombatAIQuery.Pick(legalActions, CombatCommandKind.Art);
            if (art != null)
                return art;

            if (!CombatAIQuery.TryActor(session, legalActions, out CombatantSnapshot actor))
                return CombatAIQuery.Fallback(legalActions);

            var targets = CombatAIQuery.BuildTargets(session, actor);
            if (targets.Count == 0)
                return CombatAIQuery.Fallback(legalActions);

            int current = CombatAIQuery.PathDistanceToNearest(session, actor, actor.Position, targets);
            if (current == int.MaxValue)
                return CombatAIQuery.Fallback(legalActions);

            CombatAIQuery.GetFiringRange(session, actor, out int minimum, out int maximum);

            if (current < minimum)
            {
                CombatCommand retreat = CombatAIQuery.ChooseBestMove(session, actor, legalActions, targets,
                    distance => distance > current ? distance : int.MinValue);
                if (retreat != null)
                    return retreat;
            }
            else if (current > maximum)
            {
                CombatCommand approach = CombatAIQuery.ChooseBestMove(session, actor, legalActions, targets,
                    distance => distance >= current ? int.MinValue :
                        distance >= minimum && distance <= maximum ? 1000 + distance : -distance);
                if (approach != null)
                    return approach;
            }

            CombatCommand melee = CombatAIQuery.Pick(legalActions, CombatCommandKind.BasicAttack);
            return melee ?? CombatAIQuery.Fallback(legalActions);
        }
    }

    /// <summary>Read-only ranking helpers shared by the production enemy policies.</summary>
    internal static class CombatAIQuery
    {
        // Only a ranking bound; QueryMovement never spends AP here and the board is tiny.
        private const int ApproachBudgetUnits = int.MaxValue / 4;

        public static CombatCommand Pick(IReadOnlyList<CombatCommand> actions, CombatCommandKind kind) =>
            actions.Where(action => action.Kind == kind)
                .OrderBy(action => action.ProfileId, StringComparer.Ordinal)
                .ThenBy(action => action.TargetId, StringComparer.Ordinal)
                .ThenBy(action => action.Destination.HasValue ? action.Destination.Value.Q : int.MaxValue)
                .ThenBy(action => action.Destination.HasValue ? action.Destination.Value.R : int.MaxValue)
                .FirstOrDefault();

        public static CombatCommand Fallback(IReadOnlyList<CombatCommand> actions) =>
            Pick(actions, CombatCommandKind.Guard)
            ?? Pick(actions, CombatCommandKind.Wait)
            ?? Pick(actions, CombatCommandKind.EndTurn)
            ?? Pick(actions, CombatCommandKind.SwapSpell)
            ?? actions[0];

        public static bool TryActor(CombatSession session, IReadOnlyList<CombatCommand> actions,
            out CombatantSnapshot actor)
        {
            actor = null;
            foreach (CombatCommand action in actions)
            {
                if (session == null || action == null) return false;
                if (session.Combatants.TryGet(action.ActorId, out actor) && actor.IsAlive)
                    return true;
            }
            return false;
        }

        public static List<ApproachTarget> BuildTargets(CombatSession session, CombatantSnapshot actor)
        {
            var targets = new List<ApproachTarget>();
            foreach (CombatantSnapshot enemy in session.Combatants.All)
            {
                if (!enemy.IsAlive || enemy.Team == actor.Team) continue;
                // Other living units block the approach; the target's own cell does not.
                HexCoord[] blocked = session.Combatants.All
                    .Where(unit => unit.IsAlive && unit.Id != actor.Id && unit.Id != enemy.Id)
                    .Select(unit => unit.Position)
                    .ToArray();
                targets.Add(new ApproachTarget(enemy, blocked));
            }
            return targets;
        }

        public static void GetFiringRange(CombatSession session, CombatantSnapshot actor,
            out int minimum, out int maximum)
        {
            minimum = 1;
            maximum = 1;
            CombatAttackProfile best = null;
            foreach (CombatAttackProfile profile in session.AttackProfiles)
            {
                if (profile.Kind != CombatAttackKind.Art || !actor.EquippedArtProfileIds.Contains(profile.Id)) continue;
                if (best == null || profile.MaximumRange > best.MaximumRange) best = profile;
            }
            if (best == null) return;
            minimum = best.MinimumRange;
            maximum = best.MaximumRange;
        }

        public static CombatCommand ChooseStrictAdvance(CombatSession session, IReadOnlyList<CombatCommand> legalActions)
        {
            CombatCommand[] moves = legalActions
                .Where(action => action.Kind == CombatCommandKind.Move && action.Destination.HasValue)
                .ToArray();
            if (session == null || moves.Length == 0)
                return null;
            if (!TryActor(session, moves, out CombatantSnapshot actor))
                return null;
            var targets = BuildTargets(session, actor);
            if (targets.Count == 0)
                return null;
            int current = PathDistanceToNearest(session, actor, actor.Position, targets);
            if (current == int.MaxValue)
                return null;
            return ChooseBestMove(session, actor, legalActions, targets,
                distance => distance < current ? -distance : int.MinValue);
        }

        /// <summary>Picks the legal move with the highest score; ties keep the first admitted move.</summary>
        public static CombatCommand ChooseBestMove(
            CombatSession session,
            CombatantSnapshot actor,
            IReadOnlyList<CombatCommand> legalActions,
            IReadOnlyList<ApproachTarget> targets,
            Func<int, int> score)
        {
            CombatCommand best = null;
            int bestScore = int.MinValue;
            foreach (CombatCommand move in legalActions)
            {
                if (move.Kind != CombatCommandKind.Move || !move.Destination.HasValue) continue;
                int distance = PathDistanceToNearest(session, actor, move.Destination.Value, targets);
                if (distance == int.MaxValue) continue;
                int value = score(distance);
                if (value == int.MinValue || value <= bestScore) continue;
                bestScore = value;
                best = move;
            }
            return best;
        }

        /// <summary>
        /// Hop count of the authoritative path to the closest living hostile. Terrain and every
        /// other living unit block the route; only the target's own cell stays measurable.
        /// </summary>
        public static int PathDistanceToNearest(
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

        public sealed class ApproachTarget
        {
            public ApproachTarget(CombatantSnapshot enemy, HexCoord[] blocked)
            {
                Enemy = enemy;
                Blocked = blocked;
            }

            public CombatantSnapshot Enemy { get; }
            public HexCoord[] Blocked { get; }
        }
    }
}
