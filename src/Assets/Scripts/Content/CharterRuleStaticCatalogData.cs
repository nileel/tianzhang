using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TianZhang.Content
{
    /// <summary>
    /// The single approved production static charter directory. It holds direct asset references
    /// to the imported <see cref="CharterRuleDefinitionData"/> definitions and one approved,
    /// serializable <see cref="CharterRuleReferenceCatalog"/>. It is the only player-runtime static
    /// validation source: Editor import, fixtures and defaulted data never join save or restore.
    /// </summary>
    [CreateAssetMenu(fileName = "CharterRuleStaticCatalog", menuName = "天章/内容/册界静态目录")]
    public sealed class CharterRuleStaticCatalogData : ScriptableObject
    {
        [SerializeField] private int definitionCatalogVersion;
        [SerializeField] private CharterRuleDefinitionData[] definitions = Array.Empty<CharterRuleDefinitionData>();
        [SerializeField] private CharterRuleReferenceCatalog referenceCatalog;

        /// <summary>Explicit version snapshot of this static directory; zero or missing never infers a default.</summary>
        public int DefinitionCatalogVersion => definitionCatalogVersion;

        public CharterRuleDefinitionData[] Definitions => definitions;

        public CharterRuleReferenceCatalog ReferenceCatalog => referenceCatalog;

        /// <summary>
        /// Validates the explicit version, the declared catalog (no duplicate stable IDs) and every
        /// definition (unique ruleEntryId, all eighteen contract fields resolved by this catalog).
        /// </summary>
        public bool TryValidateDefinitions(out string reason)
        {
            return CharterRuleCatalogValidator.TryValidateDefinitions(
                definitions,
                referenceCatalog,
                definitionCatalogVersion,
                out reason);
        }
    }

    public static class CharterRuleCatalogReasons
    {
        public const string Ok = "";
        public const string VersionUndeclared = "charter_catalog_version_undeclared";
        public const string CatalogUndeclared = "charter_catalog_undeclared";
        public const string DuplicateCatalogId = "charter_catalog_duplicate_id";
        public const string DuplicateRuleEntryId = "charter_catalog_duplicate_rule_entry";
        public const string InvalidDefinition = "charter_catalog_invalid_definition";
        public const string UnknownRuleEntry = "charter_catalog_unknown_rule_entry";
        public const string UnknownDisplayNameKey = "charter_catalog_unknown_display_name";
        public const string UnknownRuleFamily = "charter_catalog_unknown_rule_family";
        public const string UnknownRelationElement = "charter_catalog_unknown_relation_element";
        public const string UnknownPhenomenon = "charter_catalog_unknown_phenomenon";
        public const string AtomicCommitIncomplete = "charter_catalog_atomic_commit_incomplete";
        public const string UnknownRealitySupply = "charter_catalog_unknown_reality_supply";
        public const string UnknownAuthority = "charter_catalog_unknown_authority";
        public const string UnknownRelic = "charter_catalog_unknown_relic";
        public const string UnknownAuthorization = "charter_catalog_unknown_authorization";
        public const string UnknownNodeType = "charter_catalog_unknown_node_type";
        public const string UnknownNode = "charter_catalog_unknown_node";
        public const string UnknownBoundary = "charter_catalog_unknown_boundary";
        public const string CoverageOutOfBoundary = "charter_catalog_coverage_out_of_boundary";
        public const string UnknownVariable = "charter_catalog_unknown_variable";
        public const string UnknownConflict = "charter_catalog_unknown_conflict";
        public const string UnknownWorldEvent = "charter_catalog_unknown_world_event";
        public const string UnknownEnvironmentProfile = "charter_catalog_unknown_environment_profile";
        public const string InvalidScopeType = "charter_catalog_invalid_scope_type";
        public const string InvalidScopeTierCap = "charter_catalog_invalid_scope_tier_cap";
        public const string InvalidFailurePolicy = "charter_catalog_invalid_failure_policy";
    }

    /// <summary>
    /// The one shared definition/catalog validation implementation. Both the Editor importer and the
    /// player runtime call exactly this validation; there is no second hard-coded production catalog.
    /// </summary>
    public static class CharterRuleCatalogValidator
    {
        /// <summary>Validates the explicit version, the catalog itself and every definition.</summary>
        public static bool TryValidateDefinitions(
            CharterRuleDefinitionData[] definitions,
            CharterRuleReferenceCatalog catalog,
            int definitionCatalogVersion,
            out string reason)
        {
            if (definitionCatalogVersion <= 0)
            {
                reason = CharterRuleCatalogReasons.VersionUndeclared;
                return false;
            }
            if (!TryValidateCatalog(catalog, out reason))
                return false;

            var ruleEntryIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharterRuleDefinitionData definition in definitions ?? Array.Empty<CharterRuleDefinitionData>())
            {
                if (definition == null)
                {
                    reason = CharterRuleCatalogReasons.InvalidDefinition;
                    return false;
                }
                if (string.IsNullOrWhiteSpace(definition.ruleEntryId) ||
                    !ruleEntryIds.Add(definition.ruleEntryId))
                {
                    reason = CharterRuleCatalogReasons.DuplicateRuleEntryId;
                    return false;
                }
            }
            foreach (CharterRuleDefinitionData definition in definitions ?? Array.Empty<CharterRuleDefinitionData>())
            {
                if (!TryValidateDefinition(definition, catalog, out reason))
                    return false;
            }

            reason = CharterRuleCatalogReasons.Ok;
            return true;
        }

        /// <summary>
        /// Validates the catalog alone: explicit declaration and no duplicate stable ID anywhere.
        /// Nested record references (authority relic/organization versions, commit supplies) must
        /// resolve inside the same catalog.
        /// </summary>
        public static bool TryValidateCatalog(CharterRuleReferenceCatalog catalog, out string reason)
        {
            if (catalog == null || !catalog.HasDeclaredAuthority)
            {
                reason = CharterRuleCatalogReasons.CatalogUndeclared;
                return false;
            }

            if (!HasUniqueIds(catalog.displayNameKeys) ||
                !HasUniqueIds(catalog.ruleFamilyIds) ||
                !HasUniqueIds(catalog.relationElementIds) ||
                !HasUniqueIds(catalog.phenomenonIds) ||
                !HasUniqueIds(catalog.relicIds) ||
                !HasUniqueIds(catalog.organizationAuthorizationVersionIds) ||
                !HasUniqueIds(catalog.nodeTypeIds) ||
                !HasUniqueIds(catalog.nodeIds) ||
                !HasUniqueIds(catalog.realitySupplyIds) ||
                !HasUniqueIds(catalog.worldVariableIds) ||
                !HasUniqueIds(catalog.worldEventIds) ||
                !HasUniqueIds(catalog.environmentProfileIds) ||
                !HasUniqueIds(catalog.ruleEntryIds))
            {
                reason = CharterRuleCatalogReasons.DuplicateCatalogId;
                return false;
            }

            foreach (CharterAuthorityRequirement authority in catalog.authorityRequirements ?? Array.Empty<CharterAuthorityRequirement>())
            {
                if (authority == null || string.IsNullOrWhiteSpace(authority.authorityId) ||
                    !HasUniqueIds(authority.organizationAuthorizationVersionIds))
                {
                    reason = CharterRuleCatalogReasons.DuplicateCatalogId;
                    return false;
                }
                if (!catalog.ContainsRelic(authority.relicId))
                {
                    reason = CharterRuleCatalogReasons.UnknownRelic;
                    return false;
                }
                foreach (string versionId in authority.organizationAuthorizationVersionIds ?? Array.Empty<string>())
                {
                    if (!catalog.ContainsOrganizationAuthorizationVersion(versionId))
                    {
                        reason = CharterRuleCatalogReasons.UnknownAuthorization;
                        return false;
                    }
                }
            }

            foreach (CharterPropagationBoundaryReference boundary in catalog.propagationBoundaries ?? Array.Empty<CharterPropagationBoundaryReference>())
            {
                if (boundary == null || string.IsNullOrWhiteSpace(boundary.propagationBoundaryProfileId) ||
                    boundary.allowedCoverageIds == null || boundary.allowedCoverageIds.Length == 0 ||
                    !HasUniqueIds(boundary.allowedCoverageIds))
                {
                    reason = CharterRuleCatalogReasons.DuplicateCatalogId;
                    return false;
                }
            }

            foreach (CharterCommitReference commit in catalog.commits ?? Array.Empty<CharterCommitReference>())
            {
                if (commit == null || string.IsNullOrWhiteSpace(commit.commitId) ||
                    commit.realitySupplyIds == null || commit.realitySupplyIds.Length == 0 ||
                    !HasUniqueIds(commit.realitySupplyIds))
                {
                    reason = CharterRuleCatalogReasons.AtomicCommitIncomplete;
                    return false;
                }
                foreach (string supplyId in commit.realitySupplyIds)
                {
                    if (!catalog.ContainsRealitySupply(supplyId))
                    {
                        reason = CharterRuleCatalogReasons.UnknownRealitySupply;
                        return false;
                    }
                }
            }

            foreach (CharterConflictReference conflict in catalog.conflicts ?? Array.Empty<CharterConflictReference>())
            {
                if (conflict == null || string.IsNullOrWhiteSpace(conflict.conflictProfileId) ||
                    conflict.crossTierChallengeGrantIds == null ||
                    conflict.crossTierChallengeGrantIds.Length == 0 ||
                    !HasUniqueIds(conflict.crossTierChallengeGrantIds))
                {
                    reason = CharterRuleCatalogReasons.DuplicateCatalogId;
                    return false;
                }
            }

            reason = CharterRuleCatalogReasons.Ok;
            return true;
        }

        /// <summary>
        /// Validates one definition against the catalog: unique ruleEntryId inside the catalog and
        /// all eighteen contract fields plus every external reference resolved by the same catalog.
        /// </summary>
        public static bool TryValidateDefinition(
            CharterRuleDefinitionData definition,
            CharterRuleReferenceCatalog catalog,
            out string reason)
        {
            if (definition == null)
            {
                reason = CharterRuleCatalogReasons.InvalidDefinition;
                return false;
            }
            if (!catalog.ContainsRuleEntry(definition.ruleEntryId))
            {
                reason = CharterRuleCatalogReasons.UnknownRuleEntry;
                return false;
            }
            if (!catalog.ContainsDisplayNameKey(definition.displayName))
            {
                reason = CharterRuleCatalogReasons.UnknownDisplayNameKey;
                return false;
            }
            if (!catalog.ContainsRuleFamily(definition.ruleFamily))
            {
                reason = CharterRuleCatalogReasons.UnknownRuleFamily;
                return false;
            }
            if (!catalog.ContainsRelationElement(definition.relationElement))
            {
                reason = CharterRuleCatalogReasons.UnknownRelationElement;
                return false;
            }
            if (!HasUniqueIds(definition.compatiblePhenomena) ||
                definition.compatiblePhenomena.Any(phenomenon => !catalog.ContainsPhenomenon(phenomenon)))
            {
                reason = CharterRuleCatalogReasons.UnknownPhenomenon;
                return false;
            }
            if (!HasResolvableCommit(definition.positiveCommit, catalog) ||
                !HasResolvableCommit(definition.negativeCommit, catalog))
            {
                reason = CharterRuleCatalogReasons.AtomicCommitIncomplete;
                return false;
            }
            CharterAuthorityRequirement authority = catalog.FindAuthority(definition.requiredAuthority);
            if (authority == null || !catalog.ContainsRelic(authority.relicId))
            {
                reason = CharterRuleCatalogReasons.UnknownAuthority;
                return false;
            }
            foreach (string versionId in authority.organizationAuthorizationVersionIds ?? Array.Empty<string>())
            {
                if (!catalog.ContainsOrganizationAuthorizationVersion(versionId))
                {
                    reason = CharterRuleCatalogReasons.UnknownAuthorization;
                    return false;
                }
            }
            if (!HasUniqueIds(definition.requiredNodeTypes) ||
                definition.requiredNodeTypes.Any(nodeType => !catalog.ContainsNodeType(nodeType)))
            {
                reason = CharterRuleCatalogReasons.UnknownNodeType;
                return false;
            }
            if (!Enum.IsDefined(typeof(CharterRuleScopeType), definition.scopeType))
            {
                reason = CharterRuleCatalogReasons.InvalidScopeType;
                return false;
            }
            if (!Enum.IsDefined(typeof(CharterRuleScopeTierCap), definition.scopeTierCap))
            {
                reason = CharterRuleCatalogReasons.InvalidScopeTierCap;
                return false;
            }
            if (!HasUniqueIds(definition.anchorNodeIds) ||
                definition.anchorNodeIds.Any(nodeId => !catalog.ContainsNode(nodeId)))
            {
                reason = CharterRuleCatalogReasons.UnknownNode;
                return false;
            }
            CharterPropagationBoundaryReference boundary =
                catalog.FindPropagationBoundary(definition.propagationBoundaryProfileId);
            if (boundary == null || boundary.allowedCoverageIds == null || boundary.allowedCoverageIds.Length == 0)
            {
                reason = CharterRuleCatalogReasons.UnknownBoundary;
                return false;
            }
            if (!HasUniqueIds(definition.currentCoverageSet) ||
                definition.currentCoverageSet.Any(coverageId => !boundary.allowedCoverageIds.Contains(coverageId, StringComparer.Ordinal)))
            {
                reason = CharterRuleCatalogReasons.CoverageOutOfBoundary;
                return false;
            }
            if (!HasUniqueIds(definition.affectedWorldVariables) ||
                definition.affectedWorldVariables.Any(variableId => !catalog.ContainsWorldVariable(variableId)))
            {
                reason = CharterRuleCatalogReasons.UnknownVariable;
                return false;
            }
            if (catalog.FindConflict(definition.conflictProfileId) == null)
            {
                reason = CharterRuleCatalogReasons.UnknownConflict;
                return false;
            }
            if (!Enum.IsDefined(typeof(CharterRuleFailurePolicy), definition.failurePolicy))
            {
                reason = CharterRuleCatalogReasons.InvalidFailurePolicy;
                return false;
            }
            if (definition.worldEventOutputs == null || definition.worldEventOutputs.Length == 0 ||
                !HasUniqueEventOutputs(definition.worldEventOutputs))
            {
                reason = CharterRuleCatalogReasons.UnknownWorldEvent;
                return false;
            }
            foreach (CharterWorldEventOutputData output in definition.worldEventOutputs)
            {
                if (output == null || !catalog.ContainsWorldEvent(output.eventId))
                {
                    reason = CharterRuleCatalogReasons.UnknownWorldEvent;
                    return false;
                }
                if (!catalog.ContainsEnvironmentProfile(output.environmentProfileId))
                {
                    reason = CharterRuleCatalogReasons.UnknownEnvironmentProfile;
                    return false;
                }
            }

            reason = CharterRuleCatalogReasons.Ok;
            return true;
        }

        private static bool HasResolvableCommit(string commitId, CharterRuleReferenceCatalog catalog)
        {
            if (string.IsNullOrWhiteSpace(commitId))
                return false;
            CharterCommitReference commit = catalog.FindCommit(commitId);
            return commit != null && commit.realitySupplyIds != null && commit.realitySupplyIds.Length > 0 &&
                   commit.realitySupplyIds.All(supplyId => catalog.ContainsRealitySupply(supplyId));
        }

        private static bool HasUniqueEventOutputs(CharterWorldEventOutputData[] outputs)
        {
            var eventIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharterWorldEventOutputData output in outputs)
            {
                if (output == null || string.IsNullOrWhiteSpace(output.eventId) || !eventIds.Add(output.eventId))
                    return false;
            }
            return true;
        }

        private static bool HasUniqueIds(string[] values)
        {
            if (values == null || values.Length == 0)
                return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value) || !ids.Add(value))
                    return false;
            }
            return true;
        }
    }
}
