namespace TianZhang.World
{
    public enum WorldNodeType
    {
        RegionHub,
        City,
        Sect,
        Market,
        DungeonEntrance,
        WildEncounter,
        SpecialLocation
    }

    [System.Serializable]
    public class WorldNodeDefinition
    {
        public string id;
        public string regionId;
        public string displayName;
        public WorldNodeType nodeType;
        public string[] connectedNodeIds;
        public string settlementId;
        public string[] adventureIds;
    }
}