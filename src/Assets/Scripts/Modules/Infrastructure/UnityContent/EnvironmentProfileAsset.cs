using TianZhang.Content;
using UnityEngine;

namespace TianZhang.Infrastructure.UnityContent
{
    /// <summary>Unity asset adapter for a validated, immutable environment definition.</summary>
    [CreateAssetMenu(fileName = "EnvironmentProfile_", menuName = "天章/环境档案数据")]
    public sealed class EnvironmentProfileAsset : ScriptableObject, IEnvironmentProfileDefinitionSource
    {
        public string profileId;
        public int unitsPerRange;
        public int maxQueryRange;
        public EnvironmentDirectedEdge[] directedEdges;
        public string[] surfacePrototypeRefs;
        public EnvironmentPhenomenonChannelData[] phenomenonChannels;
        public EnvironmentPhenomenonPairing[] phenomenonPairs;
        public string[] elementRelationRefs;

        public bool TryCreateDefinition(out EnvironmentProfileDefinition definition, out string reason)
        {
            definition = new EnvironmentProfileDefinition
            {
                profileId = profileId,
                unitsPerRange = unitsPerRange,
                maxQueryRange = maxQueryRange,
                directedEdges = directedEdges,
                surfacePrototypeRefs = surfacePrototypeRefs,
                phenomenonChannels = phenomenonChannels,
                phenomenonPairs = phenomenonPairs,
                elementRelationRefs = elementRelationRefs,
            };
            return EnvironmentProfileRuntime.TryCreate(definition, out _, out reason);
        }

        public void Apply(EnvironmentProfileDefinition definition)
        {
            profileId = definition.profileId;
            unitsPerRange = definition.unitsPerRange;
            maxQueryRange = definition.maxQueryRange;
            directedEdges = definition.directedEdges;
            surfacePrototypeRefs = definition.surfacePrototypeRefs;
            phenomenonChannels = definition.phenomenonChannels;
            phenomenonPairs = definition.phenomenonPairs;
            elementRelationRefs = definition.elementRelationRefs;
        }
    }
}
