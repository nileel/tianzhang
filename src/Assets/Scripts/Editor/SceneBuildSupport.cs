using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TianZhang.Editor
{
    public static class SceneBuildSupport
    {
        public const string StartMenuScenePath = "Assets/Scenes/StartMenuScene.unity";
        public const string WorldScenePath = "Assets/Scenes/WorldScene.unity";
        public const string SettlementScenePath = "Assets/Scenes/SettlementScene.unity";
        public const string AdventureScenePath = "Assets/Scenes/AdventureScene.unity";

        public static GameObject BeginScene(string rootName, Color background)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camera.tag = "MainCamera";
            Camera component = camera.GetComponent<Camera>();
            component.orthographic = true;
            component.orthographicSize = 6.2f;
            component.backgroundColor = background;
            component.clearFlags = CameraClearFlags.SolidColor;
            component.nearClipPlane = 0.1f;
            component.farClipPlane = 60f;
            camera.transform.position = new Vector3(0f, 8f, -10f);
            camera.transform.rotation = Quaternion.Euler(38f, 0f, 0f);

            var lightObject = new GameObject("Directional Light", typeof(Light));
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.Hard;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            GameObject backdrop = GameObject.CreatePrimitive(PrimitiveType.Plane);
            backdrop.name = "VisualBackdrop";
            backdrop.transform.position = new Vector3(0f, -0.04f, 1f);
            backdrop.transform.localScale = new Vector3(2.8f, 1f, 2.1f);
            Collider backdropCollider = backdrop.GetComponent<Collider>();
            if (backdropCollider != null) UnityEngine.Object.DestroyImmediate(backdropCollider);
            MeshRenderer backdropRenderer = backdrop.GetComponent<MeshRenderer>();
            backdropRenderer.sharedMaterial = RequireAsset<Material>(VisualBaselineBuilder.BackdropMaterialPath);
            backdropRenderer.shadowCastingMode = ShadowCastingMode.Off;
            backdropRenderer.receiveShadows = true;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            return new GameObject(rootName);
        }

        public static Canvas CreateCanvas(string name = "UICanvas")
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            return canvas;
        }

        public static GameObject CreatePanel(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = new Vector2(20f, 20f);
            rect.offsetMax = new Vector2(-20f, -20f);
            go.GetComponent<Image>().color = new Color(0.055f, 0.07f, 0.075f, 0.94f);
            return go;
        }

        public static Text CreateText(string name, Transform parent, string value, int size = 20)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = new Color(0.91f, 0.88f, 0.77f, 1f);
            text.alignment = TextAnchor.MiddleCenter;
            text.text = value;
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(420f, 52f);
            return text;
        }

        public static Button CreateButton(string name, Transform parent, string labelValue, out Text label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.2f, 0.34f, 0.3f, 1f);
            go.GetComponent<LayoutElement>().preferredHeight = 48f;
            label = CreateText("Label", go.transform, labelValue, 18);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            return go.GetComponent<Button>();
        }

        public static InputField CreateInput(string name, Transform parent, string placeholderValue)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.12f, 0.15f, 0.18f, 1f);
            go.GetComponent<LayoutElement>().preferredHeight = 46f;
            Text text = CreateText("Text", go.transform, string.Empty, 18);
            Text placeholder = CreateText("Placeholder", go.transform, placeholderValue, 18);
            placeholder.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            foreach (Text child in new[] { text, placeholder })
            {
                child.alignment = TextAnchor.MiddleLeft;
                child.rectTransform.anchorMin = Vector2.zero;
                child.rectTransform.anchorMax = Vector2.one;
                child.rectTransform.offsetMin = new Vector2(12f, 0f);
                child.rectTransform.offsetMax = new Vector2(-12f, 0f);
            }
            InputField input = go.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            return input;
        }

        public static VerticalLayoutGroup AddVerticalLayout(GameObject target, int spacing = 10)
        {
            VerticalLayoutGroup layout = target.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = spacing;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return layout;
        }

        public static void SetObject(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName) ??
                throw new InvalidOperationException(target.GetType().Name + " missing serialized property " + propertyName);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetObjects(UnityEngine.Object target, string propertyName, UnityEngine.Object[] values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName) ??
                throw new InvalidOperationException(target.GetType().Name + " missing serialized property " + propertyName);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static T RequireAsset<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException("Required scene asset is missing: " + path);
            return asset;
        }

        public static void Save(string path)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new InvalidOperationException("Could not save scene: " + path);
            AssetDatabase.SaveAssets();
        }
    }
}
