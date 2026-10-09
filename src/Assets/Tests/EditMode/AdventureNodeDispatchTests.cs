using System;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Features.Adventure;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class AdventureNodeDispatchTests
    {
        [Test]
        public void NewNodeTypeExtendsThroughHandlerWithoutChangingMapLoader()
        {
            var map = ScriptableObject.CreateInstance<AdventureMapData>();
            var catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            try
            {
                map.adventureId = "map_extensible";
                map.nodes = new[]
                {
                    Node("start", AdventureNodeDispatcher.StartNodeHandler.StableNodeTypeId, 0, 0),
                    Node("resource", "adventure_node_resource", 1, 0, "item_resource"),
                };
                var resource = new RecordingHandler("adventure_node_resource");
                var dispatcher = new AdventureNodeDispatcher(new IAdventureNodeHandler[]
                {
                    new AdventureNodeDispatcher.StartNodeHandler(),
                    resource,
                });

                Assert.IsTrue(new AdventureMapLoader().TryLoad(
                    map, catalog, dispatcher, out AdventureSession session, out string reason), reason);
                Assert.IsTrue(dispatcher.TryDispatch(map.nodes[1], out reason), reason);
                Assert.AreSame(map.nodes[1], resource.LastHandled);
                Assert.AreEqual("start", session.CurrentNode.nodeId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
                UnityEngine.Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void MissingAndDuplicateHandlersFailClosed()
        {
            var dispatcher = new AdventureNodeDispatcher(new IAdventureNodeHandler[]
            {
                new AdventureNodeDispatcher.StartNodeHandler(),
            });
            Assert.IsFalse(dispatcher.TryDispatch(
                Node("future", "adventure_node_city_entrance", 2, 3), out string reason));
            Assert.AreEqual("adventure_node_handler_missing", reason);

            Assert.Throws<ArgumentException>(() => new AdventureNodeDispatcher(new IAdventureNodeHandler[]
            {
                new RecordingHandler("adventure_node_resource"),
                new RecordingHandler("adventure_node_resource"),
            }));
        }

        [Test]
        public void MapRequiresExactlyOneStartNodeRegardlessOfRowOrder()
        {
            var map = ScriptableObject.CreateInstance<AdventureMapData>();
            var catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            try
            {
                var dispatcher = new AdventureNodeDispatcher(new IAdventureNodeHandler[]
                {
                    new AdventureNodeDispatcher.StartNodeHandler(),
                    new RecordingHandler("adventure_node_resource"),
                });
                map.adventureId = "start-validation";
                map.nodes = new[]
                {
                    Node("resource", "adventure_node_resource", 1, 0),
                    Node("start", AdventureNodeDispatcher.StartNodeHandler.StableNodeTypeId, 0, 0),
                };
                Assert.IsTrue(new AdventureMapLoader().TryLoad(
                    map, catalog, dispatcher, out AdventureSession session, out string reason), reason);
                Assert.AreEqual("start", session.CurrentNode.nodeId);

                map.nodes = new[] { Node("resource", "adventure_node_resource", 1, 0) };
                Assert.IsFalse(new AdventureMapLoader().TryLoad(map, catalog, dispatcher, out _, out reason));
                Assert.AreEqual("adventure_start_node_missing", reason);

                map.nodes = new[]
                {
                    Node("start-a", AdventureNodeDispatcher.StartNodeHandler.StableNodeTypeId, 0, 0),
                    Node("start-b", AdventureNodeDispatcher.StartNodeHandler.StableNodeTypeId, 1, 0),
                };
                Assert.IsFalse(new AdventureMapLoader().TryLoad(map, catalog, dispatcher, out _, out reason));
                Assert.AreEqual("adventure_start_node_duplicate", reason);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
                UnityEngine.Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void FormalAdjacentEncounterAcceptsTheCommittedBasicAttack()
        {
            var enemyData = ScriptableObject.CreateInstance<EnemyData>();
            try
            {
                var player = Combatant("player", CombatTeam.Player, new HexCoord(0, 0), 20);
                var enemy = Combatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1);
                player.BasicAttackProfileId = "basic_unarmed";
                enemy.BasicAttackProfileId = "basic_unarmed";
                var spawned = new AdventureSpawnSet(
                    player, enemy, enemyData, "basic_unarmed", "basic_unarmed", null);
                AttackProfileData basic = AssetDatabase.LoadAssetAtPath<AttackProfileData>(
                    "Assets/Data/AttackProfiles/AttackProfile_basic_unarmed.asset");
                EnvironmentProfileAsset environment = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(
                    "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");

                Assert.IsTrue(new CombatEntryAdapter(new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0), new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) }, UnityEditor.AssetDatabase.LoadAssetAtPath<TianZhang.Content.CombatRuleSetData>("Assets/Data/CombatRuleSet.asset")).TryCreateSession(
                    spawned, new[] { basic }, environment, out CombatSession session, out string reason), reason);
                var service = new CombatCommandService();
                CombatTurnAdvance turn = service.AdvanceUntilAction(session);
                Assert.AreEqual("player", turn.ActorId);
                CombatActionResult result = service.Execute(
                    session,
                    new CombatCommand(CombatCommandKind.BasicAttack, "player", "enemy", "basic_unarmed"));
                Assert.IsTrue(result.Succeeded, result.RejectionReason);
                Assert.That(enemy.CurrentHealth, Is.LessThan(enemy.MaximumHealth));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(enemyData);
            }
        }

        [TestCase(AdventureSceneState.Combat)]
        [TestCase(AdventureSceneState.Loading)]
        [TestCase(AdventureSceneState.Returning)]
        public void ReturnHandlerAndRepeatedButtonInvocationRejectOutsideExploration(AdventureSceneState state)
        {
            var map = ScriptableObject.CreateInstance<AdventureMapData>();
            var host = new GameObject("NavigationGuardTest");
            try
            {
                map.nodes = new[] { Node("start", "adventure_node_start", 0, 0), Node("return", "adventure_node_return", 0, 2) };
                var session = new AdventureSession(map, map.nodes[0]);
                var runtime = new TianZhang.Bootstrap.GameRuntime();
                runtime.EnterWorld("guanzhong_hub");
                runtime.EnterSettlement("guanzhong_city");
                runtime.EnterAdventure("guanzhong_wild", TianZhang.Gameplay.Contracts.SceneReturnTarget.Settlement("guanzhong_city"));
                var priorNavigation = runtime.Navigation;
                var controller = host.AddComponent<AdventureController>();
                var input = host.AddComponent<AdventureInputController>();
                var hud = host.AddComponent<AdventureHudPresenter>();
                var nodes = new GameObject("Nodes", typeof(RectTransform));
                nodes.transform.SetParent(host.transform, false);
                var status = new GameObject("Status", typeof(RectTransform), typeof(UnityEngine.UI.Text));
                status.transform.SetParent(host.transform, false);
                SetPrivate(hud, "nodeContainer", nodes.transform);
                SetPrivate(hud, "statusText", status.GetComponent<UnityEngine.UI.Text>());
                SetPrivate(controller, "session", session);
                SetPrivate(controller, "hud", hud);
                SetPrivate(controller, "input", input);
                SetPrivate(controller, "navigation", runtime);
                int loads = 0;
                SetPrivate(controller, "loadScene", new Action<string>(_ => loads++));
                SetState(controller, state);
                var dispatcher = ReturnDispatcher(controller);
                input.Configure(session, dispatcher, hud, () => controller.CanSelectExplorationNode);
                hud.Present(session, input.SelectNode, null, false);
                var button = nodes.transform.GetChild(1).GetComponent<UnityEngine.UI.Button>();
                Assert.IsFalse(button.interactable);
                for (int i = 0; i < 2; i++)
                {
                    Assert.IsFalse(dispatcher.TryDispatch(map.nodes[1], out string reason));
                    Assert.AreEqual("adventure_not_exploring", reason);
                    button.onClick.Invoke();
                    Assert.IsFalse(input.SelectNode("return"));
                }
                Assert.AreEqual("adventure_not_exploring", status.GetComponent<UnityEngine.UI.Text>().text);
                Assert.AreEqual(state, controller.CurrentState);
                Assert.AreSame(priorNavigation, runtime.Navigation);
                Assert.AreSame(map.nodes[0], session.CurrentNode);
                Assert.AreEqual("adventure_ready", session.Status);
                Assert.AreEqual(0, loads);
                Assert.AreEqual(CombatSessionOutcome.Ongoing, controller.LastEncounterOutcome);
                Assert.IsNull(controller.LastFormalEncounterResult);
                Assert.IsNull(controller.LastFailureReason);
                Assert.IsFalse((bool)GetPrivate(controller, "formalEncounterConsumed"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void ExplorationReturnUsesOriginalNavigationAndExistingButtonsCanReenable()
        {
            var map = ScriptableObject.CreateInstance<AdventureMapData>();
            var host = new GameObject("ExplorationReturnTest");
            try
            {
                map.nodes = new[] { Node("start", "adventure_node_start", 0, 0), Node("return", "adventure_node_return", 0, 2) };
                var session = new AdventureSession(map, map.nodes[0]);
                var runtime = new TianZhang.Bootstrap.GameRuntime();
                runtime.EnterWorld("guanzhong_hub");
                runtime.EnterSettlement("guanzhong_city");
                runtime.EnterAdventure("guanzhong_wild", TianZhang.Gameplay.Contracts.SceneReturnTarget.Settlement("guanzhong_city"));
                var controller = host.AddComponent<AdventureController>();
                var input = host.AddComponent<AdventureInputController>();
                var hud = host.AddComponent<AdventureHudPresenter>();
                var nodes = new GameObject("Nodes", typeof(RectTransform));
                nodes.transform.SetParent(host.transform, false);
                SetPrivate(hud, "nodeContainer", nodes.transform);
                SetPrivate(controller, "session", session);
                SetPrivate(controller, "hud", hud);
                SetPrivate(controller, "input", input);
                SetPrivate(controller, "navigation", runtime);
                int loads = 0;
                string loaded = null;
                SetPrivate(controller, "loadScene", new Action<string>(scene => { loads++; loaded = scene; }));
                SetState(controller, AdventureSceneState.Exploration);
                input.Configure(session, ReturnDispatcher(controller), hud, () => controller.CanSelectExplorationNode);
                hud.Present(session, input.SelectNode, null, true);
                var button = nodes.transform.GetChild(1).GetComponent<UnityEngine.UI.Button>();
                hud.Present(session, input.SelectNode, null, false);
                Assert.IsFalse(button.interactable);
                hud.Present(session, input.SelectNode, null, true);
                Assert.IsTrue(button.interactable);
                Assert.AreSame(button, nodes.transform.GetChild(1).GetComponent<UnityEngine.UI.Button>());
                Assert.AreEqual(2, nodes.transform.childCount);
                button.onClick.Invoke();
                Assert.AreEqual(1, loads);
                Assert.AreEqual(TianZhang.Gameplay.Contracts.GameplaySceneNames.Settlement, loaded);
                Assert.AreEqual("guanzhong_city", runtime.Navigation.SettlementId);
                Assert.IsNull(runtime.Navigation.AdventureId);
                Assert.AreEqual(AdventureSceneState.Returning, controller.CurrentState);
                Assert.AreSame(map.nodes[1], session.CurrentNode);
                button.onClick.Invoke();
                Assert.AreEqual(1, loads);
                Assert.AreEqual(AdventureSceneState.Returning, controller.CurrentState);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(map);
            }
        }

        private static AdventureNodeDispatcher ReturnDispatcher(AdventureController controller)
        {
            var method = typeof(AdventureController).GetMethod("ReturnToSource",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var action = (AdventureNodeAction)Delegate.CreateDelegate(typeof(AdventureNodeAction), controller, method);
            return new AdventureNodeDispatcher(new IAdventureNodeHandler[] { new ReturnNodeHandler(action) });
        }

        private static void SetState(AdventureController controller, AdventureSceneState state) =>
            typeof(AdventureController).GetProperty("CurrentState").SetValue(controller, state);

        private static void SetPrivate(object target, string name, object value) =>
            target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(target, value);

        private static object GetPrivate(object target, string name) =>
            target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(target);
        private static CombatantSnapshot Combatant(
            string id,
            CombatTeam team,
            HexCoord position,
            int reaction)
        {
            var result = new CombatantSnapshot(id,
                team,
                position,
                reaction,
                100,
                100,
                20,
                0,
                0,
                0,
                100000,
                2000,
                1f,
                new string[0],
                new string[0],
                0,
                0);
            result.SetSpirit(10, 10);
            return result;
        }

        private static AdventureNodeData Node(string id, string type, int q, int r, string contentId = "")
        {
            return new AdventureNodeData
            {
                nodeId = id,
                nodeTypeId = type,
                q = q,
                r = r,
                contentId = contentId,
            };
        }

        private sealed class RecordingHandler : IAdventureNodeHandler
        {
            public RecordingHandler(string nodeTypeId) => NodeTypeId = nodeTypeId;
            public string NodeTypeId { get; }
            public AdventureNodeData LastHandled { get; private set; }

            public bool TryValidate(AdventureNodeData node, ContentCatalogData catalog, out string reason)
            {
                reason = null;
                return true;
            }

            public bool TryHandle(AdventureNodeData node, out string reason)
            {
                LastHandled = node;
                reason = "resource_opened";
                return true;
            }
        }
    }
}
