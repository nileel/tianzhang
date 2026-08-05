using System.Collections;
using NUnit.Framework;
using TianZhang.Game.CharacterPresentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TianZhang.Tests
{
    public sealed class CharacterPresentationPlayModeTests
    {
        [UnityTest]
        public IEnumerator PrototypeBuildsBothSurfacesAndSwitchesWithoutChangingPortraitIdentity()
        {
            CharacterPresentationDefinition definition = CreateRuntimeDefinition();
            var root = new GameObject("CharacterPresentationPlayModeTest");
            CharacterPresentationPrototypeBootstrap bootstrap =
                root.AddComponent<CharacterPresentationPrototypeBootstrap>();
            bootstrap.definition = definition;

            try
            {
                Assert.IsTrue(bootstrap.BuildPresentation());
                yield return null;

                Assert.IsNotNull(bootstrap.PresentationCanvas);
                CanvasScaler scaler = bootstrap.PresentationCanvas.GetComponent<CanvasScaler>();
                Assert.AreEqual(new Vector2(1920f, 1080f), scaler.referenceResolution);
                Assert.IsTrue(bootstrap.ProfileView.gameObject.activeSelf);
                Assert.IsFalse(bootstrap.DialogueView.gameObject.activeSelf);
                Assert.AreSame(
                    bootstrap.ProfileView.CharacterImage.sprite,
                    bootstrap.DialogueView.CharacterImage.sprite);
                Assert.AreSame(definition.characterFullBody, bootstrap.ProfileView.CharacterImage.sprite);

                bootstrap.ShowDialogue();
                yield return null;

                Assert.IsFalse(bootstrap.ProfileView.gameObject.activeSelf);
                Assert.IsTrue(bootstrap.DialogueView.gameObject.activeSelf);
                Assert.IsNotNull(bootstrap.DialogueView.DialogueMask);
                Assert.AreSame(definition.characterFullBody, bootstrap.DialogueView.CharacterImage.sprite);
            }
            finally
            {
                Object.Destroy(root);
                DestroyDefinition(definition);
            }
        }

        private static CharacterPresentationDefinition CreateRuntimeDefinition()
        {
            var definition = ScriptableObject.CreateInstance<CharacterPresentationDefinition>();
            definition.characterId = "test_character";
            definition.displayNameTraditional = "測試";
            definition.daoTitleTraditional = "測試真君";
            definition.characterFullBody = CreateSprite(Color.white, 8, 16);
            definition.profileBackground16x9 = CreateSprite(new Color(0.9f, 0.86f, 0.76f), 16, 9);
            definition.profileFx = CreateSprite(new Color(0.4f, 0.3f, 0.15f, 0.5f), 16, 9);
            definition.nameArt = CreateSprite(Color.black, 4, 8);
            definition.daoTitleArt = CreateSprite(Color.black, 2, 8);
            definition.seal = CreateSprite(new Color(0.6f, 0.1f, 0.05f), 4, 4);
            return definition;
        }

        private static Sprite CreateSprite(Color color, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        private static void DestroyDefinition(CharacterPresentationDefinition definition)
        {
            Sprite[] sprites =
            {
                definition.characterFullBody,
                definition.profileBackground16x9,
                definition.profileFx,
                definition.nameArt,
                definition.daoTitleArt,
                definition.seal,
            };

            foreach (Sprite sprite in sprites)
            {
                if (sprite == null)
                    continue;
                Texture2D texture = sprite.texture;
                Object.Destroy(sprite);
                Object.Destroy(texture);
            }

            Object.Destroy(definition);
        }
    }
}
