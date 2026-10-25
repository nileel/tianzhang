using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TianZhang.Content
{
    /// <summary>Explicit D4 scene-object definitions; map markers never register objects implicitly.</summary>
    [Serializable]
    public sealed class EnvironmentSceneObjectConfiguration
    {
        public int lightCoverHitPenalty;
        public int heavyCoverHitPenalty;
        public EnvironmentSceneObjectType[] types;
        public EnvironmentSceneObjectInstance[] instances;

        public bool IsEmpty => (types == null || types.Length == 0) &&
            (instances == null || instances.Length == 0) &&
            lightCoverHitPenalty == 0 && heavyCoverHitPenalty == 0;

        public bool TryValidate(EnvironmentBattlefieldCell[] battlefieldCells,
            EnvironmentDirectedEdge[] directedEdges, out string reason)
        {
            // Unity may materialize a null inline serialized class as an empty object on reload.
            bool noTypes = types == null || types.Length == 0;
            bool noInstances = instances == null || instances.Length == 0;
            if (IsEmpty)
            { reason = string.Empty; return true; }
            if (noTypes || noInstances || lightCoverHitPenalty <= 0 ||
                heavyCoverHitPenalty <= lightCoverHitPenalty || heavyCoverHitPenalty > 100)
                return Fail("scene_object_configuration_invalid", out reason);
            if (battlefieldCells == null || directedEdges == null)
                return Fail("scene_object_map_missing", out reason);
            var byType = new Dictionary<string, EnvironmentSceneObjectType>(StringComparer.Ordinal);
            foreach (EnvironmentSceneObjectType type in types)
            {
                if (type == null || string.IsNullOrEmpty(type.contentType))
                    return Fail("scene_object_type_missing", out reason);
                if (byType.ContainsKey(type.contentType))
                    return Fail("scene_object_type_duplicate", out reason);
                if (!type.TryValidate(out reason)) return false;
                byType.Add(type.contentType, type);
            }
            var cells = new HashSet<string>(StringComparer.Ordinal);
            foreach (EnvironmentBattlefieldCell cell in battlefieldCells)
                if (!cells.Add(CellKey(cell.q, cell.r)))
                    return Fail("scene_object_map_cell_duplicate", out reason);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var slots = new HashSet<string>(StringComparer.Ordinal);
            var wallBindings = new HashSet<string>(StringComparer.Ordinal);
            foreach (EnvironmentSceneObjectInstance instance in instances)
            {
                if (instance == null || string.IsNullOrWhiteSpace(instance.instanceId) ||
                    instance.instanceId.Trim() != instance.instanceId ||
                    string.IsNullOrEmpty(instance.contentType) || !byType.TryGetValue(instance.contentType, out var type))
                    return Fail("scene_object_instance_invalid", out reason);
                if (!ids.Add(instance.instanceId)) return Fail("scene_object_instance_duplicate", out reason);
                if (!TryCell(instance.cell, out int q, out int r) || !cells.Contains(instance.cell))
                    return Fail("scene_object_cell_unknown", out reason);
                string slot = "cell:" + instance.cell;
                int otherQ = 0, otherR = 0;
                if (type.anchorMode == "edge")
                {
                    if (!TryCell(instance.edgeOther, out otherQ, out otherR) || !cells.Contains(instance.edgeOther))
                        return Fail("scene_object_edge_cell_unknown", out reason);
                    long dq = (long)otherQ - q, dr = (long)otherR - r;
                    if ((Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2 != 1)
                        return Fail("scene_object_edge_not_adjacent", out reason);
                    slot = "edge:" + (string.CompareOrdinal(instance.cell, instance.edgeOther) < 0
                        ? instance.cell + ">" + instance.edgeOther : instance.edgeOther + ">" + instance.cell);
                    if (!directedEdges.Any(edge => Matches(edge, q, r, otherQ, otherR)) ||
                        !directedEdges.Any(edge => Matches(edge, otherQ, otherR, q, r)))
                        return Fail("scene_object_edge_missing", out reason);
                }
                else if (!string.IsNullOrEmpty(instance.edgeOther))
                    return Fail("scene_object_cell_has_edge", out reason);
                if (!slots.Add(slot)) return Fail("scene_object_slot_duplicate", out reason);
                if (type.contentType == "high_wall")
                {
                    if (string.IsNullOrWhiteSpace(instance.replacesWallConnectionId))
                        return Fail("scene_object_wall_binding_missing", out reason);
                    // One named connection may contain several wall segments. Bind each exact edge pair.
                    string binding = instance.replacesWallConnectionId + "|" + slot;
                    if (!wallBindings.Add(binding)) return Fail("scene_object_wall_binding_reused", out reason);
                    var bound = directedEdges.Where(edge => Matches(edge, q, r, otherQ, otherR) ||
                        Matches(edge, otherQ, otherR, q, r)).ToArray();
                    if (bound.Length != 2 || bound.Any(edge => edge.edgeKind != EnvironmentEdgeKind.EntityWall ||
                            edge.connectionId != instance.replacesWallConnectionId || edge.allowsMovement || edge.allowsEffects))
                        return Fail("scene_object_wall_binding_invalid", out reason);
                }
                else if (!string.IsNullOrEmpty(instance.replacesWallConnectionId))
                    return Fail("scene_object_foreign_wall_binding", out reason);
            }
            reason = string.Empty;
            return true;
        }

        private static bool Matches(EnvironmentDirectedEdge edge, int q, int r, int otherQ, int otherR) =>
            edge.fromQ == q && edge.fromR == r && edge.toQ == otherQ && edge.toR == otherR;
        private static string CellKey(int q, int r) =>
            q.ToString(CultureInfo.InvariantCulture) + ":" + r.ToString(CultureInfo.InvariantCulture);
        private static bool TryCell(string value, out int q, out int r)
        {
            q = r = 0;
            string[] parts = value?.Split(':');
            return parts?.Length == 2 && int.TryParse(parts[0], NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out q) && int.TryParse(parts[1], NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out r) && value == CellKey(q, r);
        }
        private static bool Fail(string failure, out string reason) { reason = failure; return false; }
    }

    [Serializable]
    public sealed class EnvironmentSceneObjectType
    {
        public string contentType;
        public string materialType;
        public string anchorMode;
        public string coverMode;
        public string stateRules;
        public string residueType;
        public string targetFlags;
        public string interactionType;
        public string reactionRules;
        public int maxDurability;
        public int coverTier;
        public int maxProtectedBodyScaleRank;
        public bool blocksMovement;
        public bool blocksSight;

        internal bool TryValidate(out string reason)
        {
            bool lowWall = contentType == "low_wall", highWall = contentType == "high_wall";
            bool plant = contentType == "low_plant";
            if (!(lowWall || highWall || plant || contentType == "stone_support") ||
                materialType != (plant ? "plant" : "stone") ||
                anchorMode != (lowWall || highWall ? "edge" : "cell") ||
                maxDurability <= 0 || stateRules != "half_durability" || targetFlags != "attack" ||
                interactionType != "destructible" || reactionRules != "fixed_residue" ||
                residueType != (lowWall || highWall ? "rubble" : plant ? "charred" : "failed") ||
                blocksMovement != highWall || blocksSight != highWall ||
                coverMode != (lowWall ? "edge" : "none") || coverTier != (lowWall ? 2 : 0) ||
                (lowWall ? maxProtectedBodyScaleRank < 1 : maxProtectedBodyScaleRank != 0))
            { reason = "scene_object_type_contract_invalid"; return false; }
            reason = string.Empty;
            return true;
        }
    }

    [Serializable]
    public sealed class EnvironmentSceneObjectInstance
    {
        public string instanceId;
        public string contentType;
        public string cell;
        public string edgeOther;
        public string sourceRef;
        public string replacesWallConnectionId;
    }
}
