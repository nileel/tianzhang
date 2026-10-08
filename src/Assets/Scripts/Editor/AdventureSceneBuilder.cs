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

        private static void ConfigureOrdinaryCombatActions(AdventureSceneInstaller installer)
        {
            var serialized = new SerializedObject(installer);
            SerializedProperty definitions = serialized.FindProperty("ordinaryActions");
            definitions.arraySize = 2;
            for (int index = 0; index < 2; index++)
            {
                SerializedProperty definition = definitions.GetArrayElementAtIndex(index);
                definition.FindPropertyRelative("Kind").intValue = (int)(index == 0 ? CombatCommandKind.Guard : CombatCommandKind.SwapSpell);
                definition.FindPropertyRelative("ActionPointCostUnits").intValue = 50000;
                definition.FindPropertyRelative("MinimumNetCostUnits").intValue = 50000;
                definition.FindPropertyRelative("CtPenalty").intValue = 0;
                definition.FindPropertyRelative("ReuseCooldownTicks").intValue = index == 0 ? 0 : 60;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void UpdateOrdinaryCombatActions()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            AdventureSceneInstaller installer = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out AdventureSceneInstaller found)) installer = found;
            if (installer == null) throw new InvalidOperationException("adventure_installer_missing");
            ConfigureOrdinaryCombatActions(installer);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, SceneBuildSupport.AdventureScenePath))
                throw new InvalidOperationException("AdventureScene save failed.");
        }

        [MenuItem("天章/场景/重建冒险")]
        public static void Build()
        {
            GameObject root = SceneBuildSupport.BeginScene("AdventureRoot", new Color(0.08f, 0.1f, 0.14f));
            // This legacy plane intersects the approved terrain's deep rock walls.
            GameObject.Find("VisualBackdrop").SetActive(false);
            AdventureSceneInstaller installer = root.AddComponent<AdventureSceneInstaller>();
            ConfigureOrdinaryCombatActions(installer);
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
            Static3DCombatUnitPresentationProvider static3DProvider =
                root.AddComponent<Static3DCombatUnitPresentationProvider>();

            EnvironmentProfileAsset environmentProfile = SceneBuildSupport.RequireAsset<EnvironmentProfileAsset>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            GameObject battlefield = BuildGuanzhongBattlefield(environmentProfile);
            ConfigureAdventureCamera(Camera.main, battlefield);

            Canvas canvas = SceneBuildSupport.CreateCanvas();
            GameObject adventurePanel = SceneBuildSupport.CreatePanel("AdventurePanel", canvas.transform, Vector2.zero, Vector2.one);
            SettlementSceneBuilder.PlacePanel(adventurePanel, new Vector2(48, 600), new Vector2(420, 1032));
            SettlementSceneBuilder.ApplyPanelSkin(adventurePanel);
            SettlementSceneBuilder.ConfigureColumn(adventurePanel);
            Text adventureText = SceneBuildSupport.CreateText("AdventureText", adventurePanel.transform, "冒险", 26);
            Text adventureStatus = SceneBuildSupport.CreateText("AdventureStatus", adventurePanel.transform, string.Empty, 15);
            SettlementSceneBuilder.ApplyTextSkin(adventureText, 80);
            SettlementSceneBuilder.ApplyTextSkin(adventureStatus, 48);
            var nodeContainer = new GameObject("AdventureNodeContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
            nodeContainer.transform.SetParent(adventurePanel.transform, false);
            SettlementSceneBuilder.ConfigureColumn(nodeContainer, 0);
            SettlementSceneBuilder.SetHeight(nodeContainer, 192);
            SceneBuildSupport.SetObject(adventureHud, "nodeContainer", nodeContainer.transform);
            SceneBuildSupport.SetObject(adventureHud, "adventureText", adventureText);
            SceneBuildSupport.SetObject(adventureHud, "statusText", adventureStatus);
            SceneBuildSupport.SetObject(adventureHud, "nodeFont", SettlementSceneBuilder.UiFont);
            SceneBuildSupport.SetObject(adventureHud, "nodeNormal", SettlementSceneBuilder.UiSprite("Guanzhong_Button_Normal"));
            SceneBuildSupport.SetObject(adventureHud, "nodeHover", SettlementSceneBuilder.UiSprite("Guanzhong_Button_Hover"));
            SceneBuildSupport.SetObject(adventureHud, "nodeSelected", SettlementSceneBuilder.UiSprite("Guanzhong_Button_Selected"));

            GameObject combatPanel = SceneBuildSupport.CreatePanel("CombatHudRoot", canvas.transform, Vector2.zero, Vector2.one);
            SettlementSceneBuilder.PlacePanel(combatPanel, new Vector2(1452, 48), new Vector2(1872, 1032));
            SettlementSceneBuilder.ApplyPanelSkin(combatPanel);
            SettlementSceneBuilder.ConfigureColumn(combatPanel);
            Text turn = SceneBuildSupport.CreateText("CombatTurnText", combatPanel.transform, "战斗", 24);
            SettlementSceneBuilder.AddPortrait(combatPanel.transform, "CombatPlayerPortrait", 80);
            Text player = SceneBuildSupport.CreateText("CombatPlayerText", combatPanel.transform, string.Empty, 20);
            Text enemy = SceneBuildSupport.CreateText("CombatEnemyText", combatPanel.transform, string.Empty, 20);
            SettlementSceneBuilder.ApplyTextSkin(turn, 44);
            SettlementSceneBuilder.ApplyTextSkin(player, 64);
            SettlementSceneBuilder.ApplyTextSkin(enemy, 64);
            BuildStarterActionBar(combatPanel.transform, actionBar);
            var logViewport = new GameObject("CombatLogViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            logViewport.transform.SetParent(combatPanel.transform, false);
            logViewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.15f);
            SettlementSceneBuilder.SetHeight(logViewport, 156);
            logViewport.GetComponent<LayoutElement>().flexibleHeight = 1;
            Text log = SceneBuildSupport.CreateText("CombatLogText", logViewport.transform, string.Empty, 18);
            log.font = SettlementSceneBuilder.UiFont;
            log.color = new Color32(190, 202, 195, 255);
            log.alignment = TextAnchor.UpperLeft;
            log.raycastTarget = false;
            log.rectTransform.anchorMin = new Vector2(0, 1);
            log.rectTransform.anchorMax = Vector2.one;
            log.rectTransform.pivot = new Vector2(0.5f, 1);
            log.rectTransform.anchoredPosition = Vector2.zero;
            log.rectTransform.sizeDelta = new Vector2(-16, 0);
            log.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect logScroll = logViewport.GetComponent<ScrollRect>();
            logScroll.viewport = logViewport.GetComponent<RectTransform>();
            logScroll.content = log.rectTransform;
            logScroll.horizontal = false;
            logScroll.movementType = ScrollRect.MovementType.Clamped;
            logScroll.scrollSensitivity = 28;
            SceneBuildSupport.SetObject(logView, "scrollRect", logScroll);
            combatPanel.SetActive(false);

            SceneBuildSupport.SetObject(combatView, "root", combatPanel);
            SceneBuildSupport.SetObject(combatView, "playerText", player);
            SceneBuildSupport.SetObject(combatView, "enemyText", enemy);
            SceneBuildSupport.SetObject(combatView, "turnText", turn);
            SceneBuildSupport.SetObject(logView, "logText", log);
            SceneBuildSupport.SetObject(combatInput, "actionBar", actionBar);
            SceneBuildSupport.SetObject(combatPresenter, "view", combatView);
            SceneBuildSupport.SetObject(combatPresenter, "commandInput", combatInput);
            SceneBuildSupport.SetObject(combatPresenter, "logView", logView);

            SceneBuildSupport.SetObject(installer, "languageTable", SceneBuildSupport.RequireAsset<TextAsset>("Assets/DataConfig/Language.csv"));
            SceneBuildSupport.SetObject(installer, "contentCatalog", SceneBuildSupport.RequireAsset<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset"));
            SceneBuildSupport.RequireAsset<AdventureMapData>("Assets/Data/Adventures/AdventureMap_guanzhong_wild.asset");
            SceneBuildSupport.SetObject(installer, "environmentProfile", environmentProfile);
            SceneBuildSupport.SetObject(
                installer,
                "combatPresentationProfileCatalog",
                SceneBuildSupport.RequireAsset<CombatUnitPresentationProfileCatalogData>(
                    FormalStatic3DCombatPieceBuilder.CatalogAssetPath));
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
            SceneBuildSupport.SetObject(installer, "static3DUnitPresentationProvider", static3DProvider);
            SceneBuildSupport.SetObject(
                static3DProvider,
                "profileSet",
                SceneBuildSupport.RequireAsset<Static3DCombatUnitPresentationProfileSet>(
                    FormalStatic3DCombatPieceBuilder.ProfileSetAssetPath));
            BuildUnitHealthBars(root, canvas, GameObject.Find("Main Camera").GetComponent<Camera>());
            BuildCombatTimeline(root, canvas, installer, combatPresenter);
            BuildCombatMovement(root, Camera.main, battlefield, environmentProfile);
            BuildCombatTargetSelection(root, canvas);
            BuildCombatActionPreview(root, canvas);
            BuildCombatLayout(root, canvas);
            SceneBuildSupport.Save(SceneBuildSupport.AdventureScenePath);
        }

        [MenuItem("天章/场景/收口关中战斗布局")]
        public static void RebuildCombatLayout()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject root = Array.Find(scene.GetRootGameObjects(), value => value.name == "AdventureRoot");
            Canvas canvas = Array.Find(scene.GetRootGameObjects(), value => value.name == "UICanvas").GetComponent<Canvas>();
            BuildCombatLayout(root, canvas);
            SceneBuildSupport.Save(SceneBuildSupport.AdventureScenePath);
        }

        private static void BuildCombatLayout(GameObject root, Canvas canvas)
        {
            // Only this Adventure layout owns these rectangles. Shared builders and the camera stay unchanged.
            RectTransform hud = LayoutObject(canvas.transform, "CombatHudRoot");
            RemoveLayout(hud);
            hud.anchorMin = Vector2.zero; hud.anchorMax = Vector2.one;
            hud.offsetMin = hud.offsetMax = Vector2.zero;
            hud.GetComponent<Image>().color = Color.clear;
            hud.GetComponent<Image>().raycastTarget = false;
            GameObject hotbar = LayoutPanel(hud, "CombatHotbar");
            SettlementSceneBuilder.PlacePanel(hotbar, new Vector2(351, 31), new Vector2(1442, 269));
            foreach (string name in new[] { "CombatTurnText", "CombatPlayerPortrait", "CombatPlayerText", "CombatEnemyText", "CombatActionBar" })
                LayoutObject(hud, name).SetParent(hotbar.transform, false);
            PlaceLayoutRect(LayoutObject(hotbar.transform, "CombatTurnText"), 16, 178, 198, 44);
            PlaceLayoutRect(LayoutObject(hotbar.transform, "CombatPlayerPortrait"), 16, 118, 60, 54);
            MakeLayoutScroll(hotbar.transform, "PlayerSummaryBody", LayoutObject(hotbar.transform, "CombatPlayerText").GetComponent<Text>(), 16, 16, 877, 126);
            LayoutObject(hotbar.transform, "CombatEnemyText").gameObject.SetActive(false);
            RectTransform actions = LayoutObject(hotbar.transform, "CombatActionBar");
            RemoveLayout(actions);
            PlaceLayoutRect(actions, 230, 16, 841, 206);
            PlaceLayoutRect(LayoutObject(actions, "MovementControls"), 0, 78, 240, 128);
            SettlementSceneBuilder.SetHeight(LayoutObject(actions, "MovementStatus").gameObject, 64);
            PlaceLayoutRect(LayoutObject(actions, "BasicAttackButton"), 252, 138, 114, 68);
            PlaceLayoutRect(LayoutObject(actions, "ArtButton"), 378, 138, 216, 68);
            PlaceLayoutRect(LayoutObject(actions, "SecondArtButton"), 606, 138, 216, 68);
            PlaceLayoutRect(LayoutObject(actions, "GuardButton"), 252, 92, 114, 44);
            PlaceLayoutRect(LayoutObject(actions, "WaitButton"), 378, 92, 216, 44);
            PlaceLayoutRect(LayoutObject(actions, "SecondArtTargetButton"), 606, 92, 216, 44);
            PlaceLayoutRect(LayoutObject(actions, "DivineButton"), 0, 20, 240, 64);
            PlaceLayoutRect(LayoutObject(actions, "SwapSpellButton"), 252, 8, 114, 68);
            PlaceLayoutRect(LayoutObject(actions, "ActionStatusText"), 378, 8, 444, 76);
            foreach (Text text in actions.GetComponentsInChildren<Text>(true)) text.fontSize = 20;

            RectTransform target = LayoutObject(canvas.transform, "CombatTargetRoot");
            RemoveLayout(target);
            SettlementSceneBuilder.PlacePanel(target.gameObject, new Vector2(1508, 286), new Vector2(1868, 786));
            Text targetTitle = LayoutText(target, "TargetHeading", "目标详情", 24);
            PlaceLayoutRect(targetTitle.rectTransform, 20, 440, 210, 44);
            PlaceLayoutRect(LayoutObject(target, "CloseTargetButton"), 240, 440, 100, 44);
            LayoutObject(target, "CloseTargetButton").GetComponentInChildren<Text>(true).text = "关闭";
            MakeLayoutScroll(target, "TargetBody", LayoutObject(target, "CombatTargetDetails").GetComponent<Text>(), 20, 20, 20, 76);

            RectTransform preview = LayoutObject(canvas.transform, "CombatActionPreviewRoot");
            RemoveLayout(preview);
            SettlementSceneBuilder.PlacePanel(preview.gameObject, new Vector2(351, 281), new Vector2(1442, 477));
            MakeLayoutScroll(preview, "PreviewBody", LayoutObject(preview, "ActionPreviewSummary").GetComponent<Text>(), 20, 80, 20, 16);
            PlaceLayoutRect(LayoutObject(preview, "PreviewActions"), 20, 16, 1051, 48);

            CombatBountyTrackerView bounty = root.GetComponent<CombatBountyTrackerView>() ?? root.AddComponent<CombatBountyTrackerView>();
            GameObject bountyPanel = LayoutPanel(hud, "CombatBountyTrackerRoot");
            SettlementSceneBuilder.PlacePanel(bountyPanel, new Vector2(1508, 809), new Vector2(1868, 909));
            Text bountyText = LayoutText(bountyPanel.transform, "BountySummary", string.Empty, 22);
            MakeLayoutScroll(bountyPanel.transform, "BountyBody", bountyText, 20, 16, 20, 16);
            SceneBuildSupport.SetObject(bounty, "root", bountyPanel);
            SceneBuildSupport.SetObject(bounty, "summary", bountyText);
            SceneBuildSupport.SetObject(root.GetComponent<AdventureSceneInstaller>(), "combatBountyTrackerView", bounty);

            CombatLogView logView = root.GetComponent<CombatLogView>();
            GameObject logPanel = LayoutPanel(hud, "CombatLogPanel");
            SettlementSceneBuilder.PlacePanel(logPanel, new Vector2(968, 495), new Vector2(1488, 845));
            Text logHeading = LayoutText(logPanel.transform, "LogHeading", "战报", 24);
            PlaceLayoutRect(logHeading.rectTransform, 20, 290, 360, 44);
            RectTransform logViewport = LayoutObject(hud, "CombatLogViewport");
            logViewport.SetParent(logPanel.transform, false);
            StretchLayoutRect(logViewport, 20, 20, 20, 76);
            Button closeLog = LayoutButton(logPanel.transform, "CloseLogButton", "关闭");
            PlaceLayoutRect(closeLog.GetComponent<RectTransform>(), 400, 290, 100, 44);
            Button toggleLog = LayoutButton(hud, "ToggleLogButton", "战报");
            SettlementSceneBuilder.PlacePanel(toggleLog.gameObject, new Vector2(1708, 32), new Vector2(1868, 84));
            SceneBuildSupport.SetObject(logView, "panel", logPanel);
            SceneBuildSupport.SetObject(logView, "toggleButton", toggleLog);
            SceneBuildSupport.SetObject(logView, "closeButton", closeLog);
            logPanel.SetActive(false);
            // The rail's current/selected/predicted units keep their existing runtime writer.
            LayoutObject(canvas.transform, "CombatTimelineRoot/TimelineFocus").gameObject.SetActive(false);
            hud.gameObject.SetActive(false);
        }

        private static RectTransform LayoutObject(Transform parent, string name)
        {
            if (name.Contains("/")) return (RectTransform)parent.Find(name);
            foreach (RectTransform child in parent.GetComponentsInChildren<RectTransform>(true))
                if (child.name == name) return child;
            throw new InvalidOperationException("Missing formal layout object: " + name);
        }

        private static void RemoveLayout(RectTransform rect)
        {
            foreach (LayoutGroup layout in rect.GetComponents<LayoutGroup>()) UnityEngine.Object.DestroyImmediate(layout);
            ContentSizeFitter fit = rect.GetComponent<ContentSizeFitter>();
            if (fit != null) UnityEngine.Object.DestroyImmediate(fit);
        }

        private static void PlaceLayoutRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
        }

        private static void StretchLayoutRect(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top);
        }

        private static GameObject LayoutPanel(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing.gameObject;
            GameObject panel = SceneBuildSupport.CreatePanel(name, parent, Vector2.zero, Vector2.one);
            SettlementSceneBuilder.ApplyPanelSkin(panel);
            return panel;
        }

        private static Text LayoutText(Transform parent, string name, string value, int size)
        {
            Text text = null;
            foreach (Text existing in parent.GetComponentsInChildren<Text>(true))
                if (existing.name == name) { text = existing; break; }
            if (text == null) text = SceneBuildSupport.CreateText(name, parent, value, size);
            text.font = SettlementSceneBuilder.UiFont; text.alignment = TextAnchor.MiddleLeft;
            text.color = new Color32(244, 234, 212, 255); text.raycastTarget = false;
            return text;
        }

        private static Button LayoutButton(Transform parent, string name, string value)
        {
            Button button = parent.Find(name)?.GetComponent<Button>() ?? SceneBuildSupport.CreateButton(name, parent, value, out _);
            SettlementSceneBuilder.ApplyButtonSkin(button);
            return button;
        }

        private static void MakeLayoutScroll(Transform parent, string name, Text text, float left, float bottom, float right, float top)
        {
            Transform existing = parent.Find(name);
            GameObject viewport = existing != null ? existing.gameObject :
                new GameObject(name, typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewport.transform.SetParent(parent, false);
            StretchLayoutRect(viewport.GetComponent<RectTransform>(), left, bottom, right, top);
            viewport.GetComponent<Image>().color = Color.clear;
            text.transform.SetParent(viewport.transform, false);
            LayoutElement oldHeight = text.GetComponent<LayoutElement>();
            if (oldHeight != null) UnityEngine.Object.DestroyImmediate(oldHeight);
            text.alignment = TextAnchor.UpperLeft; text.fontSize = 22;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            text.rectTransform.anchorMin = new Vector2(0, 1); text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.pivot = new Vector2(0.5f, 1); text.rectTransform.anchoredPosition = Vector2.zero;
            text.rectTransform.sizeDelta = Vector2.zero;
            ContentSizeFitter fit = text.GetComponent<ContentSizeFitter>() ?? text.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = viewport.GetComponent<ScrollRect>(); scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = text.rectTransform; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28;
        }

        [MenuItem("天章/场景/接入关中行动预览")]
        public static void RebuildCombatActionPreview()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject root = Array.Find(scene.GetRootGameObjects(), value => value.name == "AdventureRoot");
            Canvas canvas = Array.Find(scene.GetRootGameObjects(), value => value.name == "UICanvas")?.GetComponent<Canvas>();
            if (root == null || canvas == null) throw new InvalidOperationException("Adventure preview owners are missing.");
            BuildCombatActionPreview(root, canvas);
            BuildCombatLayout(root, canvas);
            SceneBuildSupport.Save(SceneBuildSupport.AdventureScenePath);
        }

        private static void BuildCombatActionPreview(GameObject root, Canvas canvas)
        {
            Transform old = canvas.transform.Find("CombatActionPreviewRoot");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            CombatActionPreviewView view = root.GetComponent<CombatActionPreviewView>() ?? root.AddComponent<CombatActionPreviewView>();
            GameObject panel = SceneBuildSupport.CreatePanel("CombatActionPreviewRoot", canvas.transform, Vector2.zero, Vector2.one);
            SettlementSceneBuilder.PlacePanel(panel, new Vector2(544, 48), new Vector2(1396, 278));
            SettlementSceneBuilder.ApplyPanelSkin(panel);
            SettlementSceneBuilder.ConfigureColumn(panel);
            Text summary = SceneBuildSupport.CreateText("ActionPreviewSummary", panel.transform, string.Empty, 20);
            SettlementSceneBuilder.ApplyTextSkin(summary, 120);
            summary.raycastTarget = false;
            var row = new GameObject("PreviewActions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(panel.transform, false);
            SettlementSceneBuilder.SetHeight(row, 44);
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            Button cancel = SceneBuildSupport.CreateButton("CancelActionButton", row.transform, "取消", out _);
            Button confirm = SceneBuildSupport.CreateButton("ConfirmActionButton", row.transform, "确认施行", out _);
            SettlementSceneBuilder.ApplyButtonSkin(cancel);
            SettlementSceneBuilder.ApplyButtonSkin(confirm);
            SceneBuildSupport.SetObject(view, "root", panel);
            SceneBuildSupport.SetObject(view, "summary", summary);
            SceneBuildSupport.SetObject(view, "confirmButton", confirm);
            SceneBuildSupport.SetObject(view, "cancelButton", cancel);
            BuildSwapSelectors(row.transform, view);
            SceneBuildSupport.SetObject(root.GetComponent<AdventureSceneInstaller>(), "combatActionPreviewView", view);
            Transform rail = canvas.transform.Find("CombatTimelineRoot");
            if (rail == null) throw new InvalidOperationException("Preview requires the formal timeline.");
            Transform oldPrediction = rail.Find("ConditionalNextMarker");
            if (oldPrediction != null) UnityEngine.Object.DestroyImmediate(oldPrediction.gameObject);
            Text prediction = SceneBuildSupport.CreateText("ConditionalNextMarker", rail, string.Empty, 15);
            prediction.font = SettlementSceneBuilder.UiFont;
            prediction.raycastTarget = false;
            prediction.alignment = TextAnchor.UpperCenter;
            prediction.color = new Color32(239, 206, 123, 255);
            PlaceTimelineRect(prediction.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -204), new Vector2(76, 116));
            SceneBuildSupport.SetObject(root.GetComponent<CombatTimelineView>(), "predictionRoot", prediction.gameObject);
            SceneBuildSupport.SetObject(root.GetComponent<CombatTimelineView>(), "predictionText", prediction);
            prediction.gameObject.SetActive(false);
            panel.SetActive(false);
        }

        [MenuItem("天章/场景/接入关中战斗换术")]
        public static void RebuildCombatSwapSpell()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject root = Array.Find(scene.GetRootGameObjects(), value => value.name == "AdventureRoot");
            Canvas canvas = Array.Find(scene.GetRootGameObjects(), value => value.name == "UICanvas")?.GetComponent<Canvas>();
            if (root == null || canvas == null) throw new InvalidOperationException("Adventure swap owners are missing.");
            CombatActionBarView bar = root.GetComponent<CombatActionBarView>();
            Transform actions = ((GameObject)new SerializedObject(bar).FindProperty("root").objectReferenceValue).transform;
            Button swap = LayoutButton(actions, "SwapSpellButton", "换术");
            SceneBuildSupport.SetObject(bar, "swapSpellButton", swap);
            PlaceLayoutRect(swap.GetComponent<RectTransform>(), 252, 8, 114, 68);
            PlaceLayoutRect(LayoutObject(actions, "ActionStatusText"), 378, 8, 444, 76);
            BuildSwapSelectors(LayoutObject(canvas.transform, "CombatActionPreviewRoot/PreviewActions"),
                root.GetComponent<CombatActionPreviewView>());
            SceneBuildSupport.Save(SceneBuildSupport.AdventureScenePath);
        }

        private static void BuildSwapSelectors(Transform row, CombatActionPreviewView view)
        {
            Button slot = LayoutButton(row, "SwapSlotButton", "选择槽位");
            Button candidate = LayoutButton(row, "SwapCandidateButton", "选择术法");
            slot.transform.SetSiblingIndex(0);
            candidate.transform.SetSiblingIndex(1);
            foreach (Button button in new[] { slot, candidate })
            {
                button.GetComponentInChildren<Text>().fontSize = 18;
                button.gameObject.SetActive(false);
            }
            SceneBuildSupport.SetObject(view, "swapSlotButton", slot);
            SceneBuildSupport.SetObject(view, "swapCandidateButton", candidate);
        }

        [MenuItem("天章/场景/接入关中目标联动")]
        public static void RebuildCombatTargetSelection()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject root = Array.Find(scene.GetRootGameObjects(), value => value.name == "AdventureRoot");
            GameObject canvas = Array.Find(scene.GetRootGameObjects(), value => value.name == "UICanvas");
            if (root == null || canvas == null) throw new InvalidOperationException("Adventure target owners are missing.");
            BuildCombatTargetSelection(root, canvas.GetComponent<Canvas>());
            BuildCombatLayout(root, canvas.GetComponent<Canvas>());
            SceneBuildSupport.Save(SceneBuildSupport.AdventureScenePath);
        }

        private static void BuildCombatTargetSelection(GameObject root, Canvas canvas)
        {
            foreach (string name in new[] { "CombatTargetRoot", "CombatTargetMarker" })
            {
                Transform old = canvas.transform.Find(name);
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            CombatTargetView view = root.GetComponent<CombatTargetView>() ?? root.AddComponent<CombatTargetView>();
            CombatTargetInput input = root.GetComponent<CombatTargetInput>() ?? root.AddComponent<CombatTargetInput>();
            GameObject panel = SceneBuildSupport.CreatePanel("CombatTargetRoot", canvas.transform, Vector2.zero, Vector2.one);
            SettlementSceneBuilder.PlacePanel(panel, new Vector2(152, 296), new Vector2(512, 640));
            SettlementSceneBuilder.ApplyPanelSkin(panel);
            SettlementSceneBuilder.ConfigureColumn(panel);
            Text details = SceneBuildSupport.CreateText("CombatTargetDetails", panel.transform, string.Empty, 20);
            SettlementSceneBuilder.ApplyTextSkin(details, 224);
            details.raycastTarget = false;
            Button close = SceneBuildSupport.CreateButton("CloseTargetButton", panel.transform, "关闭查看", out _);
            SettlementSceneBuilder.ApplyButtonSkin(close);
            var marker = new GameObject("CombatTargetMarker", typeof(RectTransform));
            marker.transform.SetParent(canvas.transform, false);
            var markerRect = marker.GetComponent<RectTransform>();
            markerRect.anchorMin = markerRect.anchorMax = markerRect.pivot = new Vector2(0.5f, 0.5f);
            for (int side = 0; side < 4; side++)
            {
                var edge = new GameObject("Edge" + side, typeof(RectTransform), typeof(Image));
                edge.transform.SetParent(marker.transform, false);
                Image image = edge.GetComponent<Image>();
                image.color = new Color32(239, 206, 123, 255);
                image.raycastTarget = false;
                RectTransform rect = image.rectTransform;
                bool horizontal = side < 2;
                float position = side % 2;
                rect.anchorMin = horizontal ? new Vector2(0, position) : new Vector2(position, 0);
                rect.anchorMax = horizontal ? new Vector2(1, position) : new Vector2(position, 1);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = horizontal ? new Vector2(0, 2) : new Vector2(2, 0);
            }
            SceneBuildSupport.SetObject(view, "root", panel);
            SceneBuildSupport.SetObject(view, "details", details);
            SceneBuildSupport.SetObject(view, "closeButton", close);
            SceneBuildSupport.SetObject(view, "worldMarker", markerRect);
            AdventureSceneInstaller installer = root.GetComponent<AdventureSceneInstaller>();
            SceneBuildSupport.SetObject(installer, "combatTargetView", view);
            SceneBuildSupport.SetObject(installer, "combatTargetInput", input);
            panel.SetActive(false);
            marker.SetActive(false);
        }

        [MenuItem("天章/场景/接入关中移动交互")]
        public static void RebuildCombatMovement()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject root = Array.Find(scene.GetRootGameObjects(), value => value.name == "AdventureRoot");
            GameObject battlefield = Array.Find(scene.GetRootGameObjects(), value => value.name == "GuanzhongBattlefield");
            Camera camera = Array.Find(scene.GetRootGameObjects(), value => value.name == "Main Camera")?.GetComponent<Camera>();
            BuildCombatMovement(root, camera, battlefield, SceneBuildSupport.RequireAsset<EnvironmentProfileAsset>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset"));
            BuildCombatLayout(root, Array.Find(scene.GetRootGameObjects(), value => value.name == "UICanvas").GetComponent<Canvas>());
            SceneBuildSupport.Save(SceneBuildSupport.AdventureScenePath);
        }

        private static void BuildCombatMovement(GameObject root, Camera camera, GameObject battlefield,
            EnvironmentProfileAsset environment)
        {
            if (root == null || camera == null || battlefield == null)
                throw new InvalidOperationException("Formal movement scene owners are missing.");
            CombatMovementView view = root.GetComponent<CombatMovementView>() ?? root.AddComponent<CombatMovementView>();
            CombatActionBarView bar = root.GetComponent<CombatActionBarView>();
            var barData = new SerializedObject(bar);
            GameObject actionRoot = (GameObject)barData.FindProperty("root").objectReferenceValue;
            Transform old = actionRoot.transform.Find("MovementControls");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var controls = new GameObject("MovementControls", typeof(RectTransform), typeof(VerticalLayoutGroup));
            controls.transform.SetParent(actionRoot.transform, false);
            controls.transform.SetAsFirstSibling();
            SettlementSceneBuilder.ConfigureColumn(controls, 0);
            controls.GetComponent<VerticalLayoutGroup>().spacing = 8;
            SettlementSceneBuilder.SetHeight(controls, 96);
            Text status = SceneBuildSupport.CreateText("MovementStatus", controls.transform, "移力", 18);
            SettlementSceneBuilder.ApplyTextSkin(status, 32);
            var row = new GameObject("MovementButtons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(controls.transform, false);
            row.GetComponent<HorizontalLayoutGroup>().spacing = 6;
            row.GetComponent<HorizontalLayoutGroup>().childControlWidth = true;
            row.GetComponent<HorizontalLayoutGroup>().childControlHeight = true;
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            SettlementSceneBuilder.SetHeight(row, 56);
            Button move = SceneBuildSupport.CreateButton("MoveButton", row.transform, "移动", out _);
            Button confirm = SceneBuildSupport.CreateButton("ConfirmMoveButton", row.transform, "确认", out _);
            Button cancel = SceneBuildSupport.CreateButton("CancelMoveButton", row.transform, "取消", out _);
            foreach (Button button in new[] { move, confirm, cancel }) SettlementSceneBuilder.ApplyButtonSkin(button);
            confirm.gameObject.SetActive(false);
            cancel.gameObject.SetActive(false);
            // Two starter arts: 96 movement + 344 controls + seven 4-unit gaps.
            VerticalLayoutGroup actionColumn = actionRoot.GetComponent<VerticalLayoutGroup>();
            if (actionColumn != null) actionColumn.spacing = 4;
            SettlementSceneBuilder.SetHeight(actionRoot, 468);
            SceneBuildSupport.SetObject(bar, "moveButton", move);
            SceneBuildSupport.SetObject(bar, "confirmMoveButton", confirm);
            SceneBuildSupport.SetObject(bar, "cancelMoveButton", cancel);
            SceneBuildSupport.SetObject(bar, "movementLabel", status);
            Button wait = (Button)barData.FindProperty("waitButton").objectReferenceValue;
            SceneBuildSupport.SetObject(bar, "waitLabel", wait.GetComponentInChildren<Text>(true));
            SceneBuildSupport.SetObject(root.GetComponent<AdventureSceneInstaller>(), "combatMovementView", view);
            SceneBuildSupport.SetObject(view, "battlefieldCamera", camera);
            SceneBuildSupport.SetObject(view, "reachableMaterial", SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.ReachableMaterialPath));
            SceneBuildSupport.SetObject(view, "selectedMaterial", SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.SelectedMaterialPath));
            var data = new SerializedObject(view);
            SerializedProperty cells = data.FindProperty("cells");
            cells.arraySize = environment.battlefieldCells.Length;
            for (int index = 0; index < cells.arraySize; index++)
            {
                EnvironmentBattlefieldCell cell = environment.battlefieldCells[index];
                SerializedProperty entry = cells.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("q").intValue = cell.q;
                entry.FindPropertyRelative("r").intValue = cell.r;
                entry.FindPropertyRelative("tile").objectReferenceValue = battlefield.transform.Find("GuanzhongHex_" + cell.q + "_" + cell.r);
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("天章/场景/重建关中头顶血条")]
        public static void RebuildUnitHealthBars()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject root = Array.Find(scene.GetRootGameObjects(), item => item.name == "AdventureRoot");
            Canvas canvas = Array.Find(scene.GetRootGameObjects(), item => item.name == "UICanvas")?.GetComponent<Canvas>();
            Camera camera = Array.Find(scene.GetRootGameObjects(), item => item.name == "Main Camera")?.GetComponent<Camera>();
            if (root == null || canvas == null || camera == null)
                throw new InvalidOperationException("Adventure health bars require the existing root, Canvas and Camera.");
            BuildUnitHealthBars(root, canvas, camera);
            if (!EditorSceneManager.SaveScene(scene, SceneBuildSupport.AdventureScenePath))
                throw new InvalidOperationException("Could not save the Guanzhong unit health bars.");
            AssetDatabase.SaveAssets();
        }

        private static void BuildUnitHealthBars(GameObject root, Canvas canvas, Camera camera)
        {
            Transform existing = canvas.transform.Find("CombatUnitHealthBars");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            var container = new GameObject("CombatUnitHealthBars", typeof(RectTransform));
            container.transform.SetParent(canvas.transform, false);
            container.transform.SetAsFirstSibling();
            RectTransform containerRect = container.GetComponent<RectTransform>();
            containerRect.anchorMin = Vector2.zero;
            containerRect.anchorMax = Vector2.one;
            containerRect.offsetMin = containerRect.offsetMax = Vector2.zero;
            CombatUnitHealthBarView view = root.GetComponent<CombatUnitHealthBarView>() ?? root.AddComponent<CombatUnitHealthBarView>();
            var serialized = new SerializedObject(view);
            SerializedProperty bars = serialized.FindProperty("bars");
            bars.arraySize = 2;
            for (int index = 0; index < 2; index++)
            {
                var bar = new GameObject("UnitHealthBar_" + index, typeof(RectTransform));
                bar.transform.SetParent(container.transform, false);
                RectTransform rect = bar.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = Vector2.one * 0.5f;
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(CombatUnitHealthBarView.BarWidth, CombatUnitHealthBarView.BarHeight);
                rect.anchoredPosition = Vector2.zero;
                var track = new GameObject("HealthTrack", typeof(RectTransform), typeof(Image));
                track.transform.SetParent(bar.transform, false);
                RectTransform trackRect = track.GetComponent<RectTransform>();
                trackRect.anchorMin = Vector2.zero;
                trackRect.anchorMax = Vector2.one;
                trackRect.offsetMin = trackRect.offsetMax = Vector2.zero;
                track.GetComponent<Image>().color = new Color32(35, 42, 42, 255);
                var fill = new GameObject("HealthFill", typeof(RectTransform), typeof(Image));
                fill.transform.SetParent(track.transform, false);
                RectTransform fillRect = fill.GetComponent<RectTransform>();
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = Vector2.one;
                fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
                Image fillImage = fill.GetComponent<Image>();
                fillImage.sprite = SettlementSceneBuilder.UiSprite("Guanzhong_Panel_Paper");
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Horizontal;
                fillImage.fillOrigin = 0;
                foreach (Graphic graphic in bar.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
                SerializedProperty entry = bars.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("root").objectReferenceValue = rect;
                entry.FindPropertyRelative("healthFill").objectReferenceValue = fillImage;
                bar.SetActive(false);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AdventureSceneInstaller installer = root.GetComponent<AdventureSceneInstaller>();
            SceneBuildSupport.SetObject(installer, "combatUnitHealthBars", view);
            SceneBuildSupport.SetObject(installer, "battlefieldCamera", camera);
            SceneBuildSupport.SetObject(installer, "hudCanvas", canvas);
            SceneBuildSupport.SetObject(root.GetComponent<CombatHudPresenter>(), "healthBars", view);
        }

        [MenuItem("天章/场景/接入关中真实行动轴")]
        public static void RebuildCombatTimeline()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject root = Array.Find(scene.GetRootGameObjects(), candidate => candidate.name == "AdventureRoot");
            GameObject canvasRoot = Array.Find(scene.GetRootGameObjects(), candidate => candidate.name == "UICanvas");
            if (root == null || canvasRoot == null) throw new InvalidOperationException("Adventure HUD owners are missing.");
            BuildCombatTimeline(root, canvasRoot.GetComponent<Canvas>(), root.GetComponent<AdventureSceneInstaller>(),
                root.GetComponent<CombatHudPresenter>());
            BuildCombatActionPreview(root, canvasRoot.GetComponent<Canvas>());
            BuildCombatLayout(root, canvasRoot.GetComponent<Canvas>());
            SceneBuildSupport.Save(SceneBuildSupport.AdventureScenePath);
        }

        private static void BuildCombatTimeline(GameObject root, Canvas canvas, AdventureSceneInstaller installer,
            CombatHudPresenter presenter)
        {
            Transform old = canvas.transform.Find("CombatTimelineRoot");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            CombatTimelineView view = root.GetComponent<CombatTimelineView>() ?? root.AddComponent<CombatTimelineView>();
            GameObject rail = SceneBuildSupport.CreatePanel("CombatTimelineRoot", canvas.transform, Vector2.zero, Vector2.one);
            SettlementSceneBuilder.PlacePanel(rail, new Vector2(52, 196), new Vector2(128, 801));
            SettlementSceneBuilder.ApplyPanelSkin(rail);
            rail.GetComponent<Image>().raycastTarget = false;
            Text tick = SceneBuildSupport.CreateText("TimelineTick", rail.transform, string.Empty, 16);
            tick.font = SettlementSceneBuilder.UiFont;
            tick.raycastTarget = false;
            PlaceTimelineRect(tick.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(76, 28));
            var entries = new RectTransform[2];
            var buttons = new Button[2];
            var labels = new Text[2];
            var portraits = new Image[2];
            for (int index = 0; index < 2; index++)
            {
                buttons[index] = SceneBuildSupport.CreateButton("TimelineEntry" + index, rail.transform, string.Empty, out labels[index]);
                SettlementSceneBuilder.ApplyButtonSkin(buttons[index]);
                labels[index].fontSize = 14;
                labels[index].rectTransform.offsetMin = new Vector2(1, 1);
                labels[index].rectTransform.offsetMax = new Vector2(-1, -1);
                labels[index].alignment = TextAnchor.LowerCenter;
                entries[index] = buttons[index].GetComponent<RectTransform>();
                PlaceTimelineRect(entries[index], new Vector2(0.5f, 1), new Vector2(0, -34 - index * 71), new Vector2(66, 66));
                var portrait = new GameObject("Identity", typeof(RectTransform), typeof(Image));
                portrait.transform.SetParent(entries[index], false);
                portrait.transform.SetAsFirstSibling();
                portraits[index] = portrait.GetComponent<Image>();
                portraits[index].preserveAspect = true;
                portraits[index].raycastTarget = false;
                PlaceTimelineRect(portrait.GetComponent<RectTransform>(), new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(30, 30));
            }
            GameObject detail = SceneBuildSupport.CreatePanel("TimelineFocus", rail.transform, Vector2.zero, Vector2.one);
            SettlementSceneBuilder.ApplyPanelSkin(detail);
            detail.GetComponent<Image>().raycastTarget = false;
            PlaceTimelineRect(detail.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(90, 0), new Vector2(280, 136));
            Text focus = SceneBuildSupport.CreateText("TimelineFocusText", detail.transform, string.Empty, 18);
            focus.font = SettlementSceneBuilder.UiFont;
            focus.alignment = TextAnchor.MiddleLeft;
            focus.raycastTarget = false;
            focus.rectTransform.anchorMin = Vector2.zero;
            focus.rectTransform.anchorMax = Vector2.one;
            focus.rectTransform.offsetMin = new Vector2(12, 8);
            focus.rectTransform.offsetMax = new Vector2(-12, -8);
            SceneBuildSupport.SetObject(view, "root", rail);
            SceneBuildSupport.SetObject(view, "tickText", tick);
            SceneBuildSupport.SetObject(view, "focusText", focus);
            SceneBuildSupport.SetObjects(view, "entryRoots", entries);
            SceneBuildSupport.SetObjects(view, "entryButtons", buttons);
            SceneBuildSupport.SetObjects(view, "entryLabels", labels);
            SceneBuildSupport.SetObjects(view, "entryPortraits", portraits);
            SceneBuildSupport.SetObject(view, "playerPortrait", SceneBuildSupport.RequireAsset<Sprite>("Assets/Art/UI/Guanzhong/FormalPlayer_Default_Portrait.png"));
            GameObject exploration = canvas.transform.Find("AdventurePanel").gameObject;
            SceneBuildSupport.SetObject(installer, "combatTimelineView", view);
            SceneBuildSupport.SetObject(installer, "explorationPanel", exploration);
            SceneBuildSupport.SetObject(presenter, "timelineView", view);
            SceneBuildSupport.SetObject(presenter, "explorationPanel", exploration);
            rail.SetActive(false);
        }

        private static void PlaceTimelineRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(anchor.x, 1);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        [MenuItem("天章/场景/接入关中初始两术法")]
        public static void RebuildStarterArts()
        {
            CombatContentImporter.ImportSpells();
            CombatContentImporter.ImportAttackProfiles();
            WorldContentImporter.ImportEnvironmentProfiles();
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject root = Array.Find(scene.GetRootGameObjects(), candidate => candidate.name == "AdventureRoot");
            if (root == null) throw new InvalidOperationException("AdventureScene is missing AdventureRoot.");
            CombatActionBarView actionBar = root.GetComponent<CombatActionBarView>();
            GameObject canvas = Array.Find(scene.GetRootGameObjects(), candidate => candidate.name == "UICanvas");
            Transform panel = canvas == null ? null : canvas.transform.Find("CombatHudRoot");
            if (actionBar == null || panel == null)
                throw new InvalidOperationException("AdventureScene is missing its combat HUD owner.");
            var barData = new SerializedObject(actionBar);
            Transform existing = ((GameObject)barData.FindProperty("root").objectReferenceValue).transform;
            Transform barParent = existing.parent;
            int siblingIndex = existing.GetSiblingIndex();
            UnityEngine.Object.DestroyImmediate(existing.gameObject);
            BuildStarterActionBar(barParent, actionBar).transform.SetSiblingIndex(siblingIndex);
            GameObject battlefield = Array.Find(scene.GetRootGameObjects(), candidate => candidate.name == "GuanzhongBattlefield");
            EnvironmentProfileAsset environment = SceneBuildSupport.RequireAsset<EnvironmentProfileAsset>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            BuildCombatMovement(root, Camera.main, battlefield, environment);
            SceneBuildSupport.SetObjects(root.GetComponent<AdventureSceneInstaller>(), "attackProfiles", LoadAttackProfiles());
            BuildCombatLayout(root, canvas.GetComponent<Canvas>());
            if (!EditorSceneManager.SaveScene(scene, SceneBuildSupport.AdventureScenePath))
                throw new InvalidOperationException("Could not save the starter arts HUD.");
            AssetDatabase.SaveAssets();
        }

        private static GameObject BuildStarterActionBar(Transform parent, CombatActionBarView actionBar)
        {
            var actionRoot = new GameObject("CombatActionBar", typeof(RectTransform), typeof(VerticalLayoutGroup));
            actionRoot.transform.SetParent(parent, false);
            SettlementSceneBuilder.ConfigureColumn(actionRoot, 0);
            actionRoot.GetComponent<VerticalLayoutGroup>().spacing = 8;
            SettlementSceneBuilder.SetHeight(actionRoot, 440);
            Button basic = SceneBuildSupport.CreateButton("BasicAttackButton", actionRoot.transform, "普攻", out _);
            Button art = SceneBuildSupport.CreateButton("ArtButton", actionRoot.transform, "术法", out Text artLabel);
            Button secondArt = SceneBuildSupport.CreateButton("SecondArtButton", actionRoot.transform, "术法", out Text secondLabel);
            Button target = SceneBuildSupport.CreateButton("SecondArtTargetButton", actionRoot.transform, "目标：自身", out Text targetLabel);
            Button divine = SceneBuildSupport.CreateButton("DivineButton", actionRoot.transform, "神通", out Text divineLabel);
            Button guard = SceneBuildSupport.CreateButton("GuardButton", actionRoot.transform, "防御", out _);
            Button wait = SceneBuildSupport.CreateButton("WaitButton", actionRoot.transform, "待机", out Text waitLabel);
            Button swap = SceneBuildSupport.CreateButton("SwapSpellButton", actionRoot.transform, "换术", out _);
            foreach (Button button in new[] { basic, art, secondArt, target, divine, guard, wait, swap })
            {
                SettlementSceneBuilder.ApplyButtonSkin(button);
                SettlementSceneBuilder.SetHeight(button.gameObject, 44);
            }
            foreach (Button button in new[] { art, secondArt })
            {
                SettlementSceneBuilder.SetHeight(button.gameObject, 64);
                button.GetComponentInChildren<Text>().fontSize = 18;
            }
            SettlementSceneBuilder.SetHeight(target.gameObject, 36);
            targetLabel.fontSize = 16;
            Text status = SceneBuildSupport.CreateText("ActionStatusText", actionRoot.transform, string.Empty, 16);
            SettlementSceneBuilder.ApplyTextSkin(status, 48);
            SceneBuildSupport.SetObject(actionBar, "root", actionRoot);
            SceneBuildSupport.SetObject(actionBar, "basicAttackButton", basic);
            SceneBuildSupport.SetObject(actionBar, "artButton", art);
            SceneBuildSupport.SetObject(actionBar, "artLabel", artLabel);
            SceneBuildSupport.SetObject(actionBar, "secondArtButton", secondArt);
            SceneBuildSupport.SetObject(actionBar, "secondArtLabel", secondLabel);
            SceneBuildSupport.SetObject(actionBar, "secondArtTargetButton", target);
            SceneBuildSupport.SetObject(actionBar, "secondArtTargetLabel", targetLabel);
            SceneBuildSupport.SetObject(actionBar, "divineButton", divine);
            SceneBuildSupport.SetObject(actionBar, "divineLabel", divineLabel);
            SceneBuildSupport.SetObject(actionBar, "guardButton", guard);
            SceneBuildSupport.SetObject(actionBar, "waitButton", wait);
            SceneBuildSupport.SetObject(actionBar, "swapSpellButton", swap);
            SceneBuildSupport.SetObject(actionBar, "waitLabel", waitLabel);
            SceneBuildSupport.SetObject(actionBar, "actionStatusLabel", status);
            return actionRoot;
        }

        [MenuItem("天章/场景/重建关中战场")]
        public static void RebuildGuanzhongBattlefield()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            EnvironmentProfileAsset environmentProfile = SceneBuildSupport.RequireAsset<EnvironmentProfileAsset>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            GameObject existing = Array.Find(scene.GetRootGameObjects(), root => root.name == "GuanzhongBattlefield");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
            GameObject battlefield = BuildGuanzhongBattlefield(environmentProfile);
            ConfigureAdventureCamera(Camera.main, battlefield);
            GameObject adventureRoot = Array.Find(scene.GetRootGameObjects(), root => root.name == "AdventureRoot");
            BuildCombatMovement(adventureRoot, Camera.main, battlefield, environmentProfile);
            BuildCombatLayout(adventureRoot, Array.Find(scene.GetRootGameObjects(), value => value.name == "UICanvas").GetComponent<Canvas>());
            if (!EditorSceneManager.SaveScene(scene, SceneBuildSupport.AdventureScenePath))
                throw new InvalidOperationException("Could not save the rebuilt Guanzhong battlefield.");
            AssetDatabase.SaveAssets();
        }

        [MenuItem("天章/场景/更新关中战术相机")]
        public static void RebuildTacticalCamera()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject battlefield = Array.Find(scene.GetRootGameObjects(), root => root.name == "GuanzhongBattlefield");
            if (battlefield == null) throw new InvalidOperationException("AdventureScene requires its battlefield.");
            ConfigureAdventureCamera(Camera.main, battlefield);
            if (!EditorSceneManager.SaveScene(scene, SceneBuildSupport.AdventureScenePath))
                throw new InvalidOperationException("Could not save the Adventure camera.");
        }

        private static void ConfigureAdventureCamera(Camera camera, GameObject battlefield)
        {
            if (camera == null) throw new InvalidOperationException("AdventureScene requires its main camera.");
            camera.orthographicSize = 4f;
            AdventureCameraController controller = camera.GetComponent<AdventureCameraController>();
            if (controller == null) controller = camera.gameObject.AddComponent<AdventureCameraController>();
            controller.Configure(camera, battlefield.transform, battlefield.transform.GetChild(0).position.y);
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(controller);
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
            GameObject panel = BuildBattleVisualComparisonPanel(canvas, board);
            board.SetActive(false);
            panel.SetActive(false);

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

        private static GameObject BuildBattleVisualComparisonPanel(Canvas canvas, GameObject board)
        {
            GameObject panel = SceneBuildSupport.CreatePanel("BattleVisualComparisonPanel", canvas.transform,
                new Vector2(0.02f, 0.03f), new Vector2(0.38f, 0.48f));
            SceneBuildSupport.AddVerticalLayout(panel, 6);
            SceneBuildSupport.CreateText("BattleVisualComparisonTitle", panel.transform, "战场角色表现比较", 22);
            Text status = SceneBuildSupport.CreateText("BattleVisualComparisonStatus", panel.transform, string.Empty, 13);

            Transform routeButtons = CreateComparisonGrid("ComparisonRouteButtons", panel.transform, 2, 1);
            Button dynamic2DRoute = SceneBuildSupport.CreateButton("Comparison2DRouteButton", routeButtons, "2D 动态", out _);
            Button static3DRoute = SceneBuildSupport.CreateButton("ComparisonStatic3DRouteButton", routeButtons, "静态 3D", out _);

            Transform motionButtons = CreateComparisonGrid("Comparison2DMotionButtons", panel.transform, 2, 1);
            Button pureFrames = SceneBuildSupport.CreateButton(
                "Comparison2DPureFramesButton", motionButtons, "纯帧动画", out _);
            Button overallMotion = SceneBuildSupport.CreateButton(
                "Comparison2DOverallMotionButton", motionButtons, "整体动效", out _);

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
            UnityEventTools.AddPersistentListener(pureFrames.onClick, comparison.SelectPureFrame2DMode);
            UnityEventTools.AddPersistentListener(overallMotion.onClick, comparison.SelectOverall2DMotionMode);
            for (int direction = 0; direction < directions.Length; direction++)
                UnityEventTools.AddIntPersistentListener(directions[direction].onClick, comparison.SelectDirection, direction);
            for (int presentationEvent = 0; presentationEvent < presentationEvents.Length; presentationEvent++)
                UnityEventTools.AddIntPersistentListener(presentationEvents[presentationEvent].onClick,
                    comparison.TriggerPresentationByIndex, presentationEvent);
            UnityEventTools.AddPersistentListener(reset.onClick, comparison.ResetPresentations);
            return panel;
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

        private static GameObject BuildGuanzhongBattlefield(EnvironmentProfileAsset environmentProfile)
        {
            if (environmentProfile == null || environmentProfile.battlefieldCells == null)
                throw new InvalidOperationException("Guanzhong battlefield requires a configured environment profile.");

            var cells = new List<Vector2Int>();
            foreach (EnvironmentBattlefieldCell cell in environmentProfile.battlefieldCells)
                AddGuanzhongBattlefieldCell(cells, cell.q, cell.r);
            if (cells.Count == 0)
                throw new InvalidOperationException("Guanzhong battlefield requires environment edge endpoints.");

            cells.Sort((first, second) => first.x != second.x
                ? first.x.CompareTo(second.x)
                : first.y.CompareTo(second.y));

            var battlefield = new GameObject("GuanzhongBattlefield");
            GameObject tilePrefab = SceneBuildSupport.RequireAsset<GameObject>(GuanzhongTerrainAssetBuilder.TilePrefabPath);
            GameObject stairPrefab = SceneBuildSupport.RequireAsset<GameObject>(GuanzhongTerrainAssetBuilder.StairPrefabPath);
            foreach (Vector2Int coord in cells)
            {
                var cell = (GameObject)PrefabUtility.InstantiatePrefab(tilePrefab);
                cell.name = "GuanzhongHex_" + coord.x + "_" + coord.y;
                cell.transform.SetParent(battlefield.transform, false);
                cell.transform.localPosition = HexToWorld(coord.x, coord.y, HeightForLevel(0));
                PrefabUtility.RecordPrefabInstancePropertyModifications(cell.transform);

                EnvironmentBattlefieldCell definition = Array.Find(
                    environmentProfile.battlefieldCells,
                    item => item.q == coord.x && item.r == coord.y);
                if (definition.blocksGroundMove)
                {
                    var obstacle = (GameObject)PrefabUtility.InstantiatePrefab(stairPrefab);
                    obstacle.name = "GuanzhongObstacle_" + coord.x + "_" + coord.y;
                    obstacle.transform.SetParent(cell.transform, false);
                }
            }
            return battlefield;
        }

        private static void AddGuanzhongBattlefieldCell(List<Vector2Int> cells, int q, int r)
        {
            var coord = new Vector2Int(q, r);
            if (!cells.Contains(coord)) cells.Add(coord);
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
