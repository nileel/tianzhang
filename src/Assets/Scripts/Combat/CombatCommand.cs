using System;

namespace TianZhang.Combat
{
    public enum CombatCommandKind
    {
        BasicAttack,
        Art,
        Divine,
        Guard,
        Wait,
    }

    /// <summary>Fixed roll values make a command replayable without a runtime random source.</summary>
    public readonly struct CombatResolutionRolls
    {
        public CombatResolutionRolls(float hitPercent, float criticalPercent, float blockPercent, float soulShieldPercent)
        {
            HitPercent = hitPercent;
            CriticalPercent = criticalPercent;
            BlockPercent = blockPercent;
            SoulShieldPercent = soulShieldPercent;
        }

        public float HitPercent { get; }
        public float CriticalPercent { get; }
        public float BlockPercent { get; }
        public float SoulShieldPercent { get; }
    }

    public sealed class CombatCommand
    {
        public CombatCommand(
            CombatCommandKind kind,
            string actorId,
            string targetId = null,
            string profileId = null,
            CombatResolutionRolls rolls = default)
        {
            if (string.IsNullOrWhiteSpace(actorId))
                throw new ArgumentException("Actor ID is required.", nameof(actorId));
            Kind = kind;
            ActorId = actorId;
            TargetId = targetId ?? string.Empty;
            ProfileId = profileId ?? string.Empty;
            Rolls = rolls;
        }

        public CombatCommandKind Kind { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public string ProfileId { get; }
        public CombatResolutionRolls Rolls { get; }
    }
}
