using System;
using TianZhang.Features.Adventure;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TianZhang.Editor
{
    /// <summary>Builds only the non-BuildSettings fire-ferry fixture scene.</summary>
    public static class JindanFireFerryLineScenarioSceneBuilder
    {
        public const string ScenePath = "Assets/Tests/Scenes/JindanFireFerryLineScenario.unity";

        private static readonly Vector2Int SourceCell = new Vector2Int(0, 0);
        private static readonly Vector2Int FormationEyeCell = new Vector2Int(0, 1);
        private static readonly Vector2Int[] FerryRoute =
        {
            new Vector2Int(1, 0),
            new Vector2Int(2, 0),
            new Vector2Int(3, 0),
        };

        [MenuItem("天章/测试/重建渡火引线单项场景")]
        public static void Build()
        {
            EnsureSceneFolder();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("JindanFireFerryLineScenarioRoot");
            BuildCamera(root.transform);
            Transform grid = new GameObject("FixtureGrid").transform;
            grid.SetParent(root.transform, false);

            CreateCell(grid, "SourceCell_0_0", SourceCell, PrimitiveType.Cube);
            CreateCell(grid, "FormationEye_0_1", FormationEyeCell, PrimitiveType.Cylinder);
            var routeMarkers = new GameObject[FerryRoute.Length];
            for (int index = 0; index < FerryRoute.Length; index++)
            {
                CreateCell(grid, "FerryRouteCell_" + index, FerryRoute[index], PrimitiveType.Cube);
                routeMarkers[index] = CreateFireMarker(
                    grid,
                    "FerryFireMarker_" + index,
                    FerryRoute[index]);
            }

            GameObject sourceMarker = CreateFireMarker(grid, "SourceFireMarker", SourceCell);
            GameObject formationEyeMarker = FindChild(grid, "FormationEye_0_1");
            Canvas canvas = SceneBuildSupport.CreateCanvas("JindanFireFerryLineFixtureCanvas");
            canvas.transform.SetParent(root.transform, false);
            GameObject panel = SceneBuildSupport.CreatePanel(
                "JindanFireFerryLineFixturePanel", canvas.transform,
                new Vector2(0.03f, 0.68f), new Vector2(0.45f, 0.94f));
            SceneBuildSupport.AddVerticalLayout(panel, 6);
            SceneBuildSupport.CreateText("FixtureTitle", panel.transform, "渡火引线单项场景", 24);
            Text status = SceneBuildSupport.CreateText("FixtureStatus", panel.transform, string.Empty, 14);

            var controllerObject = new GameObject("JindanFireFerryLineScenarioController");
            controllerObject.transform.SetParent(root.transform, false);
            JindanFireFerryLineScenarioController controller =
                controllerObject.AddComponent<JindanFireFerryLineScenarioController>();
            ConfigureFixture(controller, formationEyeMarker, sourceMarker, routeMarkers, status);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("jindan_fire_ferry_line_scene_save_failed");
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureFixture(
            JindanFireFerryLineScenarioController controller,
            GameObject formationEyeMarker,
            GameObject sourceMarker,
            GameObject[] routeMarkers,
            Text status)
        {
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("fixtureId").stringValue = JindanFireFerryLineScenarioController.FixtureIdValue;
            serialized.FindProperty("sourceCell").vector2IntValue = SourceCell;
            serialized.FindProperty("formationEyeCell").vector2IntValue = FormationEyeCell;
            SetVector2IntArray(serialized.FindProperty("ferryRoute"), FerryRoute);
            serialized.FindProperty("initialActions").intValue = 2;
            serialized.FindProperty("initialSpirit").intValue = 4;
            serialized.FindProperty("initialFuel").intValue = 2;
            serialized.FindProperty("initialFireBudget").intValue = FerryRoute.Length;
            serialized.FindProperty("initialSurfaceCapacity").intValue = 1;
            serialized.FindProperty("initialEnvironmentCapacity").intValue = 1;
            serialized.FindProperty("manifestActionCost").intValue = 1;
            serialized.FindProperty("manifestSpiritCost").intValue = 1;
            serialized.FindProperty("manifestFuelCost").intValue = 1;
            serialized.FindProperty("manifestSurfaceCapacityCost").intValue = 1;
            serialized.FindProperty("escalateActionCost").intValue = 1;
            serialized.FindProperty("escalateSpiritCost").intValue = 1;
            serialized.FindProperty("escalateFuelCost").intValue = 1;
            serialized.FindProperty("fireBudgetCostPerNode").intValue = 1;
            serialized.FindProperty("environmentCapacityCost").intValue = 1;
            serialized.FindProperty("formationEyeActive").boolValue = true;
            serialized.FindProperty("waterBlocked").boolValue = false;
            serialized.FindProperty("routeIsolated").boolValue = false;
            serialized.FindProperty("fuelDisconnected").boolValue = false;
            serialized.FindProperty("forceNonAdjacentRoute").boolValue = false;
            serialized.FindProperty("forceFireBudgetInsufficient").boolValue = false;
            serialized.FindProperty("forceSurfaceCapacityInsufficient").boolValue = false;
            serialized.FindProperty("forceEnvironmentCapacityInsufficient").boolValue = false;
            serialized.FindProperty("formationEyeMarker").objectReferenceValue = formationEyeMarker;
            serialized.FindProperty("sourceMarker").objectReferenceValue = sourceMarker;
            SerializedProperty markerProperties = serialized.FindProperty("ferryLineMarkers");
            markerProperties.arraySize = routeMarkers.Length;
            for (int index = 0; index < routeMarkers.Length; index++)
                markerProperties.GetArrayElementAtIndex(index).objectReferenceValue = routeMarkers[index];
            serialized.FindProperty("stateText").objectReferenceValue = status;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            controller.ConfigureFixturePresentation(
                formationEyeMarker, sourceMarker, routeMarkers, status);
        }

        private static void BuildCamera(Transform parent)
        {
            var cameraObject = new GameObject("FixtureCamera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(1.5f, 7f, -8f);
            cameraObject.transform.rotation = Quaternion.Euler(40f, 0f, 0f);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.04f, 0.02f);

            var lightObject = new GameObject("FixtureLight", typeof(Light));
            lightObject.transform.SetParent(parent, false);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        }

        private static void CreateCell(Transform parent, string name, Vector2Int coordinate, PrimitiveType primitive)
        {
            GameObject cell = GameObject.CreatePrimitive(primitive);
            cell.name = name;
            cell.transform.SetParent(parent, false);
            cell.transform.position = ToWorld(coordinate, 0f);
            cell.transform.localScale = primitive == PrimitiveType.Cylinder
                ? new Vector3(0.55f, 0.12f, 0.55f)
                : new Vector3(0.78f, 0.12f, 0.78f);
            RemoveCollider(cell);
        }

        private static GameObject CreateFireMarker(Transform parent, string name, Vector2Int coordinate)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = name;
            marker.transform.SetParent(parent, false);
            marker.transform.position = ToWorld(coordinate, 0.32f);
            marker.transform.localScale = Vector3.one * 0.35f;
            RemoveCollider(marker);
            marker.SetActive(false);
            return marker;
        }

        private static Vector3 ToWorld(Vector2Int coordinate, float height)
        {
            return new Vector3(coordinate.x + 0.5f * coordinate.y, height, 0.8660254f * coordinate.y);
        }

        private static void RemoveCollider(GameObject target)
        {
            Collider collider = target.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        }

        private static GameObject FindChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child == null) throw new InvalidOperationException("Fixture child is missing: " + name);
            return child.gameObject;
        }

        private static void SetVector2IntArray(SerializedProperty property, Vector2Int[] values)
        {
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
                property.GetArrayElementAtIndex(index).vector2IntValue = values[index];
        }

        private static void EnsureSceneFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Tests/Scenes"))
                AssetDatabase.CreateFolder("Assets/Tests", "Scenes");
        }
    }
}
