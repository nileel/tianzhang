using System;
using System.Linq;
using TianZhang.Character;
using TianZhang.Content;
using TianZhang.Cultivation;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Bootstrap
{
    /// <summary>Initializes the approved trial configuration as persistent player state.</summary>
    public static class JindanDemoNewGame
    {
        public static void Create(string name, JindanDemoCatalogData catalog, CombatRuleSetData rules,
            out CharacterRuntimeProfile player, out CultivationState cultivation)
        {
            var preset = RequirePreset(catalog, JindanDemoCatalogData.PlayerFirePresetId, catalog?.contentVersion);
            var source = preset.sourceCharacterDefinition;
            var attributes = new CharacterAttributes(source.rootBone, source.physique, source.spirit, source.mind,
                source.reaction, source.talent, source.fortune, preset.actionPointMaximumUnits,
                preset.movementEfficiencyMilliUnits, true);
            var stats = attributes.Derive(preset.realmMultiplier, CharacterAttributeBonuses.Empty);
            var spells = preset.equippedArtProfileIds.Concat(new[] { "spell_feixingzhi" }).Distinct().ToArray();
            var loadout = new AbilityLoadout(spells, Array.Empty<string>(), 5, 2);
            foreach (string spell in spells) loadout.TryEquipSpell(spell);
            player = new CharacterRuntimeProfile(new CharacterIdentity("player", name), attributes,
                new CharacterResources(stats.MaxHealth, stats.MaxHealth, preset.manaMaximum, preset.manaMaximum),
                loadout, new CharacterProgressionRef(preset.sourceGongFaId, preset.realmSubstage, preset.realmMultiplier),
                CharacterCombatModifiers.Empty, null, null, AppearanceProfileData.NoneId,
                visibleRootId: "root_fire_middle", visibleRootGrade: "中品", visibleRootElement: "火",
                growthRules: rules, unarmedAttackFamilyId: "body_human");
            cultivation = CreateCultivation(preset);
        }

        public static void Validate(string presetId, string version, CharacterRuntimeProfile player,
            CultivationState cultivation, JindanDemoCatalogData catalog)
        {
            if (string.IsNullOrEmpty(presetId) && string.IsNullOrEmpty(version)) return;
            var preset = RequirePreset(catalog, presetId, version);
            if (player == null || player.Identity.CharacterId != "player" ||
                player.Progression.RealmStage != preset.realmSubstage || player.Progression.GongFaId != preset.sourceGongFaId ||
                player.Progression.RealmMultiplier != preset.realmMultiplier || player.VisibleRootElement != "火" ||
                player.VisibleRootGrade != "中品" || player.Resources.MaximumSpirit != preset.manaMaximum ||
                player.AbilityLoadout.SpellSlots != 5 || player.AbilityLoadout.SkillSlots != 2 ||
                !player.AbilityLoadout.KnownSpells.Contains("spell_feixingzhi") ||
                !player.AbilityLoadout.EquippedSpells.Contains("spell_feixingzhi") || cultivation == null ||
                JsonUtility.ToJson(cultivation.CaptureFoundationPurpleMansionSaveData()) !=
                JsonUtility.ToJson(CreateCultivation(preset).CaptureFoundationPurpleMansionSaveData()))
                throw new ArgumentException("jindan_player_configuration_invalid");
        }

        private static JindanDemoPresetDefinition RequirePreset(JindanDemoCatalogData catalog, string id, string version)
        {
            if (catalog == null || !catalog.Validate(out _) || id != JindanDemoCatalogData.PlayerFirePresetId ||
                version != catalog.contentVersion) throw new ArgumentException("jindan_player_preset_invalid");
            return catalog.GetPreset(id);
        }

        private static CultivationState CreateCultivation(JindanDemoPresetDefinition preset)
        {
            // Unity embeds absent optional records as empty objects in the imported asset.
            // This approved start has no running cultivation or retreat; normalize only its copy.
            var definition = UnityEngine.Object.Instantiate(preset.foundationState);
            FoundationPurpleMansionSaveData state;
            try
            {
                if (!string.IsNullOrEmpty(definition.cultivationActionState?.actionStateId) ||
                    !string.IsNullOrEmpty(definition.closedRetreatPlan?.actionStateId))
                    throw new ArgumentException("jindan_start_has_active_cultivation");
                definition.cultivationActionState = null;
                definition.closedRetreatPlan = null;
                state = CultivationState.FromDefinition(definition).CaptureFoundationPurpleMansionSaveData();
            }
            finally
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(definition);
                else UnityEngine.Object.DestroyImmediate(definition);
            }
            string Map(string id) => JindanDemoCatalogData.PlayerInstanceId(preset.characterId, id);
            state.characterId = "player";
            state.foundationState.foundationInstanceId = Map(state.foundationState.foundationInstanceId);
            foreach (var mansion in state.mansionStates)
            {
                mansion.mansionInstanceId = Map(mansion.mansionInstanceId);
                mansion.mansionBodyEffectBindingId = Map(mansion.mansionBodyEffectBindingId);
                mansion.guardianAbilityInstanceId = Map(mansion.guardianAbilityInstanceId);
            }
            foreach (var effect in state.effectBindings)
            { effect.effectBindingId = Map(effect.effectBindingId); effect.carrierId = Map(effect.carrierId); }
            foreach (var guardian in state.guardianAbilities)
            {
                guardian.abilityInstanceId = Map(guardian.abilityInstanceId);
                guardian.mansionInstanceId = Map(guardian.mansionInstanceId);
                guardian.effectBindingIds = guardian.effectBindingIds.Select(Map).ToArray();
            }
            var formation = state.jindanLock.formationSnapshot;
            formation.foundationInstanceId = Map(formation.foundationInstanceId);
            foreach (var mansion in formation.mansionStates)
            {
                mansion.mansionBodyEffectBindingId = Map(mansion.mansionBodyEffectBindingId);
                mansion.guardianAbilityInstanceId = Map(mansion.guardianAbilityInstanceId);
            }
            return CultivationState.FromFoundationPurpleMansionSaveData(state);
        }
    }
}
