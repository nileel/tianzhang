using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Cultivation;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.Settlement;
using TianZhang.Game.CharacterCreation;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests.EditMode
{
    public sealed class GuanzhongFormalEndToEndTests
    {
        private const string BountyId = "bounty_guanzhong_shijiahou";
        private const string SettlementId = "guanzhong_city";
        private readonly List<Object> temporaryObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object temporaryObject in temporaryObjects)
                if (temporaryObject != null) Object.DestroyImmediate(temporaryObject);
            temporaryObjects.Clear();
        }

        [Test]
        public void FormalFeatureChainConsumesVictoryOnlyOnceBeforeClaimAndRestore()
        {
            ContentCatalogData catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                "Assets/Data/ContentCatalog/ContentCatalog.asset");
            EnvironmentProfileAsset environment = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            CharacterCreationPointBuyConfig pointBuyConfig =
                AssetDatabase.LoadAssetAtPath<CharacterCreationPointBuyConfig>(
                    "Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset");
            Assert.IsNotNull(catalog);
            Assert.IsNotNull(environment);
            Assert.IsNotNull(pointBuyConfig);
            Assert.IsTrue(catalog.TryGetAdventureMap(
                FormalEncounterRules.GuanzhongWildAdventureId, out AdventureMapData map));
            Assert.IsTrue(catalog.TryGetEnemy(
                FormalEncounterRules.ShijiahouEnemyId, out EnemyData enemy));

            CharacterCreationDraft draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.CharacterName = "01G 定向返工";
            draft.OriginId = "origin_minor_clan";
            CharacterData profile = Track(CharacterCreationManager.CreateProfile(draft, pointBuyConfig));
            var runtime = new GameRuntime();
            runtime.BeginNewGame(
                CharacterRuntimeProfile.FromDefinition("player", profile),
                CultivationState.FromDefinition(profile.foundationPurpleMansionState),
                "guanzhong_hub");
            Assert.AreEqual(GameplaySceneNames.World, runtime.EnterWorld("guanzhong_hub"));
            Assert.AreEqual(GameplaySceneNames.Settlement, runtime.EnterSettlement(SettlementId));

            BountyBoardView board = Track(new GameObject("FormalBountyBoard"))
                .AddComponent<BountyBoardView>();
            board.Show(catalog, SettlementId, runtime.Bounties);
            board.SubmitAccept(BountyId);
            Assert.AreEqual(BountyStatus.Accepted, runtime.Bounties.GetState(BountyId).Status);
            Assert.AreEqual(
                GameplaySceneNames.Adventure,
                runtime.EnterAdventure(
                    FormalEncounterRules.GuanzhongWildAdventureId,
                    SceneReturnTarget.Settlement(SettlementId)));

            GameObject adventureObject = Track(new GameObject("FormalAdventureController"));
            AdventureController controller = adventureObject.AddComponent<AdventureController>();
            AdventureInputController input = adventureObject.AddComponent<AdventureInputController>();
            AdventureHudPresenter hud = adventureObject.AddComponent<AdventureHudPresenter>();
            AdventureUnitSpawner spawner = adventureObject.AddComponent<AdventureUnitSpawner>();
            EncounterCoordinator encounters = adventureObject.AddComponent<EncounterCoordinator>();
            GameObject markerPrefab = Track(new GameObject("FormalMarkerPrefab"));
            int sceneLoadCount = 0;
            string loadedScene = null;
            controller.Configure(
                catalog,
                map,
                runtime.Player.Capture(),
                environment,
                markerPrefab,
                System.Array.Empty<AttackProfileData>(),
                new AdventureMapLoader(),
                spawner,
                input,
                hud,
                encounters,
                new CombatEntryAdapter(),
                runtime,
                runtime.Bounties,
                runtime.InventoryGrants,
                sceneName =>
                {
                    sceneLoadCount++;
                    loadedScene = sceneName;
                });
            controller.SetEncounterRandomSource(new SequenceRandomSource(0, 0));

            controller.ResolveEncounter(CombatSessionOutcome.Victory, enemy);

            Assert.AreEqual(1, sceneLoadCount);
            Assert.AreEqual(GameplaySceneNames.Settlement, loadedScene);
            Assert.AreEqual(BountyStatus.ObjectiveCompleted, runtime.Bounties.GetState(BountyId).Status);
            Assert.AreEqual(1, runtime.Bounties.GetState(BountyId).Progress);
            Assert.AreEqual(1, InventoryQuantity(runtime, "item_shijia_piece"));
            Assert.AreEqual(1, InventoryQuantity(runtime, "item_lingshi_low"));

            LogAssert.Expect(LogType.Error, new Regex(FormalEncounterRules.AlreadyConsumedReason));
            controller.ResolveEncounter(CombatSessionOutcome.Victory, enemy);

            Assert.AreEqual(FormalEncounterRules.AlreadyConsumedReason, controller.LastFailureReason);
            Assert.AreEqual(1, sceneLoadCount);
            Assert.AreEqual(1, runtime.Bounties.GetState(BountyId).Progress);
            Assert.AreEqual(1, InventoryQuantity(runtime, "item_shijia_piece"));
            Assert.AreEqual(1, InventoryQuantity(runtime, "item_lingshi_low"));

            board.Show(catalog, SettlementId, runtime.Bounties);
            board.SubmitClaim(BountyId);
            Assert.AreEqual(BountyStatus.Claimed, runtime.Bounties.GetState(BountyId).Status);
            Assert.AreEqual(4, InventoryQuantity(runtime, "item_lingshi_low"));

            string saved = runtime.CaptureSaveJson();
            var restored = new GameRuntime();
            restored.RestoreSaveJson(saved, catalog);
            Assert.AreEqual(BountyStatus.Claimed, restored.Bounties.GetState(BountyId).Status);
            Assert.AreEqual(1, restored.Bounties.GetState(BountyId).Progress);
            Assert.AreEqual(1, InventoryQuantity(restored, "item_shijia_piece"));
            Assert.AreEqual(4, InventoryQuantity(restored, "item_lingshi_low"));
            Assert.AreEqual("guanzhong_hub", restored.Navigation.WorldNodeId);
            Assert.AreEqual(SettlementId, restored.Navigation.SettlementId);
            Assert.IsNull(restored.Navigation.AdventureId);
        }

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

        private T Track<T>(T value)
            where T : Object
        {
            temporaryObjects.Add(value);
            return value;
        }

        private static int InventoryQuantity(GameRuntime runtime, string itemId)
        {
            foreach (InventoryRecord record in runtime.CaptureSave().inventory)
                if (record.itemId == itemId) return record.quantity;
            return 0;
        }

        private sealed class SequenceRandomSource : IFormalEncounterRandomSource
        {
            private readonly Queue<int> values;

            public SequenceRandomSource(params int[] values)
            {
                this.values = new Queue<int>(values);
            }

            public int NextPercent()
            {
                return values.Count == 0 ? 0 : values.Dequeue();
            }
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
