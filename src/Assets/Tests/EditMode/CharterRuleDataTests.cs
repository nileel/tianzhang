using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.World;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests
{
    public class CharterRuleDataTests
    {
        private static readonly string[] Columns =
        {
            "ruleEntryId", "displayName", "ruleFamily", "relationElement", "compatiblePhenomena",
            "positiveCommit", "negativeCommit", "requiredAuthority", "requiredNodeTypes", "scopeType",
            "scopeTierCap", "anchorNodeIds", "propagationBoundaryProfileId", "currentCoverageSet",
            "affectedWorldVariables", "conflictProfileId", "failurePolicy", "worldEventOutputs",
        };

        private const string ValidRow =
            "charter_fixture,display_charter_fixture,rule_family_five_element,element_water,phenomenon_rain|phenomenon_drizzle,commit_positive,commit_negative,authority_water,node_type_charter|node_type_water,CONNECTED_NODES,AREA,node_anchor|node_water,boundary_watershed,coverage_anchor|coverage_water,var_precipitation|var_water_spirit,conflict_water,REJECT,event_water~env_fixture";

        private static string Header => string.Join(",", Columns);

        [Test]
        public void ParseCharterRuleDefinitionsProjectsEveryOneOfTheEighteenContractFields()
        {
            var definitions = DataConfigImporter.ParseCharterRuleDefinitions(
                new[] { Header, ValidRow },
                "CharterRuleDefinitions.csv",
                BuildCatalog());
            try
            {
                var definition = definitions.Single();
                Assert.AreEqual(18, typeof(CharterRuleDefinitionData)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Length);
                Assert.AreEqual("charter_fixture", definition.ruleEntryId);
                Assert.AreEqual("display_charter_fixture", definition.displayName);
                Assert.AreEqual(CharterRuleScopeType.ConnectedNodes, definition.scopeType);
                Assert.AreEqual(CharterRuleScopeTierCap.Area, definition.scopeTierCap);
                Assert.AreEqual(CharterRuleFailurePolicy.Reject, definition.failurePolicy);
                CollectionAssert.AreEqual(new[] { "node_anchor", "node_water" }, definition.anchorNodeIds);
                CollectionAssert.AreEqual(new[] { "coverage_anchor", "coverage_water" }, definition.currentCoverageSet);
                Assert.AreEqual("event_water", definition.worldEventOutputs.Single().eventId);
                Assert.AreEqual("env_fixture", definition.worldEventOutputs.Single().environmentProfileId);
            }
            finally
            {
                DestroyAll(definitions);
            }
        }

        [TestCase(7, "authority_unknown", "CHARTER_UNKNOWN_AUTHORITY_REFERENCE")]
        [TestCase(8, "node_type_unknown", "CHARTER_UNKNOWN_NODE_TYPE_REFERENCE")]
        [TestCase(11, "node_unknown", "CHARTER_UNKNOWN_NODE_REFERENCE")]
        [TestCase(12, "boundary_unknown", "CHARTER_UNKNOWN_BOUNDARY_REFERENCE")]
        [TestCase(13, "coverage_outside", "CHARTER_COVERAGE_OUT_OF_BOUNDARY")]
        [TestCase(14, "variable_unknown", "CHARTER_UNKNOWN_VARIABLE_REFERENCE")]
        [TestCase(15, "conflict_unknown", "CHARTER_UNKNOWN_CONFLICT_REFERENCE")]
        [TestCase(17, "event_water~env_unknown", "CHARTER_UNKNOWN_ENVIRONMENT_PROFILE_REFERENCE")]
        public void ParseCharterRuleDefinitionsRejectsUnknownOrOutOfBoundaryReferences(
            int changedColumn,
            string changedValue,
            string expectedReason)
        {
            var values = ValidRow.Split(',');
            values[changedColumn] = changedValue;

            AssertParseFails(string.Join(",", values), BuildCatalog(), expectedReason);
        }

        [Test]
        public void ParseCharterRuleDefinitionsRejectsUnknownRelicAndOrganizationAuthorizationInTheRequiredAuthority()
        {
            var relicCatalog = BuildCatalog();
            relicCatalog.authorityRequirements[0].relicId = "relic_unknown";
            AssertParseFails(ValidRow, relicCatalog, "CHARTER_UNKNOWN_RELIC_REFERENCE");

            var authorizationCatalog = BuildCatalog();
            authorizationCatalog.authorityRequirements[0].organizationAuthorizationVersionIds = new[] { "authorization_unknown" };
            AssertParseFails(ValidRow, authorizationCatalog, "CHARTER_UNKNOWN_AUTHORIZATION_REFERENCE");
        }

        [TestCase(5)]
        [TestCase(6)]
        public void ParseCharterRuleDefinitionsRejectsEitherHalfOfAnAtomicCommit(int changedColumn)
        {
            var values = ValidRow.Split(',');
            values[changedColumn] = "none";

            AssertParseFails(string.Join(",", values), BuildCatalog(), "CHARTER_ATOMIC_COMMIT_INCOMPLETE");
        }

        [Test]
        public void DynamicStateUsesOnlyDefinitionIdsAndRejectsStateIdMixing()
        {
            var definition = ScriptableObject.CreateInstance<CharterRuleDefinitionData>();
            definition.ruleEntryId = "charter_fixture";
            try
            {
                var state = new CharterRuntimeStateData
                {
                    stateId = "charter_runtime_fixture",
                    charterRelicState = "recognized",
                    worldSealState = "recognized",
                    registeredRuleEntryIds = new[] { "charter_fixture" },
                    currentRegionRuleEntryIds = new[] { "charter_fixture" },
                };

                Assert.IsTrue(state.TryValidate(new[] { definition }, BuildCatalog(), out var validReason), validReason);

                state.registeredRuleEntryIds = new[] { "unknown_entry" };
                Assert.IsFalse(state.TryValidate(new[] { definition }, BuildCatalog(), out var unknownReason));
                Assert.AreEqual(CharterRuntimeStateReasons.UnknownRuleEntry, unknownReason);

                state.registeredRuleEntryIds = new[] { state.stateId };
                Assert.IsFalse(state.TryValidate(new[] { definition }, BuildCatalog(), out var mixedReason));
                Assert.AreEqual(CharterRuntimeStateReasons.DefinitionIdMixed, mixedReason);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void DynamicStateDoesNotOwnStaticDefinitionsOrBattlefieldEnvironmentProfiles()
        {
            var fieldTypes = typeof(CharterRuntimeStateData)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(field => field.FieldType)
                .ToArray();

            Assert.IsFalse(fieldTypes.Any(type => type == typeof(CharterRuleDefinitionData)));
            Assert.IsFalse(fieldTypes.Any(type => type.IsArray && type.GetElementType() == typeof(CharterRuleDefinitionData)));
            Assert.IsFalse(fieldTypes.Any(type => type.Name.Contains("EnvironmentProfile", StringComparison.Ordinal)));
        }

        [Test]
        public void ImportCharterRuleDefinitionsRejectsTheWholeProductionTableBeforeCreatingAnAsset()
        {
            const string sourceAssetPath = "Assets/DataConfig/CharterRuleDefinitions.csv";
            const string importedAssetPath = "Assets/Data/CharterRuleDefinitions/CharterRuleDefinition_charter_fixture.asset";
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/CharterRuleDefinitions.csv");
            byte[] originalContents = File.ReadAllBytes(sourceFilePath);

            try
            {
                AssetDatabase.DeleteAsset(importedAssetPath);
                File.WriteAllText(sourceFilePath, Header + "\n" + ValidRow + "\n");
                AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceSynchronousImport);

                var exception = Assert.Throws<InvalidDataException>(() => DataConfigImporter.ImportCharterRuleDefinitions());
                StringAssert.StartsWith("CHARTER_REFERENCE_CATALOG_UNDECLARED:", exception.Message);
                Assert.IsNull(AssetDatabase.LoadAssetAtPath<CharterRuleDefinitionData>(importedAssetPath));
            }
            finally
            {
                AssetDatabase.DeleteAsset(importedAssetPath);
                File.WriteAllBytes(sourceFilePath, originalContents);
                AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [Test]
        public void ProductionCharterRuleDefinitionCsvRemainsTheAuthorizedEmptySchema()
        {
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/CharterRuleDefinitions.csv");
            var definitions = DataConfigImporter.ParseCharterRuleDefinitions(
                File.ReadAllLines(sourceFilePath),
                sourceFilePath,
                new CharterRuleReferenceCatalog());
            try
            {
                Assert.AreEqual(0, definitions.Length);
            }
            finally
            {
                DestroyAll(definitions);
            }
        }

        private static void AssertParseFails(
            string row,
            CharterRuleReferenceCatalog catalog,
            string expectedReason)
        {
            var exception = Assert.Throws<InvalidDataException>(() => DataConfigImporter.ParseCharterRuleDefinitions(
                new[] { Header, row },
                "CharterRuleDefinitions.csv",
                catalog));
            StringAssert.StartsWith(expectedReason + ":", exception.Message);
        }

        private static CharterRuleReferenceCatalog BuildCatalog()
        {
            return new CharterRuleReferenceCatalog
            {
                displayNameKeys = new[] { "display_charter_fixture" },
                ruleFamilyIds = new[] { "rule_family_five_element" },
                relationElementIds = new[] { "element_water" },
                phenomenonIds = new[] { "phenomenon_rain", "phenomenon_drizzle" },
                relicIds = new[] { "relic_world_charter" },
                organizationAuthorizationVersionIds = new[] { "authorization_water_v1" },
                authorityRequirements = new[]
                {
                    new CharterAuthorityRequirement
                    {
                        authorityId = "authority_water",
                        relicId = "relic_world_charter",
                        organizationAuthorizationVersionIds = new[] { "authorization_water_v1" },
                    },
                },
                nodeTypeIds = new[] { "node_type_charter", "node_type_water" },
                nodeIds = new[] { "node_anchor", "node_water" },
                propagationBoundaries = new[]
                {
                    new CharterPropagationBoundaryReference
                    {
                        propagationBoundaryProfileId = "boundary_watershed",
                        allowedCoverageIds = new[] { "coverage_anchor", "coverage_water" },
                    },
                },
                realitySupplyIds = new[] { "supply_upstream", "supply_downstream" },
                commits = new[]
                {
                    new CharterCommitReference { commitId = "commit_positive", realitySupplyIds = new[] { "supply_upstream" } },
                    new CharterCommitReference { commitId = "commit_negative", realitySupplyIds = new[] { "supply_downstream" } },
                },
                worldVariableIds = new[] { "var_precipitation", "var_water_spirit" },
                conflicts = new[]
                {
                    new CharterConflictReference
                    {
                        conflictProfileId = "conflict_water",
                        crossTierChallengeGrantIds = new[] { "cross_tier_water_v1" },
                    },
                },
                worldEventIds = new[] { "event_water" },
                environmentProfileIds = new[] { "env_fixture" },
                ruleEntryIds = new[] { "charter_fixture" },
            };
        }

        private static void DestroyAll(CharterRuleDefinitionData[] definitions)
        {
            foreach (var definition in definitions ?? Array.Empty<CharterRuleDefinitionData>())
            {
                if (definition != null)
                    UnityEngine.Object.DestroyImmediate(definition);
            }
        }
    }
}
