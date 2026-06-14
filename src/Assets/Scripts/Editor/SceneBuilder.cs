using UnityEngine;
using UnityEditor;
using UnityEngine.Tilemaps;
using TianZhang.Core;
using TianZhang.Entity;
using TianZhang.Combat;
using TianZhang.HexTile;
using TianZhang.Game;
using TianZhang.Map;

namespace TianZhang.Editor
{
    public static class SceneBuilder
    {
        private const string DataPath = "Assets/Data";
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
            AssetDatabase.CreateAsset(s1, $"{DataPath}/Spells/Spell_Jinguang.asset");

            var s2 = ScriptableObject.CreateInstance<SpellData>();
            s2.spellName = "流火灵符"; s2.type = SpellType.Magic; s2.range = SpellRange.Ranged;
            s2.minRange = 1; s2.maxRange = 3; s2.mpCost = 20; s2.cooldownTicks = 35; s2.damageMultiplier = 1.5f;
            AssetDatabase.CreateAsset(s2, $"{DataPath}/Spells/Spell_Huoling.asset");

            var s3 = ScriptableObject.CreateInstance<SpellData>();
            s3.spellName = "暗蚀"; s3.type = SpellType.Magic; s3.range = SpellRange.Melee;
            s3.minRange = 1; s3.maxRange = 1; s3.mpCost = 15; s3.cooldownTicks = 30; s3.damageMultiplier = 1.4f;
            AssetDatabase.CreateAsset(s3, $"{DataPath}/Spells/Spell_Anshi.asset");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
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
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static void EnsureDir(string path)
        {
            if (!System.IO.Directory.Exists(path))
                System.IO.Directory.CreateDirectory(path);
        }

        [MenuItem("Tools/天章/生成探索场景")]
        public static void BuildExplorationScene()
        {
            CreateDemoData();

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            // Camera
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 12f;
            cam.backgroundColor = new Color(0.08f, 0.1f, 0.14f);
            cam.transform.position = new Vector3(0, 0, -10);
            camGo.AddComponent<AudioListener>();

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
                AssetDatabase.LoadAssetAtPath<SpellData>($"{DataPath}/Spells/Spell_Jinguang.asset"),
                AssetDatabase.LoadAssetAtPath<SpellData>($"{DataPath}/Spells/Spell_Huoling.asset"),
            };

            // UI Manager（Canvas 由 BattleUIManager.Awake 自动创建）
            var uiGo = new GameObject("UIManager");
            var ui = uiGo.AddComponent<BattleUIManager>();
            explCtrl.uiManager = ui;

            // EventSystem
            var evGo = new GameObject("EventSystem");
            evGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            evGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            // GameManager
            var gmGo = new GameObject("GameManager");
            gmGo.AddComponent<GameManager>();

            string scenePath = "Assets/Scenes/ExplorationScene.unity";
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log("<color=cyan>天章探索场景已生成</color>");
        }
    }
}
