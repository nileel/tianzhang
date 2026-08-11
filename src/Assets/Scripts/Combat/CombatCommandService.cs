using System;

namespace TianZhang.Combat
{
    /// <summary>Single command gate: readiness, resolution, CTB consumption, and cooldown advancement.</summary>
    public sealed class CombatCommandService
    {
        private readonly CombatActionResolver resolver;

        public CombatCommandService(CombatActionResolver resolver = null)
        {
            this.resolver = resolver ?? new CombatActionResolver();
        }

        public CombatActionResult Execute(CombatSession session, CombatCommand command)
        {
            if (session == null || command == null)
                throw new ArgumentNullException(session == null ? nameof(session) : nameof(command));
            if (!session.TurnScheduler.IsReady(command.ActorId))
                return CombatActionResult.Rejected("combat_turn_not_ready");

            CombatActionResult result = resolver.Resolve(session, command);
            if (!result.Succeeded)
                return result;

            if (command.Kind == CombatCommandKind.Wait)
            {
                session.TurnScheduler.Wait(command.ActorId);
                return result;
            }

            int cooldownPenalty = 0;
            if (!string.IsNullOrEmpty(command.ProfileId) && session.TryGetProfile(command.ProfileId, out CombatAttackProfile profile))
                cooldownPenalty = profile.CooldownTicks;
            session.TurnScheduler.ConsumeAction(command.ActorId, cooldownPenalty);
            return result;
        }

        public CombatTurnAdvance AdvanceUntilAction(CombatSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            CombatTurnAdvance advance = session.TurnScheduler.AdvanceUntilAction(session.Combatants.All);
            if (advance.TicksElapsed > 0)
            {
                foreach (CombatantSnapshot combatant in session.Combatants.All)
                    combatant.AdvanceCooldowns(advance.TicksElapsed);
            }
            return advance;
        }
    }
}
