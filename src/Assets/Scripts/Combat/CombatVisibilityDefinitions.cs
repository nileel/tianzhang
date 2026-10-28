using System;
using System.Collections.Generic;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public sealed class CombatVisibilityDefinition
    {
        public CombatVisibilityDefinition(HexCoord center, string supportId, int supportRank, int carrying,
            int durationTicks, CombatConflictSettings conflict, int protectionWindowTicks, int reductionPercent,
            int reductionCap, int windowBudget)
        {
            if (string.IsNullOrWhiteSpace(supportId) || supportRank < 1 || carrying < 1 || durationTicks < 1 ||
                conflict == null || protectionWindowTicks < 1 || reductionPercent < 0 || reductionPercent > 30 ||
                reductionCap < 0 || windowBudget < 0) throw new ArgumentException("visibility_definition_invalid");
            Center = center; SupportId = supportId; SupportRank = supportRank; Carrying = carrying;
            DurationTicks = durationTicks; Conflict = conflict; ProtectionWindowTicks = protectionWindowTicks;
            ReductionPercent = reductionPercent; ReductionCap = reductionCap; WindowBudget = windowBudget;
        }
        public HexCoord Center { get; }
        public string SupportId { get; }
        public int SupportRank { get; }
        public int Carrying { get; }
        public int DurationTicks { get; }
        public CombatConflictSettings Conflict { get; }
        public int ProtectionWindowTicks { get; }
        public int ReductionPercent { get; }
        public int ReductionCap { get; }
        public int WindowBudget { get; }
    }

    public sealed class CombatConflictSettings
    {
        public CombatConflictSettings(string id, int reserve, int pulseMana, int pulseMilliseconds,
            int windowOpenMilliseconds, int windowCloseMilliseconds, int gaugeStep, int gaugeWin,
            string holdKey, string firstKey, string secondKey)
        {
            if (string.IsNullOrWhiteSpace(id) || reserve < 0 || pulseMana < 1 || pulseMilliseconds < 1 ||
                windowOpenMilliseconds < 0 || windowCloseMilliseconds <= windowOpenMilliseconds ||
                windowCloseMilliseconds >= pulseMilliseconds || gaugeStep < 1 || gaugeWin < 1 ||
                string.IsNullOrWhiteSpace(holdKey) || string.IsNullOrWhiteSpace(firstKey) || string.IsNullOrWhiteSpace(secondKey) ||
                holdKey == firstKey || holdKey == secondKey || firstKey == secondKey)
                throw new ArgumentException("conflict_settings_invalid");
            Id = id; Reserve = reserve; PulseMana = pulseMana; PulseMilliseconds = pulseMilliseconds;
            WindowOpenMilliseconds = windowOpenMilliseconds; WindowCloseMilliseconds = windowCloseMilliseconds;
            GaugeStep = gaugeStep; GaugeWin = gaugeWin; HoldKey = holdKey; FirstKey = firstKey; SecondKey = secondKey;
        }
        public string Id { get; }
        public int Reserve { get; }
        public int PulseMana { get; }
        public int PulseMilliseconds { get; }
        public int WindowOpenMilliseconds { get; }
        public int WindowCloseMilliseconds { get; }
        public int GaugeStep { get; }
        public int GaugeWin { get; }
        public string HoldKey { get; }
        public string FirstKey { get; }
        public string SecondKey { get; }
    }

    public sealed class CombatVisibilityInstance
    {
        internal CombatVisibilityInstance(CombatVisibilityDefinition definition, CombatSceneObject support, int tick,
            int paidActionPoints, int paidMana)
        {
            Definition = definition; Support = support; CreatedTick = LastPaidTick = tick;
            ExpiresTick = checked(tick + definition.DurationTicks); PaidActionPoints = paidActionPoints; PaidMana = paidMana;
        }
        public CombatVisibilityDefinition Definition { get; }
        public CombatSceneObject Support { get; }
        public int CreatedTick { get; }
        public int LastPaidTick { get; internal set; }
        public int ExpiresTick { get; }
        public int PaidActionPoints { get; }
        public int PaidMana { get; internal set; }
    }

    /// <summary>Persists across domain instances and equipment changes, for one guardian in one battle.</summary>
    public sealed class CombatVisibilityLedger
    {
        internal CombatVisibilityLedger(int reserve) => ReserveRemaining = reserve;
        public int ReserveRemaining { get; internal set; }
        public int ConflictManaSpent { get; internal set; }
        public int ProtectionWindow { get; internal set; } = -1;
        public int ProtectionUsed { get; internal set; }
        internal HashSet<string> ProtectedMembers { get; } = new HashSet<string>(StringComparer.Ordinal);
    }
}
