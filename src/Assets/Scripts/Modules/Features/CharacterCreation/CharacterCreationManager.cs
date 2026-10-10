using TianZhang.Entity;
using TianZhang.Content;
using TianZhang.Game.CharacterCreation;

namespace TianZhang.Features.CharacterCreation
{
    public static class CharacterCreationManager
    {
        public static CharacterData CreateProfile(
            CharacterCreationDraft draft,
            CharacterCreationPointBuyConfig pointBuyConfig, CombatRuleSetData growthRules = null)
        {
            return CharacterCreationRules.BuildCharacterData(draft, pointBuyConfig, growthRules);
        }
    }
}
