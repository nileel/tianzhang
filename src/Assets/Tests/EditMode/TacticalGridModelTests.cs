using NUnit.Framework;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TianZhang.Adventure;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Core;
using TianZhang.Spatial;
using TianZhang.Editor;
using TianZhang.Entity;
using TianZhang.Game;
using TianZhang.Game.CharacterCreation;
using TianZhang.Tactical;
using TianZhang.Infrastructure.UnityContent;
using UnityEditor.SceneManagement;
using EnvironmentProfileData = TianZhang.Infrastructure.UnityContent.EnvironmentProfileAsset;

using TianZhang.Spatial;

namespace TianZhang.Tests
{
    internal static class SpatialQueryTestFixture
    {
        public static SpatialQueryBoard CreateOpenBoard(int radius = 6)
        {
            var cells = new Dictionary<HexCoord, SpatialCellRules>();
            for (int q = -radius; q <= radius; q++)
            {
                int minimumR = System.Math.Max(-radius, -q - radius);
                int maximumR = System.Math.Min(radius, -q + radius);
                for (int r = minimumR; r <= maximumR; r++)
                {
                    var coord = new HexCoord(q, r);
                    cells.Add(coord, new SpatialCellRules(0, false, false, false, 0));
                }
            }

            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            foreach (var from in cells.Keys)
            {
                foreach (var to in from.Neighbors())
                {
                    if (cells.ContainsKey(to))
                        edges.Add(new SpatialDirectedEdge(from, to), new SpatialEdgeRules(2, true, true));
                }
            }
            return new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16));
        }

        public static SpatialQueryBoard CreateCompressedLineBoard()
        {
            var origin = new HexCoord(0, 0);
            var east = new HexCoord(1, 0);
            var eastTwo = new HexCoord(2, 0);
            var cells = new Dictionary<HexCoord, SpatialCellRules>
            {
                [origin] = new SpatialCellRules(0, false, false, false, 0),
                [east] = new SpatialCellRules(0, false, false, false, 0),
                [eastTwo] = new SpatialCellRules(0, false, false, false, 0),
            };
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>
            {
                [new SpatialDirectedEdge(origin, east)] = new SpatialEdgeRules(1, true, true),
                [new SpatialDirectedEdge(east, eastTwo)] = new SpatialEdgeRules(1, true, true),
            };
            return new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16));
        }
    }

    public class TacticalGridModelTests
    {
        [Test]
        public void FromHexGridCopiesBlockersOccupantsAndDefaultHeight()
        {
            var source = new HexGrid();
            var center = new HexCoord(0, 0);
            var blocked = new HexCoord(1, 0);
            var occupied = new HexCoord(0, 1);

            source.SetBlocked(blocked, true);
            source.SetOccupied(occupied, 42);

            var model = TacticalGridModel.FromHexGrid(new[] { center, blocked, occupied }, source);

            Assert.AreEqual(3, model.Count);
            Assert.IsFalse(model.GetTile(center).BlocksGroundMove);
            Assert.IsTrue(model.GetTile(blocked).BlocksGroundMove);
            Assert.IsTrue(model.GetTile(blocked).BlocksLanding);
            Assert.AreEqual(0, model.GetTile(blocked).HeightLevel);
            Assert.AreEqual(42, model.GetTile(occupied).OccupiedUnitId);
            Assert.IsTrue(model.IsOccupied(occupied));
        }

        [Test]
        public void ToHexGridPreservesGroundBlockersAndOccupants()
        {
            var blocked = new HexCoord(1, 0);
            var occupied = new HexCoord(0, 1);
            var model = new TacticalGridModel();

            model.SetTile(new TacticalTileData(new HexCoord(0, 0)));
            model.SetTile(new TacticalTileData(blocked)
            {
                BlocksGroundMove = true,
            });
            model.SetTile(new TacticalTileData(occupied)
            {
                OccupiedUnitId = 7,
            });

            var grid = model.ToHexGrid();

            Assert.IsFalse(grid.IsBlocked(new HexCoord(0, 0)));
            Assert.IsTrue(grid.IsBlocked(blocked));
            Assert.AreEqual(7, grid.GetOccupant(occupied));
            Assert.IsTrue(grid.IsOccupied(occupied));
        }

        [Test]
        public void EnvironmentProfileProjectionProvidesOnlyConfiguredRuntimeInputs()
        {
            var profile = CreateEnvironmentProfile();
            try
            {
                Assert.IsTrue(EnvironmentProfileRuntime.TryCreate(profile, out var environment, out var reason), reason);
                Assert.AreEqual("runtime_fixture", environment.ProfileId);
                Assert.IsTrue(environment.IsSurfacePrototypeConfigured("surface_grassland", out var surfaceReason));
                Assert.AreEqual(EnvironmentRuntimeReasons.Ok, surfaceReason);
                Assert.IsFalse(environment.IsSurfacePrototypeConfigured("surface_default", out surfaceReason));
                Assert.AreEqual(EnvironmentRuntimeReasons.SurfacePrototypeNotConfigured, surfaceReason);
                Assert.IsTrue(environment.TryResolvePhenomenonPair(
                    EnvironmentPhenomenonChannel.Airflow,
                    "gust",
                    "wind",
                    out var pairingResult,
                    out var pairingReason));
                Assert.AreEqual("gust", pairingResult);
                Assert.AreEqual(EnvironmentRuntimeReasons.Ok, pairingReason);
                Assert.IsFalse(environment.TryResolvePhenomenonPair(
                    EnvironmentPhenomenonChannel.Airflow,
                    "gust",
                    "breeze",
                    out _,
                    out pairingReason));
                Assert.AreEqual(EnvironmentRuntimeReasons.PhenomenonPairNotConfigured, pairingReason);
                Assert.IsTrue(environment.IsElementRelationConfigured("element_wood", out var elementReason));
                Assert.AreEqual(EnvironmentRuntimeReasons.Ok, elementReason);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void InvalidEnvironmentProfileFailsWithoutBindingGridState()
        {
            var valid = CreateEnvironmentProfile();
            var invalid = CreateEnvironmentProfile();
            try
            {
                Assert.IsTrue(EnvironmentProfileRuntime.TryCreate(valid, out _, out var validReason), validReason);
                invalid.surfacePrototypeRefs = new[] { "surface_grassland", "surface_grassland" };

                Assert.IsFalse(EnvironmentProfileRuntime.TryCreate(invalid, out _, out var invalidReason));
                Assert.AreEqual(EnvironmentRuntimeReasons.SurfacePrototypesNotConfigured, invalidReason);
            }
            finally
            {
                Object.DestroyImmediate(invalid);
                Object.DestroyImmediate(valid);
            }
        }

        private static EnvironmentProfileAsset CreateEnvironmentProfile()
        {
            var profile = ScriptableObject.CreateInstance<EnvironmentProfileAsset>();
            profile.profileId = "runtime_fixture";
            profile.unitsPerRange = 2;
            profile.maxQueryRange = 16;
            profile.directedEdges = new[]
            {
                new EnvironmentDirectedEdge
                {
                    fromQ = 0,
                    fromR = 0,
                    toQ = 1,
                    toR = 0,
                    metricDistanceUnits = 2,
                    allowsMovement = true,
                    allowsEffects = true,
                },
            };
            profile.surfacePrototypeRefs = new[] { "surface_grassland", "surface_loess" };
            profile.phenomenonChannels = new[]
            {
                new EnvironmentPhenomenonChannelData
                {
                    channel = EnvironmentPhenomenonChannel.Airflow,
                    phenomenonTypeRefs = new[] { "wind", "gust", "breeze" },
                },
                new EnvironmentPhenomenonChannelData
                {
                    channel = EnvironmentPhenomenonChannel.Visibility,
                    phenomenonTypeRefs = new[] { "mist" },
                },
                new EnvironmentPhenomenonChannelData
                {
                    channel = EnvironmentPhenomenonChannel.Temperature,
                    phenomenonTypeRefs = new[] { "heat" },
                },
                new EnvironmentPhenomenonChannelData
                {
                    channel = EnvironmentPhenomenonChannel.Precipitation,
                    phenomenonTypeRefs = new[] { "rain" },
                },
                new EnvironmentPhenomenonChannelData
                {
                    channel = EnvironmentPhenomenonChannel.SuspendedHazard,
                    phenomenonTypeRefs = new[] { "ash" },
                },
                new EnvironmentPhenomenonChannelData
                {
                    channel = EnvironmentPhenomenonChannel.CloudDischarge,
                    phenomenonTypeRefs = new[] { "storm" },
                },
            };
            profile.phenomenonPairs = new[]
            {
                new EnvironmentPhenomenonPairing
                {
                    channel = EnvironmentPhenomenonChannel.Airflow,
                    firstTypeRef = "wind",
                    secondTypeRef = "gust",
                    resultTypeRef = "gust",
                },
            };
            profile.elementRelationRefs = new[]
            {
                "element_wood",
                "element_fire",
                "element_earth",
                "element_metal",
                "element_water",
            };
            return profile;
        }
    }

    public class FormalEncounterResultTests
    {
        private readonly List<Object> temporaryObjects = new List<Object>();

        [TearDown]
        public void DestroyTemporaryObjects()
        {
            foreach (Object value in temporaryObjects)
                Object.DestroyImmediate(value);
            temporaryObjects.Clear();
        }

        [Test]
        public void ProductionCatalogResolvesStableEnemyAndExplicitMeleeAi()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                "Assets/Data/ContentCatalog/ContentCatalog.asset");

            Assert.IsTrue(
                FormalEncounterRules.TryResolveGuanzhongEnemy(
                    catalog,
                    out EnemyData enemy,
                    out ICombatActionPolicy aiPolicy,
                    out string reason),
                reason);
            Assert.AreEqual(FormalEncounterRules.ShijiahouEnemyId, enemy.enemyId);
            Assert.IsNotNull(enemy.combatTemplate);
            Assert.IsInstanceOf<LegalActionAI>(aiPolicy);
        }

        [Test]
        public void ConfigurationRejectsMissingCatalogAndUnknownAiBeforeCombat()
        {
            Assert.IsFalse(
                FormalEncounterRules.TryResolveGuanzhongEnemy(
                    null,
                    out _,
                    out _,
                    out string reason));
            Assert.AreEqual(FormalEncounterRules.CatalogMissingReason, reason);

            var emptyCatalog = Track(ScriptableObject.CreateInstance<ContentCatalogData>());
            emptyCatalog.ReplaceEntries(null, null, null, null);
            AssertRejected(emptyCatalog, FormalEncounterRules.EnemyMissingReason);

            var fixture = CreateFixture();
            fixture.Enemy.aiProfileId = "ai_unknown";

            Assert.IsFalse(
                FormalEncounterRules.TryResolveGuanzhongEnemy(
                    fixture.Catalog,
                    out _,
                    out _,
                    out reason));
            Assert.AreEqual(EnemyAIProfileResolver.UnknownProfileReason, reason);
        }

        [Test]
        public void ConfigurationRejectsInvalidScopeTemplateDropsAndItemsBeforeCombat()
        {
            var fixture = CreateFixture();
            fixture.Enemy.contentScope = "other_scope";
            AssertRejected(fixture.Catalog, FormalEncounterRules.EnemyScopeInvalidReason);

            fixture = CreateFixture();
            fixture.Enemy.combatTemplate = null;
            AssertRejected(fixture.Catalog, FormalEncounterRules.CombatTemplateMissingReason);

            fixture = CreateFixture();
            fixture.Enemy.dropEntries = System.Array.Empty<EnemyDropEntry>();
            AssertRejected(fixture.Catalog, FormalEncounterRules.DropsMissingReason);

            fixture = CreateFixture();
            fixture.Enemy.dropEntries[0].itemId = "item_missing";
            AssertRejected(
                fixture.Catalog,
                FormalEncounterRules.DropItemMissingReason + ":item_missing");

            fixture = CreateFixture();
            Assert.IsTrue(fixture.Catalog.TryGetItem("item_shijia_piece", out ItemData item));
            item.contentScope = "reserved";
            AssertRejected(
                fixture.Catalog,
                FormalEncounterRules.DropItemNotProductionReason + ":item_shijia_piece");
        }

        [Test]
        public void VictoryRollsDropsIndependentlyWithStrictLessThanComparison()
        {
            var fixture = CreateFixture();

            Assert.IsTrue(
                FormalEncounterResult.TryCreate(
                    fixture.Catalog,
                    fixture.Enemy,
                    FormalEncounterRules.GuanzhongWildAdventureId,
                    CombatSessionOutcome.Victory,
                    new SequenceRandomSource(99, 49),
                    out FormalEncounterResult bothDrops,
                    out string reason),
                reason);
            Assert.AreEqual(FormalEncounterRules.ShijiahouEnemyId, bothDrops.EnemyId);
            Assert.AreEqual(2, bothDrops.DropGrants.Count);

            Assert.IsTrue(
                FormalEncounterResult.TryCreate(
                    fixture.Catalog,
                    fixture.Enemy,
                    FormalEncounterRules.GuanzhongWildAdventureId,
                    CombatSessionOutcome.Victory,
                    new SequenceRandomSource(0, 50),
                    out FormalEncounterResult thresholdResult,
                    out reason),
                reason);
            Assert.AreEqual(1, thresholdResult.DropGrants.Count);
            Assert.AreEqual("item_shijia_piece", thresholdResult.DropGrants[0].ItemId);
        }

        [Test]
        public void ResultRejectsDifferentEnemyIdentityAndOutOfRangeRandomValue()
        {
            var fixture = CreateFixture();
            var differentEnemy = Track(ScriptableObject.CreateInstance<EnemyData>());

            Assert.IsFalse(
                FormalEncounterResult.TryCreate(
                    fixture.Catalog,
                    differentEnemy,
                    FormalEncounterRules.GuanzhongWildAdventureId,
                    CombatSessionOutcome.Victory,
                    new SequenceRandomSource(0, 0),
                    out _,
                    out string reason));
            Assert.AreEqual(FormalEncounterRules.EnemyIdentityMismatchReason, reason);

            Assert.IsFalse(
                FormalEncounterResult.TryCreate(
                    fixture.Catalog,
                    fixture.Enemy,
                    FormalEncounterRules.GuanzhongWildAdventureId,
                    CombatSessionOutcome.Victory,
                    new SequenceRandomSource(100),
                    out _,
                    out reason));
            Assert.AreEqual(FormalEncounterRules.RandomValueInvalidReason, reason);
        }

        private FormalFixture CreateFixture()
        {
            var template = Track(ScriptableObject.CreateInstance<CharacterData>());
            template.charName = "石甲兽";

            var guaranteedItem = CreateItem("item_shijia_piece");
            var chanceItem = CreateItem("item_lingshi_low");
            var enemy = Track(ScriptableObject.CreateInstance<EnemyData>());
            enemy.enemyId = FormalEncounterRules.ShijiahouEnemyId;
            enemy.contentScope = FormalEncounterRules.GuanzhongContentScope;
            enemy.aiProfileId = EnemyAIProfileResolver.MeleeProfileId;
            enemy.combatTemplate = template;
            enemy.dropEntries = new[]
            {
                new EnemyDropEntry
                {
                    itemId = guaranteedItem.itemId,
                    dropChancePercent = 100,
                    quantity = 1,
                },
                new EnemyDropEntry
                {
                    itemId = chanceItem.itemId,
                    dropChancePercent = 50,
                    quantity = 1,
                },
            };

            var catalog = Track(ScriptableObject.CreateInstance<ContentCatalogData>());
            catalog.ReplaceEntries(
                null,
                new[] { enemy },
                new[] { guaranteedItem, chanceItem },
                null);
            return new FormalFixture(catalog, enemy);
        }

        private ItemData CreateItem(string itemId)
        {
            var item = Track(ScriptableObject.CreateInstance<ItemData>());
            item.itemId = itemId;
            item.contentScope = InventoryGrantService.ProductionContentScope;
            item.maxStack = 99;
            return item;
        }

        private static void AssertRejected(ContentCatalogData catalog, string expectedReason)
        {
            Assert.IsFalse(
                FormalEncounterRules.TryResolveGuanzhongEnemy(
                    catalog,
                    out _,
                    out _,
                    out string reason));
            Assert.AreEqual(expectedReason, reason);
        }

        private T Track<T>(T value)
            where T : Object
        {
            temporaryObjects.Add(value);
            return value;
        }

        private sealed class FormalFixture
        {
            public ContentCatalogData Catalog { get; }
            public EnemyData Enemy { get; }

            public FormalFixture(ContentCatalogData catalog, EnemyData enemy)
            {
                Catalog = catalog;
                Enemy = enemy;
            }
        }

        internal sealed class SequenceRandomSource : IFormalEncounterRandomSource
        {
            private readonly Queue<int> values;

            public SequenceRandomSource(params int[] values)
            {
                this.values = new Queue<int>(values);
            }

            public int NextPercent()
            {
                return values.Dequeue();
            }
        }
    }

    public class AdventureSceneControllerTests
    {
        [Test]
        public void GuanzhongWildInitializationSpawnsTheFormalShijiahouMarker()
        {
            DestroyExistingSceneFlowAndSession();
            SceneBuilder.BuildAdventureScene();
            EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity", OpenSceneMode.Single);

            var sessionGo = new GameObject("GameSessionTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetAdventureId("guanzhong_wild");

                var controller = Object.FindFirstObjectByType<AdventureSceneController>();
                var exploration = Object.FindFirstObjectByType<TianZhang.Map.ExplorationController>();
                Assert.IsNotNull(controller);
                Assert.IsNotNull(exploration);

                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");
                var initMethod = typeof(TianZhang.Map.ExplorationController).GetMethod(
                    "InitExploration",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(initMethod);

                var initialization = (System.Collections.IEnumerator)initMethod.Invoke(exploration, null);
                while (initialization.MoveNext())
                {
                }

                Assert.IsNotNull(GameObject.Find("石甲兽"), "The formal encounter must spawn its enemy marker.");
                var snapshot = GetPrivateField<SpatialQuerySnapshot>(exploration, "spatialQuerySnapshot");
                Assert.IsNotNull(snapshot);
                Assert.AreEqual(2, snapshot.Board.UnitsPerRange);
                var enemies = (System.Collections.IList)exploration.GetType()
                    .GetField("enemies", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .GetValue(exploration);
                var firstEnemy = enemies[0];
                var formalEnemyData = (EnemyData)firstEnemy.GetType()
                    .GetField("enemyData")
                    .GetValue(firstEnemy);
                var combatTemplate = (CharacterData)firstEnemy.GetType()
                    .GetField("data")
                    .GetValue(firstEnemy);
                var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                    "Assets/Data/ContentCatalog/ContentCatalog.asset");
                Assert.IsTrue(catalog.TryGetEnemy(FormalEncounterRules.ShijiahouEnemyId, out var expectedEnemy));
                Assert.AreSame(expectedEnemy, formalEnemyData);
                Assert.AreSame(expectedEnemy.combatTemplate, combatTemplate);
            }
            finally
            {
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void GuanzhongWildConfiguredAdjacentEnemyBeginsCombat()
        {
            DestroyExistingSceneFlowAndSession();
            SceneBuilder.BuildAdventureScene();
            EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity", OpenSceneMode.Single);

            var sessionGo = new GameObject("GameSessionTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetAdventureId("guanzhong_wild");

                var controller = Object.FindFirstObjectByType<AdventureSceneController>();
                var exploration = Object.FindFirstObjectByType<TianZhang.Map.ExplorationController>();
                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");
                var initialization = (System.Collections.IEnumerator)typeof(TianZhang.Map.ExplorationController)
                    .GetMethod("InitExploration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(exploration, null);
                while (initialization.MoveNext())
                {
                }

                var player = GetPrivateField<Character>(exploration, "player");
                var enemies = (System.Collections.IList)exploration.GetType()
                    .GetField("enemies", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .GetValue(exploration);
                var firstEnemy = enemies[0];
                var enemy = (Character)firstEnemy.GetType()
                    .GetField("character", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                    .GetValue(firstEnemy);
                var spatialSnapshot = GetPrivateField<SpatialQuerySnapshot>(exploration, "spatialQuerySnapshot");
                player.Position = new HexCoord(0, 0);
                enemy.Position = new HexCoord(1, 0);
                var range = spatialSnapshot.Board.QueryRangeEntry(
                    player.Position,
                    enemy.Position,
                    1,
                    1,
                    SpatialQueryKind.Attack,
                    true);
                Assert.IsTrue(range.IsInRange, range.Reason);

                controller.BeginEncounter();

                Assert.AreEqual(AdventureSceneState.Combat, controller.CurrentState);
            }
            finally
            {
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void GuanzhongWildDisplaysItsNameAndConfiguresOnlyTheFormalShijiahou()
        {
            DestroyExistingSceneFlowAndSession();
            SceneBuilder.BuildAdventureScene();
            EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity", OpenSceneMode.Single);

            var sessionGo = new GameObject("GameSessionTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetAdventureId("guanzhong_wild");
                session.SetReturnTarget(SceneReturnTarget.Settlement("guanzhong_city"));

                var controller = Object.FindFirstObjectByType<AdventureSceneController>();
                var exploration = Object.FindFirstObjectByType<TianZhang.Map.ExplorationController>();
                var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                    "Assets/Data/ContentCatalog/ContentCatalog.asset");
                Assert.IsNotNull(controller);
                Assert.IsNotNull(exploration);
                Assert.IsTrue(catalog.TryGetEnemy(FormalEncounterRules.ShijiahouEnemyId, out var expectedEnemy));

                exploration.enemyCount = 3;
                exploration.enemyTemplates = System.Array.Empty<CharacterData>();
                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");
                InvokeStart(controller);

                StringAssert.Contains("关中野外", GameObject.Find("AdventureIdText")?.GetComponent<Text>()?.text);
                Assert.AreEqual(1, exploration.enemyCount);
                CollectionAssert.IsEmpty(exploration.enemyTemplates);
                Assert.AreSame(
                    expectedEnemy,
                    GetPrivateField<EnemyData>(exploration, "formalEncounterEnemy"));
            }
            finally
            {
                DestroyAdventureUi();
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void GuanzhongWildWithoutFormalCatalogBlocksEncounterButKeepsReturnExit()
        {
            DestroyExistingSceneFlowAndSession();
            SceneBuilder.BuildAdventureScene();
            EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity", OpenSceneMode.Single);

            var sessionGo = new GameObject("GameSessionTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetAdventureId("guanzhong_wild");
                session.SetReturnTarget(SceneReturnTarget.Settlement("guanzhong_city"));

                var controller = Object.FindFirstObjectByType<AdventureSceneController>();
                var exploration = Object.FindFirstObjectByType<TianZhang.Map.ExplorationController>();
                Assert.IsNotNull(controller);
                Assert.IsNotNull(exploration);

                var serializedController = new SerializedObject(controller);
                var catalogProperty = serializedController.FindProperty("contentCatalog");
                Assert.IsNotNull(catalogProperty);
                catalogProperty.objectReferenceValue = null;
                serializedController.ApplyModifiedPropertiesWithoutUndo();

                exploration.enabled = true;
                LogAssert.Expect(LogType.Error, new Regex(FormalEncounterRules.CatalogMissingReason));
                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");
                InvokeStart(controller);

                Assert.IsFalse(exploration.enabled);
                Assert.AreEqual(AdventureSceneState.Loading, controller.CurrentState);
                Assert.IsNotNull(GameObject.Find("ReturnToSourceButton"));
            }
            finally
            {
                DestroyAdventureUi();
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void FormalVictoryGrantsStructuredDropsOnlyOnce()
        {
            DestroyExistingSceneFlowAndSession();
            var controllerGo = new GameObject("FormalAdventureControllerTest");
            var explorationGo = new GameObject("FormalExplorationControllerTest");
            var sessionGo = new GameObject("FormalGameSessionTest");
            try
            {
                var controller = controllerGo.AddComponent<AdventureSceneController>();
                explorationGo.AddComponent<TianZhang.Map.ExplorationController>();
                var session = sessionGo.AddComponent<GameSession>();
                session.SetAdventureId(FormalEncounterRules.GuanzhongWildAdventureId);
                session.SetReturnTarget(SceneReturnTarget.Settlement("guanzhong_city"));
                var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                    "Assets/Data/ContentCatalog/ContentCatalog.asset");
                Assert.IsTrue(catalog.TryGetEnemy(FormalEncounterRules.ShijiahouEnemyId, out var enemy));
                controller.SetContentCatalog(catalog);
                controller.SetGuanzhongWildEnvironmentProfile(
                    AssetDatabase.LoadAssetAtPath<EnvironmentProfileData>(
                        "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset"));
                controller.SetEncounterRandomSource(
                    new FormalEncounterResultTests.SequenceRandomSource(99, 49));
                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");

                controller.ResolveEncounterAndReturn(CombatSessionOutcome.Victory, enemy);

                Assert.AreEqual(AdventureSceneState.Returning, controller.CurrentState);
                Assert.AreEqual(FormalEncounterRules.ShijiahouEnemyId, controller.LastFormalEncounterResult.EnemyId);
                Assert.AreEqual("guanzhong_city", session.LastReturnTarget.SettlementId);
                Assert.IsTrue(session.InventoryStates.TryGet("item_shijia_piece", out var piece));
                Assert.AreEqual(1, piece.Quantity);
                Assert.IsTrue(session.InventoryStates.TryGet("item_lingshi_low", out var lingshi));
                Assert.AreEqual(1, lingshi.Quantity);

                LogAssert.Expect(LogType.Error, new Regex(FormalEncounterRules.AlreadyConsumedReason));
                controller.ResolveEncounterAndReturn(CombatSessionOutcome.Victory, enemy);

                Assert.AreEqual(FormalEncounterRules.AlreadyConsumedReason, controller.EncounterResolutionFailureReason);
                Assert.AreEqual(2, session.InventoryStates.Count);
                Assert.IsTrue(session.InventoryStates.TryGet("item_shijia_piece", out piece));
                Assert.AreEqual(1, piece.Quantity);
                Assert.IsTrue(session.InventoryStates.TryGet("item_lingshi_low", out lingshi));
                Assert.AreEqual(1, lingshi.Quantity);
            }
            finally
            {
                Object.DestroyImmediate(sessionGo);
                Object.DestroyImmediate(explorationGo);
                Object.DestroyImmediate(controllerGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void FormalVictoryInventoryFailureIsAtomicAndObservable()
        {
            DestroyExistingSceneFlowAndSession();
            var controllerGo = new GameObject("FormalAdventureControllerTest");
            var explorationGo = new GameObject("FormalExplorationControllerTest");
            var sessionGo = new GameObject("FormalGameSessionTest");
            try
            {
                var controller = controllerGo.AddComponent<AdventureSceneController>();
                explorationGo.AddComponent<TianZhang.Map.ExplorationController>();
                var session = sessionGo.AddComponent<GameSession>();
                session.SetAdventureId(FormalEncounterRules.GuanzhongWildAdventureId);
                session.SetReturnTarget(SceneReturnTarget.Settlement("guanzhong_city"));
                var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                    "Assets/Data/ContentCatalog/ContentCatalog.asset");
                Assert.IsTrue(catalog.TryGetEnemy(FormalEncounterRules.ShijiahouEnemyId, out var enemy));
                session.InventoryStates.Set(
                    new InventoryStateSnapshot(
                        "item_shijia_piece",
                        99,
                        new StateStepSnapshot(false, false, false, false, false, false, false)));
                controller.SetContentCatalog(catalog);
                controller.SetGuanzhongWildEnvironmentProfile(
                    AssetDatabase.LoadAssetAtPath<EnvironmentProfileData>(
                        "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset"));
                controller.SetEncounterRandomSource(
                    new FormalEncounterResultTests.SequenceRandomSource(0, 0));
                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");

                LogAssert.Expect(LogType.Error, new Regex("StackLimitExceeded"));
                controller.ResolveEncounterAndReturn(CombatSessionOutcome.Victory, enemy);

                StringAssert.Contains("StackLimitExceeded", controller.EncounterResolutionFailureReason);
                Assert.AreEqual(AdventureSceneState.Returning, controller.CurrentState);
                Assert.AreEqual("guanzhong_city", session.LastReturnTarget.SettlementId);
                Assert.IsTrue(session.InventoryStates.TryGet("item_shijia_piece", out var piece));
                Assert.AreEqual(99, piece.Quantity);
                Assert.IsFalse(session.InventoryStates.TryGet("item_lingshi_low", out _));
            }
            finally
            {
                Object.DestroyImmediate(sessionGo);
                Object.DestroyImmediate(explorationGo);
                Object.DestroyImmediate(controllerGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void FormalVictoryRegistersAcceptedBountyProgressOnlyOnce()
        {
            DestroyExistingSceneFlowAndSession();
            var controllerGo = new GameObject("FormalBountyAdventureTest");
            var explorationGo = new GameObject("FormalBountyExplorationTest");
            var sessionGo = new GameObject("FormalBountySessionTest");
            try
            {
                var controller = controllerGo.AddComponent<AdventureSceneController>();
                explorationGo.AddComponent<TianZhang.Map.ExplorationController>();
                var session = sessionGo.AddComponent<GameSession>();
                session.SetSettlementId("guanzhong_city");
                session.SetAdventureId(FormalEncounterRules.GuanzhongWildAdventureId);
                session.SetReturnTarget(SceneReturnTarget.Settlement("guanzhong_city"));
                var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                    "Assets/Data/ContentCatalog/ContentCatalog.asset");
                Assert.IsTrue(catalog.TryGetEnemy(FormalEncounterRules.ShijiahouEnemyId, out var enemy));
                BountyActionResult accept = session.AcceptBounty(catalog, "bounty_guanzhong_shijiahou");
                Assert.IsTrue(accept.Succeeded, accept.FailureReason);
                controller.SetContentCatalog(catalog);
                controller.SetGuanzhongWildEnvironmentProfile(
                    AssetDatabase.LoadAssetAtPath<EnvironmentProfileData>(
                        "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset"));
                controller.SetEncounterRandomSource(
                    new FormalEncounterResultTests.SequenceRandomSource(99, 49));
                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");

                controller.ResolveEncounterAndReturn(CombatSessionOutcome.Victory, enemy);

                BountyStateSnapshot state = session.GetBountyState("bounty_guanzhong_shijiahou");
                Assert.AreEqual(BountyStatus.ObjectiveCompleted, state.Status);
                Assert.AreEqual(1, state.Progress);

                LogAssert.Expect(LogType.Error, new Regex(FormalEncounterRules.AlreadyConsumedReason));
                controller.ResolveEncounterAndReturn(CombatSessionOutcome.Victory, enemy);

                Assert.AreEqual(FormalEncounterRules.AlreadyConsumedReason, controller.EncounterResolutionFailureReason);
                state = session.GetBountyState("bounty_guanzhong_shijiahou");
                Assert.AreEqual(BountyStatus.ObjectiveCompleted, state.Status);
                Assert.AreEqual(1, state.Progress);
            }
            finally
            {
                Object.DestroyImmediate(sessionGo);
                Object.DestroyImmediate(explorationGo);
                Object.DestroyImmediate(controllerGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void FormalDefeatDoesNotRegisterBountyProgress()
        {
            DestroyExistingSceneFlowAndSession();
            var controllerGo = new GameObject("FormalBountyDefeatTest");
            var explorationGo = new GameObject("FormalBountyDefeatExplorationTest");
            var sessionGo = new GameObject("FormalBountyDefeatSessionTest");
            try
            {
                var controller = controllerGo.AddComponent<AdventureSceneController>();
                explorationGo.AddComponent<TianZhang.Map.ExplorationController>();
                var session = sessionGo.AddComponent<GameSession>();
                session.SetSettlementId("guanzhong_city");
                session.SetAdventureId(FormalEncounterRules.GuanzhongWildAdventureId);
                session.SetReturnTarget(SceneReturnTarget.Settlement("guanzhong_city"));
                var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                    "Assets/Data/ContentCatalog/ContentCatalog.asset");
                Assert.IsTrue(catalog.TryGetEnemy(FormalEncounterRules.ShijiahouEnemyId, out var enemy));
                BountyActionResult accept = session.AcceptBounty(catalog, "bounty_guanzhong_shijiahou");
                Assert.IsTrue(accept.Succeeded, accept.FailureReason);
                controller.SetContentCatalog(catalog);
                controller.SetGuanzhongWildEnvironmentProfile(
                    AssetDatabase.LoadAssetAtPath<EnvironmentProfileData>(
                        "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset"));
                controller.SetEncounterRandomSource(
                    new FormalEncounterResultTests.SequenceRandomSource(0, 0));
                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");

                controller.ResolveEncounterAndReturn(CombatSessionOutcome.Defeat, enemy);

                BountyStateSnapshot state = session.GetBountyState("bounty_guanzhong_shijiahou");
                Assert.AreEqual(BountyStatus.Accepted, state.Status);
                Assert.AreEqual(0, state.Progress);
            }
            finally
            {
                Object.DestroyImmediate(sessionGo);
                Object.DestroyImmediate(explorationGo);
                Object.DestroyImmediate(controllerGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void NonGuanzhongAdventureDoesNotConsumeGuanzhongBinding()
        {
            DestroyExistingSceneFlowAndSession();
            SceneBuilder.BuildAdventureScene();
            EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity", OpenSceneMode.Single);

            var sessionGo = new GameObject("GameSessionTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetAdventureId("taiyi_trial");

                var controller = Object.FindFirstObjectByType<AdventureSceneController>();
                var exploration = Object.FindFirstObjectByType<TianZhang.Map.ExplorationController>();
                var originalEnemyCount = exploration.enemyCount;
                var originalEnemyTemplates = exploration.enemyTemplates;

                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");

                Assert.AreEqual(originalEnemyCount, exploration.enemyCount);
                Assert.AreSame(originalEnemyTemplates, exploration.enemyTemplates);
            }
            finally
            {
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void EncounterStateMovesBetweenExplorationCombatAndReturning()
        {
            var go = new GameObject("AdventureSceneControllerTests");
            try
            {
                var controller = go.AddComponent<AdventureSceneController>();

                Assert.AreEqual(AdventureSceneState.Loading, controller.CurrentState);
                controller.MarkExplorationReady();
                Assert.AreEqual(AdventureSceneState.Exploration, controller.CurrentState);
                controller.BeginEncounter();
                Assert.AreEqual(AdventureSceneState.Combat, controller.CurrentState);
                controller.CompleteEncounter();
                Assert.AreEqual(AdventureSceneState.Exploration, controller.CurrentState);
                controller.MarkReturning();
                Assert.AreEqual(AdventureSceneState.Returning, controller.CurrentState);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [TestCase(CombatSessionOutcome.Victory)]
        [TestCase(CombatSessionOutcome.Defeat)]
        public void CompletedEncounterRecordsOutcomeAndReturnsToSource(CombatSessionOutcome outcome)
        {
            var go = new GameObject("AdventureSceneControllerTests");
            try
            {
                var controller = go.AddComponent<AdventureSceneController>();
                controller.MarkExplorationReady();
                controller.BeginEncounter();

                controller.ResolveEncounterAndReturn(outcome);

                Assert.AreEqual(AdventureSceneState.Returning, controller.CurrentState);
                Assert.AreEqual(outcome, controller.LastEncounterOutcome);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NewGameSessionClearsPreviousSceneContext()
        {
            DestroyExistingSceneFlowAndSession();
            var sessionGo = new GameObject("GameSessionTest");
            var oldProfile = ScriptableObject.CreateInstance<CharacterData>();
            var newProfile = ScriptableObject.CreateInstance<CharacterData>();
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                oldProfile.charName = "旧档角色";
                newProfile.charName = "新档角色";

                session.SetPlayerProfile(oldProfile);
                session.SetWorldNode("old_node");
                session.SetSettlementId("old_settlement");
                session.SetAdventureId("old_adventure");
                session.SetReturnTarget(SceneReturnTarget.Settlement("old_settlement"));

                session.BeginNewGame(newProfile, "jiangzuo_hub");

                Assert.AreSame(newProfile, session.PlayerProfile);
                Assert.AreEqual("jiangzuo_hub", session.CurrentWorldNodeId);
                Assert.IsNull(session.CurrentSettlementId);
                Assert.IsNull(session.CurrentAdventureId);
                Assert.IsTrue(string.IsNullOrEmpty(session.LastReturnTarget.SceneName));
            }
            finally
            {
                Object.DestroyImmediate(newProfile);
                Object.DestroyImmediate(oldProfile);
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void ExplorationPlayerUsesGameSessionProfileWhenAvailable()
        {
            DestroyExistingSceneFlowAndSession();
            var sessionGo = new GameObject("GameSessionTest");
            var controllerGo = new GameObject("ExplorationControllerTest");
            var profile = ScriptableObject.CreateInstance<CharacterData>();
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                profile.charName = "玉清崖";
                profile.gongFaName = "苦行剑典";
                profile.rootBone = 16;
                profile.physique = 14;
                profile.spirit = 8;
                profile.mind = 14;
                profile.reaction = 20;
                profile.talent = 10;
                profile.realmMultiplier = 1.5f;
                profile.equippedSpells = new[] { "引雷诀", "苦行剑式" };
                profile.availableSpells = new[] { "引雷诀", "苦行剑式", "剑罡护体" };
                session.BeginNewGame(profile, "jiangzuo_hub");

                var controller = controllerGo.AddComponent<TianZhang.Map.ExplorationController>();
                var method = typeof(TianZhang.Map.ExplorationController).GetMethod(
                    "CreatePlayer",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                var player = (Character)method.Invoke(controller, new object[] { new HexCoord(0, 0) });

                Assert.AreEqual("玉清崖", player.Name);
                Assert.AreEqual("苦行剑典", player.GongFaName);
                Assert.AreEqual(16, player.RootBone);
                Assert.AreEqual(20, player.Reaction);
                CollectionAssert.AreEqual(new[] { "引雷诀", "苦行剑式" }, player.EquippedSpellIds);
                CollectionAssert.AreEqual(new[] { "引雷诀", "苦行剑式", "剑罡护体" }, player.AvailableSpells);
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(controllerGo);
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        private static void DestroyExistingSceneFlowAndSession()
        {
            if (SceneFlowManager.Instance != null)
                Object.DestroyImmediate(SceneFlowManager.Instance.gameObject);
            if (GameSession.Instance != null)
                Object.DestroyImmediate(GameSession.Instance.gameObject);
        }

        private static void InvokeStart(MonoBehaviour controller)
        {
            controller.GetType()
                .GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(controller, null);
        }

        private static void InvokePrivate(MonoBehaviour controller, string methodName)
        {
            var method = controller.GetType()
                .GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method, methodName);
            method.Invoke(controller, null);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            return (T)target.GetType()
                .GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(target);
        }

        private static void DestroyAdventureUi()
        {
            var canvas = GameObject.Find("UICanvas");
            if (canvas != null)
                Object.DestroyImmediate(canvas);
        }
    }

    public class BattleUIManagerTests
    {
        [Test]
        public void ActionBarButtonsRouteStableCombatContextToTheCommandHandler()
        {
            var host = new GameObject("BattleUIManagerCommandTest");
            try
            {
                var ui = host.AddComponent<BattleUIManager>();
                typeof(BattleUIManager)
                    .GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(ui, null);
                var handlerType = System.Reflection.Assembly.Load("TianZhang.Gameplay.Contracts")
                    .GetType("TianZhang.Gameplay.Contracts.ICombatCommandHandler", true);
                var createProxy = System.Array.Find(
                    typeof(System.Reflection.DispatchProxy).GetMethods(),
                    method => method.Name == "Create" && method.IsGenericMethodDefinition);
                object handler = createProxy
                    .MakeGenericMethod(handlerType, typeof(RecordingCombatCommandProxy))
                    .Invoke(null, null);
                typeof(BattleUIManager)
                    .GetMethod("SetCombatCommandHandler")
                    .Invoke(ui, new[] { handler });
                ui.SetCombatCommandContext(
                    "player",
                    "enemy",
                    new[] { "art-0", "art-1", "art-2" },
                    new[] { "divine-0", "divine-1" },
                    "art-swap");

                FindButton("BtnAttack").onClick.Invoke();
                FindButton("BtnGuard").onClick.Invoke();
                FindButton("BtnWait").onClick.Invoke();
                FindButton("BtnSwap").onClick.Invoke();
                FindButton("BtnSpell2").onClick.Invoke();
                FindButton("BtnSkill1").onClick.Invoke();

                CollectionAssert.AreEqual(
                    new[]
                    {
                        "basic:player:enemy",
                        "guard:player",
                        "wait:player",
                        "swap:player:0:art-swap",
                        "art:player:enemy:art-2",
                        "divine:player:enemy:divine-1",
                    },
                    ((RecordingCombatCommandProxy)handler).Calls);
            }
            finally
            {
                var canvas = GameObject.Find("UICanvas");
                if (canvas != null)
                    Object.DestroyImmediate(canvas);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ActionBarButtonsIgnoreClicksWhenNoCombatCommandHandlerIsBound()
        {
            var host = new GameObject("BattleUIManagerNoCommandHandlerTest");
            try
            {
                var ui = host.AddComponent<BattleUIManager>();
                typeof(BattleUIManager)
                    .GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(ui, null);

                Assert.DoesNotThrow(() =>
                {
                    FindButton("BtnAttack").onClick.Invoke();
                    FindButton("BtnGuard").onClick.Invoke();
                    FindButton("BtnWait").onClick.Invoke();
                    FindButton("BtnSwap").onClick.Invoke();
                    FindButton("BtnSpell0").onClick.Invoke();
                    FindButton("BtnSkill0").onClick.Invoke();
                });
            }
            finally
            {
                var canvas = GameObject.Find("UICanvas");
                if (canvas != null)
                    Object.DestroyImmediate(canvas);
                Object.DestroyImmediate(host);
            }
        }

        private static Button FindButton(string name)
        {
            foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
            {
                if (button.name == name)
                    return button;
            }

            Assert.Fail(name);
            return null;
        }

        public class RecordingCombatCommandProxy : System.Reflection.DispatchProxy
        {
            public List<string> Calls { get; } = new List<string>();

            protected override object Invoke(System.Reflection.MethodInfo targetMethod, object[] args)
            {
                string call = targetMethod.Name switch
                {
                    "RequestBasicAttack" => $"basic:{args[0]}:{args[1]}",
                    "RequestArt" => $"art:{args[0]}:{args[1]}:{args[2]}",
                    "RequestDivine" => $"divine:{args[0]}:{args[1]}:{args[2]}",
                    "RequestGuard" => $"guard:{args[0]}",
                    "RequestWait" => $"wait:{args[0]}",
                    "RequestMove" => $"move:{args[0]}:{args[1]}:{args[2]}",
                    "RequestSwapSpell" => $"swap:{args[0]}:{args[1]}:{args[2]}",
                    _ => targetMethod.Name,
                };
                Calls.Add(call);
                return null;
            }
        }

    }
}
