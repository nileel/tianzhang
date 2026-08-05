using System;
using System.Linq;
using TianZhang.Game.CharacterPresentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TianZhang.Editor
{
    public static class CharacterPresentationPrototypeBuilder
    {
        public const string ScenePath = "Assets/Scenes/CharacterPresentationPrototype.unity";
        public const string DefinitionPath = "Assets/Data/CharacterPresentation/FuYuanProfile.asset";

        private const string ArtRoot = "Assets/Art/Characters/PortraitPresentation/FuYuan/";
        private const string CharacterPath = ArtRoot + "FuYuan_CharacterFullBody.png";
        private const string BackgroundPath = ArtRoot + "FuYuan_ProfileBackground_16x9.png";
        private const string FxPath = ArtRoot + "FuYuan_ProfileFx.png";
        private const string NamePath = ArtRoot + "FuYuan_Name_Traditional.png";
        private const string DaoTitlePath = ArtRoot + "FuYuan_DaoTitle_Traditional.png";
        private const string SealPath = ArtRoot + "FuYuan_Seal_HanHong.png";
        private const string PreviewPath = ArtRoot + "FuYuan_ProfilePreview_16x9.png";

        [MenuItem("Tools/天章/生成苻淵宽屏人物展示隔离原型")]
        public static void BuildFuYuanPrototypeScene()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            ConfigureSprite(BackgroundPath, false);
            ConfigureSprite(CharacterPath, true);
            ConfigureSprite(FxPath, true);
            ConfigureSprite(NamePath, true);
            ConfigureSprite(DaoTitlePath, true);
            ConfigureSprite(SealPath, true);
            ConfigureSprite(PreviewPath, false);

            CharacterPresentationDefinition definition = BuildOrUpdateDefinition();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.075f, 0.065f, 0.055f, 1f);

            var root = new GameObject("CharacterPresentationPrototypeRoot");
            CharacterPresentationPrototypeBootstrap bootstrap =
                root.AddComponent<CharacterPresentationPrototypeBootstrap>();
            bootstrap.definition = definition;
            EditorUtility.SetDirty(bootstrap);

            Scene scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"Failed to save character presentation prototype scene: {ScenePath}");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("<color=cyan>苻淵宽屏人物展示隔离原型已生成</color>");
        }

        public static void BuildFuYuanPrototypeSceneForBatchMode()
        {
            BuildFuYuanPrototypeScene();
            ValidateFuYuanPrototypeSceneForBatchMode();
        }

        public static void ValidateFuYuanPrototypeSceneForBatchMode()
        {
            CharacterPresentationDefinition definition =
                AssetDatabase.LoadAssetAtPath<CharacterPresentationDefinition>(DefinitionPath);
            Require(definition != null, $"Missing Fu Yuan presentation definition: {DefinitionPath}");
            Require(definition.TryValidate(out string reason), reason);
            Require(definition.displayNameTraditional == "苻淵", "Fu Yuan display name must use Traditional 淵.");
            Require(definition.daoTitleTraditional == "含弘真君", "Fu Yuan Dao title is incorrect.");
            Require(definition.nameArt != null, "Fu Yuan presentation requires separated name art.");
            Require(definition.daoTitleArt != null, "Fu Yuan presentation requires separated Dao-title art.");
            Require(definition.seal != null, "Fu Yuan presentation requires separated seal art.");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(scene.IsValid(), $"Invalid character presentation prototype scene: {ScenePath}");
            CharacterPresentationPrototypeBootstrap bootstrap =
                UnityEngine.Object.FindFirstObjectByType<CharacterPresentationPrototypeBootstrap>();
            Require(bootstrap != null, "Character presentation prototype bootstrap is missing.");
            Require(bootstrap.definition == definition, "Prototype scene does not reference the Fu Yuan definition.");
            Require(
                !EditorBuildSettings.scenes.Any(entry => entry.enabled && entry.path == ScenePath),
                "Character presentation prototype must remain outside formal Build Settings.");
            Require(
                UnityEngine.Object.FindFirstObjectByType<TianZhang.Adventure.AdventureSceneController>() == null,
                "Character presentation prototype must not own Adventure runtime behavior.");
            Require(
                UnityEngine.Object.FindFirstObjectByType<TianZhang.Game.BattleUIManager>() == null,
                "Character presentation prototype must not own battle UI behavior.");
        }

        private static CharacterPresentationDefinition BuildOrUpdateDefinition()
        {
            EnsureFolder("Assets/Data/CharacterPresentation");
            CharacterPresentationDefinition definition =
                AssetDatabase.LoadAssetAtPath<CharacterPresentationDefinition>(DefinitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<CharacterPresentationDefinition>();
                AssetDatabase.CreateAsset(definition, DefinitionPath);
            }

            definition.characterId = "npc_fu_yuan";
            definition.displayNameTraditional = "苻淵";
            definition.daoTitleTraditional = "含弘真君";
            definition.characterFullBody = LoadSprite(CharacterPath);
            definition.dialogueOverride = null;
            definition.profileBackground16x9 = LoadSprite(BackgroundPath);
            definition.profileFx = LoadSprite(FxPath);
            definition.nameArt = LoadSprite(NamePath);
            definition.daoTitleArt = LoadSprite(DaoTitlePath);
            definition.seal = LoadSprite(SealPath);
            definition.staticPreview16x9 = LoadSprite(PreviewPath);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssetIfDirty(definition);
            return definition;
        }

        private static Sprite LoadSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                throw new InvalidOperationException($"Missing imported character presentation sprite: {path}");
            return sprite;
        }

        private static void ConfigureSprite(string path, bool alpha)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException($"Missing character presentation texture importer: {path}");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaSource = alpha
                ? TextureImporterAlphaSource.FromInput
                : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = alpha;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
