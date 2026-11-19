using System;
using TianZhang.Entity;
using TianZhang.Content;
using TianZhang.Game.CharacterCreation;
using UnityEngine;

namespace TianZhang.Features.CharacterCreation
{
    public sealed class CharacterCreationController : MonoBehaviour
    {
        [SerializeField] private CharacterCreationView view;
        private CombatRuleSetData growthRules;
        private CharacterCreationDraft draft;
        private CharacterCreationPointBuyConfig pointBuyConfig;
        private Action<string, CharacterData, string> completed;

        public CharacterCreationDraft Draft => draft;
        public bool IsJindanTrial => !string.IsNullOrEmpty(TrialPresetId);
        public string TrialPresetId { get; private set; }
        public void SelectStartMode(bool trial)
        {
            SelectTrialPreset(trial ? JindanDemoCatalogData.PlayerFirePresetId : null);
        }

        private void SelectTrialPreset(string preset)
        {
            TrialPresetId = preset;
            view?.RefreshStartMode(preset);
            RefreshOrigin();
        }

        public void Configure(
            CharacterCreationView characterCreationView,
            CharacterCreationPointBuyConfig characterCreationPointBuyConfig,
            Action<string, CharacterData, string> onCompleted, CombatRuleSetData combatGrowthRules = null)
        {
            growthRules = combatGrowthRules ?? throw new ArgumentNullException(nameof(combatGrowthRules));
            view = characterCreationView;
            pointBuyConfig = characterCreationPointBuyConfig ??
                throw new ArgumentNullException(nameof(characterCreationPointBuyConfig));
            completed = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
            draft = CharacterCreationCatalog.CreateDefaultDraft();
            view?.Configure(Submit, id => { SelectOrigin(id); }, SelectTrialPreset);
            view?.ConfigureReturnToMenu(ReturnToMenu);
            view?.ConfigureStartMode(SelectStartMode);
            SelectStartMode(false);
        }

        public void Open(string suggestedSlotId = "slot1")
        {
            if (draft == null) draft = CharacterCreationCatalog.CreateDefaultDraft();
            view?.Show(draft, suggestedSlotId);
            RefreshOrigin();
        }

        public bool SelectOrigin(string originId)
        {
            if (draft == null || CharacterCreationCatalog.FindOrigin(originId) == null) return false;
            draft.OriginId = originId;
            RefreshOrigin();
            return true;
        }

        public void ReturnToMenu()
        {
            view?.Hide();
        }

        private void RefreshOrigin()
        {
            view?.RefreshOrigin(draft, CharacterCreationRules.Validate(draft, pointBuyConfig));
        }

        public void Submit(string slotId, string characterName)
        {
            if (draft == null) draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.CharacterName = characterName;
            CharacterCreationValidationResult validation = CharacterCreationRules.Validate(draft, pointBuyConfig);
            if (!validation.IsValid)
            {
                view?.ShowFailure(string.Join("\n", validation.Errors));
                return;
            }

            CharacterData profile = CharacterCreationManager.CreateProfile(draft, pointBuyConfig, growthRules);
            OriginOption origin = CharacterCreationCatalog.FindOrigin(draft.OriginId);
            completed(slotId, profile, origin == null ? "jiangzuo_hub" : origin.StartNodeId);
        }

        public void ShowFailure(string reason)
        {
            view?.ShowFailure(reason);
        }
    }
}
