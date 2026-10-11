using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using TianZhang.Editor;
using TianZhang.Entity;

namespace TianZhang.Tests
{
    public sealed class ContentImporterArchitectureTests
    {
        private static readonly Type[] DomainImporterTypes =
        {
            typeof(CharacterContentImporter),
            typeof(CombatContentImporter),
            typeof(CultivationContentImporter),
            typeof(WorldContentImporter),
            typeof(SettlementContentImporter),
        };

        [Test]
        public void CoordinatorOwnsOnlyTheDeterministicFullImportSequence()
        {
            Type coordinator = typeof(ContentImportCoordinator);
            Assert.That(
                coordinator.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                                      BindingFlags.DeclaredOnly),
                Is.Empty);

            MethodInfo[] methods = coordinator.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            CollectionAssert.AreEquivalent(new[] { "ImportAll" }, methods.Select(method => method.Name));

            string source = ReadEditorSource(nameof(ContentImportCoordinator));
            foreach (string forbidden in new[]
                     {
                         "readonly string[]", "ScriptableObject", "CreateAsset", "LoadAssetAtPath",
                         "EditorUtility", "CsvTableReader", "AssetCommitter",
                     })
            {
                StringAssert.DoesNotContain(forbidden, source);
            }

            AssertAppearsInOrder(
                source,
                "CultivationContentImporter.ImportNpcCultivationActionWeightProfiles()",
                "CultivationContentImporter.ImportFoundationPurpleMansionStates()",
                "CultivationContentImporter.ImportJindanStaticStates()",
                "WorldContentImporter.ImportCharterRuleDefinitions()",
                "SettlementContentImporter.ImportCharterSites()",
                "CultivationContentImporter.ImportGongFa()",
                "CombatContentImporter.ImportSpells()",
                "CombatContentImporter.ImportSkills()",
                "CharacterContentImporter.ImportCharacterDefinitions()",
                "SettlementContentImporter.ImportContentCatalog()",
                "SettlementContentImporter.ImportCharacterCreationPointBuy()",
                "WorldContentImporter.ImportEnvironmentProfiles()",
                "AdventureContentImporter.Import()",
                "AssetDatabase.SaveAssets()",
                "AssetDatabase.Refresh()");
        }

        [Test]
        public void EveryDomainImporterOwnsADirectEntryAndDoesNotForwardToCoordinator()
        {
            foreach (Type importerType in DomainImporterTypes)
            {
                Assert.That(
                    importerType.GetMethod(
                        "Import",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly),
                    Is.Not.Null,
                    importerType.Name);
                StringAssert.DoesNotContain(
                    "ContentImportCoordinator.",
                    ReadEditorSource(importerType.Name),
                    importerType.Name);
            }
        }

        [Test]
        public void SharedImportMechanismsContainNoDomainSchema()
        {
            string sharedSource = string.Join(
                Environment.NewLine,
                ReadEditorSource(nameof(CsvTableReader)),
                ReadEditorSource(nameof(ImportDiagnostics)),
                ReadEditorSource(nameof(AssetCommitter)));

            foreach (string fieldName in new[]
                     {
                         "characterId", "attackProfileId", "settlementId", "ruleEntryId",
                         "profileId", "schemaId", "contentScope",
                     })
            {
                StringAssert.DoesNotContain(fieldName, sharedSource, fieldName);
            }
        }

        [Test]
        public void DirectImporterTestsNoLongerCallCoordinatorDomainApis()
        {
            string testsRoot = Path.Combine(Application.dataPath, "Tests", "EditMode");
            foreach (string path in Directory.GetFiles(testsRoot, "*.cs", SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(path) == nameof(ContentImporterArchitectureTests) + ".cs")
                    continue;
                StringAssert.DoesNotContain(
                    "ContentImportCoordinator.",
                    File.ReadAllText(path),
                    Path.GetFileName(path));
            }
        }

        [Test]
        public void CharacterDomainFailureLeavesCharacterAndOtherDomainAssetsUntouched()
        {
            string csvPath = Path.Combine(Application.dataPath, "DataConfig", "Characters.csv");
            byte[] originalCsv = File.ReadAllBytes(csvPath);
            string[] watchedDirectories =
            {
                Path.Combine(Application.dataPath, "Data", "Characters"),
                Path.Combine(Application.dataPath, "Data", "ContentCatalog"),
            };
            var before = SnapshotAssets(watchedDirectories);

            try
            {
                File.WriteAllLines(
                    csvPath,
                    File.ReadAllLines(csvPath).Concat(new[] { "invalid_character_row" }));
                Assert.Throws<InvalidDataException>(
                    () => CharacterContentImporter.ImportCharacterDefinitions());
            }
            finally
            {
                File.WriteAllBytes(csvPath, originalCsv);
            }

            var after = SnapshotAssets(watchedDirectories);
            CollectionAssert.AreEquivalent(before.Keys, after.Keys);
            foreach (string path in before.Keys)
                CollectionAssert.AreEqual(before[path], after[path], path);
        }

        [Test]
        public void FullImportRemainsDeterministicAcrossConsecutiveRuns()
        {
            string[] outputDirectories =
            {
                Path.Combine(Application.dataPath, "Data"),
                Path.Combine(Application.dataPath, "Resources", "Data"),
            };
            var before = SnapshotAssets(outputDirectories);
            var originalGuids = before.Keys.ToDictionary(
                path => path,
                path => AssetDatabase.AssetPathToGUID(ToAssetPath(path)),
                StringComparer.Ordinal);

            ContentImportCoordinator.ImportAll();
            var first = SnapshotAssets(outputDirectories);
            ContentImportCoordinator.ImportAll();
            var second = SnapshotAssets(outputDirectories);

            CollectionAssert.AreEquivalent(before.Keys, first.Keys);
            CollectionAssert.AreEquivalent(first.Keys, second.Keys);
            foreach (string path in first.Keys)
            {
                CollectionAssert.AreEqual(first[path], second[path], path);
                Assert.That(
                    AssetDatabase.AssetPathToGUID(ToAssetPath(path)),
                    Is.EqualTo(originalGuids[path]),
                    path);
            }
        }

        private static readonly string[] CharacterActionEconomyAssetPaths =
        {
            "Assets/Data/Characters/Char_char_taiyixiushi.asset",
            "Assets/Data/Characters/Char_char_sanxiuqiangdao.asset",
            "Assets/Data/Characters/Char_Player.asset",
            "Assets/Data/Characters/Char_Enemy.asset",
        };

        private const int FrozenActionPointMaximumUnits = 100000;
        private const int FrozenMovementEfficiencyMilliUnits = 4000;

        [Test]
        public void CharacterImportWritesFrozenActionEconomyInputsAndKeepsEveryOtherFieldAndGuid()
        {
            var guidsBefore = CharacterActionEconomyAssetPaths.ToDictionary(
                path => path, AssetDatabase.AssetPathToGUID, StringComparer.Ordinal);
            var othersBefore = CharacterActionEconomyAssetPaths.ToDictionary(
                path => path,
                path => DescribeCharacterWithoutActionEconomy(LoadCharacter(path)),
                StringComparer.Ordinal);

            CharacterContentImporter.ImportCharacterDefinitions();

            foreach (string assetPath in CharacterActionEconomyAssetPaths)
            {
                CharacterData asset = LoadCharacter(assetPath);
                Assert.That(asset.actionPointMaximumUnits, Is.EqualTo(FrozenActionPointMaximumUnits), assetPath);
                Assert.That(asset.movementEfficiencyMilliUnits, Is.EqualTo(assetPath.EndsWith("/Char_Player.asset") ? 2000 : FrozenMovementEfficiencyMilliUnits), assetPath);
                Assert.That(AssetDatabase.AssetPathToGUID(assetPath), Is.EqualTo(guidsBefore[assetPath]), assetPath);
                Assert.That(DescribeCharacterWithoutActionEconomy(asset), Is.EqualTo(othersBefore[assetPath]), assetPath);
            }
        }

        [Test]
        public void RepeatedCharacterImportKeepsTheWholeCharacterDirectoryAndGuidsStable()
        {
            string charactersRoot = Path.Combine(Application.dataPath, "Data", "Characters");

            CharacterContentImporter.ImportCharacterDefinitions();
            var first = SnapshotAssets(new[] { charactersRoot });
            var guidsBefore = CharacterActionEconomyAssetPaths.ToDictionary(
                path => path, AssetDatabase.AssetPathToGUID, StringComparer.Ordinal);

            CharacterContentImporter.ImportCharacterDefinitions();
            var second = SnapshotAssets(new[] { charactersRoot });

            CollectionAssert.AreEquivalent(first.Keys, second.Keys);
            foreach (string path in first.Keys)
                CollectionAssert.AreEqual(first[path], second[path], path);
            foreach (string assetPath in CharacterActionEconomyAssetPaths)
                Assert.That(AssetDatabase.AssetPathToGUID(assetPath), Is.EqualTo(guidsBefore[assetPath]), assetPath);
        }

        [TestCase("", "2000", TestName = "CharacterImportRejectsEmptyActionPointMaximum")]
        [TestCase("0", "2000", TestName = "CharacterImportRejectsZeroActionPointMaximum")]
        [TestCase("-1", "2000", TestName = "CharacterImportRejectsNegativeActionPointMaximum")]
        [TestCase("2.5", "2000", TestName = "CharacterImportRejectsFractionalActionPointMaximum")]
        [TestCase("2147483648", "2000", TestName = "CharacterImportRejectsOverflowingActionPointMaximum")]
        [TestCase("100000", "-1", TestName = "CharacterImportRejectsNegativeMovementEfficiency")]
        [TestCase("100000", "1.5", TestName = "CharacterImportRejectsFractionalMovementEfficiency")]
        [TestCase("100000", "4294967296", TestName = "CharacterImportRejectsOverflowingMovementEfficiency")]
        [TestCase("100000", "", TestName = "CharacterImportRejectsEmptyMovementEfficiency")]
        public void CharacterImportRejectsInvalidActionEconomyRowsWithoutWritingAnyAsset(
            string actionPointMaximumUnits,
            string movementEfficiencyMilliUnits)
        {
            AssertCharacterCsvRejectedWithoutWritingAssets(
                "," + actionPointMaximumUnits + "," + movementEfficiencyMilliUnits);
        }

        [TestCase(",100000", TestName = "CharacterImportRejectsShortActionEconomyRowTail")]
        [TestCase(",100000,2000,999", TestName = "CharacterImportRejectsExtraActionEconomyColumn")]
        public void CharacterImportRejectsRowWidthMismatchWithoutWritingAnyAsset(string rowTail)
        {
            AssertCharacterCsvRejectedWithoutWritingAssets(rowTail);
        }

        [Test]
        public void CharacterImportRejectsZeroMovementThatDisagreesWithRealmTable()
        { AssertCharacterCsvRejectedWithoutWritingAssets(",100000,0"); }

        [Test]
        public void CharacterImportWithoutAnIndependentTemplateWritesNoAsset()
        {
            const string missingAssetPath = "Assets/Data/Characters/Char_Enemy.asset";
            string missingFullPath = Path.Combine(Application.dataPath, "Data", "Characters", "Char_Enemy.asset");
            byte[] assetBytes = File.ReadAllBytes(missingFullPath);
            byte[] metaBytes = File.ReadAllBytes(missingFullPath + ".meta");
            var othersBefore = CharacterActionEconomyAssetPaths
                .Where(path => !string.Equals(path, missingAssetPath, StringComparison.Ordinal))
                .ToDictionary(path => path, path => File.ReadAllBytes(ToAbsolutePath(path)), StringComparer.Ordinal);

            Dictionary<string, byte[]> othersAfterFailure = null;
            try
            {
                Assert.That(AssetDatabase.DeleteAsset(missingAssetPath), Is.True);
                Assert.Throws<InvalidDataException>(() => CharacterContentImporter.ImportCharacterDefinitions());
                othersAfterFailure = othersBefore.Keys.ToDictionary(
                    path => path, path => File.ReadAllBytes(ToAbsolutePath(path)), StringComparer.Ordinal);
            }
            finally
            {
                File.WriteAllBytes(missingFullPath, assetBytes);
                File.WriteAllBytes(missingFullPath + ".meta", metaBytes);
                AssetDatabase.ImportAsset(missingAssetPath, ImportAssetOptions.ForceUpdate);
            }

            Assert.That(othersAfterFailure, Is.Not.Null);
            foreach (string path in othersBefore.Keys)
                CollectionAssert.AreEqual(othersBefore[path], othersAfterFailure[path], path);
        }

        private void AssertCharacterCsvRejectedWithoutWritingAssets(string rowTail)
        {
            string csvPath = Path.Combine(Application.dataPath, "DataConfig", "Characters.csv");
            byte[] originalCsv = File.ReadAllBytes(csvPath);
            var othersBefore = CharacterActionEconomyAssetPaths.ToDictionary(
                path => path, path => File.ReadAllBytes(ToAbsolutePath(path)), StringComparer.Ordinal);

            try
            {
                File.WriteAllText(csvPath, BuildCharacterCsv(rowTail));
                Assert.Throws<InvalidDataException>(() => CharacterContentImporter.ImportCharacterDefinitions());
            }
            finally
            {
                File.WriteAllBytes(csvPath, originalCsv);
            }

            foreach (string path in othersBefore.Keys)
                CollectionAssert.AreEqual(othersBefore[path], File.ReadAllBytes(ToAbsolutePath(path)), path);
        }

        /// <summary>替换行动经济两列，保留显式身体族，用于构造非法整表输入。</summary>
        private static string BuildCharacterCsv(string rowTail)
        {
            string csvPath = Path.Combine(Application.dataPath, "DataConfig", "Characters.csv");
            var rows = new List<string>();
            foreach (string line in File.ReadAllLines(csvPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("name,"))
                {
                    rows.Add(line);
                    continue;
                }

                string[] columns = line.Split(',');
                Assert.That(columns.Length, Is.EqualTo(23), line);
                Assert.That(columns[22], Is.EqualTo("body_human"), line);
                rows.Add(string.Join(",", columns.Take(20)) + rowTail + "," + columns[22]);
            }

            return string.Join(Environment.NewLine, rows) + Environment.NewLine;
        }

        private static CharacterData LoadCharacter(string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<CharacterData>(assetPath);
            Assert.That(asset, Is.Not.Null, assetPath);
            return asset;
        }

        /// <summary>
        /// 本轮唯一允许变化的字段是两项长期行动力输入；归一化后比较其余全部序列化字段。
        /// </summary>
        private static string DescribeCharacterWithoutActionEconomy(CharacterData data)
        {
            string json = EditorJsonUtility.ToJson(data);
            json = Regex.Replace(json, ",\"actionPointMaximumUnits\":[^,}]*", string.Empty);
            json = Regex.Replace(json, ",\"movementEfficiencyMilliUnits\":[^,}]*", string.Empty);
            return json;
        }

        private static string ToAbsolutePath(string assetPath) =>
            Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar));

        private static string ReadEditorSource(string typeName)
        {
            string path = Path.Combine(Application.dataPath, "Scripts", "Editor", typeName + ".cs");
            Assert.That(File.Exists(path), Is.True, path);
            return File.ReadAllText(path);
        }

        private static void AssertAppearsInOrder(string source, params string[] markers)
        {
            int previous = -1;
            foreach (string marker in markers)
            {
                int current = source.IndexOf(marker, StringComparison.Ordinal);
                Assert.That(current, Is.GreaterThan(previous), marker);
                previous = current;
            }
        }

        private static Dictionary<string, byte[]> SnapshotAssets(string[] directories)
        {
            return directories
                .Where(Directory.Exists)
                .SelectMany(directory => Directory.GetFiles(directory, "*.asset", SearchOption.AllDirectories))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        }

        private static string ToAssetPath(string absolutePath) =>
            "Assets" + absolutePath.Substring(Application.dataPath.Length).Replace('\\', '/');
    }
}
