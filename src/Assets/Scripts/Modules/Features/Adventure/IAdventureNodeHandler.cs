using TianZhang.Content;

namespace TianZhang.Features.Adventure
{
    public delegate bool AdventureNodeAction(AdventureNodeData node, out string reason);

    public interface IAdventureNodeHandler
    {
        string NodeTypeId { get; }
        bool TryValidate(AdventureNodeData node, ContentCatalogData catalog, out string reason);
        bool TryHandle(AdventureNodeData node, out string reason);
    }
}
