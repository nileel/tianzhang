using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Features.Adventure;
using UnityEngine;

namespace TianZhang.Tests.EditMode
{
    public sealed class GuanzhongFormalEndToEndTests
    {
        [Test]
        public void RegisteredFutureNodeExtendsDispatchWithoutChangingLoaderOrInput()
        {
            var map = ScriptableObject.CreateInstance<AdventureMapData>();
            map.adventureId = "extension_test";
            map.nodes = new[]
            {
                new AdventureNodeData
                {
                    nodeId = "start",
                    nodeTypeId = AdventureNodeDispatcher.StartNodeHandler.StableNodeTypeId,
                    q = 0,
                    r = 0,
                },
                new AdventureNodeData
                {
                    nodeId = "resource",
                    nodeTypeId = "adventure_node_resource_test",
                    q = 1,
                    r = 0,
                    contentId = "resource_fixture",
                },
            };
            var handler = new RecordingHandler();
            var dispatcher = new AdventureNodeDispatcher(new IAdventureNodeHandler[]
            {
                new AdventureNodeDispatcher.StartNodeHandler(),
                handler,
            });
            var catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            Assert.IsTrue(new AdventureMapLoader().TryLoad(
                map,
                catalog,
                dispatcher,
                out AdventureSession session,
                out string reason), reason);
            Assert.AreEqual("start", session.CurrentNode.nodeId);
            Assert.IsTrue(dispatcher.TryDispatch(map.nodes[1], out reason), reason);
            Assert.AreEqual("resource", handler.HandledNodeId);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(map);
        }

        private sealed class RecordingHandler : IAdventureNodeHandler
        {
            public string NodeTypeId => "adventure_node_resource_test";
            public string HandledNodeId { get; private set; }

            public bool TryValidate(AdventureNodeData node, ContentCatalogData catalog, out string reason)
            {
                reason = null;
                return node.contentId == "resource_fixture";
            }

            public bool TryHandle(AdventureNodeData node, out string reason)
            {
                HandledNodeId = node.nodeId;
                reason = null;
                return true;
            }
        }
    }
}
