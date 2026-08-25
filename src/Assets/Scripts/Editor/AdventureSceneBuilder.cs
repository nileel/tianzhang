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
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TianZhang.Editor
{
    public static class AdventureSceneBuilder
    {
        private static readonly float[] FacingProbeYaws = { 90f, 150f, 210f, 270f, 330f, 30f };

        private static readonly int[,] VisualBaselineCells =
        {
            { -2, 0, 0 }, { -1, 0, 1 }, { 0, 0, 2 }, { 1, 0, 1 }, { 2, 0, 0 },
            { -1, 1, 0 }, { 0, 1, 1 }, { 1, -1, 0 }, { 0, -1, 0 },
        };

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

            VisualBaselineBuilder.BuildStaticChessAssets();
            VisualBaselineBuilder.BuildTacticalSpriteAssets();
            VisualBaselineBuilder.BuildBattleAnimationSpriteAssets();
            ValidateReadOnlyVisualAssets();
            GameObject visualBaselineBoard = BuildVisualBaselineMatrix();

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

            BuildBattleVisualComparisonPanel(canvas, visualBaselineBoard);

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

        [MenuItem("天章/场景/重建战术精灵隔离矩阵")]
        public static void RebuildTacticalSpriteIsolationMatrix()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject board = FindVisualBaselineBoard(scene);
            if (board == null) throw new InvalidOperationException("AdventureScene is missing the visual baseline board.");

            Transform group = board.transform.Find("TacticalSpriteProbeGroup");
            if (group == null) throw new InvalidOperationException("AdventureScene is missing the tactical sprite probe group.");
            group.gameObject.SetActive(false);

            Transform existingOcclusion = group.Find("TacticalSpriteOcclusionProbe");
            if (existingOcclusion != null) UnityEngine.Object.DestroyImmediate(existingOcclusion.gameObject);

            GameObject spritePrefab = SceneBuildSupport.RequireAsset<GameObject>(VisualBaselineBuilder.TacticalSpritePrefabPath);
            CreateTacticalSpriteOcclusionProbe(group, VisualBaselineCells, spritePrefab);

            if (!EditorSceneManager.SaveScene(scene, SceneBuildSupport.AdventureScenePath))
                throw new InvalidOperationException("Could not save the regenerated AdventureScene.");
            AssetDatabase.SaveAssets();
        }

        [MenuItem("天章/场景/重建战斗动画精灵隔离矩阵")]
        public static void RebuildBattleAnimationSpriteIsolationMatrix()
        {
            VisualBaselineBuilder.BuildBattleAnimationSpriteAssets();
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject board = FindVisualBaselineBoard(scene);
            if (board == null) throw new InvalidOperationException("AdventureScene is missing the visual baseline board.");

            Transform existingGroup = board.transform.Find(BattleAnimationSpriteProbeMatrix.GroupName);
            if (existingGroup != null) UnityEngine.Object.DestroyImmediate(existingGroup.gameObject);
            CreateBattleAnimationSpriteProbes(board.transform, VisualBaselineCells);

            if (!EditorSceneManager.SaveScene(scene, SceneBuildSupport.AdventureScenePath))
                throw new InvalidOperationException("Could not save the regenerated battle animation isolation matrix.");
            AssetDatabase.SaveAssets();
        }

        [MenuItem("天章/场景/重建战场角色可玩比较入口")]
        public static void RebuildBattleVisualComparisonEntry()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject board = FindVisualBaselineBoard(scene);
            if (board == null) throw new InvalidOperationException("AdventureScene is missing the visual baseline board.");
            Canvas canvas = Array.Find(scene.GetRootGameObjects(), root => root.name == "UICanvas")?.GetComponent<Canvas>();
            if (canvas == null) throw new InvalidOperationException("AdventureScene is missing the comparison UI canvas.");

            Transform previousPanel = canvas.transform.Find("BattleVisualComparisonPanel");
            if (previousPanel != null) UnityEngine.Object.DestroyImmediate(previousPanel.gameObject);
            BattleVisualComparisonController previousController = board.GetComponent<BattleVisualComparisonController>();
            if (previousController != null) UnityEngine.Object.DestroyImmediate(previousController);
            BuildBattleVisualComparisonPanel(canvas, board);

            if (!EditorSceneManager.SaveScene(scene, SceneBuildSupport.AdventureScenePath))
                throw new InvalidOperationException("Could not save the regenerated battle visual comparison entry.");
            AssetDatabase.SaveAssets();
        }

        private static GameObject FindVisualBaselineBoard(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "VisualBaselineBoard") return root;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform board = root.transform.Find("VisualBaselineBoard");
                if (board != null) return board.gameObject;
            }
            return null;
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
            SceneBuildSupport.RequireAsset<GameObject>(VisualBaselineBuilder.StaticChessPrefabPath);
            SceneBuildSupport.RequireAsset<GameObject>(VisualBaselineBuilder.StaticChessMotionEffectPrefabPath);
            SceneBuildSupport.RequireAsset<GameObject>(VisualBaselineBuilder.TacticalSpritePrefabPath);
            SceneBuildSupport.RequireAsset<GameObject>(VisualBaselineBuilder.BattleAnimationSpritePrefabPath);
        }

        private static void BuildBattleVisualComparisonPanel(Canvas canvas, GameObject board)
        {
            GameObject panel = SceneBuildSupport.CreatePanel("BattleVisualComparisonPanel", canvas.transform,
                new Vector2(0.02f, 0.03f), new Vector2(0.38f, 0.42f));
            SceneBuildSupport.AddVerticalLayout(panel, 6);
            SceneBuildSupport.CreateText("BattleVisualComparisonTitle", panel.transform, "战场角色表现比较", 22);
            Text status = SceneBuildSupport.CreateText("BattleVisualComparisonStatus", panel.transform, string.Empty, 13);

            Transform routeButtons = CreateComparisonGrid("ComparisonRouteButtons", panel.transform, 2, 1);
            Button dynamic2DRoute = SceneBuildSupport.CreateButton("Comparison2DRouteButton", routeButtons, "2D 动态", out _);
            Button static3DRoute = SceneBuildSupport.CreateButton("ComparisonStatic3DRouteButton", routeButtons, "静态 3D", out _);

            Transform directionButtons = CreateComparisonGrid("ComparisonDirectionButtons", panel.transform, 3, 2);
            var directions = new Button[BattleVisualComparisonController.DirectionCount];
            for (int direction = 0; direction < directions.Length; direction++)
                directions[direction] = SceneBuildSupport.CreateButton(
                    "ComparisonDirectionButton_" + direction, directionButtons, "方向 " + direction, out _);

            Transform eventButtons = CreateComparisonGrid("ComparisonEventButtons", panel.transform, 3, 2);
            string[] eventLabels = { "待机", "移动", "攻击", "受击", "施法", "死亡" };
            var presentationEvents = new Button[eventLabels.Length];
            for (int index = 0; index < eventLabels.Length; index++)
                presentationEvents[index] = SceneBuildSupport.CreateButton(
                    "ComparisonEventButton_" + index, eventButtons, eventLabels[index], out _);
            Button reset = SceneBuildSupport.CreateButton("ComparisonResetButton", panel.transform, "复位当前比较", out _);

            BattleVisualComparisonController comparison = board.GetComponent<BattleVisualComparisonController>();
            if (comparison == null) comparison = board.AddComponent<BattleVisualComparisonController>();
            comparison.Configure(status);
            UnityEventTools.AddPersistentListener(dynamic2DRoute.onClick, comparison.SelectBattleAnimation2DRoute);
            UnityEventTools.AddPersistentListener(static3DRoute.onClick, comparison.SelectStatic3DRoute);
            for (int direction = 0; direction < directions.Length; direction++)
                UnityEventTools.AddIntPersistentListener(directions[direction].onClick, comparison.SelectDirection, direction);
            for (int presentationEvent = 0; presentationEvent < presentationEvents.Length; presentationEvent++)
                UnityEventTools.AddIntPersistentListener(presentationEvents[presentationEvent].onClick,
                    comparison.TriggerPresentationByIndex, presentationEvent);
            UnityEventTools.AddPersistentListener(reset.onClick, comparison.ResetPresentations);
        }

        private static Transform CreateComparisonGrid(string name, Transform parent, int columns, int rows)
        {
            var gridObject = new GameObject(name, typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            gridObject.transform.SetParent(parent, false);
            GridLayoutGroup grid = gridObject.GetComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.cellSize = new Vector2(112f, 40f);
            grid.spacing = new Vector2(6f, 5f);
            LayoutElement layout = gridObject.GetComponent<LayoutElement>();
            layout.preferredHeight = rows * 40f + (rows - 1) * 5f;
            return gridObject.transform;
        }

        private static GameObject BuildVisualBaselineMatrix()
        {
            var board = new GameObject("VisualBaselineBoard");
            Mesh columnMesh = SceneBuildSupport.RequireAsset<Mesh>(VisualBaselineBuilder.HexColumnMeshPath);
            Mesh overlayMesh = SceneBuildSupport.RequireAsset<Mesh>(VisualBaselineBuilder.HexOverlayMeshPath);
            Material top = SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.GroundTopMaterialPath);
            Material side = SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.GroundSideMaterialPath);

            int[,] cells = VisualBaselineCells;
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
            CreateTacticalSpriteProbes(board.transform, cells);
            CreateBattleAnimationSpriteProbes(board.transform, cells);

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
            return board;
        }

        private static void CreateFacingProbes(Transform parent, int[,] cells)
        {
            GameObject chessPrefab = SceneBuildSupport.RequireAsset<GameObject>(VisualBaselineBuilder.StaticChessPrefabPath);
            for (int direction = 0; direction < FacingProbeYaws.Length; direction++)
            {
                HexCoord neighbor = HexCoord.Directions[direction];
                int heightLevel = FindCellHeight(cells, neighbor.q, neighbor.r);
                GameObject probe = (GameObject)PrefabUtility.InstantiatePrefab(chessPrefab);
                probe.name = "FacingProbe_" + direction;
                probe.transform.SetParent(parent, false);
                probe.transform.localPosition = HexToWorld(neighbor.q, neighbor.r, HeightForLevel(heightLevel));
                probe.transform.localRotation = Quaternion.Euler(0f, FacingProbeYaws[direction], 0f);
                probe.GetComponent<StaticChessPresentationController>().CaptureRestPose();
            }
        }

        private static void CreateTacticalSpriteProbes(Transform parent, int[,] cells)
        {
            GameObject group = new GameObject("TacticalSpriteProbeGroup");
            group.transform.SetParent(parent, false);
            group.transform.localPosition = Vector3.zero;
            group.transform.localRotation = Quaternion.identity;
            group.transform.localScale = Vector3.one;

            GameObject spritePrefab = SceneBuildSupport.RequireAsset<GameObject>(VisualBaselineBuilder.TacticalSpritePrefabPath);
            for (int direction = 0; direction < FacingProbeYaws.Length; direction++)
            {
                HexCoord neighbor = HexCoord.Directions[direction];
                int heightLevel = FindCellHeight(cells, neighbor.q, neighbor.r);
                GameObject probe = (GameObject)PrefabUtility.InstantiatePrefab(spritePrefab);
                probe.name = "TacticalSpriteProbe_" + direction;
                probe.transform.SetParent(group.transform, false);
                probe.transform.localPosition = HexToWorld(neighbor.q, neighbor.r, HeightForLevel(heightLevel));
                probe.transform.localRotation = Quaternion.Euler(0f, FacingProbeYaws[direction], 0f);

                TacticalSpritePresentationController controller = probe.GetComponent<TacticalSpritePresentationController>();
                if (controller == null) throw new InvalidOperationException("Tactical sprite probe is missing its presentation controller.");
                SpriteRenderer body = probe.GetComponentInChildren<SpriteRenderer>(true);
                if (body == null) throw new InvalidOperationException("Tactical sprite probe is missing its SpriteRenderer.");
                body.sprite = SceneBuildSupport.RequireAsset<Sprite>(VisualBaselineBuilder.TacticalSpriteTexturePath(direction));

                var serialized = new SerializedObject(controller);
                SerializedProperty directionProperty = serialized.FindProperty("activeDirection") ??
                    throw new InvalidOperationException("Tactical sprite controller is missing the active direction field.");
                directionProperty.intValue = direction;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            CreateTacticalSpriteOcclusionProbe(group.transform, cells, spritePrefab);

            // 2D 组默认关闭，仅启用既有静态 3D 路线，形成互斥隔离矩阵；
            // 测试通过 TacticalSpriteProbeMatrix.SetActiveRoute(true) 显式切换到 2D。
            group.SetActive(false);
        }

        private static void CreateTacticalSpriteOcclusionProbe(Transform group, int[,] cells, GameObject spritePrefab)
        {
            int heightLevel = FindCellHeight(cells, 2, 0);
            GameObject probe = (GameObject)PrefabUtility.InstantiatePrefab(spritePrefab);
            probe.name = "TacticalSpriteOcclusionProbe";
            probe.transform.SetParent(group, false);
            probe.transform.localPosition = HexToWorld(2, 0, HeightForLevel(heightLevel));
            probe.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            TacticalSpritePresentationController controller = probe.GetComponent<TacticalSpritePresentationController>();
            if (controller == null) throw new InvalidOperationException("Tactical sprite occlusion probe is missing its presentation controller.");
            SpriteRenderer body = probe.GetComponentInChildren<SpriteRenderer>(true);
            if (body == null) throw new InvalidOperationException("Tactical sprite occlusion probe is missing its SpriteRenderer.");
            body.sprite = SceneBuildSupport.RequireAsset<Sprite>(VisualBaselineBuilder.TacticalSpriteTexturePath(0));

            var serialized = new SerializedObject(controller);
            SerializedProperty directionProperty = serialized.FindProperty("activeDirection") ??
                throw new InvalidOperationException("Tactical sprite controller is missing the active direction field.");
            directionProperty.intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateBattleAnimationSpriteProbes(Transform parent, int[,] cells)
        {
            GameObject group = new GameObject(BattleAnimationSpriteProbeMatrix.GroupName);
            group.transform.SetParent(parent, false);
            group.transform.localPosition = Vector3.zero;
            group.transform.localRotation = Quaternion.identity;
            group.transform.localScale = Vector3.one;

            GameObject spritePrefab = SceneBuildSupport.RequireAsset<GameObject>(VisualBaselineBuilder.BattleAnimationSpritePrefabPath);
            for (int direction = 0; direction < FacingProbeYaws.Length; direction++)
            {
                HexCoord neighbor = HexCoord.Directions[direction];
                int heightLevel = FindCellHeight(cells, neighbor.q, neighbor.r);
                GameObject probe = (GameObject)PrefabUtility.InstantiatePrefab(spritePrefab);
                probe.name = BattleAnimationSpriteProbeMatrix.ProbePrefix + direction;
                probe.transform.SetParent(group.transform, false);
                probe.transform.localPosition = HexToWorld(neighbor.q, neighbor.r, HeightForLevel(heightLevel));
                probe.transform.localRotation = Quaternion.Euler(0f, FacingProbeYaws[direction], 0f);
                ConfigureBattleAnimationProbe(probe, direction);
            }

            int occlusionHeightLevel = FindCellHeight(cells, 2, 0);
            GameObject occlusionProbe = (GameObject)PrefabUtility.InstantiatePrefab(spritePrefab);
            occlusionProbe.name = "BattleAnimationSpriteOcclusionProbe";
            occlusionProbe.transform.SetParent(group.transform, false);
            occlusionProbe.transform.localPosition = HexToWorld(2, 0, HeightForLevel(occlusionHeightLevel));
            occlusionProbe.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            ConfigureBattleAnimationProbe(occlusionProbe, 0);

            // 动态 2D 样例必须显式切换，不能与旧静态 2D 或静态 3D 探针共同渲染。
            group.SetActive(false);
        }

        private static void ConfigureBattleAnimationProbe(GameObject probe, int direction)
        {
            BattleAnimationSpritePresentationController controller =
                probe.GetComponent<BattleAnimationSpritePresentationController>();
            if (controller == null) throw new InvalidOperationException("Battle animation sprite probe is missing its presentation controller.");
            SpriteRenderer body = probe.GetComponentInChildren<SpriteRenderer>(true);
            if (body == null) throw new InvalidOperationException("Battle animation sprite probe is missing its SpriteRenderer.");
            body.sprite = RequireBattleAnimationSprite(0, direction, 0);

            var serialized = new SerializedObject(controller);
            SerializedProperty directionProperty = serialized.FindProperty("activeDirection") ??
                throw new InvalidOperationException("Battle animation controller is missing the active direction field.");
            directionProperty.intValue = direction;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Sprite RequireBattleAnimationSprite(int state, int direction, int frame)
        {
            string path = VisualBaselineBuilder.BattleAnimationSpriteTexturePath(state);
            string expectedName = VisualBaselineBuilder.BattleAnimationSpriteName(state, direction, frame);
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                Sprite sprite = asset as Sprite;
                if (sprite != null && sprite.name == expectedName) return sprite;
            }
            throw new InvalidOperationException("Battle animation atlas is missing imported sprite: " + expectedName);
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
