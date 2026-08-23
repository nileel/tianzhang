using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Features.CombatPresentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TianZhang.Tests.EditMode
{
    public sealed class VisualBaselineEditorTests
    {
        [Test]
        public void FormalRenderingAndSceneBaselineIsValid()
        {
            Assert.DoesNotThrow(SceneArchitectureValidator.Validate);
        }

        [Test]
        public void HexMeshesSeparateTerrainAndFeedbackLayers()
        {
            Mesh column = AssetDatabase.LoadAssetAtPath<Mesh>(VisualBaselineBuilder.HexColumnMeshPath);
            Mesh overlay = AssetDatabase.LoadAssetAtPath<Mesh>(VisualBaselineBuilder.HexOverlayMeshPath);
            Assert.IsNotNull(column);
            Assert.AreEqual(2, column.subMeshCount, "Hex top and side must use separate submeshes.");
            Assert.Greater(column.GetIndexCount(0), 0);
            Assert.Greater(column.GetIndexCount(1), 0);
            Assert.IsNotNull(overlay);
            Assert.AreEqual(1, overlay.subMeshCount);
            Assert.AreNotSame(column, overlay);
        }

        [Test]
        public void AdventureScenePersistsIndependentVisualLayers()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            Transform[] transforms = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .ToArray();
            foreach (string name in new[]
                     { "VisualBaselineBoard", "SurfaceOverlay", "ReachableOverlay", "SelectedOverlay", "AttackOverlay", "VisualBaselineOccluder" })
                Assert.AreEqual(1, transforms.Count(item => item.name == name), "Missing or duplicate visual layer: " + name);
            Assert.AreEqual(9, transforms.Count(item => item.name.StartsWith("VisualHex_")));
        }

        [Test]
        public void AdventureSceneFacingProbesMatchTheFrozenSixDirectionContract()
        {
            int[,] expectations =
            {
                { 1, 0, 1, 90 }, { 1, -1, 0, 150 }, { 0, -1, 0, 210 },
                { -1, 0, 1, 270 }, { -1, 1, 0, 330 }, { 0, 1, 1, 30 },
            };
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            Transform[] transforms = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .ToArray();
            Transform board = transforms.Single(item => item.name == "VisualBaselineBoard");

            for (int direction = 0; direction < expectations.GetLength(0); direction++)
            {
                int q = expectations[direction, 0];
                int r = expectations[direction, 1];
                int heightLevel = expectations[direction, 2];
                int yaw = expectations[direction, 3];
                string cellName = "VisualHex_" + q + "_" + r + "_Height_" + heightLevel;
                Assert.AreEqual(1, transforms.Count(item => item.name == cellName),
                    "Every rule neighbor needs exactly one known-height visual cell.");
                Transform cell = transforms.Single(item => item.name == cellName);
                Assert.Less(Vector3.Distance(cell.localPosition, HexToVisualPosition(q, r, 0f)), 0.001f);
                Assert.Less(Mathf.Abs(cell.localScale.y - HeightForLevel(heightLevel)), 0.001f);

                string probeName = "FacingProbe_" + direction;
                Assert.AreEqual(1, transforms.Count(item => item.parent == board && item.name == probeName),
                    "Every frozen direction needs exactly one static technical probe.");
                Transform probe = board.Find(probeName);
                Assert.Less(Vector3.Distance(probe.localPosition, HexToVisualPosition(q, r, HeightForLevel(heightLevel))), 0.001f);
                Assert.Less(Quaternion.Angle(probe.localRotation, Quaternion.Euler(0f, yaw, 0f)), 0.01f);
                Vector3 expectedForward = new Vector3(q + r * 0.5f, 0f, r * 0.8660254f).normalized;
                Assert.Less(Vector3.Angle(probe.localRotation * Vector3.forward, expectedForward), 0.01f,
                    "UnitMarker local +Z must face the matching rule neighbor center.");
                Assert.IsNotNull(probe.GetComponent<StaticChessPresentationController>());
                Assert.AreEqual(
                    VisualBaselineBuilder.StaticChessPrefabPath,
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(probe.gameObject));
                Assert.Zero(probe.GetComponentsInChildren<Animator>(true).Length);
                Assert.Zero(probe.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);
                Assert.IsNotNull(probe.Find("StaticChessBase"));
                MeshRenderer[] renderers = probe.GetComponentsInChildren<MeshRenderer>(true);
                Assert.GreaterOrEqual(renderers.Length, 2);
                foreach (MeshRenderer renderer in renderers)
                {
                    Assert.AreEqual(ShadowCastingMode.On, renderer.shadowCastingMode);
                    Assert.IsTrue(renderer.receiveShadows);
                }
            }
        }

        [Test]
        public void SettlementCharterLayoutControlsChildHeight()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.SettlementScenePath, OpenSceneMode.Single);
            VerticalLayoutGroup layout = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<VerticalLayoutGroup>(true))
                .Single(item => item.name == "CharterSitePanel");
            Assert.IsTrue(layout.childControlHeight,
                "CharterSitePanel must honor child preferred heights so every action remains inside the 1920x1080 canvas.");
        }

        [Test]
        public void UnitMarkerUses3DMeshesAndStandardShadows()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(VisualBaselineBuilder.UnitMarkerPrefabPath);
            try
            {
                Assert.Zero(root.GetComponentsInChildren<SpriteRenderer>(true).Length);
                MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                Assert.GreaterOrEqual(renderers.Length, 2);
                Assert.IsTrue(renderers.Any(item => item.name == "Facing"),
                    "The technical marker must expose its local +Z facing geometry.");
                foreach (MeshRenderer renderer in renderers)
                {
                    Assert.AreEqual(ShadowCastingMode.On, renderer.shadowCastingMode);
                    Assert.IsTrue(renderer.receiveShadows);
                    Assert.AreEqual(
                        VisualBaselineBuilder.UnitMaterialPath,
                        AssetDatabase.GetAssetPath(renderer.sharedMaterial));
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void StaticChessAssetsKeepTheFigureAndBaseIndependent()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(VisualBaselineBuilder.StaticChessMaterialPath);
            Assert.IsNotNull(material);
            Assert.AreEqual("Universal Render Pipeline/Lit", material.shader.name);
            ModelImporter importer = AssetImporter.GetAtPath(VisualBaselineBuilder.StaticChessModelPath) as ModelImporter;
            Assert.IsNotNull(importer);
            Assert.IsFalse(importer.importAnimation);
            Assert.AreEqual(ModelImporterAnimationType.None, importer.animationType);
            GameObject root = PrefabUtility.LoadPrefabContents(VisualBaselineBuilder.StaticChessPrefabPath);
            try
            {
                Assert.AreEqual("FuYuan_StaticChess", root.name);
                Assert.AreEqual(Vector3.zero, root.transform.localPosition);
                Assert.AreEqual(Vector3.one, root.transform.localScale);
                Assert.IsNotNull(root.GetComponent<StaticChessPresentationController>());
                Assert.Zero(root.GetComponentsInChildren<Animation>(true).Length);
                Assert.Zero(root.GetComponentsInChildren<Animator>(true).Length);
                Assert.Zero(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);
                Transform basePlaceholder = root.transform.Find("StaticChessBase");
                Assert.IsNotNull(basePlaceholder);
                Assert.Less(Mathf.Abs(basePlaceholder.localPosition.y + 0.04f), 0.001f);
                Assert.AreEqual(
                    VisualBaselineBuilder.UnitMaterialPath,
                    AssetDatabase.GetAssetPath(basePlaceholder.GetComponent<MeshRenderer>().sharedMaterial));
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true)
                             .Where(item => item.transform != basePlaceholder))
                    Assert.AreEqual(
                        VisualBaselineBuilder.StaticChessMaterialPath,
                        AssetDatabase.GetAssetPath(renderer.sharedMaterial));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void TacticalSpriteTexturesMatchTheFrozenSixDirectionContract()
        {
            for (int direction = 0; direction < 6; direction++)
            {
                string copyPath = UnityCopyFilePath(direction);
                string sourcePath = ApprovedSourcePath(direction);
                Assert.AreEqual(FrozenTacticalSpriteSha256[direction], ComputeSha256Hex(copyPath),
                    "Unity copy must stay byte-for-byte equal to the approved source (direction " + direction + ").");
                Assert.AreEqual(FrozenTacticalSpriteSha256[direction], ComputeSha256Hex(sourcePath),
                    "Approved source must stay byte-for-byte unchanged (direction " + direction + ").");
                AssertPngHeader(copyPath, 768, 768);

                TextureImporter importer = AssetImporter.GetAtPath(VisualBaselineBuilder.TacticalSpriteTexturePath(direction)) as TextureImporter;
                Assert.IsNotNull(importer);
                Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);
                Assert.AreEqual(SpriteImportMode.Single, importer.spriteImportMode);
                Assert.AreEqual(512f, importer.spritePixelsPerUnit, 0.001f);
                Assert.Less(Vector2.Distance(importer.spritePivot, new Vector2(0.5f, 0.125f)), 0.001f);
                Assert.IsTrue(importer.alphaIsTransparency);

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(VisualBaselineBuilder.TacticalSpriteTexturePath(direction));
                Assert.IsNotNull(texture);
                Assert.AreEqual(768, texture.width);
                Assert.AreEqual(768, texture.height);
            }
        }

        [Test]
        public void TacticalSpritePrefabOwnsOneSpriteAndSixFrozenDirections()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(VisualBaselineBuilder.TacticalSpritePrefabPath);
            try
            {
                Assert.AreEqual("FuYuan_TacticalSprite", root.name);
                Assert.AreEqual(Vector3.zero, root.transform.localPosition);
                Assert.AreEqual(Vector3.one, root.transform.localScale);
                TacticalSpritePresentationController controller = root.GetComponent<TacticalSpritePresentationController>();
                Assert.IsNotNull(controller);
                Assert.AreEqual(1, root.GetComponentsInChildren<SpriteRenderer>(true).Length);
                Assert.Zero(root.GetComponentsInChildren<Animator>(true).Length);
                Assert.Zero(root.GetComponentsInChildren<Animation>(true).Length);
                Assert.Zero(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);

                var serialized = new SerializedObject(controller);
                SerializedProperty sprites = serialized.FindProperty("directionSprites");
                Assert.IsNotNull(sprites);
                Assert.AreEqual(6, sprites.arraySize);
                var seen = new HashSet<Sprite>();
                for (int direction = 0; direction < 6; direction++)
                {
                    Sprite sprite = sprites.GetArrayElementAtIndex(direction).objectReferenceValue as Sprite;
                    Assert.IsNotNull(sprite, "Missing frozen direction sprite " + direction + ".");
                    Assert.AreEqual("FuYuan_TacticalDirection_" + direction, sprite.name);
                    Assert.IsTrue(seen.Add(sprite), "Direction sprites must be six distinct assets.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void AdventureScenePersistsTheIsolatedTacticalSpriteGroup()
        {
            int[,] expectations =
            {
                { 1, 0, 1, 90 }, { 1, -1, 0, 150 }, { 0, -1, 0, 210 },
                { -1, 0, 1, 270 }, { -1, 1, 0, 330 }, { 0, 1, 1, 30 },
            };
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            Transform[] transforms = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .ToArray();
            Transform board = transforms.Single(item => item.name == "VisualBaselineBoard");
            Transform group = transforms.Single(item => item.parent == board && item.name == "TacticalSpriteProbeGroup");
            Assert.IsFalse(group.gameObject.activeSelf,
                "The 2D tactical sprite group must stay inactive by default so the 2D and 3D routes are mutually exclusive.");
            for (int direction = 0; direction < expectations.GetLength(0); direction++)
            {
                int q = expectations[direction, 0];
                int r = expectations[direction, 1];
                int heightLevel = expectations[direction, 2];
                int yaw = expectations[direction, 3];
                Transform probe = group.Find("TacticalSpriteProbe_" + direction);
                Assert.IsNotNull(probe, "Missing tactical sprite probe " + direction + ".");
                Assert.Less(Vector3.Distance(probe.localPosition, HexToVisualPosition(q, r, HeightForLevel(heightLevel))), 0.001f);
                Assert.Less(Quaternion.Angle(probe.localRotation, Quaternion.Euler(0f, yaw, 0f)), 0.01f);
                Vector3 expectedForward = new Vector3(q + r * 0.5f, 0f, r * 0.8660254f).normalized;
                Assert.Less(Vector3.Angle(probe.localRotation * Vector3.forward, expectedForward), 0.01f);
                TacticalSpritePresentationController controller = probe.GetComponent<TacticalSpritePresentationController>();
                Assert.IsNotNull(controller);
                var serialized = new SerializedObject(controller);
                SerializedProperty directionProperty = serialized.FindProperty("activeDirection");
                Assert.IsNotNull(directionProperty);
                Assert.AreEqual(direction, directionProperty.intValue);
                SpriteRenderer renderer = probe.GetComponentInChildren<SpriteRenderer>(true);
                Assert.IsNotNull(renderer);
                Assert.IsNotNull(renderer.sprite);
                Assert.AreEqual("FuYuan_TacticalDirection_" + direction, renderer.sprite.name);
            }
            for (int direction = 0; direction < 6; direction++)
                Assert.AreEqual(1, transforms.Count(item => item.parent == board && item.name == "FacingProbe_" + direction),
                    "The static 3D facing probes must remain untouched beside the new 2D group.");
        }

        [Test]
        public void TacticalSpriteOcclusionProbeIsDepthOccludedByTheExistingOccluder()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            Transform[] transforms = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .ToArray();
            Transform board = transforms.Single(item => item.name == "VisualBaselineBoard");
            Transform group = board.Find("TacticalSpriteProbeGroup");
            Assert.IsNotNull(group, "AdventureScene must persist the tactical sprite probe group.");
            Transform probe = group.Find("TacticalSpriteOcclusionProbe");
            Assert.IsNotNull(probe, "AdventureScene must persist the tactical sprite occlusion probe.");
            Assert.Less(Vector3.Distance(probe.localPosition, HexToVisualPosition(2, 0, HeightForLevel(0))), 0.001f,
                "The occlusion probe must anchor at the occluder's own cell center.");

            SpriteRenderer renderer = probe.GetComponentInChildren<SpriteRenderer>(true);
            Assert.IsNotNull(renderer);
            Assert.IsNotNull(renderer.sprite);
            Assert.AreEqual("FuYuan_TacticalDirection_0", renderer.sprite.name,
                "The occlusion probe must render the direction-0 sprite.");
            Assert.AreEqual(0, renderer.sortingOrder, "The occlusion probe must not use always-on-top sorting.");
            Assert.AreEqual(0, renderer.sortingLayerID, "The occlusion probe must keep the default sorting layer.");

            Material occluderMaterial = AssetDatabase.LoadAssetAtPath<Material>(VisualBaselineBuilder.OccluderMaterialPath);
            Assert.IsNotNull(occluderMaterial);
            Assert.Less(occluderMaterial.renderQueue, (int)RenderQueue.Transparent,
                "The occluder must stay opaque so depth occlusion is real.");

            Vector3 cameraForward = Quaternion.Euler(38f, 0f, 0f) * Vector3.forward;
            Vector3 occluderPosition = HexToVisualPosition(2, 0, HeightForLevel(0) + 0.58f);
            Vector3 probePosition = HexToVisualPosition(2, 0, HeightForLevel(0));
            Assert.Greater(Vector3.Dot(probePosition - occluderPosition, cameraForward), 0f,
                "The occlusion probe must sit behind the occluder along the frozen camera forward.");
        }

        private static readonly string[] FrozenTacticalSpriteSha256 =
        {
            "66B3734DFB1DB56B78920FD37FDA5F072FBFE4BF470A4A1FD4E61F9A9BDA526A",
            "C586821EE4D8A1D1E7924EF7787EBE1D208EE149AF5A78740647A4C9DC7B790B",
            "F690FC98710620E8E240CEFAABF430AF8EAFD6308225E89E68F30F8094E7C6F8",
            "F8AB6F4B53EE2DFA913105E8D75C8CF125751674AEE427A0F17610CAA412EBDD",
            "E12DC8F10B16FEB2A5E97FA908A32759EFFA3EC15198BC263B582CAF10366597",
            "5A8E97D92FCD9FC87E0FB11BB6D42111A3624B80E344BBB484E5D5102066F4D8",
        };

        private static string ComputeSha256Hex(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) builder.Append(value.ToString("X2"));
                return builder.ToString();
            }
        }

        private static string UnityCopyFilePath(int direction) =>
            Path.Combine(Application.dataPath, "Art", "Characters", "TacticalSprites", "FuYuan",
                "FuYuan_TacticalDirection_" + direction + ".png");

        private static string ApprovedSourcePath(int direction) =>
            Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..")),
                "assets", "generated-character-art", "fuyuan-2d-tactical-sprite",
                "fuyuan_tactical_direction_" + direction + ".png");

        private static void AssertPngHeader(string path, int expectedWidth, int expectedHeight)
        {
            byte[] bytes = File.ReadAllBytes(path);
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            Assert.GreaterOrEqual(bytes.Length, 33, "The tactical sprite PNG is truncated.");
            for (int i = 0; i < signature.Length; i++)
                Assert.AreEqual(signature[i], bytes[i], "The tactical sprite must be a PNG.");
            Assert.AreEqual(expectedWidth, ReadBigEndianInt32(bytes, 16), "Unexpected tactical sprite width.");
            Assert.AreEqual(expectedHeight, ReadBigEndianInt32(bytes, 20), "Unexpected tactical sprite height.");
            Assert.AreEqual(6, bytes[25], "The tactical sprite must be an 8-bit RGBA PNG.");
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
            (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

        private static float HeightForLevel(int heightLevel) => 0.34f + heightLevel * 0.28f;

        private static Vector3 HexToVisualPosition(int q, int r, float y) =>
            new Vector3(q + r * 0.5f, y, r * 0.8660254f + 1f);
    }
}
