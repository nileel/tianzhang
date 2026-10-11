using TianZhang.Bootstrap;
using TianZhang.Content;
using TianZhang.Features.CharacterCreation;
using TianZhang.Game.CharacterCreation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TianZhang.Editor
{
    public static class StartMenuSceneBuilder
    {
        private const string PointBuyConfigAssetPath =
            "Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset";

        [MenuItem("天章/场景/重建开始菜单")]
        public static void Build()
        {
            GameObject root = SceneBuildSupport.BeginScene("StartMenuRoot", new Color(0.025f, 0.04f, 0.06f));
            new GameObject("GameBootstrap").AddComponent<GameBootstrap>();
            StartMenuSceneInstaller installer = root.AddComponent<StartMenuSceneInstaller>();
            StartMenuController controller = root.AddComponent<StartMenuController>();
            StartMenuView menuView = root.AddComponent<StartMenuView>();
            CharacterCreationController creationController = root.AddComponent<CharacterCreationController>();
            CharacterCreationView creationView = root.AddComponent<CharacterCreationView>();

            Canvas canvas = SceneBuildSupport.CreateCanvas();
            GameObject menuPanel = SceneBuildSupport.CreatePanel("StartMenuPanel", canvas.transform, new Vector2(0.25f, 0.15f), new Vector2(0.75f, 0.85f));
            SceneBuildSupport.AddVerticalLayout(menuPanel);
            SceneBuildSupport.CreateText("Title", menuPanel.transform, "天章", 38);
            Button newButton = SceneBuildSupport.CreateButton("NewPlayerButton", menuPanel.transform, "新建角色", out _);
            var slotContainer = new GameObject("SaveSlotContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
            slotContainer.transform.SetParent(menuPanel.transform, false);
            LayoutElement slotLayout = slotContainer.AddComponent<LayoutElement>();
            slotLayout.minHeight = 120f;
            slotLayout.flexibleHeight = 1f;
            Text emptyText = SceneBuildSupport.CreateText("EmptySaveText", menuPanel.transform, "暂无已有角色存档", 16);
            Text menuFailure = SceneBuildSupport.CreateText("StartMenuFailureText", menuPanel.transform, string.Empty, 16);

            GameObject creationPanel = SceneBuildSupport.CreatePanel("CharacterCreationPanel", canvas.transform, new Vector2(0.25f, 0.15f), new Vector2(0.75f, 0.85f));
            SceneBuildSupport.AddVerticalLayout(creationPanel);
            SceneBuildSupport.CreateText("CreationTitle", creationPanel.transform, "创建角色", 30);
            InputField slotInput = SceneBuildSupport.CreateInput("SlotIdInput", creationPanel.transform, "存档编号，例如 slot1");
            InputField nameInput = SceneBuildSupport.CreateInput("CharacterNameInput", creationPanel.transform, "角色名");
            Text summary = SceneBuildSupport.CreateText("CreationSummary", creationPanel.transform, string.Empty, 16);
            Text creationFailure = SceneBuildSupport.CreateText("CreationFailure", creationPanel.transform, string.Empty, 16);
            Button createButton = SceneBuildSupport.CreateButton("CreatePlayerButton", creationPanel.transform, "创建并进入游戏", out _);
            Button returnToMenuButton = SceneBuildSupport.CreateButton("ReturnToMenuButton", creationPanel.transform, "返回主页", out _);
            creationPanel.SetActive(false);

            SceneBuildSupport.SetObject(menuView, "newPlayerButton", newButton);
            SceneBuildSupport.SetObject(menuView, "slotContainer", slotContainer.transform);
            SceneBuildSupport.SetObject(menuView, "emptyText", emptyText);
            SceneBuildSupport.SetObject(menuView, "failureText", menuFailure);
            BuildSaveSlotList(menuView, menuPanel);
            BuildDeleteConfirmation(menuView, canvas);
            SceneBuildSupport.SetObject(creationView, "panel", creationPanel);
            SceneBuildSupport.SetObject(creationView, "slotIdInput", slotInput);
            SceneBuildSupport.SetObject(creationView, "characterNameInput", nameInput);
            SceneBuildSupport.SetObject(creationView, "summaryText", summary);
            SceneBuildSupport.SetObject(creationView, "failureText", creationFailure);
            SceneBuildSupport.SetObject(creationView, "createButton", createButton);
            SceneBuildSupport.SetObject(creationView, "returnToMenuButton", returnToMenuButton);
            BuildOriginSelection(creationView, creationPanel, summary);
            SceneBuildSupport.SetObject(controller, "view", menuView);
            SceneBuildSupport.SetObject(controller, "characterCreation", creationController);
            SceneBuildSupport.SetObject(creationController, "view", creationView);
            SceneBuildSupport.SetObject(installer, "contentCatalog", SceneBuildSupport.RequireAsset<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset"));
            SceneBuildSupport.SetObject(
                installer,
                "pointBuyConfig",
                SceneBuildSupport.RequireAsset<CharacterCreationPointBuyConfig>(PointBuyConfigAssetPath));
            SceneBuildSupport.SetObject(installer, "startMenuController", controller);
            SceneBuildSupport.SetObject(installer, "startMenuView", menuView);
            SceneBuildSupport.SetObject(installer, "characterCreationController", creationController);
            SceneBuildSupport.SetObject(installer, "characterCreationView", creationView);
            SceneBuildSupport.Save(SceneBuildSupport.StartMenuScenePath);
        }

        public static void BindPointBuyConfig()
        {
            Scene scene = EditorSceneManager.OpenScene(
                SceneBuildSupport.StartMenuScenePath,
                OpenSceneMode.Single);
            StartMenuSceneInstaller installer =
                Object.FindFirstObjectByType<StartMenuSceneInstaller>();
            if (installer == null)
                throw new System.InvalidOperationException("start_menu_installer_missing");

            SceneBuildSupport.SetObject(
                installer,
                "pointBuyConfig",
                SceneBuildSupport.RequireAsset<CharacterCreationPointBuyConfig>(PointBuyConfigAssetPath));
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException("start_menu_scene_save_failed");
        }

        [MenuItem("天章/场景/修复开始菜单存档列表")]
        public static void RebuildSaveSlotList()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.StartMenuScenePath, OpenSceneMode.Single);
            StartMenuView view = Object.FindFirstObjectByType<StartMenuView>(FindObjectsInactive.Include);
            GameObject panel = GameObject.Find("UICanvas/StartMenuPanel");
            if (view == null || panel == null)
                throw new System.InvalidOperationException("start_menu_save_list_owner_missing");
            BuildSaveSlotList(view, panel);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException("start_menu_scene_save_failed");
        }

        [MenuItem("天章/场景/增加开始菜单删除确认面板")]
        public static void RebuildSaveDeleteConfirmation()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.StartMenuScenePath, OpenSceneMode.Single);
            StartMenuView view = Object.FindFirstObjectByType<StartMenuView>(FindObjectsInactive.Include);
            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (view == null || canvas == null)
                throw new System.InvalidOperationException("start_menu_delete_confirmation_owner_missing");
            Transform previous = canvas.transform.Find("SaveDeleteConfirmPanel");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            BuildDeleteConfirmation(view, canvas);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException("start_menu_scene_save_failed");
        }

        private static void BuildDeleteConfirmation(StartMenuView view, Canvas canvas)
        {
            GameObject panel = SceneBuildSupport.CreatePanel(
                "SaveDeleteConfirmPanel",
                canvas.transform,
                new Vector2(0.32f, 0.3f),
                new Vector2(0.68f, 0.7f));
            SceneBuildSupport.AddVerticalLayout(panel);
            Text message = SceneBuildSupport.CreateText("DeleteConfirmText", panel.transform, string.Empty, 18);
            LayoutElement messageLayout = message.gameObject.AddComponent<LayoutElement>();
            messageLayout.minHeight = messageLayout.preferredHeight = 110f;
            messageLayout.flexibleHeight = 0f;
            Button confirm = SceneBuildSupport.CreateButton("ConfirmDeleteButton", panel.transform, "确认删除", out _);
            Button cancel = SceneBuildSupport.CreateButton("CancelDeleteButton", panel.transform, "取消", out _);
            panel.SetActive(false);
            SceneBuildSupport.SetObject(view, "deleteConfirmPanel", panel);
            SceneBuildSupport.SetObject(view, "deleteConfirmText", message);
            SceneBuildSupport.SetObject(view, "deleteConfirmButton", confirm);
            SceneBuildSupport.SetObject(view, "deleteCancelButton", cancel);
        }

        private static void BuildSaveSlotList(StartMenuView view, GameObject panel)
        {
            VerticalLayoutGroup panelLayout = panel.GetComponent<VerticalLayoutGroup>();
            panelLayout.childControlWidth = panelLayout.childControlHeight = true;
            panelLayout.childForceExpandHeight = false;
            foreach (string name in new[] { "Title", "NewPlayerButton", "EmptySaveText", "StartMenuFailureText" })
            {
                GameObject item = panel.transform.Find(name).gameObject;
                LayoutElement layout = item.GetComponent<LayoutElement>() ?? item.AddComponent<LayoutElement>();
                layout.minHeight = layout.preferredHeight = name == "Title" ? 52f : 48f;
                layout.flexibleHeight = 0f;
            }
            var serialized = new SerializedObject(view);
            Transform content = serialized.FindProperty("slotContainer").objectReferenceValue as Transform;
            if (content == null) throw new System.InvalidOperationException("start_menu_slot_container_missing");
            Transform oldScroll = panel.transform.Find("SaveSlotScroll");
            content.SetParent(panel.transform, false);
            if (oldScroll != null) Object.DestroyImmediate(oldScroll.gameObject);
            LayoutElement oldLayout = content.GetComponent<LayoutElement>();
            if (oldLayout != null) Object.DestroyImmediate(oldLayout);
            var scrollRoot = new GameObject("SaveSlotScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
            scrollRoot.transform.SetParent(panel.transform, false);
            scrollRoot.transform.SetSiblingIndex(2);
            scrollRoot.GetComponent<Image>().color = new Color(0.055f, 0.07f, 0.075f, 1f);
            LayoutElement scrollLayout = scrollRoot.GetComponent<LayoutElement>();
            scrollLayout.minHeight = 120f;
            scrollLayout.flexibleHeight = 1f;
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(scrollRoot.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
            content.SetParent(viewport.transform, false);
            RectTransform contentRect = (RectTransform)content;
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.anchoredPosition = contentRect.sizeDelta = Vector2.zero;
            VerticalLayoutGroup rows = content.GetComponent<VerticalLayoutGroup>();
            rows.childControlWidth = rows.childControlHeight = true;
            rows.childForceExpandWidth = true;
            rows.childForceExpandHeight = false;
            rows.spacing = 6f;
            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = scrollRoot.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            SceneBuildSupport.SetObject(view, "slotContainer", content);
        }

        [MenuItem("天章/场景/更新建角出身选择")]
        public static void RebuildOriginSelection()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.StartMenuScenePath, OpenSceneMode.Single);
            CharacterCreationView view = Object.FindFirstObjectByType<CharacterCreationView>(FindObjectsInactive.Include);
            if (view == null) throw new System.InvalidOperationException("character_creation_view_missing");
            var serialized = new SerializedObject(view);
            GameObject panel = serialized.FindProperty("panel").objectReferenceValue as GameObject;
            Text summary = serialized.FindProperty("summaryText").objectReferenceValue as Text;
            if (panel == null || summary == null)
                throw new System.InvalidOperationException("character_creation_panel_references_missing");
            BuildOriginSelection(view, panel, summary);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException("start_menu_scene_save_failed");
        }

        private static void BuildOriginSelection(CharacterCreationView view, GameObject panel, Text summary)
        {
            panel.GetComponent<VerticalLayoutGroup>().childControlWidth = true;
            Transform previous = panel.transform.Find("OriginSelection");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var row = new GameObject("OriginSelection", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(panel.transform, false);
            row.transform.SetSiblingIndex(summary.transform.GetSiblingIndex());
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            row.GetComponent<LayoutElement>().preferredHeight = 48f;
            var buttons = new Button[CharacterCreationCatalog.Origins.Length];
            var labels = new Text[buttons.Length];
            for (int index = 0; index < buttons.Length; index++)
            {
                OriginOption option = CharacterCreationCatalog.Origins[index];
                buttons[index] = SceneBuildSupport.CreateButton("Origin_" + option.Id, row.transform, option.DisplayName, out labels[index]);
                buttons[index].GetComponent<LayoutElement>().flexibleWidth = 1f;
                labels[index].fontSize = 16;
            }
            LayoutElement summaryLayout = summary.GetComponent<LayoutElement>();
            if (summaryLayout == null) summaryLayout = summary.gameObject.AddComponent<LayoutElement>();
            summaryLayout.preferredHeight = 84f;
            SceneBuildSupport.SetObjects(view, "originButtons", buttons);
            SceneBuildSupport.SetObjects(view, "originLabels", labels);
        }
    }
}
