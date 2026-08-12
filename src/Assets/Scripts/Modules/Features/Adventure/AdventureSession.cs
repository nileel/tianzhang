using System;
using System.Collections.Generic;
using TianZhang.Content;

namespace TianZhang.Features.Adventure
{
    public sealed class AdventureSession
    {
        private readonly Dictionary<string, AdventureNodeData> nodes;

        public AdventureSession(AdventureMapData map, AdventureNodeData startNode)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            CurrentNode = startNode ?? throw new ArgumentNullException(nameof(startNode));
            nodes = new Dictionary<string, AdventureNodeData>(StringComparer.Ordinal);
            foreach (AdventureNodeData node in map.nodes) nodes.Add(node.nodeId, node);
        }

        public AdventureMapData Map { get; }
        public AdventureNodeData CurrentNode { get; private set; }
        public string Status { get; private set; } = "adventure_ready";

        public bool TryGetNode(string nodeId, out AdventureNodeData node)
        {
            return nodes.TryGetValue(nodeId ?? string.Empty, out node);
        }

        public void Select(AdventureNodeData node, string status)
        {
            CurrentNode = node ?? throw new ArgumentNullException(nameof(node));
            Status = string.IsNullOrWhiteSpace(status) ? "adventure_node_selected" : status;
        }
    }
}
