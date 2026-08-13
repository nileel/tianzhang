using System;
using TianZhang.Entity;
using TianZhang.Game.CharacterCreation;
using UnityEngine;

namespace TianZhang.Features.CharacterCreation
{
    public sealed class CharacterCreationController : MonoBehaviour
    {
        [SerializeField] private CharacterCreationView view;
        private CharacterCreationDraft draft;
        private CharacterCreationPointBuyConfig pointBuyConfig;
        private Action<string, CharacterData, string> completed;

        public CharacterCreationDraft Draft => draft;

        public void Configure(
            CharacterCreationView characterCreationView,
            CharacterCreationPointBuyConfig characterCreationPointBuyConfig,
            Action<string, CharacterData, string> onCompleted)
        {
            view = characterCreationView;
            pointBuyConfig = characterCreationPointBuyConfig ??
                throw new ArgumentNullException(nameof(characterCreationPointBuyConfig));
            completed = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
            draft = CharacterCreationCatalog.CreateDefaultDraft();
            view?.Configure(Submit);
        }

        public void Open()
        {
            if (draft == null) draft = CharacterCreationCatalog.CreateDefaultDraft();
            view?.Show(draft);
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

            CharacterData profile = CharacterCreationManager.CreateProfile(draft, pointBuyConfig);
            OriginOption origin = CharacterCreationCatalog.FindOrigin(draft.OriginId);
            completed(slotId, profile, origin == null ? "jiangzuo_hub" : origin.StartNodeId);
        }

        public void ShowFailure(string reason)
        {
            view?.ShowFailure(reason);
        }
    }
}
