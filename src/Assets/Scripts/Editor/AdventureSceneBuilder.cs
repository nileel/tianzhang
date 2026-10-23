using System;
using System.Collections.Generic;
using System.Linq;
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
        [MenuItem("天章/场景/接入关中战斗撤离")]
        public static void RebuildWithdrawalEntry()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject root = Array.Find(scene.GetRootGameObjects(), value => value.name == "AdventureRoot");
            Canvas canvas = Array.Find(scene.GetRootGameObjects(), value => value.name == "UICanvas")?.GetComponent<Canvas>();
            if (root == null || canvas == null) throw new InvalidOperationException("Adventure withdrawal owners are missing.");
            BuildWithdrawalEntry(root, canvas);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, SceneBuildSupport.AdventureScenePath))
                throw new InvalidOperationException("Adventure withdrawal scene save failed.");
        }

        private static void BuildWithdrawalEntry(GameObject root, Canvas canvas)
        {
            AdventureHudPresenter hud = root.GetComponent<AdventureHudPresenter>();
            // Independent canvas children: CombatHudPresenter only hides AdventurePanel.
            GameObject entry = LayoutPanel(canvas.transform, "AdventureReturnEntry");
            RectTransform entryRect = entry.GetComponent<RectTransform>();
            RectTransform explorationRect = LayoutObject(canvas.transform, "AdventurePanel");
            SceneBuildSupport.SetObject(hud, "returnEntry", entry);
            SceneBuildSupport.SetObject(hud, "explorationPanel", explorationRect);
            hud.ApplyReturnEntryLayout(false);
            Button button = LayoutButton(entry.transform, "ReturnButton", "返回关中城");
            PlaceLayoutRect(button.GetComponent<RectTransform>(), 16, 68, entryRect.rect.width - 32, 60);
            Text label = button.GetComponentInChildren<Text>();
            label.fontSize = 24;
            Text reason = LayoutText(entry.transform, "ReturnReason", "正在加载", 22);
            PlaceLayoutRect(reason.rectTransform, 16, 12, entryRect.rect.width - 32, 48);
            reason.alignment = TextAnchor.MiddleCenter;

            GameObject confirmation = LayoutPanel(canvas.transform, "AdventureWithdrawalConfirmation");
            RectTransform blocker = confirmation.GetComponent<RectTransform>();
            blocker.anchorMin = Vector2.zero; blocker.anchorMax = Vector2.one;
            blocker.offsetMin = blocker.offsetMax = Vector2.zero;
            confirmation.GetComponent<Image>().sprite = null;
            confirmation.GetComponent<Image>().color = new Color(0, 0, 0, .65f);
            confirmation.GetComponent<Image>().raycastTarget = true;
            confirmation.transform.SetAsLastSibling();
            GameObject dialog = LayoutPanel(confirmation.transform, "WithdrawalDialog");
            RectTransform dialogRect = dialog.GetComponent<RectTransform>();
            dialogRect.anchorMin = dialogRect.anchorMax = dialogRect.pivot = new Vector2(.5f, .5f);
            dialogRect.anchoredPosition = Vector2.zero;
            dialogRect.sizeDelta = new Vector2(760, 400);
            Text title = LayoutText(dialog.transform, "WithdrawalTitle", "撤离战斗？", 30);
            PlaceLayoutRect(title.rectTransform, 32, 316, 696, 52);
            Text message = LayoutText(dialog.transform, "WithdrawalMessage", AdventureHudPresenter.WithdrawalMessage, 26);
            PlaceLayoutRect(message.rectTransform, 32, 108, 696, 196);
            message.alignment = TextAnchor.MiddleLeft;
            Button confirm = LayoutButton(dialog.transform, "ConfirmWithdrawalButton", "确认撤离");
            Button cancel = LayoutButton(dialog.transform, "ContinueCombatButton", "继续战斗");
            PlaceLayoutRect(confirm.GetComponent<RectTransform>(), 32, 32, 332, 60);
            PlaceLayoutRect(cancel.GetComponent<RectTransform>(), 396, 32, 332, 60);
            foreach (Button action in new[] { confirm, cancel }) action.GetComponentInChildren<Text>().fontSize = 24;
            confirm.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = cancel, selectOnRight = cancel };
            cancel.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = confirm, selectOnRight = confirm };
            SceneBuildSupport.SetObject(hud, "returnButton", button);
            SceneBuildSupport.SetObject(hud, "returnLabel", label);
            SceneBuildSupport.SetObject(hud, "returnReason", reason);
            SceneBuildSupport.SetObject(hud, "withdrawalConfirmation", confirmation);
            SceneBuildSupport.SetObject(hud, "confirmWithdrawalButton", confirm);
            SceneBuildSupport.SetObject(hud, "continueCombatButton", cancel);
            CombatCompactUiAssets.Button(button);
            entry.GetComponent<Image>().color = Color.clear;
            entry.GetComponent<Image>().raycastTarget = false;
            hud.ApplyReturnEntryLayout(false);
            entry.SetActive(false);
            confirmation.SetActive(false);
        }

        /// <summary>Refreshes only the frozen body-attack content and the formal scene's profile references.</summary>
        public static void RebuildUnarmedBindings()
        {
            CombatContentImporter.ImportAttackProfiles();
            CharacterContentImporter.ImportCharacterDefinitions();
            SettlementContentImporter.ImportContentCatalog();
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity", OpenSceneMode.Single);
            AdventureSceneInstaller installer = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out AdventureSceneInstaller found)) installer = found;
            if (installer == null) throw new InvalidOperationException("Adventure installer missing.");
            SceneBuildSupport.SetObjects(installer, "attackProfiles", LoadAttackProfiles());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

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
            definitions.arraySize = 4;
            for (int index = 0; index < 4; index++)
            {
                SerializedProperty definition = definitions.GetArrayElementAtIndex(index);
                definition.FindPropertyRelative("Kind").intValue = (int)(index == 0 ? CombatCommandKind.Guard : index == 1 ? CombatCommandKind.SwapSpell : index == 2 ? CombatCommandKind.SwapEquipment : CombatCommandKind.UseItem);
                definition.FindPropertyRelative("ActionPointCostUnits").intValue = index == 3 ? 30000 : index == 2 ? 25000 : 50000;
                definition.FindPropertyRelative("MinimumNetCostUnits").intValue = index == 3 ? 30000 : index == 2 ? 25000 : 50000;
                definition.FindPropertyRelative("CtPenalty").intValue = 0;
                definition.FindPropertyRelative("ReuseCooldownTicks").intValue = index == 1 ? 60 : 0;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void UpdateEquipmentContent()
        {
            CombatContentImporter.ImportAttackProfiles();
            SettlementContentImporter.ImportContentCatalog();
            UpdateOrdinaryCombatActions();
        }

        private static void ConfigureEquipmentButton(GameObject root)
        {
            var bar = root.GetComponent<CombatActionBarView>();
            Transform actions = ((GameObject)new SerializedObject(bar).FindProperty("root").objectReferenceValue).transform;
            Button button = LayoutButton(actions, "SwapEquipmentButton", "换装备");
            SceneBuildSupport.SetObject(bar, "swapEquipmentButton", button);
            PlaceLayoutRect(button.GetComponent<RectTransform>(), 378, 8, 114, 68);
            Button item = LayoutButton(actions, "UseItemButton", "使用道具");
            SceneBuildSupport.SetObject(bar, "useItemButton", item);
            Text itemLabel = item.GetComponentInChildren<Text>();
            itemLabel.rectTransform.offsetMin = new Vector2(12, 4);
            itemLabel.rectTransform.offsetMax = new Vector2(-12, -4);
            RectTransform equipment = button.GetComponent<RectTransform>();
            float itemX = equipment.anchoredPosition.x + equipment.rect.width + 12;
            PlaceLayoutRect(item.GetComponent<RectTransform>(), itemX, equipment.anchoredPosition.y, equipment.rect.width, equipment.rect.height);
            float statusX = itemX + equipment.rect.width + 12;
            PlaceLayoutRect(LayoutObject(actions.parent, "ActionStatusText"), statusX, 8, 822 - statusX, 76);
        }

        public static void UpdateOrdinaryCombatActions()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            AdventureSceneInstaller installer = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out AdventureSceneInstaller found)) installer = found;
            if (installer == null) throw new InvalidOperationException("adventure_installer_missing");
            ConfigureOrdinaryCombatActions(installer);
            ConfigureEquipmentButton(installer.gameObject);
            SceneBuildSupport.SetObjects(installer, "attackProfiles", LoadAttackProfiles());
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, SceneBuildSupport.AdventureScenePath))
                throw new InvalidOperationException("AdventureScene save failed.");
        }

        [MenuItem("天章/场景/重建冒险")]
        public static void Build()
        {
            CombatCompactUiAssets.Import();
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
            GuanzhongEnvironmentBuilder.Build();

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
            SceneBuildSupport.SetObject(
                static3DProvider,
                "attackVfxCatalog",
                SceneBuildSupport.RequireAsset<CombatAttackVfxCatalogData>(
                    CombatAttackVfxBuilder.CatalogPath));
            SceneBuildSupport.SetObject(static3DProvider, "targetPreviewMaterial",
                SceneBuildSupport.RequireAsset<Material>("Assets/Art/VFX/CombatTargetPreview.mat"));
            BuildUnitHealthBars(root, canvas, GameObject.Find("Main Camera").GetComponent<Camera>());
            BuildCombatTimeline(root, canvas, installer, combatPresenter);
            BuildCombatMovement(root, Camera.main, battlefield, environmentProfile);
            BuildCombatTargetSelection(root, canvas);
            BuildCombatActionPreview(root, canvas);
            BuildCombatLayout(root, canvas);
            BuildWithdrawalEntry(root, canvas);
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
            // All rectangles use V4's 1920x1080 design space; only this canvas uses uniform Fit.
            canvas.GetComponent<CanvasScaler>().screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            GameObject hotbar = LayoutPanel(hud, "CombatHotbar");
            CompactRect(hotbar, 160, 892, 1728, 164);
            CombatCompactUiAssets.Panel(hotbar);
            GameObject resources = LayoutPanel(hud, "CombatResourceCard");
            CompactRect(resources, 32, 24, 416, 188);
            CombatCompactUiAssets.Panel(resources);
            foreach (string name in new[] { "CombatPlayerPortrait", "CombatPlayerText", "CombatEnemyText" })
                LayoutObject(hud, name).SetParent(resources.transform, false);
            PlaceLayoutRect(LayoutObject(resources.transform, "CombatPlayerPortrait"), 20, 48, 104, 120);
            MakeLayoutScroll(resources.transform, "PlayerSummaryBody", LayoutObject(resources.transform, "CombatPlayerText").GetComponent<Text>(), 144, 132, 20, 16);
            CombatHudView resourceView = root.GetComponent<CombatHudView>();
            BuildResourceBar(resources.transform, resourceView, "Health", "health", "生命", 92, "fill_health");
            BuildResourceBar(resources.transform, resourceView, "Spirit", "spirit", "灵力", 52, "fill_mana");
            BuildResourceBar(resources.transform, resourceView, "ActionPoint", "actionPoint", "AP", 12, "fill_ap");
            LayoutObject(resources.transform, "CombatEnemyText").gameObject.SetActive(false);
            RectTransform turn = LayoutObject(hud, "CombatTurnText");
            turn.SetParent(hotbar.transform, false);
            PlaceLayoutRect(turn, 18, 16, 60, 64);
            turn.GetComponent<Text>().fontSize = 22;
            turn.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
            Image turnPortrait = CompactImage(hotbar.transform, "CombatTurnPortrait", null);
            turnPortrait.sprite = SceneBuildSupport.RequireAsset<Sprite>("Assets/Art/UI/Guanzhong/FormalPlayer_Default_Portrait.png");
            turnPortrait.preserveAspect = true;
            PlaceLayoutRect(turnPortrait.rectTransform, 20, 84, 56, 60);

            Text location = LayoutText(hud, "CombatLocationText", string.Empty, 34);
            PlaceLayoutRect(location.rectTransform, 740, 992, 440, 64);
            location.alignment = TextAnchor.MiddleCenter;
            SceneBuildSupport.SetObject(root.GetComponent<AdventureHudPresenter>(), "combatLocationText", location);

            RectTransform actions = LayoutObject(hud, "CombatActionBar");
            actions.SetParent(hotbar.transform, false);
            RemoveLayout(actions);
            PlaceLayoutRect(actions, 0, 0, 1728, 164);
            ConfigureEquipmentButton(root);
            var viewport = actions.Find("ActionViewport")?.gameObject ??
                new GameObject("ActionViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewport.transform.SetParent(actions, false);
            PlaceLayoutRect(viewport.GetComponent<RectTransform>(), 96, 12, 1412, 140);
            viewport.GetComponent<Image>().color = Color.clear;
            var content = viewport.transform.Find("ActionContent")?.gameObject ??
                new GameObject("ActionContent", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero; contentRect.anchorMax = Vector2.up;
            contentRect.pivot = Vector2.up; contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            HorizontalLayoutGroup row = content.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 12; row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>(); scroll.content = contentRect;
            scroll.horizontal = true; scroll.vertical = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            string[] names = { "BasicAttackButton", "ArtButton", "SecondArtButton", "ThirdArtButton", "MovementControls", "GuardButton", "SwapSpellButton", "SwapEquipmentButton", "UseItemButton", "DivineButton" };
            string[] icons = { "icon_attack", "icon_guanshiji", "icon_julingshu", "icon_feixingzhi", "icon_move", "icon_defend", "icon_switch_art", "icon_equipment", "icon_medicine", "icon_divine" };
            for (int index = 0; index < names.Length; index++)
            {
                RectTransform item = LayoutObject(actions, names[index]);
                item.SetParent(content.transform, false); item.SetAsLastSibling();
                RemoveLayout(item);
                LayoutElement size = item.GetComponent<LayoutElement>() ?? item.gameObject.AddComponent<LayoutElement>();
                size.minWidth = size.preferredWidth = 144; size.minHeight = size.preferredHeight = 140;
                size.flexibleWidth = size.flexibleHeight = 0;
                Button button;
                if (names[index] == "MovementControls")
                {
                    button = LayoutObject(item, "MoveButton").GetComponent<Button>();
                    button.transform.SetParent(item, false);
                    StretchLayoutRect(button.GetComponent<RectTransform>(), 0, 0, 0, 0);
                    LayoutObject(item, "MovementButtons").gameObject.SetActive(false);
                }
                else button = item.GetComponent<Button>();
                CombatCompactUiAssets.Button(button);
                Image icon = CompactImage(button.transform, "ActionIcon", icons[index]);
                PlaceLayoutRect(icon.rectTransform, 58, 108, 28, 28);
                Text label = button.GetComponentInChildren<Text>(true);
                PlaceLayoutRect(label.rectTransform, 8, 0, 128, 108);
                label.fontSize = 22;
                // This font's default line advance is 32px at 22pt; four dynamic lines must fit below the icon.
                label.lineSpacing = .75f;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                label.resizeTextForBestFit = false;
                Text cooldown = LayoutText(button.transform, "Cooldown", string.Empty, 22);
                PlaceLayoutRect(cooldown.rectTransform, 6, 104, 132, 36);
                cooldown.alignment = TextAnchor.MiddleCenter;
                Image shade = CompactImage(button.transform, "CooldownShade", "cooldown_shade");
                StretchLayoutRect(shade.rectTransform, 0, 0, 0, 0);
                shade.transform.SetAsFirstSibling(); shade.gameObject.SetActive(false);
            }
            RectTransform movement = LayoutObject(actions, "MovementStatus");
            movement.SetParent(LayoutObject(actions, "MoveButton"), false);
            PlaceLayoutRect(movement, 8, 4, 128, 64);
            movement.GetComponent<Text>().fontSize = 22;
            movement.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
            PlaceLayoutRect(LayoutObject(actions, "MoveButton").GetComponentInChildren<Text>().rectTransform, 8, 68, 128, 36);
            PlaceLayoutRect(LayoutObject(actions, "WaitButton"), 1540, 84, 168, 64);
            CombatCompactUiAssets.Button(LayoutObject(actions, "WaitButton").GetComponent<Button>(), true);
            MakeLayoutScroll(hotbar.transform, "ActionStatusBody", LayoutObject(hud, "ActionStatusText").GetComponent<Text>(), 96, 176, 220, -68);
            LayoutObject(hud, "ActionStatusText").gameObject.AddComponent<LayoutElement>().minHeight = 36;
            var bar = root.GetComponent<CombatActionBarView>();
            SceneBuildSupport.SetObject(bar, "attackIcon", CombatCompactUiAssets.Sprite("icon_attack"));
            SceneBuildSupport.SetObject(bar, "unarmedIcon", CombatCompactUiAssets.Sprite("icon_unarmed"));
            SceneBuildSupport.SetObject(bar, "guanshijiIcon", CombatCompactUiAssets.Sprite("icon_guanshiji"));
            SceneBuildSupport.SetObject(bar, "julingshuIcon", CombatCompactUiAssets.Sprite("icon_julingshu"));
            SceneBuildSupport.SetObject(bar, "feixingzhiIcon", CombatCompactUiAssets.Sprite("icon_feixingzhi"));

            RectTransform target = LayoutObject(canvas.transform, "CombatTargetRoot");
            RemoveLayout(target);
            // Structured details: portrait, identity, two resource bars and an unknown-facts body.
            CompactRect(target.gameObject, 1504, 232, 384, 368);
            CombatCompactUiAssets.Panel(target.gameObject);
            Text targetTitle = LayoutText(target, "TargetHeading", "目标详情", 24);
            PlaceLayoutRect(targetTitle.rectTransform, 20, 320, 210, 36);
            PlaceLayoutRect(LayoutObject(target, "CloseTargetButton"), 288, 320, 80, 34);
            LayoutObject(target, "CloseTargetButton").GetComponentInChildren<Text>(true).text = "关闭";
            PlaceLayoutRect(LayoutObject(target, "TargetPortrait"), 20, 216, 96, 96);
            Text targetName = LayoutText(target, "TargetName", string.Empty, 26);
            PlaceLayoutRect(targetName.rectTransform, 128, 276, 236, 40);
            Text targetFaction = LayoutText(target, "TargetFaction", string.Empty, 20);
            targetFaction.color = new Color32(194, 208, 203, 255);
            PlaceLayoutRect(targetFaction.rectTransform, 128, 242, 236, 32);
            PlaceLayoutRect(LayoutObject(target, "TargetHealthBar"), 128, 232, 236, 6);
            StretchLayoutRect((RectTransform)LayoutObject(target, "TargetHealthBar").Find("Fill"), 0, 0, 0, 0);
            Text targetHealth = LayoutText(target, "TargetHealthLabel", string.Empty, 18);
            PlaceLayoutRect(targetHealth.rectTransform, 128, 204, 236, 28);
            PlaceLayoutRect(LayoutObject(target, "TargetSpiritBar"), 128, 192, 236, 6);
            StretchLayoutRect((RectTransform)LayoutObject(target, "TargetSpiritBar").Find("Fill"), 0, 0, 0, 0);
            Text targetSpirit = LayoutText(target, "TargetSpiritLabel", string.Empty, 18);
            PlaceLayoutRect(targetSpirit.rectTransform, 128, 164, 236, 28);
            MakeLayoutScroll(target, "TargetBody", LayoutObject(target, "CombatTargetDetails").GetComponent<Text>(), 20, 20, 20, 210);

            RectTransform preview = LayoutObject(canvas.transform, "CombatActionPreviewRoot");
            RemoveLayout(preview);
            // The interaction preview belongs to the accepted compact layout: lower right, one
            // narrow column above the hotbar, clear of the timeline, log and target panels.
            CompactRect(preview.gameObject, 1268, 612, 620, 276);
            CombatCompactUiAssets.Panel(preview.gameObject);
            MakeLayoutScroll(preview, "PreviewBody", LayoutObject(preview, "ActionPreviewSummary").GetComponent<Text>(), 12, 96, 12, 40);
            Text previewSummary = LayoutObject(preview, "ActionPreviewSummary").GetComponent<Text>();
            previewSummary.fontSize = 20;
            RectTransform previewHeading = FindLayout(preview, "ActionPreviewHeading");
            if (previewHeading != null)
            {
                PlaceLayoutRect(previewHeading, 16, 238, 280, 36);
                Text label = previewHeading.GetComponent<Text>();
                label.fontSize = 24;
                label.alignment = TextAnchor.MiddleLeft;
            }
            RectTransform previewLegend = FindLayout(preview, "PreviewLegend");
            if (previewLegend != null)
            {
                PlaceLayoutRect(previewLegend, 300, 240, 304, 34);
                Text label = previewLegend.GetComponent<Text>();
                label.fontSize = 15;
                label.alignment = TextAnchor.MiddleRight;
            }
            RectTransform previewSelectors = FindLayout(preview, "PreviewSelectors");
            if (previewSelectors != null)
            {
                PlaceLayoutRect(previewSelectors, 12, 54, 596, 38);
                ConfigurePreviewRow(previewSelectors);
            }
            RectTransform previewActions = LayoutObject(preview, "PreviewActions");
            PlaceLayoutRect(previewActions, 12, 10, 596, 38);
            ConfigurePreviewRow(previewActions);
            BuildAttackPointer(previewActions, root);

            CombatBountyTrackerView bounty = root.GetComponent<CombatBountyTrackerView>() ?? root.AddComponent<CombatBountyTrackerView>();
            GameObject bountyPanel = LayoutPanel(hud, "CombatBountyTrackerRoot");
            CompactRect(bountyPanel, 1536, 112, 352, 112);
            CombatCompactUiAssets.Panel(bountyPanel);
            Text bountyText = LayoutText(bountyPanel.transform, "BountySummary", string.Empty, 22);
            MakeLayoutScroll(bountyPanel.transform, "BountyBody", bountyText, 20, 16, 20, 16);
            SceneBuildSupport.SetObject(bounty, "root", bountyPanel);
            SceneBuildSupport.SetObject(bounty, "summary", bountyText);
            SceneBuildSupport.SetObject(root.GetComponent<AdventureSceneInstaller>(), "combatBountyTrackerView", bounty);

            CombatLogView logView = root.GetComponent<CombatLogView>();
            GameObject logPanel = LayoutPanel(hud, "CombatLogPanel");
            SettlementSceneBuilder.PlacePanel(logPanel, new Vector2(968, 495), new Vector2(1488, 845));
            CombatCompactUiAssets.Panel(logPanel);
            Text logHeading = LayoutText(logPanel.transform, "LogHeading", "战报", 24);
            PlaceLayoutRect(logHeading.rectTransform, 20, 290, 360, 44);
            RectTransform logViewport = LayoutObject(hud, "CombatLogViewport");
            logViewport.SetParent(logPanel.transform, false);
            StretchLayoutRect(logViewport, 20, 20, 20, 76);
            Text logText = LayoutObject(logViewport, "CombatLogText").GetComponent<Text>();
            logText.fontSize = 24;
            logText.color = new Color32(194, 208, 203, 255);
            Button closeLog = LayoutButton(logPanel.transform, "CloseLogButton", "关闭");
            PlaceLayoutRect(closeLog.GetComponent<RectTransform>(), 400, 290, 100, 44);
            Button toggleLog = LayoutButton(hud, "ToggleLogButton", "战报");
            CompactRect(toggleLog.gameObject, 1700, 992, 168, 48);
            foreach (Button button in new[] { toggleLog, closeLog, LayoutObject(target, "CloseTargetButton").GetComponent<Button>() })
                CombatCompactUiAssets.Button(button);
            SceneBuildSupport.SetObject(logView, "panel", logPanel);
            SceneBuildSupport.SetObject(logView, "toggleButton", toggleLog);
            SceneBuildSupport.SetObject(logView, "closeButton", closeLog);
            logPanel.SetActive(false);
            // The rail's current/selected/predicted units keep their existing runtime writer.
            LayoutObject(canvas.transform, "CombatTimelineRoot/TimelineFocus").gameObject.SetActive(false);
            hud.gameObject.SetActive(false);
        }

        private static void BuildResourceBar(Transform parent, CombatHudView view, string objectName,
            string fieldName, string caption, float y, string fillSprite)
        {
            string name = "Combat" + objectName + "Bar";
            GameObject track = parent.root.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(rect => rect.name == name)?.gameObject ??
                new GameObject(name, typeof(RectTransform), typeof(Image));
            track.transform.SetParent(parent, false);
            PlaceLayoutRect(track.GetComponent<RectTransform>(), 144, y, 252, 4);
            track.GetComponent<Image>().sprite = CombatCompactUiAssets.Sprite("bar_track");
            track.GetComponent<Image>().type = Image.Type.Sliced;
            track.GetComponent<Image>().color = Color.white;
            GameObject fill = track.transform.Find("Fill")?.gameObject ?? new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            PlaceLayoutRect(fill.GetComponent<RectTransform>(), 0, 0, 252, 4);
            Image image = fill.GetComponent<Image>();
            image.sprite = CombatCompactUiAssets.Sprite(fillSprite);
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = 0;
            image.color = Color.white;
            Text label = LayoutText(track.transform, "Value", caption + " 0/0", 24);
            PlaceLayoutRect(label.rectTransform, 0, 4, 252, 36);
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.transform.SetAsLastSibling();
            Shadow shadow = label.GetComponent<Shadow>() ?? label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color32(0, 0, 0, 220);
            shadow.effectDistance = new Vector2(1, -1);
            foreach (Graphic graphic in track.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            SceneBuildSupport.SetObject(view, fieldName + "Fill", image);
            SceneBuildSupport.SetObject(view, fieldName + "Label", label);
        }

        private static void CompactRect(GameObject target, float x, float y, float width, float height)
        {
            PlaceLayoutRect(target.GetComponent<RectTransform>(), x, 1080 - y - height, width, height);
        }

        private static Image CompactImage(Transform parent, string name, string sprite)
        {
            GameObject go = parent.Find(name)?.gameObject ?? new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            if (sprite != null) image.sprite = CombatCompactUiAssets.Sprite(sprite);
            image.color = Color.white; image.raycastTarget = false;
            return image;
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
            text.fontSize = size;
            text.color = new Color32(239, 233, 211, 255); text.raycastTarget = false;
            return text;
        }

        private static Button LayoutButton(Transform parent, string name, string value)
        {
            Button button = parent.GetComponentsInChildren<Button>(true).SingleOrDefault(candidate => candidate.name == name) ?? SceneBuildSupport.CreateButton(name, parent, value, out _);
            SettlementSceneBuilder.ApplyButtonSkin(button);
            return button;
        }

        private static RectTransform FindLayout(Transform parent, string name)
        {
            foreach (RectTransform child in parent.GetComponentsInChildren<RectTransform>(true))
                if (child.name == name) return child;
            return null;
        }

        /// <summary>One equal-width button row; a lone button (attack/move cancel) fills the row.</summary>
        private static void ConfigurePreviewRow(RectTransform row)
        {
            RemoveLayout(row);
            HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 6;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleCenter;
            foreach (Button button in row.GetComponentsInChildren<Button>(true))
            {
                LayoutElement element = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
                element.minWidth = 0; element.minHeight = element.preferredHeight = 38;
                element.flexibleWidth = 1; element.flexibleHeight = 0;
                Text label = button.GetComponentInChildren<Text>(true);
                if (label != null) label.fontSize = 20;
            }
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
            SettlementSceneBuilder.ApplyPanelSkin(panel);
            SettlementSceneBuilder.ConfigureColumn(panel);
            Text heading = LayoutText(panel.transform, "ActionPreviewHeading", string.Empty, 24);
            heading.alignment = TextAnchor.MiddleLeft;
            Text legend = LayoutText(panel.transform, "PreviewLegend", string.Empty, 16);
            legend.alignment = TextAnchor.MiddleRight;
            legend.color = new Color32(194, 208, 203, 255);
            legend.supportRichText = true;
            Text summary = SceneBuildSupport.CreateText("ActionPreviewSummary", panel.transform, string.Empty, 20);
            SettlementSceneBuilder.ApplyTextSkin(summary, 120);
            summary.raycastTarget = false;
            var selectors = new GameObject("PreviewSelectors", typeof(RectTransform));
            selectors.transform.SetParent(panel.transform, false);
            var row = new GameObject("PreviewActions", typeof(RectTransform));
            row.transform.SetParent(panel.transform, false);
            Button cancel = SceneBuildSupport.CreateButton("CancelActionButton", row.transform, "取消", out _);
            Button confirm = SceneBuildSupport.CreateButton("ConfirmActionButton", row.transform, "确认施行", out _);
            SettlementSceneBuilder.ApplyButtonSkin(cancel);
            SettlementSceneBuilder.ApplyButtonSkin(confirm);
            SceneBuildSupport.SetObject(view, "root", panel);
            SceneBuildSupport.SetObject(view, "heading", heading);
            SceneBuildSupport.SetObject(view, "legend", legend);
            SceneBuildSupport.SetObject(view, "summary", summary);
            SceneBuildSupport.SetObject(view, "confirmButton", confirm);
            SceneBuildSupport.SetObject(view, "cancelButton", cancel);
            BuildSwapSelectors(selectors.transform, view);
            SceneBuildSupport.SetObject(root.GetComponent<AdventureSceneInstaller>(), "combatActionPreviewView", view);
            Transform rail = canvas.transform.Find("CombatTimelineRoot");
            if (rail == null) throw new InvalidOperationException("Preview requires the formal timeline.");
            Transform oldPrediction = rail.Find("ConditionalNextMarker");
            if (oldPrediction != null) UnityEngine.Object.DestroyImmediate(oldPrediction.gameObject);
            Text prediction = SceneBuildSupport.CreateText("ConditionalNextMarker", rail, string.Empty, 20);
            prediction.font = SettlementSceneBuilder.UiFont;
            prediction.raycastTarget = false;
            prediction.alignment = TextAnchor.UpperCenter;
            prediction.color = new Color32(239, 206, 123, 255);
            // The compact preview carries the CT lines; the rail annotation stays under the rail
            // instead of floating over the board or behind the old wide popup.
            PlaceTimelineRect(prediction.rectTransform, Vector2.zero, new Vector2(0, -6), new Vector2(184, 132));
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
            ConfigureEquipmentButton(root);
            BuildSwapSelectors(LayoutObject(canvas.transform, "CombatActionPreviewRoot/PreviewSelectors"),
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

        private static void BuildAttackPointer(Transform row, GameObject root)
        {
            for (int direction = 0; direction < 6; direction++)
            {
                Transform old = row.Find("AttackDirection_" + direction);
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            SceneBuildSupport.SetObject(root.GetComponent<CombatCommandInput>(), "targetInput", root.GetComponent<CombatTargetInput>());
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
            SettlementSceneBuilder.ApplyPanelSkin(panel);
            SettlementSceneBuilder.ConfigureColumn(panel);
            Text heading = SceneBuildSupport.CreateText("TargetHeading", panel.transform, "目标详情", 24);
            SettlementSceneBuilder.ApplyTextSkin(heading, 34);
            Text closeLabel;
            Button close = SceneBuildSupport.CreateButton("CloseTargetButton", panel.transform, "关闭", out closeLabel);
            SettlementSceneBuilder.ApplyButtonSkin(close);
            closeLabel.font = SettlementSceneBuilder.UiFont;
            closeLabel.verticalOverflow = VerticalWrapMode.Overflow;
            Sprite playerPortrait = SceneBuildSupport.RequireAsset<Sprite>(
                "Assets/Art/UI/Guanzhong/FormalPlayer_Default_Portrait.png");
            Image portrait = CompactImage(panel.transform, "TargetPortrait", null);
            portrait.sprite = playerPortrait;
            Text targetNameLabel = SceneBuildSupport.CreateText("TargetName", panel.transform, string.Empty, 26);
            SettlementSceneBuilder.ApplyTextSkin(targetNameLabel, 32);
            Text faction = SceneBuildSupport.CreateText("TargetFaction", panel.transform, string.Empty, 20);
            faction.font = SettlementSceneBuilder.UiFont;
            faction.color = new Color32(194, 208, 203, 255);
            faction.raycastTarget = false;
            Text healthLabel = SceneBuildSupport.CreateText("TargetHealthLabel", panel.transform, string.Empty, 18);
            Text spiritLabel = SceneBuildSupport.CreateText("TargetSpiritLabel", panel.transform, string.Empty, 18);
            foreach (Text label in new[] { healthLabel, spiritLabel })
            {
                label.font = SettlementSceneBuilder.UiFont;
                label.alignment = TextAnchor.MiddleLeft;
                label.color = new Color32(239, 233, 211, 255);
                label.raycastTarget = false;
            }
            // The project font's line advance exceeds the compact label heights; never crop glyphs.
            foreach (Text label in new[] { heading, targetNameLabel, faction, healthLabel, spiritLabel })
            {
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
            }
            Image healthFill = CreateTargetBar(panel.transform, "TargetHealthBar", "fill_health");
            Image spiritFill = CreateTargetBar(panel.transform, "TargetSpiritBar", "fill_mana");
            Text details = SceneBuildSupport.CreateText("CombatTargetDetails", panel.transform, string.Empty, 20);
            SettlementSceneBuilder.ApplyTextSkin(details, 120);
            details.raycastTarget = false;
            SceneBuildSupport.SetObject(view, "root", panel);
            SceneBuildSupport.SetObject(view, "portrait", portrait);
            SceneBuildSupport.SetObject(view, "nameText", targetNameLabel);
            SceneBuildSupport.SetObject(view, "factionText", faction);
            SceneBuildSupport.SetObject(view, "healthFill", healthFill);
            SceneBuildSupport.SetObject(view, "healthLabel", healthLabel);
            SceneBuildSupport.SetObject(view, "spiritFill", spiritFill);
            SceneBuildSupport.SetObject(view, "spiritLabel", spiritLabel);
            SceneBuildSupport.SetObject(view, "details", details);
            SceneBuildSupport.SetObject(view, "closeButton", close);
            SceneBuildSupport.SetObject(view, "playerPortrait", playerPortrait);
            SceneBuildSupport.SetObject(view, "shijiahouPortrait",
                SceneBuildSupport.RequireAsset<Sprite>(CombatCompactUiAssets.Folder + "enemy_shijiahou.png"));
            SceneBuildSupport.SetObject(view, "lieshijiaPortrait",
                SceneBuildSupport.RequireAsset<Sprite>(CombatCompactUiAssets.Folder + "enemy_lieshijia.png"));
            AdventureSceneInstaller installer = root.GetComponent<AdventureSceneInstaller>();
            SceneBuildSupport.SetObject(installer, "combatTargetView", view);
            SceneBuildSupport.SetObject(installer, "combatTargetInput", input);
            panel.SetActive(false);
        }

        private static Image CreateTargetBar(Transform parent, string name, string fillSprite)
        {
            var track = new GameObject(name, typeof(RectTransform), typeof(Image));
            track.transform.SetParent(parent, false);
            track.GetComponent<Image>().sprite = CombatCompactUiAssets.Sprite("bar_track");
            track.GetComponent<Image>().type = Image.Type.Sliced;
            track.GetComponent<Image>().color = Color.white;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            Image image = fill.GetComponent<Image>();
            image.sprite = CombatCompactUiAssets.Sprite(fillSprite);
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = 0;
            image.color = Color.white;
            foreach (Graphic graphic in track.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            return image;
        }

        [MenuItem("天章/场景/刷新战斗攻击档案")]
        public static void RefreshCombatAttackProfiles()
        {
            // Content imports create AttackProfileData assets; the saved scene owns the serialized
            // array the combat session reads at runtime, so refresh only that reference list.
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject root = Array.Find(scene.GetRootGameObjects(), value => value.name == "AdventureRoot");
            if (root == null) throw new InvalidOperationException("AdventureScene is missing AdventureRoot.");
            SceneBuildSupport.SetObjects(root.GetComponent<AdventureSceneInstaller>(), "attackProfiles", LoadAttackProfiles());
            SceneBuildSupport.Save(SceneBuildSupport.AdventureScenePath);
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
            Transform old = actionRoot.GetComponentsInChildren<RectTransform>(true).SingleOrDefault(rect => rect.name == "MovementControls");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var controls = new GameObject("MovementControls", typeof(RectTransform), typeof(VerticalLayoutGroup));
            controls.transform.SetParent(actionRoot.transform, false);
            controls.transform.SetAsFirstSibling();
            SettlementSceneBuilder.ConfigureColumn(controls, 0);
            controls.GetComponent<VerticalLayoutGroup>().spacing = 8;
            SettlementSceneBuilder.SetHeight(controls, 96);
            Text status = SceneBuildSupport.CreateText("MovementStatus", controls.transform, "移动", 18);
            SettlementSceneBuilder.ApplyTextSkin(status, 32);
            var row = new GameObject("MovementButtons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(controls.transform, false);
            row.GetComponent<HorizontalLayoutGroup>().spacing = 6;
            row.GetComponent<HorizontalLayoutGroup>().childControlWidth = true;
            row.GetComponent<HorizontalLayoutGroup>().childControlHeight = true;
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            SettlementSceneBuilder.SetHeight(row, 56);
            Button move = SceneBuildSupport.CreateButton("MoveButton", row.transform, "移动", out _);
            SettlementSceneBuilder.ApplyButtonSkin(move);
            // Moving commits on the first legal board click; the shared preview popup owns cancellation.
            // Two starter arts: 96 movement + 344 controls + seven 4-unit gaps.
            VerticalLayoutGroup actionColumn = actionRoot.GetComponent<VerticalLayoutGroup>();
            if (actionColumn != null) actionColumn.spacing = 4;
            SettlementSceneBuilder.SetHeight(actionRoot, 468);
            SceneBuildSupport.SetObject(bar, "moveButton", move);
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
            bars.arraySize = 3;
            for (int index = 0; index < 3; index++)
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
            var siblingIndices = new List<(string Name, int Index)>();
            foreach (string name in new[] { "CombatTimelineRoot", "CombatActionPreviewRoot" })
            {
                Transform existing = canvasRoot.transform.Find(name);
                if (existing != null) siblingIndices.Add((name, existing.GetSiblingIndex()));
            }
            siblingIndices.Sort((left, right) => left.Index.CompareTo(right.Index));
            BuildCombatTimeline(root, canvasRoot.GetComponent<Canvas>(), root.GetComponent<AdventureSceneInstaller>(),
                root.GetComponent<CombatHudPresenter>());
            BuildCombatActionPreview(root, canvasRoot.GetComponent<Canvas>());
            BuildCombatLayout(root, canvasRoot.GetComponent<Canvas>());
            foreach (var sibling in siblingIndices)
                canvasRoot.transform.Find(sibling.Name).SetSiblingIndex(sibling.Index);
            SceneBuildSupport.Save(SceneBuildSupport.AdventureScenePath);
        }

        private static void BuildCombatTimeline(GameObject root, Canvas canvas, AdventureSceneInstaller installer,
            CombatHudPresenter presenter)
        {
            Transform old = canvas.transform.Find("CombatTimelineRoot");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            CombatTimelineView view = root.GetComponent<CombatTimelineView>() ?? root.AddComponent<CombatTimelineView>();
            GameObject rail = SceneBuildSupport.CreatePanel("CombatTimelineRoot", canvas.transform, Vector2.zero, Vector2.one);
            CompactRect(rail, 32, 232, 104, 512);
            CombatCompactUiAssets.Panel(rail);
            rail.GetComponent<Image>().raycastTarget = false;
            Text tick = SceneBuildSupport.CreateText("TimelineTick", rail.transform, string.Empty, 22);
            tick.font = SettlementSceneBuilder.UiFont;
            tick.raycastTarget = false;
            PlaceTimelineRect(tick.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(96, 34));
            var track = new GameObject("TimelineTrack", typeof(RectTransform)).GetComponent<RectTransform>();
            track.SetParent(rail.transform, false);
            track.anchorMin = Vector2.zero;
            track.anchorMax = Vector2.one;
            track.offsetMin = new Vector2(4, 18);
            track.offsetMax = new Vector2(-4, -44);
            var entries = new RectTransform[3];
            var buttons = new Button[3];
            var labels = new Text[3];
            var ctLabels = new Text[3];
            var portraits = new Image[3];
            for (int index = 0; index < 3; index++)
            {
                buttons[index] = SceneBuildSupport.CreateButton("TimelineEntry" + index, track, string.Empty, out labels[index]);
                CombatCompactUiAssets.Button(buttons[index]);
                buttons[index].image.sprite = null;
                buttons[index].image.color = Color.clear;
                buttons[index].transition = Selectable.Transition.None;
                labels[index].fontSize = 22;
                labels[index].alignment = TextAnchor.MiddleCenter;
                PlaceTimelineRect(labels[index].rectTransform, new Vector2(0, 1), new Vector2(0, 0), new Vector2(96, 34));
                entries[index] = buttons[index].GetComponent<RectTransform>();
                PlaceTimelineRect(entries[index], new Vector2(0.5f, 1), new Vector2(0, -index * track.rect.height / 3), new Vector2(96, 148));
                ctLabels[index] = SceneBuildSupport.CreateText("TimelineCt", entries[index], string.Empty, 24);
                ctLabels[index].font = SettlementSceneBuilder.UiFont;
                ctLabels[index].raycastTarget = false;
                ctLabels[index].alignment = TextAnchor.MiddleCenter;
                PlaceTimelineRect(ctLabels[index].rectTransform, new Vector2(0, 1), new Vector2(0, -112), new Vector2(96, 36));
                var portrait = new GameObject("Identity", typeof(RectTransform), typeof(Image));
                portrait.transform.SetParent(entries[index], false);
                portrait.transform.SetAsFirstSibling();
                portraits[index] = portrait.GetComponent<Image>();
                portraits[index].preserveAspect = true;
                portraits[index].raycastTarget = false;
                PlaceTimelineRect(portrait.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(9, -34), new Vector2(78, 78));
                Image frame = CompactImage(entries[index], "SelectionFrame", "frame_selected");
                frame.type = Image.Type.Sliced;
                PlaceTimelineRect(frame.rectTransform, new Vector2(0, 1), new Vector2(9, -34), new Vector2(78, 78));
                frame.gameObject.SetActive(false);
                Image current = CompactImage(entries[index], "CurrentMarker", "timeline_current");
                PlaceTimelineRect(current.rectTransform, new Vector2(0, 1), new Vector2(0, -34), new Vector2(4, 78));
                current.gameObject.SetActive(false);
            }
            GameObject detail = SceneBuildSupport.CreatePanel("TimelineFocus", rail.transform, Vector2.zero, Vector2.one);
            CombatCompactUiAssets.Panel(detail);
            detail.GetComponent<Image>().raycastTarget = false;
            PlaceTimelineRect(detail.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(12, 0), new Vector2(320, 160));
            detail.GetComponent<RectTransform>().pivot = new Vector2(0, 1);
            Text focus = SceneBuildSupport.CreateText("TimelineFocusText", detail.transform, string.Empty, 24);
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
            SceneBuildSupport.SetObject(view, "chargeTrack", track);
            SceneBuildSupport.SetObjects(view, "entryRoots", entries);
            SceneBuildSupport.SetObjects(view, "entryButtons", buttons);
            SceneBuildSupport.SetObjects(view, "entryLabels", labels);
            SceneBuildSupport.SetObjects(view, "entryCtLabels", ctLabels);
            SceneBuildSupport.SetObjects(view, "entryPortraits", portraits);
            SceneBuildSupport.SetObject(view, "playerPortrait", SceneBuildSupport.RequireAsset<Sprite>("Assets/Art/UI/Guanzhong/FormalPlayer_Default_Portrait.png"));
            SceneBuildSupport.SetObject(view, "shijiahouPortrait", SceneBuildSupport.RequireAsset<Sprite>(CombatCompactUiAssets.Folder + "enemy_shijiahou.png"));
            SceneBuildSupport.SetObject(view, "lieshijiaPortrait", SceneBuildSupport.RequireAsset<Sprite>(CombatCompactUiAssets.Folder + "enemy_lieshijia.png"));
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
            Button thirdArt = SceneBuildSupport.CreateButton("ThirdArtButton", actionRoot.transform, "术法", out Text thirdLabel);
            Button divine = SceneBuildSupport.CreateButton("DivineButton", actionRoot.transform, "神通", out Text divineLabel);
            Button guard = SceneBuildSupport.CreateButton("GuardButton", actionRoot.transform, "防御", out _);
            Button wait = SceneBuildSupport.CreateButton("WaitButton", actionRoot.transform, "待机", out Text waitLabel);
            Button swap = SceneBuildSupport.CreateButton("SwapSpellButton", actionRoot.transform, "换术", out _);
            foreach (Button button in new[] { basic, art, secondArt, thirdArt, divine, guard, wait, swap })
            {
                SettlementSceneBuilder.ApplyButtonSkin(button);
                SettlementSceneBuilder.SetHeight(button.gameObject, 44);
            }
            foreach (Button button in new[] { art, secondArt, thirdArt })
            {
                SettlementSceneBuilder.SetHeight(button.gameObject, 64);
                button.GetComponentInChildren<Text>().fontSize = 18;
            }
            Text status = SceneBuildSupport.CreateText("ActionStatusText", actionRoot.transform, string.Empty, 16);
            SettlementSceneBuilder.ApplyTextSkin(status, 48);
            SceneBuildSupport.SetObject(actionBar, "root", actionRoot);
            SceneBuildSupport.SetObject(actionBar, "basicAttackButton", basic);
            SceneBuildSupport.SetObject(actionBar, "artButton", art);
            SceneBuildSupport.SetObject(actionBar, "artLabel", artLabel);
            SceneBuildSupport.SetObject(actionBar, "secondArtButton", secondArt);
            SceneBuildSupport.SetObject(actionBar, "secondArtLabel", secondLabel);
            SceneBuildSupport.SetObject(actionBar, "thirdArtButton", thirdArt);
            SceneBuildSupport.SetObject(actionBar, "thirdArtLabel", thirdLabel);
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
            camera.orthographicSize = 5f;
            AdventureCameraController controller = camera.GetComponent<AdventureCameraController>();
            if (controller == null) controller = camera.gameObject.AddComponent<AdventureCameraController>();
            controller.Configure(camera, battlefield.transform, battlefield.transform.GetChild(0).position.y,
                minimumSize: 3f, maximumSize: 7f, zoomStep: 0.5f);
            CenterCameraOnBattlefield(camera, battlefield);
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(controller);
        }

        private static void CenterCameraOnBattlefield(Camera camera, GameObject battlefield)
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (Renderer renderer in battlefield.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!any) return;

            float groundHeight = battlefield.transform.GetChild(0).position.y;
            // Leave room below the top-right HUD for enemy head bars in the default close view.
            var focus = new Vector3(bounds.center.x, groundHeight, bounds.center.z + 0.4f);
            Vector3 forward = camera.transform.forward;
            if (Mathf.Abs(forward.y) > 0.0001f)
            {
                float distance = (focus.y - camera.transform.position.y) / forward.y;
                if (distance > 0f)
                {
                    Vector3 viewCenter = camera.transform.position + forward * distance;
                    camera.transform.position += new Vector3(focus.x - viewCenter.x, 0f, focus.z - viewCenter.z);
                }
            }
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
            GameObject rockPrefab = SceneBuildSupport.RequireAsset<GameObject>(GuanzhongTerrainAssetBuilder.RockPrefabPath);
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
                    var obstacle = (GameObject)PrefabUtility.InstantiatePrefab(rockPrefab);
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
