using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Features.Adventure;
using TianZhang.Features.CombatPresentation;
using TianZhang.Game.CharacterPresentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TianZhang.Tests
{
    public sealed class CharacterPresentationViewTests
    {
        [Test]
        public void FuYuanDefinitionKeepsTraditionalTextAndSeparatedLayers()
        {
            CharacterPresentationDefinition definition = LoadDefinition();

            Assert.AreEqual("npc_fu_yuan", definition.characterId);
            Assert.AreEqual("苻淵", definition.displayNameTraditional);
            Assert.AreEqual("含弘真君", definition.daoTitleTraditional);
            Assert.IsTrue(definition.TryValidate(out string reason), reason);
            Assert.IsNotNull(definition.characterFullBody);
            Assert.IsNull(definition.dialogueOverride);
            Assert.AreSame(definition.characterFullBody, definition.DialoguePortrait);
            Assert.IsNotNull(definition.profileBackground16x9);
            Assert.IsNotNull(definition.profileFx);
            Assert.IsNotNull(definition.nameArt);
            Assert.IsNotNull(definition.daoTitleArt);
            Assert.IsNotNull(definition.seal);
            Assert.IsNotNull(definition.staticPreview16x9);
            Assert.AreEqual(1920, definition.profileBackground16x9.texture.width);
            Assert.AreEqual(1080, definition.profileBackground16x9.texture.height);
            Assert.AreEqual(1920, definition.profileFx.texture.width);
            Assert.AreEqual(1080, definition.profileFx.texture.height);
        }

        [Test]
        public void ProfileAndDialogueReuseTheSameFullBodySprite()
        {
            CharacterPresentationDefinition definition = LoadDefinition();
            GameObject canvasGo = CreateCanvas();
            try
            {
                CharacterPresentationView profile = CreateView(
                    canvasGo.transform,
                    "ProfileTestView",
                    definition,
                    CharacterPresentationSurface.Profile);
                CharacterPresentationView dialogue = CreateView(
                    canvasGo.transform,
                    "DialogueTestView",
                    definition,
                    CharacterPresentationSurface.Dialogue);

                Assert.AreSame(definition.characterFullBody, profile.CharacterImage.sprite);
                Assert.AreSame(definition.characterFullBody, dialogue.CharacterImage.sprite);
                Assert.AreSame(profile.CharacterImage.sprite, dialogue.CharacterImage.sprite);
                Assert.IsNotNull(profile.BackgroundImage);
                Assert.IsNotNull(profile.ProfileFxImage);
                Assert.IsNotNull(profile.NameArtImage);
                Assert.IsNotNull(profile.DaoTitleArtImage);
                Assert.IsNotNull(profile.SealImage);
                Assert.IsNull(profile.DialogueMask);
                Assert.IsNull(dialogue.BackgroundImage);
                Assert.IsNull(dialogue.NameArtImage);
                Assert.IsNull(dialogue.DaoTitleArtImage);
                Assert.IsNotNull(dialogue.DialogueMask);
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
            }
        }

        [Test]
        public void PrototypeSceneIsIsolatedFromFormalBuildSettingsAndOwners()
        {
            Scene scene = EditorSceneManager.OpenScene(
                CharacterPresentationPrototypeBuilder.ScenePath,
                OpenSceneMode.Single);

            Assert.IsTrue(scene.IsValid());
            CharacterPresentationPrototypeBootstrap bootstrap =
                Object.FindFirstObjectByType<CharacterPresentationPrototypeBootstrap>();
            Assert.IsNotNull(bootstrap);
            Assert.AreSame(LoadDefinition(), bootstrap.definition);
            Assert.IsNull(Object.FindFirstObjectByType<AdventureController>());
            Assert.IsNull(Object.FindFirstObjectByType<CombatHudPresenter>());
            CollectionAssert.DoesNotContain(
                EditorBuildSettings.scenes.Where(entry => entry.enabled).Select(entry => entry.path),
                CharacterPresentationPrototypeBuilder.ScenePath);
        }

        private static CharacterPresentationDefinition LoadDefinition()
        {
            CharacterPresentationDefinition definition =
                AssetDatabase.LoadAssetAtPath<CharacterPresentationDefinition>(
                    CharacterPresentationPrototypeBuilder.DefinitionPath);
            Assert.IsNotNull(definition, "Run CharacterPresentationPrototypeBuilder before this test.");
            return definition;
        }

        private static GameObject CreateCanvas()
        {
            var go = new GameObject(
                "CharacterPresentationTestCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return go;
        }

        private static CharacterPresentationView CreateView(
            Transform parent,
            string name,
            CharacterPresentationDefinition definition,
            CharacterPresentationSurface surface)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CharacterPresentationView));
            go.transform.SetParent(parent, false);
            CharacterPresentationView view = go.GetComponent<CharacterPresentationView>();
            Assert.IsTrue(view.Initialize(definition, surface, out string reason), reason);
            return view;
        }
    }
}
