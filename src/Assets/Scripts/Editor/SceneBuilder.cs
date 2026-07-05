using UnityEngine;
using UnityEditor;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using System;
using System.IO;
using System.Linq;
using System.Text;
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
        private const string DataPath = "Assets/Data";

        // 场景路径常量（⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro）
        private const string StartMenuScenePath = "Assets/Scenes/StartMenuScene.unity";
        private const string WorldScenePath = "Assets/Scenes/WorldScene.unity";
        private const string SettlementScenePath = "Assets/Scenes/SettlementScene.unity";
        private const string AdventureScenePath = "Assets/Scenes/AdventureScene.unity";
                private static void CreateDemoData()
        {
            EnsureDir($"{DataPath}/Characters");
            EnsureDir($"{DataPath}/Spells");

            var p = ScriptableObject.CreateInstance<CharacterData>();
            p.charName = "太一修士";
            p.rootBone = 14; p.physique = 14; p.spirit = 14; p.mind = 14; p.reaction = 14; p.talent = 14;
            p.blockRate = 8; p.soulShieldRate = 10; p.critRate = 5; p.critDamage = 15;
            p.realmMultiplier = 1.5f;
            p.equippedSpells = new[] { "金光破岳", "流火灵符" };
            AssetDatabase.CreateAsset(p, $"{DataPath}/Characters/Char_Player.asset");

            var e = ScriptableObject.CreateInstance<CharacterData>();
            e.charName = "散修";
            e.rootBone = 12; e.physique = 12; e.spirit = 10; e.mind = 10; e.reaction = 12; e.talent = 10;
            e.blockRate = 6; e.soulShieldRate = 4; e.critRate = 4; e.critDamage = 10;
            e.realmMultiplier = 1.5f;
            e.equippedSpells = new[] { "暗蚀" };
            AssetDatabase.CreateAsset(e, $"{DataPath}/Characters/Char_Enemy.asset");

            var s1 = ScriptableObject.CreateInstance<SpellData>();
            s1.spellName = "金光破岳"; s1.type = SpellType.Physical; s1.range = SpellRange.Melee;
            s1.minRange = 1; s1.maxRange = 1; s1.mpCost = 15; s1.cooldownTicks = 30; s1.damageMultiplier = 1.6f;
            AssetDatabase.CreateAsset(s1, $"{DataPath}/Spells/Spell_spell_jinguangpoyue.asset");

            var s2 = ScriptableObject.CreateInstance<SpellData>();
            s2.spellName = "流火灵符"; s2.type = SpellType.Magic; s2.range = SpellRange.Ranged;
            s2.minRange = 1; s2.maxRange = 3; s2.mpCost = 20; s2.cooldownTicks = 35; s2.damageMultiplier = 1.5f;
            AssetDatabase.CreateAsset(s2, $"{DataPath}/Spells/Spell_spell_liuhuolingfu.asset");

            var s3 = ScriptableObject.CreateInstance<SpellData>();
            s3.spellName = "暗蚀"; s3.type = SpellType.Magic; s3.range = SpellRange.Melee;
            s3.minRange = 1; s3.maxRange = 1; s3.mpCost = 15; s3.cooldownTicks = 30; s3.damageMultiplier = 1.4f;
            AssetDatabase.CreateAsset(s3, $"{DataPath}/Spells/Spell_spell_tx_anshi.asset");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

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

        [MenuItem("Tools/天章/生成场景架构空场景")]
        public static void BuildSceneArchitectureShells()
        {
            BuildEmptyScene(StartMenuScenePath, "StartMenuRoot", new Color(0.05f, 0.05f, 0.08f));
            BuildEmptyScene(WorldScenePath, "WorldRoot", new Color(0.04f, 0.08f, 0.1f), typeof(TianZhang.World.WorldSceneController));
            BuildEmptyScene(SettlementScenePath, "SettlementRoot", new Color(0.08f, 0.07f, 0.05f), typeof(TianZhang.Settlement.SettlementSceneController));
            BuildEmptyScene(AdventureScenePath, "AdventureRoot", new Color(0.08f, 0.1f, 0.14f), typeof(TianZhang.Adventure.AdventureSceneController));
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
            BuildSceneArchitectureShells();

            ValidateBuildScenes(StartMenuScenePath, WorldScenePath, SettlementScenePath, AdventureScenePath);
            ValidateSceneShell(StartMenuScenePath, "StartMenuRoot", null);
            ValidateStartMenuShell(StartMenuScenePath);
            ValidateSceneShell(WorldScenePath, "WorldRoot", typeof(TianZhang.World.WorldSceneController));
            ValidateSceneShell(SettlementScenePath, "SettlementRoot", typeof(TianZhang.Settlement.SettlementSceneController));
            ValidateSceneShell(AdventureScenePath, "AdventureRoot", typeof(TianZhang.Adventure.AdventureSceneController));

            Debug.Log("[TQ-016] Scene architecture shells generated, registered, and loaded successfully.");
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
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();

            var gameManager = new GameObject("GameManager");
            gameManager.AddComponent<TianZhang.Game.GameManager>();
            gameManager.AddComponent<TianZhang.Game.SceneFlowManager>();


            if (sceneControllerType != null)
            {
                var controllerGo = new GameObject("SceneController");
                controllerGo.AddComponent(sceneControllerType);
            }

            if (rootName == "StartMenuRoot")
                CreateStartMenuSectSelection(gameManager.GetComponent<GameManager>());

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
            NormalizeGeneratedSceneYaml(scenePath);
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

        private static void NormalizeGeneratedSceneYaml(string scenePath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var fullPath = Path.Combine(projectRoot, scenePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                return;

            var content = File.ReadAllText(fullPath, Encoding.UTF8);
            var newline = content.Contains("\r\n") ? "\r\n" : "\n";
            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var normalized = string.Join(newline, lines.Select(line => line.TrimEnd()));
            File.WriteAllText(fullPath, normalized, new UTF8Encoding(false));
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
            Debug.Log("<color=cyan>天章副本场景已生成</color>");
        }

        [MenuItem("Tools/天章/生成探索场景")]
        public static void BuildExplorationScene()
        {
            CreateDemoData();

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            // Camera
            CreateMainCamera(12f, new Color(0.08f, 0.1f, 0.14f));

            // Hex Grid (复用战斗场景基础设施)
            var gridGo = new GameObject("HexGrid");
            var grid = gridGo.AddComponent<Grid>();
            grid.cellLayout = GridLayout.CellLayout.Hexagon;
            grid.cellSize = new Vector3(1, 1, 0);

            var groundGo = new GameObject("Ground");
            groundGo.transform.SetParent(gridGo.transform);
            var groundTml = groundGo.AddComponent<Tilemap>();
            var groundRnd = groundGo.AddComponent<TilemapRenderer>();
            groundTml.color = new Color(0.35f, 0.4f, 0.45f);

            var overlayGo = new GameObject("Overlay");
            overlayGo.transform.SetParent(gridGo.transform);
            var overlayTml = overlayGo.AddComponent<Tilemap>();
            var overlayRnd = overlayGo.AddComponent<TilemapRenderer>();
            overlayRnd.sortingOrder = 1;

            var unitGo = new GameObject("Units");
            unitGo.transform.SetParent(gridGo.transform);
            var unitTml = unitGo.AddComponent<Tilemap>();
            var unitRnd = unitGo.AddComponent<TilemapRenderer>();
            unitRnd.sortingOrder = 2;

            // TilemapManager
            var mgrGo = new GameObject("TilemapManager");
            var mgr = mgrGo.AddComponent<HexTilemapManager>();
            mgr.groundTilemap = groundTml;
            mgr.overlayTilemap = overlayTml;
            mgr.unitTilemap = unitTml;
            mgr.gridRadius = 15;
            mgr.groundTile = MakeTile("GroundTile", new Color(0.3f, 0.5f, 0.2f));
            mgr.moveHighlightTile = MakeTile("MoveHighlight", new Color(0.2f, 0.8f, 0.2f, 0.4f));
            mgr.attackHighlightTile = MakeTile("AttackHighlight", new Color(0.8f, 0.2f, 0.2f, 0.4f));
            mgr.selectedTile = MakeTile("Selected", new Color(1f, 0.8f, 0.2f, 0.5f));
            mgr.unitPrefab = MakeUnitPrefab();

            // Exploration Controller
            var explGo = new GameObject("ExplorationController");
            var explCtrl = explGo.AddComponent<TianZhang.Map.ExplorationController>();
            explCtrl.tilemapManager = mgr;
            explCtrl.mapRadius = 12;
            explCtrl.obstaclePercent = 15;
            explCtrl.enemyCount = 4;
            explCtrl.playerSpells = new[] {
                AssetDatabase.LoadAssetAtPath<SpellData>($"{DataPath}/Spells/Spell_spell_jinguangpoyue.asset"),
                AssetDatabase.LoadAssetAtPath<SpellData>($"{DataPath}/Spells/Spell_spell_liuhuolingfu.asset"),
            };

            // UI Manager（Canvas 由 BattleUIManager.Awake 自动创建）
            var uiGo = new GameObject("UIManager");
            var ui = uiGo.AddComponent<BattleUIManager>();
            explCtrl.uiManager = ui;

            // EventSystem
            var evGo = new GameObject("EventSystem");
            evGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            evGo.AddComponent<InputSystemUIInputModule>();

            // GameManager
            var gmGo = new GameObject("GameManager");
            gmGo.AddComponent<GameManager>();

            string scenePath = "Assets/Scenes/ExplorationScene.unity";
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log("<color=cyan>天章探索场景已生成</color>");
        }
    }
}
