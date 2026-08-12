using TianZhang.Content;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Tactical;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TianZhang.Editor
{
    public static class HybridTacticalPrototypeBuilder
    {
        public const string ScenePath = "Assets/Scenes/HybridTacticalPrototype.unity";

        [MenuItem("Tools/天章/生成2.5D战棋隔离原型场景")]
        public static void Build()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 9f;
            camera.backgroundColor = new Color(0.05f, 0.07f, 0.09f);
            camera.transform.position = new Vector3(0f, 10f, -10f);
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -1f, 1f), Vector3.up);
            EnvironmentProfileAsset environment = SceneBuildSupport.RequireAsset<EnvironmentProfileAsset>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            var root = new GameObject("HybridTacticalPrototypeRoot");
            HybridTacticalRenderer renderer = root.AddComponent<HybridTacticalRenderer>();
            renderer.SetPresentationCamera(camera);
            HybridTacticalPrototypeController controller = root.AddComponent<HybridTacticalPrototypeController>();
            controller.SetEnvironmentProfile(environment);
            controller.SetPresentationCamera(camera);
            controller.SetPrototypeRadius(5);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
        }
    }
}
