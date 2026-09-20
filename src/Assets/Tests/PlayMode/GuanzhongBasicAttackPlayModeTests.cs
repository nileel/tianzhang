using System;
using System.Collections;
using System.Collections.Generic;
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
using TianZhang.Spatial;
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
            Screen.SetResolution(1920, 1080, false);
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
            AssertFormalBattlefieldPresentation();
            yield return CaptureFormalTerrainIfRequested();

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
        public IEnumerator FormalPlayerExecutionSamplesHitCriticalAndBlockThresholds()
        {
            var hitBoundary = new SequenceCombatRandomSource(50f, 50f, 50f, 50f);
            var missAboveBoundary = new SequenceCombatRandomSource(51f, 0f, 0f, 0f);
            Assert.Greater(ExecutePlayerBasicAttack(hitBoundary), 0);
            Assert.AreEqual(0, ExecutePlayerBasicAttack(missAboveBoundary));
            Assert.AreEqual(4, hitBoundary.Count);
            Assert.AreEqual(4, missAboveBoundary.Count);

            var criticalBelowBoundary = new SequenceCombatRandomSource(0f, 49f, 99f, 99f);
            var criticalAtBoundary = new SequenceCombatRandomSource(0f, 50f, 99f, 99f);
            int criticalDamage = ExecutePlayerBasicAttack(criticalBelowBoundary);
            int normalDamage = ExecutePlayerBasicAttack(criticalAtBoundary);
            Assert.Greater(criticalDamage, normalDamage);

            var blockBelowBoundary = new SequenceCombatRandomSource(0f, 99f, 49f, 99f);
            var blockAtBoundary = new SequenceCombatRandomSource(0f, 99f, 50f, 99f);
            int blockedDamage = ExecutePlayerBasicAttack(blockBelowBoundary);
            int unblockedDamage = ExecutePlayerBasicAttack(blockAtBoundary);
            Assert.Less(blockedDamage, unblockedDamage);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FormalEnemyRunCombatBranchUsesTheSharedSampler()
        {
            CombatSession session = CreateCombatSession(1, 100, out CombatantSnapshot player, out CombatantSnapshot enemy);
            var source = new SequenceCombatRandomSource(50f, 50f, 50f, 50f);
            EncounterCoordinator coordinator = CreateCoordinator(session, player, enemy, source, out GameObject host, out EnemyData enemyData);
            SetPrivateField(coordinator, "enemyPolicy", new LegalActionAI());
            MethodInfo runCombat = typeof(EncounterCoordinator).GetMethod(
                "RunCombat",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(runCombat);
            int healthBefore = player.CurrentHealth;
            Coroutine coroutine = coordinator.StartCoroutine((IEnumerator)runCombat.Invoke(coordinator, null));

            for (int frame = 0; frame < 10 && source.Count == 0; frame++)
                yield return null;

            Assert.AreEqual(4, source.Count, "The formal enemy action did not sample exactly four rolls.");
            Assert.Less(player.CurrentHealth, healthBefore);
            coordinator.StopCoroutine(coroutine);
            Object.Destroy(host);
            Object.Destroy(enemyData);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InvalidEnumerationAndNonAttackCommandsDoNotConsumeSamples()
        {
            CombatSession session = CreateCombatSession(100, 1, out CombatantSnapshot player, out CombatantSnapshot enemy);
            var source = new SequenceCombatRandomSource();
            EncounterCoordinator coordinator = CreateCoordinator(session, player, enemy, source, out GameObject host, out EnemyData enemyData);
            CombatTurnAdvance advance = new CombatCommandService().AdvanceUntilAction(session);
            Assert.AreEqual("player", advance.ActorId);
            SetPrivateField(coordinator, "acceptsPlayerCommand", true);

            Assert.IsNotEmpty(new CombatLegalActionService().GetLegalActions(session, "player"));
            Assert.AreEqual(0, source.Count);
            ICombatCommandHandler commands = coordinator;
            commands.RequestBasicAttack("player", "missing");
            Assert.AreEqual(0, source.Count, "An invalid attack consumed combat samples.");
            commands.RequestGuard("player");
            Assert.AreEqual(0, source.Count, "A non-attack command consumed combat samples.");

            Object.Destroy(host);
            Object.Destroy(enemyData);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UnauthorizedBasicAttackDoesNotConsumeCombatSamples()
        {
            CombatantSnapshot player = new CombatantSnapshot(
                "player", CombatTeam.Player, new HexCoord(0, 0), 100,
                500, 500, 60, 0, 0, 0, 1f, 0)
            {
                CriticalRate = 50f,
                DodgeRate = 50f,
                BlockRate = 50f,
                BlockReduction = 50f,
                Facing = 0,
                BasicAttackProfileId = "basic_unarmed",
            };
            CombatantSnapshot enemy = new CombatantSnapshot(
                "enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1,
                500, 500, 60, 0, 0, 0, 1f, 0)
            {
                CriticalRate = 50f,
                DodgeRate = 50f,
                BlockRate = 50f,
                BlockReduction = 50f,
                Facing = 3,
                BasicAttackProfileId = "basic_unarmed",
            };
            CombatSession session = new CombatSession(
                new[] { player, enemy },
                new[]
                {
                    new CombatAttackProfile(
                        "basic_unarmed", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, physicalMultiplier: 1f),
                    new CombatAttackProfile(
                        "foreign_basic", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, physicalMultiplier: 1f),
                },
                new AlwaysInRangeCombatSpatialQuery());

            var source = new SequenceCombatRandomSource();
            EncounterCoordinator coordinator = CreateCoordinator(session, player, enemy, source, out GameObject host, out EnemyData enemyData);
            CombatTurnAdvance advance = new CombatCommandService().AdvanceUntilAction(session);
            Assert.AreEqual("player", advance.ActorId);
            SetPrivateField(coordinator, "acceptsPlayerCommand", true);
            SetPrivateField(coordinator, "spawned", new AdventureSpawnSet(
                player, enemy, enemyData, "foreign_basic", "basic_unarmed", Array.Empty<string>(), null, null));

            ((ICombatCommandHandler)coordinator).RequestBasicAttack("player", "enemy");
            Assert.AreEqual(0, source.Count, "An unauthorized attack consumed combat samples.");

            Object.Destroy(host);
            Object.Destroy(enemyData);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SeededSamplerReplaysTheSameLegalCommandSequence()
        {
            var first = new SystemCombatResolutionRandomSource(1729);
            var second = new SystemCombatResolutionRandomSource(1729);
            var firstDamage = new int[3];
            var secondDamage = new int[3];
            for (int index = 0; index < firstDamage.Length; index++)
            {
                firstDamage[index] = ExecutePlayerBasicAttack(first);
                secondDamage[index] = ExecutePlayerBasicAttack(second);
            }
            CollectionAssert.AreEqual(firstDamage, secondDamage);
            yield return null;
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

        private static int ExecutePlayerBasicAttack(ICombatResolutionRandomSource source)
        {
            CombatSession session = CreateCombatSession(100, 1, out CombatantSnapshot player, out CombatantSnapshot enemy);
            EncounterCoordinator coordinator = CreateCoordinator(session, player, enemy, source, out GameObject host, out EnemyData enemyData);
            CombatTurnAdvance advance = new CombatCommandService().AdvanceUntilAction(session);
            Assert.AreEqual("player", advance.ActorId);
            SetPrivateField(coordinator, "acceptsPlayerCommand", true);
            int healthBefore = enemy.CurrentHealth;
            ((ICombatCommandHandler)coordinator).RequestBasicAttack("player", "enemy");
            int damage = healthBefore - enemy.CurrentHealth;
            Object.Destroy(host);
            Object.Destroy(enemyData);
            return damage;
        }

        private static EncounterCoordinator CreateCoordinator(
            CombatSession session,
            CombatantSnapshot player,
            CombatantSnapshot enemy,
            ICombatResolutionRandomSource source,
            out GameObject host,
            out EnemyData enemyData)
        {
            host = new GameObject("CombatResolutionSamplerTest");
            EncounterCoordinator coordinator = host.AddComponent<EncounterCoordinator>();
            enemyData = ScriptableObject.CreateInstance<EnemyData>();
            enemyData.displayNameKey = "enemy_test";
            coordinator.Configure(new RecordingCombatPresentationSink(), (_, _) => { });
            coordinator.SetCombatResolutionRandomSource(source);
            SetPrivateField(coordinator, "session", session);
            SetPrivateField(coordinator, "spawned", new AdventureSpawnSet(
                player,
                enemy,
                enemyData,
                "basic_unarmed",
                "basic_unarmed",
                Array.Empty<string>(),
                null,
                null));
            return coordinator;
        }

        private static CombatSession CreateCombatSession(
            int playerSpeed,
            int enemySpeed,
            out CombatantSnapshot player,
            out CombatantSnapshot enemy)
        {
            player = new CombatantSnapshot(
                "player", CombatTeam.Player, new HexCoord(0, 0), playerSpeed,
                500, 500, 60, 0, 0, 0, 1f, 0)
            {
                CriticalRate = 50f,
                DodgeRate = 50f,
                BlockRate = 50f,
                BlockReduction = 50f,
                Facing = 0,
                BasicAttackProfileId = "basic_unarmed",
            };
            enemy = new CombatantSnapshot(
                "enemy", CombatTeam.Enemy, new HexCoord(1, 0), enemySpeed,
                500, 500, 60, 0, 0, 0, 1f, 0)
            {
                CriticalRate = 50f,
                DodgeRate = 50f,
                BlockRate = 50f,
                BlockReduction = 50f,
                Facing = 3,
                BasicAttackProfileId = "basic_unarmed",
            };
            return new CombatSession(
                new[] { player, enemy },
                new[]
                {
                    new CombatAttackProfile(
                        "basic_unarmed",
                        CombatAttackKind.Basic,
                        CombatAttackEffect.Physical,
                        1,
                        1,
                        physicalMultiplier: 1f),
                },
                new AlwaysInRangeCombatSpatialQuery());
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

        private static void AssertFormalBattlefieldPresentation()
        {
            GameObject battlefield = FindSceneObjectIncludingInactive("GuanzhongBattlefield");
            GameObject comparisonBoard = FindSceneObjectIncludingInactive("VisualBaselineBoard");
            GameObject comparisonPanel = FindSceneObjectIncludingInactive("BattleVisualComparisonPanel");
            Assert.IsNotNull(battlefield, "AdventureScene is missing the functional Guanzhong battlefield.");
            Assert.IsNotNull(comparisonBoard, "AdventureScene is missing the preserved visual baseline fixture.");
            Assert.IsNotNull(comparisonPanel, "AdventureScene is missing the preserved comparison panel fixture.");
            Assert.IsTrue(battlefield.activeInHierarchy, "The formal Adventure route must show the functional battlefield.");
            Assert.AreEqual(6, battlefield.transform.childCount);
            Assert.Zero(battlefield.GetComponentsInChildren<Collider>(true).Length);
            GameObject backdrop = FindSceneObjectIncludingInactive("VisualBackdrop");
            Assert.IsNotNull(backdrop);
            Assert.IsFalse(backdrop.activeInHierarchy,
                "The legacy backdrop must remain hidden after formal runtime initialization.");
            Assert.IsFalse(comparisonBoard.activeInHierarchy,
                "The formal Adventure route must not show the visual baseline fixture.");
            Assert.IsFalse(comparisonPanel.activeInHierarchy,
                "The formal Adventure route must not show the comparison panel fixture.");

            Camera camera = Camera.main;
            Assert.IsNotNull(camera, "AdventureScene is missing its formal camera.");
            Assert.Less(Vector3.Distance(camera.transform.position, new Vector3(0f, 8f, -10f)), 0.001f);
            Assert.Less(Quaternion.Angle(camera.transform.rotation, Quaternion.Euler(38f, 0f, 0f)), 0.01f);
            Assert.AreEqual(6.2f, camera.orthographicSize, 0.001f);

            AssertMarkerGrounding("PlayerMarker", 0, 0, battlefield.transform, camera);
            AssertMarkerGrounding("EnemyMarker", 1, 0, battlefield.transform, camera);
        }

        private static void AssertMarkerGrounding(
            string markerName,
            int q,
            int r,
            Transform battlefield,
            Camera camera)
        {
            GameObject marker = FindSceneObjectIncludingInactive(markerName);
            Transform ground = battlefield.Find("GuanzhongHex_" + q + "_" + r);
            Assert.IsNotNull(marker, "Adventure combat did not create " + markerName + ".");
            Assert.IsNotNull(ground, "The functional battlefield is missing the marker ground cell.");
            Bounds markerBounds = CombinedRendererBounds(marker);
            Bounds groundBounds = CombinedRendererBounds(ground.gameObject);
            Vector3 expectedCenter = new Vector3(q + r * 0.5f, 0f, r * 0.8660254f + 1f);
            Assert.Less(Vector2.Distance(
                new Vector2(marker.transform.position.x, marker.transform.position.z),
                new Vector2(expectedCenter.x, expectedCenter.z)), 0.001f);
            Assert.Less(Vector2.Distance(
                new Vector2(ground.position.x, ground.position.z),
                new Vector2(expectedCenter.x, expectedCenter.z)), 0.001f);
            Assert.AreEqual(0.34f, ground.position.y, 0.001f);
            Assert.AreEqual(Vector3.one, ground.localScale);
            Assert.AreEqual(0.38f, marker.transform.position.y, 0.001f);
            Assert.AreEqual(0.45f, markerBounds.min.y, 0.001f,
                "Keep the technical marker's measured foot height; do not move it to hide the terrain gap.");
            float surfaceY = CenterSurfaceY(ground);
            Assert.AreEqual(0.340432711f, surfaceY, 0.001f,
                "The original normalized source has a natural center surface above its root datum.");
            float footGap = markerBounds.min.y - surfaceY;
            Assert.AreEqual(0.109567289f, footGap, 0.001f);

            Rect markerScreenRect = ScreenRectFromBounds(camera, markerBounds);
            Rect groundScreenRect = ScreenRectFromBounds(camera, groundBounds);
            Debug.Log("[GuanzhongBattlefieldProof] baselineResolution=1920x1080" +
                      " runtimeResolution=" + Screen.width + "x" + Screen.height +
                      " cameraPosition=" + camera.transform.position.ToString("F4") +
                      " cameraEuler=" + camera.transform.eulerAngles.ToString("F4") +
                      " ortho=" + camera.orthographicSize.ToString("F4") +
                      " marker=" + markerName +
                      " root=" + marker.transform.position.ToString("F4") +
                      " markerBoundsMin=" + markerBounds.min.ToString("F4") +
                      " markerBoundsMax=" + markerBounds.max.ToString("F4") +
                      " groundCell=(" + q + "," + r + ")" +
                      " groundBoundsMaxY=" + groundBounds.max.y.ToString("F6") +
                      " centerSurfaceY=" + surfaceY.ToString("F6") +
                      " footGap=" + footGap.ToString("F4") +
                      " markerScreenRect=" + RectText(markerScreenRect) +
                      " groundScreenRect=" + RectText(groundScreenRect));
        }


        private static float CenterSurfaceY(Transform ground)
        {
            float highest = float.NegativeInfinity;
            Vector3 center = ground.position;
            foreach (MeshFilter filter in ground.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                Assert.IsNotNull(mesh);
                Assert.IsTrue(mesh.isReadable, "Source mesh must remain readable for triangle evidence.");
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = filter.transform.TransformPoint(vertices[triangles[index]]);
                    Vector3 b = filter.transform.TransformPoint(vertices[triangles[index + 1]]);
                    Vector3 c = filter.transform.TransformPoint(vertices[triangles[index + 2]]);
                    float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(denominator) < 1e-10f) continue;
                    float u = ((b.z - c.z) * (center.x - c.x) + (c.x - b.x) * (center.z - c.z)) / denominator;
                    float v = ((c.z - a.z) * (center.x - c.x) + (a.x - c.x) * (center.z - c.z)) / denominator;
                    float w = 1f - u - v;
                    if (u >= 0f && v >= 0f && w >= 0f)
                        highest = Mathf.Max(highest, u * a.y + v * b.y + w * c.y);
                }
            }
            Assert.IsFalse(float.IsInfinity(highest), "No source triangle supports the logical cell center.");
            return highest;
        }

        private static IEnumerator CaptureFormalTerrainIfRequested()
        {
            string path = Environment.GetEnvironmentVariable("TZ_GZ_TERRAIN_CAPTURE_PATH");
            if (string.IsNullOrEmpty(path)) yield break;
            // Capture after the spawned scene has gone through its first normal render frame.
            yield return null;
            Camera camera = Camera.main;
            Renderer backdrop = FindSceneObjectIncludingInactive("VisualBackdrop").GetComponent<Renderer>();
            Material backdropMaterial = backdrop.sharedMaterial;
            var backdropProperties = new MaterialPropertyBlock();
            backdrop.GetPropertyBlock(backdropProperties);
            Debug.Log("[GuanzhongRenderProof] backdrop=" + backdropMaterial.name +
                      " active=" + backdrop.gameObject.activeInHierarchy +
                      " color=" + backdropMaterial.GetColor("_BaseColor") +
                      " texture=" + backdropMaterial.GetTexture("_BaseMap") +
                      " propertyBlockEmpty=" + backdropProperties.isEmpty +
                      " position=" + backdrop.transform.position +
                      " bounds=" + backdrop.bounds +
                      " cameraAspect=" + camera.aspect + " pixelRect=" + camera.pixelRect);
            WriteTerrainCapture(camera, path);

            // A temporary source-model reference, never a replacement for the formal provider.
            Transform source = FindSceneObjectIncludingInactive("VisualBaselineBoard")
                .transform.Find("FacingProbe_0/FuYuan_Model");
            Assert.IsNotNull(source, "The preserved FuYuan source model must exist for visual QA.");
            GameObject battlefield = FindSceneObjectIncludingInactive("GuanzhongBattlefield");
            Transform ground = battlefield.transform.Find("GuanzhongHex_0_0");
            GameObject reference = Object.Instantiate(source.gameObject, ground.position, source.rotation);
            reference.name = "FuYuan_TerrainVisualReference";
            reference.SetActive(true);
            Renderer[] markers = FindSceneObjectIncludingInactive("PlayerMarker")
                .GetComponentsInChildren<Renderer>();
            var markerVisibility = new bool[markers.Length];
            for (int index = 0; index < markers.Length; index++)
            {
                markerVisibility[index] = markers[index].enabled;
                markers[index].enabled = false;
            }
            Vector3 cameraPosition = camera.transform.position;
            float cameraSize = camera.orthographicSize;
            string referencePath = Path.Combine(Path.GetDirectoryName(path),
                Path.GetFileNameWithoutExtension(path) + "-fuyuan.png");
            try
            {
                yield return null;
                Assert.Less(Vector3.Distance(source.lossyScale, reference.transform.lossyScale), .00001f);
                Assert.Less(Quaternion.Angle(source.rotation, reference.transform.rotation), .001f);
                Renderer[] originalRenderers = source.GetComponentsInChildren<Renderer>(true);
                Renderer[] referenceRenderers = reference.GetComponentsInChildren<Renderer>(true);
                Assert.AreEqual(originalRenderers.Length, referenceRenderers.Length);
                for (int index = 0; index < referenceRenderers.Length; index++)
                    CollectionAssert.AreEqual(originalRenderers[index].sharedMaterials,
                        referenceRenderers[index].sharedMaterials);
                Bounds modelBounds = CombinedRendererBounds(reference);
                Bounds terrainBounds = CombinedRendererBounds(battlefield);
                float surfaceY = CenterSurfaceY(ground);
                Debug.Log("[GuanzhongFuYuanProof] source=VisualBaselineBoard/FacingProbe_0/FuYuan_Model" +
                          " model=" + reference.GetComponentInChildren<MeshFilter>().sharedMesh.name +
                          " material=" + referenceRenderers[0].sharedMaterial.name +
                          " root=" + reference.transform.position.ToString("F6") +
                          " scale=" + reference.transform.lossyScale.ToString("F6") +
                          " rotation=" + reference.transform.eulerAngles.ToString("F4") +
                          " modelBoundsMin=" + modelBounds.min.ToString("F6") +
                          " modelBoundsMax=" + modelBounds.max.ToString("F6") +
                          " terrainBoundsMin=" + terrainBounds.min.ToString("F6") +
                          " terrainBoundsMax=" + terrainBounds.max.ToString("F6") +
                          " centerSurfaceY=" + surfaceY.ToString("F6") +
                          " geometryMinGap=" + (modelBounds.min.y - surfaceY).ToString("F6") +
                          " technicalBaseIncluded=False formalProviderChanged=False");
                WriteTerrainCapture(camera, referencePath);

                // Additional inspection framing only; the formal camera is restored before gameplay resumes.
                Bounds inspectionBounds = terrainBounds;
                inspectionBounds.Encapsulate(modelBounds);
                camera.transform.position = inspectionBounds.center - camera.transform.forward * 14f;
                camera.orthographicSize = inspectionBounds.extents.magnitude * 1.1f;
                WriteTerrainCapture(camera, Path.Combine(Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path) + "-fuyuan-detail.png"));
            }
            finally
            {
                camera.transform.position = cameraPosition;
                camera.orthographicSize = cameraSize;
                for (int index = 0; index < markers.Length; index++)
                    markers[index].enabled = markerVisibility[index];
                reference.SetActive(false);
                Object.Destroy(reference);
            }
            yield return null;
            Assert.IsTrue(reference == null, "The visual QA model must be removed before formal combat resumes.");
            AssertFormalBattlefieldPresentation();
        }

        private static void WriteTerrainCapture(Camera camera, string path)
        {
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            target.Create();
            var request = new RenderPipeline.StandardRequest { destination = target };
            Assert.IsTrue(RenderPipeline.SupportsRenderRequest(camera, request));
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousCameraTarget = camera.targetTexture;
            try
            {
                // Keep the render and measured pixel rectangles on the same 16:9 projection.
                camera.targetTexture = target;
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                GameObject battlefield = FindSceneObjectIncludingInactive("GuanzhongBattlefield");
                Rect terrainRect = ScreenRectFromBounds(camera, CombinedRendererBounds(battlefield));
                Assert.GreaterOrEqual(terrainRect.xMin, 0);
                Assert.GreaterOrEqual(terrainRect.yMin, 0);
                Assert.LessOrEqual(terrainRect.xMax, 1920);
                Assert.LessOrEqual(terrainRect.yMax, 1080);
                GameObject reference = GameObject.Find("FuYuan_TerrainVisualReference");
                Debug.Log("[GuanzhongTerrainCapture] path=" + path +
                          " cameraPosition=" + camera.transform.position.ToString("F4") +
                          " cameraEuler=" + camera.transform.eulerAngles.ToString("F4") +
                          " ortho=" + camera.orthographicSize.ToString("F6") +
                          " aspect=" + camera.aspect.ToString("F6") +
                          " terrainScreenRect=" + RectText(terrainRect) +
                          " referenceScreenRect=" + (reference == null ? "none" :
                              RectText(ScreenRectFromBounds(camera, CombinedRendererBounds(reference)))) +
                          " overlayUIIncluded=False");
            }
            finally
            {
                camera.targetTexture = previousCameraTarget;
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static GameObject FindSceneObjectIncludingInactive(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                if (transform.name == name) return transform.gameObject;
            return null;
        }

        private static Bounds CombinedRendererBounds(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0, target.name + " must contain a visible renderer.");
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static Rect ScreenRectFromBounds(Camera camera, Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector2 screenMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 screenMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 point = camera.WorldToViewportPoint(new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z));
                Vector2 screenPoint = new Vector2(point.x * 1920f, point.y * 1080f);
                screenMin = Vector2.Min(screenMin, screenPoint);
                screenMax = Vector2.Max(screenMax, screenPoint);
            }
            return Rect.MinMaxRect(screenMin.x, screenMin.y, screenMax.x, screenMax.y);
        }

        private static string RectText(Rect rect) =>
            "(" + rect.xMin.ToString("F2") + "," + rect.yMin.ToString("F2") + ")-(" +
            rect.xMax.ToString("F2") + "," + rect.yMax.ToString("F2") + ")";

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

        private sealed class SequenceCombatRandomSource : ICombatResolutionRandomSource
        {
            private readonly float[] values;
            private int index;

            public SequenceCombatRandomSource(params float[] values)
            {
                this.values = values ?? Array.Empty<float>();
            }

            public int Count => index;

            public float NextPercent()
            {
                if (index >= values.Length)
                    throw new InvalidOperationException("The combat roll sequence was exhausted.");
                return values[index++];
            }
        }

        private sealed class RecordingCombatPresentationSink : ICombatPresentationSink
        {
            public void Present(CombatHudSnapshot snapshot) { }
            public void ClearLog() { }
            public void AppendLog(string message) { }
            public void Hide() { }
        }

        private sealed class AlwaysInRangeCombatSpatialQuery : ICombatSpatialQuery
        {
            public CombatRangeQueryResult QueryRange(
                HexCoord source,
                HexCoord target,
                int minimumRange,
                int maximumRange) => new CombatRangeQueryResult(true, string.Empty);

            public CombatMovementQueryResult QueryMovement(
                HexCoord source,
                HexCoord destination,
                int movementPoints,
                IReadOnlyCollection<HexCoord> occupied) =>
                new CombatMovementQueryResult(false, "movement_not_used", null, 0);

            public IReadOnlyDictionary<HexCoord, int> FindReachable(
                HexCoord source,
                int movementPoints,
                IReadOnlyCollection<HexCoord> occupied) => new Dictionary<HexCoord, int>();
        }
    }
}
