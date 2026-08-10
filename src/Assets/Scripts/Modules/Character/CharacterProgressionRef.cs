namespace TianZhang.Character
{
    /// <summary>References to progression definitions; Cultivation owns progression transitions.</summary>
    public sealed class CharacterProgressionRef
    {
        public CharacterProgressionRef(string gongFaId, string realmStage, float realmMultiplier)
        {
            GongFaId = gongFaId ?? string.Empty; RealmStage = realmStage ?? string.Empty;
            RealmMultiplier = realmMultiplier > 0f ? realmMultiplier : 1f;
        }
        public string GongFaId { get; private set; } public string RealmStage { get; private set; }
        public float RealmMultiplier { get; private set; }
        public CharacterProgressionSnapshot Capture() { return new CharacterProgressionSnapshot(GongFaId, RealmStage, RealmMultiplier); }
        public void Restore(CharacterProgressionSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            GongFaId = snapshot.GongFaId; RealmStage = snapshot.RealmStage; RealmMultiplier = snapshot.RealmMultiplier > 0f ? snapshot.RealmMultiplier : 1f;
        }
    }
    public sealed class CharacterProgressionSnapshot
    {
        public CharacterProgressionSnapshot(string gongFaId, string realmStage, float realmMultiplier)
        { GongFaId = gongFaId ?? string.Empty; RealmStage = realmStage ?? string.Empty; RealmMultiplier = realmMultiplier; }
        public string GongFaId { get; } public string RealmStage { get; } public float RealmMultiplier { get; }
    }
}
