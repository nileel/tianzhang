using System;
using System.Collections.Generic;

namespace TianZhang.Combat.Turns
{
    public readonly struct CTBAdvance
    {
        public CTBAdvance(string unitId, int ticksElapsed)
        {
            UnitId = unitId ?? string.Empty;
            TicksElapsed = ticksElapsed;
        }

        public string UnitId { get; }
        public int TicksElapsed { get; }
    }

    public readonly struct CTBUnitSnapshot
    {
        internal CTBUnitSnapshot(string id, int speed, float chargeTime, float nextActionThreshold, bool isReady)
        {
            Id = id;
            Speed = speed;
            ChargeTime = chargeTime;
            NextActionThreshold = nextActionThreshold;
            IsReady = isReady;
        }

        public string Id { get; }
        public int Speed { get; }
        public float ChargeTime { get; }
        public float NextActionThreshold { get; }
        public bool IsReady { get; }
    }

    public sealed class CTBSnapshot
    {
        internal CTBSnapshot(int currentTick, IEnumerable<CTBUnitSnapshot> units,
            IEnumerable<string> readyQueueIds, IEnumerable<string> chargingUnitIds)
        {
            CurrentTick = currentTick;
            Units = new List<CTBUnitSnapshot>(units).AsReadOnly();
            ReadyQueueIds = new List<string>(readyQueueIds).AsReadOnly();
            ChargingUnitIds = new List<string>(chargingUnitIds).AsReadOnly();
        }

        public int CurrentTick { get; }
        public IReadOnlyList<CTBUnitSnapshot> Units { get; }
        public IReadOnlyList<string> ReadyQueueIds { get; }
        public IReadOnlyList<string> ChargingUnitIds { get; }
    }

    /// <summary>Pure stable-ID CTB scheduler; it owns no combatant, scene, or presentation reference.</summary>
    public sealed class CTBEngine
    {
        public const float ActionThreshold = 100f;
        public const float CtRetentionOnWait = 0.5f;

        private readonly Dictionary<string, UnitState> units = new Dictionary<string, UnitState>(StringComparer.Ordinal);
        private readonly Queue<string> actionQueue = new Queue<string>();

        public int CurrentTick { get; private set; }

        public void Register(string unitId, int speed)
        {
            if (string.IsNullOrWhiteSpace(unitId) || units.ContainsKey(unitId))
                throw new ArgumentException("CTB units require unique stable IDs.", nameof(unitId));
            units.Add(unitId, new UnitState(unitId, Math.Max(1, speed)));
        }

        public bool IsReady(string unitId)
        {
            return !string.IsNullOrWhiteSpace(unitId) && units.TryGetValue(unitId, out UnitState state) && state.IsReady;
        }

        /// <summary>Copies actual state and projects charging order without advancing or consuming the queue.</summary>
        public CTBSnapshot GetSnapshot()
        {
            var snapshots = new List<CTBUnitSnapshot>();
            var charging = new List<ChargingCandidate>();
            foreach (UnitState state in units.Values)
            {
                snapshots.Add(new CTBUnitSnapshot(state.Id, state.Speed, state.ChargeTime,
                    state.NextActionThreshold, state.IsReady));
                if (!state.IsReady)
                {
                    // The engine charges at least one tick before checking a non-ready unit,
                    // including a Wait whose retained overshoot is already above its threshold.
                    long ticks = ChargingTicks(state.ChargeTime, state.NextActionThreshold, state.Speed);
                    charging.Add(new ChargingCandidate(state.Id, state.Speed, ticks,
                        state.ChargeTime + state.Speed * ticks));
                }
            }
            charging.Sort(CompareChargingUnits);
            var chargingIds = new List<string>();
            foreach (ChargingCandidate candidate in charging)
                chargingIds.Add(candidate.Id);
            return new CTBSnapshot(CurrentTick, snapshots, actionQueue.ToArray(), chargingIds);
        }

        /// <summary>Projects only this turn's settlement on a private engine copy; never advances live time.</summary>
        public CTBSnapshot PreviewTurnEnd(string unitId, int cooldownPenalty, bool wait)
        {
            var copy = new CTBEngine();
            copy.CurrentTick = CurrentTick;
            foreach (UnitState state in units.Values)
                copy.units.Add(state.Id, new UnitState(state.Id, state.Speed)
                {
                    ChargeTime = state.ChargeTime,
                    NextActionThreshold = state.NextActionThreshold,
                    IsReady = state.IsReady,
                });
            foreach (string id in actionQueue) copy.actionQueue.Enqueue(id);
            if (wait) copy.WaitAction(unitId);
            else copy.ConsumeAction(unitId, cooldownPenalty);
            return copy.GetSnapshot();
        }

        public static long TicksUntilReady(CTBUnitSnapshot unit) => unit.IsReady ? 0 :
            ChargingTicks(unit.ChargeTime, unit.NextActionThreshold, unit.Speed);

        private static long ChargingTicks(float ct, float threshold, int speed) =>
            Math.Max(1L, (long)Math.Ceiling((threshold - (double)ct) / speed));

        public CTBAdvance AdvanceUntilAction(IReadOnlyDictionary<string, bool> aliveById)
        {
            if (aliveById == null)
                throw new ArgumentNullException(nameof(aliveById));
            int ticks = 0;
            while (true)
            {
                while (actionQueue.Count > 0)
                {
                    string queuedId = actionQueue.Dequeue();
                    if (IsAlive(queuedId, aliveById))
                        return new CTBAdvance(queuedId, ticks);
                }

                CurrentTick = checked(CurrentTick + 1);
                ticks = checked(ticks + 1);
                var ready = new List<UnitState>();
                foreach (UnitState state in units.Values)
                {
                    if (!IsAlive(state.Id, aliveById))
                        continue;
                    state.ChargeTime += state.Speed;
                    if (state.ChargeTime >= state.NextActionThreshold)
                        ready.Add(state);
                }
                ready.Sort(CompareReadyUnits);
                if (ready.Count > 0)
                {
                    foreach (UnitState state in ready)
                        state.IsReady = true;
                    for (int index = 1; index < ready.Count; index++)
                        actionQueue.Enqueue(ready[index].Id);
                    return new CTBAdvance(ready[0].Id, ticks);
                }
                if (ticks > 10000)
                    return new CTBAdvance(string.Empty, ticks);
            }
        }

        public void ConsumeAction(string unitId, int cooldownPenalty)
        {
            if (cooldownPenalty < 0 || cooldownPenalty > int.MaxValue - 100)
                throw new ArgumentOutOfRangeException(nameof(cooldownPenalty));
            UnitState state = GetUnit(unitId);
            state.ChargeTime = 0f;
            state.NextActionThreshold = ActionThreshold + Math.Max(0, cooldownPenalty);
            state.IsReady = false;
        }

        public void WaitAction(string unitId)
        {
            UnitState state = GetUnit(unitId);
            state.ChargeTime *= CtRetentionOnWait;
            state.NextActionThreshold = ActionThreshold;
            state.IsReady = false;
        }

        private static int CompareReadyUnits(UnitState left, UnitState right)
        {
            int order = CompareReadyValues(left.Speed, left.ChargeTime, right.Speed, right.ChargeTime);
            return order != 0 ? order : StringComparer.Ordinal.Compare(left.Id, right.Id);
        }

        private static int CompareChargingUnits(ChargingCandidate left, ChargingCandidate right)
        {
            int ticks = left.TicksUntilReady.CompareTo(right.TicksUntilReady);
            int order = ticks != 0 ? ticks : CompareReadyValues(left.Speed, left.ArrivalChargeTime,
                right.Speed, right.ArrivalChargeTime);
            return order != 0 ? order : StringComparer.Ordinal.Compare(left.Id, right.Id);
        }

        private static int CompareReadyValues(int leftSpeed, float leftChargeTime, int rightSpeed, float rightChargeTime)
        {
            int speed = rightSpeed.CompareTo(leftSpeed);
            return speed != 0 ? speed : rightChargeTime.CompareTo(leftChargeTime);
        }

        private static bool IsAlive(string unitId, IReadOnlyDictionary<string, bool> aliveById)
        {
            return aliveById.TryGetValue(unitId, out bool alive) && alive;
        }

        private UnitState GetUnit(string unitId)
        {
            if (string.IsNullOrWhiteSpace(unitId) || !units.TryGetValue(unitId, out UnitState state))
                throw new ArgumentException("Unknown CTB unit ID.", nameof(unitId));
            return state;
        }

        private sealed class UnitState
        {
            public UnitState(string id, int speed)
            {
                Id = id;
                Speed = speed;
                NextActionThreshold = ActionThreshold;
            }

            public string Id { get; }
            public int Speed { get; }
            public float ChargeTime { get; set; }
            public float NextActionThreshold { get; set; }
            public bool IsReady { get; set; }
        }

        private readonly struct ChargingCandidate
        {
            public ChargingCandidate(string id, int speed, long ticksUntilReady, float arrivalChargeTime)
            {
                Id = id;
                Speed = speed;
                TicksUntilReady = ticksUntilReady;
                ArrivalChargeTime = arrivalChargeTime;
            }

            public string Id { get; }
            public int Speed { get; }
            public long TicksUntilReady { get; }
            public float ArrivalChargeTime { get; }
        }
    }
}
