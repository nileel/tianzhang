using System;
using UnityEngine;

namespace TianZhang.Tactical
{
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

    [CreateAssetMenu(fileName = "EnvironmentProfile_", menuName = "天章/环境档案数据")]
    public class EnvironmentProfileData : ScriptableObject
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
