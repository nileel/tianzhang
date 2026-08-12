using System;
using System.Linq;
using TianZhang.Bootstrap;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.Settlement;
using TianZhang.Features.WorldMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TianZhang.Editor
{
    public static class SceneArchitectureValidator
    {
        [MenuItem("天章/场景/验证正式场景")]
        public static void Validate()
        {
            string[] expected =
            {
                SceneBuildSupport.StartMenuScenePath,
                SceneBuildSupport.WorldScenePath,
                SceneBuildSupport.SettlementScenePath,
                SceneBuildSupport.AdventureScenePath,
            };
            string[] enabled = EditorBuildSettings.scenes.Where(item => item.enabled).Select(item => item.path).ToArray();
            Require(expected.SequenceEqual(enabled), "Build Settings must contain exactly the four formal scenes in order.");
            ValidateScene<StartMenuSceneInstaller, StartMenuController>(expected[0], true);
            ValidateScene<WorldSceneInstaller, WorldMapController>(expected[1], false);
            ValidateScene<SettlementSceneInstaller, SettlementController>(expected[2], false);
            ValidateScene<AdventureSceneInstaller, AdventureController>(expected[3], false);
        }

        public static void ValidateForBatchMode() => Validate();

        private static void ValidateScene<TInstaller, TController>(string path, bool expectsBootstrap)
            where TInstaller : Component
            where TController : Component
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded, "Invalid formal scene: " + path);
            Require(UnityEngine.Object.FindFirstObjectByType<TInstaller>() != null, path + " missing installer.");
            Require(UnityEngine.Object.FindFirstObjectByType<TController>() != null, path + " missing controller.");
            int bootstrapCount = UnityEngine.Object.FindObjectsByType<GameBootstrap>(FindObjectsSortMode.None).Length;
            Require(bootstrapCount == (expectsBootstrap ? 1 : 0), path + " has an invalid serialized GameBootstrap count.");
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
                Require(component != null, path + " contains a Missing Script.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
