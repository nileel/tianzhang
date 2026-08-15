using System;
using System.Collections.Generic;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Features.Adventure;
using TianZhang.Features.CombatPresentation;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace TianZhang.Editor
{
    public static class AdventureSceneBuilder
    {
        private static readonly float[] FacingProbeYaws = { 90f, 150f, 210f, 270f, 330f, 30f };

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
            BuildVisualBaselineMatrix();

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
            SceneBuildSupport.RequireAsset<Mesh>(VisualBaselineBuilder.HexColumnMeshPath);
            SceneBuildSupport.RequireAsset<Mesh>(VisualBaselineBuilder.HexOverlayMeshPath);
            SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.GroundTopMaterialPath);
            SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.GroundSideMaterialPath);
            SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.SurfaceMaterialPath);
            SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.ReachableMaterialPath);
            SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.SelectedMaterialPath);
            SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.AttackMaterialPath);
            SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.OccluderMaterialPath);
        }

        private static void BuildVisualBaselineMatrix()
        {
            var board = new GameObject("VisualBaselineBoard");
            Mesh columnMesh = SceneBuildSupport.RequireAsset<Mesh>(VisualBaselineBuilder.HexColumnMeshPath);
            Mesh overlayMesh = SceneBuildSupport.RequireAsset<Mesh>(VisualBaselineBuilder.HexOverlayMeshPath);
            Material top = SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.GroundTopMaterialPath);
            Material side = SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.GroundSideMaterialPath);

            int[,] cells =
            {
                { -2, 0, 0 }, { -1, 0, 1 }, { 0, 0, 2 }, { 1, 0, 1 }, { 2, 0, 0 },
                { -1, 1, 0 }, { 0, 1, 1 }, { 1, -1, 0 }, { 0, -1, 0 },
            };
            for (int index = 0; index < cells.GetLength(0); index++)
            {
                int q = cells[index, 0];
                int r = cells[index, 1];
                int heightLevel = cells[index, 2];
                float visualHeight = HeightForLevel(heightLevel);
                var cell = new GameObject(
                    "VisualHex_" + q + "_" + r + "_Height_" + heightLevel,
                    typeof(MeshFilter),
                    typeof(MeshRenderer));
                cell.transform.SetParent(board.transform, false);
                cell.transform.localPosition = HexToWorld(q, r, 0f);
                cell.transform.localScale = new Vector3(1f, visualHeight, 1f);
                cell.GetComponent<MeshFilter>().sharedMesh = columnMesh;
                MeshRenderer renderer = cell.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { top, side };
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            CreateFacingProbes(board.transform, cells);

            CreateOverlay(board.transform, overlayMesh, -2, 0, 0, "SurfaceOverlay",
                VisualBaselineBuilder.SurfaceMaterialPath, 10, 0.012f);
            CreateOverlay(board.transform, overlayMesh, -1, 0, 1, "ReachableOverlay",
                VisualBaselineBuilder.ReachableMaterialPath, 20, 0.024f);
            CreateOverlay(board.transform, overlayMesh, 0, 0, 2, "SelectedOverlay",
                VisualBaselineBuilder.SelectedMaterialPath, 30, 0.036f);
            CreateOverlay(board.transform, overlayMesh, 1, 0, 1, "AttackOverlay",
                VisualBaselineBuilder.AttackMaterialPath, 40, 0.048f);

            GameObject occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            occluder.name = "VisualBaselineOccluder";
            occluder.transform.SetParent(board.transform, false);
            float occluderBase = HeightForLevel(0);
            occluder.transform.localPosition = HexToWorld(2, 0, occluderBase + 0.58f);
            occluder.transform.localScale = new Vector3(0.38f, 1.15f, 0.38f);
            Collider collider = occluder.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            MeshRenderer occluderRenderer = occluder.GetComponent<MeshRenderer>();
            occluderRenderer.sharedMaterial =
                SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.OccluderMaterialPath);
            occluderRenderer.shadowCastingMode = ShadowCastingMode.On;
            occluderRenderer.receiveShadows = true;
        }

        private static void CreateFacingProbes(Transform parent, int[,] cells)
        {
            GameObject markerPrefab = SceneBuildSupport.RequireAsset<GameObject>(VisualBaselineBuilder.UnitMarkerPrefabPath);
            for (int direction = 0; direction < FacingProbeYaws.Length; direction++)
            {
                HexCoord neighbor = HexCoord.Directions[direction];
                int heightLevel = FindCellHeight(cells, neighbor.q, neighbor.r);
                GameObject probe = (GameObject)PrefabUtility.InstantiatePrefab(markerPrefab);
                probe.name = "FacingProbe_" + direction;
                probe.transform.SetParent(parent, false);
                probe.transform.localPosition = HexToWorld(neighbor.q, neighbor.r, HeightForLevel(heightLevel));
                probe.transform.localRotation = Quaternion.Euler(0f, FacingProbeYaws[direction], 0f);
            }
        }

        private static int FindCellHeight(int[,] cells, int q, int r)
        {
            for (int index = 0; index < cells.GetLength(0); index++)
                if (cells[index, 0] == q && cells[index, 1] == r)
                    return cells[index, 2];
            throw new InvalidOperationException("Facing probe is missing a visual baseline cell.");
        }

        private static void CreateOverlay(
            Transform parent,
            Mesh mesh,
            int q,
            int r,
            int heightLevel,
            string name,
            string materialPath,
            int sortingOrder,
            float offset)
        {
            var overlay = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            overlay.transform.SetParent(parent, false);
            overlay.transform.localPosition = HexToWorld(q, r, HeightForLevel(heightLevel) + offset);
            overlay.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = overlay.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = SceneBuildSupport.RequireAsset<Material>(materialPath);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = sortingOrder;
        }

        private static float HeightForLevel(int heightLevel) => 0.34f + heightLevel * 0.28f;

        private static Vector3 HexToWorld(int q, int r, float y) =>
            new Vector3(q + r * 0.5f, y, r * 0.8660254f + 1f);
    }
}
