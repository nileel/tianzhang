using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Direct EditMode coverage of the single approved charter site data contract: the unique
    /// production row, full field projection, cross-table resolution, the shared conflict decision
    /// (the charter side deterministically does not win), per-category fail-closed fixtures, the
    /// production asset/catalog wiring and the yuanying sample isolation.
    /// </summary>
    public sealed class CharterSiteDataTests
    {
        private const string ProductionSiteId = "charter_site_old_water_station";
        private const string SiteAssetPath = "Assets/Data/CharterSites/CharterSite_charter_site_old_water_station.asset";

        private static readonly string[] Columns =
        {
            "siteId", "displayNameKey", "settlementId",
            "passageCapabilityId", "passageOperatorId", "passageTargetId", "passageProtocolState",
            "passageStructureState", "passagePowerState", "interactionTimeProfileId", "recognitionTiming",
            "operationTiming", "cancellationPolicy",
            "facilityId", "sealRelicId", "sealManagerId", "sealBeneficiaryId", "sealAuthorizationVersionId",
            "ruleEntryId", "ruleEntryOccupancyId", "nodeOccupancyId",
            "jindanConflictEventId", "jindanChallengeEventId",
            "grantId", "grantDefinitionVersion", "grantTargetVariableId", "grantChallengerId",
            "grantQualificationSource", "grantAllowedOperationId", "grantTargetId", "grantScopeId",
            "grantBeneficiaryId", "grantRealityAnchorId", "grantResourceLedgerRef", "grantCapacityLedgerRef",
            "grantChallengeRuleTier", "grantEffectiveAtTick", "grantExpiresAtTick", "grantIsRevoked",
            "grantRevocationReason", "grantDisplaySource",
            "leftCandidateId", "leftCandidateTargetVariableId", "leftCandidateTargetId",
            "leftCandidateHasVariableAuthority", "leftCandidateHasLegalTarget", "leftCandidatePositionRank",
            "leftCandidateRealityAnchorRank", "leftCandidateAlreadyPaidCost",
            "leftCandidateHasActiveContinuousCarrier", "leftCandidateConflictReserve", "leftCandidatePulseCost",
            "leftCandidateSettlementCooldown",
            "rightCandidateId", "rightCandidateTargetVariableId", "rightCandidateTargetId",
            "rightCandidateHasVariableAuthority", "rightCandidateHasLegalTarget", "rightCandidatePositionRank",
            "rightCandidateRealityAnchorRank", "rightCandidateAlreadyPaidCost",
            "rightCandidateHasActiveContinuousCarrier", "rightCandidateConflictReserve", "rightCandidatePulseCost",
            "rightCandidateSettlementCooldown",
            "charterCandidateId",
            "yuanyingConflictEventId", "yuanyingTargetVariableId", "yuanyingTargetId", "yuanyingScopeId",
            "yuanyingRealityAnchorId",
        };

        private const string ValidRow =
            "charter_site_old_water_station,charter_site_old_water_station,guanzhong_city," +
            "capability_kaihe_jiuzhang_v1,operator_fixture,gate_fixture,compatible,intact,available," +
            "interaction_time_old_water_station_gate_v1,instant,sustained_guided,no_commit_on_cancel," +
            "facility_fixture,relic_taixuan_realm_seal,manager_fixture,beneficiary_fixture,authorization_seal_management_v1," +
            "charter_fixture,occupancy_fixture_entry,occupancy_fixture_node," +
            "conflict_fixture_001,challenge_fixture_001," +
            "cross_tier_water_v1,1,var_water_spirit,challenger_fixture,JindanProtection,charter_apply," +
            "node_water,scope_fixture_basin,beneficiary_fixture,anchor_fixture_waterway," +
            "ledger_fixture_resource,ledger_fixture_capacity,1,0,500,false,none,charter_site_old_water_station," +
            "jindan_left,var_water_spirit,node_water,true,true,3,1,2,true,6,2,3," +
            "jindan_right,var_water_spirit,node_water,true,true,2,1,2,true,6,2,3," +
            "jindan_right," +
            "anchor_fixture_001,var_wetland_waterline,node_river_wetland,scope_fixture_basin,anchor_fixture_road";

        private static string Header => string.Join(",", Columns);

        private static Dictionary<string, string> BuildLanguage()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "charter_site_old_water_station", "旧水驿" },
            };
        }

        private static CharterRuleDefinitionData BuildDefinition()
        {
            var definition = ScriptableObject.CreateInstance<CharterRuleDefinitionData>();
            definition.ruleEntryId = "charter_fixture";
            definition.conflictProfileId = "conflict_water";
            return definition;
        }

        private static CharterRuleReferenceCatalog BuildCatalog()
        {
            return new CharterRuleReferenceCatalog
            {
                displayNameKeys = new[] { "display_charter_fixture" },
                ruleFamilyIds = new[] { "rule_family_five_element" },
                relationElementIds = new[] { "element_water" },
                phenomenonIds = new[] { "phenomenon_rain", "phenomenon_drizzle" },
                relicIds = new[] { "relic_world_charter", "relic_taixuan_realm_seal" },
                organizationAuthorizationVersionIds =
                    new[] { "authorization_water_v1", "authorization_seal_management_v1" },
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
                nodeIds = new[] { "node_anchor", "node_water", "node_river_wetland" },
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
                worldVariableIds = new[] { "var_precipitation", "var_water_spirit", "var_wetland_waterline" },
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

        [Test]
        public void ParseCharterSitesProjectsEveryContractField()
        {
            var sites = DataConfigImporter.ParseCharterSites(
                new[] { Header, ValidRow },
                "CharterSites.csv",
                BuildLanguage(),
                BuildCatalog(),
                new[] { BuildDefinition() });
            try
            {
                var site = sites.Single();
                Assert.AreEqual(32, typeof(CharterSiteData)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Length);
                Assert.AreEqual(18, typeof(CharterSiteCrossTierChallengeGrantData)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Length);
                Assert.AreEqual(12, typeof(CharterSiteRuleConflictCandidateData)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Length);

                Assert.AreEqual(ProductionSiteId, site.siteId);
                Assert.AreEqual("charter_site_old_water_station", site.displayNameKey);
                Assert.AreEqual("guanzhong_city", site.settlementId);
                Assert.AreEqual("capability_kaihe_jiuzhang_v1", site.passageCapabilityId);
                Assert.AreEqual("operator_fixture", site.passageOperatorId);
                Assert.AreEqual("gate_fixture", site.passageTargetId);
                Assert.AreEqual("compatible", site.passageProtocolState);
                Assert.AreEqual("intact", site.passageStructureState);
                Assert.AreEqual("available", site.passagePowerState);
                Assert.AreEqual("interaction_time_old_water_station_gate_v1", site.interactionTimeProfileId);
                Assert.AreEqual("instant", site.recognitionTiming);
                Assert.AreEqual("sustained_guided", site.operationTiming);
                Assert.AreEqual("no_commit_on_cancel", site.cancellationPolicy);
                Assert.AreEqual("facility_fixture", site.facilityId);
                Assert.AreEqual("relic_taixuan_realm_seal", site.sealRelicId);
                Assert.AreEqual("manager_fixture", site.sealManagerId);
                Assert.AreEqual("beneficiary_fixture", site.sealBeneficiaryId);
                Assert.AreEqual("authorization_seal_management_v1", site.sealAuthorizationVersionId);
                Assert.AreEqual("charter_fixture", site.ruleEntryId);
                Assert.AreEqual("occupancy_fixture_entry", site.ruleEntryOccupancyId);
                Assert.AreEqual("occupancy_fixture_node", site.nodeOccupancyId);
                Assert.AreEqual("conflict_fixture_001", site.jindanConflictEventId);
                Assert.AreEqual("challenge_fixture_001", site.jindanChallengeEventId);

                var grant = site.jindanGrant;
                Assert.IsNotNull(grant);
                Assert.AreEqual("cross_tier_water_v1", grant.grantId);
                Assert.AreEqual(1, grant.definitionVersion);
                Assert.AreEqual("var_water_spirit", grant.targetVariableId);
                Assert.AreEqual("challenger_fixture", grant.challengerId);
                Assert.AreEqual("JindanProtection", grant.qualificationSource);
                Assert.AreEqual("charter_apply", grant.allowedOperationId);
                Assert.AreEqual("node_water", grant.targetId);
                Assert.AreEqual("scope_fixture_basin", grant.scopeId);
                Assert.AreEqual("beneficiary_fixture", grant.beneficiaryId);
                Assert.AreEqual("anchor_fixture_waterway", grant.realityAnchorId);
                Assert.AreEqual("ledger_fixture_resource", grant.resourceLedgerRef);
                Assert.AreEqual("ledger_fixture_capacity", grant.capacityLedgerRef);
                Assert.AreEqual(1, grant.challengeRuleTier);
                Assert.AreEqual(0, grant.effectiveAtTick);
                Assert.AreEqual(500, grant.expiresAtTick);
                Assert.IsFalse(grant.isRevoked);
                Assert.AreEqual(string.Empty, grant.revocationReason);
                Assert.AreEqual(ProductionSiteId, grant.displaySource);

                AssertCandidate(site.leftCandidate, "jindan_left", 3);
                AssertCandidate(site.rightCandidate, "jindan_right", 2);
                Assert.AreEqual("jindan_right", site.charterCandidateId);

                Assert.AreEqual("anchor_fixture_001", site.yuanyingConflictEventId);
                Assert.AreEqual("var_wetland_waterline", site.yuanyingTargetVariableId);
                Assert.AreEqual("node_river_wetland", site.yuanyingTargetId);
                Assert.AreEqual("scope_fixture_basin", site.yuanyingScopeId);
                Assert.AreEqual("anchor_fixture_road", site.yuanyingRealityAnchorId);
            }
            finally
            {
                DestroyAll(sites);
            }
        }

        private static void AssertCandidate(CharterSiteRuleConflictCandidateData candidate, string id, int rank)
        {
            Assert.IsNotNull(candidate);
            Assert.AreEqual(id, candidate.candidateId);
            Assert.AreEqual("var_water_spirit", candidate.targetVariableId);
            Assert.AreEqual("node_water", candidate.targetId);
            Assert.IsTrue(candidate.hasVariableAuthority);
            Assert.IsTrue(candidate.hasLegalTarget);
            Assert.AreEqual(rank, candidate.positionRank);
            Assert.AreEqual(1, candidate.realityAnchorRank);
            Assert.AreEqual(2, candidate.alreadyPaidCost);
            Assert.IsTrue(candidate.hasActiveContinuousCarrier);
            Assert.AreEqual(6, candidate.conflictReserve);
            Assert.AreEqual(2, candidate.pulseCost);
            Assert.AreEqual(3, candidate.settlementCooldown);
        }

        [TestCase(2, "settlement_unknown", "CHARTER_SITE_UNKNOWN_SETTLEMENT")]
        [TestCase(14, "relic_unknown", "CHARTER_SITE_UNKNOWN_RELIC_REFERENCE")]
        [TestCase(17, "authorization_unknown", "CHARTER_SITE_UNKNOWN_AUTHORIZATION_REFERENCE")]
        [TestCase(18, "charter_entry_unknown", "CHARTER_SITE_UNKNOWN_RULE_ENTRY_REFERENCE")]
        [TestCase(25, "variable_unknown", "CHARTER_SITE_UNKNOWN_WORLD_VARIABLE_REFERENCE")]
        [TestCase(29, "node_unknown", "CHARTER_SITE_UNKNOWN_NODE_REFERENCE")]
        [TestCase(23, "cross_tier_unknown_v1", "CHARTER_SITE_UNKNOWN_GRANT_REFERENCE")]
        [TestCase(67, "variable_unknown", "CHARTER_SITE_UNKNOWN_WORLD_VARIABLE_REFERENCE")]
        [TestCase(68, "node_unknown", "CHARTER_SITE_UNKNOWN_NODE_REFERENCE")]
        public void ParseCharterSitesRejectsUnknownOrOutOfBoundaryReferences(
            int changedColumn,
            string changedValue,
            string expectedReason)
        {
            var values = ValidRow.Split(',');
            values[changedColumn] = changedValue;

            AssertParseFails(string.Join(",", values), expectedReason);
        }

        [TestCase(3, "capability_kaihe_other_v1", "CHARTER_SITE_CAPABILITY_MISMATCH")]
        [TestCase(6, "incompatible", "CHARTER_SITE_GATE_NOT_OPERABLE")]
        [TestCase(7, "damaged", "CHARTER_SITE_GATE_NOT_OPERABLE")]
        [TestCase(8, "unavailable", "CHARTER_SITE_GATE_NOT_OPERABLE")]
        [TestCase(9, "interaction_time_other_v1", "CHARTER_SITE_TIME_PROFILE_MISMATCH")]
        [TestCase(10, "delayed", "CHARTER_SITE_TIMING_SEMANTICS_INVALID")]
        [TestCase(11, "interruptible", "CHARTER_SITE_TIMING_SEMANTICS_INVALID")]
        [TestCase(12, "commit_on_cancel", "CHARTER_SITE_TIMING_SEMANTICS_INVALID")]
        [TestCase(15, "operator_fixture", "CHARTER_SITE_MANAGER_INVALID")]
        public void ParseCharterSitesRejectsFixedSemanticsViolations(
            int changedColumn,
            string changedValue,
            string expectedReason)
        {
            var values = ValidRow.Split(',');
            values[changedColumn] = changedValue;

            AssertParseFails(string.Join(",", values), expectedReason);
        }

        [TestCase(31, "beneficiary_other", "CHARTER_SITE_GRANT_INVALID")]
        [TestCase(27, "UnknownSource", "CHARTER_SITE_GRANT_INVALID")]
        [TestCase(24, "0", "CHARTER_SITE_GRANT_INVALID")]
        [TestCase(37, "-1", "CHARTER_SITE_GRANT_INVALID")]
        [TestCase(38, "true", "CHARTER_SITE_GRANT_INVALID")]
        [TestCase(42, "var_precipitation", "CHARTER_SITE_CANDIDATE_MISMATCH")]
        [TestCase(51, "0", "CHARTER_SITE_CANDIDATE_INVALID")]
        public void ParseCharterSitesRejectsInvalidGrantOrCandidateFields(
            int changedColumn,
            string changedValue,
            string expectedReason)
        {
            var values = ValidRow.Split(',');
            values[changedColumn] = changedValue;

            AssertParseFails(string.Join(",", values), expectedReason);
        }

        [TestCase(65, "jindan_left", "CHARTER_SITE_CONFLICT_NOT_STABLE")]
        [TestCase(65, "candidate_not_participating", "CHARTER_SITE_CHARTER_SIDE_UNDECLARED")]
        [TestCase(58, "4", "CHARTER_SITE_CONFLICT_NOT_STABLE")]
        [TestCase(46, "2", "CHARTER_SITE_CONFLICT_NOT_STABLE")]
        [TestCase(53, "jindan_left", "CHARTER_SITE_CHARTER_SIDE_UNDECLARED")]
        public void ParseCharterSitesRejectsUnstableOrAmbiguousCharterSideOutcomes(
            int changedColumn,
            string changedValue,
            string expectedReason)
        {
            var values = ValidRow.Split(',');
            values[changedColumn] = changedValue;

            AssertParseFails(string.Join(",", values), expectedReason);
        }

        [Test]
        public void ParseCharterSitesRequiresExactlyOneProductionSiteRow()
        {
            var duplicate = Assert.Throws<InvalidDataException>(() => DataConfigImporter.ParseCharterSites(
                new[] { Header, ValidRow, ValidRow },
                "CharterSites.csv",
                BuildLanguage(),
                BuildCatalog(),
                new[] { BuildDefinition() }));
            StringAssert.StartsWith("CHARTER_SITE_NOT_UNIQUE:", duplicate.Message);

            var wrongId = ValidRow.Split(',');
            wrongId[0] = "charter_site_other";
            AssertParseFails(string.Join(",", wrongId), "CHARTER_SITE_NOT_UNIQUE");
        }

        [Test]
        public void ParseCharterSitesRejectsUnknownLanguageKey()
        {
            var values = ValidRow.Split(',');
            values[1] = "display_key_missing";

            var exception = Assert.Throws<InvalidDataException>(() => DataConfigImporter.ParseCharterSites(
                new[] { Header, string.Join(",", values) },
                "CharterSites.csv",
                BuildLanguage(),
                BuildCatalog(),
                new[] { BuildDefinition() }));
            StringAssert.Contains("not present in Language.csv", exception.Message);
        }

        [Test]
        public void ProductionCharterSiteImportsTheApprovedOldWaterStationAndProjectsItsAsset()
        {
            const string sourceAssetPath = "Assets/DataConfig/CharterSites.csv";
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/CharterSites.csv");
            CharterRuleStaticCatalogData staticCatalog = LoadProductionStaticCatalog();
            Assert.That(staticCatalog.TryValidateDefinitions(out string staticReason), Is.True, staticReason);
            var language = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "charter_site_old_water_station", "旧水驿" },
            };
            var sites = DataConfigImporter.ParseCharterSites(
                File.ReadAllLines(sourceFilePath),
                sourceFilePath,
                language,
                staticCatalog.ReferenceCatalog,
                staticCatalog.Definitions);

            try
            {
                var site = sites.Single();
                Assert.AreEqual(ProductionSiteId, site.siteId);
                Assert.AreEqual("guanzhong_city", site.settlementId);
                Assert.AreEqual("capability_kaihe_jiuzhang_v1", site.passageCapabilityId);
                Assert.AreEqual("interaction_time_old_water_station_gate_v1", site.interactionTimeProfileId);
                Assert.AreEqual("charter_entry_suifu_diji", site.ruleEntryId);
                Assert.AreEqual("cross_tier_charter_water_basin_v1", site.jindanGrant.grantId);
                Assert.AreEqual(1, site.jindanGrant.definitionVersion);
                Assert.AreEqual("charter_site_old_water_station", site.jindanGrant.displaySource);
                Assert.AreEqual("jindan_right", site.charterCandidateId);
                Assert.AreEqual("jindan_left", site.leftCandidate.candidateId);
                Assert.AreEqual("jindan_right", site.rightCandidate.candidateId);
                Assert.AreEqual("wetland_waterline_state", site.yuanyingTargetVariableId);
                Assert.AreEqual("node_old_water_station_river_wetland", site.yuanyingTargetId);
            }
            finally
            {
                DestroyAll(sites);
            }

            DataConfigImporter.ImportCharterSites();
            AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadAssetAtPath<CharterSiteData>(SiteAssetPath);
            Assert.IsNotNull(asset);
            Assert.AreEqual(ProductionSiteId, asset.siteId);
            Assert.AreEqual("guanzhong_city", asset.settlementId);
            Assert.AreEqual("capability_kaihe_jiuzhang_v1", asset.passageCapabilityId);
            Assert.AreEqual("interaction_time_old_water_station_gate_v1", asset.interactionTimeProfileId);
            Assert.AreEqual("cross_tier_charter_water_basin_v1", asset.jindanGrant.grantId);
            Assert.AreEqual("jindan_right", asset.charterCandidateId);
            Assert.AreEqual("wetland_waterline_state", asset.yuanyingTargetVariableId);
        }

        [Test]
        public void ProductionCharterSiteSharedDecisionProvesCharterSideNotWon()
        {
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/CharterSites.csv");
            CharterRuleStaticCatalogData staticCatalog = LoadProductionStaticCatalog();
            var sites = DataConfigImporter.ParseCharterSites(
                File.ReadAllLines(sourceFilePath),
                sourceFilePath,
                BuildLanguage(),
                staticCatalog.ReferenceCatalog,
                staticCatalog.Definitions);
            try
            {
                var site = sites.Single();
                var grant = new CrossTierChallengeGrant(
                    site.jindanGrant.grantId,
                    site.jindanGrant.definitionVersion,
                    site.jindanGrant.targetVariableId,
                    site.jindanGrant.challengerId,
                    CrossTierChallengeSourceKind.JindanProtection,
                    site.jindanGrant.allowedOperationId,
                    site.jindanGrant.targetId,
                    site.jindanGrant.scopeId,
                    site.jindanGrant.beneficiaryId,
                    site.jindanGrant.realityAnchorId,
                    site.jindanGrant.resourceLedgerRef,
                    site.jindanGrant.capacityLedgerRef,
                    site.jindanGrant.challengeRuleTier,
                    site.jindanGrant.effectiveAtTick,
                    site.jindanGrant.expiresAtTick,
                    site.jindanGrant.isRevoked,
                    site.jindanGrant.revocationReason,
                    site.jindanGrant.displaySource);
                var archive = new CrossTierChallengeArchive(new[] { grant });
                var request = new CrossTierChallengeRequest(
                    site.jindanChallengeEventId,
                    grant.GrantId,
                    grant.DefinitionVersion,
                    grant.TargetVariableId,
                    grant.ChallengerId,
                    grant.EffectiveAtTick);
                var instance = new RuleConflictInstance(
                    RuleConflictInstance.ContractVersionV1,
                    site.jindanConflictEventId,
                    RuleConflictKind.JindanSameVariable,
                    site.ruleEntryId,
                    grant.TargetVariableId,
                    grant.AllowedOperationId,
                    grant.TargetId,
                    grant.ScopeId,
                    grant.BeneficiaryId,
                    grant.RealityAnchorId,
                    grant.ResourceLedgerRef,
                    grant.CapacityLedgerRef,
                    grant.EffectiveAtTick,
                    BuildConflictCandidate(site.leftCandidate),
                    BuildConflictCandidate(site.rightCandidate),
                    request);

                var decision = instance.Decide(archive);

                Assert.IsNotNull(decision.CrossTierAuthorization);
                Assert.IsTrue(decision.CrossTierAuthorization.IsEligible);
                Assert.AreEqual(RuleConflictOutcome.LeftWins, decision.Outcome);
                Assert.AreEqual("jindan_left", decision.WinnerCandidateId);
                Assert.AreNotEqual(site.charterCandidateId, decision.WinnerCandidateId);
                // 运行时把败方稳定映射为未获胜：册界候选不是确定性赢家，不提交规则状态或事件。
                Assert.AreEqual("charter_conflict_not_won", CharterRuleRuntimeReasons.ConflictNotWon);
            }
            finally
            {
                DestroyAll(sites);
            }
        }

        [Test]
        public void YuanyingSampleCarriesOnlyAnchoringIdentityWithoutJindanCandidatesOrGrant()
        {
            var sites = DataConfigImporter.ParseCharterSites(
                new[] { Header, ValidRow },
                "CharterSites.csv",
                BuildLanguage(),
                BuildCatalog(),
                new[] { BuildDefinition() });
            try
            {
                var site = sites.Single();
                // 元婴样例字段全部是稳定 ID 字符串，不夹带金丹候选、grant 或可覆盖结果。
                var yuanyingFields = typeof(CharterSiteData)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(field => field.Name.StartsWith("yuanying", StringComparison.Ordinal))
                    .ToArray();
                Assert.AreEqual(5, yuanyingFields.Length);
                Assert.IsTrue(yuanyingFields.All(field => field.FieldType == typeof(string)));

                Assert.AreNotEqual(site.jindanGrant.targetVariableId, site.yuanyingTargetVariableId);
                Assert.AreNotEqual(site.jindanGrant.targetId, site.yuanyingTargetId);
                Assert.IsFalse(string.Equals(site.yuanyingConflictEventId, site.jindanConflictEventId, StringComparison.Ordinal));
            }
            finally
            {
                DestroyAll(sites);
            }
        }

        [Test]
        public void ContentCatalogDataExposesOnlyTheSingleApprovedCharterSiteFailClosed()
        {
            var without = ScriptableObject.CreateInstance<ContentCatalogData>();
            try
            {
                Assert.IsFalse(without.TryGetCharterSite(ProductionSiteId, out CharterSiteData missing));
                Assert.IsNull(missing);

                CharterSiteData production = AssetDatabase.LoadAssetAtPath<CharterSiteData>(SiteAssetPath);
                Assert.IsNotNull(production, "The single approved charter site asset is missing.");
                without.SetCharterSites(new[] { production });
                Assert.IsTrue(without.TryGetCharterSite(ProductionSiteId, out CharterSiteData resolved));
                Assert.AreSame(production, resolved);
                Assert.IsFalse(without.TryGetCharterSite("charter_site_other", out CharterSiteData unknown));
                Assert.IsNull(unknown);

                var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                    "Assets/Data/ContentCatalog/ContentCatalog.asset");
                Assert.IsNotNull(catalog);
                Assert.IsTrue(catalog.TryGetCharterSite(ProductionSiteId, out CharterSiteData fromCatalog));
                Assert.AreEqual(ProductionSiteId, fromCatalog.siteId);
                Assert.IsTrue(catalog.TryGetCharterRuleStaticCatalog(
                    out CharterRuleStaticCatalogData staticCatalog,
                    out string catalogReason), catalogReason);
                Assert.AreEqual(ProductionSiteId, fromCatalog.jindanGrant.displaySource);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(without);
            }
        }

        private static RuleConflictCandidate BuildConflictCandidate(CharterSiteRuleConflictCandidateData data)
        {
            return new RuleConflictCandidate(
                data.candidateId,
                data.targetVariableId,
                data.targetId,
                data.hasVariableAuthority,
                data.hasLegalTarget,
                data.positionRank,
                data.realityAnchorRank,
                data.alreadyPaidCost,
                data.hasActiveContinuousCarrier,
                data.conflictReserve,
                data.pulseCost,
                data.settlementCooldown);
        }

        private static void AssertParseFails(string row, string expectedReason)
        {
            var exception = Assert.Throws<InvalidDataException>(() => DataConfigImporter.ParseCharterSites(
                new[] { Header, row },
                "CharterSites.csv",
                BuildLanguage(),
                BuildCatalog(),
                new[] { BuildDefinition() }));
            StringAssert.StartsWith(expectedReason + ":", exception.Message);
        }

        private static CharterRuleStaticCatalogData LoadProductionStaticCatalog()
        {
            var staticCatalog = AssetDatabase.LoadAssetAtPath<CharterRuleStaticCatalogData>(
                "Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset");
            Assert.IsNotNull(staticCatalog, "The single approved charter static catalog asset is missing.");
            return staticCatalog;
        }

        private static void DestroyAll(CharterSiteData[] sites)
        {
            foreach (var site in sites ?? Array.Empty<CharterSiteData>())
            {
                if (site != null)
                    UnityEngine.Object.DestroyImmediate(site);
            }
        }
    }
}
