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
            var definitions = WorldContentImporter.ParseCharterRuleDefinitions(
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
        public void ProductionCharterRuleDefinitionImportsTheApprovedWaterBureauChronicleAndProjectsItsAsset()
        {
            const string sourceAssetPath = "Assets/DataConfig/CharterRuleDefinitions.csv";
            const string importedAssetPath = "Assets/Data/CharterRuleDefinitions/CharterRuleDefinition_charter_entry_suifu_diji.asset";
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/CharterRuleDefinitions.csv");
            CharterRuleStaticCatalogData staticCatalog = LoadProductionStaticCatalog();
            Assert.That(staticCatalog.TryValidateDefinitions(out string staticReason), Is.True, staticReason);
            var definitions = WorldContentImporter.ParseCharterRuleDefinitions(
                File.ReadAllLines(sourceFilePath),
                sourceFilePath,
                staticCatalog.ReferenceCatalog);

            try
            {
                var definition = definitions.Single();
                Assert.AreEqual("charter_entry_suifu_diji", definition.ruleEntryId);
                Assert.AreEqual("charter_entry_suifu_diji", definition.displayName);
                Assert.AreEqual("propagation_suifu_watershed", definition.propagationBoundaryProfileId);
                Assert.AreEqual("conflict_charter_water_basin", definition.conflictProfileId);
                CollectionAssert.AreEqual(
                    new[] { "rain", "drizzle" },
                    definition.compatiblePhenomena);
                CollectionAssert.AreEqual(
                    new[]
                    {
                        "seasonal_precipitation_distribution",
                        "wetland_waterline_state",
                        "water_element_spirit_flow",
                        "aquatic_resource_yield",
                    },
                    definition.affectedWorldVariables);
            }
            finally
            {
                DestroyAll(definitions);
            }

            WorldContentImporter.ImportCharterRuleDefinitions();
            AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadAssetAtPath<CharterRuleDefinitionData>(importedAssetPath);
            Assert.IsNotNull(asset);
            Assert.AreEqual("charter_entry_suifu_diji", asset.ruleEntryId);
            Assert.AreEqual(CharterRuleScopeType.ConnectedNodes, asset.scopeType);
            Assert.AreEqual(CharterRuleScopeTierCap.Area, asset.scopeTierCap);
            Assert.AreEqual(CharterRuleFailurePolicy.Reject, asset.failurePolicy);
            Assert.AreEqual("env_guanzhong_wild", asset.worldEventOutputs[0].environmentProfileId);
        }

        [TestCase(7, "authority_suifu_without_kaihe_passage_v1", "CHARTER_UNKNOWN_AUTHORITY_REFERENCE")]
        [TestCase(7, "authority_suifu_passage_without_seal_management_v1", "CHARTER_UNKNOWN_AUTHORITY_REFERENCE")]
        [TestCase(7, "authority_suifu_without_legal_authorization_v1", "CHARTER_UNKNOWN_AUTHORITY_REFERENCE")]
        [TestCase(11, "node_old_water_station_disconnected", "CHARTER_UNKNOWN_NODE_REFERENCE")]
        [TestCase(5, "commit_suifu_positive_without_registered_supply", "CHARTER_ATOMIC_COMMIT_INCOMPLETE")]
        [TestCase(13, "coverage_suifu_outside_connected_watershed", "CHARTER_COVERAGE_OUT_OF_BOUNDARY")]
        [TestCase(13, "coverage_suifu_yuanying_anchor", "CHARTER_COVERAGE_OUT_OF_BOUNDARY")]
        [TestCase(15, "conflict_charter_water_basin_without_challenge", "CHARTER_UNKNOWN_CONFLICT_REFERENCE")]
        [TestCase(5, "none", "CHARTER_ATOMIC_COMMIT_INCOMPLETE")]
        [TestCase(6, "none", "CHARTER_ATOMIC_COMMIT_INCOMPLETE")]
        public void ProductionCharterRuleDefinitionRejectsUndeclaredOrIncompleteBoundaryFixtures(
            int changedColumn,
            string changedValue,
            string expectedReason)
        {
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/CharterRuleDefinitions.csv");
            string productionRow = File.ReadAllLines(sourceFilePath)
                .Single(line => line.StartsWith("charter_entry_", StringComparison.Ordinal));
            var values = productionRow.Split(',');
            values[changedColumn] = changedValue;

            AssertParseFails(
                string.Join(",", values),
                LoadProductionStaticCatalog().ReferenceCatalog,
                expectedReason);
        }

        [Test]
        public void ProductionCharterAuthorityDirectoryKeepsPassageManagementAndChallengeReferencesExplicit()
        {
            var catalog = LoadProductionStaticCatalog().ReferenceCatalog;
            var authority = catalog.FindAuthority("authority_suifu_kaihe_passage_and_seal_management_v1");
            var conflict = catalog.FindConflict("conflict_charter_water_basin");

            Assert.IsNotNull(authority);
            Assert.AreEqual("relic_world_charter", authority.relicId);
            CollectionAssert.AreEqual(
                new[]
                {
                    "authorization_suifu_water_basin_v1",
                    "authorization_taixuan_seal_old_water_station_management_v1",
                },
                authority.organizationAuthorizationVersionIds);
            Assert.IsTrue(catalog.ContainsRelic("relic_taixuan_realm_seal"));
            CollectionAssert.AreEqual(new[] { "cross_tier_charter_water_basin_v1" }, conflict.crossTierChallengeGrantIds);
        }

        [Test]
        public void ProductionStaticCatalogResolvesOnlyTheImportedDefinitionAndTheApprovedDirectory()
        {
            CharterRuleStaticCatalogData staticCatalog = LoadProductionStaticCatalog();

            Assert.That(staticCatalog.TryValidateDefinitions(out string staticReason), Is.True, staticReason);
            Assert.Greater(staticCatalog.DefinitionCatalogVersion, 0);
            Assert.AreEqual(1, staticCatalog.Definitions.Length);
            Assert.AreEqual("charter_entry_suifu_diji", staticCatalog.Definitions[0].ruleEntryId);
            Assert.IsTrue(staticCatalog.ReferenceCatalog.ContainsRuleEntry("charter_entry_suifu_diji"));
            Assert.IsTrue(staticCatalog.ReferenceCatalog.ContainsRelic("relic_world_charter"));
            Assert.AreEqual(
                "relic_world_charter",
                staticCatalog.ReferenceCatalog.FindAuthority(
                    "authority_suifu_kaihe_passage_and_seal_management_v1").relicId);
            Assert.That(
                CharterRuleCatalogValidator.TryValidateDefinition(
                    staticCatalog.Definitions[0],
                    staticCatalog.ReferenceCatalog,
                    out string definitionReason),
                Is.True,
                definitionReason);
        }

        [Test]
        public void StaticCatalogValidatorRejectsUndeclaredDuplicateOrZeroVersionDirectories()
        {
            Assert.IsFalse(CharterRuleCatalogValidator.TryValidateDefinitions(
                Array.Empty<CharterRuleDefinitionData>(),
                BuildCatalog(),
                0,
                out string zeroVersionReason));
            Assert.AreEqual(CharterRuleCatalogReasons.VersionUndeclared, zeroVersionReason);

            Assert.IsFalse(CharterRuleCatalogValidator.TryValidateDefinitions(
                Array.Empty<CharterRuleDefinitionData>(),
                null,
                1,
                out string undeclaredReason));
            Assert.AreEqual(CharterRuleCatalogReasons.CatalogUndeclared, undeclaredReason);

            var duplicateNodeCatalog = BuildCatalog();
            duplicateNodeCatalog.nodeIds = new[] { "node_anchor", "node_anchor" };
            Assert.IsFalse(CharterRuleCatalogValidator.TryValidateCatalog(
                duplicateNodeCatalog,
                out string duplicateCatalogReason));
            Assert.AreEqual(CharterRuleCatalogReasons.DuplicateCatalogId, duplicateCatalogReason);

            var duplicateRuleEntries = new[]
            {
                ScriptableObject.CreateInstance<CharterRuleDefinitionData>(),
                ScriptableObject.CreateInstance<CharterRuleDefinitionData>(),
            };
            try
            {
                duplicateRuleEntries[0].ruleEntryId = "charter_fixture";
                duplicateRuleEntries[1].ruleEntryId = "charter_fixture";
                Assert.IsFalse(CharterRuleCatalogValidator.TryValidateDefinitions(
                    duplicateRuleEntries,
                    BuildCatalog(),
                    1,
                    out string duplicateEntryReason));
                Assert.AreEqual(CharterRuleCatalogReasons.DuplicateRuleEntryId, duplicateEntryReason);
            }
            finally
            {
                foreach (var definition in duplicateRuleEntries)
                    UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void StaticCatalogValidatorRejectsDuplicateAuthorityBoundaryCommitAndConflictRecords()
        {
            var duplicateAuthority = BuildCatalog();
            duplicateAuthority.authorityRequirements = new[]
            {
                new CharterAuthorityRequirement
                {
                    authorityId = "authority_water",
                    relicId = "relic_world_charter",
                    organizationAuthorizationVersionIds = new[] { "authorization_water_v1" },
                },
                new CharterAuthorityRequirement
                {
                    authorityId = "authority_water",
                    relicId = "relic_world_charter",
                    organizationAuthorizationVersionIds = new[] { "authorization_water_v1" },
                },
            };
            Assert.IsFalse(CharterRuleCatalogValidator.TryValidateCatalog(
                duplicateAuthority,
                out string authorityReason));
            Assert.AreEqual(CharterRuleCatalogReasons.DuplicateCatalogId, authorityReason);

            var duplicateBoundary = BuildCatalog();
            duplicateBoundary.propagationBoundaries = new[]
            {
                new CharterPropagationBoundaryReference
                {
                    propagationBoundaryProfileId = "boundary_watershed",
                    allowedCoverageIds = new[] { "coverage_anchor", "coverage_water" },
                },
                new CharterPropagationBoundaryReference
                {
                    propagationBoundaryProfileId = "boundary_watershed",
                    allowedCoverageIds = new[] { "coverage_anchor" },
                },
            };
            Assert.IsFalse(CharterRuleCatalogValidator.TryValidateCatalog(
                duplicateBoundary,
                out string boundaryReason));
            Assert.AreEqual(CharterRuleCatalogReasons.DuplicateCatalogId, boundaryReason);

            var duplicateCommit = BuildCatalog();
            duplicateCommit.commits = new[]
            {
                new CharterCommitReference { commitId = "commit_positive", realitySupplyIds = new[] { "supply_upstream" } },
                new CharterCommitReference { commitId = "commit_positive", realitySupplyIds = new[] { "supply_downstream" } },
            };
            Assert.IsFalse(CharterRuleCatalogValidator.TryValidateCatalog(
                duplicateCommit,
                out string commitReason));
            Assert.AreEqual(CharterRuleCatalogReasons.AtomicCommitIncomplete, commitReason);

            var duplicateConflict = BuildCatalog();
            duplicateConflict.conflicts = new[]
            {
                new CharterConflictReference
                {
                    conflictProfileId = "conflict_water",
                    crossTierChallengeGrantIds = new[] { "cross_tier_water_v1" },
                },
                new CharterConflictReference
                {
                    conflictProfileId = "conflict_water",
                    crossTierChallengeGrantIds = new[] { "cross_tier_water_v1" },
                },
            };
            Assert.IsFalse(CharterRuleCatalogValidator.TryValidateCatalog(
                duplicateConflict,
                out string conflictReason));
            Assert.AreEqual(CharterRuleCatalogReasons.DuplicateCatalogId, conflictReason);
        }

        [Test]
        public void ContentCatalogDataExposesOnlyTheSingleApprovedStaticCatalogFailClosed()
        {
            var without = ScriptableObject.CreateInstance<ContentCatalogData>();
            try
            {
                Assert.IsFalse(without.TryGetCharterRuleStaticCatalog(
                    out CharterRuleStaticCatalogData missing,
                    out string missingReason));
                Assert.IsNull(missing);
                Assert.IsFalse(string.IsNullOrEmpty(missingReason));

                CharterRuleStaticCatalogData production = LoadProductionStaticCatalog();
                without.SetCharterRuleStaticCatalog(production);
                Assert.IsTrue(without.TryGetCharterRuleStaticCatalog(
                    out CharterRuleStaticCatalogData resolved,
                    out string resolvedReason));
                Assert.AreSame(production, resolved);
                Assert.IsNull(resolvedReason);
                Assert.AreEqual(1, resolved.Definitions.Length);
                Assert.AreEqual("charter_entry_suifu_diji", resolved.Definitions[0].ruleEntryId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(without);
            }
        }

        [Test]
        public void DynamicStateRejectsNodesOrCoverageOutsideTheEntryDefinitionBoundary()
        {
            var definition = ScriptableObject.CreateInstance<CharterRuleDefinitionData>();
            definition.ruleEntryId = "charter_fixture";
            definition.anchorNodeIds = new[] { "node_anchor", "node_water" };
            definition.propagationBoundaryProfileId = "boundary_watershed";
            definition.currentCoverageSet = new[] { "coverage_anchor", "coverage_water" };
            definition.requiredAuthority = "authority_water";
            definition.positiveCommit = "commit_positive";
            definition.negativeCommit = "commit_negative";
            try
            {
                var catalog = BuildCatalog();
                catalog.nodeIds = new[] { "node_anchor", "node_water", "node_other" };
                catalog.propagationBoundaries = new[]
                {
                    new CharterPropagationBoundaryReference
                    {
                        propagationBoundaryProfileId = "boundary_watershed",
                        allowedCoverageIds = new[] { "coverage_anchor", "coverage_water" },
                    },
                    new CharterPropagationBoundaryReference
                    {
                        propagationBoundaryProfileId = "boundary_other",
                        allowedCoverageIds = new[] { "coverage_other" },
                    },
                };
                var state = new CharterRuntimeStateData
                {
                    stateId = "charter_runtime_boundary_fixture",
                    charterRelicState = "recognized",
                    worldSealState = "recognized",
                    registeredRuleEntryIds = new[] { "charter_fixture" },
                    nodeStates = new[]
                    {
                        new CharterNodeRuntimeStateData { nodeId = "node_anchor", state = "connected" },
                        new CharterNodeRuntimeStateData { nodeId = "node_water", state = "connected" },
                    },
                    currentCoverageSet = new[] { "coverage_anchor", "coverage_water" },
                    organizationAuthorizationVersions = new[]
                    {
                        new CharterAuthorizationVersionStateData
                        {
                            authorizationVersionId = "authorization_water_v1",
                            state = "recognized",
                        },
                    },
                    realitySupplyStates = new[]
                    {
                        new CharterRealitySupplyStateData
                        {
                            realitySupplyId = "supply_upstream",
                            state = "registered",
                        },
                    },
                };

                Assert.IsTrue(state.TryValidate(new[] { definition }, catalog, out var validReason), validReason);

                var extraNode = state.CreateCopy();
                extraNode.nodeStates = new[]
                {
                    new CharterNodeRuntimeStateData { nodeId = "node_anchor", state = "connected" },
                    new CharterNodeRuntimeStateData { nodeId = "node_water", state = "connected" },
                    new CharterNodeRuntimeStateData { nodeId = "node_other", state = "connected" },
                };
                Assert.IsFalse(extraNode.TryValidate(new[] { definition }, catalog, out var nodeReason));
                Assert.AreEqual(CharterRuntimeStateReasons.NodeOutsideEntryBoundary, nodeReason);

                var extraCoverage = state.CreateCopy();
                extraCoverage.currentCoverageSet =
                    new[] { "coverage_anchor", "coverage_water", "coverage_other" };
                Assert.IsFalse(extraCoverage.TryValidate(new[] { definition }, catalog, out var coverageReason));
                Assert.AreEqual(CharterRuntimeStateReasons.CoverageOutsideEntryBoundary, coverageReason);

                var missingAnchor = state.CreateCopy();
                missingAnchor.nodeStates = new[]
                {
                    new CharterNodeRuntimeStateData { nodeId = "node_anchor", state = "connected" },
                };
                Assert.IsFalse(missingAnchor.TryValidate(new[] { definition }, catalog, out var anchorReason));
                Assert.AreEqual(CharterRuntimeStateReasons.EntryAnchorMissing, anchorReason);

                var missingAuthorization = state.CreateCopy();
                missingAuthorization.organizationAuthorizationVersions = Array.Empty<CharterAuthorizationVersionStateData>();
                Assert.IsFalse(missingAuthorization.TryValidate(new[] { definition }, catalog, out var authorizationReason));
                Assert.AreEqual(CharterRuntimeStateReasons.AuthorizationRequirementMismatch, authorizationReason);

                var oneSidedCommit = state.CreateCopy();
                oneSidedCommit.positiveCommitResults = new[]
                {
                    new CharterCommitResultStateData { commitId = "commit_positive", resultState = "applied" },
                };
                Assert.IsFalse(oneSidedCommit.TryValidate(new[] { definition }, catalog, out var commitReason));
                Assert.AreEqual(CharterRuntimeStateReasons.CommitPairIncomplete, commitReason);

                var duplicateSupply = state.CreateCopy();
                duplicateSupply.realitySupplyStates = new[]
                {
                    new CharterRealitySupplyStateData { realitySupplyId = "supply_upstream", state = "registered" },
                    new CharterRealitySupplyStateData { realitySupplyId = "supply_upstream", state = "allocated" },
                };
                Assert.IsFalse(duplicateSupply.TryValidate(new[] { definition }, catalog, out var supplyReason));
                Assert.AreEqual(CharterRuntimeStateReasons.DuplicateRealitySupply, supplyReason);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void DynamicStateRejectsDuplicateNodeAuthorizationAndCoverageStableIds()
        {
            var definition = ScriptableObject.CreateInstance<CharterRuleDefinitionData>();
            definition.ruleEntryId = "charter_fixture";
            definition.anchorNodeIds = new[] { "node_anchor", "node_water" };
            definition.propagationBoundaryProfileId = "boundary_watershed";
            definition.currentCoverageSet = new[] { "coverage_anchor", "coverage_water" };
            definition.requiredAuthority = "authority_water";
            definition.positiveCommit = "commit_positive";
            definition.negativeCommit = "commit_negative";
            try
            {
                var catalog = BuildCatalog();
                var state = new CharterRuntimeStateData
                {
                    stateId = "charter_runtime_duplicate_fixture",
                    charterRelicState = "recognized",
                    worldSealState = "recognized",
                    registeredRuleEntryIds = new[] { "charter_fixture" },
                    nodeStates = new[]
                    {
                        new CharterNodeRuntimeStateData { nodeId = "node_anchor", state = "connected" },
                        new CharterNodeRuntimeStateData { nodeId = "node_water", state = "connected" },
                    },
                    currentCoverageSet = new[] { "coverage_anchor", "coverage_water" },
                    organizationAuthorizationVersions = new[]
                    {
                        new CharterAuthorizationVersionStateData
                        {
                            authorizationVersionId = "authorization_water_v1",
                            state = "recognized",
                        },
                    },
                };

                Assert.IsTrue(state.TryValidate(new[] { definition }, catalog, out var validReason), validReason);

                var duplicateNode = state.CreateCopy();
                duplicateNode.nodeStates = new[]
                {
                    new CharterNodeRuntimeStateData { nodeId = "node_anchor", state = "connected" },
                    new CharterNodeRuntimeStateData { nodeId = "node_anchor", state = "connected" },
                    new CharterNodeRuntimeStateData { nodeId = "node_water", state = "connected" },
                };
                Assert.IsFalse(duplicateNode.TryValidate(new[] { definition }, catalog, out var nodeReason));
                Assert.AreEqual(CharterRuntimeStateReasons.DuplicateNode, nodeReason);

                var duplicateAuthorization = state.CreateCopy();
                duplicateAuthorization.organizationAuthorizationVersions = new[]
                {
                    new CharterAuthorizationVersionStateData
                    {
                        authorizationVersionId = "authorization_water_v1",
                        state = "recognized",
                    },
                    new CharterAuthorizationVersionStateData
                    {
                        authorizationVersionId = "authorization_water_v1",
                        state = "recognized",
                    },
                };
                Assert.IsFalse(duplicateAuthorization.TryValidate(new[] { definition }, catalog, out var authorizationReason));
                Assert.AreEqual(CharterRuntimeStateReasons.DuplicateAuthorization, authorizationReason);

                var duplicateCoverage = state.CreateCopy();
                duplicateCoverage.currentCoverageSet =
                    new[] { "coverage_anchor", "coverage_anchor", "coverage_water" };
                Assert.IsFalse(duplicateCoverage.TryValidate(new[] { definition }, catalog, out var coverageReason));
                Assert.AreEqual(CharterRuntimeStateReasons.DuplicateCoverage, coverageReason);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        private static void AssertParseFails(
            string row,
            CharterRuleReferenceCatalog catalog,
            string expectedReason)
        {
            var exception = Assert.Throws<InvalidDataException>(() => WorldContentImporter.ParseCharterRuleDefinitions(
                new[] { Header, row },
                "CharterRuleDefinitions.csv",
                catalog));
            StringAssert.StartsWith(expectedReason + ":", exception.Message);
        }

        private static CharterRuleStaticCatalogData LoadProductionStaticCatalog()
        {
            var staticCatalog = AssetDatabase.LoadAssetAtPath<CharterRuleStaticCatalogData>(
                "Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset");
            Assert.IsNotNull(staticCatalog, "The single approved charter static catalog asset is missing.");
            return staticCatalog;
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
