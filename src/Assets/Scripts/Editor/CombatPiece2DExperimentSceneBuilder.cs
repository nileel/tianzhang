using System;
using TianZhang.Features.CombatPresentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TianZhang.Editor
{
    /// <summary>构建非 BuildSettings 的 2D 棋子合同实验入口；不创建正式 Adventure 或 Bootstrap。</summary>
    public static class CombatPiece2DExperimentSceneBuilder
    {
        public const string ScenePath = "Assets/Tests/Scenes/CombatPiece2DExperimentScene.unity";

        [MenuItem("天章/测试/重建战斗棋子 2D 实验")]
        public static void Build()
        {
            EnsureSceneFolder();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("CombatPiece2DExperimentRoot");
            var camera = new GameObject("ExperimentCamera", typeof(Camera), typeof(AudioListener));
            camera.tag = "MainCamera";
            camera.transform.SetParent(root.transform, false);
            camera.transform.position = new Vector3(0f, 2f, -10f);
            camera.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
            Camera cameraComponent = camera.GetComponent<Camera>();
            cameraComponent.orthographic = true;
            cameraComponent.orthographicSize = 4f;
            cameraComponent.clearFlags = CameraClearFlags.SolidColor;
            cameraComponent.backgroundColor = new Color(0.05f, 0.07f, 0.1f);

            GameObject input = new GameObject("CombatPiece2DExperimentInput");
            input.transform.SetParent(root.transform, false);
            BattleAnimationSpriteCombatUnitPresentationAdapter adapter =
                input.AddComponent<BattleAnimationSpriteCombatUnitPresentationAdapter>();
            SceneBuildSupport.SetObject(adapter, "battleAnimationSpritePrefab",
                SceneBuildSupport.RequireAsset<GameObject>(VisualBaselineBuilder.BattleAnimationSpritePrefabPath));

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("combat_piece_2d_experiment_scene_save_failed");
            AssetDatabase.SaveAssets();
        }

        private static void EnsureSceneFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Tests/Scenes"))
                AssetDatabase.CreateFolder("Assets/Tests", "Scenes");
        }
    }
}
