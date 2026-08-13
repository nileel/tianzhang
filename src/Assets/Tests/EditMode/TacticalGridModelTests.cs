using NUnit.Framework;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TianZhang.Features.Adventure;
using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Spatial;
using TianZhang.Editor;
using TianZhang.Entity;
using TianZhang.Game;
using TianZhang.Features.CharacterCreation;
using TianZhang.Cultivation;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using TianZhang.World;
using TianZhang.Infrastructure.UnityContent;
using UnityEditor.SceneManagement;
using EnvironmentProfileData = TianZhang.Infrastructure.UnityContent.EnvironmentProfileAsset;

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
            item.contentScope = InventoryGrantUseCase.ProductionContentScope;
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

}
