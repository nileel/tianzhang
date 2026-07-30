using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Content;

namespace TianZhang.World
{
    [Serializable]
    public sealed class CharterNodeRuntimeStateData
    {
        public string nodeId;
        public string state;
    }

    [Serializable]
    public sealed class CharterAuthorizationVersionStateData
    {
        public string authorizationVersionId;
        public string state;
    }

    [Serializable]
    public sealed class CharterOccupancyStateData
    {
        public string resourceId;
        public string occupancyId;
    }

    [Serializable]
    public sealed class CharterRealitySupplyStateData
    {
        public string realitySupplyId;
        public string state;
    }

    [Serializable]
    public sealed class CharterCommitResultStateData
    {
        public string commitId;
        public string resultState;
    }

    public static class CharterRuntimeStateReasons
    {
        public const string Ok = "";
        public const string InvalidState = "charter_state_invalid";
        public const string CatalogUndeclared = "charter_state_catalog_undeclared";
        public const string UnknownRuleEntry = "charter_state_unknown_rule_entry";
        public const string DefinitionIdMixed = "charter_state_definition_id_mixed";
        public const string UnknownNode = "charter_state_unknown_node";
        public const string UnknownAuthorization = "charter_state_unknown_authorization";
        public const string UnknownCoverage = "charter_state_unknown_coverage";
        public const string UnknownRealitySupply = "charter_state_unknown_reality_supply";
        public const string UnknownCommit = "charter_state_unknown_commit";
    }

    /// <summary>
    /// Dynamic state only. It records stable IDs and outcomes; it neither owns rule definitions
    /// nor joins GameSession, save-version migration, or runtime rule execution.
    /// </summary>
    [Serializable]
    public sealed class CharterRuntimeStateData
    {
        public string stateId;
        public string[] registeredRuleEntryIds;
        public string charterRelicState;
        public string worldSealState;
        public CharterNodeRuntimeStateData[] nodeStates;
        public CharterAuthorizationVersionStateData[] organizationAuthorizationVersions;
        public string[] currentCoverageSet;
        public CharterOccupancyStateData[] ruleEntryOccupancies;
        public CharterOccupancyStateData[] nodeOccupancies;
        public CharterRealitySupplyStateData[] realitySupplyStates;
        public CharterCommitResultStateData[] positiveCommitResults;
        public CharterCommitResultStateData[] negativeCommitResults;
        public string[] currentRegionRuleEntryIds;

        public bool TryValidate(
            CharterRuleDefinitionData[] definitions,
            CharterRuleReferenceCatalog catalog,
            out string reason)
        {
            if (string.IsNullOrWhiteSpace(stateId) || string.IsNullOrWhiteSpace(charterRelicState) ||
                string.IsNullOrWhiteSpace(worldSealState) || catalog == null || !catalog.HasDeclaredAuthority)
            {
                reason = catalog == null || !catalog.HasDeclaredAuthority
                    ? CharterRuntimeStateReasons.CatalogUndeclared
                    : CharterRuntimeStateReasons.InvalidState;
                return false;
            }

            var definitionIds = new HashSet<string>(
                (definitions ?? Array.Empty<CharterRuleDefinitionData>())
                    .Where(definition => definition != null && !string.IsNullOrWhiteSpace(definition.ruleEntryId))
                    .Select(definition => definition.ruleEntryId),
                StringComparer.Ordinal);
            if (!TryValidateRuleEntryIds(registeredRuleEntryIds, definitionIds, out reason) ||
                !TryValidateRuleEntryIds(currentRegionRuleEntryIds, definitionIds, out reason) ||
                !TryValidateNodeStates(nodeStates, catalog, out reason) ||
                !TryValidateAuthorizationStates(organizationAuthorizationVersions, catalog, out reason) ||
                !TryValidateCoverage(currentCoverageSet, catalog, out reason) ||
                !TryValidateOccupancies(ruleEntryOccupancies, definitionIds, catalog, true, out reason) ||
                !TryValidateOccupancies(nodeOccupancies, definitionIds, catalog, false, out reason) ||
                !TryValidateRealitySupplies(realitySupplyStates, catalog, out reason) ||
                !TryValidateCommitResults(positiveCommitResults, catalog, out reason) ||
                !TryValidateCommitResults(negativeCommitResults, catalog, out reason))
            {
                return false;
            }

            reason = CharterRuntimeStateReasons.Ok;
            return true;
        }

        private bool TryValidateRuleEntryIds(
            IEnumerable<string> values,
            ISet<string> definitionIds,
            out string reason)
        {
            foreach (string value in values ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    reason = CharterRuntimeStateReasons.InvalidState;
                    return false;
                }
                if (string.Equals(value, stateId, StringComparison.Ordinal))
                {
                    reason = CharterRuntimeStateReasons.DefinitionIdMixed;
                    return false;
                }
                if (!definitionIds.Contains(value))
                {
                    reason = CharterRuntimeStateReasons.UnknownRuleEntry;
                    return false;
                }
            }

            reason = CharterRuntimeStateReasons.Ok;
            return true;
        }

        private static bool TryValidateNodeStates(
            IEnumerable<CharterNodeRuntimeStateData> values,
            CharterRuleReferenceCatalog catalog,
            out string reason)
        {
            foreach (var value in values ?? Array.Empty<CharterNodeRuntimeStateData>())
            {
                if (value == null || string.IsNullOrWhiteSpace(value.state))
                {
                    reason = CharterRuntimeStateReasons.InvalidState;
                    return false;
                }
                if (!catalog.ContainsNode(value.nodeId))
                {
                    reason = CharterRuntimeStateReasons.UnknownNode;
                    return false;
                }
            }

            reason = CharterRuntimeStateReasons.Ok;
            return true;
        }

        private static bool TryValidateAuthorizationStates(
            IEnumerable<CharterAuthorizationVersionStateData> values,
            CharterRuleReferenceCatalog catalog,
            out string reason)
        {
            foreach (var value in values ?? Array.Empty<CharterAuthorizationVersionStateData>())
            {
                if (value == null || string.IsNullOrWhiteSpace(value.state))
                {
                    reason = CharterRuntimeStateReasons.InvalidState;
                    return false;
                }
                if (!catalog.ContainsOrganizationAuthorizationVersion(value.authorizationVersionId))
                {
                    reason = CharterRuntimeStateReasons.UnknownAuthorization;
                    return false;
                }
            }

            reason = CharterRuntimeStateReasons.Ok;
            return true;
        }

        private static bool TryValidateCoverage(
            IEnumerable<string> values,
            CharterRuleReferenceCatalog catalog,
            out string reason)
        {
            var allowedCoverage = new HashSet<string>(
                (catalog.propagationBoundaries ?? Array.Empty<CharterPropagationBoundaryReference>())
                    .Where(boundary => boundary != null && boundary.allowedCoverageIds != null)
                    .SelectMany(boundary => boundary.allowedCoverageIds),
                StringComparer.Ordinal);
            foreach (string value in values ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(value) || !allowedCoverage.Contains(value))
                {
                    reason = CharterRuntimeStateReasons.UnknownCoverage;
                    return false;
                }
            }

            reason = CharterRuntimeStateReasons.Ok;
            return true;
        }

        private static bool TryValidateOccupancies(
            IEnumerable<CharterOccupancyStateData> values,
            ISet<string> definitionIds,
            CharterRuleReferenceCatalog catalog,
            bool isRuleEntryOccupancy,
            out string reason)
        {
            foreach (var value in values ?? Array.Empty<CharterOccupancyStateData>())
            {
                if (value == null || string.IsNullOrWhiteSpace(value.resourceId) || string.IsNullOrWhiteSpace(value.occupancyId))
                {
                    reason = CharterRuntimeStateReasons.InvalidState;
                    return false;
                }
                if (isRuleEntryOccupancy && !definitionIds.Contains(value.resourceId))
                {
                    reason = CharterRuntimeStateReasons.UnknownRuleEntry;
                    return false;
                }
                if (!isRuleEntryOccupancy && !catalog.ContainsNode(value.resourceId))
                {
                    reason = CharterRuntimeStateReasons.UnknownNode;
                    return false;
                }
            }

            reason = CharterRuntimeStateReasons.Ok;
            return true;
        }

        private static bool TryValidateRealitySupplies(
            IEnumerable<CharterRealitySupplyStateData> values,
            CharterRuleReferenceCatalog catalog,
            out string reason)
        {
            foreach (var value in values ?? Array.Empty<CharterRealitySupplyStateData>())
            {
                if (value == null || string.IsNullOrWhiteSpace(value.state))
                {
                    reason = CharterRuntimeStateReasons.InvalidState;
                    return false;
                }
                if (!catalog.ContainsRealitySupply(value.realitySupplyId))
                {
                    reason = CharterRuntimeStateReasons.UnknownRealitySupply;
                    return false;
                }
            }

            reason = CharterRuntimeStateReasons.Ok;
            return true;
        }

        private static bool TryValidateCommitResults(
            IEnumerable<CharterCommitResultStateData> values,
            CharterRuleReferenceCatalog catalog,
            out string reason)
        {
            foreach (var value in values ?? Array.Empty<CharterCommitResultStateData>())
            {
                if (value == null || string.IsNullOrWhiteSpace(value.resultState))
                {
                    reason = CharterRuntimeStateReasons.InvalidState;
                    return false;
                }
                if (catalog.FindCommit(value.commitId) == null)
                {
                    reason = CharterRuntimeStateReasons.UnknownCommit;
                    return false;
                }
            }

            reason = CharterRuntimeStateReasons.Ok;
            return true;
        }
    }
}
