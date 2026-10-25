namespace TianZhang.Gameplay.Contracts
{
    /// <summary>Read-only state from the committed session; preview damage is a separate prediction.</summary>
    public sealed class CombatSceneObjectSnapshot
    {
        public CombatSceneObjectSnapshot(string id, string contentType, string displayName, string materialType,
            int durability, int maxDurability, int state, string residueType, int predictedDamage = 0)
        {
            Id = id; ContentType = contentType; DisplayName = displayName; MaterialType = materialType;
            Durability = durability; MaxDurability = maxDurability; State = state;
            ResidueType = residueType; PredictedDamage = predictedDamage;
        }
        public string Id { get; }
        public string ContentType { get; }
        public string DisplayName { get; }
        public string MaterialType { get; }
        public int Durability { get; }
        public int MaxDurability { get; }
        /// <summary>0 intact, 1 damaged, 2 fixed residue.</summary>
        public int State { get; }
        public string ResidueType { get; }
        public int PredictedDamage { get; }
    }
}
