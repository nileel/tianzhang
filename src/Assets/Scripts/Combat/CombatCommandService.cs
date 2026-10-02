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
                session.FinishTurn();
                return result;
            }

            if (command.Kind == CombatCommandKind.EndTurn)
            {
                EndTurn(session, command.ActorId);
                return result;
            }
            if (command.Kind == CombatCommandKind.Move)
            {
                if (session.TurnPhase == CombatTurnPhase.ActionThenMove &&
                    session.Combatants.TryGet(command.ActorId, out CombatantSnapshot movedActor) && movedActor.MovePoints == 0)
                    EndTurn(session, command.ActorId);
                return result;
            }

            int cooldownPenalty = 0;
            if ((command.Kind is CombatCommandKind.BasicAttack or CombatCommandKind.Art or CombatCommandKind.Divine) &&
                !string.IsNullOrEmpty(command.ProfileId) &&
                session.TryGetProfile(command.ProfileId, out CombatAttackProfile profile))
                cooldownPenalty = profile.CooldownTicks;
            session.RecordAction(cooldownPenalty);
            if (session.IsFinished || (session.Combatants.TryGet(command.ActorId, out CombatantSnapshot actor) && actor.MovePoints == 0))
                EndTurn(session, command.ActorId);
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
            if (session.IsFinished)
            {
                session.FinishTurn();
                return new CombatTurnAdvance(string.Empty, 0);
            }
            if (session.TurnPhase != CombatTurnPhase.Finished)
            {
                if (session.Combatants.TryGet(session.CurrentActorId, out CombatantSnapshot currentActor) && currentActor.IsAlive)
                    return new CombatTurnAdvance(currentActor.Id, 0);
                session.FinishTurn();
            }
            CombatTurnAdvance advance = session.TurnScheduler.AdvanceUntilAction(session.Combatants.All);
            if (advance.TicksElapsed > 0)
            {
                foreach (CombatantSnapshot combatant in session.Combatants.All)
                    combatant.AdvanceCooldowns(advance.TicksElapsed);
            }
            if (advance.HasActor && session.Combatants.TryGet(advance.ActorId, out CombatantSnapshot actor))
                session.BeginTurn(actor);
            return advance;
        }

        private static void EndTurn(CombatSession session, string actorId)
        {
            session.TurnScheduler.ConsumeAction(actorId, session.PendingCooldownPenalty);
            session.FinishTurn();
        }
    }
}
