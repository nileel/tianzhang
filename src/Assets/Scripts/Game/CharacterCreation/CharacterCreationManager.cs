using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Cultivation;
using TianZhang.Entity;

namespace TianZhang.Game.CharacterCreation
{
    public static class CharacterCreationManager
    {
        public static CharacterData CreateProfile(CharacterCreationDraft draft)
        {
            return CharacterCreationRules.BuildCharacterData(draft);
        }

        public static CharacterData BeginNewGame(CharacterCreationDraft draft, GameRuntime runtime = null)
        {
            var profile = CreateProfile(draft);
            var origin = CharacterCreationCatalog.FindOrigin(draft.OriginId);
            var startNodeId = origin != null ? origin.StartNodeId : "jiangzuo_hub";

            GameRuntime target = runtime ?? GameBootstrap.RequireRuntime();
            target.BeginNewGame(
                CharacterRuntimeProfile.FromDefinition("player", profile),
                CultivationState.FromDefinition(profile.foundationPurpleMansionState),
                startNodeId);

            return profile;
        }
    }
}
