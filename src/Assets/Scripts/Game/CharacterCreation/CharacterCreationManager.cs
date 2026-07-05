using TianZhang.Entity;

namespace TianZhang.Game.CharacterCreation
{
    public static class CharacterCreationManager
    {
        public static CharacterData CreateProfile(CharacterCreationDraft draft)
        {
            return CharacterCreationRules.BuildCharacterData(draft);
        }

        public static CharacterData BeginNewGame(CharacterCreationDraft draft, GameSession session = null)
        {
            var profile = CreateProfile(draft);
            var origin = CharacterCreationCatalog.FindOrigin(draft.OriginId);
            var startNodeId = origin != null ? origin.StartNodeId : "jiangzuo_hub";

            if (session != null)
                session.BeginNewGame(profile, startNodeId);
            else if (GameSession.Instance != null)
                GameSession.Instance.BeginNewGame(profile, startNodeId);

            return profile;
        }
    }
}
