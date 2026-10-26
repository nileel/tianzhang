using System;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public enum CombatEarthMode { Wall, Ramp, Heavy, Light, Anchor }

    public sealed class CombatEarthInstance
    {
        internal CombatEarthInstance(int createdTurn, int ends, CombatSceneObject[] objects,
            string targetId = null, CombatOwnTurnStatus objectStatus = null)
        { CreatedOwnTurn = createdTurn; RemainingOwnTurnEnds = ends; Objects = objects; TargetId = targetId; ObjectStatus = objectStatus; }
        public int CreatedOwnTurn { get; }
        public int RemainingOwnTurnEnds { get; internal set; }
        public System.Collections.Generic.IReadOnlyList<CombatSceneObject> Objects { get; internal set; }
        public string TargetId { get; }
        public CombatOwnTurnStatus ObjectStatus { get; }
    }

    internal static class CombatEarthRules
    {
        internal static string Validate(CombatSession session, CombatantSnapshot actor, CombatJindanSeatState seat,
            CombatJindanRequest request, CombatJindanAbilityDefinition definition)
        {
            if (!request.EarthMode.HasValue || !Enum.IsDefined(typeof(CombatEarthMode), request.EarthMode.Value) ||
                request.RemoteOrigin.HasValue) return "earth_selection_invalid";
            var mode = request.EarthMode.Value;
            if (seat.EffectId == "EARTH_MANIFEST")
            {
                if (mode is not (CombatEarthMode.Wall or CombatEarthMode.Ramp) || request.EdgePath.Count != 2)
                    return "earth_selection_invalid";
                if ((seat.ActiveEarth?.Objects.Count ?? 0) >= definition.Capacity) return "jindan_capacity_full";
                var a = request.EdgePath[0]; var b = request.EdgePath[1]; var board = session.SceneObjects.Board;
                if (!a.TryGetDirectionTo(b, out _)) return "earth_selection_invalid";
                foreach (var cell in request.EdgePath)
                {
                    string empty = board.InspectPlacement(new SpatialPose(cell), session.GetOccupiedPositionsExcept(null));
                    if (empty.Length > 0) return empty;
                    if (session.SceneObjects.All.Any(item => !item.Definition.IsSupportMarker &&
                        (!item.EdgeOther.HasValue && item.Cell == cell || item.Contains(a) && item.Contains(b))))
                        return "earth_anchor_occupied";
                    var contact = board.QueryContact(actor.Pose, new SpatialPose(cell), 0, definition.Range, Policy(definition));
                    if (!contact.IsInRange || !contact.HasLineOfSight) return contact.Reason;
                }
                if (!board.TryGetEdge(a, b, out var forward) || !board.TryGetEdge(b, a, out var reverse))
                    return SpatialQueryReasons.MissingDirectedEdge;
                if (!board.InspectEdge(a,b,SpatialQueryKind.Attack).IsLegal ||
                    !board.InspectEdge(b,a,SpatialQueryKind.Attack).IsLegal) return "earth_edge_blocked";
                if (mode == CombatEarthMode.Ramp)
                {
                    board.TryGetCell(a, out var from); board.TryGetCell(b, out var to);
                    if (Math.Abs((long)from.HeightLevel - to.HeightLevel) != 1 ||
                        forward.EdgeKind is SpatialEdgeKind.Slope or SpatialEdgeKind.Stairs ||
                        reverse.EdgeKind is SpatialEdgeKind.Slope or SpatialEdgeKind.Stairs) return "earth_ramp_height_invalid";
                }
                return string.Empty;
            }
            if (mode is CombatEarthMode.Wall or CombatEarthMode.Ramp || string.IsNullOrWhiteSpace(request.EarthTargetId) ||
                !request.SelectedTargetCell.HasValue) return "earth_selection_invalid";
            if (session.Combatants.TryGet(request.EarthTargetId, out var target))
            {
                if (!target.IsAlive || !target.OccupiedCells.Contains(request.SelectedTargetCell.Value)) return "earth_target_invalid";
                if (target.Statuses.Any(value => value.Kind == CombatStatusKind.EarthGravity &&
                    value.SourceRef != seat.Definition.AbilityInstanceId)) return "earth_conflict_unresolved";
                var selected = new SpatialPose(request.SelectedTargetCell.Value, target.Facing,
                    SpatialFootprintKind.Single, target.BodyScaleRank, target.MovementMode, target.AltitudeBand);
                var contact = session.SceneObjects.Board.QueryContact(actor.Pose, selected, 0, definition.Range, Policy(definition));
                return !contact.IsInRange || !contact.HasLineOfSight ? contact.Reason : string.Empty;
            }
            var obj = session.SceneObjects.Find(request.EarthTargetId);
            if (obj == null || obj.IsDestroyed || !obj.Contains(request.SelectedTargetCell.Value) ||
                obj.Definition.MaterialType is not ("stone" or "earth")) return "earth_target_invalid";
            var objectContact = session.SceneObjects.Board.QuerySceneObjectContact(actor.Pose,
                request.SelectedTargetCell.Value, obj.Id, 0, definition.Range, Policy(definition));
            return !objectContact.IsInRange || !objectContact.HasLineOfSight ? objectContact.Reason : string.Empty;
        }

        internal static CombatEarthInstance Prepare(CombatSession session, CombatantSnapshot actor,
            CombatJindanSeatState seat, CombatJindanRequest request, CombatJindanAbilityDefinition definition,
            out CombatOwnTurnStatus unitStatus)
        {
            unitStatus = null;
            if (seat.EffectId == "EARTH_MANIFEST")
            {
                bool ramp = request.EarthMode == CombatEarthMode.Ramp;
                string id = seat.Definition.AbilityInstanceId + ":earth:" + (seat.SuccessfulDeployments + 1);
                var obj = new CombatSceneObject(id, new CombatSceneObjectDefinition(ramp ? "stone_support" : "high_wall",
                    "stone", checked(definition.SupportDurability * 5 / 4), 0, 0, !ramp, !ramp, "none", isTemporaryRamp:ramp),
                    request.EdgePath[0], request.EdgePath[1], seat.Definition.AbilityInstanceId);
                return new CombatEarthInstance(seat.ActiveEarth?.CreatedOwnTurn ?? actor.OwnTurnsStarted,
                    seat.ActiveEarth?.RemainingOwnTurnEnds ?? definition.DurationOwnTurnEnds,
                    (seat.ActiveEarth?.Objects ?? Array.Empty<CombatSceneObject>()).Concat(new[] {obj}).ToArray());
            }
            session.Combatants.TryGet(request.EarthTargetId, out var target);
            bool nourished = actor.MovementMode == SpatialMovementMode.Ground || target == null || target.MovementMode == SpatialMovementMode.Ground;
            var previous = target?.Statuses.FirstOrDefault(value => value.Kind == CombatStatusKind.EarthGravity &&
                value.SourceRef == seat.Definition.AbilityInstanceId);
            var status = new CombatOwnTurnStatus(CombatStatusKind.EarthGravity, definition.DurationOwnTurnEnds, 0,
                target?.OwnTurnActive ?? actor.OwnTurnActive)
            {
                StatusId = "earth_" + request.EarthMode.Value.ToString().ToLowerInvariant(),
                SourceRef = seat.Definition.AbilityInstanceId, SourceKind = target == actor ? CombatStatusSourceKind.SELF_ABILITY : CombatStatusSourceKind.OTHER_ABILITY,
                Polarity = CombatStatusPolarity.MIXED, Tags = request.EarthMode == CombatEarthMode.Anchor ? CombatStatusTags.ANCHOR : CombatStatusTags.GRAVITY,
                EarthMode = request.EarthMode, GravityShiftRank = nourished ? 3 : 2,
                Pressure = previous?.Pressure ?? 0,
            };
            if (target != null) unitStatus = status;
            return new CombatEarthInstance(actor.OwnTurnsStarted, definition.DurationOwnTurnEnds, Array.Empty<CombatSceneObject>(),
                request.EarthTargetId, target == null ? status : null);
        }

        private static SpatialAttackPolicy Policy(CombatJindanAbilityDefinition definition) =>
            new SpatialAttackPolicy(SpatialMovementMode.Ground | SpatialMovementMode.Fly,
                SpatialMovementMode.Ground | SpatialMovementMode.Fly, definition.VerticalReach);
    }
}
