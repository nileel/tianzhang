using System;
using System.Collections.Generic;
using TianZhang.Combat.Turns;

namespace TianZhang.Combat
{
    public readonly struct CombatTurnAdvance
    {
        public CombatTurnAdvance(string actorId, int ticksElapsed)
        {
            ActorId = actorId;
            TicksElapsed = ticksElapsed;
        }

        public string ActorId { get; }
        public int TicksElapsed { get; }
        public bool HasActor => !string.IsNullOrEmpty(ActorId);
    }

    public readonly struct CombatTurnUnitSnapshot
    {
        internal CombatTurnUnitSnapshot(string id, int speed, float chargeTime, float nextActionThreshold, bool isReady)
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

    public sealed class CombatTurnSnapshot
    {
        internal CombatTurnSnapshot(int currentTick, IEnumerable<CombatTurnUnitSnapshot> units,
            IEnumerable<string> readyQueueIds, IEnumerable<string> chargingUnitIds)
        {
            CurrentTick = currentTick;
            Units = new List<CombatTurnUnitSnapshot>(units).AsReadOnly();
            ReadyQueueIds = new List<string>(readyQueueIds).AsReadOnly();
            ChargingUnitIds = new List<string>(chargingUnitIds).AsReadOnly();
        }

        public int CurrentTick { get; }
        public IReadOnlyList<CombatTurnUnitSnapshot> Units { get; }
        public IReadOnlyList<string> ReadyQueueIds { get; }
        public IReadOnlyList<string> ChargingUnitIds { get; }
    }

    /// <summary>Root Combat wrapper for the lower-level pure CTB engine.</summary>
    public sealed class CombatTurnScheduler
    {
        private readonly CTBEngine engine;

        public CombatTurnScheduler(IReadOnlyList<CombatantSnapshot> combatants)
        {
            if (combatants == null)
                throw new ArgumentNullException(nameof(combatants));
            engine = new CTBEngine();
            foreach (CombatantSnapshot combatant in combatants)
                engine.Register(combatant.Id, combatant.Speed);
        }

        public int CurrentTick => engine.CurrentTick;

        /// <summary>Filters the immutable engine projection through the session's living combatants.</summary>
        public CombatTurnSnapshot GetSnapshot(IReadOnlyList<CombatantSnapshot> combatants)
        {
            if (combatants == null)
                throw new ArgumentNullException(nameof(combatants));
            var aliveIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (CombatantSnapshot combatant in combatants)
            {
                if (combatant.IsAlive)
                    aliveIds.Add(combatant.Id);
            }

            CTBSnapshot snapshot = engine.GetSnapshot();
            var unitSnapshots = new List<CombatTurnUnitSnapshot>();
            foreach (CTBUnitSnapshot unit in snapshot.Units)
            {
                if (aliveIds.Contains(unit.Id))
                    unitSnapshots.Add(new CombatTurnUnitSnapshot(unit.Id, unit.Speed, unit.ChargeTime,
                        unit.NextActionThreshold, unit.IsReady));
            }
            var readyQueueIds = new List<string>();
            foreach (string id in snapshot.ReadyQueueIds)
            {
                if (aliveIds.Contains(id))
                    readyQueueIds.Add(id);
            }
            var chargingUnitIds = new List<string>();
            foreach (string id in snapshot.ChargingUnitIds)
            {
                if (aliveIds.Contains(id))
                    chargingUnitIds.Add(id);
            }
            return new CombatTurnSnapshot(snapshot.CurrentTick, unitSnapshots, readyQueueIds, chargingUnitIds);
        }

        public CombatTurnAdvance AdvanceUntilAction(IReadOnlyList<CombatantSnapshot> combatants)
        {
            if (combatants == null)
                throw new ArgumentNullException(nameof(combatants));
            var aliveById = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (CombatantSnapshot combatant in combatants)
                aliveById.Add(combatant.Id, combatant.IsAlive);

            CTBAdvance advance = engine.AdvanceUntilAction(aliveById);
            return new CombatTurnAdvance(advance.UnitId, advance.TicksElapsed);
        }

        public bool IsReady(string combatantId)
        {
            return engine.IsReady(combatantId);
        }

        public void ConsumeAction(string combatantId, int cooldownPenalty)
        {
            engine.ConsumeAction(combatantId, cooldownPenalty);
        }

        public void Wait(string combatantId)
        {
            engine.WaitAction(combatantId);
        }
    }
}
