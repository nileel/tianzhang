using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public sealed class CombatFireDomainDefinition
    {
        public CombatFireDomainDefinition(HexCoord center, string supportId, int fuelBudget, int tailFuel,
            int tailCycles, int attackCapacity, float damageMultiplier, bool chooseCenter = false)
        {
            if (string.IsNullOrWhiteSpace(supportId) || fuelBudget < tailFuel || tailFuel < 1 || tailCycles < 1 ||
                attackCapacity < 1 || damageMultiplier <= 0 || float.IsNaN(damageMultiplier) || float.IsInfinity(damageMultiplier))
                throw new ArgumentException("fire_domain_definition_invalid");
            Center = center; ChooseCenter = chooseCenter; SupportId = supportId; FuelBudget = fuelBudget; TailFuel = tailFuel;
            TailCycles = tailCycles; AttackCapacity = attackCapacity; DamageMultiplier = damageMultiplier;
        }
        public HexCoord Center { get; }
        public bool ChooseCenter { get; }
        public string SupportId { get; }
        public int FuelBudget { get; }
        public int TailFuel { get; }
        public int TailCycles { get; }
        public int AttackCapacity { get; }
        public float DamageMultiplier { get; }
    }

    public sealed class CombatFireDomainInstance
    {
        internal CombatFireDomainInstance(long ownTurn, int cycles, int reservedFuel)
        { CreatedOwnTurn = ownTurn; RemainingCycles = cycles; ReservedFuel = reservedFuel; }
        internal readonly HashSet<HexCoord> cells = new HashSet<HexCoord>();
        public IReadOnlyCollection<HexCoord> Cells => cells.ToArray();
        public long CreatedOwnTurn { get; internal set; }
        public int RemainingCycles { get; internal set; }
        public int ReservedFuel { get; internal set; }
    }

    /// <summary>The existing field node owns finite fuel; main and paid tails share the guardian's capacity.</summary>
    public sealed class CombatFireDomainState
    {
        internal CombatFireDomainState(CombatJindanSeatDefinition seat, CombatJindanAbilityDefinition ability)
        {
            var rules = ability.FireDomain;
            RemainingFuel = rules.FuelBudget;
            HasPlacedSupport = !rules.ChooseCenter;
            Support = new CombatSceneObject(seat.AbilityInstanceId + ":" + rules.SupportId,
                new CombatSceneObjectDefinition("stone_support", "stone", ability.SupportDurability,
                    0, 0, false, false, "none", isSupportMarker: true), rules.Center, sourceRef: seat.AbilityInstanceId);
        }
        internal readonly List<CombatFireDomainInstance> tails = new List<CombatFireDomainInstance>();
        public CombatSceneObject Support { get; private set; }
        public bool HasPlacedSupport { get; private set; }

        internal void PlaceSupport(HexCoord center)
        {
            if (HasPlacedSupport) return;
            Support = new CombatSceneObject(Support.Id, Support.Definition, center, sourceRef: Support.SourceRef);
            HasPlacedSupport = true;
        }
        public int RemainingFuel { get; internal set; }
        public CombatFireDomainInstance Main { get; internal set; }
        public IReadOnlyList<CombatFireDomainInstance> Tails => tails.AsReadOnly();
        public int ReservedFuel => (Main?.ReservedFuel ?? 0) + tails.Sum(value => value.ReservedFuel);
        internal bool Cleared { get; set; }
    }

    public static class CombatFireDomainRules
    {
        public const string Effect = "FIRE_MANIFEST";
        internal static CombatCommand Deployment(CombatJindanSeatState seat) => new CombatCommand(
            CombatCommandKind.JindanAbility, seat.Definition.ActorId, profileId: Effect,
            jindan: new CombatJindanRequest(seat.Definition.SeatId));

        internal static string Validate(CombatSession session, CombatantSnapshot actor, CombatJindanSeatState seat,
            CombatJindanRequest request, CombatJindanAbilityDefinition ability)
        {
            if (request.EdgePath.Count != 0 || request.RemoteOrigin.HasValue || request.MetricDistanceUnits != 0 ||
                request.EarthMode.HasValue || request.TideAction.HasValue ||
                request.EarthTargetId != null || request.TideSource.HasValue || request.Charged || request.CreateTideEye)
                return "fire_fixed_rule_required";
            var fire = seat.FireDomain;
            if (session.Surfaces == null || fire == null || fire.Cleared) return "fire_surface_missing";
            if (fire.Main != null) return "jindan_capacity_full";
            if (fire.Support.IsDestroyed) return "fire_center_destroyed";
            if (fire.RemainingFuel < ability.FireDomain.TailFuel) return "fire_fuel_insufficient";
            if (!fire.HasPlacedSupport && !request.SelectedTargetCell.HasValue) return "fire_center_required";
            var center = request.SelectedTargetCell ?? fire.Support.Cell;
            if (fire.HasPlacedSupport && center != fire.Support.Cell) return "fire_fixed_rule_required";
            if (!fire.HasPlacedSupport && (!session.SceneObjects.Board.TryGetCell(center, out var tile) ||
                tile.IsEntityObstacle || tile.BlocksMovement || session.Surfaces.Get(center) == null))
                return "fire_center_invalid";
            var contact = session.SceneObjects.Board.QueryContact(actor.Pose, new SpatialPose(center),
                0, ability.Range, new SpatialAttackPolicy(SpatialMovementMode.Ground | SpatialMovementMode.Fly,
                    SpatialMovementMode.Ground, ability.VerticalReach));
            if (!contact.IsInRange || !contact.HasLineOfSight) return contact.Reason;
            if (BurningCount(session, seat) >= ability.Capacity) return "fire_tail_capacity_full";
            if (Candidates(session, seat, ability, center).Length == 0) return "fire_no_eligible_surface";
            return string.Empty;
        }

        internal static void Deploy(CombatantSnapshot actor, CombatJindanSeatState seat, CombatJindanAbilityDefinition ability, HexCoord? selectedCenter)
        {
            var fire = seat.FireDomain;
            if (selectedCenter.HasValue) fire.PlaceSupport(selectedCenter.Value);
            fire.RemainingFuel -= ability.FireDomain.TailFuel;
            fire.Main = new CombatFireDomainInstance(actor.OwnTurnsStarted, ability.DurationOwnTurnEnds, ability.FireDomain.TailFuel);
        }

        public static int BurningCount(CombatSession session, CombatJindanSeatState seat) =>
            session.Surfaces?.Cells.Count(value => IsOwnedBurning(value, seat)) ?? 0;

        private static bool IsOwnedBurning(CombatSurfaceCell cell, CombatJindanSeatState seat) =>
            cell != null && cell.Kind == CombatSurfaceKind.Burning && cell.SourceRef == seat.Definition.AbilityInstanceId;

        public static bool IsBurnable(CombatSession session, CombatSurfaceCell cell)
        {
            if (cell == null) return false;
            bool localFuel = cell.Fuel > 0 && (cell.FuelObjectId == null ||
                session.SceneObjects.Find(cell.FuelObjectId) is { IsDestroyed: false });
            if (localFuel) return true;
            bool hearth = session.Jindan?.Seats.Any(seat => seat.FireDomain is { HasPlacedSupport: true } fire &&
                fire.Support.Cell == cell.Cell && !fire.Support.IsDestroyed && session.Jindan.Ability(Effect).FireDomain.ChooseCenter) == true;
            return hearth || session.SceneObjects.All.Any(value => value.Contains(cell.Cell) &&
                value.Definition.ContentType == "low_plant" && !value.IsDestroyed);
        }

        public static HexCoord[] Candidates(CombatSession session, CombatJindanSeatState seat, CombatJindanAbilityDefinition ability, HexCoord? prospectiveCenter = null)
        {
            if (session.Surfaces == null || seat.FireDomain == null) return Array.Empty<HexCoord>();
            var fire = seat.FireDomain;
            var center = prospectiveCenter ?? fire.Support.Cell;
            var tails = fire.tails.SelectMany(value => value.cells).Where(cell => IsOwnedBurning(session.Surfaces.Get(cell), seat)).ToHashSet();
            int capacity = Math.Max(0, ability.Capacity - tails.Count);
            var current = fire.Main?.cells.Where(cell => IsOwnedBurning(session.Surfaces.Get(cell), seat) &&
                IsBurnable(session, session.Surfaces.Get(cell))).OrderBy(cell => cell.TopologicalDistanceTo(center))
                .ThenBy(cell => cell.Q).ThenBy(cell => cell.R).ToArray() ?? Array.Empty<HexCoord>();
            return current.Concat(session.Surfaces.Cells.Where(value => (ability.FireDomain.ChooseCenter && value.Cell == center || IsBurnable(session, value)) &&
                    !tails.Contains(value.Cell) && value.Kind != CombatSurfaceKind.Burning)
                .OrderBy(value => value.Cell.TopologicalDistanceTo(center))
                .ThenBy(value => value.Cell.Q).ThenBy(value => value.Cell.R).Select(value => value.Cell))
                .Take(capacity).ToArray();
        }

        internal static void EndMain(CombatSession session, CombatJindanSeatState seat,
            CombatJindanAbilityDefinition ability, string reason)
        {
            var fire = seat.FireDomain;
            if (fire?.Main == null) return;
            var old = fire.Main;
            old.cells.RemoveWhere(cell => !IsOwnedBurning(session.Surfaces.Get(cell), seat) || !IsBurnable(session, session.Surfaces.Get(cell)));
            fire.Main = null;
            session.Combatants.TryGet(seat.Definition.ActorId, out var actor);
            if (actor.IsAlive && old.cells.Count > 0 && old.ReservedFuel > 0)
            {
                old.CreatedOwnTurn = actor.OwnTurnsStarted;
                old.RemainingCycles = ability.FireDomain.TailCycles;
                fire.tails.Add(old);
            }
            else ReturnUnused(fire, old);
            seat.LastEndReason = reason;
        }

        private static void ReturnUnused(CombatFireDomainState fire, CombatFireDomainInstance instance)
        {
            if (!fire.Support.IsDestroyed) fire.RemainingFuel += instance.ReservedFuel;
            instance.ReservedFuel = 0;
        }

        internal static bool Reconcile(CombatSession session, CombatJindanSeatState seat, CombatJindanAbilityDefinition ability)
        {
            var fire = seat.FireDomain;
            if (fire == null || fire.Cleared) return false;
            session.Combatants.TryGet(seat.Definition.ActorId, out var actor);
            if (!actor.IsAlive)
            {
                if (fire.Main == null && fire.tails.Count == 0) return false;
                Clear(session, seat); seat.LastEndReason = "jindan_owner_dead"; return true;
            }
            bool changed = false;
            foreach (var instance in fire.tails.Concat(fire.Main == null ? Array.Empty<CombatFireDomainInstance>() : new[] { fire.Main }).ToArray())
                changed |= instance.cells.RemoveWhere(cell => !IsOwnedBurning(session.Surfaces.Get(cell), seat)) > 0;
            var invalid = session.Surfaces.Cells.Where(value => IsOwnedBurning(value, seat) && !IsBurnable(session, value)).Select(value => value.Cell).ToArray();
            if (invalid.Length > 0) { Extinguish(session, seat, invalid); changed = true; }
            foreach (var tail in fire.tails.Where(value => !value.cells.Any(cell => IsOwnedBurning(session.Surfaces.Get(cell), seat))).ToArray())
            { ReturnUnused(fire, tail); fire.tails.Remove(tail); changed = true; }
            if (fire.Main != null && (fire.Support.IsDestroyed || actor.HasStatus(CombatStatusKind.SealSpirit) || seat.EffectId != Effect))
            {
                EndMain(session, seat, ability, fire.Support.IsDestroyed ? "fire_center_destroyed" : "jindan_source_disabled");
                changed = true;
            }
            return changed;
        }

        internal static bool EndOwnTurn(CombatSession session, CombatantSnapshot actor, CombatJindanSeatState seat,
            CombatJindanAbilityDefinition ability)
        {
            bool changed = Reconcile(session, seat, ability);
            var fire = seat.FireDomain;
            if (fire == null || fire.Cleared || !actor.IsAlive) return changed;
            var hit = new HashSet<string>(StringComparer.Ordinal);
            bool spread = false;
            // Snapshot tails before ending the main rule; a newly created tail cannot tick at this same boundary.
            foreach (var tail in fire.tails.ToArray())
            {
                if (actor.OwnTurnsStarted <= tail.CreatedOwnTurn) continue;
                var burning = tail.cells.Where(cell => IsOwnedBurning(session.Surfaces.Get(cell), seat))
                    .OrderBy(cell => cell.Q).ThenBy(cell => cell.R).ToArray();
                var paid = burning.Take(tail.ReservedFuel).ToArray();
                tail.ReservedFuel -= paid.Length;
                Extinguish(session, seat, burning.Except(paid));
                Damage(session, actor, seat, ability, paid, hit);
                if (!actor.IsAlive || session.IsFinished) return true;
                if (!spread) spread = Spread(session, seat, ability, tail, paid, true);
                tail.RemainingCycles--;
                if (tail.RemainingCycles == 0 || tail.ReservedFuel == 0)
                { Extinguish(session, seat, tail.cells); ReturnUnused(fire, tail); fire.tails.Remove(tail); }
                changed = true;
            }
            var main = fire.Main;
            if (main == null || actor.OwnTurnsStarted <= main.CreatedOwnTurn) return changed;
            var candidates = Candidates(session, seat, ability);
            string failure = candidates.Length == 0 ? "fire_no_eligible_surface" : fire.RemainingFuel < candidates.Length ? "fire_fuel_insufficient" :
                actor.CurrentSpirit < ability.UpkeepMana || seat.TotalManaSpent > int.MaxValue - ability.UpkeepMana ||
                seat.MaintenancePayments == int.MaxValue ? "jindan_upkeep_failed" : string.Empty;
            if (failure.Length > 0) { EndMain(session, seat, ability, failure); return true; }
            actor.TryConsumeSpirit(ability.UpkeepMana);
            seat.TotalManaSpent += ability.UpkeepMana; seat.MaintenancePayments++;
            fire.RemainingFuel -= candidates.Length;
            var changes = candidates.Select(cell => new CombatSurfaceChange(cell, session.Surfaces.Get(cell).Kind,
                CombatSurfaces.Heat(session.Surfaces.Get(cell).Kind, true))).ToArray();
            session.Surfaces.ApplyChanges(changes, seat.Definition.AbilityInstanceId);
            session.Surfaces.PublishBoard(session, Array.Empty<CombatSceneObjectChange>());
            session.Surfaces.Receipt(seat.Definition.AbilityInstanceId, session.TurnScheduler.CurrentTick, changes,
                Array.Empty<CombatTidePush>(), Array.Empty<CombatDamageResult>(), Array.Empty<CombatSceneObjectChange>());
            foreach (var cell in candidates.Where(cell => IsOwnedBurning(session.Surfaces.Get(cell), seat))) main.cells.Add(cell);
            var active = main.cells.Where(cell => IsOwnedBurning(session.Surfaces.Get(cell), seat)).ToArray();
            Damage(session, actor, seat, ability, active, hit);
            if (!actor.IsAlive || session.IsFinished) return true;
            if (!spread) Spread(session, seat, ability, main, active, false);
            main.RemainingCycles--;
            if (main.RemainingCycles == 0) EndMain(session, seat, ability, "fire_domain_expired");
            Reconcile(session, seat, ability);
            return true;
        }

        private static void Damage(CombatSession session, CombatantSnapshot actor, CombatJindanSeatState seat,
            CombatJindanAbilityDefinition ability, IEnumerable<HexCoord> cells, HashSet<string> hit)
        {
            var active = cells.ToHashSet();
            var source = actor.CopyForResolution();
            var targets = session.Combatants.All.Where(value => value.IsAlive && value.MovementMode == SpatialMovementMode.Ground &&
                value.OccupiedCells.Any(active.Contains) && hit.Add(value.Id)).OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();
            var damage = targets.Select(target => CombatActionResolver.ResolveEnvironmentDamage(source, target, false,
                ability.FireDomain.AttackCapacity, ability.FireDomain.DamageMultiplier, "element_fire").ForTarget(target.Id, default)).ToArray();
            session.Surfaces.Receipt(seat.Definition.AbilityInstanceId, session.TurnScheduler.CurrentTick,
                Array.Empty<CombatSurfaceChange>(), Array.Empty<CombatTidePush>(), damage, Array.Empty<CombatSceneObjectChange>(),
                targets: targets.Select(value => new CombatSurfaceTargetSnapshot(value)));
        }

        private static bool Spread(CombatSession session, CombatJindanSeatState seat, CombatJindanAbilityDefinition ability,
            CombatFireDomainInstance instance, IEnumerable<HexCoord> active, bool tail)
        {
            var fire = seat.FireDomain;
            if ((tail ? instance.ReservedFuel : fire.RemainingFuel) <= 0 || BurningCount(session, seat) >= ability.Capacity) return false;
            foreach (var cell in active.OrderBy(value => value.Q).ThenBy(value => value.R))
                for (int direction = 0; direction < 6; direction++)
                {
                    var next = session.Surfaces.Get(cell.Step(direction));
                    if (!IsBurnable(session, next) || next.Kind is not (CombatSurfaceKind.None or CombatSurfaceKind.Scorched) ||
                        !CombatTideRules.CanCrossWaterEdge(session, cell, next.Cell)) continue;
                    if (tail) instance.ReservedFuel--; else fire.RemainingFuel--;
                    var changes = new[] { new CombatSurfaceChange(next.Cell, next.Kind, CombatSurfaceKind.Burning) };
                    instance.cells.Add(next.Cell);
                    session.Surfaces.ApplyChanges(changes, seat.Definition.AbilityInstanceId);
                    session.Surfaces.PublishBoard(session, Array.Empty<CombatSceneObjectChange>());
                    session.Surfaces.Receipt(seat.Definition.AbilityInstanceId, session.TurnScheduler.CurrentTick, changes,
                        Array.Empty<CombatTidePush>(), Array.Empty<CombatDamageResult>(), Array.Empty<CombatSceneObjectChange>());
                    return true;
                }
            return false;
        }

        internal static CombatCommand ChooseDeploymentOrApproach(CombatSession session, IReadOnlyList<CombatCommand> actions)
        {
            var deploy = actions.FirstOrDefault(value => value.Kind == CombatCommandKind.JindanAbility && value.ProfileId == Effect);
            if (deploy != null) return deploy;
            var seat = session.Jindan?.Seats.FirstOrDefault(value => value.Definition.ActorId == session.CurrentActorId &&
                value.EffectId == Effect && value.FireDomain is { Cleared: false, Main: null });
            if (seat == null || seat.FireDomain.Support.IsDestroyed) return null;
            var ability = session.Jindan.Ability(Effect);
            session.Combatants.TryGet(seat.Definition.ActorId, out var actor);
            if (actor.CurrentSpirit < ability.Mana || actor.GetCooldown(Effect) > 0 ||
                actor.HasStatus(CombatStatusKind.SealSpirit) || seat.FireDomain.RemainingFuel < ability.FireDomain.TailFuel ||
                BurningCount(session, seat) >= ability.Capacity || Candidates(session, seat, ability).Length == 0) return null;
            var service = new CombatCommandService();
            var center = seat.FireDomain.Support.Cell;
            return actions.Where(value => value.Kind == CombatCommandKind.Move &&
                    value.Destination.Value.TopologicalDistanceTo(center) < actor.Position.TopologicalDistanceTo(center) &&
                    service.Preview(session, value).ActionPointCostUnits <= actor.ActionPointsRemainingUnits - ability.ActionPoints)
                .OrderBy(value => value.Destination.Value.TopologicalDistanceTo(center))
                .ThenBy(value => value.Destination.Value.Q).ThenBy(value => value.Destination.Value.R).FirstOrDefault();
        }

        private static void Extinguish(CombatSession session, CombatJindanSeatState seat, IEnumerable<HexCoord> cells)
        {
            var changes = cells.Distinct().Where(cell => IsOwnedBurning(session.Surfaces.Get(cell), seat))
                .Select(cell => new CombatSurfaceChange(cell, CombatSurfaceKind.Burning, CombatSurfaceKind.Scorched)).ToArray();
            if (changes.Length == 0) return;
            session.Surfaces.ApplyChanges(changes, null);
            session.Surfaces.PublishBoard(session, Array.Empty<CombatSceneObjectChange>());
            session.Surfaces.Receipt(seat.Definition.AbilityInstanceId, session.TurnScheduler.CurrentTick, changes,
                Array.Empty<CombatTidePush>(), Array.Empty<CombatDamageResult>(), Array.Empty<CombatSceneObjectChange>());
        }

        internal static void Clear(CombatSession session, CombatJindanSeatState seat)
        {
            var fire = seat.FireDomain;
            if (fire == null) return;
            Extinguish(session, seat, session.Surfaces.Cells.Where(value => IsOwnedBurning(value, seat)).Select(value => value.Cell));
            fire.Main = null; fire.tails.Clear(); fire.Cleared = true;
        }
    }
}
