using System.Collections;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Features.Adventure;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongFormalUiTextPlayModeTests
    {
        [UnityTest]
        public IEnumerator AdventureHudShowsMapWithoutOwningCombatHud()
        {
            AdventureMapData map = ScriptableObject.CreateInstance<AdventureMapData>();
            map.adventureId = "test";
            map.displayNameKey = "adventure_test";
            map.nodes = new[]
            {
                new AdventureNodeData { nodeId = "start", nodeTypeId = "adventure_node_start" },
            };
            var go = new GameObject("AdventureHud");
            AdventureHudPresenter hud = go.AddComponent<AdventureHudPresenter>();
            Assert.DoesNotThrow(() => hud.Present(new AdventureSession(map, map.nodes[0]), _ => true, null));
            Assert.IsNull(go.GetComponent<TianZhang.Features.CombatPresentation.CombatHudPresenter>());
            Object.Destroy(go);
            Object.Destroy(map);
            yield return null;
        }
    }
}
