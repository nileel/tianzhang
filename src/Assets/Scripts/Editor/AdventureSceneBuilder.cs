using System;
using System.Collections.Generic;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Features.Adventure;
using TianZhang.Features.CombatPresentation;
using TianZhang.Infrastructure.UnityContent;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace TianZhang.Editor
{
    public static class AdventureSceneBuilder
    {
        [MenuItem("天章/场景/重建冒险")]
        public static void Build()
        {
            GameObject root = SceneBuildSupport.BeginScene("AdventureRoot", new Color(0.08f, 0.1f, 0.14f));
            AdventureSceneInstaller installer = root.AddComponent<AdventureSceneInstaller>();
            AdventureController controller = root.AddComponent<AdventureController>();
            AdventureInputController input = root.AddComponent<AdventureInputController>();
            AdventureHudPresenter adventureHud = root.AddComponent<AdventureHudPresenter>();
            AdventureUnitSpawner spawner = root.AddComponent<AdventureUnitSpawner>();
            EncounterCoordinator coordinator = root.AddComponent<EncounterCoordinator>();
            CombatHudPresenter combatPresenter = root.AddComponent<CombatHudPresenter>();
            CombatHudView combatView = root.AddComponent<CombatHudView>();
            CombatCommandInput combatInput = root.AddComponent<CombatCommandInput>();
            CombatActionBarView actionBar = root.AddComponent<CombatActionBarView>();
            CombatLogView logView = root.AddComponent<CombatLogView>();

            ValidateReadOnlyVisualAssets();
            Sprite ground = SceneBuildSupport.RequireAsset<Sprite>("Assets/Resources/Tiles/AdventureGround.png");
            var groundObject = new GameObject("AdventureGroundReference", typeof(SpriteRenderer));
            groundObject.GetComponent<SpriteRenderer>().sprite = ground;
            groundObject.transform.localScale = Vector3.one * 12f;

            Canvas canvas = SceneBuildSupport.CreateCanvas();
            GameObject adventurePanel = SceneBuildSupport.CreatePanel("AdventurePanel", canvas.transform, new Vector2(0.02f, 0.45f), new Vector2(0.28f, 0.96f));
            SceneBuildSupport.AddVerticalLayout(adventurePanel);
            Text adventureText = SceneBuildSupport.CreateText("AdventureText", adventurePanel.transform, "冒险", 26);
            Text adventureStatus = SceneBuildSupport.CreateText("AdventureStatus", adventurePanel.transform, string.Empty, 15);
            var nodeContainer = new GameObject("AdventureNodeContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
            nodeContainer.transform.SetParent(adventurePanel.transform, false);
            LayoutElement nodeLayout = nodeContainer.AddComponent<LayoutElement>();
            nodeLayout.minHeight = 160f;
            nodeLayout.flexibleHeight = 1f;
            SceneBuildSupport.SetObject(adventureHud, "nodeContainer", nodeContainer.transform);
            SceneBuildSupport.SetObject(adventureHud, "adventureText", adventureText);
            SceneBuildSupport.SetObject(adventureHud, "statusText", adventureStatus);

            GameObject combatPanel = SceneBuildSupport.CreatePanel("CombatHudRoot", canvas.transform, new Vector2(0.62f, 0.05f), new Vector2(0.98f, 0.95f));
            SceneBuildSupport.AddVerticalLayout(combatPanel);
            Text turn = SceneBuildSupport.CreateText("CombatTurnText", combatPanel.transform, "战斗", 24);
            Text player = SceneBuildSupport.CreateText("CombatPlayerText", combatPanel.transform, string.Empty, 17);
            Text enemy = SceneBuildSupport.CreateText("CombatEnemyText", combatPanel.transform, string.Empty, 17);
            Text log = SceneBuildSupport.CreateText("CombatLogText", combatPanel.transform, string.Empty, 14);
            GameObject actionRoot = new GameObject("CombatActionBar", typeof(RectTransform), typeof(VerticalLayoutGroup));
            actionRoot.transform.SetParent(combatPanel.transform, false);
            Button basic = SceneBuildSupport.CreateButton("BasicAttackButton", actionRoot.transform, "普攻", out _);
            Button art = SceneBuildSupport.CreateButton("ArtButton", actionRoot.transform, "术法", out Text artLabel);
            Button divine = SceneBuildSupport.CreateButton("DivineButton", actionRoot.transform, "神通", out Text divineLabel);
            Button guard = SceneBuildSupport.CreateButton("GuardButton", actionRoot.transform, "防御", out _);
            Button wait = SceneBuildSupport.CreateButton("WaitButton", actionRoot.transform, "待机", out _);
            combatPanel.SetActive(false);

            SceneBuildSupport.SetObject(combatView, "root", combatPanel);
            SceneBuildSupport.SetObject(combatView, "playerText", player);
            SceneBuildSupport.SetObject(combatView, "enemyText", enemy);
            SceneBuildSupport.SetObject(combatView, "turnText", turn);
            SceneBuildSupport.SetObject(logView, "logText", log);
            SceneBuildSupport.SetObject(actionBar, "root", actionRoot);
            SceneBuildSupport.SetObject(actionBar, "basicAttackButton", basic);
            SceneBuildSupport.SetObject(actionBar, "artButton", art);
            SceneBuildSupport.SetObject(actionBar, "divineButton", divine);
            SceneBuildSupport.SetObject(actionBar, "guardButton", guard);
            SceneBuildSupport.SetObject(actionBar, "waitButton", wait);
            SceneBuildSupport.SetObject(actionBar, "artLabel", artLabel);
            SceneBuildSupport.SetObject(actionBar, "divineLabel", divineLabel);
            SceneBuildSupport.SetObject(combatInput, "actionBar", actionBar);
            SceneBuildSupport.SetObject(combatPresenter, "view", combatView);
            SceneBuildSupport.SetObject(combatPresenter, "commandInput", combatInput);
            SceneBuildSupport.SetObject(combatPresenter, "logView", logView);

            SceneBuildSupport.SetObject(installer, "languageTable", SceneBuildSupport.RequireAsset<TextAsset>("Assets/DataConfig/Language.csv"));
            SceneBuildSupport.SetObject(installer, "contentCatalog", SceneBuildSupport.RequireAsset<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset"));
            SceneBuildSupport.RequireAsset<AdventureMapData>("Assets/Data/Adventures/AdventureMap_guanzhong_wild.asset");
            SceneBuildSupport.SetObject(installer, "environmentProfile", SceneBuildSupport.RequireAsset<EnvironmentProfileAsset>("Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset"));
            SceneBuildSupport.SetObject(installer, "unitMarkerPrefab", SceneBuildSupport.RequireAsset<GameObject>("Assets/Resources/UnitMarker.prefab"));
            SceneBuildSupport.SetObjects(installer, "attackProfiles", LoadAttackProfiles());
            SceneBuildSupport.SetObject(installer, "controller", controller);
            SceneBuildSupport.SetObject(installer, "input", input);
            SceneBuildSupport.SetObject(installer, "adventureHud", adventureHud);
            SceneBuildSupport.SetObject(installer, "unitSpawner", spawner);
            SceneBuildSupport.SetObject(installer, "encounterCoordinator", coordinator);
            SceneBuildSupport.SetObject(installer, "combatHudPresenter", combatPresenter);
            SceneBuildSupport.SetObject(installer, "combatHudView", combatView);
            SceneBuildSupport.SetObject(installer, "combatCommandInput", combatInput);
            SceneBuildSupport.SetObject(installer, "combatActionBar", actionBar);
            SceneBuildSupport.SetObject(installer, "combatLogView", logView);
            SceneBuildSupport.Save(SceneBuildSupport.AdventureScenePath);
        }

        private static UnityEngine.Object[] LoadAttackProfiles()
        {
            string[] guids = AssetDatabase.FindAssets("t:AttackProfileData", new[] { "Assets/Data/AttackProfiles" });
            Array.Sort(guids, StringComparer.Ordinal);
            var profiles = new List<UnityEngine.Object>(guids.Length);
            foreach (string guid in guids)
            {
                AttackProfileData profile = AssetDatabase.LoadAssetAtPath<AttackProfileData>(AssetDatabase.GUIDToAssetPath(guid));
                if (profile != null) profiles.Add(profile);
            }
            if (profiles.Count == 0) throw new InvalidOperationException("Adventure requires committed AttackProfileData assets.");
            return profiles.ToArray();
        }

        private static void ValidateReadOnlyVisualAssets()
        {
            string[] tilePaths =
            {
                "Assets/Resources/Tiles/AdventureGround.asset",
                "Assets/Resources/Tiles/AdventureMoveHighlight.asset",
                "Assets/Resources/Tiles/AdventureAttackHighlight.asset",
                "Assets/Resources/Tiles/AdventureSelected.asset",
            };
            foreach (string path in tilePaths) SceneBuildSupport.RequireAsset<TileBase>(path);
            SceneBuildSupport.RequireAsset<Sprite>("Assets/Resources/UnitMarker.png");
        }
    }
}
