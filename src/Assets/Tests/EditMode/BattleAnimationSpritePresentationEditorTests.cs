using System;
using System.Collections.Generic;
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
        public void AdventureScenePersistsTheIsolatedBattleAnimationProbeGroup()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            Transform board = FindSingle(scene, BattleAnimationSpriteProbeMatrix.BoardName);
            Transform group = board.Find(BattleAnimationSpriteProbeMatrix.GroupName);
            Assert.IsNotNull(group);
            Assert.IsFalse(group.gameObject.activeSelf);
            Assert.IsFalse(board.Find(TacticalSpriteProbeMatrix.GroupName).gameObject.activeSelf);

            int[,] expectations =
            {
                { 1, 0, 1, 90 }, { 1, -1, 0, 150 }, { 0, -1, 0, 210 },
                { -1, 0, 1, 270 }, { -1, 1, 0, 330 }, { 0, 1, 1, 30 },
            };
            for (int direction = 0; direction < 6; direction++)
            {
                Transform probe = group.Find(BattleAnimationSpriteProbeMatrix.ProbePrefix + direction);
                Assert.IsNotNull(probe);
                Assert.Less(Vector3.Distance(probe.localPosition,
                    HexToVisualPosition(expectations[direction, 0], expectations[direction, 1],
                        HeightForLevel(expectations[direction, 2]))), 0.001f);
                Assert.Less(Quaternion.Angle(probe.localRotation,
                    Quaternion.Euler(0f, expectations[direction, 3], 0f)), 0.01f);
                BattleAnimationSpritePresentationController controller = probe.GetComponent<BattleAnimationSpritePresentationController>();
                Assert.IsNotNull(controller);
                SerializedProperty activeDirection = new SerializedObject(controller).FindProperty("activeDirection");
                Assert.AreEqual(direction, activeDirection.intValue);
                SpriteRenderer renderer = probe.GetComponentInChildren<SpriteRenderer>(true);
                Assert.IsNotNull(renderer);
                Assert.AreEqual(VisualBaselineBuilder.BattleAnimationSpriteName(0, direction, 0), renderer.sprite.name);
            }
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

        private static Transform FindSingle(Scene scene, string name)
        {
            var matches = new List<Transform>();
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                    if (transform.name == name) matches.Add(transform);
            Assert.AreEqual(1, matches.Count, "Expected exactly one object named " + name + ".");
            return matches[0];
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

        private static float HeightForLevel(int heightLevel) => 0.34f + heightLevel * 0.28f;

        private static Vector3 HexToVisualPosition(int q, int r, float y) =>
            new Vector3(q + r * 0.5f, y, r * 0.8660254f + 1f);

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
