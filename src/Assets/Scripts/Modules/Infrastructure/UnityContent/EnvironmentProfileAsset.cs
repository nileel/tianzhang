using TianZhang.Content;
using UnityEngine;

namespace TianZhang.Infrastructure.UnityContent
{
    /// <summary>Unity asset adapter for a validated, immutable environment definition.</summary>
    [CreateAssetMenu(fileName = "EnvironmentProfile_", menuName = "天章/环境档案数据")]
    public sealed class EnvironmentProfileAsset : ScriptableObject, IEnvironmentProfileDefinitionSource
    {
        public string profileId;
        public SpiritRecoveryEnvironment spiritRecoveryEnvironment;
        public int unitsPerRange;
        public int maxQueryRange;
        public EnvironmentBattlefieldCell[] battlefieldCells;
        public EnvironmentDirectedEdge[] directedEdges;
        public string[] surfacePrototypeRefs;
        public EnvironmentPhenomenonChannelData[] phenomenonChannels;
        public EnvironmentPhenomenonPairing[] phenomenonPairs;
        public string[] elementRelationRefs;
        public string mapSchemaVersion;
        public string mapVersion;
        public string directedEdgeOverrides;
        public string physicalSurfaceProfiles;
        public string mapMarkers;
        public EnvironmentSceneObjectConfiguration sceneObjects;
        public EnvironmentFlightConfiguration flightSpace;

        public bool TryCreateDefinition(out EnvironmentProfileDefinition definition, out string reason)
        {
            definition = new EnvironmentProfileDefinition
            {
                profileId = profileId,
                spiritRecoveryEnvironment = spiritRecoveryEnvironment,
                unitsPerRange = unitsPerRange,
                maxQueryRange = maxQueryRange,
                battlefieldCells = battlefieldCells,
                directedEdges = directedEdges,
                surfacePrototypeRefs = surfacePrototypeRefs,
                phenomenonChannels = phenomenonChannels,
                phenomenonPairs = phenomenonPairs,
                elementRelationRefs = elementRelationRefs,
                mapSchemaVersion = mapSchemaVersion,
                mapVersion = mapVersion,
                directedEdgeOverrides = directedEdgeOverrides,
                physicalSurfaceProfiles = physicalSurfaceProfiles,
                mapMarkers = mapMarkers,
                sceneObjects = sceneObjects,
                flightSpace = flightSpace,
            };
            return EnvironmentProfileRuntime.TryCreate(definition, out _, out reason);
        }

        public void Apply(EnvironmentProfileDefinition definition)
        {
            profileId = definition.profileId;
            spiritRecoveryEnvironment = definition.spiritRecoveryEnvironment;
            unitsPerRange = definition.unitsPerRange;
            maxQueryRange = definition.maxQueryRange;
            battlefieldCells = definition.battlefieldCells;
            directedEdges = definition.directedEdges;
            surfacePrototypeRefs = definition.surfacePrototypeRefs;
            phenomenonChannels = definition.phenomenonChannels;
            phenomenonPairs = definition.phenomenonPairs;
            elementRelationRefs = definition.elementRelationRefs;
            mapSchemaVersion = definition.mapSchemaVersion;
            mapVersion = definition.mapVersion;
            directedEdgeOverrides = definition.directedEdgeOverrides;
            physicalSurfaceProfiles = definition.physicalSurfaceProfiles;
            mapMarkers = definition.mapMarkers;
            sceneObjects = definition.sceneObjects;
            flightSpace = definition.flightSpace;
        }
    }
}
