using System;
using System.Collections.Generic;

namespace TianZhang.Combat
{
    public sealed class CombatResultBuilder
    {
        public CombatSessionResult Build(CombatSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            bool playersAlive = session.Combatants.HasLivingMembers(CombatTeam.Player);
            bool enemiesAlive = session.Combatants.HasLivingMembers(CombatTeam.Enemy);
            CombatSessionOutcome outcome = !playersAlive
                ? CombatSessionOutcome.Defeat
                : enemiesAlive
                    ? CombatSessionOutcome.Ongoing
                    : CombatSessionOutcome.Victory;
            var defeated = new List<string>();
            foreach (CombatantSnapshot combatant in session.Combatants.All)
            {
                if (!combatant.IsAlive)
                    defeated.Add(combatant.Id);
            }
            return new CombatSessionResult(outcome, defeated);
        }
    }
}
