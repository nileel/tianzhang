using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public sealed class CombatForcedMovementResult
    {
        internal CombatForcedMovementResult(SpatialPose origin, List<SpatialPose> path, int distance, string reason, bool fell)
        { Origin = origin; Path = path.AsReadOnly(); ConsumedDistanceUnits = distance; Reason = reason; CrossedDrop = fell; }
        public SpatialPose Origin { get; }
        public IReadOnlyList<SpatialPose> Path { get; }
        public SpatialPose Destination => Path.Count == 0 ? Origin : Path[Path.Count - 1];
        public int ConsumedDistanceUnits { get; }
        public string Reason { get; }
        public bool CrossedDrop { get; }
    }

    /// <summary>Shared voluntary/forced force facts. Physical pushes preserve facing and never spend target AP.</summary>
    public static class CombatForceMovement
    {
        public static CombatOwnTurnStatus Gravity(CombatantSnapshot actor) => actor.Statuses.FirstOrDefault(value => value.Kind == CombatStatusKind.EarthGravity);
        public static int LoadRank(CombatantSnapshot actor)
        {
            var state = Gravity(actor);
            return state?.EarthMode == CombatEarthMode.Heavy ? actor.BodyScaleRank + state.GravityShiftRank :
                state?.EarthMode == CombatEarthMode.Light ? Math.Max(0, actor.BodyScaleRank - state.GravityShiftRank) : actor.BodyScaleRank;
        }
        public static SpatialMovementPolicy Policy(CombatantSnapshot actor, int forceRank = 2)
        {
            var status = Gravity(actor);
            bool ground = actor.MovementMode == SpatialMovementMode.Ground;
            return status?.EarthMode switch
            {
                CombatEarthMode.Anchor => new SpatialMovementPolicy(blockedReason: forceRank <= status.GravityShiftRank ? "earth_anchored" : ""),
                CombatEarthMode.Heavy => new SpatialMovementPolicy(ground ? 2 : 1, maximumEdges:Math.Max(0, 2-status.Pressure),
                    blockedReason:status.Pressure >= 2 ? "earth_pressure_limit" : ""),
                CombatEarthMode.Light => new SpatialMovementPolicy(ground ? 3 : 1, ground ? 4 : 1, ground),
                _ => SpatialMovementPolicy.Normal,
            };
        }

        public static CombatForcedMovementResult Query(CombatSession session, string actorId, int direction,
            int distanceBudget, int forceRank)
        {
            if (direction is < 0 or > 5 || distanceBudget < 0 || forceRank < 0 ||
                !session.Combatants.TryGet(actorId, out var actor) || !actor.IsAlive)
                throw new ArgumentException("forced_movement_request_invalid");
            var path = new List<SpatialPose>(); var board = session.SceneObjects.Board;
            var current = actor.Pose; int consumed = 0; bool fell = false;
            var status = Gravity(actor);
            int adjusted = status?.EarthMode == CombatEarthMode.Heavy ? Math.Max(0,distanceBudget-1) :
                status?.EarthMode == CombatEarthMode.Light ? Math.Min(2,checked(distanceBudget+1)) : distanceBudget;
            int budget = checked(adjusted * board.UnitsPerRange);
            var occupied = session.GetOccupiedPositionsExcept(actorId);
            var policy = Policy(actor, forceRank);
            string reason = string.Empty;
            while (true)
            {
                var next = current.WithAnchor(current.Anchor.Step(direction));
                // Check every body's space seal before force/anchor comparisons.
                foreach (var cell in current.OccupiedCells)
                    if (board.TryGetEdge(cell,cell.Step(direction),out var edge) && edge.EffectBlockerMask != 0)
                    { reason = "forced_movement_space_sealed"; break; }
                if (reason.Length > 0) break;
                if (policy.BlockedReason.Length > 0) { reason = policy.BlockedReason; break; }
                if (path.Count >= policy.MaximumEdges) { reason = "earth_pressure_limit"; break; }
                if (consumed >= budget) { reason = SpatialQueryReasons.DistanceBudgetExhausted; break; }
                int metric = 0;
                foreach (var cell in current.OccupiedCells)
                {
                    var body = new SpatialPose(cell, current.Facing, SpatialFootprintKind.Single, current.BodyScaleRank, current.MovementMode, current.AltitudeBand);
                    var edge = current.MovementMode == SpatialMovementMode.Fly ? board.InspectFlightEdge(body,cell.Step(direction)) :
                        board.InspectGroundMoveEdge(body,cell.Step(direction),policy.LightLeap && current.Footprint == SpatialFootprintKind.Single);
                    if (!edge.IsLegal) { reason = edge.Reason; break; }
                    metric = Math.Max(metric,edge.MetricDistanceUnits);
                }
                if (reason.Length > 0) break;
                reason = board.InspectPlacement(next, occupied);
                if (reason.Length > 0) break;
                if ((long)consumed + metric > budget) { reason = SpatialQueryReasons.DistanceBudgetExhausted; break; }
                if (current.MovementMode == SpatialMovementMode.Ground &&
                    board.TryGetEdge(current.Anchor,next.Anchor,out var front) && front.EdgeKind == SpatialEdgeKind.OneWayDrop &&
                    board.TryGetCell(current.Anchor,out var fromCell) && board.TryGetCell(next.Anchor,out var toCell) &&
                    fromCell.HeightLevel > toCell.HeightLevel) fell = true;
                consumed += metric; path.Add(next); current = next;
            }
            return new CombatForcedMovementResult(actor.Pose,path,consumed,reason,fell);
        }

        public static CombatForcedMovementResult Apply(CombatSession session, string actorId, int direction, int distanceBudget, int forceRank)
        {
            var result = Query(session,actorId,direction,distanceBudget,forceRank);
            session.Combatants.TryGet(actorId,out var actor);
            if (result.Path.Count > 0)
            {
                actor.SetPosition(result.Destination.Anchor);
                actor.RecordPhysicalMovement(result.Path.Count);
                var status = Gravity(actor);
                if (status?.EarthMode == CombatEarthMode.Anchor && forceRank > status.GravityShiftRank)
                    actor.RemoveEarthGravity(status.SourceRef);
                session.Jindan?.Reconcile(session); session.Flight?.Reconcile(session);
            }
            return result;
        }
    }

    /// <summary>Decorates the existing spatial port only for actual gravity states; all callers share one query.</summary>
    internal sealed class CombatGravitySpatialQuery : ICombatSpatialQuery
    {
        private readonly CombatSession session;
        private readonly ICombatSpatialQuery inner;
        internal CombatGravitySpatialQuery(CombatSession session, ICombatSpatialQuery inner) { this.session=session; this.inner=inner; }
        public SpatialContactResult QueryRange(SpatialPose source, SpatialPose target, int min, int max, SpatialAttackPolicy policy=null) =>
            inner.QueryRange(source,target,min,max,policy);
        private SpatialMovementPolicy Policy(SpatialPose source)
        {
            var actor=session.Combatants.All.FirstOrDefault(value=>value.IsAlive && value.Pose==source);
            return actor == null ? SpatialMovementPolicy.Normal : CombatForceMovement.Policy(actor);
        }
        public CombatMovementQueryResult QueryMovement(SpatialPose source, HexCoord destination,int budget,int efficiency,
            IReadOnlyCollection<HexCoord> occupied,int? destinationFacing=null)
        {
            var policy=Policy(source);
            if (policy==SpatialMovementPolicy.Normal) return inner.QueryMovement(source,destination,budget,efficiency,occupied,destinationFacing);
            var result=session.SceneObjects.Board.QueryMovementPath(source,destination,budget,efficiency,occupied,destinationFacing,policy);
            return new CombatMovementQueryResult(result.Path.Count>0,result.Reason,result.Steps,result.UnitsPerRange,result.ActionPointCostUnits);
        }
        public IReadOnlyDictionary<SpatialPose,int> FindReachable(SpatialPose source,int budget,int efficiency,IReadOnlyCollection<HexCoord> occupied)
        {
            var policy=Policy(source);
            return policy==SpatialMovementPolicy.Normal ? inner.FindReachable(source,budget,efficiency,occupied) :
                session.SceneObjects.Board.FindReachableWithinActionPoints(source,budget,efficiency,occupied,policy);
        }
    }
}
