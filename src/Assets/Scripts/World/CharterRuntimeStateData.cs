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
        public const string DuplicateRuleEntry = "charter_state_duplicate_rule_entry";
        public const string DefinitionIdMixed = "charter_state_definition_id_mixed";
        public const string UnknownNode = "charter_state_unknown_node";
        public const string UnknownBoundary = "charter_state_unknown_boundary";
        public const string UnknownAuthorization = "charter_state_unknown_authorization";
        public const string UnknownCoverage = "charter_state_unknown_coverage";
        public const string DuplicateNode = "charter_state_duplicate_node";
        public const string DuplicateAuthorization = "charter_state_duplicate_authorization";
        public const string DuplicateCoverage = "charter_state_duplicate_coverage";
        public const string UnknownRealitySupply = "charter_state_unknown_reality_supply";
        public const string UnknownCommit = "charter_state_unknown_commit";
        public const string DuplicateOccupancy = "charter_state_duplicate_occupancy";
        public const string DuplicateRealitySupply = "charter_state_duplicate_reality_supply";
        public const string DuplicateCommitResult = "charter_state_duplicate_commit_result";
        public const string EntryAnchorMissing = "charter_state_entry_anchor_missing";
        public const string NodeOutsideEntryBoundary = "charter_state_node_outside_entry_boundary";
        public const string EntryCoverageMissing = "charter_state_entry_coverage_missing";
        public const string CoverageOutsideEntryBoundary = "charter_state_coverage_outside_entry_boundary";
        public const string AuthorizationRequirementMismatch = "charter_state_authorization_requirement_mismatch";
        public const string CommitPairIncomplete = "charter_state_commit_pair_incomplete";
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

        /// <summary>
        /// Returns an independent copy of this state so a rule transaction can build its next
        /// state without mutating the input instance or sharing record instances.
        /// </summary>
        public CharterRuntimeStateData CreateCopy()
        {
            return new CharterRuntimeStateData
            {
                stateId = stateId,
                charterRelicState = charterRelicState,
                worldSealState = worldSealState,
                registeredRuleEntryIds = CopyStrings(registeredRuleEntryIds),
                nodeStates = CopyNodes(nodeStates),
                organizationAuthorizationVersions = CopyAuthorizations(organizationAuthorizationVersions),
                currentCoverageSet = CopyStrings(currentCoverageSet),
                ruleEntryOccupancies = CopyOccupancies(ruleEntryOccupancies),
                nodeOccupancies = CopyOccupancies(nodeOccupancies),
                realitySupplyStates = CopySupplies(realitySupplyStates),
                positiveCommitResults = CopyCommitResults(positiveCommitResults),
                negativeCommitResults = CopyCommitResults(negativeCommitResults),
                currentRegionRuleEntryIds = CopyStrings(currentRegionRuleEntryIds),
            };
        }

        private static string[] CopyStrings(string[] values)
        {
            if (values == null)
                return null;
            var copy = new string[values.Length];
            Array.Copy(values, copy, values.Length);
            return copy;
        }

        private static CharterNodeRuntimeStateData[] CopyNodes(CharterNodeRuntimeStateData[] values)
        {
            if (values == null)
                return null;
            var copy = new CharterNodeRuntimeStateData[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                copy[i] = value == null ? null : new CharterNodeRuntimeStateData { nodeId = value.nodeId, state = value.state };
            }
            return copy;
        }

        private static CharterAuthorizationVersionStateData[] CopyAuthorizations(CharterAuthorizationVersionStateData[] values)
        {
            if (values == null)
                return null;
            var copy = new CharterAuthorizationVersionStateData[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                copy[i] = value == null
                    ? null
                    : new CharterAuthorizationVersionStateData { authorizationVersionId = value.authorizationVersionId, state = value.state };
            }
            return copy;
        }

        private static CharterOccupancyStateData[] CopyOccupancies(CharterOccupancyStateData[] values)
        {
            if (values == null)
                return null;
            var copy = new CharterOccupancyStateData[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                copy[i] = value == null
                    ? null
                    : new CharterOccupancyStateData { resourceId = value.resourceId, occupancyId = value.occupancyId };
            }
            return copy;
        }

        private static CharterRealitySupplyStateData[] CopySupplies(CharterRealitySupplyStateData[] values)
        {
            if (values == null)
                return null;
            var copy = new CharterRealitySupplyStateData[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                copy[i] = value == null
                    ? null
                    : new CharterRealitySupplyStateData { realitySupplyId = value.realitySupplyId, state = value.state };
            }
            return copy;
        }

        private static CharterCommitResultStateData[] CopyCommitResults(CharterCommitResultStateData[] values)
        {
            if (values == null)
                return null;
            var copy = new CharterCommitResultStateData[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                copy[i] = value == null
                    ? null
                    : new CharterCommitResultStateData { commitId = value.commitId, resultState = value.resultState };
            }
            return copy;
        }

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
                !TryValidateCommitResults(negativeCommitResults, catalog, out reason) ||
                !TryValidateEntryRelationships(definitions, catalog, out reason))
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
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    reason = CharterRuntimeStateReasons.InvalidState;
                    return false;
                }
                if (!ids.Add(value))
                {
                    reason = CharterRuntimeStateReasons.DuplicateRuleEntry;
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
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values ?? Array.Empty<CharterNodeRuntimeStateData>())
            {
                if (value == null || string.IsNullOrWhiteSpace(value.state))
                {
                    reason = CharterRuntimeStateReasons.InvalidState;
                    return false;
                }
                if (!nodeIds.Add(value.nodeId))
                {
                    reason = CharterRuntimeStateReasons.DuplicateNode;
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
            var versionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values ?? Array.Empty<CharterAuthorizationVersionStateData>())
            {
                if (value == null || string.IsNullOrWhiteSpace(value.state))
                {
                    reason = CharterRuntimeStateReasons.InvalidState;
                    return false;
                }
                if (!versionIds.Add(value.authorizationVersionId))
                {
                    reason = CharterRuntimeStateReasons.DuplicateAuthorization;
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
            var coverageIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    reason = CharterRuntimeStateReasons.UnknownCoverage;
                    return false;
                }
                if (!coverageIds.Add(value))
                {
                    reason = CharterRuntimeStateReasons.DuplicateCoverage;
                    return false;
                }
                if (!allowedCoverage.Contains(value))
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
            var resourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values ?? Array.Empty<CharterOccupancyStateData>())
            {
                if (value == null || string.IsNullOrWhiteSpace(value.resourceId) || string.IsNullOrWhiteSpace(value.occupancyId))
                {
                    reason = CharterRuntimeStateReasons.InvalidState;
                    return false;
                }
                if (!resourceIds.Add(value.resourceId))
                {
                    reason = CharterRuntimeStateReasons.DuplicateOccupancy;
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
            var supplyIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values ?? Array.Empty<CharterRealitySupplyStateData>())
            {
                if (value == null || string.IsNullOrWhiteSpace(value.state))
                {
                    reason = CharterRuntimeStateReasons.InvalidState;
                    return false;
                }
                if (!supplyIds.Add(value.realitySupplyId))
                {
                    reason = CharterRuntimeStateReasons.DuplicateRealitySupply;
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
            var commitIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values ?? Array.Empty<CharterCommitResultStateData>())
            {
                if (value == null || string.IsNullOrWhiteSpace(value.resultState))
                {
                    reason = CharterRuntimeStateReasons.InvalidState;
                    return false;
                }
                if (!commitIds.Add(value.commitId))
                {
                    reason = CharterRuntimeStateReasons.DuplicateCommitResult;
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

        /// <summary>
        /// Validates the complete relationship between every registered/current-region entry and its
        /// static definition: nodes and coverage belong to the definition boundary, authorization
        /// matches the definition requirement, and positive/negative commit results stay paired.
        /// It only validates saved results and never re-executes rules, conflicts or events.
        /// </summary>
        private bool TryValidateEntryRelationships(
            CharterRuleDefinitionData[] definitions,
            CharterRuleReferenceCatalog catalog,
            out string reason)
        {
            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string entryId in (registeredRuleEntryIds ?? Array.Empty<string>())
                         .Concat(currentRegionRuleEntryIds ?? Array.Empty<string>()))
            {
                if (!string.IsNullOrWhiteSpace(entryId))
                    entryIds.Add(entryId);
            }
            if (entryIds.Count == 0)
            {
                reason = CharterRuntimeStateReasons.Ok;
                return true;
            }

            var entries = new List<CharterRuleDefinitionData>();
            foreach (string entryId in entryIds)
            {
                CharterRuleDefinitionData definition = null;
                foreach (CharterRuleDefinitionData candidate in definitions ?? Array.Empty<CharterRuleDefinitionData>())
                {
                    if (candidate != null && string.Equals(candidate.ruleEntryId, entryId, StringComparison.Ordinal))
                    {
                        definition = candidate;
                        break;
                    }
                }
                if (definition == null)
                {
                    reason = CharterRuntimeStateReasons.UnknownRuleEntry;
                    return false;
                }
                entries.Add(definition);
            }

            // 节点与覆盖属于其定义边界。
            var anchors = new HashSet<string>(StringComparer.Ordinal);
            var boundaryCoverage = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharterRuleDefinitionData entry in entries)
            {
                foreach (string anchorId in entry.anchorNodeIds ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(anchorId))
                        anchors.Add(anchorId);
                }
                if (string.IsNullOrWhiteSpace(entry.propagationBoundaryProfileId))
                    continue;
                CharterPropagationBoundaryReference boundary =
                    catalog.FindPropagationBoundary(entry.propagationBoundaryProfileId);
                if (boundary == null || boundary.allowedCoverageIds == null)
                {
                    reason = CharterRuntimeStateReasons.UnknownBoundary;
                    return false;
                }
                foreach (string coverageId in boundary.allowedCoverageIds)
                {
                    if (!string.IsNullOrWhiteSpace(coverageId))
                        boundaryCoverage.Add(coverageId);
                }
            }

            var stateNodes = new HashSet<string>(
                (nodeStates ?? Array.Empty<CharterNodeRuntimeStateData>())
                    .Where(record => record != null && !string.IsNullOrWhiteSpace(record.nodeId))
                    .Select(record => record.nodeId),
                StringComparer.Ordinal);
            foreach (string anchorId in anchors)
            {
                if (!stateNodes.Contains(anchorId))
                {
                    reason = CharterRuntimeStateReasons.EntryAnchorMissing;
                    return false;
                }
            }
            foreach (string nodeId in stateNodes)
            {
                if (!anchors.Contains(nodeId))
                {
                    reason = CharterRuntimeStateReasons.NodeOutsideEntryBoundary;
                    return false;
                }
            }

            var stateCoverage = new HashSet<string>(
                (currentCoverageSet ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.Ordinal);
            foreach (CharterRuleDefinitionData entry in entries)
            {
                foreach (string coverageId in entry.currentCoverageSet ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(coverageId) || !stateCoverage.Contains(coverageId))
                    {
                        reason = CharterRuntimeStateReasons.EntryCoverageMissing;
                        return false;
                    }
                }
            }
            foreach (string coverageId in stateCoverage)
            {
                if (!boundaryCoverage.Contains(coverageId))
                {
                    reason = CharterRuntimeStateReasons.CoverageOutsideEntryBoundary;
                    return false;
                }
            }

            // 授权与定义要求匹配：条目要求的组织授权版本必须出现在状态中。
            var stateAuthorizations = new HashSet<string>(
                (organizationAuthorizationVersions ?? Array.Empty<CharterAuthorizationVersionStateData>())
                    .Where(record => record != null && !string.IsNullOrWhiteSpace(record.authorizationVersionId))
                    .Select(record => record.authorizationVersionId),
                StringComparer.Ordinal);
            foreach (CharterRuleDefinitionData entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.requiredAuthority))
                    continue;
                CharterAuthorityRequirement authority = catalog.FindAuthority(entry.requiredAuthority);
                if (authority == null)
                {
                    reason = CharterRuntimeStateReasons.UnknownAuthorization;
                    return false;
                }
                foreach (string versionId in authority.organizationAuthorizationVersionIds ?? Array.Empty<string>())
                {
                    if (!stateAuthorizations.Contains(versionId))
                    {
                        reason = CharterRuntimeStateReasons.AuthorizationRequirementMismatch;
                        return false;
                    }
                }
            }

            // 正负提交成对且都能解析：已记录的一方必须与另一方同时存在。
            var positiveCommits = new HashSet<string>(
                (positiveCommitResults ?? Array.Empty<CharterCommitResultStateData>())
                    .Where(record => record != null && !string.IsNullOrWhiteSpace(record.commitId))
                    .Select(record => record.commitId),
                StringComparer.Ordinal);
            var negativeCommits = new HashSet<string>(
                (negativeCommitResults ?? Array.Empty<CharterCommitResultStateData>())
                    .Where(record => record != null && !string.IsNullOrWhiteSpace(record.commitId))
                    .Select(record => record.commitId),
                StringComparer.Ordinal);
            foreach (CharterRuleDefinitionData entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.positiveCommit) || string.IsNullOrWhiteSpace(entry.negativeCommit))
                    continue;
                bool hasPositive = positiveCommits.Contains(entry.positiveCommit);
                bool hasNegative = negativeCommits.Contains(entry.negativeCommit);
                if (hasPositive != hasNegative)
                {
                    reason = CharterRuntimeStateReasons.CommitPairIncomplete;
                    return false;
                }
            }

            reason = CharterRuntimeStateReasons.Ok;
            return true;
        }
    }
}
