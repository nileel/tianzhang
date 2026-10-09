using TianZhang.Content;
using TianZhang.Entity;

namespace TianZhang.Character
{
    /// <summary>Character-module composition root. It does not own scene, UI, CTB or Cultivation state.</summary>
    public sealed class CharacterRuntimeProfile
    {
        public CharacterRuntimeProfile(CharacterIdentity identity, CharacterAttributes attributes, CharacterResources resources,
            AbilityLoadout abilityLoadout, CharacterProgressionRef progression, CharacterCombatModifiers combatModifiers,
            string mainEquipmentBasicAttackProfileId, string unarmedBasicAttackProfileId, string appearanceProfileId,
            string visibleRootId = null, string visibleRootGrade = null, string visibleRootElement = null,
            string mainEquipmentItemId = null, string mainEquipmentInstanceId = null)
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
            UnarmedBasicAttackProfileId = unarmedBasicAttackProfileId;
            AppearanceProfileId = string.IsNullOrWhiteSpace(appearanceProfileId)
                ? throw new System.ArgumentException("Appearance profile ID is required.", nameof(appearanceProfileId))
                : appearanceProfileId;
            VisibleRootId = visibleRootId;
            VisibleRootGrade = visibleRootGrade;
            VisibleRootElement = visibleRootElement;
        }
        public CharacterIdentity Identity { get; } public CharacterAttributes Attributes { get; }
        public CharacterResources Resources { get; } public AbilityLoadout AbilityLoadout { get; }
        public CharacterProgressionRef Progression { get; } public CharacterCombatModifiers CombatModifiers { get; private set; }
        public string MainEquipmentItemId { get; private set; }
        public string MainEquipmentInstanceId { get; private set; }
        public string MainEquipmentBasicAttackProfileId { get; private set; }
        public string UnarmedBasicAttackProfileId { get; private set; }
        public string AppearanceProfileId { get; private set; }
        public string VisibleRootId { get; private set; }
        public string VisibleRootGrade { get; private set; }
        public string VisibleRootElement { get; private set; }

        public bool TryPrepareEquipmentChange(ContentCatalogData catalog, string slotId, string itemId,
            string instanceId, bool instanceOwned, out System.Action commit, out string reason)
        {
            commit = null;
            if (!ValidateEquipment(catalog, slotId, itemId, instanceId, instanceOwned, out ItemData item, out reason)) return false;
            if (MainEquipmentItemId == itemId && MainEquipmentInstanceId == instanceId)
            { reason = "equipment_no_change"; return false; }
            string profileId = item.equipmentBasicAttackProfileId;
            commit = () =>
            {
                MainEquipmentItemId = itemId;
                MainEquipmentInstanceId = instanceId;
                MainEquipmentBasicAttackProfileId = profileId;
                UnarmedBasicAttackProfileId = string.Empty;
            };
            return true;
        }

        public bool ValidateEquipmentState(ContentCatalogData catalog, bool instanceOwned)
        {
            if (string.IsNullOrEmpty(MainEquipmentItemId) && string.IsNullOrEmpty(MainEquipmentInstanceId))
                return string.IsNullOrEmpty(MainEquipmentBasicAttackProfileId) && UnarmedBasicAttackProfileId == "basic_unarmed";
            return string.IsNullOrEmpty(UnarmedBasicAttackProfileId) &&
                ValidateEquipment(catalog, "main_hand", MainEquipmentItemId, MainEquipmentInstanceId, instanceOwned, out ItemData item, out _) &&
                MainEquipmentBasicAttackProfileId == item.equipmentBasicAttackProfileId;
        }

        private bool ValidateEquipment(ContentCatalogData catalog, string slotId, string itemId, string instanceId,
            bool instanceOwned, out ItemData item, out string reason)
        {
            item = null;
            reason = "equipment_slot_invalid";
            if (slotId != "main_hand") return false;
            reason = "equipment_definition_invalid";
            if (catalog == null || !catalog.TryGetEquipment(itemId, out item)) return false;
            reason = "equipment_instance_invalid";
            if (!string.Equals(instanceId, Identity.CharacterId + ":initial:" + itemId, System.StringComparison.Ordinal)) return false;
            reason = "equipment_not_owned";
            if (!instanceOwned) return false;
            reason = "equipment_qualification_invalid";
            if (Progression.RealmStage is not ("练气" or "筑基" or "金丹" or "元婴" or "化神")) return false;
            reason = null;
            return true;
        }

        public static CharacterRuntimeProfile FromDefinition(string characterId, CharacterData definition)
        {
            if (definition == null) throw new System.ArgumentNullException(nameof(definition));
            CharacterAttributes attributes = CharacterAttributes.FromDefinition(definition);
            CharacterCombatModifiers combatModifiers = CharacterCombatModifiers.FromDefinition(definition);
            var progression = new CharacterProgressionRef(definition.gongFaName, definition.realmStage,
                definition.realmMultiplier > 0f ? definition.realmMultiplier : 1f);
            CharacterDerivedAttributes derived = attributes.Derive(progression.RealmMultiplier, combatModifiers.ToAttributeBonuses());
            var loadout = new AbilityLoadout(definition.availableSpells, definition.availableSkills,
                definition.maxSpellSlots, definition.maxSkillSlots);
            foreach (string spell in definition.equippedSpells ?? new string[0]) loadout.TryEquipSpell(spell);
            foreach (string skill in definition.equippedSkills ?? new string[0]) loadout.TryEquipSkill(skill);
            return new CharacterRuntimeProfile(new CharacterIdentity(characterId, definition.charName), attributes,
                new CharacterResources(derived.MaxHealth, derived.MaxHealth, derived.MaxSpirit, derived.MaxSpirit), loadout, progression,
                combatModifiers,
                definition.mainEquipmentBasicAttackProfileId, definition.unarmedBasicAttackProfileId, AppearanceProfileData.NoneId,
                definition.visibleRootId, definition.visibleRootGrade, definition.visibleRootElement);
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
                    snapshot.Attributes.Fortune,
                    snapshot.Attributes.ActionPointMaximumUnits,
                    snapshot.Attributes.MovementEfficiencyMilliUnits,
                    snapshot.Attributes.ActionEconomyInputsPresent),
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
                snapshot.CombatModifiers,
                snapshot.MainEquipmentBasicAttackProfileId,
                snapshot.UnarmedBasicAttackProfileId,
                snapshot.AppearanceProfileId,
                snapshot.VisibleRootId, snapshot.VisibleRootGrade, snapshot.VisibleRootElement,
                snapshot.MainEquipmentItemId, snapshot.MainEquipmentInstanceId);
            profile.Restore(snapshot);
            return profile;
        }

        public CharacterStateSnapshot Capture()
        {
            return new CharacterStateSnapshot(
                Identity.Capture(), Attributes.Capture(), Resources.Capture(), AbilityLoadout.Capture(), Progression.Capture(),
                CombatModifiers,
                MainEquipmentBasicAttackProfileId, UnarmedBasicAttackProfileId, AppearanceProfileId,
                VisibleRootId, VisibleRootGrade, VisibleRootElement, MainEquipmentItemId, MainEquipmentInstanceId);
        }
        public void Restore(CharacterStateSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            RequireRestorable(snapshot);
            Identity.Restore(snapshot.Identity); Attributes.Restore(snapshot.Attributes); Resources.Restore(snapshot.Resources);
            AbilityLoadout.Restore(snapshot.AbilityLoadout); Progression.Restore(snapshot.Progression);
            CombatModifiers = snapshot.CombatModifiers;
            MainEquipmentItemId = snapshot.MainEquipmentItemId;
            MainEquipmentInstanceId = snapshot.MainEquipmentInstanceId;
            MainEquipmentBasicAttackProfileId = snapshot.MainEquipmentBasicAttackProfileId;
            UnarmedBasicAttackProfileId = snapshot.UnarmedBasicAttackProfileId;
            AppearanceProfileId = snapshot.AppearanceProfileId;
            VisibleRootId = snapshot.VisibleRootId;
            VisibleRootGrade = snapshot.VisibleRootGrade;
            VisibleRootElement = snapshot.VisibleRootElement;
        }

        /// <summary>
        /// Full validation of the incoming aggregate before any live field is written, so a failed restore
        /// keeps identity, attributes, resources, loadout, progression and the long-term inputs untouched.
        /// </summary>
        private static void RequireRestorable(CharacterStateSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(snapshot.Identity.CharacterId) ||
                string.IsNullOrWhiteSpace(snapshot.Identity.DisplayName))
            {
                throw new System.ArgumentException("Character identity is invalid.", nameof(snapshot));
            }
            if (snapshot.Resources.MaximumHealth < 0 || snapshot.Resources.CurrentHealth < 0 ||
                snapshot.Resources.CurrentHealth > snapshot.Resources.MaximumHealth ||
                snapshot.Resources.MaximumSpirit < 0 || snapshot.Resources.CurrentSpirit < 0 ||
                snapshot.Resources.CurrentSpirit > snapshot.Resources.MaximumSpirit)
            {
                throw new System.ArgumentException("Character resources are invalid.", nameof(snapshot));
            }
            if (snapshot.AbilityLoadout.SpellSlots < 0 || snapshot.AbilityLoadout.SkillSlots < 0 ||
                !IsEquippedSubsetOfKnown(snapshot.AbilityLoadout.EquippedSpells, snapshot.AbilityLoadout.KnownSpells,
                    snapshot.AbilityLoadout.SpellSlots) ||
                !IsEquippedSubsetOfKnown(snapshot.AbilityLoadout.EquippedSkills, snapshot.AbilityLoadout.KnownSkills,
                    snapshot.AbilityLoadout.SkillSlots))
            {
                throw new System.ArgumentException("Character ability loadout is invalid.", nameof(snapshot));
            }
            if (!(snapshot.Progression.RealmMultiplier > 0f) ||
                float.IsNaN(snapshot.Progression.RealmMultiplier) ||
                float.IsInfinity(snapshot.Progression.RealmMultiplier))
            {
                throw new System.ArgumentException("Character progression is invalid.", nameof(snapshot));
            }
            CharacterAttributes.RequireActionPointMaximumUnits(snapshot.Attributes.ActionPointMaximumUnits);
            CharacterAttributes.RequireMovementEfficiencyMilliUnits(snapshot.Attributes.MovementEfficiencyMilliUnits);
            CharacterAttributes.RequireActionEconomyInputsPresent(snapshot.Attributes.ActionEconomyInputsPresent);
        }

        private static bool IsEquippedSubsetOfKnown(string[] equipped, string[] known, int slots)
        {
            equipped = equipped ?? new string[0];
            known = known ?? new string[0];
            if (equipped.Length > slots) return false;
            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            var knownSet = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (string value in known) if (!string.IsNullOrWhiteSpace(value)) knownSet.Add(value);
            foreach (string value in equipped)
            {
                if (string.IsNullOrWhiteSpace(value) || !seen.Add(value) || !knownSet.Contains(value)) return false;
            }
            return true;
        }
    }
}
