using System;
using System.Collections.Generic;
using TianZhang.Content;

namespace TianZhang.Features.Adventure
{
    public sealed class AdventureNodeDispatcher
    {
        private readonly Dictionary<string, IAdventureNodeHandler> handlers;

        public AdventureNodeDispatcher(IEnumerable<IAdventureNodeHandler> nodeHandlers)
        {
            handlers = new Dictionary<string, IAdventureNodeHandler>(StringComparer.Ordinal);
            if (nodeHandlers == null) throw new ArgumentNullException(nameof(nodeHandlers));
            foreach (IAdventureNodeHandler handler in nodeHandlers)
            {
                if (handler == null || string.IsNullOrWhiteSpace(handler.NodeTypeId))
                    throw new ArgumentException("Adventure node handlers require stable type IDs.", nameof(nodeHandlers));
                if (!handlers.TryAdd(handler.NodeTypeId, handler))
                    throw new ArgumentException("Duplicate adventure node handler: " + handler.NodeTypeId, nameof(nodeHandlers));
            }
        }

        public bool TryValidate(AdventureNodeData node, ContentCatalogData catalog, out string reason)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.nodeTypeId) ||
                !handlers.TryGetValue(node.nodeTypeId, out IAdventureNodeHandler handler))
            {
                reason = "adventure_node_handler_missing";
                return false;
            }
            return handler.TryValidate(node, catalog, out reason);
        }

        public bool TryDispatch(AdventureNodeData node, out string reason)
        {
            if (node == null || !handlers.TryGetValue(node.nodeTypeId ?? string.Empty, out IAdventureNodeHandler handler))
            {
                reason = "adventure_node_handler_missing";
                return false;
            }
            return handler.TryHandle(node, out reason);
        }

        public sealed class StartNodeHandler : IAdventureNodeHandler
        {
            public const string StableNodeTypeId = "adventure_node_start";
            public string NodeTypeId => StableNodeTypeId;
            public bool TryValidate(AdventureNodeData node, ContentCatalogData catalog, out string reason)
            {
                reason = string.IsNullOrWhiteSpace(node.contentId) ? null : "adventure_start_content_must_be_empty";
                return reason == null;
            }
            public bool TryHandle(AdventureNodeData node, out string reason)
            {
                reason = "adventure_start_selected";
                return true;
            }
        }
    }
}
