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
#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator AdventureChineseTextSurvivesFirstEntryRefreshRejectionAndReentry()
        {
            var table = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/DataConfig/Language.csv");
            var map = UnityEditor.AssetDatabase.LoadAssetAtPath<AdventureMapData>(
                "Assets/Data/Adventures/AdventureMap_guanzhong_wild.asset");
            Assert.IsNotNull(table);
            Assert.IsNotNull(map);
            TianZhang.Features.Settlement.UiText.Load(table);
            var host = new GameObject("AdventureChineseTextRuntimeTest");
            try
            {
                var hud = host.AddComponent<AdventureHudPresenter>();
                var mapLabel = CreateText(host.transform, "AdventureText");
                var statusLabel = CreateText(host.transform, "AdventureStatus");
                var nodeRoot = new GameObject("AdventureNodeContainer", typeof(RectTransform));
                nodeRoot.transform.SetParent(host.transform, false);
                SetField(hud, "adventureText", mapLabel);
                SetField(hud, "statusText", statusLabel);
                SetField(hud, "nodeContainer", nodeRoot.transform);
                hud.ConfigureText(TianZhang.Features.Settlement.UiText.Resolve,
                    reason => TianZhang.Features.Settlement.UiText.ReasonDisplay(reason, "操作未完成"));
                var session = new AdventureSession(map, map.nodes[0]);
                string selectedNodeId = null;
                hud.Present(session, id => { selectedNodeId = id; return true; }, null);
                yield return null;
                Assert.AreEqual("关中野外", mapLabel.text);
                Assert.AreEqual(TianZhang.Features.Settlement.UiText.Resolve("adventure_ready"), statusLabel.text);
                Assert.AreEqual(map.nodes.Length, nodeRoot.transform.childCount);
                for (int i = 0; i < map.nodes.Length; i++)
                {
                    var node = map.nodes[i];
                    var nodeObject = nodeRoot.transform.GetChild(i);
                    string expected = TianZhang.Features.Settlement.UiText.Resolve("adventure_node_" + node.nodeId);
                    StringAssert.StartsWith(expected, nodeObject.GetComponentInChildren<UnityEngine.UI.Text>().text);
                    nodeObject.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                    Assert.AreEqual(node.nodeId, selectedNodeId, "Display translation must preserve command node ID.");
                }
                session.Select(map.nodes[0], "adventure_start_selected");
                hud.Present(session, _ => true, null);
                yield return null;
                Assert.AreEqual(TianZhang.Features.Settlement.UiText.Resolve("adventure_start_selected"), statusLabel.text);
                const string rejection = "adventure_not_exploring:shijiahou_encounter";
                hud.Present(session, _ => true, rejection);
                yield return null;
                Assert.AreEqual(TianZhang.Features.Settlement.UiText.Resolve("adventure_not_exploring"), statusLabel.text);
                Assert.AreEqual("adventure_start_selected", session.Status);
                host.SetActive(false);
                yield return null;
                host.SetActive(true);
                hud.Present(new AdventureSession(map, map.nodes[0]), _ => true, null);
                yield return null;
                Assert.AreEqual("关中野外", mapLabel.text);
                Assert.AreEqual(TianZhang.Features.Settlement.UiText.Resolve("adventure_ready"), statusLabel.text);
                Assert.AreEqual(map.nodes.Length, nodeRoot.transform.childCount, "Reentry must not duplicate node controls.");
            }
            finally
            {
                Object.Destroy(host);
            }
            yield return null;
        }

        private static UnityEngine.UI.Text CreateText(Transform parent, string name)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Text));
            host.transform.SetParent(parent, false);
            return host.GetComponent<UnityEngine.UI.Text>();
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }
#endif
    }
}
