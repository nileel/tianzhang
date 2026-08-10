using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TianZhang.Content
{
    public interface IEnvironmentProfileDefinitionSource
    {
        bool TryCreateDefinition(out EnvironmentProfileDefinition definition, out string reason);
    }

    public enum EnvironmentPhenomenonChannel
    {
        Airflow,
        Visibility,
        Temperature,
        Precipitation,
        SuspendedHazard,
        CloudDischarge,
    }

    [Serializable]
    public struct EnvironmentDirectedEdge
    {
        public int fromQ;
        public int fromR;
        public int toQ;
        public int toR;
        public int metricDistanceUnits;
        public bool allowsMovement;
        public bool allowsEffects;
    }

    [Serializable]
    public struct EnvironmentPhenomenonChannelData
    {
        public EnvironmentPhenomenonChannel channel;
        public string[] phenomenonTypeRefs;
    }

    [Serializable]
    public struct EnvironmentPhenomenonPairing
    {
        public EnvironmentPhenomenonChannel channel;
        public string firstTypeRef;
        public string secondTypeRef;
        public string resultTypeRef;
    }

    public static class EnvironmentRuntimeReasons
    {
        public const string Ok = "";
        public const string ProfileNotConfigured = "environment_profile_not_configured";
        public const string ProfileIdNotConfigured = "environment_profile_id_not_configured";
        public const string QueryLimitsNotConfigured = "query_limits_not_configured";
        public const string DirectedEdgesNotConfigured = "directed_edges_not_configured";
        public const string SurfacePrototypesNotConfigured = "surface_prototypes_not_configured";
        public const string SurfacePrototypeNotConfigured = "surface_prototype_not_configured";
        public const string PhenomenonChannelsNotConfigured = "phenomenon_channels_not_configured";
        public const string DuplicatePhenomenonChannel = "duplicate_phenomenon_channel";
        public const string PhenomenonTypesNotConfigured = "phenomenon_types_not_configured";
        public const string PhenomenonTypeNotConfigured = "phenomenon_type_not_configured";
        public const string PhenomenonPairReferenceNotConfigured = "phenomenon_pair_reference_not_configured";
        public const string DuplicatePhenomenonPair = "duplicate_phenomenon_pair";
        public const string PhenomenonPairNotConfigured = "phenomenon_pair_not_configured";
        public const string ElementRelationsNotConfigured = "element_relations_not_configured";
        public const string ElementRelationNotConfigured = "element_relation_not_configured";
    }

    public sealed class EnvironmentProfileRuntime
    {
        private readonly ReadOnlyCollection<EnvironmentDirectedEdge> directedEdges;
        private readonly ReadOnlyCollection<string> surfacePrototypeRefs;
        private readonly ReadOnlyCollection<string> elementRelationRefs;
        private readonly Dictionary<EnvironmentPhenomenonChannel, HashSet<string>> phenomenonTypes;
        private readonly Dictionary<string, string> phenomenonPairResults;

        private EnvironmentProfileRuntime(
            string profileId,
            EnvironmentDirectedEdge[] directedEdges,
            string[] surfacePrototypeRefs,
            string[] elementRelationRefs,
            Dictionary<EnvironmentPhenomenonChannel, HashSet<string>> phenomenonTypes,
            Dictionary<string, string> phenomenonPairResults,
            int unitsPerRange,
            int maxQueryRange)
        {
            ProfileId = profileId;
            this.directedEdges = Array.AsReadOnly(directedEdges);
            this.surfacePrototypeRefs = Array.AsReadOnly(surfacePrototypeRefs);
            this.elementRelationRefs = Array.AsReadOnly(elementRelationRefs);
            this.phenomenonTypes = phenomenonTypes;
            this.phenomenonPairResults = phenomenonPairResults;
            UnitsPerRange = unitsPerRange;
            MaxQueryRange = maxQueryRange;
        }

        public string ProfileId { get; }
        public int UnitsPerRange { get; }
        public int MaxQueryRange { get; }
        public IReadOnlyList<EnvironmentDirectedEdge> DirectedEdges => directedEdges;
        public IReadOnlyList<string> SurfacePrototypeRefs => surfacePrototypeRefs;
        public IReadOnlyList<string> ElementRelationRefs => elementRelationRefs;

        public bool IsSurfacePrototypeConfigured(string surfacePrototypeRef, out string reason)
        {
            if (!string.IsNullOrWhiteSpace(surfacePrototypeRef) &&
                surfacePrototypeRefs.Contains(surfacePrototypeRef))
            {
                reason = EnvironmentRuntimeReasons.Ok;
                return true;
            }

            reason = EnvironmentRuntimeReasons.SurfacePrototypeNotConfigured;
            return false;
        }

        public bool IsElementRelationConfigured(string elementRelationRef, out string reason)
        {
            if (!string.IsNullOrWhiteSpace(elementRelationRef) &&
                elementRelationRefs.Contains(elementRelationRef))
            {
                reason = EnvironmentRuntimeReasons.Ok;
                return true;
            }

            reason = EnvironmentRuntimeReasons.ElementRelationNotConfigured;
            return false;
        }

        public bool TryResolvePhenomenonPair(
            EnvironmentPhenomenonChannel channel,
            string firstTypeRef,
            string secondTypeRef,
            out string resultTypeRef,
            out string reason)
        {
            resultTypeRef = null;
            if (!phenomenonTypes.TryGetValue(channel, out var allowedTypes) ||
                string.IsNullOrWhiteSpace(firstTypeRef) ||
                string.IsNullOrWhiteSpace(secondTypeRef) ||
                !allowedTypes.Contains(firstTypeRef) ||
                !allowedTypes.Contains(secondTypeRef))
            {
                reason = EnvironmentRuntimeReasons.PhenomenonTypeNotConfigured;
                return false;
            }

            if (string.Equals(firstTypeRef, secondTypeRef, StringComparison.Ordinal))
            {
                resultTypeRef = firstTypeRef;
                reason = EnvironmentRuntimeReasons.Ok;
                return true;
            }

            if (phenomenonPairResults.TryGetValue(CreatePairKey(channel, firstTypeRef, secondTypeRef), out resultTypeRef))
            {
                reason = EnvironmentRuntimeReasons.Ok;
                return true;
            }

            reason = EnvironmentRuntimeReasons.PhenomenonPairNotConfigured;
            return false;
        }

        public static bool TryCreate(
            EnvironmentProfileDefinition profile,
            out EnvironmentProfileRuntime runtime,
            out string reason)
        {
            runtime = null;
            if (profile == null)
                return Fail(EnvironmentRuntimeReasons.ProfileNotConfigured, out reason);
            if (string.IsNullOrWhiteSpace(profile.profileId))
                return Fail(EnvironmentRuntimeReasons.ProfileIdNotConfigured, out reason);
            if (profile.unitsPerRange < 1 || profile.maxQueryRange < 1)
                return Fail(EnvironmentRuntimeReasons.QueryLimitsNotConfigured, out reason);
            if (profile.directedEdges == null || profile.directedEdges.Length == 0)
                return Fail(EnvironmentRuntimeReasons.DirectedEdgesNotConfigured, out reason);

            if (!TryCopyUniqueRefs(
                    profile.surfacePrototypeRefs,
                    EnvironmentRuntimeReasons.SurfacePrototypesNotConfigured,
                    out var surfaces,
                    out reason))
            {
                return false;
            }

            if (!TryCopyUniqueRefs(
                    profile.elementRelationRefs,
                    EnvironmentRuntimeReasons.ElementRelationsNotConfigured,
                    out var elements,
                    out reason) ||
                elements.Length != 5)
            {
                reason = EnvironmentRuntimeReasons.ElementRelationsNotConfigured;
                return false;
            }

            if (!TryCreatePhenomenonTypes(profile.phenomenonChannels, out var types, out reason) ||
                !TryCreatePhenomenonPairResults(profile.phenomenonPairs, types, out var pairs, out reason))
            {
                return false;
            }

            var edges = new EnvironmentDirectedEdge[profile.directedEdges.Length];
            Array.Copy(profile.directedEdges, edges, edges.Length);
            runtime = new EnvironmentProfileRuntime(
                profile.profileId,
                edges,
                surfaces,
                elements,
                types,
                pairs,
                profile.unitsPerRange,
                profile.maxQueryRange);
            reason = EnvironmentRuntimeReasons.Ok;
            return true;
        }

        public static bool TryCreate(
            IEnvironmentProfileDefinitionSource source,
            out EnvironmentProfileRuntime runtime,
            out string reason)
        {
            runtime = null;
            reason = null;
            if (source == null || !source.TryCreateDefinition(out var definition, out reason))
            {
                reason ??= EnvironmentRuntimeReasons.ProfileNotConfigured;
                return false;
            }
            return TryCreate(definition, out runtime, out reason);
        }

        private static bool TryCreatePhenomenonTypes(
            EnvironmentPhenomenonChannelData[] channels,
            out Dictionary<EnvironmentPhenomenonChannel, HashSet<string>> types,
            out string reason)
        {
            types = null;
            if (channels == null || channels.Length == 0)
            {
                reason = EnvironmentRuntimeReasons.PhenomenonChannelsNotConfigured;
                return false;
            }

            var result = new Dictionary<EnvironmentPhenomenonChannel, HashSet<string>>();
            foreach (var configured in channels)
            {
                if (!Enum.IsDefined(typeof(EnvironmentPhenomenonChannel), configured.channel))
                {
                    reason = EnvironmentRuntimeReasons.PhenomenonChannelsNotConfigured;
                    return false;
                }
                if (result.ContainsKey(configured.channel))
                {
                    reason = EnvironmentRuntimeReasons.DuplicatePhenomenonChannel;
                    return false;
                }
                if (!TryCopyUniqueRefs(
                        configured.phenomenonTypeRefs,
                        EnvironmentRuntimeReasons.PhenomenonTypesNotConfigured,
                        out var typeRefs,
                        out reason))
                {
                    return false;
                }

                result.Add(
                    configured.channel,
                    new HashSet<string>(typeRefs, StringComparer.Ordinal));
            }

            foreach (EnvironmentPhenomenonChannel channel in Enum.GetValues(typeof(EnvironmentPhenomenonChannel)))
            {
                if (!result.ContainsKey(channel))
                {
                    reason = EnvironmentRuntimeReasons.PhenomenonChannelsNotConfigured;
                    return false;
                }
            }

            types = result;
            reason = EnvironmentRuntimeReasons.Ok;
            return true;
        }

        private static bool TryCreatePhenomenonPairResults(
            EnvironmentPhenomenonPairing[] pairings,
            IReadOnlyDictionary<EnvironmentPhenomenonChannel, HashSet<string>> types,
            out Dictionary<string, string> results,
            out string reason)
        {
            results = null;
            if (pairings == null)
            {
                reason = EnvironmentRuntimeReasons.PhenomenonPairNotConfigured;
                return false;
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pairing in pairings)
            {
                if (!types.TryGetValue(pairing.channel, out var allowedTypes) ||
                    string.IsNullOrWhiteSpace(pairing.firstTypeRef) ||
                    string.IsNullOrWhiteSpace(pairing.secondTypeRef) ||
                    string.IsNullOrWhiteSpace(pairing.resultTypeRef) ||
                    !allowedTypes.Contains(pairing.firstTypeRef) ||
                    !allowedTypes.Contains(pairing.secondTypeRef) ||
                    !allowedTypes.Contains(pairing.resultTypeRef))
                {
                    reason = EnvironmentRuntimeReasons.PhenomenonPairReferenceNotConfigured;
                    return false;
                }

                string key = CreatePairKey(pairing.channel, pairing.firstTypeRef, pairing.secondTypeRef);
                if (result.ContainsKey(key))
                {
                    reason = EnvironmentRuntimeReasons.DuplicatePhenomenonPair;
                    return false;
                }
                result.Add(key, pairing.resultTypeRef);
            }

            results = result;
            reason = EnvironmentRuntimeReasons.Ok;
            return true;
        }

        private static bool TryCopyUniqueRefs(
            string[] source,
            string emptyReason,
            out string[] result,
            out string reason)
        {
            result = null;
            if (source == null || source.Length == 0)
            {
                reason = emptyReason;
                return false;
            }

            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reference in source)
            {
                if (string.IsNullOrWhiteSpace(reference) || !unique.Add(reference))
                {
                    reason = emptyReason;
                    return false;
                }
            }

            result = new string[unique.Count];
            source.CopyTo(result, 0);
            reason = EnvironmentRuntimeReasons.Ok;
            return true;
        }

        private static string CreatePairKey(
            EnvironmentPhenomenonChannel channel,
            string firstTypeRef,
            string secondTypeRef)
        {
            return string.CompareOrdinal(firstTypeRef, secondTypeRef) <= 0
                ? ((int)channel) + "|" + firstTypeRef + "|" + secondTypeRef
                : ((int)channel) + "|" + secondTypeRef + "|" + firstTypeRef;
        }

        private static bool Fail(string failureReason, out string reason)
        {
            reason = failureReason;
            return false;
        }
    }

    /// <summary>Immutable environment definition. Unity serialization lives in Infrastructure.UnityContent.</summary>
    [Serializable]
    public sealed class EnvironmentProfileDefinition
    {
        public string profileId;
        public int unitsPerRange;
        public int maxQueryRange;
        public EnvironmentDirectedEdge[] directedEdges;
        public string[] surfacePrototypeRefs;
        public EnvironmentPhenomenonChannelData[] phenomenonChannels;
        public EnvironmentPhenomenonPairing[] phenomenonPairs;
        public string[] elementRelationRefs;
    }
}
