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

                CurrentTick++;
                ticks++;
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
            int speed = right.Speed.CompareTo(left.Speed);
            return speed != 0 ? speed : right.ChargeTime.CompareTo(left.ChargeTime);
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
    }
}
