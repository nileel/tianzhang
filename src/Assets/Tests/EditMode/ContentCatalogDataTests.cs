using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.Entity;
using UnityEditor;
using UnityEngine;

using TianZhang.Spatial;
using EntityCharacter = TianZhang.Entity.Character;

namespace TianZhang.Tests
{
    public class ContentCatalogDataTests
    {
        private const string LanguagePath = "Assets/DataConfig/Language.csv";
        private const string SettlementsPath = "Assets/DataConfig/Settlements.csv";
        private const string ItemsPath = "Assets/DataConfig/Items.csv";
        private const string BountiesPath = "Assets/DataConfig/Bounties.csv";
        private const string EnemiesPath = "Assets/DataConfig/Enemies.csv";

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
                    new[] { "item_lingshi_low", "item_shijia_piece" },
                    preview.items.Select(item => item.itemId));
                Assert.IsTrue(preview.items.All(item => item.maxStack == 99));

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

                var runtimeEnemy = EntityCharacter.FromData(template, new TianZhang.Spatial.HexCoord(1, 0));
                Assert.AreEqual("basic_unarmed", runtimeEnemy.BasicAttackProfileId);
                Assert.AreEqual("unarmed_fallback", runtimeEnemy.BasicAttackBindingKind);
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

            Assert.Throws<InvalidDataException>(() => ContentImportCoordinator.ParseContentCatalog(
                language.Where(line => !line.StartsWith("bounty_guanzhong_shijiahou_title,", StringComparison.Ordinal)).ToArray(),
                settlements, items, bounties, enemies));

            var duplicateItems = items.Concat(new[]
            {
                "item_lingshi_low,item_lingshi_low,item_lingshi_low_description,content_scope_production,basic_resource,99"
            }).ToArray();
            Assert.Throws<InvalidDataException>(() => ContentImportCoordinator.ParseContentCatalog(
                language, settlements, duplicateItems, bounties, enemies));

            var invalidEnemyParameters = enemies
                .Select(line => line.Replace("item_lingshi_low@50@1", "item_lingshi_low@101@1"))
                .ToArray();
            Assert.Throws<InvalidDataException>(() => ContentImportCoordinator.ParseContentCatalog(
                language, settlements, items, bounties, invalidEnemyParameters));

            var invalidBountyReward = bounties
                .Select(line => line.Replace("item_lingshi_low@3", "item_shijia_piece@3"))
                .ToArray();
            Assert.Throws<InvalidDataException>(() => ContentImportCoordinator.ParseContentCatalog(
                language, settlements, items, invalidBountyReward, enemies));
        }

        [Test]
        public void ImportKeepsStoneArmorBeastTemplateGuidAndRejectsBadWholeTableWithoutMutation()
        {
            const string characterPath = "Assets/Data/Characters/Char_Enemy_enemy_shijiahou.asset";
            const string bountyAssetPath = "Assets/Data/Bounties/Bounty_bounty_guanzhong_shijiahou.asset";
            string characterGuid = AssetDatabase.AssetPathToGUID(characterPath);
            Assert.IsFalse(string.IsNullOrEmpty(characterGuid));

            ContentImportCoordinator.ImportContentCatalog();
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

                Assert.Throws<InvalidDataException>(() => ContentImportCoordinator.ImportContentCatalog());
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
            Assert.AreEqual(1, catalog.GetBountiesByIssuer("guanzhong_city").Count);
            Assert.IsTrue(catalog.TryGetAdventureMap("guanzhong_wild", out var adventure));
            Assert.AreEqual("adventure_node_start", adventure.nodes[0].nodeTypeId);
            Assert.IsFalse(catalog.TryGetEnemy("enemy_fengsun", out _));
            Assert.IsFalse(catalog.TryGetItem("item_unknown", out _));
        }

        private static ContentImportCoordinator.ContentCatalogImportPreview ParseProductionPreview()
        {
            return ContentImportCoordinator.ParseContentCatalog(
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

        private static void DestroyPreview(ContentImportCoordinator.ContentCatalogImportPreview preview)
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
