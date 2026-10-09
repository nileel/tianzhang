using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.Entity;
using TianZhang.Game.CharacterCreation;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests
{
    public class ContentCatalogDataTests
    {
        private const string LanguagePath = "Assets/DataConfig/Language.csv";
        private const string SettlementsPath = "Assets/DataConfig/Settlements.csv";
        private const string ItemsPath = "Assets/DataConfig/Items.csv";
        private const string BountiesPath = "Assets/DataConfig/Bounties.csv";
        private const string EnemiesPath = "Assets/DataConfig/Enemies.csv";
        private const string PointBuyPath = "Assets/DataConfig/CharacterCreationPointBuy.csv";
        private const string PointBuyAssetPath = "Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset";
        private static readonly string[] EnemyIds =
        {
            "enemy_shijiahou", "enemy_fengsun", "enemy_yanweihu",
            "enemy_xueyingmoxiu", "enemy_huangyesanxiu"
        };

        [Test]
        public void FormalContentCsvBuildsExactlyTheApprovedFourDomainProjections()
        {
            var preview = ParseProductionPreview();
            try
            {
                Assert.AreEqual(1, preview.settlements.Length);
                Assert.AreEqual("guanzhong_city", preview.settlements[0].settlementId);
                Assert.AreEqual("content_scope_production", preview.settlements[0].contentScope);
                Assert.AreEqual("bounty_board", preview.settlements[0].features[0].featureId);

                Assert.AreEqual(1, preview.enemies.Length);
                var enemy = preview.enemies[0];
                Assert.AreEqual("enemy_shijiahou", enemy.enemyId);
                Assert.AreEqual("ai_melee", enemy.aiProfileId);
                Assert.AreSame(enemy.combatTemplate, preview.enemyTemplates.Single(template => template == enemy.combatTemplate));
                CollectionAssert.AreEquivalent(
                    new[] { "item_shijia_piece", "item_lingshi_low" },
                    enemy.dropEntries.Select(entry => entry.itemId));
                Assert.AreEqual(100, enemy.dropEntries.Single(entry => entry.itemId == "item_shijia_piece").dropChancePercent);
                Assert.AreEqual(50, enemy.dropEntries.Single(entry => entry.itemId == "item_lingshi_low").dropChancePercent);

                CollectionAssert.AreEquivalent(
                    new[] { "item_lingshi_low", "item_shijia_piece", "item_iron_sword", "item_iron_blade" },
                    preview.items.Select(item => item.itemId));
                Assert.IsTrue(preview.items.All(item => item.maxStack == (item.IsApprovedEquipment ? 1 : 99)));

                Assert.AreEqual(1, preview.bounties.Length);
                var bounty = preview.bounties[0];
                Assert.AreEqual("bounty_guanzhong_shijiahou", bounty.bountyId);
                Assert.AreEqual("item_lingshi_low", bounty.rewardEntries[0].itemId);
                Assert.AreEqual(3, bounty.rewardEntries[0].quantity);
            }
            finally
            {
                DestroyPreview(preview);
            }
        }

        [Test]
        public void FormalEnemyTemplateExplicitlyBindsTheBasicUnarmedAttackProfile()
        {
            var preview = ParseProductionPreview();
            try
            {
                var enemy = preview.enemies[0];
                Assert.AreEqual("enemy_shijiahou", enemy.enemyId);
                var template = preview.enemyTemplates.Single(candidate => candidate == enemy.combatTemplate);
                Assert.AreEqual("basic_unarmed", template.unarmedBasicAttackProfileId);
                Assert.IsTrue(string.IsNullOrEmpty(template.mainEquipmentBasicAttackProfileId));
            }
            finally
            {
                DestroyPreview(preview);
            }
        }

        [Test]
        public void InvalidContentFixturesFailBeforePersistentAssetsCanBeWritten()
        {
            var language = ReadDataFile(LanguagePath);
            var settlements = ReadDataFile(SettlementsPath);
            var items = ReadDataFile(ItemsPath);
            var bounties = ReadDataFile(BountiesPath);
            var enemies = ReadDataFile(EnemiesPath);

            Assert.Throws<InvalidDataException>(() => SettlementContentImporter.ParseContentCatalog(
                language.Where(line => !line.StartsWith("bounty_guanzhong_shijiahou_title,", StringComparison.Ordinal)).ToArray(),
                settlements, items, bounties, enemies));

            var duplicateItems = items.Concat(new[]
            {
                "item_lingshi_low,item_lingshi_low,item_lingshi_low_description,content_scope_production,basic_resource,99,,,"
            }).ToArray();
            Assert.Throws<InvalidDataException>(() => SettlementContentImporter.ParseContentCatalog(
                language, settlements, duplicateItems, bounties, enemies));

            var invalidEnemyParameters = enemies
                .Select(line => line.Replace("item_lingshi_low@50@1", "item_lingshi_low@101@1"))
                .ToArray();
            Assert.Throws<InvalidDataException>(() => SettlementContentImporter.ParseContentCatalog(
                language, settlements, items, bounties, invalidEnemyParameters));

            var invalidBountyReward = bounties
                .Select(line => line.Replace("item_lingshi_low@3", "item_shijia_piece@3"))
                .ToArray();
            Assert.Throws<InvalidDataException>(() => SettlementContentImporter.ParseContentCatalog(
                language, settlements, items, invalidBountyReward, enemies));
        }

        [Test]
        public void ImportKeepsStoneArmorBeastTemplateGuidAndRejectsBadWholeTableWithoutMutation()
        {
            const string characterPath = "Assets/Data/Characters/Char_Enemy_enemy_shijiahou.asset";
            const string bountyAssetPath = "Assets/Data/Bounties/Bounty_bounty_guanzhong_shijiahou.asset";
            string characterGuid = AssetDatabase.AssetPathToGUID(characterPath);
            Assert.IsFalse(string.IsNullOrEmpty(characterGuid));

            SettlementContentImporter.ImportContentCatalog();
            Assert.AreEqual(characterGuid, AssetDatabase.AssetPathToGUID(characterPath));

            var bountyBefore = AssetDatabase.LoadAssetAtPath<BountyData>(bountyAssetPath);
            Assert.IsNotNull(bountyBefore);
            string descriptionKeyBefore = bountyBefore.descriptionKey;
            byte[] originalBountyCsv = File.ReadAllBytes(ToAbsolutePath(BountiesPath));
            try
            {
                File.WriteAllText(
                    ToAbsolutePath(BountiesPath),
                    string.Join("\n", ReadDataFile(BountiesPath)
                        .Select(line => line.Replace("item_lingshi_low@3", "item_lingshi_low@0"))));
                AssetDatabase.ImportAsset(BountiesPath, ImportAssetOptions.ForceSynchronousImport);

                Assert.Throws<InvalidDataException>(() => SettlementContentImporter.ImportContentCatalog());
                var bountyAfterFailure = AssetDatabase.LoadAssetAtPath<BountyData>(bountyAssetPath);
                Assert.AreEqual(descriptionKeyBefore, bountyAfterFailure.descriptionKey);
                Assert.AreEqual(3, bountyAfterFailure.rewardEntries[0].quantity);
                Assert.AreEqual(characterGuid, AssetDatabase.AssetPathToGUID(characterPath));
            }
            finally
            {
                File.WriteAllBytes(ToAbsolutePath(BountiesPath), originalBountyCsv);
                AssetDatabase.ImportAsset(BountiesPath, ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [Test]
        public void AllFiveEnemyTemplatesHaveExplicitProductionActionEconomyInputs()
        {
            var preview = ParseProductionPreview();
            try
            {
                CollectionAssert.AreEquivalent(EnemyIds, preview.enemyTemplateIds);
                Assert.AreEqual(EnemyIds.Length, preview.enemyTemplates.Length);
                foreach (var template in preview.enemyTemplates)
                {
                    Assert.AreEqual(100000, template.actionPointMaximumUnits);
                    Assert.AreEqual(2000, template.movementEfficiencyMilliUnits);
                }
                Assert.AreEqual(1, preview.enemies.Length);
            }
            finally
            {
                DestroyPreview(preview);
            }
        }

        [TestCase("actionPointMaximumUnits", "")]
        [TestCase("actionPointMaximumUnits", "0")]
        [TestCase("actionPointMaximumUnits", "-1")]
        [TestCase("actionPointMaximumUnits", "1.5")]
        [TestCase("actionPointMaximumUnits", "2147483648")]
        [TestCase("movementEfficiencyMilliUnits", "")]
        [TestCase("movementEfficiencyMilliUnits", "-1")]
        [TestCase("movementEfficiencyMilliUnits", "0.5")]
        [TestCase("movementEfficiencyMilliUnits", "2147483648")]
        [TestCase("extraColumn", "unexpected")]
        [TestCase("missingColumn", "movementEfficiencyMilliUnits")]
        public void InvalidLastEnemyRowRejectsWholeImportBeforeAnyAssetWrite(string field, string value)
        {
            var originalCsv = File.ReadAllBytes(ToAbsolutePath(EnemiesPath));
            var assetBytes = Directory.GetFiles(Application.dataPath, "*.asset", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes);
            var metaBytes = Directory.GetFiles(Application.dataPath, "*.asset.meta", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes);
            try
            {
                var lines = WithEnemyInput(ReadDataFile(EnemiesPath), EnemyIds[0], "actionPointMaximumUnits", "100001");
                if (field == "extraColumn")
                    lines = lines.Select(line => line.StartsWith(EnemyIds[4] + ",", StringComparison.Ordinal) ? line + "," + value : line).ToArray();
                else if (field == "missingColumn")
                    lines = lines.Select(line => line.StartsWith("#", StringComparison.Ordinal) ? line : string.Join(",", line.Split(',').Take(line.Split(',').Length - 1))).ToArray();
                else
                    lines = WithEnemyInput(lines, EnemyIds[4], field, value);
                File.WriteAllLines(ToAbsolutePath(EnemiesPath), lines);

                Assert.Throws<InvalidDataException>(() => SettlementContentImporter.ImportContentCatalog());
                CollectionAssert.AreEquivalent(assetBytes.Keys,
                    Directory.GetFiles(Application.dataPath, "*.asset", SearchOption.AllDirectories));
                foreach (var entry in assetBytes.Concat(metaBytes))
                    CollectionAssert.AreEqual(entry.Value, File.ReadAllBytes(entry.Key), entry.Key);
                var firstTemplate = AssetDatabase.LoadAssetAtPath<CharacterData>(
                    "Assets/Data/Characters/Char_Enemy_enemy_shijiahou.asset");
                Assert.AreEqual(100000, firstTemplate.actionPointMaximumUnits);
            }
            finally
            {
                File.WriteAllBytes(ToAbsolutePath(EnemiesPath), originalCsv);
            }
        }

        [Test]
        public void EnemyInputChangesPersistByIdWithZeroMovementAndRepeatWithoutGuidChanges()
        {
            var originalCsv = File.ReadAllBytes(ToAbsolutePath(EnemiesPath));
            var paths = EnemyIds.Select(id => $"Assets/Data/Characters/Char_Enemy_{id}.asset").ToArray();
            var guids = paths.Select(AssetDatabase.AssetPathToGUID).ToArray();
            const string enemyPath = "Assets/Data/Enemies/Enemy_enemy_shijiahou.asset";
            var enemyGuid = AssetDatabase.AssetPathToGUID(enemyPath);
            try
            {
                var lines = ReadDataFile(EnemiesPath);
                for (var index = 0; index < EnemyIds.Length; index++)
                {
                    lines = WithEnemyInput(lines, EnemyIds[index], "actionPointMaximumUnits", (100001 + index).ToString());
                    lines = WithEnemyInput(lines, EnemyIds[index], "movementEfficiencyMilliUnits", index.ToString());
                }
                File.WriteAllLines(ToAbsolutePath(EnemiesPath), lines);
                SettlementContentImporter.ImportContentCatalog();

                for (var index = 0; index < paths.Length; index++)
                {
                    AssetDatabase.ImportAsset(paths[index], ImportAssetOptions.ForceSynchronousImport);
                    var template = AssetDatabase.LoadAssetAtPath<CharacterData>(paths[index]);
                    Assert.AreEqual(100001 + index, template.actionPointMaximumUnits, EnemyIds[index]);
                    Assert.AreEqual(index, template.movementEfficiencyMilliUnits, EnemyIds[index]);
                    Assert.AreEqual(guids[index], AssetDatabase.AssetPathToGUID(paths[index]));
                }
                var bytesAfterFirstImport = paths.Select(File.ReadAllBytes).ToArray();
                SettlementContentImporter.ImportContentCatalog();
                for (var index = 0; index < paths.Length; index++)
                {
                    CollectionAssert.AreEqual(bytesAfterFirstImport[index], File.ReadAllBytes(paths[index]));
                    Assert.AreEqual(guids[index], AssetDatabase.AssetPathToGUID(paths[index]));
                }
                Assert.AreEqual(enemyGuid, AssetDatabase.AssetPathToGUID(enemyPath));
                Assert.AreSame(AssetDatabase.LoadAssetAtPath<CharacterData>(paths[0]),
                    AssetDatabase.LoadAssetAtPath<EnemyData>(enemyPath).combatTemplate);
            }
            finally
            {
                File.WriteAllBytes(ToAbsolutePath(EnemiesPath), originalCsv);
                SettlementContentImporter.ImportContentCatalog();
            }
        }

        private static string[] WithEnemyInput(string[] lines, string enemyId, string field, string value)
        {
            var header = lines.First(line => line.StartsWith("name,", StringComparison.Ordinal)).Split(',');
            int index = Array.IndexOf(header, field);
            Assert.GreaterOrEqual(index, 0);
            return lines.Select(line =>
            {
                if (!line.StartsWith(enemyId + ",", StringComparison.Ordinal))
                    return line;
                var columns = line.Split(',');
                columns[index] = value;
                return string.Join(",", columns);
            }).ToArray();
        }

        [Test]
        public void PointBuyCsvImportWritesTheApprovedLongTermInputsAndStaysIdempotent()
        {
            var originalCsv = File.ReadAllBytes(ToAbsolutePath(PointBuyPath));
            var originalAsset = File.ReadAllBytes(ToProjectPath(PointBuyAssetPath));
            string assetGuid = AssetDatabase.AssetPathToGUID(PointBuyAssetPath);
            try
            {
                SettlementContentImporter.ImportCharacterCreationPointBuy();
                AssetDatabase.ImportAsset(PointBuyAssetPath, ImportAssetOptions.ForceSynchronousImport);
                var config = AssetDatabase.LoadAssetAtPath<CharacterCreationPointBuyConfig>(PointBuyAssetPath);
                Assert.IsNotNull(config);
                Assert.AreEqual(100000, config.actionPointMaximumUnits);
                Assert.AreEqual(2000, config.movementEfficiencyMilliUnits);
                Assert.AreEqual(25, config.purchasePointLimit);
                Assert.AreEqual(3, config.minValue);
                Assert.AreEqual(3, config.baseValue);
                Assert.AreEqual(15, config.maxValue);
                Assert.AreEqual(3, config.costRanges.Length);
                Assert.AreEqual(1, config.CostForLevel(4));
                Assert.AreEqual(2, config.CostForLevel(9));
                Assert.AreEqual(3, config.CostForLevel(13));
                byte[] firstImport = File.ReadAllBytes(ToProjectPath(PointBuyAssetPath));

                SettlementContentImporter.ImportCharacterCreationPointBuy();
                AssetDatabase.ImportAsset(PointBuyAssetPath, ImportAssetOptions.ForceSynchronousImport);
                CollectionAssert.AreEqual(firstImport, File.ReadAllBytes(ToProjectPath(PointBuyAssetPath)));
                Assert.AreEqual(assetGuid, AssetDatabase.AssetPathToGUID(PointBuyAssetPath));
                Assert.AreEqual(100000,
                    AssetDatabase.LoadAssetAtPath<CharacterCreationPointBuyConfig>(PointBuyAssetPath).actionPointMaximumUnits);
            }
            finally
            {
                File.WriteAllBytes(ToAbsolutePath(PointBuyPath), originalCsv);
                File.WriteAllBytes(ToProjectPath(PointBuyAssetPath), originalAsset);
                AssetDatabase.ImportAsset(PointBuyAssetPath, ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [TestCase("actionPointMaximumUnits", "")]
        [TestCase("actionPointMaximumUnits", "0")]
        [TestCase("actionPointMaximumUnits", "-1")]
        [TestCase("actionPointMaximumUnits", "100000.5")]
        [TestCase("actionPointMaximumUnits", "2147483648")]
        [TestCase("movementEfficiencyMilliUnits", "")]
        [TestCase("movementEfficiencyMilliUnits", "-1")]
        [TestCase("movementEfficiencyMilliUnits", "0.5")]
        [TestCase("movementEfficiencyMilliUnits", "2147483648")]
        [TestCase("extraColumn", "unexpected")]
        [TestCase("missingColumn", "movementEfficiencyMilliUnits")]
        public void InvalidPointBuyRowRejectsTheWholeImportBeforeAnyAssetWrite(string field, string value)
        {
            var originalCsv = File.ReadAllBytes(ToAbsolutePath(PointBuyPath));
            var originalAsset = File.ReadAllBytes(ToProjectPath(PointBuyAssetPath));
            string assetGuid = AssetDatabase.AssetPathToGUID(PointBuyAssetPath);
            try
            {
                var lines = ReadDataFile(PointBuyPath);
                if (field == "extraColumn")
                {
                    lines = lines.Select(line => line.StartsWith("default,", StringComparison.Ordinal)
                        ? line + "," + value
                        : line).ToArray();
                }
                else if (field == "missingColumn")
                {
                    lines = lines.Select(line => line.StartsWith("#", StringComparison.Ordinal)
                        ? line
                        : string.Join(",", line.Split(',').Take(line.Split(',').Length - 1))).ToArray();
                }
                else
                {
                    lines = WithPointBuyInput(lines, field, value);
                }
                File.WriteAllLines(ToAbsolutePath(PointBuyPath), lines);

                Assert.Throws<InvalidDataException>(() => SettlementContentImporter.ImportCharacterCreationPointBuy());
                CollectionAssert.AreEqual(originalAsset, File.ReadAllBytes(ToProjectPath(PointBuyAssetPath)));
                Assert.AreEqual(assetGuid, AssetDatabase.AssetPathToGUID(PointBuyAssetPath));
                Assert.AreEqual(100000, AssetDatabase.LoadAssetAtPath<CharacterCreationPointBuyConfig>(
                    PointBuyAssetPath).actionPointMaximumUnits);
                Assert.AreEqual(2000, AssetDatabase.LoadAssetAtPath<CharacterCreationPointBuyConfig>(
                    PointBuyAssetPath).movementEfficiencyMilliUnits);
            }
            finally
            {
                File.WriteAllBytes(ToAbsolutePath(PointBuyPath), originalCsv);
            }
        }

        [Test]
        public void PointBuyRowsWithConflictingLongTermInputsAreRejected()
        {
            var originalCsv = File.ReadAllBytes(ToAbsolutePath(PointBuyPath));
            var originalAsset = File.ReadAllBytes(ToProjectPath(PointBuyAssetPath));
            string assetGuid = AssetDatabase.AssetPathToGUID(PointBuyAssetPath);
            try
            {
                // Only the last default row disagrees, so a partial write would silently keep the first value.
                var lines = ReadDataFile(PointBuyPath).ToArray();
                int last = Array.FindLastIndex(lines, line => line.StartsWith("default,", StringComparison.Ordinal));
                Assert.GreaterOrEqual(last, 0);
                var columns = lines[last].Split(',');
                int index = Array.IndexOf(ReadDataFile(PointBuyPath)
                    .First(line => line.StartsWith("configId,", StringComparison.Ordinal)).Split(','), "actionPointMaximumUnits");
                Assert.GreaterOrEqual(index, 0);
                columns[index] = "100001";
                lines[last] = string.Join(",", columns);
                File.WriteAllLines(ToAbsolutePath(PointBuyPath), lines);

                Assert.Throws<InvalidDataException>(() => SettlementContentImporter.ImportCharacterCreationPointBuy());
                CollectionAssert.AreEqual(originalAsset, File.ReadAllBytes(ToProjectPath(PointBuyAssetPath)));
                Assert.AreEqual(assetGuid, AssetDatabase.AssetPathToGUID(PointBuyAssetPath));
            }
            finally
            {
                File.WriteAllBytes(ToAbsolutePath(PointBuyPath), originalCsv);
            }
        }

        private static string[] WithPointBuyInput(string[] lines, string field, string value)
        {
            var header = lines.First(line => line.StartsWith("configId,", StringComparison.Ordinal)).Split(',');
            int index = Array.IndexOf(header, field);
            Assert.GreaterOrEqual(index, 0);
            return lines.Select(line =>
            {
                if (!line.StartsWith("default,", StringComparison.Ordinal))
                    return line;
                var columns = line.Split(',');
                columns[index] = value;
                return string.Join(",", columns);
            }).ToArray();
        }

        private static string ToProjectPath(string assetsRelativePath)
        {
            return Path.Combine(Application.dataPath, assetsRelativePath.Substring("Assets/".Length));
        }

        [Test]
        public void GeneratedCatalogProvidesReadOnlyStableIdQueries()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                "Assets/Data/ContentCatalog/ContentCatalog.asset");
            Assert.IsNotNull(catalog);

            Assert.IsTrue(catalog.TryGetSettlement("guanzhong_city", out var settlement));
            Assert.AreEqual("settlement_guanzhong_city", settlement.displayNameKey);
            Assert.IsTrue(catalog.TryGetEnemy("enemy_shijiahou", out var enemy));
            Assert.AreEqual("ai_melee", enemy.aiProfileId);
            Assert.IsTrue(catalog.TryGetItem("item_lingshi_low", out var item));
            Assert.AreEqual(99, item.maxStack);
            Assert.IsTrue(catalog.TryGetBounty("bounty_guanzhong_shijiahou", out var bounty));
            Assert.AreEqual("one_time", bounty.repeatPolicy);
            Assert.IsTrue(catalog.TryGetAppearanceProfile("none", out var appearance));
            Assert.IsTrue(appearance.IsNone);
            Assert.AreEqual(1, catalog.GetBountiesByIssuer("guanzhong_city").Count);
            Assert.IsTrue(catalog.TryGetAdventureMap("guanzhong_wild", out var adventure));
            Assert.AreEqual("adventure_node_start", adventure.nodes[0].nodeTypeId);
            Assert.IsFalse(catalog.TryGetEnemy("enemy_fengsun", out _));
            Assert.IsFalse(catalog.TryGetItem("item_unknown", out _));
            Assert.IsFalse(catalog.TryGetAppearanceProfile("appearance_unknown", out _));
        }

        private static SettlementContentImporter.ContentCatalogImportPreview ParseProductionPreview()
        {
            return SettlementContentImporter.ParseContentCatalog(
                ReadDataFile(LanguagePath),
                ReadDataFile(SettlementsPath),
                ReadDataFile(ItemsPath),
                ReadDataFile(BountiesPath),
                ReadDataFile(EnemiesPath));
        }

        private static string[] ReadDataFile(string relativePath)
        {
            return File.ReadAllLines(ToAbsolutePath(relativePath));
        }

        private static string ToAbsolutePath(string relativePath)
        {
            return Path.Combine(Application.dataPath, "DataConfig", Path.GetFileName(relativePath));
        }

        private static void DestroyPreview(SettlementContentImporter.ContentCatalogImportPreview preview)
        {
            foreach (var settlement in preview.settlements)
                UnityEngine.Object.DestroyImmediate(settlement);
            foreach (var enemy in preview.enemies)
                UnityEngine.Object.DestroyImmediate(enemy);
            foreach (var item in preview.items)
                UnityEngine.Object.DestroyImmediate(item);
            foreach (var bounty in preview.bounties)
                UnityEngine.Object.DestroyImmediate(bounty);
            foreach (var template in preview.enemyTemplates)
                UnityEngine.Object.DestroyImmediate(template);
        }
    }
}
