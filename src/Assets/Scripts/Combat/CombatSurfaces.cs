using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    /// <summary>One public surface per support cell; converted surfaces never retain a hidden predecessor.</summary>
    public sealed class CombatSurfaces
    {
        private readonly Dictionary<HexCoord,CombatSurfaceCell> cells;
        private readonly Dictionary<string,CombatFireState> fires;
        private readonly Dictionary<string,HashSet<HexCoord>> owned = new Dictionary<string,HashSet<HexCoord>>(StringComparer.Ordinal);
        private readonly List<CombatSurfaceSettlement> settlements = new List<CombatSurfaceSettlement>();
        private readonly int wetBurden, deepBurden;
        private readonly Dictionary<HexCoord,int> baseBurdens = new Dictionary<HexCoord,int>();
        public CombatSurfaces(IEnumerable<CombatSurfaceDefinition> cells, IEnumerable<CombatFireDefinition> fires = null,
            int wetMovementBurden = 1, int deepMovementBurden = 2)
        {
            if (cells == null || wetMovementBurden < 0 || deepMovementBurden < wetMovementBurden)
                throw new ArgumentException("surface_configuration_invalid");
            this.cells=cells.ToDictionary(value=>value.Cell,value=>new CombatSurfaceCell(value));
            this.fires=(fires ?? Array.Empty<CombatFireDefinition>()).ToDictionary(value=>value.SourceRef,value=>new CombatFireState(value),StringComparer.Ordinal);
            wetBurden=wetMovementBurden; deepBurden=deepMovementBurden;
            Cells=Array.AsReadOnly(this.cells.Values.OrderBy(value=>value.Cell.Q).ThenBy(value=>value.Cell.R).ToArray());
            FireSources=Array.AsReadOnly(this.fires.Values.ToArray());
            Settlements=settlements.AsReadOnly();
            foreach(var cell in Cells) AddOwner(cell);
            foreach (var cell in Cells.Where(value=>value.Kind==CombatSurfaceKind.Burning))
                if (cell.SourceRef==null || !this.fires.ContainsKey(cell.SourceRef) || cell.Fuel==0)
                    throw new ArgumentException("surface_fire_source_missing");
            foreach (var fire in FireSources)
                if (OwnedCount(fire.Definition.SourceRef)>fire.Definition.Capacity) throw new ArgumentException("surface_fire_capacity_invalid");
        }
        public IReadOnlyList<CombatSurfaceCell> Cells { get; }
        public IReadOnlyList<CombatFireState> FireSources { get; }
        public IReadOnlyList<CombatSurfaceSettlement> Settlements { get; }
        public long Revision { get; private set; }
        public long SettlementSequence { get; private set; }
        public CombatSurfaceCell Get(HexCoord cell) => cells.TryGetValue(cell,out var value) ? value : null;
        public int OwnedCount(string sourceRef) => sourceRef!=null && owned.TryGetValue(sourceRef,out var set)?set.Count:0;
        internal IReadOnlyCollection<HexCoord> OwnedCells(string sourceRef)=>
            sourceRef!=null && owned.TryGetValue(sourceRef,out var set)?set.ToArray():Array.Empty<HexCoord>();

        private void AddOwner(CombatSurfaceCell cell)
        {
            if(cell.SourceRef==null || cell.Kind==CombatSurfaceKind.None) return;
            if(!owned.TryGetValue(cell.SourceRef,out var set)) owned.Add(cell.SourceRef,set=new HashSet<HexCoord>());
            set.Add(cell.Cell);
        }

        internal void Bind(CombatSession session)
        {
            foreach (var cell in Cells)
            {
                if (!session.SceneObjects.Board.TryGetCell(cell.Cell,out var rules) || cell.FuelObjectId!=null && session.SceneObjects.Find(cell.FuelObjectId)==null)
                    throw new ArgumentException("surface_cell_or_fuel_object_missing");
                baseBurdens[cell.Cell]=Math.Max(0,rules.MovementBurdenUnits-Burden(cell.Kind));
            }
            foreach (var fire in FireSources)
                if (!session.Combatants.TryGet(fire.Definition.ActorId,out _)) throw new ArgumentException("surface_fire_actor_missing");
            PublishBoard(session,Array.Empty<CombatSceneObjectChange>());
        }

        public static CombatSurfaceKind Water(CombatSurfaceKind kind, CombatTideAction action) => action==CombatTideAction.Rise
            ? kind switch { CombatSurfaceKind.None or CombatSurfaceKind.Scorched or CombatSurfaceKind.Burning=>CombatSurfaceKind.Wet,
                CombatSurfaceKind.Wet=>CombatSurfaceKind.ShallowWater, CombatSurfaceKind.ShallowWater=>CombatSurfaceKind.DeepWater, _=>kind }
            : kind switch { CombatSurfaceKind.DeepWater=>CombatSurfaceKind.ShallowWater, CombatSurfaceKind.ShallowWater=>CombatSurfaceKind.Wet,
                CombatSurfaceKind.Wet=>CombatSurfaceKind.None, _=>kind };

        public static CombatSurfaceKind Heat(CombatSurfaceKind kind, bool hasFuel) => kind switch
        {
            CombatSurfaceKind.DeepWater=>CombatSurfaceKind.ShallowWater,
            CombatSurfaceKind.ShallowWater=>CombatSurfaceKind.Wet,
            CombatSurfaceKind.Wet=>CombatSurfaceKind.None,
            CombatSurfaceKind.None or CombatSurfaceKind.Scorched when hasFuel=>CombatSurfaceKind.Burning,
            _=>kind,
        };

        internal void ApplyChanges(IEnumerable<CombatSurfaceChange> changes,string sourceRef)
        {
            foreach (var change in changes)
            {
                var cell=cells[change.Cell];
                if(cell.SourceRef!=null && owned.TryGetValue(cell.SourceRef,out var set)) set.Remove(cell.Cell);
                cell.Kind=change.After;
                cell.SourceRef=change.After is CombatSurfaceKind.None or CombatSurfaceKind.Scorched ? null : sourceRef;
                AddOwner(cell);
            }
            Revision++;
        }

        internal void PublishBoard(CombatSession session,IReadOnlyList<CombatSceneObjectChange> objectChanges)
        {
            session.SceneObjects.ApplySurfaces(Cells.ToDictionary(value=>value.Cell,value=>baseBurdens[value.Cell]+Burden(value.Kind)),objectChanges);
        }

        private int Burden(CombatSurfaceKind kind)=>kind switch
        { CombatSurfaceKind.Wet or CombatSurfaceKind.ShallowWater=>wetBurden,CombatSurfaceKind.DeepWater=>deepBurden,_=>0 };

        internal void Receipt(string sourceRef,int tick,IEnumerable<CombatSurfaceChange> changes,
            IEnumerable<CombatTidePush> pushes,IEnumerable<CombatDamageResult> damage,IEnumerable<CombatSceneObjectChange> objectChanges,
            IEnumerable<CombatDamageResult> fallDamage=null,IEnumerable<CombatSurfaceTargetSnapshot> targets=null,
            HexCoord? tideSource=null, CombatTideAction? tideAction=null)
        { settlements.Add(new CombatSurfaceSettlement(++SettlementSequence,sourceRef,tick,changes,pushes,damage,objectChanges,fallDamage,targets,tideSource,tideAction)); }

        internal void ClearSource(CombatSession session,string sourceRef)
        {
            var changes=OwnedCells(sourceRef).Select(cell=>new CombatSurfaceChange(cell,cells[cell].Kind,CombatSurfaceKind.None)).ToArray();
            if (changes.Length==0) return;
            ApplyChanges(changes,null); PublishBoard(session,Array.Empty<CombatSceneObjectChange>());
            Receipt(sourceRef,session.TurnScheduler.CurrentTick,changes,Array.Empty<CombatTidePush>(),Array.Empty<CombatDamageResult>(),Array.Empty<CombatSceneObjectChange>());
        }

        internal void Reconcile(CombatSession session)
        {
            var changes=new List<CombatSurfaceChange>();
            foreach (var cell in Cells.Where(value=>value.FuelObjectId!=null && value.Fuel>0))
                if (session.SceneObjects.Find(cell.FuelObjectId).IsDestroyed)
                {
                    cell.Fuel=0; Revision++;
                    if (cell.Kind==CombatSurfaceKind.Burning) changes.Add(new CombatSurfaceChange(cell.Cell,cell.Kind,CombatSurfaceKind.Scorched));
                }
            if (changes.Count==0) return;
            ApplyChanges(changes,null); PublishBoard(session,Array.Empty<CombatSceneObjectChange>());
            Receipt("fuel_removed",session.TurnScheduler.CurrentTick,changes,Array.Empty<CombatTidePush>(),Array.Empty<CombatDamageResult>(),Array.Empty<CombatSceneObjectChange>());
        }

        internal void AdvanceTick(CombatSession session,int tick)
        {
            Reconcile(session);
            foreach (var fire in FireSources)
            {
                if (fire.ElapsedTicks>=fire.Definition.DurationTicks) continue;
                fire.ElapsedTicks++; Revision++;
                if (fire.ElapsedTicks%fire.Definition.PeriodTicks==0) BurnCycle(session,fire,tick);
                if (session.IsFinished) return;
                if (fire.ElapsedTicks==fire.Definition.DurationTicks) EndFire(session,fire,tick);
            }
        }

        private void BurnCycle(CombatSession session,CombatFireState fire,int tick)
        {
            string source=fire.Definition.SourceRef;
            var current=OwnedCells(source).Select(Get).Where(value=>value.Kind==CombatSurfaceKind.Burning)
                .OrderBy(value=>value.Cell.Q).ThenBy(value=>value.Cell.R).ToArray();
            if (current.Length==0) return;
            var changes=new List<CombatSurfaceChange>();
            var burning=new List<CombatSurfaceCell>();
            foreach (var cell in current)
            {
                if (cell.Fuel==0 || fire.RemainingFuel==0) changes.Add(new CombatSurfaceChange(cell.Cell,cell.Kind,CombatSurfaceKind.Scorched));
                else { cell.Fuel--; fire.RemainingFuel--; burning.Add(cell); }
            }
            session.Combatants.TryGet(fire.Definition.ActorId,out var sourceActor);
            var sourceSnapshot=sourceActor.CopyForResolution();
            var active=burning.Select(value=>value.Cell).ToHashSet();
            var damage=new List<CombatDamageResult>();
            foreach (var target in session.Combatants.All.Where(value=>value.IsAlive && value.MovementMode==SpatialMovementMode.Ground &&
                value.OccupiedCells.Any(active.Contains)).OrderBy(value=>value.Id,StringComparer.Ordinal))
                damage.Add(CombatActionResolver.ResolveEnvironmentDamage(sourceSnapshot,target,false,fire.Definition.AttackCapacity,
                    fire.Definition.DamageMultiplier,"element_fire").ForTarget(target.Id,default));
            // One finite adjacent ignition; new fire neither damages nor spreads again this cycle.
            if (burning.Count<fire.Definition.Capacity && fire.RemainingFuel>0)
            {
                CombatSurfaceCell candidate=null;
                foreach (var cell in burning)
                {
                    for (int direction=0;direction<6;direction++)
                    {
                        var next=Get(cell.Cell.Step(direction));
                        if (next==null || next.Fuel==0 || next.Kind is not (CombatSurfaceKind.None or CombatSurfaceKind.Scorched) ||
                            !CombatTideRules.CanCrossWaterEdge(session,cell.Cell,next.Cell)) continue;
                        candidate=next; break;
                    }
                    if (candidate!=null) break;
                }
                if (candidate!=null)
                {
                    candidate.Fuel--; fire.RemainingFuel--;
                    changes.Add(new CombatSurfaceChange(candidate.Cell,candidate.Kind,CombatSurfaceKind.Burning));
                }
            }
            foreach (var cell in burning)
                if (cell.Fuel==0 || fire.RemainingFuel==0) changes.Add(new CombatSurfaceChange(cell.Cell,cell.Kind,CombatSurfaceKind.Scorched));
            ApplyChanges(changes,source); PublishBoard(session,Array.Empty<CombatSceneObjectChange>());
            Receipt(source,tick,changes,Array.Empty<CombatTidePush>(),damage,Array.Empty<CombatSceneObjectChange>(),
                targets:damage.Select(value=> { session.Combatants.TryGet(value.TargetId,out var actor);return new CombatSurfaceTargetSnapshot(actor); }));
            session.Jindan?.Reconcile(session); session.Flight?.Reconcile(session);
        }

        private void EndFire(CombatSession session,CombatFireState fire,int tick)
        {
            var changes=Cells.Where(value=>value.Kind==CombatSurfaceKind.Burning && value.SourceRef==fire.Definition.SourceRef)
                .Select(value=>new CombatSurfaceChange(value.Cell,value.Kind,CombatSurfaceKind.Scorched)).ToArray();
            if (changes.Length==0) return;
            ApplyChanges(changes,null); PublishBoard(session,Array.Empty<CombatSceneObjectChange>());
            Receipt(fire.Definition.SourceRef,tick,changes,Array.Empty<CombatTidePush>(),Array.Empty<CombatDamageResult>(),Array.Empty<CombatSceneObjectChange>());
        }

        public void Clear(CombatSession session)
        {
            foreach (var cell in Cells) { cell.Kind=CombatSurfaceKind.None; cell.SourceRef=null; }
            owned.Clear();
            foreach (var fire in FireSources) fire.ElapsedTicks=fire.Definition.DurationTicks;
            Revision++; PublishBoard(session,Array.Empty<CombatSceneObjectChange>());
        }
    }
}
