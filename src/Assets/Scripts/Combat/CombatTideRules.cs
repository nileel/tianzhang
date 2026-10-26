using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public sealed class CombatPendingTide
    {
        internal CombatPendingTide(CombatJindanRequest request,int dueTick) { Request=request; DueTick=dueTick; }
        public CombatJindanRequest Request { get; }
        public int DueTick { get; }
    }

    public sealed class CombatTidePreview
    {
        internal CombatTidePreview(string reason,int waterline=0,IEnumerable<CombatSurfaceChange> changes=null,
            IEnumerable<CombatTidePush> pushes=null,IEnumerable<string> targets=null,IEnumerable<string> objects=null)
        {
            Reason=reason; WaterlineHeight=waterline;
            Changes=Array.AsReadOnly((changes??Array.Empty<CombatSurfaceChange>()).ToArray());
            Pushes=Array.AsReadOnly((pushes??Array.Empty<CombatTidePush>()).ToArray());
            WaterTargetIds=Array.AsReadOnly((targets??Array.Empty<string>()).ToArray());
            ErosionObjectIds=Array.AsReadOnly((objects??Array.Empty<string>()).ToArray());
        }
        public bool IsLegal=>Reason.Length==0;
        public string Reason { get; }
        public int WaterlineHeight { get; }
        public IReadOnlyList<CombatSurfaceChange> Changes { get; }
        public IReadOnlyList<CombatTidePush> Pushes { get; }
        public IReadOnlyList<string> WaterTargetIds { get; }
        public IReadOnlyList<string> ErosionObjectIds { get; }
    }

    /// <summary>The same finite frozen result feeds players, AI, confirmation and deferred completion.</summary>
    public static class CombatTideRules
    {
        public static CombatTidePreview Preview(CombatSession session,CombatantSnapshot actor,
            CombatJindanSeatState seat,CombatJindanRequest request)
        {
            if (session.Surfaces==null || seat==null || seat.EffectId!="WATER_TIDE" || !request.TideSource.HasValue ||
                !request.TideAction.HasValue || !Enum.IsDefined(typeof(CombatTideAction),request.TideAction.Value) ||
                request.EdgePath.Count!=0 || request.RemoteOrigin.HasValue || request.EarthMode.HasValue)
                return Reject("tide_selection_invalid");
            var ability=session.Jindan.Ability(seat.EffectId); var definition=ability.Tide;
            var source=request.TideSource.Value; var state=session.Surfaces.Get(source); var board=session.SceneObjects.Board;
            if (definition==null || state==null || !board.TryGetCell(source,out var sourceCell))
                return Reject("tide_source_invalid");
            string contact=SourceContact(session,actor,source,ability);
            if (contact.Length>0) return Reject(contact);
            bool rise=request.TideAction==CombatTideAction.Rise;
            if (request.CreateTideEye)
            {
                if (!rise || state.Kind is not (CombatSurfaceKind.None or CombatSurfaceKind.Scorched or CombatSurfaceKind.Wet) ||
                    session.SceneObjects.All.Any(value=>!value.IsDestroyed && !value.EdgeOther.HasValue &&
                        value.Definition.BlocksMovement && value.Cell==source))
                    return Reject("tide_eye_cell_invalid");
                if (HasAvailableWaterSource(session,actor,ability)) return Reject("tide_real_source_available");
            }
            else if (rise ? !IsWaterSource(state.Kind) : !IsWaterChain(state.Kind)) return Reject("tide_source_invalid");
            int waterline=checked(sourceCell.HeightLevel+definition.WaterlineOffset);
            var visited=new HashSet<HexCoord>{source}; var queue=new Queue<HexCoord>(); queue.Enqueue(source);
            var changes=new List<CombatSurfaceChange>();
            while(queue.Count>0)
            {
                var cell=queue.Dequeue(); var before=session.Surfaces.Get(cell).Kind;
                var current=request.CreateTideEye && cell==source ? CombatSurfaceKind.ShallowWater : before;
                var after=CombatSurfaces.Water(current,request.TideAction.Value);
                if (after!=before) changes.Add(new CombatSurfaceChange(cell,before,after));
                for(int direction=0;direction<6;direction++)
                {
                    var next=cell.Step(direction); var nextState=session.Surfaces.Get(next);
                    if (visited.Contains(next) || next.TopologicalDistanceTo(source)>definition.Radius || nextState==null ||
                        !board.TryGetCell(next,out var nextCell) || nextCell.HeightLevel>waterline ||
                        !CanCrossWaterEdge(session,cell,next) || !rise && !IsWaterChain(nextState.Kind)) continue;
                    visited.Add(next);queue.Enqueue(next);
                }
            }
            if(changes.Count==0) return Reject("tide_no_changes");
            var changed=changes.ToDictionary(value=>value.Cell);
            string sourceRef=seat.Definition.AbilityInstanceId;
            var owned=new HashSet<HexCoord>(session.Surfaces.OwnedCells(sourceRef));
            foreach(var change in changes)
                if(change.After==CombatSurfaceKind.None) owned.Remove(change.Cell); else owned.Add(change.Cell);
            if(owned.Count>ability.Capacity) return Reject("jindan_capacity_full");
            var targets=session.Combatants.All.Where(value=>value.IsAlive && value.MovementMode==SpatialMovementMode.Ground &&
                value.OccupiedCells.Any(changed.ContainsKey)).ToArray();
            // Distances and targets are frozen before the first surface mutation or push.
            var ordered=targets.OrderBy(value=>rise ? -Distance(value,source,changed) : Distance(value,source,changed))
                .ThenBy(value=>value.Id,StringComparer.Ordinal).ToArray();
            var positions=session.Combatants.All.Where(value=>value.IsAlive).ToDictionary(value=>value.Id,value=>value.Pose);
            var pushes=new List<CombatTidePush>();
            foreach(var target in ordered)
            {
                var selected=target.OccupiedCells.Where(changed.ContainsKey).OrderBy(cell=>cell.TopologicalDistanceTo(source))
                    .ThenBy(cell=>cell.Q).ThenBy(cell=>cell.R).First();
                int direction=rise ? selected==source?0:source.DirectionToward(selected) : selected.DirectionToward(source);
                var occupiedCells=positions.Where(pair=>pair.Key!=target.Id).SelectMany(pair=>pair.Value.OccupiedCells).ToArray();
                var movement=direction<0 ? new CombatForcedMovementResult(target.Pose,new List<SpatialPose>(),0,"tide_at_source",false) :
                    CombatForceMovement.Query(session,target.Id,direction,definition.DistanceBudget,definition.ForceRank,occupiedCells);
                positions[target.Id]=movement.Destination;pushes.Add(new CombatTidePush(target.Id,direction,movement)
                    { FallDamage=movement.CrossedDrop?(int)(((long)target.MaximumHealth+9)/10):0 });
            }
            var eroded=changes.Where(value=>IsWaterSource(value.After)).Select(value=>value.Cell).ToHashSet();
            var objects=session.SceneObjects.All.Where(value=>!value.IsDestroyed &&
                (eroded.Contains(value.Cell) || value.EdgeOther.HasValue && eroded.Contains(value.EdgeOther.Value)) && Erodible(value))
                .Select(value=>value.Id).OrderBy(value=>value,StringComparer.Ordinal);
            return new CombatTidePreview("",waterline,changes,pushes,ordered.Select(value=>value.Id),objects);
        }

        private static int Distance(CombatantSnapshot actor,HexCoord source,Dictionary<HexCoord,CombatSurfaceChange> changed) =>
            actor.OccupiedCells.Where(changed.ContainsKey).Min(cell=>cell.TopologicalDistanceTo(source));
        private static bool IsWaterSource(CombatSurfaceKind kind)=>kind is CombatSurfaceKind.ShallowWater or CombatSurfaceKind.DeepWater;
        private static bool IsWaterChain(CombatSurfaceKind kind)=>IsWaterSource(kind) || kind==CombatSurfaceKind.Wet;
        private static CombatTidePreview Reject(string reason)=>new CombatTidePreview(reason);
        private static bool Erodible(CombatSceneObject value) =>
            value.Definition.ContentType=="low_plant" ||
            !string.IsNullOrEmpty(value.SourceRef) && !value.Definition.IsSupportMarker && value.Definition.MaterialType is "earth" or "stone" or "sand" ||
            value.IsDamaged && (value.Definition.MaterialType=="stone" && value.Definition.ContentType is "low_wall" or "high_wall" || value.Definition.MaterialType=="wood");

        private static string SourceContact(CombatSession session,CombatantSnapshot actor,HexCoord cell,CombatJindanAbilityDefinition definition)
        {
            var contact=session.SceneObjects.Board.QueryContact(actor.Pose,new SpatialPose(cell),0,definition.Range,
                new SpatialAttackPolicy(SpatialMovementMode.Ground|SpatialMovementMode.Fly,SpatialMovementMode.Ground,definition.VerticalReach));
            return contact.IsInRange && contact.HasLineOfSight ? "" : contact.Reason;
        }

        private static bool HasAvailableWaterSource(CombatSession session,CombatantSnapshot actor,CombatJindanAbilityDefinition definition)
        {
            // Every metric edge is at least one fixed-point unit; this circle bounds all affordable paths.
            int radius=checked(definition.Range*session.SceneObjects.Board.UnitsPerRange);
            var seen=new HashSet<HexCoord>();
            foreach(var body in actor.OccupiedCells)
                for(int q=-radius;q<=radius;q++)
                    for(int r=Math.Max(-radius,-q-radius);r<=Math.Min(radius,-q+radius);r++)
                    {
                        var cell=new HexCoord(checked(body.Q+q),checked(body.R+r));
                        if(!seen.Add(cell)) continue;
                        var state=session.Surfaces.Get(cell);
                        if(state!=null && IsWaterSource(state.Kind) && SourceContact(session,actor,cell,definition).Length==0) return true;
                    }
            return false;
        }

        internal static bool CanCrossWaterEdge(CombatSession session,HexCoord from,HexCoord to)
        {
            if(!session.SceneObjects.Board.TryGetEdge(from,to,out var edge) || edge.EdgeKind==SpatialEdgeKind.EntityWall ||
                !edge.AllowsEffects || edge.EffectBlockerMask!=0) return false;
            return !session.SceneObjects.All.Any(value=>!value.IsDestroyed && value.Definition.BlocksMovement &&
                (value.EdgeOther.HasValue ? value.Contains(from)&&value.Contains(to) : value.Cell==from || value.Cell==to));
        }

        internal static Action PrepareSettlement(CombatSession session,CombatantSnapshot actor,CombatJindanSeatState seat,
            CombatJindanRequest request,CombatTidePreview preview,int tick)
        {
            var definition=session.Jindan.Ability("WATER_TIDE").Tide;
            var copies=preview.WaterTargetIds.ToDictionary(id=>id,id=> {session.Combatants.TryGet(id,out var target);return target.CopyForResolution();});
            var damage=new List<CombatDamageResult>();
            var fallDamage=new List<CombatDamageResult>();
            foreach(var push in preview.Pushes)
            {
                var target=copies[push.ActorId];
                if(push.Movement.Path.Count==0) continue;
                target.SetPosition(push.Movement.Destination.Anchor); target.RecordPhysicalMovement(push.Movement.Path.Count);
                var gravity=CombatForceMovement.Gravity(target);
                if(gravity?.EarthMode==CombatEarthMode.Anchor && definition.ForceRank>gravity.GravityShiftRank) target.RemoveEarthGravity(gravity.SourceRef);
                if(push.Movement.CrossedDrop)
                {
                    int fall=(int)(((long)target.MaximumHealth+9)/10);target.ReceiveDamage(fall);
                    fallDamage.Add(new CombatDamageResult(fall,true,false,false,false).ForTarget(target.Id,default));
                }
            }
            foreach(var id in preview.WaterTargetIds)
            {
                var target=copies[id];
                if(!target.IsAlive) continue;
                damage.Add(CombatActionResolver.ResolveEnvironmentDamage(actor,target,true,definition.AttackCapacity,
                    definition.WaterMultiplier*(request.Charged?definition.ChargeMultiplier:1),"element_water").ForTarget(id,default));
            }
            var objectChanges=session.SceneObjects.PrepareDamage(preview.ErosionObjectIds,definition.ErosionDamage);
            return () =>
            {
                session.Surfaces.ApplyChanges(preview.Changes,seat.Definition.AbilityInstanceId);
                foreach(var pair in copies) { session.Combatants.TryGet(pair.Key,out var original);original.CommitResolvedState(pair.Value); }
                session.Surfaces.PublishBoard(session,objectChanges);
                session.Surfaces.Receipt(seat.Definition.AbilityInstanceId,tick,preview.Changes,preview.Pushes,damage,objectChanges,fallDamage,
                    copies.Values.Select(value=>new CombatSurfaceTargetSnapshot(value)));
                session.Surfaces.Reconcile(session);
            };
        }
    }
}
