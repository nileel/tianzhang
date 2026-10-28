using System;
using System.Collections.Generic;

namespace TianZhang.Combat
{
    public enum CombatConflictOutcome { Pending, Left, Right, Neutral, Cancelled }

    public readonly struct CombatConflictPriority
    {
        public CombatConflictPriority(bool permission, int positionRank, int supportRank, int paidActionPoints, int paidMana, int carrying)
        { Permission = permission; PositionRank = positionRank; SupportRank = supportRank; PaidActionPoints = paidActionPoints; PaidMana = paidMana; Carrying = carrying; }
        public bool Permission { get; }
        public int PositionRank { get; }
        public int SupportRank { get; }
        public int PaidActionPoints { get; }
        public int PaidMana { get; }
        public int Carrying { get; }
        public static int Compare(CombatConflictPriority left, CombatConflictPriority right, out string step)
        {
            step = "permission"; int comparison = left.Permission.CompareTo(right.Permission);
            if (comparison != 0) return comparison;
            step = "position"; comparison = left.PositionRank.CompareTo(right.PositionRank);
            if (comparison != 0) return comparison;
            step = "support"; comparison = left.SupportRank.CompareTo(right.SupportRank);
            if (comparison != 0) return comparison;
            step = "paid_cost";
            int ap = left.PaidActionPoints.CompareTo(right.PaidActionPoints), mp = left.PaidMana.CompareTo(right.PaidMana);
            // No conversion rate exists between AP and MP. Mixed opposing costs are outside this demo contract.
            if (ap != 0 && mp != 0 && ap != mp) throw new ArgumentException("conflict_costs_not_comparable");
            if (ap != 0 || mp != 0) return ap != 0 ? ap : mp;
            step = "carrying"; comparison = left.Carrying.CompareTo(right.Carrying);
            if (comparison != 0) return comparison;
            step = "equal"; return 0;
        }
    }

    /// <summary>Only this event can spend the two guardian reserves; elapsed time is independent of combat ticks.</summary>
    public sealed class CombatVisibilityConflict
    {
        private readonly CombatantSnapshot leftActor, rightActor;
        private readonly CombatJindanSeatState leftSeat, rightSeat;
        private readonly List<string> log = new List<string>();
        private long startedAt = -1, lastTime = -1;
        private bool windowConsumed;
        public CombatVisibilityConflict(long eventId, string queryKey, string observerId, string targetId,
            CombatantSnapshot leftActor, CombatJindanSeatState leftSeat, CombatantSnapshot rightActor,
            CombatJindanSeatState rightSeat, CombatConflictSettings settings,
            CombatConflictPriority leftPriority, CombatConflictPriority rightPriority, bool baseVisible)
        {
            EventId = eventId; QueryKey = queryKey; ObserverId = observerId; TargetId = targetId;
            this.leftActor = leftActor; this.rightActor = rightActor; this.leftSeat = leftSeat; this.rightSeat = rightSeat;
            Settings = settings; BaseVisible = baseVisible;
            log.Add("query:" + queryKey + ":" + observerId + ":" + targetId);
            log.Add("sources:" + leftSeat.Definition.AbilityInstanceId + ":" + rightSeat.Definition.AbilityInstanceId);
            log.Add("priorities:" + Facts(leftPriority) + "|" + Facts(rightPriority));
            PlayerSide = leftActor.Team == CombatTeam.Player ? 1 : rightActor.Team == CombatTeam.Player ? 2 : 0;
            int comparison = CombatConflictPriority.Compare(leftPriority, rightPriority, out string step);
            PriorityStep = step;
            log.Add("priority:" + step + ":" + comparison);
            if (comparison != 0) Finish(comparison > 0 ? CombatConflictOutcome.Left : CombatConflictOutcome.Right, step);
            else if (LeftAvailable < Settings.PulseMana && RightAvailable < Settings.PulseMana)
                Finish(CombatConflictOutcome.Neutral, "both_cannot_pay");
        }
        public long EventId { get; }
        public string QueryKey { get; }
        public string ObserverId { get; }
        public string TargetId { get; }
        public string PriorityStep { get; }
        public CombatConflictSettings Settings { get; }
        public bool BaseVisible { get; }
        public int PlayerSide { get; }
        public int PulseCount { get; private set; }
        public int Gauge { get; private set; }
        public int LeftSpent { get; private set; }
        public int RightSpent { get; private set; }
        public int LeftAvailable => Math.Min(leftActor.CurrentSpirit, leftSeat.VisibilityLedger.ReserveRemaining);
        public int RightAvailable => Math.Min(rightActor.CurrentSpirit, rightSeat.VisibilityLedger.ReserveRemaining);
        public bool Started => startedAt >= 0;
        public CombatConflictOutcome Outcome { get; private set; }
        public bool IsPending => Outcome == CombatConflictOutcome.Pending;
        public bool Visible => Outcome == CombatConflictOutcome.Left || Outcome == CombatConflictOutcome.Neutral && BaseVisible;
        public string EndReason { get; private set; } = string.Empty;
        public IReadOnlyList<string> Log => log.AsReadOnly();
        public string ExpectedKey => PulseCount % 2 == 1 ? Settings.FirstKey : Settings.SecondKey;
        public bool WindowOpen { get; private set; }
        private static string Facts(CombatConflictPriority value) => value.Permission + "," + value.PositionRank + "," +
            value.SupportRank + "," + value.PaidActionPoints + "AP/" + value.PaidMana + "MP," + value.Carrying;

        public void Input(long eventId, long timeMilliseconds, bool holdPressed, bool holdReleased, int pressedKeyMask)
        {
            if (eventId != EventId || !IsPending || timeMilliseconds < lastTime || timeMilliseconds < 0) return;
            lastTime = timeMilliseconds;
            if (!Started)
            {
                if (!holdPressed) return;
                startedAt = timeMilliseconds;
                Pulse();
            }
            long elapsed = timeMilliseconds - startedAt;
            while (IsPending && elapsed >= (long)PulseCount * Settings.PulseMilliseconds) Pulse();
            if (!IsPending) return;
            if (holdReleased)
            { Finish(PlayerSide == 1 ? CombatConflictOutcome.Right : CombatConflictOutcome.Left, "released"); return; }
            long phase = elapsed - (long)(PulseCount - 1) * Settings.PulseMilliseconds;
            WindowOpen = phase >= Settings.WindowOpenMilliseconds && phase < Settings.WindowCloseMilliseconds;
            int expected = PulseCount % 2 == 1 ? 1 : 2;
            if (WindowOpen && !windowConsumed && pressedKeyMask == expected)
            {
                windowConsumed = true; Gauge = Math.Min(Settings.GaugeWin, Gauge + Settings.GaugeStep);
                log.Add("input:" + PulseCount + ":" + ExpectedKey + ":" + Gauge);
                if (Gauge >= Settings.GaugeWin)
                    Finish(PlayerSide == 1 ? CombatConflictOutcome.Left : CombatConflictOutcome.Right, "gauge");
            }
        }
        public void Skip(long eventId)
        {
            if (eventId != EventId) return;
            while (IsPending) Pulse();
        }
        public void Cancel() { if (IsPending) Finish(CombatConflictOutcome.Cancelled, "battle_ended"); }
        private void Pulse()
        {
            int cost = Settings.PulseMana;
            bool left = LeftAvailable >= cost, right = RightAvailable >= cost;
            // Both affordability checks precede either mutation. No callback can interleave the commit.
            if (left) { Pay(leftActor, leftSeat, cost); LeftSpent += cost; }
            if (right) { Pay(rightActor, rightSeat, cost); RightSpent += cost; }
            PulseCount++; windowConsumed = false; WindowOpen = false;
            log.Add("pulse:" + PulseCount + ":" + (left ? cost : 0) + ":" + (right ? cost : 0));
            if (!left || !right)
                Finish(left ? CombatConflictOutcome.Left : right ? CombatConflictOutcome.Right : CombatConflictOutcome.Neutral,
                    left || right ? "one_cannot_pay" : "both_cannot_pay");
        }
        private static void Pay(CombatantSnapshot actor, CombatJindanSeatState seat, int cost)
        {
            actor.TryConsumeSpirit(cost);
            seat.VisibilityLedger.ReserveRemaining -= cost; seat.VisibilityLedger.ConflictManaSpent += cost;
            seat.TotalManaSpent += cost;
        }
        private void Finish(CombatConflictOutcome outcome, string reason)
        { Outcome = outcome; EndReason = reason; WindowOpen = false; log.Add("result:" + outcome + ":" + reason); }
    }
}
