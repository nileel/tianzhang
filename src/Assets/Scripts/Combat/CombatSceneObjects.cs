using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public sealed class CombatSceneObjectDefinition
    {
        public CombatSceneObjectDefinition(string contentType, string materialType, int maxDurability,
            int coverTier, int maxProtectedBodyScaleRank, bool blocksMovement, bool blocksSight, string residueType,
            bool isSupportMarker = false, bool isTemporaryRamp = false)
        {
            if (string.IsNullOrWhiteSpace(contentType) || string.IsNullOrWhiteSpace(materialType) ||
                maxDurability <= 0 || coverTier < 0 || coverTier > 2 || maxProtectedBodyScaleRank < 0 || coverTier > 0 && maxProtectedBodyScaleRank == 0 ||
                string.IsNullOrWhiteSpace(residueType)) throw new ArgumentException("scene_object_definition_invalid");
            ContentType = contentType; MaterialType = materialType; MaxDurability = maxDurability;
            CoverTier = coverTier; MaxProtectedBodyScaleRank = maxProtectedBodyScaleRank;
            BlocksMovement = blocksMovement; BlocksSight = blocksSight; ResidueType = residueType;
            IsSupportMarker = isSupportMarker;
            IsTemporaryRamp = isTemporaryRamp;
        }
        public string ContentType { get; }
        public string MaterialType { get; }
        public int MaxDurability { get; }
        public int CoverTier { get; }
        public int MaxProtectedBodyScaleRank { get; }
        public bool BlocksMovement { get; }
        public bool BlocksSight { get; }
        public string ResidueType { get; }
        public bool IsSupportMarker { get; }
        public bool IsTemporaryRamp { get; }
    }

    public sealed class CombatSceneObject
    {
        public CombatSceneObject(string id, CombatSceneObjectDefinition definition, HexCoord cell,
            HexCoord? edgeOther = null, string sourceRef = null, string replacesWallConnectionId = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("scene_object_id_invalid");
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            if (edgeOther.HasValue && !cell.TryGetDirectionTo(edgeOther.Value, out _))
                throw new ArgumentException("scene_object_edge_invalid");
            Id = id; Cell = cell; EdgeOther = edgeOther; SourceRef = sourceRef;
            ReplacesWallConnectionId = replacesWallConnectionId; Durability = definition.MaxDurability;
        }
        public string Id { get; }
        public CombatSceneObjectDefinition Definition { get; }
        public HexCoord Cell { get; }
        public HexCoord? EdgeOther { get; }
        public string SourceRef { get; }
        public string ReplacesWallConnectionId { get; }
        public int Durability { get; private set; }
        public bool IsDestroyed => Durability == 0;
        public bool IsDamaged => !IsDestroyed && (long)Durability * 2 <= Definition.MaxDurability;
        public int CoverTier => IsDestroyed ? 0 : IsDamaged ? Math.Min(1, Definition.CoverTier) : Definition.CoverTier;
        public bool Contains(HexCoord cell) => cell == Cell || EdgeOther == cell;
        internal void ApplyDurability(int durability) => Durability = durability;
        internal SpatialSceneObjectRules SpatialRules => new SpatialSceneObjectRules(Id, Cell, EdgeOther,
            !IsDestroyed && Definition.BlocksMovement, !IsDestroyed && Definition.BlocksSight,
            CoverTier, Definition.MaxProtectedBodyScaleRank, ReplacesWallConnectionId, Definition.IsSupportMarker,
            !IsDestroyed && Definition.IsTemporaryRamp);
    }

    public readonly struct CombatSceneObjectChange
    {
        public CombatSceneObjectChange(string id, string contentType, int before, int after)
        { Id = id; ContentType = contentType; Before = before; After = after; }
        public string Id { get; }
        public string ContentType { get; }
        public int Before { get; }
        public int After { get; }
        public int Damage => Before - After;
    }

    /// <summary>Session-local passive objects. One action prepares all effects, then publishes one board revision.</summary>
    public sealed class CombatSceneObjects
    {
        private readonly SpatialQueryBoard baseBoard;
        private Dictionary<string, CombatSceneObject> objects;
        private readonly CombatSceneObject[] originalObjects;
        private IReadOnlyList<SpatialMetricOverride> metricOverrides = Array.Empty<SpatialMetricOverride>();
        private IReadOnlyDictionary<HexCoord,int> surfaceBurdens = new Dictionary<HexCoord,int>();
        public CombatSceneObjects(SpatialQueryBoard board, IEnumerable<CombatSceneObject> instances,
            int lightCoverHitPenalty, int heavyCoverHitPenalty)
        {
            baseBoard = board ?? throw new ArgumentNullException(nameof(board));
            objects = (instances ?? throw new ArgumentNullException(nameof(instances))).ToDictionary(value => value.Id, StringComparer.Ordinal);
            originalObjects = objects.Values.ToArray();
            if (lightCoverHitPenalty < 0 || heavyCoverHitPenalty < lightCoverHitPenalty || heavyCoverHitPenalty > 100)
                throw new ArgumentException("cover_hit_penalty_invalid");
            LightCoverHitPenalty = lightCoverHitPenalty; HeavyCoverHitPenalty = heavyCoverHitPenalty;
            All = objects.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToList().AsReadOnly();
            RebuildBoard();
        }
        public IReadOnlyList<CombatSceneObject> All { get; private set; }
        public SpatialQueryBoard Board { get; private set; }
        public long Revision { get; private set; }
        public int LightCoverHitPenalty { get; }
        public int HeavyCoverHitPenalty { get; }
        public int HitPenalty(int tier) => tier == 2 ? HeavyCoverHitPenalty : tier == 1 ? LightCoverHitPenalty : 0;
        public CombatSceneObject Find(string id) => objects.TryGetValue(id, out var value) ? value : null;

        public IReadOnlyList<CombatSceneObjectChange> PrepareDamage(IEnumerable<string> ids, int damage)
        {
            if (damage <= 0) return Array.Empty<CombatSceneObjectChange>();
            return ids.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).Select(id =>
            {
                CombatSceneObject value = objects[id];
                return new CombatSceneObjectChange(id, value.Definition.ContentType, value.Durability, Math.Max(0, value.Durability - damage));
            }).ToList().AsReadOnly();
        }

        internal void Apply(IReadOnlyList<CombatSceneObjectChange> changes)
        {
            if (changes.Count == 0) return;
            foreach (var change in changes) objects[change.Id].ApplyDurability(change.After);
            RebuildBoard();
            Revision++;
        }

        internal Action PrepareAbilityObjects(IReadOnlyList<CombatSceneObject> abilityObjects,
            IReadOnlyList<SpatialMetricOverride> overrides)
        {
            var next = originalObjects.Concat(abilityObjects).ToDictionary(value => value.Id, StringComparer.Ordinal);
            var ordered = next.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToList().AsReadOnly();
            var frozen = Array.AsReadOnly(overrides.ToArray());
            var board = baseBoard.WithCellMovementBurdens(surfaceBurdens).WithMetricOverrides(frozen).WithSceneObjects(ordered.Select(value => value.SpatialRules).ToArray());
            return () => { objects = next; All = ordered; metricOverrides = frozen; Board = board; Revision++; };
        }

        internal void ApplySurfaces(IReadOnlyDictionary<HexCoord,int> burdens, IReadOnlyList<CombatSceneObjectChange> changes)
        {
            foreach (var change in changes) objects[change.Id].ApplyDurability(change.After);
            surfaceBurdens = burdens.ToDictionary(pair=>pair.Key,pair=>pair.Value);
            RebuildBoard(); Revision++;
        }

        private void RebuildBoard() => Board = baseBoard.WithCellMovementBurdens(surfaceBurdens).WithMetricOverrides(metricOverrides)
            .WithSceneObjects(All.Select(value => value.SpatialRules).ToArray());
    }
}
