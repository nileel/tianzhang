using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    /// <summary>Read-only command discovery for both player and AI policy layers.</summary>
    public sealed class CombatLegalActionService
    {
        private readonly CombatCommandService commandService;

        public CombatLegalActionService(CombatCommandService commandService = null)
        {
            this.commandService = commandService ?? new CombatCommandService();
        }

        public IReadOnlyList<CombatCommand> GetLegalActions(CombatSession session, string actorId)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (!session.Combatants.TryGet(actorId, out CombatantSnapshot actor) ||
                !actor.IsAlive || session.IsFinished || session.TurnPhase == CombatTurnPhase.Finished ||
                !string.Equals(session.CurrentActorId, actor.Id, StringComparison.Ordinal) ||
                !session.TurnScheduler.IsReady(actor.Id))
                return Array.Empty<CombatCommand>();

            var actions = new List<CombatCommand>();
            AddTargetedAttacks(session, actor, CombatAttackKind.Basic, CombatCommandKind.BasicAttack, actions);
            AddTargetedAttacks(session, actor, CombatAttackKind.Art, CombatCommandKind.Art, actions);
            AddTargetedAttacks(session, actor, CombatAttackKind.Divine, CombatCommandKind.Divine, actions);
            AddIfValid(session, new CombatCommand(CombatCommandKind.Guard, actor.Id), actions);
            AddIfValid(session, new CombatCommand(CombatCommandKind.Wait, actor.Id), actions);
            AddIfValid(session, new CombatCommand(CombatCommandKind.EndTurn, actor.Id), actions);
            AddMoves(session, actor, actions);
            AddSpellSwaps(session, actor, actions);
            if (session.ExternalResources != null)
            {
                foreach (CombatCommand candidate in session.ExternalResources.GetEquipmentCandidates(actor.Id))
                    AddIfValid(session, candidate, actions);
                foreach (CombatCommand candidate in session.ExternalResources.GetItemCandidates(actor.Id))
                    AddIfValid(session, candidate, actions);
            }
            return actions;
        }

        private void AddTargetedAttacks(
            CombatSession session,
            CombatantSnapshot actor,
            CombatAttackKind profileKind,
            CombatCommandKind commandKind,
            ICollection<CombatCommand> actions)
        {
            foreach (CombatAttackProfile profile in session.AttackProfiles.Where(profile => profile.Kind == profileKind))
            {
                if (profile.Area != CombatAttackArea.Single)
                {
                    for (int direction = 0; direction < 6; direction++)
                        AddIfValid(session, new CombatCommand(commandKind, actor.Id, profileId: profile.Id,
                            attackDirection: direction), actions);
                    continue;
                }
                foreach (CombatantSnapshot target in session.Combatants.All)
                {
                    if (!target.IsAlive || !profile.AllowsTarget(actor.Team, target.Team))
                        continue;
                    AddIfValid(session, new CombatCommand(commandKind, actor.Id, target.Id, profile.Id), actions);
                }
            }
        }

        private void AddMoves(CombatSession session, CombatantSnapshot actor, ICollection<CombatCommand> actions)
        {
            if (actor.ActionPointsRemainingUnits <= 0 || actor.MovementEfficiencyMilliUnits == 0)
                return;

            IReadOnlyCollection<HexCoord> occupied = session.GetOccupiedPositionsExcept(actor.Id);
            foreach (HexCoord destination in session.SpatialQuery
                         .FindReachable(actor.Position, actor.ActionPointsRemainingUnits, actor.MovementEfficiencyMilliUnits, occupied)
                         .Keys
                         .Where(destination => destination != actor.Position)
                         .OrderBy(destination => destination.Q)
                         .ThenBy(destination => destination.R))
            {
                AddIfValid(session, new CombatCommand(CombatCommandKind.Move, actor.Id, destination: destination), actions);
            }
        }

        private void AddSpellSwaps(CombatSession session, CombatantSnapshot actor, ICollection<CombatCommand> actions)
        {
            for (int slotIndex = 0; slotIndex < actor.EquippedArtProfileIds.Count; slotIndex++)
            {
                foreach (string profileId in actor.AvailableArtProfileIds)
                {
                    AddIfValid(session, new CombatCommand(
                        CombatCommandKind.SwapSpell,
                        actor.Id,
                        profileId: profileId,
                        slotIndex: slotIndex), actions);
                }
            }
        }

        private void AddIfValid(CombatSession session, CombatCommand command, ICollection<CombatCommand> actions)
        {
            if (commandService.Validate(session, command).Succeeded)
                actions.Add(command);
        }
    }
}
