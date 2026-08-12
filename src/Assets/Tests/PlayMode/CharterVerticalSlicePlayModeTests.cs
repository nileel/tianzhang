using System.Collections;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Features.Adventure;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests.PlayMode
{
    public sealed class CharterVerticalSlicePlayModeTests
    {
        [UnityTest]
        public IEnumerator UnknownNodeTypeFailsClosedAtMapLoad()
        {
            AdventureMapData map = ScriptableObject.CreateInstance<AdventureMapData>();
            map.adventureId = "unknown_node_test";
            map.nodes = new[]
            {
                new AdventureNodeData { nodeId = "unknown", nodeTypeId = "future_node", q = 0, r = 0 },
            };
            var dispatcher = new AdventureNodeDispatcher(new IAdventureNodeHandler[]
            {
                new AdventureNodeDispatcher.StartNodeHandler(),
            });
            bool loaded = new AdventureMapLoader().TryLoad(
                map,
                ScriptableObject.CreateInstance<ContentCatalogData>(),
                dispatcher,
                out AdventureSession session,
                out string reason);
            Assert.IsFalse(loaded);
            Assert.IsNull(session);
            Assert.AreEqual("adventure_node_handler_missing", reason);
            Object.Destroy(map);
            yield return null;
        }
    }
}
