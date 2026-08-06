using UnityEngine;
using UnityEditor;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using System;
using System.Linq;
using TianZhang.Core;
using TianZhang.Entity;
using TianZhang.Combat;
using TianZhang.HexTile;
using TianZhang.Game;
using TianZhang.Game.CharacterCreation;
using TianZhang.Map;
using TianZhang.World;
using TianZhang.Settlement;
using TianZhang.Adventure;
using TianZhang.Content;
using TianZhang.Tactical;
using UnityEngine.InputSystem.UI;

namespace TianZhang.Editor
{
    public static class SceneBuilder
    {
        // 场景路径常量（⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro）
        private const string StartMenuScenePath = "Assets/Scenes/StartMenuScene.unity";
        private const string WorldScenePath = "Assets/Scenes/WorldScene.unity";
        private const string SettlementScenePath = "Assets/Scenes/SettlementScene.unity";
        private const string AdventureScenePath = "Assets/Scenes/AdventureScene.unity";
        private const string HybridTacticalPrototypeScenePath = "Assets/Scenes/HybridTacticalPrototype.unity";
        // 01B 新增：唯一正式册界站点入口 ID（⚠️ 已修改/未审核；修改方：DeepSeek V4 Flash；变更范围：01B 面板接入）
        private const string CharterSiteEntryId = "charter_site_old_water_station";

        /// <summary>
        /// 创建正交主相机（⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro）
        /// </summary>
        private static Camera CreateMainCamera(float orthographicSize, Color background)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = orthographicSize;
            cam.backgroundColor = background;
            cam.transform.position = new Vector3(0, 0, -10);
            camGo.AddComponent<AudioListener>();
            return cam;
        }

        private static TileBase MakeTile(string name, Color color)
        {
            int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var px = new Color[s * s];
            float r = s / 2f - 1f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    px[y * s + x] = ((x - s/2f)*(x - s/2f) + (y - s/2f)*(y - s/2f) <= r*r) ? Color.white : Color.clear;
            tex.SetPixels(px);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();

            string p = $"Assets/Resources/Tiles/{name}.png";
            EnsureDir("Assets/Resources/Tiles");
            System.IO.File.WriteAllBytes(p, tex.EncodeToPNG());
            AssetDatabase.Refresh();
            var imp = AssetImporter.GetAtPath(p) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.spritePixelsPerUnit = s;
                imp.SaveAndReimport();
            }

            string tilePath = $"Assets/Resources/Tiles/{name}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, tilePath);
            }
            tile.name = name;
            tile.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(p);
            tile.color = color;
            EditorUtility.SetDirty(tile);
            return tile;
        }

        private static GameObject MakeUnitPrefab()
        {
            int s = 32;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var px = new Color[s * s];
            float r = s / 2f - 1f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    px[y * s + x] = ((x - s/2f)*(x - s/2f) + (y - s/2f)*(y - s/2f) <= r*r) ? Color.white : Color.clear;
            tex.SetPixels(px);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();

            string p = "Assets/Resources/UnitMarker.png";
            EnsureDir("Assets/Resources");
            System.IO.File.WriteAllBytes(p, tex.EncodeToPNG());
            AssetDatabase.Refresh();
            var imp = AssetImporter.GetAtPath(p) as TextureImporter;
            if (imp != null) { imp.textureType = TextureImporterType.Sprite; imp.spritePixelsPerUnit = s; imp.SaveAndReimport(); }

            var go = new GameObject("UnitMarker");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(p);
            sr.sortingOrder = 5;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, "Assets/Resources/UnitMarker.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        private static void EnsureDir(string path)
        {
            if (!System.IO.Directory.Exists(path))
                System.IO.Directory.CreateDirectory(path);
        }

        private static T SetSerializedComponentName<T>(T component, string componentName) where T : Component
        {
            var serializedComponent = new SerializedObject(component);
            var nameProperty = serializedComponent.FindProperty("m_Name");
            if (nameProperty != null && nameProperty.stringValue != componentName)
            {
                nameProperty.stringValue = componentName;
                serializedComponent.ApplyModifiedPropertiesWithoutUndo();
            }

            return component;
        }

        [MenuItem("Tools/天章/生成场景架构空场景")]
        public static void BuildSceneArchitectureShells()
        {
            BuildEmptyScene(StartMenuScenePath, "StartMenuRoot", new Color(0.05f, 0.05f, 0.08f));
            BuildEmptyScene(WorldScenePath, "WorldRoot", new Color(0.04f, 0.08f, 0.1f), typeof(TianZhang.World.WorldSceneController));
            BuildEmptyScene(SettlementScenePath, "SettlementRoot", new Color(0.08f, 0.07f, 0.05f), typeof(TianZhang.Settlement.SettlementSceneController));
            BuildAdventureScene();
            RegisterBuildScenes(StartMenuScenePath, WorldScenePath, SettlementScenePath, AdventureScenePath);
            AssetDatabase.Refresh();
        }

        private static void RegisterBuildScenes(params string[] scenePaths)
        {
            var scenePathSet = scenePaths.ToHashSet();
            var scenes = scenePaths
                .Select(path => new EditorBuildSettingsScene(path, true))
                .Concat(EditorBuildSettings.scenes.Where(scene => !scenePathSet.Contains(scene.path)))
                .ToArray();

            EditorBuildSettings.scenes = scenes;
        }

        public static void ValidateSceneArchitectureShellsForBatchMode()
        {
            ValidateBuildScenes(StartMenuScenePath, WorldScenePath, SettlementScenePath, AdventureScenePath);
            ValidateSceneShell(StartMenuScenePath, "StartMenuRoot", null);
            ValidateStartMenuShell(StartMenuScenePath);
            ValidateSceneShell(WorldScenePath, "WorldRoot", typeof(TianZhang.World.WorldSceneController));
            ValidateSceneShell(SettlementScenePath, "SettlementRoot", typeof(TianZhang.Settlement.SettlementSceneController));
            ValidateSettlementSceneBindings();
            ValidateSceneShell(AdventureScenePath, "AdventureRoot", typeof(TianZhang.Adventure.AdventureSceneController));
            ValidateAdventureSceneBindings();

            Debug.Log("[TQ-016] Scene architecture shells validated successfully.");
        }

        private static void ValidateBuildScenes(params string[] scenePaths)
        {
            var enabledScenePaths = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (!scenePaths.SequenceEqual(enabledScenePaths.Take(scenePaths.Length)))
                throw new InvalidOperationException("Scene architecture scenes are not registered first in EditorBuildSettings.");
        }

        private static void ValidateSceneShell(string scenePath, string rootName, Type sceneControllerType)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                scenePath,
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            Require(scene.IsValid(), $"{scenePath} is not a valid scene.");
            Require(scene.isLoaded, $"{scenePath} is not loaded.");
            Require(GameObject.Find(rootName) != null, $"{scenePath} missing {rootName}.");
            Require(GameObject.Find("Main Camera") != null, $"{scenePath} missing Main Camera.");
            Require(GameObject.Find("EventSystem") != null, $"{scenePath} missing EventSystem.");

            var gameManager = GameObject.Find("GameManager");
            Require(gameManager != null, $"{scenePath} missing GameManager.");
            Require(gameManager.GetComponent<TianZhang.Game.GameManager>() != null, $"{scenePath} missing GameManager component.");
            Require(gameManager.GetComponent<TianZhang.Game.SceneFlowManager>() != null, $"{scenePath} missing SceneFlowManager component.");

            var controller = GameObject.Find("SceneController");
            if (sceneControllerType == null)
            {
                Require(controller == null, $"{scenePath} should not contain SceneController.");
                return;
            }

            Require(controller != null, $"{scenePath} missing SceneController.");
            Require(controller.GetComponent(sceneControllerType) != null, $"{scenePath} missing {sceneControllerType.Name}.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void ValidateSettlementSceneBindings()
        {
            var controller = UnityEngine.Object.FindFirstObjectByType<SettlementSceneController>();
            var view = UnityEngine.Object.FindFirstObjectByType<SettlementSceneView>();
            var dispatcher = UnityEngine.Object.FindFirstObjectByType<SettlementFeatureDispatcher>();
            var charterSiteView = UnityEngine.Object.FindFirstObjectByType<CharterSiteView>(FindObjectsInactive.Include);
            var charterSiteController = UnityEngine.Object.FindFirstObjectByType<CharterSiteController>(FindObjectsInactive.Include);
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                "Assets/Data/ContentCatalog/ContentCatalog.asset");
            Require(controller != null, "Settlement scene is missing SettlementSceneController.");
            Require(view != null, "Settlement scene is missing SettlementSceneView.");
            Require(dispatcher != null, "Settlement scene is missing SettlementFeatureDispatcher.");
            Require(charterSiteView != null, "Settlement scene is missing CharterSiteView.");
            Require(charterSiteController != null, "Settlement scene is missing CharterSiteController.");
            Require(catalog != null, "Settlement scene is missing the formal ContentCatalogData asset.");

            var serializedController = new SerializedObject(controller);
            Require(
                serializedController.FindProperty("contentCatalog").objectReferenceValue == catalog,
                "Settlement scene does not serialize the formal ContentCatalogData reference.");
            Require(
                serializedController.FindProperty("sceneView").objectReferenceValue == view,
                "Settlement scene does not serialize the SettlementSceneView reference.");
            Require(
                serializedController.FindProperty("featureDispatcher").objectReferenceValue == dispatcher,
                "Settlement scene does not serialize the SettlementFeatureDispatcher reference.");
            Require(
                serializedController.FindProperty("charterSiteId").stringValue == CharterSiteEntryId,
                "Settlement scene does not serialize the formal charter site entry id.");

            var serializedView = new SerializedObject(view);
            Require(
                serializedView.FindProperty("bountyBoardView").objectReferenceValue != null,
                "Settlement scene does not serialize the BountyBoardView reference.");
            Require(
                serializedView.FindProperty("charterSiteView").objectReferenceValue == charterSiteView,
                "Settlement scene does not serialize the CharterSiteView reference.");
            Require(
                serializedView.FindProperty("charterSiteEntryButton").objectReferenceValue != null,
                "Settlement scene does not serialize the charter site entry button.");

            var serializedCharterView = new SerializedObject(charterSiteView);
            Require(
                serializedCharterView.FindProperty("controller").objectReferenceValue == charterSiteController,
                "CharterSitePanel does not serialize its CharterSiteController reference.");
            Require(
                serializedCharterView.FindProperty("resultText").objectReferenceValue != null,
                "CharterSitePanel does not serialize its result text.");
        }

        private static void ValidateAdventureSceneBindings()
        {
            var controller = UnityEngine.Object.FindFirstObjectByType<AdventureSceneController>();
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                "Assets/Data/ContentCatalog/ContentCatalog.asset");
            var environment = AssetDatabase.LoadAssetAtPath<EnvironmentProfileData>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            Require(controller != null, "Adventure scene is missing AdventureSceneController.");
            Require(catalog != null, "Adventure scene is missing the formal ContentCatalogData asset.");
            Require(environment != null, "Adventure scene is missing env_guanzhong_wild EnvironmentProfileData.");

            var serializedController = new SerializedObject(controller);
            Require(
                serializedController.FindProperty("contentCatalog").objectReferenceValue == catalog,
                "Adventure scene does not serialize the formal ContentCatalogData reference.");
            Require(
                serializedController.FindProperty("guanzhongWildEnvironmentProfile").objectReferenceValue ==
                environment,
                "Adventure scene does not serialize env_guanzhong_wild EnvironmentProfileData.");
        }
        /// <summary>

        /// 生成空场景外壳。sceneControllerType 为可选场景专属控制器（⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro）
        /// </summary>
        private static void BuildEmptyScene(string scenePath, string rootName, Color background, System.Type sceneControllerType = null, int buildIndexOffset = 0)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            CreateMainCamera(12f, background);
            new GameObject(rootName);

            var eventSystem = new GameObject("EventSystem");
            SetSerializedComponentName(eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>(), "EventSystem");
            SetSerializedComponentName(eventSystem.AddComponent<InputSystemUIInputModule>(), "InputSystemUIInputModule");

            var gameManager = new GameObject("GameManager");
            SetSerializedComponentName(gameManager.AddComponent<TianZhang.Game.GameManager>(), "GameManager");
            SetSerializedComponentName(gameManager.AddComponent<TianZhang.Game.SceneFlowManager>(), "SceneFlowManager");


            if (sceneControllerType != null)
            {
                var controllerGo = new GameObject("SceneController");
                var sceneController = controllerGo.AddComponent(sceneControllerType);
                SetSerializedComponentName(sceneController, sceneControllerType.Name);
            }

            if (rootName == "StartMenuRoot")
                CreateStartMenuSectSelection(gameManager.GetComponent<GameManager>());

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static void CreateStartMenuSectSelection(GameManager gameManager)
        {
            var canvasGo = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<RectTransform>().localScale = Vector3.one;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var panelGo = new GameObject("SectSelectionPanel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            panelRt.sizeDelta = Vector2.zero;
            panelGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

            var title = CreateText("Title", panelGo.transform, "选择门派", 36, Color.white, TextAnchor.MiddleCenter);
            var titleRt = title.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1f);
            titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0, -60);
            titleRt.sizeDelta = new Vector2(400, 50);

            var buttonContainerGo = new GameObject("ButtonContainer", typeof(RectTransform));
            buttonContainerGo.transform.SetParent(panelGo.transform, false);
            var buttonRt = buttonContainerGo.GetComponent<RectTransform>();
            buttonRt.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRt.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRt.anchoredPosition = new Vector2(0, 60);
            buttonRt.sizeDelta = new Vector2(300, 300);

            var selectedText = CreateText("SelectedText", panelGo.transform, "", 18, Color.yellow, TextAnchor.MiddleCenter);
            var selectedRt = selectedText.GetComponent<RectTransform>();
            selectedRt.anchorMin = new Vector2(0.5f, 0f);
            selectedRt.anchorMax = new Vector2(0.5f, 0f);
            selectedRt.anchoredPosition = new Vector2(0, 180);
            selectedRt.sizeDelta = new Vector2(500, 30);

            var descText = CreateText("DescText", panelGo.transform, "", 14, Color.gray, TextAnchor.MiddleCenter);
            var descRt = descText.GetComponent<RectTransform>();
            descRt.anchorMin = new Vector2(0.5f, 0f);
            descRt.anchorMax = new Vector2(0.5f, 0f);
            descRt.anchoredPosition = new Vector2(0, 140);
            descRt.sizeDelta = new Vector2(600, 50);

            var summaryGo = new GameObject("CharacterCreationSummary", typeof(RectTransform), typeof(VerticalLayoutGroup));
            summaryGo.transform.SetParent(panelGo.transform, false);
            var summaryRt = summaryGo.GetComponent<RectTransform>();
            summaryRt.anchorMin = new Vector2(1f, 0.5f);
            summaryRt.anchorMax = new Vector2(1f, 0.5f);
            summaryRt.anchoredPosition = new Vector2(-340, 90);
            summaryRt.sizeDelta = new Vector2(420, 190);
            var summaryLayout = summaryGo.GetComponent<VerticalLayoutGroup>();
            summaryLayout.spacing = 8;
            summaryLayout.childAlignment = TextAnchor.UpperLeft;
            summaryLayout.childControlWidth = true;
            summaryLayout.childControlHeight = true;
            summaryLayout.childForceExpandWidth = true;
            summaryLayout.childForceExpandHeight = false;

            var innateBudgetText = CreateText("InnateBudgetText", summaryGo.transform, "先天购买点剩余：0/25", 16, new Color(0.86f, 0.9f, 0.78f, 1f), TextAnchor.MiddleLeft);
            var visibleRootText = CreateText("VisibleRootText", summaryGo.transform, "显性灵根：中品水灵根", 16, new Color(0.86f, 0.9f, 0.78f, 1f), TextAnchor.MiddleLeft);
            var hiddenRootSeedText = CreateText("HiddenRootSeedText", summaryGo.transform, "隐藏灵根种子：无", 16, new Color(0.86f, 0.9f, 0.78f, 1f), TextAnchor.MiddleLeft);
            var creationBudgetText = CreateText("CreationBudgetText", summaryGo.transform, "创建预算剩余：10/10", 16, new Color(0.86f, 0.9f, 0.78f, 1f), TextAnchor.MiddleLeft);
            var craftSkillText = CreateText("CraftSkillText", summaryGo.transform, "技艺点剩余：0/3", 16, new Color(0.86f, 0.9f, 0.78f, 1f), TextAnchor.MiddleLeft);
            foreach (var summaryText in new[] { innateBudgetText, visibleRootText, hiddenRootSeedText, creationBudgetText, craftSkillText })
                summaryText.GetComponent<RectTransform>().sizeDelta = new Vector2(420, 28);

            var startButton = CreateButton("StartButton", panelGo.transform, "开始游戏", new Color(0.3f, 0.5f, 0.3f, 1f));
            var startRt = startButton.GetComponent<RectTransform>();
            startRt.anchorMin = new Vector2(0.5f, 0f);
            startRt.anchorMax = new Vector2(0.5f, 0f);
            startRt.anchoredPosition = new Vector2(0, 80);
            startRt.sizeDelta = new Vector2(200, 50);

            var selection = panelGo.AddComponent<SectSelectionManager>();
            selection.selectionPanel = panelGo;
            selection.buttonContainer = buttonContainerGo.transform;
            selection.startButton = startButton.GetComponent<Button>();
            selection.selectedSectText = selectedText.GetComponent<Text>();
            selection.selectedSectDesc = descText.GetComponent<Text>();
            selection.innateBudgetText = innateBudgetText.GetComponent<Text>();
            selection.visibleRootText = visibleRootText.GetComponent<Text>();
            selection.hiddenRootSeedText = hiddenRootSeedText.GetComponent<Text>();
            selection.creationBudgetText = creationBudgetText.GetComponent<Text>();
            selection.craftSkillText = craftSkillText.GetComponent<Text>();
            selection.gameManager = gameManager;
        }

        private static GameObject CreateText(string name, Transform parent, string text, int fontSize, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = anchor;
            return go;
        }

        private static GameObject CreateButton(string name, Transform parent, string labelText, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;

            var label = CreateText("Label", go.transform, labelText, 20, Color.white, TextAnchor.MiddleCenter);
            var labelRt = label.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.sizeDelta = Vector2.zero;
            return go;
        }

        private static void ValidateStartMenuShell(string scenePath)
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                scenePath,
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            Require(GameObject.Find("UICanvas") != null, $"{scenePath} missing UICanvas.");
            var selection = UnityEngine.Object.FindFirstObjectByType<SectSelectionManager>();
            Require(selection != null, $"{scenePath} missing SectSelectionManager.");
            Require(selection.selectionPanel != null, $"{scenePath} missing selection panel reference.");
            Require(selection.buttonContainer != null, $"{scenePath} missing button container reference.");
            Require(selection.startButton != null, $"{scenePath} missing start button reference.");
            Require(selection.gameManager != null, $"{scenePath} missing GameManager reference.");
            Require(selection.innateBudgetText != null, $"{scenePath} missing innate budget text reference.");
            Require(selection.visibleRootText != null, $"{scenePath} missing visible root text reference.");
            Require(selection.hiddenRootSeedText != null, $"{scenePath} missing hidden root seed text reference.");
            Require(selection.creationBudgetText != null, $"{scenePath} missing creation budget text reference.");
            Require(selection.craftSkillText != null, $"{scenePath} missing craft skill text reference.");
        }

        [MenuItem("Tools/天章/生成开始菜单场景")]
        public static void BuildStartMenuScene()
        {
            BuildEmptyScene(StartMenuScenePath, "StartMenuRoot", new Color(0.05f, 0.05f, 0.1f));
            Debug.Log("<color=cyan>天章开始菜单场景已生成</color>");
        }

        [MenuItem("Tools/天章/生成主世界场景")]
        public static void BuildWorldScene()
        {
            BuildEmptyScene(WorldScenePath, "WorldRoot", new Color(0.1f, 0.15f, 0.08f), typeof(TianZhang.World.WorldSceneController));
            AssignSceneLanguageTables(WorldScenePath, typeof(TianZhang.World.WorldSceneController));
            Debug.Log("<color=cyan>天章主世界场景已生成</color>");
        }

        /// <summary>
        /// 四个正式场景的显示组件都序列化同一个 Language.csv TextAsset 引用（U-GZ-UI-TEXT-01）：
        /// 视图运行时从该唯一文本源解析玩家显示文本，场景重建后引用不丢失。
        /// </summary>
        private static void AssignSceneLanguageTables(string scenePath, params System.Type[] componentTypes)
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                scenePath,
                UnityEditor.SceneManagement.OpenSceneMode.Single);
            var table = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/DataConfig/Language.csv");
            Require(table != null, scenePath + " must serialize the Language.csv TextAsset.");
            foreach (System.Type type in componentTypes)
            {
                var component = UnityEngine.Object.FindFirstObjectByType(type, FindObjectsInactive.Include);
                Require(component != null, scenePath + " missing " + type.Name + " for the language table.");
                var serialized = new SerializedObject(component);
                var property = serialized.FindProperty("languageTable");
                Require(property != null, type.Name + " must declare the serialized languageTable field.");
                property.objectReferenceValue = table;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene(),
                scenePath);
        }

        [MenuItem("Tools/天章/生成据点场景")]
        public static void BuildSettlementScene()
        {
            BuildEmptyScene(SettlementScenePath, "SettlementRoot", new Color(0.12f, 0.1f, 0.08f), typeof(TianZhang.Settlement.SettlementSceneController));
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                SettlementScenePath,
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            var controller = UnityEngine.Object.FindFirstObjectByType<SettlementSceneController>();
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                "Assets/Data/ContentCatalog/ContentCatalog.asset");
            Require(controller != null, "Settlement scene is missing SettlementSceneController.");
            Require(catalog != null, "Settlement scene is missing the formal ContentCatalogData asset.");

            var canvasGo = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            SetSerializedComponentName(canvas, "SettlementCanvas");
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            SetSerializedComponentName(scaler, "SettlementCanvasScaler");
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            SetSerializedComponentName(canvasGo.GetComponent<GraphicRaycaster>(), "SettlementGraphicRaycaster");

            var panelGo = new GameObject("SettlementPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 0.5f);
            panelRect.anchoredPosition = new Vector2(24f, 0f);
            panelRect.sizeDelta = new Vector2(420f, -48f);
            var panelImage = panelGo.GetComponent<Image>();
            SetSerializedComponentName(panelImage, "SettlementPanelImage");
            panelImage.color = new Color(0.06f, 0.05f, 0.04f, 0.9f);
            var layout = panelGo.GetComponent<VerticalLayoutGroup>();
            SetSerializedComponentName(layout, "SettlementPanelLayout");
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 10f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            CreateSettlementText("SettlementTitle", panelGo.transform, "据点", 30, Color.white, 44f);
            var nameText = CreateSettlementText("SettlementNameText", panelGo.transform, "据点数据", 24, Color.yellow, 38f);
            var detailText = CreateSettlementText("SettlementDetailText", panelGo.transform, "正在读取据点数据", 14, new Color(0.85f, 0.85f, 0.75f), 56f);
            var statusText = CreateSettlementText("SettlementStatusText", panelGo.transform, "等待据点数据", 14, Color.gray, 56f);
            var featureButton = CreateSettlementButton("SettlementFeature_bounty_board", "功能入口", panelGo.transform, out Text featureButtonText);
            var adventureButton = CreateSettlementButton("SettlementAdventure_guanzhong_wild", "副本入口", panelGo.transform, out Text adventureButtonText);
            var charterSiteEntryButton = CreateSettlementButton("SettlementCharterSiteEntry", "旧水驿入口", panelGo.transform, out Text charterSiteEntryLabel);
            var charterSiteEntryText = CreateSettlementText("SettlementCharterSiteEntryStatus", panelGo.transform, "旧水驿入口: 未打开", 14, Color.gray, 30f);
            var returnButton = CreateSettlementButton("ReturnToWorldButton", "返回主世界", panelGo.transform, out Text returnButtonText);
            returnButton.GetComponent<Image>().color = new Color(0.32f, 0.38f, 0.28f, 1f);
            returnButtonText.text = "返回主世界";

            var view = panelGo.AddComponent<SettlementSceneView>();
            SetSerializedComponentName(view, "SettlementSceneView");
            view.Configure(
                nameText,
                detailText,
                statusText,
                featureButton,
                featureButtonText,
                adventureButton,
                adventureButtonText,
                returnButton,
                CreateBountyBoard(canvasGo.transform),
                charterSiteEntryButton,
                charterSiteEntryText,
                CreateCharterSitePanel(canvasGo.transform));
            AssignLanguageTable(view);

            var dispatcherGo = new GameObject("SettlementFeatureDispatcher");
            var dispatcher = dispatcherGo.AddComponent<SettlementFeatureDispatcher>();
            SetSerializedComponentName(dispatcher, "SettlementFeatureDispatcher");
            controller.Configure(catalog, view, dispatcher, CharterSiteEntryId);

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene(),
                SettlementScenePath);
            Debug.Log("<color=cyan>天章据点场景已生成</color>");
        }

        private static BountyBoardView CreateBountyBoard(Transform canvas)
        {
            var boardGo = new GameObject(
                "BountyBoardPanel",
                typeof(RectTransform),
                typeof(Image),
                typeof(VerticalLayoutGroup));
            boardGo.transform.SetParent(canvas, false);
            var boardRect = boardGo.GetComponent<RectTransform>();
            boardRect.anchorMin = new Vector2(0.5f, 0.5f);
            boardRect.anchorMax = new Vector2(0.5f, 0.5f);
            boardRect.pivot = new Vector2(0.5f, 0.5f);
            boardRect.anchoredPosition = Vector2.zero;
            boardRect.sizeDelta = new Vector2(560f, 520f);
            var boardImage = boardGo.GetComponent<Image>();
            SetSerializedComponentName(boardImage, "BountyBoardPanelImage");
            boardImage.color = new Color(0.05f, 0.07f, 0.1f, 0.95f);
            var boardLayout = boardGo.GetComponent<VerticalLayoutGroup>();
            SetSerializedComponentName(boardLayout, "BountyBoardPanelLayout");
            boardLayout.padding = new RectOffset(24, 24, 24, 24);
            boardLayout.spacing = 8f;
            boardLayout.childForceExpandWidth = true;
            boardLayout.childForceExpandHeight = false;

            var title = CreateSettlementText("BountyBoardTitleText", boardGo.transform, "悬赏面板", 26, Color.white, 40f);
            var entries = CreateSettlementText(
                "BountyBoardEntriesText",
                boardGo.transform,
                "等待悬赏数据",
                14,
                new Color(0.85f, 0.85f, 0.75f),
                200f);
            var result = CreateSettlementText("BountyBoardResultText", boardGo.transform, "等待悬赏操作", 14, Color.gray, 36f);
            var acceptButton = CreateSettlementButton("BountyBoardAcceptButton", "接取", boardGo.transform, out Text acceptText);
            var claimButton = CreateSettlementButton("BountyBoardClaimButton", "领奖", boardGo.transform, out Text claimText);
            var closeButton = CreateSettlementButton("BountyBoardCloseButton", "关闭", boardGo.transform, out Text closeText);
            closeButton.GetComponent<Image>().color = new Color(0.32f, 0.38f, 0.28f, 1f);

            var board = boardGo.AddComponent<BountyBoardView>();
            SetSerializedComponentName(board, "BountyBoardView");
            board.Configure(title, entries, result, acceptButton, claimButton, closeButton);
            boardGo.SetActive(false);
            return board;
        }

        private static CharterSiteView CreateCharterSitePanel(Transform canvas)
        {
            var panelGo = new GameObject(
                "CharterSitePanel",
                typeof(RectTransform),
                typeof(Image),
                typeof(VerticalLayoutGroup));
            panelGo.transform.SetParent(canvas, false);
            var panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(0f, -60f);
            panelRect.sizeDelta = new Vector2(820f, 1020f);
            var panelImage = panelGo.GetComponent<Image>();
            SetSerializedComponentName(panelImage, "CharterSitePanelImage");
            panelImage.color = new Color(0.07f, 0.05f, 0.04f, 0.96f);
            var panelLayout = panelGo.GetComponent<VerticalLayoutGroup>();
            SetSerializedComponentName(panelLayout, "CharterSitePanelLayout");
            panelLayout.padding = new RectOffset(20, 20, 20, 20);
            panelLayout.spacing = 6f;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            var titleText = CreateSettlementText("CharterSiteTitleText", panelGo.transform, "旧水驿 · 册界单据点", 26, Color.white, 38f);
            var siteText = CreateSettlementText("CharterSiteSiteText", panelGo.transform, "等待站点数据", 14, Color.yellow, 36f);
            var stepText = CreateSettlementText("CharterSiteStepText", panelGo.transform, "等待交互", 14, Color.white, 30f);
            var identityText = CreateSettlementText("CharterSiteIdentityText", panelGo.transform, "等待身份数据", 14, new Color(0.85f, 0.85f, 0.75f), 64f);
            var authorizationText = CreateSettlementText("CharterSiteAuthorizationText", panelGo.transform, "等待授权数据", 14, new Color(0.85f, 0.85f, 0.75f), 36f);
            var nodeText = CreateSettlementText("CharterSiteNodeText", panelGo.transform, "等待节点数据", 14, new Color(0.85f, 0.85f, 0.75f), 36f);
            var supplyText = CreateSettlementText("CharterSiteSupplyText", panelGo.transform, "等待供给数据", 14, new Color(0.85f, 0.85f, 0.75f), 36f);
            var environmentText = CreateSettlementText("CharterSiteEnvironmentText", panelGo.transform, "等待环境引用", 14, new Color(0.85f, 0.85f, 0.75f), 36f);
            var resultText = CreateSettlementText("CharterSiteResultText", panelGo.transform, "等待操作", 14, Color.gray, 96f);

            var passageButton = CreateSettlementButton("CharterSitePassageButton", "通行", panelGo.transform, out Text passageText);
            var managementButton = CreateSettlementButton("CharterSiteManagementButton", "管理", panelGo.transform, out Text managementText);
            var nodeButton = CreateSettlementButton("CharterSiteNodeButton", "接通节点", panelGo.transform, out Text nodeButtonText);
            var registrationButton = CreateSettlementButton("CharterSiteRegistrationButton", "登记与授权", panelGo.transform, out Text registrationText);
            var supplyButton = CreateSettlementButton("CharterSiteSupplyButton", "准备供给", panelGo.transform, out Text supplyButtonText);
            var jindanButton = CreateSettlementButton("CharterSiteJindanButton", "金丹介入", panelGo.transform, out Text jindanText);
            var yuanyingButton = CreateSettlementButton("CharterSiteYuanyingButton", "元婴受锚", panelGo.transform, out Text yuanyingText);
            var formalButton = CreateSettlementButton("CharterSiteFormalButton", "正式调用", panelGo.transform, out Text formalText);
            var closeButton = CreateSettlementButton("CharterSiteCloseButton", "关闭并丢弃进度", panelGo.transform, out Text closeText);
            closeButton.GetComponent<Image>().color = new Color(0.32f, 0.38f, 0.28f, 1f);

            var charterController = panelGo.AddComponent<CharterSiteController>();
            SetSerializedComponentName(charterController, "CharterSiteController");
            var charterView = panelGo.AddComponent<CharterSiteView>();
            SetSerializedComponentName(charterView, "CharterSiteView");
            charterController.Configure(charterView);
            charterView.Configure(
                titleText,
                siteText,
                stepText,
                identityText,
                authorizationText,
                nodeText,
                supplyText,
                environmentText,
                resultText,
                passageButton,
                managementButton,
                nodeButton,
                registrationButton,
                supplyButton,
                jindanButton,
                yuanyingButton,
                formalButton,
                closeButton,
                charterController);
            panelGo.SetActive(false);
            return charterView;
        }

        private static Text CreateSettlementText(
            string name,
            Transform parent,
            string text,
            int fontSize,
            Color color,
            float preferredHeight)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            SetSerializedComponentName(label, name + "Label");
            var layoutElement = go.GetComponent<LayoutElement>();
            SetSerializedComponentName(layoutElement, name + "LayoutElement");
            layoutElement.preferredHeight = preferredHeight;
            return label;
        }

        private static Button CreateSettlementButton(string name, string labelText, Transform parent, out Text label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            SetSerializedComponentName(image, name + "Image");
            image.color = new Color(0.18f, 0.22f, 0.18f, 1f);
            var button = go.GetComponent<Button>();
            SetSerializedComponentName(button, name + "Button");
            var layoutElement = go.GetComponent<LayoutElement>();
            SetSerializedComponentName(layoutElement, name + "LayoutElement");
            layoutElement.preferredHeight = 40f;
            label = CreateSettlementText("Label", go.transform, labelText, 16, Color.white, 40f);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.sizeDelta = Vector2.zero;
            return button;
        }

        private static void AssignLanguageTable(UnityEngine.Object component)
        {
            var serialized = new SerializedObject(component);
            serialized.FindProperty("languageTable").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/DataConfig/Language.csv");
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Tools/天章/生成副本场景")]
        public static void BuildAdventureScene()
        {
            BuildEmptyScene(AdventureScenePath, "AdventureRoot", new Color(0.08f, 0.1f, 0.14f), typeof(TianZhang.Adventure.AdventureSceneController));
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                AdventureScenePath,
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            var adventureController = UnityEngine.Object.FindFirstObjectByType<AdventureSceneController>();
            var contentCatalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                "Assets/Data/ContentCatalog/ContentCatalog.asset");
            var guanzhongWildEnvironment = AssetDatabase.LoadAssetAtPath<EnvironmentProfileData>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            Require(adventureController != null, "Adventure scene is missing AdventureSceneController.");
            Require(contentCatalog != null, "Adventure scene is missing the formal ContentCatalogData.");
            Require(guanzhongWildEnvironment != null, "Adventure scene is missing env_guanzhong_wild EnvironmentProfileData.");
            SetSerializedComponentName(adventureController, "AdventureSceneController");
            adventureController.SetContentCatalog(contentCatalog);
            adventureController.SetGuanzhongWildEnvironmentProfile(guanzhongWildEnvironment);
            AssignLanguageTable(adventureController);

            var gridGo = new GameObject("HexGrid");
            var grid = gridGo.AddComponent<Grid>();
            grid.cellLayout = GridLayout.CellLayout.Hexagon;
            grid.cellSize = new Vector3(1f, 1f, 0f);

            var groundGo = new GameObject("Ground");
            groundGo.transform.SetParent(gridGo.transform);
            var groundTilemap = groundGo.AddComponent<Tilemap>();
            groundGo.AddComponent<TilemapRenderer>();

            var overlayGo = new GameObject("Overlay");
            overlayGo.transform.SetParent(gridGo.transform);
            var overlayTilemap = overlayGo.AddComponent<Tilemap>();
            var overlayRenderer = overlayGo.AddComponent<TilemapRenderer>();
            overlayRenderer.sortingOrder = 1;

            var unitsGo = new GameObject("Units");
            unitsGo.transform.SetParent(gridGo.transform);
            var unitTilemap = unitsGo.AddComponent<Tilemap>();
            var unitRenderer = unitsGo.AddComponent<TilemapRenderer>();
            unitRenderer.sortingOrder = 2;

            var tilemapManagerGo = new GameObject("TilemapManager");
            var tilemapManager = tilemapManagerGo.AddComponent<HexTilemapManager>();
            SetSerializedComponentName(tilemapManager, "HexTilemapManager");
            tilemapManager.groundTilemap = groundTilemap;
            tilemapManager.overlayTilemap = overlayTilemap;
            tilemapManager.unitTilemap = unitTilemap;
            tilemapManager.gridRadius = 6;
            tilemapManager.groundTile = MakeTile("AdventureGround", new Color(0.3f, 0.5f, 0.2f));
            tilemapManager.moveHighlightTile = MakeTile("AdventureMoveHighlight", new Color(0.2f, 0.8f, 0.2f, 0.4f));
            tilemapManager.attackHighlightTile = MakeTile("AdventureAttackHighlight", new Color(0.8f, 0.2f, 0.2f, 0.4f));
            tilemapManager.selectedTile = MakeTile("AdventureSelected", new Color(1f, 0.8f, 0.2f, 0.5f));
            tilemapManager.unitPrefab = MakeUnitPrefab();

            var explorationGo = new GameObject("AdventureEncounterController");
            var exploration = explorationGo.AddComponent<ExplorationController>();
            SetSerializedComponentName(exploration, "ExplorationController");
            exploration.tilemapManager = tilemapManager;
            exploration.mapRadius = 6;
            exploration.obstaclePercent = 0;
            exploration.enemyCount = 1;
            // 场景构建所有者显式绑定唯一生产无装备普攻档案；场景重建后引用不丢失。
            var basicUnarmed = AssetDatabase.LoadAssetAtPath<AttackProfileData>(
                $"Assets/Data/AttackProfiles/AttackProfile_{CharacterCreationCatalog.BasicUnarmedAttackProfileId}.asset");
            Require(basicUnarmed != null, "Adventure scene is missing the production basic_unarmed AttackProfileData asset.");
            exploration.attackProfiles = new[] { basicUnarmed };

            var uiGo = new GameObject("BattleUIManager");
            exploration.uiManager = uiGo.AddComponent<BattleUIManager>();
            SetSerializedComponentName(exploration.uiManager, "BattleUIManager");

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene(),
                AdventureScenePath);
            Debug.Log("<color=cyan>天章副本场景已生成</color>");
        }

        [MenuItem("Tools/天章/生成2.5D战棋隔离原型场景")]
        public static void BuildHybridTacticalPrototypeScene()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            var camera = CreateMainCamera(9f, new Color(0.05f, 0.07f, 0.09f));
            camera.transform.position = new Vector3(0f, 10f, -10f);
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -1f, 1f), Vector3.up);

            var environmentProfile = AssetDatabase.LoadAssetAtPath<EnvironmentProfileData>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            Require(environmentProfile != null, "Hybrid tactical prototype requires env_guanzhong_wild EnvironmentProfileData.");

            var root = new GameObject("HybridTacticalPrototypeRoot");
            var renderer = root.AddComponent<HybridTacticalRenderer>();
            SetSerializedComponentName(renderer, "HybridTacticalRenderer");
            renderer.SetPresentationCamera(camera);

            var controller = root.AddComponent<HybridTacticalPrototypeController>();
            SetSerializedComponentName(controller, "HybridTacticalPrototypeController");
            controller.SetEnvironmentProfile(environmentProfile);
            controller.SetPresentationCamera(camera);
            controller.SetPrototypeRadius(5);

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene(),
                HybridTacticalPrototypeScenePath);
            AssetDatabase.Refresh();
            Debug.Log("<color=cyan>2.5D战棋隔离原型场景已生成</color>");
        }

    }
}
