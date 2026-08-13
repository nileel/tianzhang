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
            creationPanel.SetActive(false);

            SceneBuildSupport.SetObject(menuView, "newPlayerButton", newButton);
            SceneBuildSupport.SetObject(menuView, "slotContainer", slotContainer.transform);
            SceneBuildSupport.SetObject(menuView, "emptyText", emptyText);
            SceneBuildSupport.SetObject(menuView, "failureText", menuFailure);
            SceneBuildSupport.SetObject(creationView, "panel", creationPanel);
            SceneBuildSupport.SetObject(creationView, "slotIdInput", slotInput);
            SceneBuildSupport.SetObject(creationView, "characterNameInput", nameInput);
            SceneBuildSupport.SetObject(creationView, "summaryText", summary);
            SceneBuildSupport.SetObject(creationView, "failureText", creationFailure);
            SceneBuildSupport.SetObject(creationView, "createButton", createButton);
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
    }
}
