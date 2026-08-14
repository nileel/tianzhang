using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.Settlement;
using TianZhang.Features.WorldMap;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using TianZhang.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongBasicAttackPlayModeTests
    {
        private const string BountyId = "bounty_guanzhong_shijiahou";
        private const string SettlementId = "guanzhong_city";
        private const string AdventureId = "guanzhong_wild";
        private string temporarySaveDirectory;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return null;
            if (!string.IsNullOrWhiteSpace(temporarySaveDirectory) && Directory.Exists(temporarySaveDirectory))
                Directory.Delete(temporarySaveDirectory, true);
            temporarySaveDirectory = null;
        }

        [UnityTest]
        public IEnumerator FormalFeatureSceneChainCreatesFightsClaimsSavesAndLoads()
        {
            GameBootstrap staleBootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (staleBootstrap != null)
            {
                Object.Destroy(staleBootstrap.gameObject);
                yield return null;
            }

            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return WaitForScene(GameplaySceneNames.StartMenu);

            GameBootstrap bootstrap = GameBootstrap.RequireInstance();
            temporarySaveDirectory = Path.Combine(
                Application.temporaryCachePath,
                "TianZhang-01G-" + Guid.NewGuid().ToString("N"));
            SetPrivateField(bootstrap, "slotStore", new GameSaveSlotStore(temporarySaveDirectory));
            const string slotId = "formal-01g";
            CharacterCreationController creation = FindInActiveScene<CharacterCreationController>();
            Assert.IsNotNull(creation, "StartMenu must expose the CharacterCreation feature owner.");
            creation.Open();
            creation.Draft.OriginId = "origin_minor_clan";
            creation.Draft.Innate = new InnateAttributeSet(15, 3, 6, 3, 3);
            creation.Submit(slotId, "01G 正式薄切片");
            yield return WaitForScene(GameplaySceneNames.World);

            GameRuntime runtime = GameBootstrap.RequireRuntime();
            Assert.IsNotNull(runtime.Player);
            Assert.AreEqual("guanzhong_hub", runtime.Navigation.WorldNodeId);
            WorldMapController world = FindInActiveScene<WorldMapController>();
            Assert.IsNotNull(world, "WorldScene must expose the WorldMap feature owner.");
            Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation();
            yield return WaitForScene(GameplaySceneNames.Settlement);

            SettlementController settlement = FindInActiveScene<SettlementController>();
            Assert.IsNotNull(settlement, "SettlementScene must expose the Settlement feature owner.");
            Assert.AreEqual(SettlementId, settlement.CurrentSettlementId);
            ContentCatalogData catalog = GetPrivateField<ContentCatalogData>(settlement, "contentCatalog");
            BountyBoardView board = FindInActiveScene<BountyBoardView>();
            Assert.IsNotNull(board, "SettlementScene must expose the bounty board view.");
            board.Show(catalog, SettlementId, runtime.Bounties);
            board.SubmitAccept(BountyId);
            Assert.AreEqual(BountyStatus.Accepted, runtime.Bounties.GetState(BountyId).Status);

            Assert.IsTrue(settlement.EnterAdventure(AdventureId));
            yield return WaitForScene(GameplaySceneNames.Adventure);
            AdventureController adventure = FindInActiveScene<AdventureController>();
            AdventureInputController adventureInput = FindInActiveScene<AdventureInputController>();
            EncounterCoordinator encounter = FindInActiveScene<EncounterCoordinator>();
            Assert.IsNotNull(adventure);
            Assert.IsNotNull(adventureInput);
            Assert.IsNotNull(encounter);
            yield return WaitForAdventureReady(adventure);
            AssertAdventureNodeButtonReadable("shijiahou_encounter");
            adventure.SetEncounterRandomSource(new SequenceRandomSource(0, 0));
            Assert.IsTrue(adventureInput.SelectNode("shijiahou_encounter"));
            Assert.AreEqual(AdventureSceneState.Combat, adventure.CurrentState);
            AssertTechnicalMarker("PlayerMarker", Color.cyan);
            AssertTechnicalMarker("EnemyMarker", Color.red);

            Assert.IsInstanceOf<ICombatCommandHandler>(encounter);
            ICombatCommandHandler combatCommands = encounter;
            for (int frame = 0;
                 frame < 600 && SceneManager.GetActiveScene().name == GameplaySceneNames.Adventure;
                 frame++)
            {
                combatCommands.RequestBasicAttack("player", "enemy");
                yield return null;
            }
            Assert.AreEqual(
                GameplaySceneNames.Settlement,
                SceneManager.GetActiveScene().name,
                "The formal combat did not resolve and return to its source settlement.");

            runtime = GameBootstrap.RequireRuntime();
            BountyState completed = runtime.Bounties.GetState(BountyId);
            Assert.AreEqual(BountyStatus.ObjectiveCompleted, completed.Status);
            Assert.AreEqual(1, completed.Progress);
            Assert.AreEqual(1, InventoryQuantity(runtime, "item_shijia_piece"));
            Assert.AreEqual(1, InventoryQuantity(runtime, "item_lingshi_low"));
            Assert.AreEqual(SettlementId, runtime.Navigation.SettlementId);
            Assert.IsNull(runtime.Navigation.AdventureId);

            settlement = FindInActiveScene<SettlementController>();
            catalog = GetPrivateField<ContentCatalogData>(settlement, "contentCatalog");
            board = FindInActiveScene<BountyBoardView>();
            board.Show(catalog, SettlementId, runtime.Bounties);
            board.SubmitClaim(BountyId);
            Assert.AreEqual(BountyStatus.Claimed, runtime.Bounties.GetState(BountyId).Status);
            Assert.AreEqual(1, InventoryQuantity(runtime, "item_shijia_piece"));
            Assert.AreEqual(4, InventoryQuantity(runtime, "item_lingshi_low"));
            string expectedSave = runtime.CaptureSaveJson();

            settlement.SaveAndReturnToMenu();
            yield return WaitForScene(GameplaySceneNames.StartMenu);
            StartMenuSceneInstaller startMenu = FindInActiveScene<StartMenuSceneInstaller>();
            Assert.IsNotNull(startMenu);
            Assert.IsTrue(startMenu.ListSlots()[0].CanLoad);
            StartMenuController startMenuController = FindInActiveScene<StartMenuController>();
            startMenuController.LoadPlayer(slotId);
            yield return WaitForScene(GameplaySceneNames.Settlement);

            GameRuntime restored = GameBootstrap.RequireRuntime();
            Assert.AreEqual(expectedSave, restored.CaptureSaveJson());
            Assert.AreEqual(BountyStatus.Claimed, restored.Bounties.GetState(BountyId).Status);
            Assert.AreEqual(1, restored.Bounties.GetState(BountyId).Progress);
            Assert.AreEqual(1, InventoryQuantity(restored, "item_shijia_piece"));
            Assert.AreEqual(4, InventoryQuantity(restored, "item_lingshi_low"));
            Assert.AreEqual("guanzhong_hub", restored.Navigation.WorldNodeId);
            Assert.AreEqual(SettlementId, restored.Navigation.SettlementId);
            Assert.IsNull(restored.Navigation.AdventureId);
        }

        [UnityTest]
        public IEnumerator CombatEntryRejectsMissingCommittedProfiles()
        {
            var adapter = new CombatEntryAdapter();
            bool created = adapter.TryCreateSession(
                null,
                new AttackProfileData[0],
                null,
                out CombatSession session,
                out string reason);
            Assert.IsFalse(created);
            Assert.IsNull(session);
            Assert.AreEqual("adventure_spawn_set_missing", reason);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UnitSpawnerRejectsMissingPlayerWithoutFallback()
        {
            var go = new GameObject("AdventureUnitSpawnerTest");
            AdventureUnitSpawner spawner = go.AddComponent<AdventureUnitSpawner>();
            bool spawned = spawner.TrySpawn(
                null,
                ScriptableObject.CreateInstance<ContentCatalogData>(),
                new AdventureNodeData { nodeId = "start", q = 0, r = 0 },
                new AdventureNodeData { nodeId = "encounter", q = 1, r = 0, contentId = "enemy" },
                new GameObject("MarkerPrefab"),
                out AdventureSpawnSet result,
                out string reason);
            Assert.IsFalse(spawned);
            Assert.IsNull(result);
            Assert.AreEqual("adventure_player_missing", reason);
            Object.Destroy(go);
            yield return null;
        }

        private static IEnumerator WaitForScene(string sceneName)
        {
            for (int frame = 0; frame < 120; frame++)
            {
                if (SceneManager.GetActiveScene().name == sceneName)
                {
                    yield return null;
                    yield break;
                }
                yield return null;
            }
            Assert.Fail("Scene did not load: " + sceneName);
        }

        private static IEnumerator WaitForAdventureReady(AdventureController controller)
        {
            for (int frame = 0; frame < 120; frame++)
            {
                if (controller != null && controller.Session != null &&
                    controller.CurrentState == AdventureSceneState.Exploration)
                    yield break;
                yield return null;
            }
            Assert.Fail("AdventureScene did not reach the exploration state.");
        }

        private static T FindInActiveScene<T>()
            where T : Component
        {
            Scene activeScene = SceneManager.GetActiveScene();
            foreach (T value in Object.FindObjectsByType<T>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
                if (value.gameObject.scene == activeScene) return value;
            return null;
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private field: " + fieldName);
            return (T)field.GetValue(target);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private field: " + fieldName);
            field.SetValue(target, value);
        }

        private static int InventoryQuantity(GameRuntime runtime, string itemId)
        {
            foreach (InventoryRecord record in runtime.CaptureSave().inventory)
                if (record.itemId == itemId) return record.quantity;
            return 0;
        }

        private static void AssertAdventureNodeButtonReadable(string nodeId)
        {
            GameObject buttonObject = GameObject.Find("AdventureNode_" + nodeId);
            Assert.IsNotNull(buttonObject, "Adventure HUD did not create the expected runtime node button.");
            Image image = buttonObject.GetComponent<Image>();
            Text label = buttonObject.GetComponentInChildren<Text>(true);
            Assert.IsNotNull(image);
            Assert.IsNotNull(label);
            Assert.AreEqual(new Color(0.2f, 0.34f, 0.3f, 1f), image.color);
            Assert.AreEqual(new Color(0.91f, 0.88f, 0.77f, 1f), label.color);
            Assert.AreNotEqual(image.color, label.color, "Adventure node labels must contrast with their button background.");
        }

        private static void AssertTechnicalMarker(string objectName, Color expectedColor)
        {
            GameObject marker = GameObject.Find(objectName);
            Assert.IsNotNull(marker, "Adventure combat did not create " + objectName + ".");
            Assert.Greater(marker.transform.position.y, 0f, objectName + " must use the 3D ground plane.");
            Assert.Zero(marker.GetComponentsInChildren<SpriteRenderer>(true).Length,
                objectName + " must not use the legacy SpriteRenderer.");
            MeshRenderer[] renderers = marker.GetComponentsInChildren<MeshRenderer>(true);
            Assert.GreaterOrEqual(renderers.Length, 2, objectName + " must expose body and facing meshes.");
            int baseColorId = Shader.PropertyToID("_BaseColor");
            foreach (MeshRenderer renderer in renderers)
            {
                Assert.AreEqual(ShadowCastingMode.On, renderer.shadowCastingMode);
                Assert.IsTrue(renderer.receiveShadows);
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Color actual = properties.GetColor(baseColorId);
                Assert.That(actual.r, Is.EqualTo(expectedColor.r).Within(0.001f));
                Assert.That(actual.g, Is.EqualTo(expectedColor.g).Within(0.001f));
                Assert.That(actual.b, Is.EqualTo(expectedColor.b).Within(0.001f));
                Assert.That(actual.a, Is.EqualTo(expectedColor.a).Within(0.001f));
            }
        }

        private sealed class SequenceRandomSource : IFormalEncounterRandomSource
        {
            private readonly int[] values;
            private int index;

            public SequenceRandomSource(params int[] values)
            {
                this.values = values ?? Array.Empty<int>();
            }

            public int NextPercent()
            {
                return index < values.Length ? values[index++] : 0;
            }
        }
    }
}
