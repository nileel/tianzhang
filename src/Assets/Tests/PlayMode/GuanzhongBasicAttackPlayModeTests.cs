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
