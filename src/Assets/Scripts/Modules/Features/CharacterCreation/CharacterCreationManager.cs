using TianZhang.Entity;

namespace TianZhang.Features.CharacterCreation
{
    public static class CharacterCreationManager
    {
        public static CharacterData CreateProfile(CharacterCreationDraft draft)
        {
            return CharacterCreationRules.BuildCharacterData(draft);
        }

    }
}
