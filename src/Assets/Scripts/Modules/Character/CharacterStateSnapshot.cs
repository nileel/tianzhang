namespace TianZhang.Character
{
    /// <summary>Read-only aggregate passed across module boundaries instead of a mutable Character implementation.</summary>
    public sealed class CharacterStateSnapshot
    {
        public CharacterStateSnapshot(CharacterIdentitySnapshot identity, CharacterAttributesSnapshot attributes,
            CharacterResourcesSnapshot resources, AbilityLoadoutSnapshot abilityLoadout, CharacterProgressionSnapshot progression,
            string mainEquipmentBasicAttackProfileId, string unarmedBasicAttackProfileId)
        {
            Identity = identity ?? throw new System.ArgumentNullException(nameof(identity));
            Attributes = attributes ?? throw new System.ArgumentNullException(nameof(attributes));
            Resources = resources ?? throw new System.ArgumentNullException(nameof(resources));
            AbilityLoadout = abilityLoadout ?? throw new System.ArgumentNullException(nameof(abilityLoadout));
            Progression = progression ?? throw new System.ArgumentNullException(nameof(progression));
            MainEquipmentBasicAttackProfileId = mainEquipmentBasicAttackProfileId;
            UnarmedBasicAttackProfileId = unarmedBasicAttackProfileId;
        }
        public CharacterIdentitySnapshot Identity { get; }
        public CharacterAttributesSnapshot Attributes { get; }
        public CharacterResourcesSnapshot Resources { get; }
        public AbilityLoadoutSnapshot AbilityLoadout { get; }
        public CharacterProgressionSnapshot Progression { get; }
        public string MainEquipmentBasicAttackProfileId { get; }
        public string UnarmedBasicAttackProfileId { get; }
    }
}
