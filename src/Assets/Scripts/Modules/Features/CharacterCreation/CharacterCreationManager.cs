using TianZhang.Entity;
using TianZhang.Game.CharacterCreation;

namespace TianZhang.Features.CharacterCreation
{
    public static class CharacterCreationManager
    {
        public static CharacterData CreateProfile(
            CharacterCreationDraft draft,
            CharacterCreationPointBuyConfig pointBuyConfig)
        {
            return CharacterCreationRules.BuildCharacterData(draft, pointBuyConfig);
        }
    }
}
