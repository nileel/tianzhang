namespace TianZhang.Settlement
{
    public enum SettlementType
    {
        City,
        Sect,
        Cave,
        Market,
        Special
    }

    [System.Serializable]
    public class SettlementDefinition
    {
        public string id;
        public string displayName;
        public SettlementType settlementType;
        public string regionId;
        public string ownerFactionId;
        public string[] availableServices;
        public string[] adventureEntrances;
        public string visualTheme;
    }
}