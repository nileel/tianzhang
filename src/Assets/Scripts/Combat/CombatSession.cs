using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public readonly struct CombatRangeQueryResult
    {
        public CombatRangeQueryResult(bool isInRange, string reason)
        {
            IsInRange = isInRange;
            Reason = reason ?? string.Empty;
        }

        public bool IsInRange { get; }
        public string Reason { get; }
    }

    public readonly struct CombatMovementQueryResult
    {
        public CombatMovementQueryResult(
            bool isReachable,
            string reason,
            IReadOnlyList<HexCoord> path,
            int movementCost)
        {
            IsReachable = isReachable;
            Reason = reason ?? string.Empty;
            Path = path ?? Array.Empty<HexCoord>();
            MovementCost = movementCost;
        }

        public bool IsReachable { get; }
        public string Reason { get; }
        public IReadOnlyList<HexCoord> Path { get; }
        public int MovementCost { get; }
    }

    /// <summary>Combat receives spatial facts through this narrow query boundary.</summary>
    public interface ICombatSpatialQuery
    {
        CombatRangeQueryResult QueryRange(HexCoord source, HexCoord target, int minimumRange, int maximumRange);
        CombatMovementQueryResult QueryMovement(
            HexCoord source,
            HexCoord destination,
            int movementPoints,
            IReadOnlyCollection<HexCoord> occupied);
        IReadOnlyDictionary<HexCoord, int> FindReachable(
            HexCoord source,
            int movementPoints,
            IReadOnlyCollection<HexCoord> occupied);
    }

    public enum CombatTurnPhase
    {
        MoveThenAction,
        ActionThenMove,
        Finished,
    }

    /// <summary>Pure session aggregate; it has no scene registration or production composition role.</summary>
    public sealed class CombatSession
    {
        private readonly Dictionary<string, CombatAttackProfile> profiles;
        private readonly List<CombatAttackProfile> orderedProfiles;

        public CombatSession(
            IEnumerable<CombatantSnapshot> combatants,
            IEnumerable<CombatAttackProfile> attackProfiles,
            ICombatSpatialQuery spatialQuery)
        {
            if (spatialQuery == null)
                throw new ArgumentNullException(nameof(spatialQuery));
            Combatants = new CombatantRegistry(combatants);
            ValidateSideCardinality(Combatants.All);
            profiles = new Dictionary<string, CombatAttackProfile>(StringComparer.Ordinal);
            orderedProfiles = new List<CombatAttackProfile>();
            if (attackProfiles == null)
                throw new ArgumentNullException(nameof(attackProfiles));
            foreach (CombatAttackProfile profile in attackProfiles)
            {
                if (profile == null || !profiles.TryAdd(profile.Id, profile))
                    throw new ArgumentException("Attack profiles must have unique non-null IDs.", nameof(attackProfiles));
                orderedProfiles.Add(profile);
            }

            SpatialQuery = spatialQuery;
            TurnScheduler = new CombatTurnScheduler(Combatants.All);
        }

        public string CurrentActorId { get; private set; } = string.Empty;
        public CombatTurnPhase TurnPhase { get; private set; } = CombatTurnPhase.Finished;
        public int PendingCooldownPenalty { get; private set; }
        internal bool IsFinished => !Combatants.HasLivingMembers(CombatTeam.Player) ||
            !Combatants.HasLivingMembers(CombatTeam.Enemy);

        internal void BeginTurn(CombatantSnapshot actor)
        {
            CurrentActorId = actor.Id;
            TurnPhase = CombatTurnPhase.MoveThenAction;
            PendingCooldownPenalty = 0;
            actor.ResetMovePoints();
        }

        internal void RecordAction(int cooldownPenalty)
        {
            PendingCooldownPenalty = cooldownPenalty;
            TurnPhase = CombatTurnPhase.ActionThenMove;
        }

        internal void FinishTurn()
        {
            CurrentActorId = string.Empty;
            TurnPhase = CombatTurnPhase.Finished;
            PendingCooldownPenalty = 0;
        }

        public CombatantRegistry Combatants { get; }
        public ICombatSpatialQuery SpatialQuery { get; }
        public CombatTurnScheduler TurnScheduler { get; }
        public IReadOnlyList<CombatAttackProfile> AttackProfiles => orderedProfiles.AsReadOnly();

        public bool TryGetProfile(string id, out CombatAttackProfile profile)
        {
            profile = null;
            return !string.IsNullOrWhiteSpace(id) && profiles.TryGetValue(id, out profile);
        }

        /// <summary>Single side-effect-free command validation entry shared by resolution and action discovery.</summary>
        public CombatActionResult ValidateCommand(CombatCommand command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (!Combatants.TryGet(command.ActorId, out CombatantSnapshot actor) || !actor.IsAlive)
                return CombatActionResult.Rejected("combat_session_actor_invalid");
            if (IsFinished)
                return CombatActionResult.Rejected("combat_session_finished");
            if (!string.Equals(CurrentActorId, actor.Id, StringComparison.Ordinal) ||
                TurnPhase == CombatTurnPhase.Finished)
                return CombatActionResult.Rejected("combat_turn_actor_invalid");
            if (!TurnScheduler.IsReady(actor.Id))
                return CombatActionResult.Rejected("combat_turn_not_ready");

            if ((command.Kind == CombatCommandKind.EndTurn && TurnPhase != CombatTurnPhase.ActionThenMove) ||
                (command.Kind != CombatCommandKind.EndTurn && command.Kind != CombatCommandKind.Move &&
                 TurnPhase != CombatTurnPhase.MoveThenAction))
                return CombatActionResult.Rejected("combat_turn_phase_invalid");

            return command.Kind switch
            {
                CombatCommandKind.EndTurn => CombatActionResult.Success(),
                CombatCommandKind.Guard => CombatActionResult.Success(),
                CombatCommandKind.Wait => CombatActionResult.Success(),
                CombatCommandKind.BasicAttack => ValidateAttack(command, actor, CombatAttackKind.Basic),
                CombatCommandKind.Art => ValidateAttack(command, actor, CombatAttackKind.Art),
                CombatCommandKind.Divine => ValidateAttack(command, actor, CombatAttackKind.Divine),
                CombatCommandKind.Move => ValidateMove(command, actor),
                CombatCommandKind.SwapSpell => ValidateSwapSpell(command, actor),
                _ => CombatActionResult.Rejected("combat_command_kind_invalid"),
            };
        }

        internal IReadOnlyCollection<HexCoord> GetOccupiedPositionsExcept(string combatantId)
        {
            var occupied = new List<HexCoord>();
            foreach (CombatantSnapshot combatant in Combatants.All)
            {
                if (combatant.IsAlive && !string.Equals(combatant.Id, combatantId, StringComparison.Ordinal))
                    occupied.Add(combatant.Position);
            }
            return occupied;
        }

        private CombatActionResult ValidateAttack(
            CombatCommand command,
            CombatantSnapshot actor,
            CombatAttackKind expectedKind)
        {
            if (!Combatants.TryGet(command.TargetId, out CombatantSnapshot target) || !target.IsAlive || target.Team == actor.Team)
                return CombatActionResult.Rejected("combat_session_target_invalid");
            if (!TryGetProfile(command.ProfileId, out CombatAttackProfile profile) || profile.Kind != expectedKind)
                return CombatActionResult.Rejected("attack_profile_unresolved");
            if (!IsAuthorized(actor, profile))
                return CombatActionResult.Rejected("attack_profile_not_authorized");
            if (actor.GetCooldown(profile.Id) > 0)
                return CombatActionResult.Rejected("attack_profile_cooldown_active");
            if (profile.SpiritCost > 0 && actor.CurrentSpirit < profile.SpiritCost)
                return CombatActionResult.Rejected("spirit_insufficient");

            CombatRangeQueryResult range = SpatialQuery.QueryRange(
                actor.Position, target.Position, profile.MinimumRange, profile.MaximumRange);
            return range.IsInRange
                ? CombatActionResult.Success()
                : CombatActionResult.Rejected(string.IsNullOrEmpty(range.Reason) ? "target_out_of_range" : range.Reason);
        }

        private static bool IsAuthorized(CombatantSnapshot actor, CombatAttackProfile profile)
        {
            switch (profile.Kind)
            {
                case CombatAttackKind.Basic:
                    return string.Equals(actor.BasicAttackProfileId, profile.Id, StringComparison.Ordinal);
                case CombatAttackKind.Art:
                    return actor.AvailableArtProfileIds.Contains(profile.Id) && actor.EquippedArtProfileIds.Contains(profile.Id);
                case CombatAttackKind.Divine:
                    return actor.AvailableDivineProfileIds.Contains(profile.Id) && actor.EquippedDivineProfileIds.Contains(profile.Id);
                default:
                    return false;
            }
        }

        private CombatActionResult ValidateMove(CombatCommand command, CombatantSnapshot actor)
        {
            if (!command.Destination.HasValue || command.Destination.Value == actor.Position)
                return CombatActionResult.Rejected("combat_move_destination_invalid");
            if (actor.MovePoints <= 0)
                return CombatActionResult.Rejected("combat_move_points_exhausted");

            IReadOnlyCollection<HexCoord> occupied = GetOccupiedPositionsExcept(actor.Id);
            if (occupied.Contains(command.Destination.Value))
                return CombatActionResult.Rejected("combat_move_destination_occupied");

            CombatMovementQueryResult movement = SpatialQuery.QueryMovement(
                actor.Position,
                command.Destination.Value,
                actor.MovePoints,
                occupied);
            if (!movement.IsReachable || movement.Path.Count == 0 ||
                movement.Path[movement.Path.Count - 1] != command.Destination.Value ||
                movement.MovementCost < 0 || movement.MovementCost > actor.MovePoints)
            {
                return CombatActionResult.Rejected(
                    string.IsNullOrEmpty(movement.Reason) ? "combat_move_path_invalid" : movement.Reason);
            }
            HexCoord previous = movement.Path.Count == 1
                ? actor.Position
                : movement.Path[movement.Path.Count - 2];
            int facing = previous.DirectionTo(movement.Path[movement.Path.Count - 1]);
            if (facing < 0)
                return CombatActionResult.Rejected("combat_move_path_invalid");
            return CombatActionResult.MovementSuccess(movement.Path, movement.MovementCost, facing);
        }

        private CombatActionResult ValidateSwapSpell(CombatCommand command, CombatantSnapshot actor)
        {
            if (actor.CombatSwapsUsed >= actor.MaxCombatSwaps)
                return CombatActionResult.Rejected("combat_swap_limit_reached");
            if (command.SlotIndex < 0 || command.SlotIndex >= actor.EquippedArtProfileIds.Count)
                return CombatActionResult.Rejected("combat_swap_slot_invalid");
            if (!actor.AvailableArtProfileIds.Contains(command.ProfileId))
                return CombatActionResult.Rejected("combat_swap_candidate_unavailable");
            if (string.Equals(actor.EquippedArtProfileIds[command.SlotIndex], command.ProfileId, StringComparison.Ordinal))
                return CombatActionResult.Rejected("combat_swap_same_profile");
            if (actor.EquippedArtProfileIds.Contains(command.ProfileId))
                return CombatActionResult.Rejected("combat_swap_profile_already_equipped");
            if (!TryGetProfile(command.ProfileId, out CombatAttackProfile profile) || profile.Kind != CombatAttackKind.Art)
                return CombatActionResult.Rejected("combat_swap_profile_unresolved");
            return CombatActionResult.Success();
        }

        private static void ValidateSideCardinality(IReadOnlyList<CombatantSnapshot> combatants)
        {
            int players = 0;
            int enemies = 0;
            foreach (CombatantSnapshot combatant in combatants)
            {
                if (combatant.Team == CombatTeam.Player)
                    players++;
                else
                    enemies++;
            }
            if (players is < 1 or > 2 || players != enemies)
                throw new ArgumentException("Combat sessions require matched 1v1 or 2v2 teams.", nameof(combatants));
        }
    }
}
