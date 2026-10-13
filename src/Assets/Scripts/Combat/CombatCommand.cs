using System;
using System.Collections.Generic;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public enum CombatCommandKind
    {
        BasicAttack,
        Art,
        Divine,
        Guard,
        Wait,
        Move,
        SwapSpell,
        EndTurn,
        SwapEquipment,
        UseItem,
    }

    /// <summary>Fixed roll values make a command replayable without a runtime random source.</summary>
    public readonly struct CombatResolutionRolls
    {
        public CombatResolutionRolls(float hitPercent, float criticalPercent, float blockPercent, float soulShieldPercent,
            float? controlRoll = null)
        {
            HitPercent = hitPercent;
            CriticalPercent = criticalPercent;
            BlockPercent = blockPercent;
            SoulShieldPercent = soulShieldPercent;
            ControlRoll = controlRoll;
        }

        public float HitPercent { get; }
        public float CriticalPercent { get; }
        public float BlockPercent { get; }
        public float SoulShieldPercent { get; }
        public float? ControlRoll { get; }
    }

    /// <summary>One target's five samples, in the quote's Ordinal TargetIds order.</summary>
    public readonly struct CombatTargetResolutionRolls
    {
        public CombatTargetResolutionRolls(string targetId, CombatResolutionRolls rolls)
        {
            TargetId = targetId;
            Rolls = rolls;
        }
        public string TargetId { get; }
        public CombatResolutionRolls Rolls { get; }
    }

    public sealed class CombatCommand
    {
        public CombatCommand(
            CombatCommandKind kind,
            string actorId,
            string targetId = null,
            string profileId = null,
            CombatResolutionRolls rolls = default,
            HexCoord? destination = null,
            int slotIndex = -1,
            long contextVersion = 0, string equipmentSlotId = null, string itemId = null, string instanceId = null,
            int itemQuantityCost = 1, int? attackDirection = null,
            IReadOnlyList<CombatTargetResolutionRolls> targetRolls = null)
        {
            if (string.IsNullOrWhiteSpace(actorId))
                throw new ArgumentException("Actor ID is required.", nameof(actorId));
            if (contextVersion < 0) throw new ArgumentOutOfRangeException(nameof(contextVersion));
            Kind = kind;
            ActorId = actorId;
            TargetId = targetId ?? string.Empty;
            ProfileId = profileId ?? string.Empty;
            Rolls = rolls;
            Destination = destination;
            SlotIndex = slotIndex;
            ContextVersion = contextVersion;
            EquipmentSlotId = equipmentSlotId ?? string.Empty;
            ItemId = itemId ?? string.Empty;
            InstanceId = instanceId ?? string.Empty;
            ItemQuantityCost = itemQuantityCost;
            AttackDirection = attackDirection;
            TargetRolls = new List<CombatTargetResolutionRolls>(targetRolls ??
                Array.Empty<CombatTargetResolutionRolls>()).AsReadOnly();
        }

        public int ItemQuantityCost { get; }
        public int? AttackDirection { get; }
        public IReadOnlyList<CombatTargetResolutionRolls> TargetRolls { get; }

        public string EquipmentSlotId { get; }
        public string ItemId { get; }
        public string InstanceId { get; }
        public CombatCommandKind Kind { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public string ProfileId { get; }
        public CombatResolutionRolls Rolls { get; }
        public HexCoord? Destination { get; }
        public int SlotIndex { get; }
        public long ContextVersion { get; }
    }
}
