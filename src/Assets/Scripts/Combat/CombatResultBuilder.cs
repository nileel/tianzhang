using System;
using System.Collections.Generic;

namespace TianZhang.Combat
{
    public sealed class CombatResultBuilder
    {
        public static CombatSessionOutcome GetOutcome(CombatSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (!session.Combatants.HasLivingMembers(CombatTeam.Player)) return CombatSessionOutcome.Defeat;
            return session.Combatants.HasLivingMembers(CombatTeam.Enemy)
                ? CombatSessionOutcome.Ongoing : CombatSessionOutcome.Victory;
        }

        public CombatSessionResult Build(CombatSession session)
        {
            CombatSessionOutcome outcome = GetOutcome(session);
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
