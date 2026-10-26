using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public enum CombatFlightAction { Takeoff, Ascend, Descend, Land, Recover }

    public sealed class CombatFlightCost
    {
        public CombatFlightCost(CombatFlightAction action, int actionPoints, int mana, int cooldown)
        {
            if (actionPoints <= 0 || mana < 0 || cooldown < 0) throw new ArgumentException("flight_cost_invalid");
            Action = action; ActionPoints = actionPoints; Mana = mana; Cooldown = cooldown;
        }
        public CombatFlightAction Action { get; }
        public int ActionPoints { get; }
        public int Mana { get; }
        public int Cooldown { get; }
    }

    public sealed class CombatFlightState
    {
        internal CombatFlightState(string actorId, string instanceId) { ActorId = actorId; InstanceId = instanceId; }
        public string ActorId { get; }
        public string InstanceId { get; }
        public bool Active { get; internal set; }
        public bool FailedLanding { get; internal set; }
        public bool SourceAvailable { get; internal set; } = true;
        public int RemainingOwnTurnEnds { get; internal set; }
        public int CreatedOwnTurn { get; internal set; }
        public int FailureOwnTurn { get; internal set; }
        public int MaintenancePayments { get; internal set; }
        public long TotalManaSpent { get; internal set; }
        public long TotalActionPointsSpent { get; internal set; }
        public string LastEndReason { get; internal set; } = string.Empty;
        public string ProcessState => FailedLanding ? "demo_20261024_flight_failed_landing" : string.Empty;
    }

    /// <summary>Independent ability lifecycle; uses the owning session's AP, MP, CD and own-turn clock.</summary>
    public sealed class CombatFlight
    {
        public const string ProfileId = "demo_20261024_yufeng_flight";
        private readonly Dictionary<string, CombatFlightState> states;
        private readonly Dictionary<CombatFlightAction, CombatFlightCost> costs;
        public CombatFlight(IEnumerable<KeyValuePair<string, string>> sources, IEnumerable<CombatFlightCost> commands,
            int upkeepMana, int durationOwnTurnEnds)
        {
            states = sources.ToDictionary(value => value.Key, value => new CombatFlightState(value.Key, value.Value), StringComparer.Ordinal);
            costs = commands.ToDictionary(value => value.Action);
            if (states.Count == 0 || states.Values.Any(value => string.IsNullOrWhiteSpace(value.InstanceId)) ||
                states.Values.Select(value => value.InstanceId).Distinct().Count() != states.Count || costs.Count != 5 ||
                upkeepMana <= 0 || durationOwnTurnEnds <= 0) throw new ArgumentException("flight_definition_invalid");
            UpkeepMana = upkeepMana; DurationOwnTurnEnds = durationOwnTurnEnds;
            States = Array.AsReadOnly(states.Values.ToArray());
        }
        public int UpkeepMana { get; }
        public int DurationOwnTurnEnds { get; }
        public long Revision { get; private set; }
        public IReadOnlyList<CombatFlightState> States { get; }
        public CombatFlightState Find(string actorId) => states.TryGetValue(actorId, out var state) ? state : null;
        public CombatFlightCost Cost(CombatFlightAction action) => costs[action];

        internal void ValidateBindings(CombatSession session)
        {
            if (session.SceneObjects == null) throw new ArgumentException("flight_board_missing");
            foreach (var state in States)
                if (!session.Combatants.TryGet(state.ActorId, out var actor) || actor.Footprint != SpatialFootprintKind.Single ||
                    actor.BodyScaleRank != 1 || actor.GetAbilityInstanceId(ProfileId) != state.InstanceId)
                    throw new ArgumentException("flight_source_binding_invalid");
        }

        public string LandingReason(CombatSession session, CombatantSnapshot actor) => session.SceneObjects.Board.InspectPlacement(
            Pose(actor, SpatialMovementMode.Ground, 0), session.GetOccupiedPositionsExcept(actor.Id));

        public string MovementReason(CombatSession session, CombatantSnapshot actor)
        {
            if (actor.MovementMode != SpatialMovementMode.Fly) return string.Empty;
            var state = Find(actor.Id);
            if (state == null || !state.Active || state.FailedLanding) return "flight_not_active";
            return FlightReason(session, actor, state, actor.AltitudeBand);
        }

        internal CombatActionResult Validate(CombatSession session, CombatCommand command, CombatantSnapshot actor)
        {
            var state = Find(actor.Id);
            if (state == null || command.ProfileId != ProfileId || !command.FlightAction.HasValue ||
                !costs.ContainsKey(command.FlightAction.Value)) return Reject("flight_source_missing");
            var action = command.FlightAction.Value;
            if (actor.HasStatus(CombatStatusKind.Immobilize)) return Reject("combat_actor_immobilized");
            string reason;
            switch (action)
            {
                case CombatFlightAction.Takeoff:
                    if (state.Active || state.FailedLanding || actor.MovementMode != SpatialMovementMode.Ground)
                        return Reject("flight_takeoff_requires_ground");
                    reason = FlightReason(session, actor, state, 1); break;
                case CombatFlightAction.Recover:
                    if (!state.FailedLanding) return Reject("flight_recovery_requires_failure");
                    reason = FlightReason(session, actor, state, 1); break;
                case CombatFlightAction.Land:
                    if (!state.Active || actor.AltitudeBand != 1) return Reject("flight_landing_requires_low");
                    reason = LandingReason(session, actor); break;
                default:
                    if (!state.Active || state.FailedLanding || actor.MovementMode != SpatialMovementMode.Fly ||
                        actor.AltitudeBand != (action == CombatFlightAction.Ascend ? 1 : 2)) return Reject("flight_altitude_invalid");
                    reason = FlightReason(session, actor, state, action == CombatFlightAction.Ascend ? 2 : 1); break;
            }
            if (reason.Length > 0) return Reject(reason);
            if (action is CombatFlightAction.Takeoff or CombatFlightAction.Recover && actor.GetCooldown(ProfileId) > 0)
                return Reject("combat_ability_on_cooldown");
            if (actor.CurrentSpirit < costs[action].Mana) return Reject("spirit_insufficient");
            return CombatActionResult.Success();
        }

        internal CombatActionResult Prepare(CombatSession session, CombatCommand command, out Action apply)
        {
            session.Combatants.TryGet(command.ActorId, out var actor);
            var state = Find(actor.Id); var action = command.FlightAction.Value; var cost = costs[action];
            int altitude = action == CombatFlightAction.Land ? 0 : action == CombatFlightAction.Ascend ? 2 : 1;
            apply = () =>
            {
                actor.TryConsumeSpirit(cost.Mana);
                actor.SetMovementMode(altitude == 0 ? SpatialMovementMode.Ground : SpatialMovementMode.Fly, altitude);
                actor.IsGuarding = false;
                state.TotalActionPointsSpent += cost.ActionPoints; state.TotalManaSpent += cost.Mana;
                if (action is CombatFlightAction.Takeoff or CombatFlightAction.Recover)
                {
                    state.Active = true; state.FailedLanding = false;
                    state.CreatedOwnTurn = actor.OwnTurnsStarted; state.RemainingOwnTurnEnds = DurationOwnTurnEnds;
                    state.LastEndReason = string.Empty; actor.SetCooldown(ProfileId, cost.Cooldown);
                }
                else if (action == CombatFlightAction.Land) Stop(state, "flight_landed");
                Revision++;
            };
            return CombatActionResult.AttackSuccess(cost.Mana);
        }

        internal void BeginOwnTurn(CombatSession session, CombatantSnapshot actor)
        {
            Reconcile(session);
            var state = Find(actor.Id);
            if (state == null || !actor.IsAlive || !state.Active || actor.OwnTurnsStarted <= state.CreatedOwnTurn) return;
            if (!actor.TryConsumeSpirit(UpkeepMana)) Fail(session, actor, state, "flight_upkeep_failed");
            else { state.TotalManaSpent += UpkeepMana; state.MaintenancePayments++; Revision++; }
        }

        internal void EndOwnTurn(CombatSession session, CombatantSnapshot actor)
        {
            var state = Find(actor.Id);
            if (state == null || !actor.IsAlive) { Reconcile(session); return; }
            if (state.FailedLanding && actor.OwnTurnsStarted > state.FailureOwnTurn)
            { actor.ReceiveDamage((int)(((long)actor.MaximumHealth + 4) / 5)); Revision++; }
            else if (state.Active && actor.OwnTurnsStarted > state.CreatedOwnTurn)
            {
                state.RemainingOwnTurnEnds--; Revision++;
                if (state.RemainingOwnTurnEnds == 0) Fail(session, actor, state, "flight_expired");
            }
            Reconcile(session);
        }

        public void SetSourceAvailable(CombatSession session, string actorId, bool available)
        {
            var state = Find(actorId) ?? throw new ArgumentException("flight_source_missing");
            if (state.SourceAvailable == available) return;
            state.SourceAvailable = available; Revision++; Reconcile(session);
        }

        internal void Reconcile(CombatSession session)
        {
            foreach (var state in States)
            {
                session.Combatants.TryGet(state.ActorId, out var actor);
                if (!actor.IsAlive)
                {
                    if (state.Active || state.FailedLanding)
                    { Stop(state, "flight_owner_dead"); actor.SetMovementMode(SpatialMovementMode.Ground, 0); Revision++; }
                    continue;
                }
                if (state.FailedLanding)
                {
                    if (LandingReason(session, actor).Length == 0)
                    { actor.SetMovementMode(SpatialMovementMode.Ground, 0); Stop(state, "flight_rescued_landing"); Revision++; }
                }
                else if (state.Active)
                {
                    if (CombatForceMovement.LoadRank(actor) == 3 && actor.AltitudeBand == 2)
                    { actor.SetMovementMode(SpatialMovementMode.Fly, 1); Revision++; }
                    string reason = FlightReason(session, actor, state, actor.AltitudeBand);
                    if (reason.Length > 0) Fail(session, actor, state, reason);
                }
            }
            // A forced landing can kill a gravity target or its source in this same settlement.
            session.Jindan?.Reconcile(session);
        }

        public void Clear(CombatSession session)
        {
            foreach (var state in States)
            {
                session.Combatants.TryGet(state.ActorId, out var actor);
                actor.SetMovementMode(SpatialMovementMode.Ground, 0);
                Stop(state, "flight_session_ended");
            }
            Revision++;
        }

        private string FlightReason(CombatSession session, CombatantSnapshot actor, CombatFlightState state, int altitude)
        {
            if (!state.SourceAvailable || actor.GetAbilityInstanceId(ProfileId) != state.InstanceId) return "flight_source_missing";
            if (actor.HasStatus(CombatStatusKind.SealSpirit)) return "combat_actor_sealed";
            int load = CombatForceMovement.LoadRank(actor);
            if (load >= 4 || load == 3 && altitude > 1) return "flight_load_exceeded";
            return session.SceneObjects.Board.InspectPlacement(Pose(actor, SpatialMovementMode.Fly, altitude),
                session.GetOccupiedPositionsExcept(actor.Id));
        }

        private void Fail(CombatSession session, CombatantSnapshot actor, CombatFlightState state, string reason)
        {
            if (!state.Active || state.FailedLanding) return;
            bool land = LandingReason(session, actor).Length == 0;
            Stop(state, reason);
            if (land) actor.SetMovementMode(SpatialMovementMode.Ground, 0);
            else { state.FailedLanding = true; state.FailureOwnTurn = actor.OwnTurnsStarted; }
            actor.ReceiveDamage((int)(((long)actor.MaximumHealth + 9) / 10));
            if (!actor.IsAlive) { Stop(state, "flight_owner_dead"); actor.SetMovementMode(SpatialMovementMode.Ground, 0); }
            Revision++;
        }

        private static void Stop(CombatFlightState state, string reason)
        { state.Active = false; state.FailedLanding = false; state.RemainingOwnTurnEnds = 0; state.LastEndReason = reason; }
        private static SpatialPose Pose(CombatantSnapshot actor, SpatialMovementMode mode, int altitude) =>
            new SpatialPose(actor.Position, actor.Facing, actor.Footprint, actor.BodyScaleRank, mode, altitude);
        private static CombatActionResult Reject(string reason) => CombatActionResult.Rejected(reason);
    }
}
