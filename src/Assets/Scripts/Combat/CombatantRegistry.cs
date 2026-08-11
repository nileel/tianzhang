using System;
using System.Collections.Generic;

namespace TianZhang.Combat
{
    /// <summary>Ordered lookup for session-local snapshots. Stable IDs are the only identity key.</summary>
    public sealed class CombatantRegistry
    {
        private readonly Dictionary<string, CombatantSnapshot> byId;
        private readonly List<CombatantSnapshot> ordered;

        public CombatantRegistry(IEnumerable<CombatantSnapshot> combatants)
        {
            if (combatants == null)
                throw new ArgumentNullException(nameof(combatants));

            byId = new Dictionary<string, CombatantSnapshot>(StringComparer.Ordinal);
            ordered = new List<CombatantSnapshot>();
            foreach (CombatantSnapshot combatant in combatants)
            {
                if (combatant == null || !byId.TryAdd(combatant.Id, combatant))
                    throw new ArgumentException("Combatants must have unique non-null stable IDs.", nameof(combatants));
                ordered.Add(combatant);
            }
        }

        public IReadOnlyList<CombatantSnapshot> All => ordered;

        public bool TryGet(string id, out CombatantSnapshot combatant)
        {
            combatant = null;
            return !string.IsNullOrWhiteSpace(id) && byId.TryGetValue(id, out combatant);
        }

        public bool HasLivingMembers(CombatTeam team)
        {
            foreach (CombatantSnapshot combatant in ordered)
            {
                if (combatant.Team == team && combatant.IsAlive)
                    return true;
            }
            return false;
        }
    }
}
