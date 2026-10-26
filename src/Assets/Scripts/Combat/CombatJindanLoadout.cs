using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    /// <summary>Session-owned seats and receipts. AP/MP/CD and time remain owned by Combat.</summary>
    public sealed class CombatJindanLoadout
    {
        private readonly Dictionary<string, CombatJindanAbilityDefinition> abilities;
        private readonly Dictionary<string, CombatJindanSeatState> seats;
        public CombatJindanLoadout(IEnumerable<CombatJindanSeatDefinition> seatDefinitions,
            IEnumerable<CombatJindanAbilityDefinition> abilityDefinitions, int maxSwitchesPerOwnAction)
        {
            if (maxSwitchesPerOwnAction != 1) throw new ArgumentException("jindan_switch_tuning_invalid");
            abilities = abilityDefinitions.ToDictionary(value => value.EffectId, StringComparer.Ordinal);
            seats = seatDefinitions.ToDictionary(value => value.SeatId, value => new CombatJindanSeatState(value), StringComparer.Ordinal);
            var definitions = seats.Values.Select(value => value.Definition).ToArray();
            if (definitions.Length == 0 || definitions.Select(value => value.PositionId).Distinct().Count() != definitions.Length ||
                definitions.Select(value => value.AbilityInstanceId).Distinct().Count() != definitions.Length ||
                definitions.Any(value => value.Candidates.Any(id => !abilities.ContainsKey(id))) ||
                definitions.GroupBy(value => value.ActorId).Any(group => group.Count() > 3 ||
                    group.Select(value => value.PositionType).Distinct().Count() != group.Count()))
                throw new ArgumentException("jindan_seat_ownership_invalid");
            Seats = Array.AsReadOnly(seats.Values.ToArray());
        }
        public IReadOnlyList<CombatJindanSeatState> Seats { get; }
        public long Revision { get; private set; }
        public CombatJindanSeatState Find(string seatId) => seats.TryGetValue(seatId ?? string.Empty, out var value) ? value : null;
        public CombatJindanAbilityDefinition Ability(string effectId) => abilities[effectId];

        internal void ValidateBindings(CombatSession session)
        {
            if (session.SceneObjects == null) throw new ArgumentException("jindan_board_missing");
            foreach (var seat in Seats)
            {
                if (!session.Combatants.TryGet(seat.Definition.ActorId, out var actor) ||
                    seat.Definition.Candidates.Any(id => actor.GetAbilityInstanceId(id) != seat.Definition.AbilityInstanceId))
                    throw new ArgumentException("jindan_ability_instance_binding_invalid");
            }
        }

        public (int actionPoints, int mana, int cooldown) Costs(CombatCommand command, CombatantSnapshot actor)
        {
            var seat = Find(command.Jindan?.SeatId);
            if (seat == null) return (0, 0, 0);
            var definition = abilities[seat.EffectId];
            if (command.Kind==CombatCommandKind.JindanAbility && seat.EffectId=="WATER_TIDE" && definition.Tide!=null)
            {
                var tide=definition.Tide;
                return (checked(definition.ActionPoints+(command.Jindan.Charged?tide.ChargeExtraActionPoints:0)+(command.Jindan.CreateTideEye?tide.EyeExtraActionPoints:0)),
                    checked(definition.Mana+(command.Jindan.Charged?tide.ChargeExtraMana:0)+(command.Jindan.CreateTideEye?tide.EyeExtraMana:0)),definition.Cooldown);
            }
            return command.Kind switch
            {
                CombatCommandKind.SwapJindan => (actor.ActionPointLimitAtTurnStartUnits, 0, 0),
                CombatCommandKind.CloseJindan => (definition.CloseActionPoints, 0, 0),
                CombatCommandKind.JindanAbility => (definition.ActionPoints,
                    checked(definition.Mana + (command.Jindan.RemoteOrigin.HasValue ? definition.RemoteMana : 0)), definition.Cooldown),
                _ => (0, 0, 0),
            };
        }

        internal CombatActionResult Validate(CombatSession session, CombatCommand command, CombatantSnapshot actor)
        {
            var seat = Find(command.Jindan?.SeatId);
            if (seat == null || seat.Definition.ActorId != actor.Id) return Reject("jindan_seat_not_owned");
            if (command.Kind == CombatCommandKind.SwapJindan)
            {
                if (!seat.Definition.Candidates.Contains(command.ProfileId) || command.ProfileId == seat.EffectId)
                    return Reject("jindan_effect_candidate_invalid");
                if (session.HasCommittedBenefit || actor.ActionPointsRemainingUnits != actor.ActionPointLimitAtTurnStartUnits ||
                    actor.ActionPointLimitAtTurnStartUnits <= 0 || seat.LastSwitchTurn == session.TurnSequence)
                    return Reject("jindan_switch_requires_unused_full_turn");
                return CombatActionResult.Success();
            }
            if (command.ProfileId != seat.EffectId) return Reject("jindan_effect_not_equipped");
            var definition = abilities[seat.EffectId];
            if (command.Kind == CombatCommandKind.CloseJindan)
                return seat.ActiveScale == null && seat.ActiveSeal == null && seat.ActiveEarth == null && seat.PendingTide==null &&
                    (session.Surfaces?.OwnedCount(seat.Definition.AbilityInstanceId)??0)==0 ? Reject("jindan_instance_missing") : CombatActionResult.Success();
            if (!definition.Enabled) return Reject(definition.UnavailableReason);
            if (seat.LockedOwnTurn >= actor.OwnTurnsStarted) return Reject("jindan_effect_wait_next_own_turn");
            if (actor.HasStatus(CombatStatusKind.SealSpirit)) return Reject("combat_actor_sealed");
            if (actor.GetCooldown(seat.EffectId) > 0) return Reject("combat_ability_on_cooldown");
            if (seat.ActiveScale != null || seat.ActiveSeal != null) return Reject("jindan_capacity_full");
            if (seat.PendingTide!=null) return Reject("tide_process_pending");
            if (actor.CurrentSpirit < Costs(command, actor).mana) return Reject("spirit_insufficient");
            string reason = seat.EffectId == "SPACE_SEAL" ? CombatSealRules.Validate(session, actor, command.Jindan, definition)
                : seat.EffectId=="WATER_TIDE" ? CombatTideRules.Preview(session,actor,seat,command.Jindan).Reason : seat.EffectId is "EARTH_MANIFEST" or "EARTH_GRAVITY"
                ? CombatEarthRules.Validate(session, actor, seat, command.Jindan, definition)
                : CombatScaleRules.Validate(session, actor, command.Jindan, definition);
            if (reason.Length != 0) return Reject(reason);
            if (seat.SuccessfulDeployments == int.MaxValue || seat.TotalManaSpent > int.MaxValue - Costs(command, actor).mana)
                return Reject("combat_effect_overflow");
            return CombatActionResult.Success();
        }

        internal CombatActionResult Prepare(CombatSession session, CombatCommand command, out Action apply)
        {
            apply = null;
            var seat = Find(command.Jindan.SeatId);
            session.Combatants.TryGet(command.ActorId, out var actor);
            var definition = abilities[seat.EffectId];
            var costs = Costs(command, actor);
            if (seat.EffectId=="WATER_TIDE" && command.Kind!=CombatCommandKind.SwapJindan)
                return PrepareTide(session,command,actor,seat,costs,out apply);
            CombatScaleInstance next = null;
            CombatSealInstance nextSeal = null;
            CombatEarthInstance nextEarth = null;
            CombatOwnTurnStatus unitStatus = null;
            if (command.Kind == CombatCommandKind.JindanAbility && seat.EffectId == "SPACE_SEAL")
                nextSeal = CombatSealRules.Prepare(session, actor, seat, definition);
            else if (command.Kind == CombatCommandKind.JindanAbility && seat.EffectId is "EARTH_MANIFEST" or "EARTH_GRAVITY")
                nextEarth = CombatEarthRules.Prepare(session, actor, seat, command.Jindan, definition, out unitStatus);
            else if (command.Kind == CombatCommandKind.JindanAbility)
            {
                string id = seat.Definition.AbilityInstanceId + ":support:" + (seat.SuccessfulDeployments + 1);
                var support = new CombatSceneObject(id, new CombatSceneObjectDefinition("stone_support", "stone",
                    definition.SupportDurability, 0, 0, false, false, "failed", isSupportMarker: true),
                    command.Jindan.EdgePath[0], command.Jindan.EdgePath[1], seat.Definition.AbilityInstanceId);
                next = new CombatScaleInstance(id, session.TurnScheduler.CurrentTick,
                    command.Jindan.RemoteOrigin ?? actor.Position, command.Jindan.EdgePath,
                    command.Jindan.MetricDistanceUnits, support);
            }
            Action publish = PrepareProjection(session, seat, next, nextEarth, nextSeal);
            apply = () =>
            {
                actor.TryConsumeSpirit(costs.mana);
                actor.IsGuarding = false;
                publish();
                RemoveGravity(session, seat);
                if (unitStatus != null && session.Combatants.TryGet(nextEarth.TargetId, out var target))
                    target.ApplyEarthGravity(unitStatus);
                seat.TotalManaSpent += costs.mana;
                seat.TotalActionPointSpent += costs.actionPoints;
                seat.ActiveScale = next;
                seat.ActiveSeal = nextSeal;
                seat.ActiveEarth = nextEarth;
                if (command.Kind == CombatCommandKind.SwapJindan)
                {
                    seat.PendingTide=null;
                    seat.EffectId = command.ProfileId; seat.LastSwitchTurn = session.TurnSequence;
                    seat.LockedOwnTurn = actor.OwnTurnsStarted; seat.LastEndReason = "jindan_switched_end";
                }
                else if (next != null || nextEarth != null || nextSeal != null)
                {
                    actor.SetCooldown(seat.EffectId, costs.cooldown);
                    seat.SuccessfulDeployments++;
                    seat.LastEndReason = string.Empty;
                }
                else seat.LastEndReason = "jindan_closed";
                Revision++;
            };
            return CombatActionResult.AttackSuccess(costs.mana);
        }

        /// <summary>Called only for elapsed ticks returned by the existing CT scheduler.</summary>
        internal void AdvanceTicks(CombatSession session, int elapsed)
        {
            Reconcile(session);
            int now = session.TurnScheduler.CurrentTick;
            for (int tick = now - elapsed + 1; tick <= now && elapsed > 0; tick++)
            {
                // The scheduler may return several ticks at once. Process expiry/payment before each tick's effects.
                foreach (var seat in Seats.Where(value => value.ActiveSeal != null).ToArray())
                {
                    var instance = seat.ActiveSeal;
                    if (tick <= instance.LastPaidTick) continue;
                    if (tick >= instance.ExpiresTick) { End(session, seat, "seal_expired"); continue; }
                    session.Combatants.TryGet(seat.Definition.ActorId, out var owner);
                    int upkeep = abilities[seat.EffectId].UpkeepMana;
                    if (seat.TotalManaSpent > int.MaxValue - upkeep || seat.MaintenancePayments == int.MaxValue ||
                        !owner.TryConsumeSpirit(upkeep)) { End(session, seat, "jindan_upkeep_failed"); continue; }
                    seat.TotalManaSpent += upkeep; seat.MaintenancePayments++;
                    instance.LastPaidTick = tick; Revision++;
                }
                CompleteTides(session,tick);
                if (session.IsFinished) return;
                session.Surfaces?.AdvanceTick(session,tick);
                if (session.IsFinished) return;
                foreach (var seat in Seats.Where(value => value.ActiveScale != null).ToArray())
                {
                    var instance = seat.ActiveScale;
                    if (tick <= instance.LastPaidTick) continue;
                    session.Combatants.TryGet(seat.Definition.ActorId, out var actor);
                    int cost = abilities[seat.EffectId].UpkeepMana;
                    if (seat.TotalManaSpent > int.MaxValue - cost || seat.MaintenancePayments == int.MaxValue ||
                        !actor.TryConsumeSpirit(cost)) { End(session, seat, "jindan_upkeep_failed"); continue; }
                    seat.TotalManaSpent += cost; seat.MaintenancePayments++;
                    instance.LastPaidTick = tick; Revision++;
                }
                if (tick == int.MaxValue) break;
            }
        }

        internal void Reconcile(CombatSession session)
        {
            foreach (var seat in Seats.Where(value => value.ActiveSeal != null).ToArray())
            {
                var seal = seat.ActiveSeal;
                session.Combatants.TryGet(seat.Definition.ActorId, out var owner);
                if (!owner.IsAlive) End(session, seat, "jindan_owner_dead");
                else if (owner.HasStatus(CombatStatusKind.SealSpirit)) End(session, seat, "jindan_source_disabled");
                else if (!seal.Definition.ContainsOwner(owner.Position)) End(session, seat, "seal_owner_outside_domain");
                else if (seal.Support.IsDestroyed) End(session, seat, "jindan_support_destroyed");
                else if (!seal.OuterLayerBroken && seal.OuterDurability == 0)
                {
                    seal.OuterLayerBroken = true;
                    // LastPaidTick is the actual processed tick, including inside a multi-tick scheduler advance.
                    seal.ExpiresTick = (int)Math.Min(seal.ExpiresTick, (long)seal.LastPaidTick + seal.Definition.BrokenLayerTicks);
                    Revision++;
                }
            }
            foreach (var seat in Seats)
            {
                seat.OwnedSurfaceCount=session.Surfaces?.OwnedCount(seat.Definition.AbilityInstanceId)??0;
                if (seat.PendingTide==null) continue;
                session.Combatants.TryGet(seat.Definition.ActorId,out var tideActor);
                var request=seat.PendingTide.Request;
                var kind=session.Surfaces?.Get(request.TideSource.Value)?.Kind;
                bool sourceLost=!request.CreateTideEye && (request.TideAction==CombatTideAction.Rise
                    ? kind is not (CombatSurfaceKind.ShallowWater or CombatSurfaceKind.DeepWater)
                    : kind is not (CombatSurfaceKind.Wet or CombatSurfaceKind.ShallowWater or CombatSurfaceKind.DeepWater));
                if (!tideActor.IsAlive || tideActor.HasStatus(CombatStatusKind.SealSpirit) || seat.EffectId!="WATER_TIDE" || sourceLost)
                { seat.PendingTide=null;seat.LastEndReason="tide_process_interrupted";Revision++; }
            }
            foreach (var seat in Seats.Where(value => value.ActiveScale != null).ToArray())
            {
                session.Combatants.TryGet(seat.Definition.ActorId, out var actor);
                if (!actor.IsAlive) End(session, seat, "jindan_owner_dead");
                else if (actor.HasStatus(CombatStatusKind.SealSpirit)) End(session, seat, "jindan_source_disabled");
                else if (seat.ActiveScale.Support.IsDestroyed) End(session, seat, "jindan_support_destroyed");
            }
            foreach (var seat in Seats.Where(value => value.ActiveEarth != null).ToArray())
            {
                session.Combatants.TryGet(seat.Definition.ActorId, out var actor);
                var earth = seat.ActiveEarth;
                if (!actor.IsAlive) End(session, seat, "jindan_owner_dead");
                else if (actor.HasStatus(CombatStatusKind.SealSpirit)) End(session, seat, "jindan_source_disabled");
                else if (earth.TargetId != null)
                {
                    if (session.Combatants.TryGet(earth.TargetId, out var target))
                    {
                        if (!target.IsAlive || !target.Statuses.Any(value => value.Kind == CombatStatusKind.EarthGravity &&
                            value.SourceRef == seat.Definition.AbilityInstanceId)) End(session, seat, "earth_status_ended");
                    }
                    else if (session.SceneObjects.Find(earth.TargetId) is not {IsDestroyed:false})
                        End(session, seat, "earth_target_ended");
                }
                else if (earth.Objects.Any(value => value.IsDestroyed))
                {
                    earth.Objects = earth.Objects.Where(value => !value.IsDestroyed).ToArray();
                    if (earth.Objects.Count == 0) End(session, seat, "earth_components_destroyed");
                    else { PrepareProjection(session, seat, null, earth)(); Revision++; }
                }
            }
        }

        internal void EndOwnTurn(CombatSession session, CombatantSnapshot actor)
        {
            foreach (var seat in Seats.Where(value => value.ActiveEarth != null && value.Definition.ActorId == actor.Id).ToArray())
            {
                var earth = seat.ActiveEarth;
                if (earth.TargetId != null && earth.ObjectStatus == null) continue;
                if (actor.OwnTurnsStarted <= earth.CreatedOwnTurn) continue;
                earth.RemainingOwnTurnEnds--;
                if (earth.ObjectStatus != null) earth.ObjectStatus.RemainingOwnTurnEnds = earth.RemainingOwnTurnEnds;
                Revision++;
                if (earth.RemainingOwnTurnEnds == 0) End(session, seat, "earth_expired");
            }
            Reconcile(session);
        }

        internal void MovementCommitted(CombatSession session, CombatantSnapshot actor, IReadOnlyList<SpatialMovementStep> steps)
        {
            foreach (var seat in Seats.Where(value => value.Definition.ActorId == actor.Id && value.ActiveSeal != null).ToArray())
                if (steps.Any(step => !seat.ActiveSeal.Definition.ContainsOwner(step.To.Anchor)))
                    End(session, seat, "seal_owner_outside_domain");
        }

        public void Clear(CombatSession session)
        {
            foreach (var seat in Seats.Where(value => value.ActiveScale != null || value.ActiveSeal != null || value.ActiveEarth != null || value.PendingTide!=null || value.OwnedSurfaceCount>0).ToArray())
                End(session, seat, "jindan_session_ended");
            session.Surfaces?.Clear(session);
        }

        private void End(CombatSession session, CombatJindanSeatState seat, string reason)
        {
            PrepareProjection(session, seat, null, null)();
            RemoveGravity(session, seat);
            seat.ActiveScale = null; seat.ActiveSeal = null; seat.ActiveEarth = null; seat.PendingTide=null; seat.OwnedSurfaceCount=0; seat.LastEndReason = reason; Revision++;
        }

        private CombatActionResult PrepareTide(CombatSession session,CombatCommand command,CombatantSnapshot actor,
            CombatJindanSeatState seat,(int actionPoints,int mana,int cooldown) costs,out Action apply)
        {
            Action settlement=null; CombatPendingTide pending=null;
            if (command.Kind==CombatCommandKind.JindanAbility)
            {
                if(command.Jindan.Charged)
                    pending=new CombatPendingTide(command.Jindan,checked(session.TurnScheduler.CurrentTick+abilities[seat.EffectId].Tide.ChargeTicks));
                else settlement=CombatTideRules.PrepareSettlement(session,actor,seat,command.Jindan,
                    CombatTideRules.Preview(session,actor,seat,command.Jindan),session.TurnScheduler.CurrentTick);
            }
            apply=()=>
            {
                settlement?.Invoke();
                actor.TryConsumeSpirit(costs.mana);actor.IsGuarding=false;
                seat.TotalManaSpent+=costs.mana;seat.TotalActionPointSpent+=costs.actionPoints;seat.PendingTide=pending;
                if(command.Kind==CombatCommandKind.CloseJindan)
                {session.Surfaces.ClearSource(session,seat.Definition.AbilityInstanceId);seat.LastEndReason="jindan_closed";}
                else {actor.SetCooldown(seat.EffectId,costs.cooldown);seat.SuccessfulDeployments++;seat.LastEndReason="";}
                seat.OwnedSurfaceCount=session.Surfaces.OwnedCount(seat.Definition.AbilityInstanceId);Revision++;
            };
            return CombatActionResult.AttackSuccess(costs.mana);
        }

        private void CompleteTides(CombatSession session,int tick)
        {
            Reconcile(session);
            foreach(var seat in Seats.Where(value=>value.PendingTide!=null && value.PendingTide.DueTick<=tick).ToArray())
            {
                session.Combatants.TryGet(seat.Definition.ActorId,out var actor);
                var request=seat.PendingTide.Request;
                var preview=CombatTideRules.Preview(session,actor,seat,request);
                if(preview.IsLegal)
                    CombatTideRules.PrepareSettlement(session,actor,seat,request,preview,tick)();
                seat.PendingTide=null;seat.LastEndReason=preview.IsLegal?"":"tide_process_invalid:"+preview.Reason;
                seat.OwnedSurfaceCount=session.Surfaces.OwnedCount(seat.Definition.AbilityInstanceId);Revision++;
                Reconcile(session);session.Flight?.Reconcile(session);
                if (session.IsFinished) return;
            }
        }

        private static void RemoveGravity(CombatSession session, CombatJindanSeatState seat)
        {
            if (seat.ActiveEarth?.TargetId != null && session.Combatants.TryGet(seat.ActiveEarth.TargetId, out var target))
                target.RemoveEarthGravity(seat.Definition.AbilityInstanceId);
        }

        private Action PrepareProjection(CombatSession session, CombatJindanSeatState changed, CombatScaleInstance next,
            CombatEarthInstance nextEarth, CombatSealInstance nextSeal = null)
        {
            var instances = Seats.Select(seat => (seat, instance: seat == changed ? next : seat.ActiveScale))
                .Where(value => value.instance != null).ToArray();
            var rules = new List<SpatialMetricOverride>();
            foreach (var pair in instances)
                for (int i = 1; i < pair.instance.Path.Count; i++)
                    rules.Add(new SpatialMetricOverride(pair.seat.Definition.AbilityInstanceId,
                        pair.instance.Path[i - 1], pair.instance.Path[i], pair.instance.MetricDistanceUnits));
            var earthObjects = Seats.Select(seat => seat == changed ? nextEarth : seat.ActiveEarth)
                .Where(value => value != null).SelectMany(value => value.Objects);
            var supports = Seats.Select(seat => seat == changed ? nextSeal : seat.ActiveSeal)
                .Where(value => value != null).Select(value => value.Support);
            return session.SceneObjects.PrepareAbilityObjects(instances.Select(value => value.instance.Support)
                .Concat(earthObjects).Concat(supports).ToArray(), rules);
        }
        private static CombatActionResult Reject(string reason) => CombatActionResult.Rejected(reason);
    }
}
