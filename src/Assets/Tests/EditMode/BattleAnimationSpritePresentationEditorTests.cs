using System;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Features.CombatPresentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TianZhang.Tests.EditMode
{
    public sealed class BattleAnimationSpritePresentationEditorTests
    {
        [Test]
        public void FrozenPilotAtlasesAreCopiedAndImportedAsSixByThreeSpriteSheets()
        {
            VisualBaselineBuilder.BuildBattleAnimationSpriteAssets();
            BattleAnimationManifest manifest = ReadManifest();
            for (int state = 0; state < VisualBaselineBuilder.BattleAnimationStateCount; state++)
            {
                BattleAnimationState sourceState = StateAt(manifest.states, state);
                string sourcePath = Path.Combine(PilotDirectory(), sourceState.file);
                string copyPath = VisualBaselineBuilder.BattleAnimationSpriteTexturePath(state);
                Assert.AreEqual(sourceState.sha256, ComputeSha256(sourcePath));
                Assert.AreEqual(sourceState.sha256, ComputeSha256(copyPath));
                AssertPngHeader(copyPath, 2304, 4608);

                TextureImporter importer = AssetImporter.GetAtPath(copyPath) as TextureImporter;
                Assert.IsNotNull(importer);
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);
                Assert.AreEqual(SpriteImportMode.Multiple, importer.spriteImportMode);
                Assert.AreEqual(VisualBaselineBuilder.TacticalSpritePixelsPerUnit, importer.spritePixelsPerUnit, 0.001f);
                Assert.AreEqual((int)SpriteAlignment.Custom, settings.spriteAlignment);
                Assert.Less(Vector2.Distance(settings.spritePivot, VisualBaselineBuilder.BattleAnimationSpritePivot), 0.001f);
                Assert.IsTrue(importer.alphaIsTransparency);
                Assert.AreEqual(18, importer.spritesheet.Length);

                for (int direction = 0; direction < 6; direction++)
                for (int frame = 0; frame < 3; frame++)
                {
                    SpriteMetaData metadata = importer.spritesheet[direction * 3 + frame];
                    Assert.AreEqual(VisualBaselineBuilder.BattleAnimationSpriteName(state, direction, frame), metadata.name);
                    Assert.AreEqual(new Rect(frame * 768, direction * 768, 768, 768), metadata.rect);
                    Assert.Less(Vector2.Distance(metadata.pivot, VisualBaselineBuilder.BattleAnimationSpritePivot), 0.001f);
                }
            }
        }

        [Test]
        public void BattleAnimationPrefabOwnsOneRendererAndEveryManifestFrame()
        {
            VisualBaselineBuilder.BuildBattleAnimationSpriteAssets();
            GameObject root = PrefabUtility.LoadPrefabContents(VisualBaselineBuilder.BattleAnimationSpritePrefabPath);
            try
            {
                Assert.AreEqual("FuYuan_BattleAnimationSprite", root.name);
                Assert.AreEqual(1, root.GetComponentsInChildren<SpriteRenderer>(true).Length);
                Assert.Zero(root.GetComponentsInChildren<Animator>(true).Length);
                Assert.Zero(root.GetComponentsInChildren<Animation>(true).Length);
                Assert.Zero(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);
                BattleAnimationSpritePresentationController controller = root.GetComponent<BattleAnimationSpritePresentationController>();
                Assert.IsNotNull(controller);

                SerializedObject serialized = new SerializedObject(controller);
                for (int state = 0; state < VisualBaselineBuilder.BattleAnimationStateCount; state++)
                {
                    SerializedProperty frames = serialized.FindProperty(FrameFieldName(state));
                    Assert.IsNotNull(frames);
                    Assert.AreEqual(18, frames.arraySize);
                    for (int direction = 0; direction < 6; direction++)
                    for (int frame = 0; frame < 3; frame++)
                    {
                        Sprite sprite = frames.GetArrayElementAtIndex(direction * 3 + frame).objectReferenceValue as Sprite;
                        Assert.IsNotNull(sprite);
                        Assert.AreEqual(VisualBaselineBuilder.BattleAnimationSpriteName(state, direction, frame), sprite.name);
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void FormalAdventureSceneExcludesTheBattleAnimationProbeGroup()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            Transform[] transforms = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            Assert.IsFalse(transforms.Any(item => item.name == BattleAnimationSpriteProbeMatrix.BoardName));
            Assert.IsFalse(transforms.Any(item => item.name == BattleAnimationSpriteProbeMatrix.GroupName));
            Assert.IsFalse(transforms.Any(item => item.GetComponent<BattleAnimationSpritePresentationController>() != null));
            CollectionAssert.DoesNotContain(AssetDatabase.GetDependencies(SceneBuildSupport.AdventureScenePath, true),
                VisualBaselineBuilder.BattleAnimationSpritePrefabPath);
        }

        private static string PilotDirectory() => Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "..", "assets", "generated-character-art", "fuyuan-2d-battle-animation-pilot"));

        private static BattleAnimationManifest ReadManifest()
        {
            string path = Path.Combine(PilotDirectory(), "manifest.json");
            BattleAnimationManifest manifest = JsonUtility.FromJson<BattleAnimationManifest>(File.ReadAllText(path));
            Assert.IsNotNull(manifest);
            Assert.IsNotNull(manifest.states);
            return manifest;
        }

        private static BattleAnimationState StateAt(BattleAnimationStates states, int state)
        {
            switch (state)
            {
                case 0: return states.idle;
                case 1: return states.move;
                case 2: return states.attack;
                case 3: return states.hit;
                case 4: return states.cast;
                case 5: return states.death;
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
        }

        private static string FrameFieldName(int state)
        {
            switch (state)
            {
                case 0: return "idleFrames";
                case 1: return "moveFrames";
                case 2: return "attackFrames";
                case 3: return "hitFrames";
                case 4: return "castFrames";
                case 5: return "deathFrames";
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static void AssertPngHeader(string path, int expectedWidth, int expectedHeight)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Assert.GreaterOrEqual(bytes.Length, 26);
            Assert.AreEqual(expectedWidth, ReadBigEndianInt32(bytes, 16));
            Assert.AreEqual(expectedHeight, ReadBigEndianInt32(bytes, 20));
            Assert.AreEqual(8, bytes[24]);
            Assert.AreEqual(6, bytes[25]);
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
            (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

        [Serializable]
        private sealed class BattleAnimationManifest { public BattleAnimationStates states; }

        [Serializable]
        private sealed class BattleAnimationStates
        {
            public BattleAnimationState idle;
            public BattleAnimationState move;
            public BattleAnimationState attack;
            public BattleAnimationState hit;
            public BattleAnimationState cast;
            public BattleAnimationState death;
        }

        [Serializable]
        private sealed class BattleAnimationState
        {
            public string file;
            public string sha256;
        }
    }
}
