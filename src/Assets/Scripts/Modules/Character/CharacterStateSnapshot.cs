namespace TianZhang.Character
{
    /// <summary>Read-only aggregate passed across module boundaries instead of a mutable Character implementation.</summary>
    public sealed class CharacterStateSnapshot
    {
        public CharacterStateSnapshot(CharacterIdentitySnapshot identity, CharacterAttributesSnapshot attributes,
            CharacterResourcesSnapshot resources, AbilityLoadoutSnapshot abilityLoadout, CharacterProgressionSnapshot progression,
            CharacterCombatModifiers combatModifiers,
            string mainEquipmentBasicAttackProfileId, string unarmedBasicAttackProfileId, string appearanceProfileId,
            string visibleRootId = null, string visibleRootGrade = null, string visibleRootElement = null,
            string mainEquipmentItemId = null, string mainEquipmentInstanceId = null, string unarmedAttackFamilyId = null)
        {
            Identity = identity ?? throw new System.ArgumentNullException(nameof(identity));
            Attributes = attributes ?? throw new System.ArgumentNullException(nameof(attributes));
            Resources = resources ?? throw new System.ArgumentNullException(nameof(resources));
            AbilityLoadout = abilityLoadout ?? throw new System.ArgumentNullException(nameof(abilityLoadout));
            Progression = progression ?? throw new System.ArgumentNullException(nameof(progression));
            CombatModifiers = combatModifiers ?? throw new System.ArgumentNullException(nameof(combatModifiers));
            MainEquipmentItemId = mainEquipmentItemId;
            MainEquipmentInstanceId = mainEquipmentInstanceId;
            MainEquipmentBasicAttackProfileId = mainEquipmentBasicAttackProfileId;
            UnarmedAttackFamilyId = unarmedAttackFamilyId;
            UnarmedBasicAttackProfileId = unarmedBasicAttackProfileId;
            AppearanceProfileId = string.IsNullOrWhiteSpace(appearanceProfileId)
                ? throw new System.ArgumentException("Appearance profile ID is required.", nameof(appearanceProfileId))
                : appearanceProfileId;
            VisibleRootId = visibleRootId;
            VisibleRootGrade = visibleRootGrade;
            VisibleRootElement = visibleRootElement;
        }
        public CharacterIdentitySnapshot Identity { get; }
        public CharacterAttributesSnapshot Attributes { get; }
        public CharacterResourcesSnapshot Resources { get; }
        public AbilityLoadoutSnapshot AbilityLoadout { get; }
        public CharacterProgressionSnapshot Progression { get; }
        public CharacterCombatModifiers CombatModifiers { get; }
        public string MainEquipmentItemId { get; }
        public string MainEquipmentInstanceId { get; }
        public string MainEquipmentBasicAttackProfileId { get; }
        public string UnarmedBasicAttackProfileId { get; }
        public string UnarmedAttackFamilyId { get; }
        public string AppearanceProfileId { get; }
        public string VisibleRootId { get; }
        public string VisibleRootGrade { get; }
        public string VisibleRootElement { get; }
    }
}
