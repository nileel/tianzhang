using System;
using TianZhang.Content;

namespace TianZhang.Features.Adventure
{
    public sealed class ReturnNodeHandler : IAdventureNodeHandler
    {
        public const string StableNodeTypeId = "adventure_node_return";
        private readonly AdventureNodeAction returnToSource;

        public ReturnNodeHandler(AdventureNodeAction action)
        {
            returnToSource = action ?? throw new ArgumentNullException(nameof(action));
        }

        public string NodeTypeId => StableNodeTypeId;

        public bool TryValidate(AdventureNodeData node, ContentCatalogData catalog, out string reason)
        {
            reason = string.IsNullOrWhiteSpace(node?.contentId) ? null : "adventure_return_content_must_be_empty";
            return reason == null;
        }

        public bool TryHandle(AdventureNodeData node, out string reason) => returnToSource(node, out reason);
    }
}
