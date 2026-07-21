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
using TianZhang.Map;
using TianZhang.World;
using TianZhang.Settlement;
using TianZhang.Adventure;
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
            if (imp != null) { imp.textureType = TextureImporterType.Sprite; imp.spritePixelsPerUnit = s; imp.SaveAndReimport(); }

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = name;
            tile.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(p);
            tile.color = color;
            AssetDatabase.CreateAsset(tile, $"Assets/Resources/Tiles/{name}.asset");
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
            ValidateSceneShell(AdventureScenePath, "AdventureRoot", typeof(TianZhang.Adventure.AdventureSceneController));

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
            Debug.Log("<color=cyan>天章主世界场景已生成</color>");
        }

        [MenuItem("Tools/天章/生成据点场景")]
        public static void BuildSettlementScene()
        {
            BuildEmptyScene(SettlementScenePath, "SettlementRoot", new Color(0.12f, 0.1f, 0.08f), typeof(TianZhang.Settlement.SettlementSceneController));
            Debug.Log("<color=cyan>天章据点场景已生成</color>");
        }

        [MenuItem("Tools/天章/生成副本场景")]
        public static void BuildAdventureScene()
        {
            BuildEmptyScene(AdventureScenePath, "AdventureRoot", new Color(0.08f, 0.1f, 0.14f), typeof(TianZhang.Adventure.AdventureSceneController));
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                AdventureScenePath,
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            var adventureController = UnityEngine.Object.FindFirstObjectByType<AdventureSceneController>();
            var guanzhongWildEnemy = AssetDatabase.LoadAssetAtPath<CharacterData>(
                "Assets/Data/Characters/Char_Enemy_enemy_shijiahou.asset");
            Require(adventureController != null, "Adventure scene is missing AdventureSceneController.");
            Require(guanzhongWildEnemy != null, "Adventure scene is missing the formal stone-armored beast CharacterData.");
            SetSerializedComponentName(adventureController, "AdventureSceneController");
            adventureController.SetGuanzhongWildEnemyTemplates(new[] { guanzhongWildEnemy });

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

            var uiGo = new GameObject("BattleUIManager");
            exploration.uiManager = uiGo.AddComponent<BattleUIManager>();
            SetSerializedComponentName(exploration.uiManager, "BattleUIManager");

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene(),
                AdventureScenePath);
            Debug.Log("<color=cyan>天章副本场景已生成</color>");
        }

    }
}
