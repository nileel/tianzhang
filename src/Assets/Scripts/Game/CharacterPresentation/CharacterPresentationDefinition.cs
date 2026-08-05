using UnityEngine;

namespace TianZhang.Game.CharacterPresentation
{
    [CreateAssetMenu(
        fileName = "CharacterPresentationDefinition",
        menuName = "天章/角色展示/人物展示定义")]
    public sealed class CharacterPresentationDefinition : ScriptableObject
    {
        public string characterId;
        public string displayNameTraditional;
        public string daoTitleTraditional;
        public Sprite characterFullBody;
        public Sprite dialogueOverride;
        public Sprite profileBackground16x9;
        public Sprite profileFx;
        public Sprite nameArt;
        public Sprite daoTitleArt;
        public Sprite seal;
        public Sprite staticPreview16x9;

        public Sprite DialoguePortrait => dialogueOverride != null
            ? dialogueOverride
            : characterFullBody;

        public bool TryValidate(out string reason)
        {
            if (string.IsNullOrWhiteSpace(characterId))
            {
                reason = "Character presentation requires a stable characterId.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(displayNameTraditional))
            {
                reason = $"Character presentation '{characterId}' requires a Traditional Chinese display name.";
                return false;
            }

            if (characterFullBody == null)
            {
                reason = $"Character presentation '{characterId}' requires characterFullBody.";
                return false;
            }

            if (profileBackground16x9 == null)
            {
                reason = $"Character presentation '{characterId}' requires profileBackground16x9.";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
