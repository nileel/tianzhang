using System;

namespace TianZhang.Combat
{
    public sealed class CombatCommandPreview
    {
        internal CombatCommandPreview(CombatActionResult validation, int spiritCost, int penalty,
            bool remainsInTurn, CombatTurnPreview turn)
        {
            Validation = validation;
            SpiritCost = spiritCost;
            CooldownPenalty = penalty;
            RemainsInTurn = remainsInTurn;
            Turn = turn;
        }
        public CombatActionResult Validation { get; }
        public int SpiritCost { get; }
        public int CooldownPenalty { get; }
        public bool RemainsInTurn { get; }
        public CombatTurnPreview Turn { get; }
    }

    /// <summary>Single command gate: readiness, resolution, CTB consumption, and cooldown advancement.</summary>
    public sealed class CombatCommandService
    {
        private readonly CombatActionResolver resolver;

        public CombatCommandService(CombatActionResolver resolver = null)
        {
            this.resolver = resolver ?? new CombatActionResolver();
        }

        /// <summary>Read-only legality and conditional turn-end CT; no resolver or random source.</summary>
        public CombatCommandPreview Preview(CombatSession session, CombatCommand command)
        {
            CombatActionResult validation = Validate(session, command);
            int cost = 0, penalty = command.Kind == CombatCommandKind.EndTurn ? session.PendingCooldownPenalty : 0;
            if (command.Kind is CombatCommandKind.BasicAttack or CombatCommandKind.Art or CombatCommandKind.Divine)
                if (session.TryGetProfile(command.ProfileId, out CombatAttackProfile profile))
                {
                    cost = profile.GetSpiritCost(command.ActorId == command.TargetId);
                    penalty = profile.CtPenalty;
                }
            // Movement has its own preview/confirmation and cannot predict an unchosen action.
            bool supported = command.Kind is CombatCommandKind.BasicAttack or CombatCommandKind.Art or
                CombatCommandKind.Divine or CombatCommandKind.Guard or CombatCommandKind.Wait or CombatCommandKind.EndTurn;
            if (!supported) validation = CombatActionResult.Rejected("combat_command_kind_invalid");
            session.Combatants.TryGet(command.ActorId, out CombatantSnapshot actor);
            bool remains = command.Kind != CombatCommandKind.Wait && command.Kind != CombatCommandKind.EndTurn &&
                actor != null && actor.MovePoints > 0;
            return new CombatCommandPreview(validation, cost, penalty, remains,
                validation.Succeeded ? session.TurnScheduler.PreviewTurnEnd(command.ActorId, penalty,
                    command.Kind == CombatCommandKind.Wait, session.Combatants.All) : null);
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
                cooldownPenalty = profile.CtPenalty;
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
