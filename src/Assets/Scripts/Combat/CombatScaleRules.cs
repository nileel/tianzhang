using System;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    internal static class CombatScaleRules
    {
        internal static string Validate(CombatSession session, CombatantSnapshot actor,
            CombatJindanRequest request, CombatJindanAbilityDefinition definition)
        {
            var board = session.SceneObjects.Board;
            if (request.EdgePath.Count < 2 || request.EdgePath.Count > definition.Capacity + 1 ||
                request.EdgePath.Distinct().Count() != request.EdgePath.Count ||
                !definition.MetricModes.Contains(request.MetricDistanceUnits)) return "jindan_scale_selection_invalid";
            var policy = new SpatialAttackPolicy(SpatialMovementMode.Ground | SpatialMovementMode.Fly,
                SpatialMovementMode.Ground, definition.VerticalReach);
            if ((actor.MovementMode & policy.SourceModeMask) == 0) return SpatialQueryReasons.SourceModeNotAllowed;
            HexCoord origin = request.RemoteOrigin ?? actor.Position;
            if (request.RemoteOrigin.HasValue)
            {
                if (session.Combatants.All.Any(value => value.IsAlive && value.OccupiedCells.Contains(origin)) ||
                    board.InspectPlacement(Pose(origin)) != SpatialQueryReasons.Ok)
                    return "jindan_remote_origin_not_empty";
                var sight = board.QueryContact(actor.Pose, Pose(origin), 0, definition.VisionRange, policy);
                if (!sight.IsInRange || !sight.HasLineOfSight) return "jindan_remote_origin_not_visible";
            }
            if (!board.TryGetCell(actor.Position, out var sourceRules)) return "cell_not_configured";
            foreach (HexCoord cell in request.EdgePath)
            {
                if (!board.TryGetCell(cell, out var rules) || rules.IsEntityObstacle || rules.BlocksMovement)
                    return "jindan_scale_cell_invalid";
                // Remote origin changes range, not the source's vertical eligibility.
                if (Math.Abs((long)sourceRules.HeightLevel + actor.AltitudeBand - rules.HeightLevel) > definition.VerticalReach)
                    return SpatialQueryReasons.VerticalReachExceeded;
                var contact = board.QueryContact(request.RemoteOrigin.HasValue ? Pose(origin) : actor.Pose,
                    Pose(cell), 0, definition.Range, policy);
                if (!contact.IsInRange || !contact.HasLineOfSight) return contact.Reason;
            }
            for (int i = 1; i < request.EdgePath.Count; i++)
            {
                HexCoord from = request.EdgePath[i - 1], to = request.EdgePath[i];
                if (!from.TryGetDirectionTo(to, out _)) return "jindan_scale_edges_not_adjacent";
                if (!board.InspectEdge(from, to, SpatialQueryKind.Attack).IsLegal ||
                    !board.InspectEdge(to, from, SpatialQueryKind.Attack).IsLegal)
                    return "jindan_scale_edge_blocked";
            }
            // The immutable board performs final complete-set validation before any payment.
            try
            {
                board.WithMetricOverrides(board.MetricOverrides.Concat(Enumerable.Range(1, request.EdgePath.Count - 1).Select(i =>
                    new SpatialMetricOverride(request.SeatId, request.EdgePath[i - 1], request.EdgePath[i],
                        request.MetricDistanceUnits))).ToArray());
            }
            catch (ArgumentException) { return "jindan_scale_selection_invalid"; }
            return string.Empty;
        }
        private static SpatialPose Pose(HexCoord cell) => new SpatialPose(cell, 0, SpatialFootprintKind.Single, 1);
    }
}
