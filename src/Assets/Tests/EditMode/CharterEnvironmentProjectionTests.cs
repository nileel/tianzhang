using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Adventure;
using TianZhang.Bootstrap;
using TianZhang.Content;
using TianZhang.World;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests
{
    /// <summary>
    /// Direct EditMode coverage of the single charter environment projection on the approved old
    /// water station: only the current-region effective 水府地纪 entry resolves through its declared
    /// event outputs to the serialized env_guanzhong_wild asset; unaccessed sessions, not-in-effect
    /// entries, duplicate/unknown entries, definition-level failures propagated from the single
    /// static catalog, duplicate environment IDs and asset profile mismatches all fail closed with
    /// stable reasons, and no outcome ever mutates the long-term state or an environment asset.
    /// </summary>
    public sealed class CharterEnvironmentProjectionTests
    {
        private const string SettlementId = "guanzhong_city";
        private const string CapabilityId = "capability_kaihe_jiuzhang_v1";
        private const string OperatorId = "operator_old_water_station";
        private const string TargetId = "gate_old_water_station_pump";
        private const string ManagerId = "manager_old_water_station";
        private const string BeneficiaryId = "beneficiary_water_basin";
        private const string RuleEntryId = "charter_entry_suifu_diji";
        private const string CharterRelicId = "relic_world_charter";
        private const string CharterNode = "node_old_water_station_charter";
        private const string WaterworksNode = "node_old_water_station_waterworks";
        private const string RiverWetlandNode = "node_old_water_station_river_wetland";
        private const string SupplyRain = "supply_suifu_registered_seasonal_rain";
        private const string SupplyBalance = "supply_suifu_connected_water_balance";
        private const string SupplyLand = "supply_suifu_wetland_land_capacity";

        private readonly List<UnityEngine.Object> temporaryAssets = new List<UnityEngine.Object>();
        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object asset in temporaryAssets)
                UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void CurrentRegionEntryResolvesToDeclaredEnvironmentProfileAndMatchesSerializedAsset()
        {
            GameRuntime runtimeOwner = CreateCommittedRuntime(out ContentCatalogData catalog);
            string[] entriesBefore = CaptureCurrentRegionEntries(runtimeOwner.Charters.CurrentState);

            Assert.That(CharterEnvironmentProjection.TryResolve(
                runtimeOwner.Charters.CurrentState,
                catalog,
                "env_guanzhong_wild",
                out CharterEnvironmentProjectionResult result), Is.True, result.Reason);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(CharterEnvironmentProjectionReasons.Ok, result.Reason);
            CollectionAssert.AreEqual(new[] { RuleEntryId }, result.RuleEntryIds);
            CollectionAssert.AreEqual(
                new[] { "event_suifu_water_redistribution", "event_suifu_downstream_supply_delay" },
                result.EventIds);
            Assert.AreEqual("env_guanzhong_wild", result.EnvironmentProfileId);
            AssertStateUntouched(entriesBefore, runtimeOwner.Charters.CurrentState);
        }

        [Test]
        public void UnaccessedSessionFailsClosedWithoutAnyEnvironmentReference()
        {
            ContentCatalogData catalog = CreateCatalogWith(LoadProductionStaticCatalog());

            Assert.That(CharterEnvironmentProjection.TryResolve(
                null, catalog, "env_guanzhong_wild", out CharterEnvironmentProjectionResult result), Is.False);
            Assert.AreEqual(CharterEnvironmentProjectionReasons.NoLongTermState, result.Reason);
            Assert.IsFalse(result.Succeeded);
        }

        [Test]
        public void RegisteredButNotCurrentRegionEntryProducesNoReference()
        {
            ContentCatalogData catalog = CreateCatalogWith(LoadProductionStaticCatalog());
            var state = new CharterRuntimeStateData
            {
                stateId = "charter_runtime_projection_fixture",
                registeredRuleEntryIds = new[] { RuleEntryId },
                currentRegionRuleEntryIds = null,
            };

            Assert.That(CharterEnvironmentProjection.TryResolve(
                state, catalog, "env_guanzhong_wild", out CharterEnvironmentProjectionResult result), Is.False);
            Assert.AreEqual(CharterEnvironmentProjectionReasons.NoCurrentRegionEntry, result.Reason);

            state.currentRegionRuleEntryIds = new string[0];
            Assert.That(CharterEnvironmentProjection.TryResolve(
                state, catalog, "env_guanzhong_wild", out result), Is.False);
            Assert.AreEqual(CharterEnvironmentProjectionReasons.NoCurrentRegionEntry, result.Reason);
        }

        [Test]
        public void DuplicateCurrentRegionEntriesFailClosed()
        {
            ContentCatalogData catalog = CreateCatalogWith(LoadProductionStaticCatalog());
            var state = new CharterRuntimeStateData
            {
                stateId = "charter_runtime_projection_fixture",
                currentRegionRuleEntryIds = new[] { RuleEntryId, RuleEntryId },
            };

            Assert.That(CharterEnvironmentProjection.TryResolve(
                state, catalog, "env_guanzhong_wild", out CharterEnvironmentProjectionResult result), Is.False);
            Assert.AreEqual(CharterEnvironmentProjectionReasons.DuplicateCurrentRegionEntry, result.Reason);
        }

        [Test]
        public void UnknownCurrentRegionEntryFailsClosed()
        {
            ContentCatalogData catalog = CreateCatalogWith(LoadProductionStaticCatalog());
            var state = new CharterRuntimeStateData
            {
                stateId = "charter_runtime_projection_fixture",
                currentRegionRuleEntryIds = new[] { "charter_entry_unknown" },
            };

            Assert.That(CharterEnvironmentProjection.TryResolve(
                state, catalog, "env_guanzhong_wild", out CharterEnvironmentProjectionResult result), Is.False);
            Assert.AreEqual(CharterEnvironmentProjectionReasons.UnknownRuleEntry, result.Reason);
        }

        [Test]
        public void DuplicateDefinitionsFailClosedThroughCatalogValidation()
        {
            CharterRuleDefinitionData definition = LoadProductionDefinition();
            CharterRuleStaticCatalogData staticCatalog = CreateStaticCatalog(
                LoadProductionStaticCatalog().DefinitionCatalogVersion,
                new[] { definition, definition },
                LoadProductionStaticCatalog().ReferenceCatalog);
            ContentCatalogData catalog = CreateCatalogWith(staticCatalog);
            var state = new CharterRuntimeStateData
            {
                stateId = "charter_runtime_projection_fixture",
                currentRegionRuleEntryIds = new[] { RuleEntryId },
            };

            Assert.That(CharterEnvironmentProjection.TryResolve(
                state, catalog, "env_guanzhong_wild", out CharterEnvironmentProjectionResult result), Is.False);
            StringAssert.StartsWith(CharterEnvironmentProjectionReasons.CatalogUnavailable, result.Reason);
            StringAssert.Contains(CharterRuleCatalogReasons.DuplicateRuleEntryId, result.Reason);
        }

        [Test]
        public void MissingEventOutputsFailClosedThroughCatalogValidation()
        {
            CharterRuleDefinitionData definition = CloneDefinition(LoadProductionDefinition());
            definition.worldEventOutputs = null;
            CharterRuleStaticCatalogData staticCatalog = CreateStaticCatalog(
                LoadProductionStaticCatalog().DefinitionCatalogVersion,
                new[] { definition },
                LoadProductionStaticCatalog().ReferenceCatalog);
            ContentCatalogData catalog = CreateCatalogWith(staticCatalog);
            var state = new CharterRuntimeStateData
            {
                stateId = "charter_runtime_projection_fixture",
                currentRegionRuleEntryIds = new[] { RuleEntryId },
            };

            Assert.That(CharterEnvironmentProjection.TryResolve(
                state, catalog, "env_guanzhong_wild", out CharterEnvironmentProjectionResult result), Is.False);
            StringAssert.StartsWith(CharterEnvironmentProjectionReasons.CatalogUnavailable, result.Reason);
            StringAssert.Contains(CharterRuleCatalogReasons.UnknownWorldEvent, result.Reason);
        }

        [Test]
        public void OutOfCatalogEnvironmentIdFailsClosedThroughCatalogValidation()
        {
            CharterRuleDefinitionData definition = CloneDefinition(LoadProductionDefinition());
            definition.worldEventOutputs = new[]
            {
                new CharterWorldEventOutputData
                {
                    eventId = "event_suifu_water_redistribution",
                    environmentProfileId = "env_unknown",
                },
            };
            CharterRuleStaticCatalogData staticCatalog = CreateStaticCatalog(
                LoadProductionStaticCatalog().DefinitionCatalogVersion,
                new[] { definition },
                LoadProductionStaticCatalog().ReferenceCatalog);
            ContentCatalogData catalog = CreateCatalogWith(staticCatalog);
            var state = new CharterRuntimeStateData
            {
                stateId = "charter_runtime_projection_fixture",
                currentRegionRuleEntryIds = new[] { RuleEntryId },
            };

            Assert.That(CharterEnvironmentProjection.TryResolve(
                state, catalog, "env_guanzhong_wild", out CharterEnvironmentProjectionResult result), Is.False);
            StringAssert.StartsWith(CharterEnvironmentProjectionReasons.CatalogUnavailable, result.Reason);
            StringAssert.Contains(CharterRuleCatalogReasons.UnknownEnvironmentProfile, result.Reason);
        }

        [Test]
        public void DuplicateEnvironmentIdsFailClosedWithAValidCatalog()
        {
            CharterRuleDefinitionData definition = CloneDefinition(LoadProductionDefinition());            definition.worldEventOutputs = new[]
            {
                new CharterWorldEventOutputData
                {
                    eventId = "event_suifu_water_redistribution",
                    environmentProfileId = "env_guanzhong_wild",
                },
                new CharterWorldEventOutputData
                {
                    eventId = "event_fixture_second",
                    environmentProfileId = "env_fixture_second",
                },
            };
            CharterRuleReferenceCatalog referenceCatalog = ExtendReferenceCatalog(
                new[] { "event_fixture_second" },
                new[] { "env_fixture_second" });
            CharterRuleStaticCatalogData staticCatalog = CreateStaticCatalog(
                LoadProductionStaticCatalog().DefinitionCatalogVersion,
                new[] { definition },
                referenceCatalog);
            ContentCatalogData catalog = CreateCatalogWith(staticCatalog);
            Assert.That(staticCatalog.TryValidateDefinitions(out string catalogReason), Is.True, catalogReason);

            var state = new CharterRuntimeStateData
            {
                stateId = "charter_runtime_projection_fixture",
                currentRegionRuleEntryIds = new[] { RuleEntryId },
            };
            Assert.That(CharterEnvironmentProjection.TryResolve(
                state, catalog, "env_guanzhong_wild", out CharterEnvironmentProjectionResult result), Is.False);
            Assert.AreEqual(CharterEnvironmentProjectionReasons.DuplicateEnvironmentId, result.Reason);
        }

        [Test]
        public void AssetProfileMismatchFailsClosedWithoutFallback()
        {
            GameRuntime runtimeOwner = CreateCommittedRuntime(out ContentCatalogData catalog);
            CharterRuntimeStateData state = runtimeOwner.Charters.CurrentState;

            Assert.That(CharterEnvironmentProjection.TryResolve(
                state, catalog, "env_other", out CharterEnvironmentProjectionResult result), Is.False);
            Assert.AreEqual(CharterEnvironmentProjectionReasons.AssetProfileMismatch, result.Reason);

            Assert.That(CharterEnvironmentProjection.TryResolve(
                state, catalog, null, out result), Is.False);
            Assert.AreEqual(CharterEnvironmentProjectionReasons.AssetProfileMismatch, result.Reason);

            Assert.That(CharterEnvironmentProjection.TryResolve(
                state, catalog, string.Empty, out result), Is.False);
            Assert.AreEqual(CharterEnvironmentProjectionReasons.AssetProfileMismatch, result.Reason);
            AssertStateUntouched(new[] { RuleEntryId }, runtimeOwner.Charters.CurrentState);
        }

        [Test]
        public void FailedOutcomesLeaveTheLongTermStateUntouched()
        {
            GameRuntime runtimeOwner = CreateCommittedRuntime(out ContentCatalogData catalog);
            string[] entriesBefore = CaptureCurrentRegionEntries(runtimeOwner.Charters.CurrentState);

            Assert.That(CharterEnvironmentProjection.TryResolve(
                runtimeOwner.Charters.CurrentState, catalog, "env_other", out _), Is.False);
            AssertStateUntouched(entriesBefore, runtimeOwner.Charters.CurrentState);
            Assert.IsNotNull(runtimeOwner.Charters.CurrentState);
        }

        private GameRuntime CreateCommittedRuntime(out ContentCatalogData catalog)
        {
            var runtimeOwner = new GameRuntime();
            Assert.IsNull(runtimeOwner.Charters.CurrentState);

            CharterSiteData site = LoadProductionSite();
            CharterRuleStaticCatalogData staticCatalog = LoadProductionStaticCatalog();
            Assert.That(CharterSiteInteractionRuntime.TryCreate(
                site, staticCatalog, SettlementId, out CharterSiteInteractionRuntime runtime, out string createReason),
                Is.True, createReason);
            CompleteAllSteps(runtime);
            Assert.That(runtime.TryCreatePreparation(out CharterInvocationPreparation preparation, out string prepReason),
                Is.True, prepReason);

            CharterRuleInvocationResult result = runtime.EvaluateFormal(
                preparation, null, 100, "applied", "applied");
            Assert.IsTrue(result.Succeeded, result.Reason);

            catalog = CreateCatalogWith(staticCatalog);
            CharterUseCaseResult commit = runtimeOwner.Charters.CommitEvaluatedState(
                catalog, result.NextState, preparation.CatalogVersion);
            Assert.IsTrue(commit.Succeeded, commit.Reason);
            Assert.IsNotNull(runtimeOwner.Charters.CurrentState);
            Assert.AreEqual(1, runtimeOwner.Charters.CurrentState.currentRegionRuleEntryIds.Length);
            Assert.AreEqual(RuleEntryId, runtimeOwner.Charters.CurrentState.currentRegionRuleEntryIds[0]);
            return runtimeOwner;
        }

        private void CompleteAllSteps(CharterSiteInteractionRuntime runtime)
        {
            AssertOk(runtime.VerifyPassage(CapabilityId, OperatorId, TargetId));
            AssertOk(runtime.VerifyManagement(ManagerId, BeneficiaryId));
            AssertOk(runtime.ConnectNodes(new[] { CharterNode, WaterworksNode, RiverWetlandNode }));
            AssertOk(runtime.VerifyRuleEntryRegistration(
                RuleEntryId,
                CharterRelicId,
                new[] { "authorization_suifu_water_basin_v1", "authorization_taixuan_seal_old_water_station_management_v1" }));
            AssertOk(runtime.PrepareRealitySupplies(new[] { SupplyRain, SupplyBalance, SupplyLand }));
        }

        private static void AssertOk(CharterInteractionActionResult result)
        {
            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.AreEqual(CharterSiteInteractionReasons.Ok, result.Reason);
        }

        private ContentCatalogData CreateCatalogWith(CharterRuleStaticCatalogData staticCatalog)
        {
            ContentCatalogData catalog = Track(ScriptableObject.CreateInstance<ContentCatalogData>());
            catalog.SetCharterRuleStaticCatalog(staticCatalog);
            return catalog;
        }

        private static CharterRuleDefinitionData LoadProductionDefinition()
        {
            CharterRuleStaticCatalogData staticCatalog = LoadProductionStaticCatalog();
            Assert.AreEqual(1, staticCatalog.Definitions.Length);
            return staticCatalog.Definitions[0];
        }

        private static CharterRuleStaticCatalogData LoadProductionStaticCatalog()
        {
            var staticCatalog = AssetDatabase.LoadAssetAtPath<CharterRuleStaticCatalogData>(
                "Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset");
            Assert.IsNotNull(staticCatalog, "The single approved charter static catalog asset is missing.");
            return staticCatalog;
        }

        private static CharterSiteData LoadProductionSite()
        {
            var site = AssetDatabase.LoadAssetAtPath<CharterSiteData>(
                "Assets/Data/CharterSites/CharterSite_charter_site_old_water_station.asset");
            Assert.IsNotNull(site, "The single approved charter site asset is missing.");
            return site;
        }

        private CharterRuleDefinitionData CloneDefinition(CharterRuleDefinitionData source)
        {
            var copy = Track(ScriptableObject.CreateInstance<CharterRuleDefinitionData>());
            copy.ruleEntryId = source.ruleEntryId;
            copy.displayName = source.displayName;
            copy.ruleFamily = source.ruleFamily;
            copy.relationElement = source.relationElement;
            copy.compatiblePhenomena = source.compatiblePhenomena;
            copy.positiveCommit = source.positiveCommit;
            copy.negativeCommit = source.negativeCommit;
            copy.requiredAuthority = source.requiredAuthority;
            copy.requiredNodeTypes = source.requiredNodeTypes;
            copy.scopeType = source.scopeType;
            copy.scopeTierCap = source.scopeTierCap;
            copy.anchorNodeIds = source.anchorNodeIds;
            copy.propagationBoundaryProfileId = source.propagationBoundaryProfileId;
            copy.currentCoverageSet = source.currentCoverageSet;
            copy.affectedWorldVariables = source.affectedWorldVariables;
            copy.conflictProfileId = source.conflictProfileId;
            copy.failurePolicy = source.failurePolicy;
            copy.worldEventOutputs = source.worldEventOutputs;
            return copy;
        }

        private static CharterRuleReferenceCatalog ExtendReferenceCatalog(
            string[] additionalEventIds,
            string[] additionalEnvironmentProfileIds)
        {
            CharterRuleReferenceCatalog production = LoadProductionStaticCatalog().ReferenceCatalog;
            return new CharterRuleReferenceCatalog
            {
                displayNameKeys = production.displayNameKeys,
                ruleFamilyIds = production.ruleFamilyIds,
                relationElementIds = production.relationElementIds,
                phenomenonIds = production.phenomenonIds,
                relicIds = production.relicIds,
                organizationAuthorizationVersionIds = production.organizationAuthorizationVersionIds,
                authorityRequirements = production.authorityRequirements,
                nodeTypeIds = production.nodeTypeIds,
                nodeIds = production.nodeIds,
                propagationBoundaries = production.propagationBoundaries,
                realitySupplyIds = production.realitySupplyIds,
                commits = production.commits,
                worldVariableIds = production.worldVariableIds,
                conflicts = production.conflicts,
                worldEventIds = Append(production.worldEventIds, additionalEventIds),
                environmentProfileIds = Append(production.environmentProfileIds, additionalEnvironmentProfileIds),
                ruleEntryIds = production.ruleEntryIds,
            };
        }

        private static string[] Append(string[] source, string[] additional)
        {
            var combined = new string[source.Length + additional.Length];
            System.Array.Copy(source, combined, source.Length);
            System.Array.Copy(additional, 0, combined, source.Length, additional.Length);
            return combined;
        }

        private CharterRuleStaticCatalogData CreateStaticCatalog(
            int version,
            CharterRuleDefinitionData[] definitions,
            CharterRuleReferenceCatalog referenceCatalog)
        {
            var catalog = Track(ScriptableObject.CreateInstance<CharterRuleStaticCatalogData>());
            var type = typeof(CharterRuleStaticCatalogData);
            type.GetField("definitionCatalogVersion", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(catalog, version);
            type.GetField("definitions", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(catalog, definitions);
            type.GetField("referenceCatalog", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(catalog, referenceCatalog);
            return catalog;
        }

        private static string[] CaptureCurrentRegionEntries(CharterRuntimeStateData state)
        {
            return state == null ? null : (string[])state.currentRegionRuleEntryIds.Clone();
        }

        private static void AssertStateUntouched(string[] entriesBefore, CharterRuntimeStateData state)
        {
            Assert.IsNotNull(state);
            CollectionAssert.AreEqual(entriesBefore, state.currentRegionRuleEntryIds);
        }

        private T Track<T>(T value)
            where T : UnityEngine.Object
        {
            temporaryAssets.Add(value);
            return value;
        }
    }
}
