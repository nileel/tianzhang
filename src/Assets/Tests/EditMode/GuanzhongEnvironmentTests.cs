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
            Assert.AreEqual(0, environment.GetComponentsInChildren<Collider>().Length, "环境层不得拦截棋盘点击。");

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
            }

            Assert.AreEqual(6.3617754f, Camera.main.orthographicSize, 0.01f,
                "环境层不得改变按战场计算的相机取景。");
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
                    $"{layer.position.x:F4},{layer.position.y:F4},{layer.position.z:F4}",
                    $"{layer.lossyScale.x:F4},{layer.lossyScale.y:F4},{layer.lossyScale.z:F4}",
                    renderer.sharedMaterial.shader.name,
                    renderer.sharedMaterial.mainTexture.name,
                    renderer.sharedMaterial.GetColor("_Color").a.ToString("F3")));
            }
            snapshot.Add("camera|" + Camera.main.orthographicSize.ToString("F4"));
            return snapshot;
        }

        private static GameObject Find(Scene scene, string name) =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(transform => transform.name == name).Select(transform => transform.gameObject).FirstOrDefault();
    }
}
