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
                var spawned = new AdventureSpawnSet(
                    player, enemy, enemyData, "basic_unarmed", "basic_unarmed", null, null, null);
                AttackProfileData basic = AssetDatabase.LoadAssetAtPath<AttackProfileData>(
                    "Assets/Data/AttackProfiles/AttackProfile_basic_unarmed.asset");
                EnvironmentProfileAsset environment = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(
                    "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");

                Assert.IsTrue(new CombatEntryAdapter().TryCreateSession(
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

        private static CombatantSnapshot Combatant(
            string id,
            CombatTeam team,
            HexCoord position,
            int reaction)
        {
            var result = new CombatantSnapshot(
                id, team, position, reaction, 100, 100, 20, 0, 0, 0, 1f, 4,
                new string[0], new string[0], 0, 0);
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
