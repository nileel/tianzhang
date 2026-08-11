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
            CombatActionResult validation = Validate(session, command);
            if (!validation.Succeeded)
                return validation;

            CombatActionResult result = resolver.ResolveValidated(session, command, validation);
            if (!result.Succeeded)
                return result;

            if (command.Kind == CombatCommandKind.Wait)
            {
                session.TurnScheduler.Wait(command.ActorId);
                return result;
            }

            int cooldownPenalty = 0;
            if ((command.Kind is CombatCommandKind.BasicAttack or CombatCommandKind.Art or CombatCommandKind.Divine) &&
                !string.IsNullOrEmpty(command.ProfileId) &&
                session.TryGetProfile(command.ProfileId, out CombatAttackProfile profile))
                cooldownPenalty = profile.CooldownTicks;
            session.TurnScheduler.ConsumeAction(command.ActorId, cooldownPenalty);
            return result;
        }

        public CombatActionResult Validate(CombatSession session, CombatCommand command)
        {
            if (session == null || command == null)
                throw new ArgumentNullException(session == null ? nameof(session) : nameof(command));
            return session.ValidateCommand(command);
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
