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

            CombatAIQuery.GetFiringRange(session, actor, out int minimum, out int maximum);
            CombatCommand reposition = CombatAIQuery.ChooseApproach(session, actor, legalActions, minimum, maximum);
            if (reposition != null) return reposition;

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
            if (!session.TryGetProfile(actor.BasicAttackProfileId, out var profile)) return null;
            return ChooseApproach(session, actor, legalActions, profile.MinimumRange, profile.MaximumRange);
        }

        /// <summary>Walk toward an actual legal firing pose, keeping both body cells clear.
        /// Ranking uses the same AP path and contact metric as execution, including detours and retreat.</summary>
        public static CombatCommand ChooseApproach(
            CombatSession session,
            CombatantSnapshot actor,
            IReadOnlyList<CombatCommand> legalActions,
            int minimum, int maximum)
        {
            var moves = legalActions.Where(action => action.Kind == CombatCommandKind.Move).ToArray();
            if (moves.Length == 0) return null;
            var targets = session.Combatants.All.Where(unit => unit.IsAlive && unit.Team != actor.Team)
                .OrderBy(unit => unit.Id, StringComparer.Ordinal).ToArray();
            bool CanEngage(SpatialPose pose) => targets.Any(target =>
                session.SpatialQuery.QueryRange(pose, target.Pose, minimum, maximum).IsInRange);
            if (targets.Length == 0 || CanEngage(actor.Pose)) return null;
            var occupied = session.GetOccupiedPositionsExcept(actor.Id);
            var reachable = session.SpatialQuery.FindReachable(actor.Pose, ApproachBudgetUnits,
                actor.MovementEfficiencyMilliUnits, occupied);
            foreach (var entry in reachable.OrderBy(pair => pair.Value).ThenBy(pair => pair.Key.Anchor.Q)
                         .ThenBy(pair => pair.Key.Anchor.R).ThenBy(pair => pair.Key.Facing))
            {
                if (entry.Key.Anchor == actor.Position || !CanEngage(entry.Key)) continue;
                CombatMovementQueryResult route = session.SpatialQuery.QueryMovement(actor.Pose, entry.Key.Anchor,
                    ApproachBudgetUnits, actor.MovementEfficiencyMilliUnits, occupied, entry.Key.Facing);
                if (!route.IsReachable) continue;
                CombatCommand best = null;
                foreach (SpatialMovementStep step in route.Steps)
                {
                    var admitted = moves.FirstOrDefault(move => move.Destination == step.To.Anchor &&
                        move.DestinationFacing == step.To.Facing);
                    if (admitted == null) break;
                    best = admitted;
                }
                return best;
            }
            return null;
        }
    }
}
