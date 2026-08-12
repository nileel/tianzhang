using System;
using System.Collections.Generic;
using System.Linq;

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
        CombatCommand ChooseAction(IReadOnlyList<CombatCommand> legalActions);
    }

    /// <summary>Production policy with no Character, scene, resolver, or spatial dependency.</summary>
    public sealed class LegalActionAI : ICombatActionPolicy
    {
        public CombatCommand ChooseAction(IReadOnlyList<CombatCommand> legalActions)
        {
            if (legalActions == null || legalActions.Count == 0)
                return null;

            return legalActions
                .OrderBy(action => GetPriority(action.Kind))
                .ThenBy(action => action.ProfileId, StringComparer.Ordinal)
                .ThenBy(action => action.TargetId, StringComparer.Ordinal)
                .ThenBy(action => action.Destination.HasValue ? action.Destination.Value.Q : int.MaxValue)
                .ThenBy(action => action.Destination.HasValue ? action.Destination.Value.R : int.MaxValue)
                .First();
        }

        private static int GetPriority(CombatCommandKind kind)
        {
            return kind switch
            {
                CombatCommandKind.Art => 0,
                CombatCommandKind.Divine => 1,
                CombatCommandKind.BasicAttack => 2,
                CombatCommandKind.Move => 3,
                CombatCommandKind.Guard => 4,
                CombatCommandKind.Wait => 5,
                CombatCommandKind.SwapSpell => 6,
                _ => int.MaxValue,
            };
        }
    }

}
