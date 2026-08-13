using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using TianZhang.Editor;

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
