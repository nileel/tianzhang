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
            IReadOnlyList<SpatialMovementEdgeCost> movementEdges,
            int unitsPerRange,
            int actionPointCostUnits)
        {
            IsReachable = isReachable;
            Reason = reason ?? string.Empty;
            Path = new List<HexCoord>(path ?? Array.Empty<HexCoord>()).AsReadOnly();
            MovementEdges = new List<SpatialMovementEdgeCost>(movementEdges ?? Array.Empty<SpatialMovementEdgeCost>()).AsReadOnly();
            UnitsPerRange = unitsPerRange;
            ActionPointCostUnits = actionPointCostUnits;
        }

        public bool IsReachable { get; }
        public string Reason { get; }

        /// <summary>所选路径的经过格与终点，不含起点；按现有 FindPath 顺序。</summary>
        public IReadOnlyList<HexCoord> Path { get; }

        /// <summary>
        /// Path 的原始有向边度量，与 Path 一一对应（统一行动力公共合同 §四）；同一路径来源，不另建寻路。
        /// 合法移动必须完整提供同源边载荷。
        /// </summary>
        public IReadOnlyList<SpatialMovementEdgeCost> MovementEdges { get; }

        /// <summary>报价板的 K=SpatialQueryLimits.UnitsPerRange。</summary>
        public int UnitsPerRange { get; }

        /// <summary>
        /// Path 的 AP 最小单位费用；合法路径的每条边至少收费 1 单位。
        /// </summary>
        public int ActionPointCostUnits { get; }
    }

    /// <summary>
    /// Combat receives spatial facts through this narrow AP budget / effective v boundary.
    /// </summary>
    public interface ICombatSpatialQuery
    {
        CombatRangeQueryResult QueryRange(HexCoord source, HexCoord target, int minimumRange, int maximumRange);
        CombatMovementQueryResult QueryMovement(
            HexCoord source,
            HexCoord destination,
            int actionPointBudgetUnits,
            int movementEfficiencyMilliUnits,
            IReadOnlyCollection<HexCoord> occupied);
        IReadOnlyDictionary<HexCoord, int> FindReachable(
            HexCoord source,
            int actionPointBudgetUnits,
            int movementEfficiencyMilliUnits,
            IReadOnlyCollection<HexCoord> occupied);
    }

    public enum CombatTurnPhase
    {
        Active,
        Finished,
    }

    [Serializable]
    public sealed class CombatOrdinaryActionDefinition
    {
        public CombatCommandKind Kind;
        public int ActionPointCostUnits;
        public int MinimumNetCostUnits;
        public int CtPenalty;
        public int ReuseCooldownTicks;

        public CombatOrdinaryActionDefinition(CombatCommandKind kind, int cost, int minimum, int penalty, int cooldown)
        { Kind = kind; ActionPointCostUnits = cost; MinimumNetCostUnits = minimum; CtPenalty = penalty; ReuseCooldownTicks = cooldown; }

        internal CombatOrdinaryActionDefinition CopyValidated()
        {
            if (Kind is not (CombatCommandKind.Guard or CombatCommandKind.SwapSpell or CombatCommandKind.SwapEquipment) || ActionPointCostUnits <= 0 ||
                MinimumNetCostUnits <= 0 || MinimumNetCostUnits > ActionPointCostUnits ||
                CtPenalty != 0 || (Kind == CombatCommandKind.SwapSpell ? ReuseCooldownTicks != 60 : ReuseCooldownTicks != 0))
                throw new ArgumentException("Invalid ordinary action definition.");
            return new CombatOrdinaryActionDefinition(Kind, ActionPointCostUnits, MinimumNetCostUnits, CtPenalty, ReuseCooldownTicks);
        }
    }

    /// <summary>Immutable external facts only; the owning bridge commits its prepared domain state.</summary>
    public interface ICombatExternalResourceQuery
    {
        CombatEquipmentResource QueryEquipment(CombatCommand command);
        IReadOnlyList<CombatCommand> GetEquipmentCandidates(string actorId);
    }

    public sealed class CombatEquipmentResource
    {
        public CombatEquipmentResource(long version, string currentInstanceId, string profileId, string reason)
        { Version = version; CurrentInstanceId = currentInstanceId; ProfileId = profileId; Reason = reason; }
        public long Version { get; }
        public string CurrentInstanceId { get; }
        public string ProfileId { get; }
        public string Reason { get; }
        internal string Facts => string.Join("|", Version, CurrentInstanceId, ProfileId, Reason);
    }

    /// <summary>Pure session aggregate; it has no scene registration or production composition role.</summary>
    public sealed class CombatSession
    {
        private readonly Dictionary<string, CombatAttackProfile> profiles;
        private readonly List<CombatAttackProfile> orderedProfiles;
        private readonly Dictionary<CombatCommandKind, CombatOrdinaryActionDefinition> ordinary =
            new Dictionary<CombatCommandKind, CombatOrdinaryActionDefinition>();

        public CombatSession(
            IEnumerable<CombatantSnapshot> combatants,
            IEnumerable<CombatAttackProfile> attackProfiles,
            ICombatSpatialQuery spatialQuery,
            IEnumerable<CombatOrdinaryActionDefinition> ordinaryActions,
            float spiritRecoveryEnvironmentMultiplier = 1f)
        {
            if (spatialQuery == null)
                throw new ArgumentNullException(nameof(spatialQuery));
            if (spiritRecoveryEnvironmentMultiplier != 1f && spiritRecoveryEnvironmentMultiplier != 2f)
                throw new ArgumentOutOfRangeException(nameof(spiritRecoveryEnvironmentMultiplier));
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

            if (ordinaryActions == null) throw new ArgumentNullException(nameof(ordinaryActions));
            foreach (CombatOrdinaryActionDefinition definition in ordinaryActions)
                if (definition == null || !ordinary.TryAdd(definition.Kind, definition.CopyValidated()))
                    throw new ArgumentException("Ordinary actions must have unique non-null definitions.", nameof(ordinaryActions));
            if (!ordinary.ContainsKey(CombatCommandKind.Guard) || !ordinary.ContainsKey(CombatCommandKind.SwapSpell))
                throw new ArgumentException("Guard and SwapSpell definitions are required.", nameof(ordinaryActions));
            foreach (CombatantSnapshot actor in Combatants.All) ValidateModifiers(actor);

            SpatialQuery = spatialQuery;
            SpiritRecoveryEnvironmentMultiplier = spiritRecoveryEnvironmentMultiplier;
            TurnScheduler = new CombatTurnScheduler(Combatants.All);
        }

        public ICombatExternalResourceQuery ExternalResources { get; set; }

        public string CurrentActorId { get; private set; } = string.Empty;
        public CombatTurnPhase TurnPhase { get; private set; } = CombatTurnPhase.Finished;
        public int PendingCtPenalty { get; private set; }
        public long TurnSequence { get; private set; }
        public long StateVersion { get; private set; }
        public bool HasCommittedBenefit { get; private set; }
        internal bool IsFinished => !Combatants.HasLivingMembers(CombatTeam.Player) ||
            !Combatants.HasLivingMembers(CombatTeam.Enemy);

        internal bool BeginTurn(CombatantSnapshot actor)
        {
            CurrentActorId = actor.Id;
            TurnPhase = CombatTurnPhase.Active;
            PendingCtPenalty = 0;
            HasCommittedBenefit = false;
            TurnSequence = checked(TurnSequence + 1);
            StateVersion = checked(StateVersion + 1);
            return actor.BeginOwnTurn();
        }

        internal void RecordAction(int ctPenalty)
        {
            PendingCtPenalty = checked(PendingCtPenalty + ctPenalty);
            HasCommittedBenefit = true;
            StateVersion = checked(StateVersion + 1);
        }

        internal void FinishTurn()
        {
            if (Combatants.TryGet(CurrentActorId, out CombatantSnapshot actor)) actor.EndOwnTurn();
            if (TurnPhase != CombatTurnPhase.Finished) StateVersion = checked(StateVersion + 1);
            CurrentActorId = string.Empty;
            TurnPhase = CombatTurnPhase.Finished;
            PendingCtPenalty = 0;
        }

        internal void Terminate()
        {
            FinishTurn();
            foreach (CombatantSnapshot actor in Combatants.All) { actor.ClearActionPoints(); actor.IsGuarding = false; }
        }

        internal CombatOrdinaryActionDefinition GetOrdinary(CombatCommandKind kind) => ordinary[kind];

        private void ValidateModifiers(CombatantSnapshot actor)
        {
            if (actor.HealthRecoveryPerOwnTurn < 0 || actor.SpiritRecoveryPerOwnTurn < 0)
                throw new ArgumentException("Recovery inputs cannot be negative.");
            long budget = 0;
            foreach (CombatActionEconomyModifier modifier in actor.ActionEconomyModifiers)
            {
                budget = checked(budget + modifier.RefundBudgetPerOwnTurnUnits);
                if (budget > int.MaxValue) throw new ArgumentException("Refund budget overflow.");
                bool matched = false;
                foreach (CombatAttackProfile profile in orderedProfiles)
                {
                    CombatCommandKind kind = profile.Kind == CombatAttackKind.Basic ? CombatCommandKind.BasicAttack :
                        profile.Kind == CombatAttackKind.Art ? CombatCommandKind.Art : CombatCommandKind.Divine;
                    if (!modifier.Matches(kind, profile.Id)) continue;
                    matched = true;
                    ValidateModifierScope(actor, modifier, kind, profile.Id, profile.ActionPointCostUnits, profile.CtPenalty, profile.ReuseCooldownTicks);
                }
                foreach (var pair in ordinary)
                {
                    if (!modifier.Matches(pair.Key, string.Empty)) continue;
                    matched = true;
                    ValidateModifierScope(actor, modifier, pair.Key, string.Empty, pair.Value.ActionPointCostUnits,
                        pair.Value.CtPenalty, pair.Value.ReuseCooldownTicks);
                }
                if (!matched) throw new ArgumentException("Unknown action economy scope.");
            }
        }

        private static void ValidateModifierScope(CombatantSnapshot actor, CombatActionEconomyModifier modifier,
            CombatCommandKind kind, string profile, int cost, int ct, int cd)
        {
            if (modifier.MinimumNetCostUnits > cost || modifier.CtPenaltyReduction > ct || modifier.ReuseCooldownReductionTicks > cd ||
                actor.ActionEconomyModifiers.Count(value => value.Matches(kind, profile)) != 1)
                throw new ArgumentException("Invalid or overlapping action economy scope.");
        }

        public CombatantRegistry Combatants { get; }
        public ICombatSpatialQuery SpatialQuery { get; }
        public float SpiritRecoveryEnvironmentMultiplier { get; }
        public CombatTurnScheduler TurnScheduler { get; }
        public IReadOnlyList<CombatAttackProfile> AttackProfiles => orderedProfiles.AsReadOnly();

        public bool TryGetProfile(string id, out CombatAttackProfile profile)
        {
            profile = null;
            return !string.IsNullOrWhiteSpace(id) && profiles.TryGetValue(id, out profile);
        }

        /// <summary>Single side-effect-free command validation entry shared by resolution and action discovery.</summary>
        public CombatActionResult ValidateCommand(CombatCommand command)
            => new CombatCommandService().Validate(this, command);

        internal CombatActionResult ValidateRules(CombatCommand command)
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

            if (actor.HasStatus(CombatStatusKind.Stun)) return CombatActionResult.Rejected("combat_actor_stunned");

            return command.Kind switch
            {
                CombatCommandKind.EndTurn => CombatActionResult.Success(),
                CombatCommandKind.Guard => actor.IsGuarding ? CombatActionResult.Rejected("combat_guard_already_active") : CombatActionResult.Success(),
                CombatCommandKind.Wait => HasCommittedBenefit ? CombatActionResult.Rejected("combat_wait_after_benefit") : CombatActionResult.Success(),
                CombatCommandKind.BasicAttack => ValidateAttack(command, actor, CombatAttackKind.Basic),
                CombatCommandKind.Art => ValidateAttack(command, actor, CombatAttackKind.Art),
                CombatCommandKind.Divine => ValidateAttack(command, actor, CombatAttackKind.Divine),
                CombatCommandKind.Move => ValidateMove(command, actor),
                CombatCommandKind.SwapSpell => ValidateSwapSpell(command, actor),
                CombatCommandKind.SwapEquipment => ValidateEquipment(command, actor),
                _ => CombatActionResult.Rejected("combat_command_kind_invalid"),
            };
        }

        private CombatActionResult ValidateEquipment(CombatCommand command, CombatantSnapshot actor)
        {
            if (actor.Team != CombatTeam.Player || !ordinary.ContainsKey(CombatCommandKind.SwapEquipment) || ExternalResources == null)
                return CombatActionResult.Rejected("equipment_definition_invalid");
            CombatEquipmentResource resource = ExternalResources.QueryEquipment(command);
            if (resource == null) return CombatActionResult.Rejected("equipment_definition_invalid");
            if (!string.IsNullOrEmpty(resource.Reason)) return CombatActionResult.Rejected(resource.Reason);
            if (resource.ProfileId != command.ProfileId || !TryGetProfile(command.ProfileId, out var profile) ||
                profile.Kind != CombatAttackKind.Basic)
                return CombatActionResult.Rejected("equipment_definition_invalid");
            return CombatActionResult.Success();
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
            if (!TryGetProfile(command.ProfileId, out CombatAttackProfile profile) || profile.Kind != expectedKind)
                return CombatActionResult.Rejected("attack_profile_unresolved");
            if (expectedKind != CombatAttackKind.Basic && actor.HasStatus(CombatStatusKind.SealSpirit))
                return CombatActionResult.Rejected("combat_spirit_sealed");
            if (!Combatants.TryGet(command.TargetId, out CombatantSnapshot target) || !target.IsAlive ||
                (profile.Effect != CombatAttackEffect.RestoreSpirit && target.Team == actor.Team))
                return CombatActionResult.Rejected("combat_session_target_invalid");
            string authorizationFailure = GetAuthorizationFailure(actor, profile);
            if (!string.IsNullOrEmpty(authorizationFailure))
                return CombatActionResult.Rejected(authorizationFailure);
            if (!profile.MeetsRequirements(actor.RealmStage, actor.VisibleRootElement, actor.VisibleRootGrade))
                return CombatActionResult.Rejected("attack_profile_requirements_not_met");
            if (actor.GetCooldown(profile.Id) > 0)
                return CombatActionResult.Rejected("attack_profile_cooldown_active");
            bool targetsSelf = string.Equals(actor.Id, target.Id, StringComparison.Ordinal);
            int spiritCost = profile.GetSpiritCost(targetsSelf);
            if (actor.CurrentSpirit < spiritCost)
                return CombatActionResult.Rejected("spirit_insufficient");

            CombatRangeQueryResult range = SpatialQuery.QueryRange(
                actor.Position, target.Position,
                profile.Effect == CombatAttackEffect.RestoreSpirit && !targetsSelf
                    ? Math.Max(1, profile.MinimumRange) : profile.MinimumRange,
                profile.MaximumRange);
            return range.IsInRange
                ? CombatActionResult.Success()
                : CombatActionResult.Rejected(string.IsNullOrEmpty(range.Reason) ? "target_out_of_range" : range.Reason);
        }

        private static string GetAuthorizationFailure(CombatantSnapshot actor, CombatAttackProfile profile)
        {
            switch (profile.Kind)
            {
                case CombatAttackKind.Basic:
                    return string.Equals(actor.BasicAttackProfileId, profile.Id, StringComparison.Ordinal)
                        ? string.Empty : "attack_profile_not_authorized";
                case CombatAttackKind.Art:
                    if (!actor.AvailableArtProfileIds.Contains(profile.Id)) return "attack_profile_not_learned";
                    return actor.EquippedArtProfileIds.Contains(profile.Id) ? string.Empty : "attack_profile_not_equipped";
                case CombatAttackKind.Divine:
                    if (!actor.AvailableDivineProfileIds.Contains(profile.Id)) return "attack_profile_not_learned";
                    return actor.EquippedDivineProfileIds.Contains(profile.Id) ? string.Empty : "attack_profile_not_equipped";
                default:
                    return "attack_profile_not_authorized";
            }
        }

        private CombatActionResult ValidateMove(CombatCommand command, CombatantSnapshot actor)
        {
            if (!command.Destination.HasValue || command.Destination.Value == actor.Position)
                return CombatActionResult.Rejected("combat_move_destination_invalid");
            if (actor.HasStatus(CombatStatusKind.Immobilize)) return CombatActionResult.Rejected("combat_actor_immobilized");
            if (actor.MovementEfficiencyMilliUnits == 0) return CombatActionResult.Rejected("movement_efficiency_zero");
            if (actor.ActionPointsRemainingUnits <= 0) return CombatActionResult.Rejected("action_points_insufficient");

            IReadOnlyCollection<HexCoord> occupied = GetOccupiedPositionsExcept(actor.Id);
            if (occupied.Contains(command.Destination.Value))
                return CombatActionResult.Rejected("combat_move_destination_occupied");

            CombatMovementQueryResult movement = SpatialQuery.QueryMovement(
                actor.Position,
                command.Destination.Value,
                actor.ActionPointsRemainingUnits,
                actor.MovementEfficiencyMilliUnits,
                occupied);
            if (!movement.IsReachable || movement.Path.Count == 0 ||
                movement.Path[movement.Path.Count - 1] != command.Destination.Value ||
                movement.UnitsPerRange <= 0 || movement.MovementEdges.Count != movement.Path.Count ||
                movement.ActionPointCostUnits <= 0 || movement.ActionPointCostUnits > actor.ActionPointsRemainingUnits)
            {
                return CombatActionResult.Rejected(
                    string.IsNullOrEmpty(movement.Reason) ? "combat_move_path_invalid" : movement.Reason);
            }
            try
            {
                int cost = 0;
                HexCoord from = actor.Position;
                for (int index = 0; index < movement.Path.Count; index++)
                {
                    SpatialMovementEdgeCost edge = movement.MovementEdges[index];
                    if (edge.From != from || edge.To != movement.Path[index] || from.DirectionTo(edge.To) < 0 ||
                        edge.MetricDistanceUnits <= 0 || edge.MovementBurdenUnits < 0 || occupied.Contains(edge.To))
                        return CombatActionResult.Rejected("combat_move_path_invalid");
                    long numerator = checked(100000000L * checked(edge.MetricDistanceUnits + (long)edge.MovementBurdenUnits));
                    long denominator = checked((long)movement.UnitsPerRange * actor.MovementEfficiencyMilliUnits);
                    cost = checked(cost + checked((int)Math.Max(1L, numerator / denominator)));
                    from = edge.To;
                }
                if (cost != movement.ActionPointCostUnits) return CombatActionResult.Rejected("combat_move_quote_invalid");
            }
            catch (OverflowException) { return CombatActionResult.Rejected("combat_move_cost_overflow"); }
            HexCoord previous = movement.Path.Count == 1
                ? actor.Position
                : movement.Path[movement.Path.Count - 2];
            int facing = previous.DirectionTo(movement.Path[movement.Path.Count - 1]);
            if (facing < 0)
                return CombatActionResult.Rejected("combat_move_path_invalid");
            return CombatActionResult.MovementSuccess(movement.Path, movement.ActionPointCostUnits, facing,
                movement.MovementEdges, movement.UnitsPerRange);
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
