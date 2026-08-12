using System;
using System.Collections.Generic;
using TianZhang.Content;

namespace TianZhang.Features.Adventure
{
    public sealed class AdventureMapLoader
    {
        public bool TryLoad(
            AdventureMapData map,
            ContentCatalogData catalog,
            AdventureNodeDispatcher dispatcher,
            out AdventureSession session,
            out string reason)
        {
            session = null;
            if (map == null || string.IsNullOrWhiteSpace(map.adventureId) || map.nodes == null || map.nodes.Length == 0)
            {
                reason = "adventure_map_invalid";
                return false;
            }
            if (dispatcher == null)
            {
                reason = "adventure_dispatcher_missing";
                return false;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var coordinates = new HashSet<string>(StringComparer.Ordinal);
            AdventureNodeData startNode = null;
            foreach (AdventureNodeData node in map.nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.nodeId) || !ids.Add(node.nodeId))
                {
                    reason = "adventure_node_id_invalid";
                    return false;
                }
                if (!coordinates.Add(node.q + ":" + node.r))
                {
                    reason = "adventure_node_coordinate_duplicate";
                    return false;
                }
                if (string.Equals(
                        node.nodeTypeId,
                        AdventureNodeDispatcher.StartNodeHandler.StableNodeTypeId,
                        StringComparison.Ordinal))
                {
                    if (startNode != null)
                    {
                        reason = "adventure_start_node_duplicate";
                        return false;
                    }
                    startNode = node;
                }
                if (!dispatcher.TryValidate(node, catalog, out reason)) return false;
            }

            if (startNode == null)
            {
                reason = "adventure_start_node_missing";
                return false;
            }

            session = new AdventureSession(map, startNode);
            reason = null;
            return true;
        }
    }
}
