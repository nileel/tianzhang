using System;
using System.Linq;
using UnityEngine;

namespace TianZhang.Content
{
    public enum CharterRuleScopeType
    {
        SingleNode,
        ConnectedNodes,
        RegionalHub,
    }

    public enum CharterRuleScopeTierCap
    {
        Node,
        Area,
        Region,
    }

    public enum CharterRuleFailurePolicy
    {
        Reject,
        Suspend,
        SafeDowngrade,
    }

    [Serializable]
    public sealed class CharterWorldEventOutputData
    {
        public string eventId;
        public string environmentProfileId;
    }

    [CreateAssetMenu(fileName = "CharterRuleDefinition_", menuName = "天章/内容/册界规则定义")]
    public sealed class CharterRuleDefinitionData : ScriptableObject
    {
        public string ruleEntryId;
        public string displayName;
        public string ruleFamily;
        public string relationElement;
        public string[] compatiblePhenomena;
        public string positiveCommit;
        public string negativeCommit;
        public string requiredAuthority;
        public string[] requiredNodeTypes;
        public CharterRuleScopeType scopeType;
        public CharterRuleScopeTierCap scopeTierCap;
        public string[] anchorNodeIds;
        public string propagationBoundaryProfileId;
        public string[] currentCoverageSet;
        public string[] affectedWorldVariables;
        public string conflictProfileId;
        public CharterRuleFailurePolicy failurePolicy;
        public CharterWorldEventOutputData[] worldEventOutputs;
    }

    /// <summary>
    /// The one approved external reference directory. It is a serializable data type so the single
    /// production static catalog asset can hold it and both the importer and the player runtime can
    /// validate against the same instance. It does not create a second rule, node, authorization,
    /// commit, conflict, or environment registry; production rows must resolve through real owners.
    /// </summary>
    [Serializable]
    public sealed class CharterRuleReferenceCatalog
    {
        public string[] displayNameKeys;
        public string[] ruleFamilyIds;
        public string[] relationElementIds;
        public string[] phenomenonIds;
        public string[] relicIds;
        public string[] organizationAuthorizationVersionIds;
        public CharterAuthorityRequirement[] authorityRequirements;
        public string[] nodeTypeIds;
        public string[] nodeIds;
        public CharterPropagationBoundaryReference[] propagationBoundaries;
        public string[] realitySupplyIds;
        public CharterCommitReference[] commits;
        public string[] worldVariableIds;
        public CharterConflictReference[] conflicts;
        public string[] worldEventIds;
        public string[] environmentProfileIds;
        public string[] ruleEntryIds;

        public bool HasDeclaredAuthority =>
            displayNameKeys != null && ruleFamilyIds != null && relationElementIds != null &&
            phenomenonIds != null && relicIds != null && organizationAuthorizationVersionIds != null &&
            authorityRequirements != null && nodeTypeIds != null && nodeIds != null &&
            propagationBoundaries != null && realitySupplyIds != null && commits != null &&
            worldVariableIds != null && conflicts != null && worldEventIds != null &&
            environmentProfileIds != null;

        public bool ContainsDisplayNameKey(string id) => Contains(displayNameKeys, id);
        public bool ContainsRuleFamily(string id) => Contains(ruleFamilyIds, id);
        public bool ContainsRelationElement(string id) => Contains(relationElementIds, id);
        public bool ContainsPhenomenon(string id) => Contains(phenomenonIds, id);
        public bool ContainsRelic(string id) => Contains(relicIds, id);
        public bool ContainsOrganizationAuthorizationVersion(string id) => Contains(organizationAuthorizationVersionIds, id);
        public bool ContainsNodeType(string id) => Contains(nodeTypeIds, id);
        public bool ContainsNode(string id) => Contains(nodeIds, id);
        public bool ContainsRealitySupply(string id) => Contains(realitySupplyIds, id);
        public bool ContainsWorldVariable(string id) => Contains(worldVariableIds, id);
        public bool ContainsWorldEvent(string id) => Contains(worldEventIds, id);
        public bool ContainsEnvironmentProfile(string id) => Contains(environmentProfileIds, id);
        public bool ContainsRuleEntry(string id) => Contains(ruleEntryIds, id);

        public CharterAuthorityRequirement FindAuthority(string id) =>
            Find(authorityRequirements, value => value.authorityId, id);

        public CharterPropagationBoundaryReference FindPropagationBoundary(string id) =>
            Find(propagationBoundaries, value => value.propagationBoundaryProfileId, id);

        public CharterCommitReference FindCommit(string id) =>
            Find(commits, value => value.commitId, id);

        public CharterConflictReference FindConflict(string id) =>
            Find(conflicts, value => value.conflictProfileId, id);

        private static bool Contains(string[] ids, string id) =>
            !string.IsNullOrWhiteSpace(id) && ids != null && ids.Contains(id, StringComparer.Ordinal);

        private static T Find<T>(T[] values, Func<T, string> getId, string id)
            where T : class
        {
            if (values == null || string.IsNullOrWhiteSpace(id))
                return null;

            T match = null;
            foreach (var value in values)
            {
                if (value == null || !string.Equals(getId(value), id, StringComparison.Ordinal))
                    continue;
                if (match != null)
                    return null;
                match = value;
            }

            return match;
        }
    }

    [Serializable]
    public sealed class CharterAuthorityRequirement
    {
        public string authorityId;
        public string relicId;
        public string[] organizationAuthorizationVersionIds;
    }

    [Serializable]
    public sealed class CharterPropagationBoundaryReference
    {
        public string propagationBoundaryProfileId;
        public string[] allowedCoverageIds;
    }

    [Serializable]
    public sealed class CharterCommitReference
    {
        public string commitId;
        public string[] realitySupplyIds;
    }

    [Serializable]
    public sealed class CharterConflictReference
    {
        public string conflictProfileId;
        public string[] crossTierChallengeGrantIds;
    }
}
