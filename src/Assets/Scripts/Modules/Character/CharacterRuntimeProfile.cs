using TianZhang.Entity;

namespace TianZhang.Character
{
    /// <summary>Character-module composition root. It does not own scene, UI, CTB or Cultivation state.</summary>
    public sealed class CharacterRuntimeProfile
    {
        public CharacterRuntimeProfile(CharacterIdentity identity, CharacterAttributes attributes, CharacterResources resources,
            AbilityLoadout abilityLoadout, CharacterProgressionRef progression,
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
        public CharacterIdentity Identity { get; } public CharacterAttributes Attributes { get; }
        public CharacterResources Resources { get; } public AbilityLoadout AbilityLoadout { get; }
        public CharacterProgressionRef Progression { get; }
        public string MainEquipmentBasicAttackProfileId { get; private set; }
        public string UnarmedBasicAttackProfileId { get; private set; }

        public static CharacterRuntimeProfile FromDefinition(string characterId, CharacterData definition)
        {
            if (definition == null) throw new System.ArgumentNullException(nameof(definition));
            CharacterAttributes attributes = CharacterAttributes.FromDefinition(definition);
            var progression = new CharacterProgressionRef(definition.gongFaName, definition.realmStage,
                definition.realmMultiplier > 0f ? definition.realmMultiplier : 1f);
            CharacterDerivedAttributes derived = attributes.Derive(progression.RealmMultiplier, CharacterAttributeBonuses.Empty);
            var loadout = new AbilityLoadout(definition.availableSpells, definition.availableSkills,
                definition.maxSpellSlots, definition.maxSkillSlots);
            foreach (string spell in definition.equippedSpells ?? new string[0]) loadout.TryEquipSpell(spell);
            foreach (string skill in definition.equippedSkills ?? new string[0]) loadout.TryEquipSkill(skill);
            return new CharacterRuntimeProfile(new CharacterIdentity(characterId, definition.charName), attributes,
                new CharacterResources(derived.MaxHealth, derived.MaxHealth, derived.MaxSpirit, derived.MaxSpirit), loadout, progression,
                definition.mainEquipmentBasicAttackProfileId, definition.unarmedBasicAttackProfileId);
        }

        public static CharacterRuntimeProfile FromSnapshot(CharacterStateSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            var profile = new CharacterRuntimeProfile(
                new CharacterIdentity(snapshot.Identity.CharacterId, snapshot.Identity.DisplayName),
                new CharacterAttributes(
                    snapshot.Attributes.RootBone,
                    snapshot.Attributes.Physique,
                    snapshot.Attributes.Spirit,
                    snapshot.Attributes.Mind,
                    snapshot.Attributes.Reaction,
                    snapshot.Attributes.Talent,
                    snapshot.Attributes.Fortune),
                new CharacterResources(
                    snapshot.Resources.MaximumHealth,
                    snapshot.Resources.CurrentHealth,
                    snapshot.Resources.MaximumSpirit,
                    snapshot.Resources.CurrentSpirit),
                new AbilityLoadout(
                    snapshot.AbilityLoadout.KnownSpells,
                    snapshot.AbilityLoadout.KnownSkills,
                    snapshot.AbilityLoadout.SpellSlots,
                    snapshot.AbilityLoadout.SkillSlots),
                new CharacterProgressionRef(
                    snapshot.Progression.GongFaId,
                    snapshot.Progression.RealmStage,
                    snapshot.Progression.RealmMultiplier),
                snapshot.MainEquipmentBasicAttackProfileId,
                snapshot.UnarmedBasicAttackProfileId);
            profile.Restore(snapshot);
            return profile;
        }

        public CharacterStateSnapshot Capture()
        {
            return new CharacterStateSnapshot(
                Identity.Capture(), Attributes.Capture(), Resources.Capture(), AbilityLoadout.Capture(), Progression.Capture(),
                MainEquipmentBasicAttackProfileId, UnarmedBasicAttackProfileId);
        }
        public void Restore(CharacterStateSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            Identity.Restore(snapshot.Identity); Attributes.Restore(snapshot.Attributes); Resources.Restore(snapshot.Resources);
            AbilityLoadout.Restore(snapshot.AbilityLoadout); Progression.Restore(snapshot.Progression);
            MainEquipmentBasicAttackProfileId = snapshot.MainEquipmentBasicAttackProfileId;
            UnarmedBasicAttackProfileId = snapshot.UnarmedBasicAttackProfileId;
        }
    }
}
