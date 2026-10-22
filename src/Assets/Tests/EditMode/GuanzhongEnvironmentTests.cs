using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TianZhang.Tests.EditMode
{
    public sealed class GuanzhongEnvironmentTests
    {
        private static readonly string[] LayerNames =
        {
            "Environment_DistantCanyon",
            "Environment_ForegroundLeft",
            "Environment_ForegroundRight",
            "Environment_ValleyMist",
        };

        [Test]
        public void SceneKeepsTheFourLayersOutsideTheBattlefieldAndCameraFraming()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject environment = Find(scene, GuanzhongEnvironmentBuilder.EnvironmentRootName);
            Assert.IsNotNull(environment, "AdventureScene 必须包含环境层根节点。");
            GameObject battlefield = Find(scene, "GuanzhongBattlefield");
            Assert.IsNotNull(battlefield, "AdventureScene 必须包含战场。");
            Assert.IsFalse(environment.transform.IsChildOf(battlefield.transform),
                "环境层不能挂在战场下，否则会扩大相机自动取景与平移边界。");
            Assert.AreSame(Camera.main.transform, environment.transform.parent,
                "环境层必须像天空盒一样挂在相机下，平移时相对镜头不变。");
            Assert.AreEqual(0, environment.GetComponentsInChildren<Collider>().Length, "环境层不得拦截棋盘点击。");

            var expected = new Dictionary<string, Vector3>
            {
                { "Environment_DistantCanyon", new Vector3(0f, 0f, 35f) },
                { "Environment_ForegroundLeft", new Vector3(-10.7290f, -4.1398f, 4f) },
                { "Environment_ForegroundRight", new Vector3(11.2710f, -4.4732f, 4f) },
                { "Environment_ValleyMist", new Vector3(0f, -4.2f, 3.5f) },
            };
            foreach (string name in LayerNames)
            {
                Transform layer = environment.transform.Find(name);
                Assert.IsNotNull(layer, "缺少环境层：" + name);
                var renderer = layer.GetComponent<Renderer>();
                Assert.IsNotNull(renderer, name + " 缺少 Renderer。");
                Assert.IsTrue(renderer.enabled, name + " 必须可见。");
                Assert.AreEqual("Sprites/Default", renderer.sharedMaterial.shader.name, name + " 必须使用无光照材质。");
                Assert.IsNotNull(renderer.sharedMaterial.mainTexture, name + " 缺少贴图。");
                Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode, name + " 不得投射阴影。");
                Assert.Less(Vector3.Distance(expected[name], layer.localPosition), 0.01f,
                    name + " 的镜头相对位置不得漂移：" + layer.localPosition);
                if (name != "Environment_DistantCanyon")
                    Assert.Less(layer.localPosition.z, 5f, name + " 必须比最近的棋盘格更靠近相机，才能压在地块上层。");
            }

            Assert.AreEqual(5f, Camera.main.orthographicSize, 0.01f,
                "环境层不得覆盖玩家默认近景取景。");
        }

        [Test]
        public void TacticalCameraRebuildKeepsZoomAndEnvironmentAfterReopen()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            List<string> before = Snapshot(scene);
            AdventureSceneBuilder.RebuildTacticalCamera();
            AdventureSceneBuilder.RebuildTacticalCamera();
            scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            CollectionAssert.AreEqual(before, Snapshot(scene));
            var camera = Camera.main;
            var controller = camera.GetComponent<TianZhang.Features.Adventure.AdventureCameraController>();
            var data = new SerializedObject(controller);
            Assert.AreSame(camera, data.FindProperty("controlledCamera").objectReferenceValue);
            Assert.AreSame(Find(scene, "GuanzhongBattlefield").transform,
                data.FindProperty("battlefield").objectReferenceValue);
            Assert.AreEqual(5f, camera.orthographicSize);
            Assert.AreEqual(3f, data.FindProperty("minimumSize").floatValue);
            Assert.AreEqual(7f, data.FindProperty("maximumSize").floatValue);
            Assert.AreEqual(.5f, data.FindProperty("zoomStep").floatValue);
            Assert.AreEqual(8f, camera.transform.position.y, .001f);
            Assert.Less(Quaternion.Angle(camera.transform.rotation, Quaternion.Euler(38f, 0f, 0f)), .01f);
        }

        [Test]
        public void SceneUsesLocalContactShadowsAndLitOpaqueMaterials()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            Light key = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Light>())
                .Single(light => light.type == LightType.Directional);
            Component lightData = key.GetComponents<Component>().Single(component => component.GetType().Name == "UniversalAdditionalLightData");
            Assert.That((bool)lightData.GetType().GetProperty("usePipelineSettings").GetValue(lightData), Is.False,
                "主光必须采用自己的接触阴影偏移，不能回落到全局 URP 偏移。");
            Assert.That(key.shadowBias, Is.InRange(0.05f, 0.2f));
            Assert.That(key.shadowNormalBias, Is.InRange(0.1f, 0.25f));
            Assert.That(key.color.r, Is.GreaterThan(key.color.b), "暖色主光。");
            Assert.That(RenderSettings.ambientSkyColor.b, Is.GreaterThan(RenderSettings.ambientSkyColor.r), "冷色环境光。");
            foreach (string name in new[] { "Tile", "RockCluster" })
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(GuanzhongTerrainAssetBuilder.AssetRoot + "/" + name + ".mat");
                Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
                Assert.That(material.GetFloat("_Surface"), Is.Zero);
                Assert.That(material.GetFloat("_Smoothness"), Is.LessThan(0.4f));
                Assert.That(material.GetTexture("_BaseMap"), Is.Not.Null);
            }
        }

        [Test]
        public void RebuildKeepsLayerPlacementAndSurvivesSaveAndReopen()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            List<string> before = Snapshot(scene);
            GuanzhongEnvironmentBuilder.Build();
            CollectionAssert.AreEqual(before, Snapshot(scene), "重建后环境层位置与材质必须一致。");
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, SceneBuildSupport.AdventureScenePath));
            AssetDatabase.SaveAssets();

            Scene reopened = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            CollectionAssert.AreEqual(before, Snapshot(reopened), "保存并重开后环境层必须一致。");
        }

        private static List<string> Snapshot(Scene scene)
        {
            GameObject environment = Find(scene, GuanzhongEnvironmentBuilder.EnvironmentRootName);
            Assert.IsNotNull(environment);
            var snapshot = new List<string>();
            foreach (Transform layer in environment.transform.Cast<Transform>().OrderBy(value => value.name, StringComparer.Ordinal))
            {
                var renderer = layer.GetComponent<Renderer>();
                Assert.IsNotNull(renderer, layer.name);
                snapshot.Add(string.Join("|", layer.name,
                    $"{layer.localPosition.x:F4},{layer.localPosition.y:F4},{layer.localPosition.z:F4}",
                    $"{layer.localScale.x:F4},{layer.localScale.y:F4},{layer.localScale.z:F4}",
                    renderer.sharedMaterial.shader.name,
                    renderer.sharedMaterial.mainTexture.name,
                    renderer.sharedMaterial.GetColor("_Color").ToString("F3")));
            }
            snapshot.Add("camera|" + Camera.main.orthographicSize.ToString("F4") + "|" + environment.transform.parent.name +
                "|" + Camera.main.transform.position.ToString("F4") + "|" + Camera.main.transform.eulerAngles.ToString("F4"));
            Light key = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Light>())
                .Single(light => light.type == LightType.Directional);
            snapshot.Add($"light|{key.transform.eulerAngles:F3}|{key.color:F3}|{key.intensity:F3}|{key.shadows}|{key.shadowStrength:F3}|{key.shadowBias:F3}|{key.shadowNormalBias:F3}");
            Component lightData = key.GetComponents<Component>().Single(component => component.GetType().Name == "UniversalAdditionalLightData");
            snapshot.Add("localShadows|" + lightData.GetType().GetProperty("usePipelineSettings").GetValue(lightData));
            snapshot.Add($"ambient|{RenderSettings.ambientMode}|{RenderSettings.ambientSkyColor:F3}|{RenderSettings.ambientEquatorColor:F3}|{RenderSettings.ambientGroundColor:F3}|{RenderSettings.ambientIntensity:F3}");
            return snapshot;
        }

        private static GameObject Find(Scene scene, string name) =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(transform => transform.name == name).Select(transform => transform.gameObject).FirstOrDefault();
    }
}
