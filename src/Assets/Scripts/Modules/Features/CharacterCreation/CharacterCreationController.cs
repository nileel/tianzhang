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
            view?.Configure(Submit, id => { SelectOrigin(id); });
        }

        public void Open()
        {
            if (draft == null) draft = CharacterCreationCatalog.CreateDefaultDraft();
            view?.Show(draft);
            RefreshOrigin();
        }

        public bool SelectOrigin(string originId)
        {
            if (draft == null || CharacterCreationCatalog.FindOrigin(originId) == null) return false;
            draft.OriginId = originId;
            RefreshOrigin();
            return true;
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
