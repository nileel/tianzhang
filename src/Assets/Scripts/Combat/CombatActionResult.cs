using System;
using System.Collections.Generic;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public readonly struct CombatDamageResult
    {
        public CombatDamageResult(int finalDamage, bool isHit, bool isCritical, bool isBlocked, bool isSoulShielded)
        {
            FinalDamage = finalDamage;
            IsHit = isHit;
            IsCritical = isCritical;
            IsBlocked = isBlocked;
            IsSoulShielded = isSoulShielded;
        }

        public int FinalDamage { get; }
        public bool IsHit { get; }
        public bool IsCritical { get; }
        public bool IsBlocked { get; }
        public bool IsSoulShielded { get; }
    }

    public sealed class CombatActionResult
    {
        private CombatActionResult(
            bool succeeded,
            string rejectionReason,
            IReadOnlyList<CombatDamageResult> damage,
            IReadOnlyList<HexCoord> movementPath,
            int movementCost)
        {
            Succeeded = succeeded;
            RejectionReason = rejectionReason ?? string.Empty;
            Damage = damage ?? Array.Empty<CombatDamageResult>();
            MovementPath = movementPath == null
                ? Array.Empty<HexCoord>()
                : new List<HexCoord>(movementPath).AsReadOnly();
            MovementCost = movementCost;
        }

        public bool Succeeded { get; }
        public string RejectionReason { get; }
        public IReadOnlyList<CombatDamageResult> Damage { get; }
        public IReadOnlyList<HexCoord> MovementPath { get; }
        public int MovementCost { get; }

        public static CombatActionResult Success(params CombatDamageResult[] damage)
        {
            return new CombatActionResult(true, string.Empty, damage, null, 0);
        }

        public static CombatActionResult MovementSuccess(IReadOnlyList<HexCoord> path, int movementCost)
        {
            return new CombatActionResult(true, string.Empty, null, path, movementCost);
        }

        public static CombatActionResult Rejected(string reason)
        {
            return new CombatActionResult(false, reason, null, null, 0);
        }
    }

    public enum CombatSessionOutcome
    {
        Ongoing,
        Victory,
        Defeat,
    }

    public sealed class CombatSessionResult
    {
        public CombatSessionResult(CombatSessionOutcome outcome, IReadOnlyList<string> defeatedCombatantIds)
        {
            Outcome = outcome;
            DefeatedCombatantIds = defeatedCombatantIds ?? Array.Empty<string>();
        }

        public CombatSessionOutcome Outcome { get; }
        public IReadOnlyList<string> DefeatedCombatantIds { get; }
    }
}
