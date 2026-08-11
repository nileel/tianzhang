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
