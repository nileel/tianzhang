using System.Linq;
using TianZhang.Bootstrap;
using TianZhang.Content;
using TianZhang.Features.Settlement;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Editor
{
    public static class SettlementSceneBuilder
    {
        [MenuItem("天章/场景/重建据点")]
        public static void Build()
        {
            GameObject root = SceneBuildSupport.BeginScene("SettlementRoot", new Color(0.08f, 0.07f, 0.05f));
            SettlementSceneInstaller installer = root.AddComponent<SettlementSceneInstaller>();
            SettlementController controller = root.AddComponent<SettlementController>();
            SettlementView view = root.AddComponent<SettlementView>();
            SettlementFeatureDispatcher dispatcher = root.AddComponent<SettlementFeatureDispatcher>();
            Canvas canvas = SceneBuildSupport.CreateCanvas();
            var background = new GameObject("GuanzhongCityBackground", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(canvas.transform, false);
            Image backgroundImage = background.GetComponent<Image>();
            backgroundImage.sprite = SceneBuildSupport.RequireAsset<Sprite>("Assets/Art/UI/Guanzhong/Guanzhong_City_Background.png");
            backgroundImage.preserveAspect = true;
            backgroundImage.raycastTarget = false;
            backgroundImage.rectTransform.anchorMin = Vector2.zero;
            backgroundImage.rectTransform.anchorMax = Vector2.one;
            backgroundImage.rectTransform.sizeDelta = Vector2.zero;
            GameObject panel = SceneBuildSupport.CreatePanel("SettlementPanel", canvas.transform, Vector2.zero, Vector2.one);
            PlacePanel(panel, new Vector2(48, 48), new Vector2(468, 1032));
            ApplyPanelSkin(panel);
            ConfigureColumn(panel);
            Text name = SceneBuildSupport.CreateText("SettlementNameText", panel.transform, "据点", 32);
            AddPortrait(panel.transform, "SettlementPlayerPortrait", 184f);
            Text detail = SceneBuildSupport.CreateText("SettlementDetailText", panel.transform, string.Empty, 18);
            Text status = SceneBuildSupport.CreateText("SettlementStatusText", panel.transform, string.Empty, 16);
            Button feature = SceneBuildSupport.CreateButton("SettlementFeature_bounty_board", panel.transform, "功能", out Text featureLabel);
            Button adventure = SceneBuildSupport.CreateButton("SettlementAdventure_guanzhong_wild", panel.transform, "副本", out Text adventureLabel);
            Button charterEntry = SceneBuildSupport.CreateButton("SettlementCharterSiteEntry", panel.transform, "旧水驿入口", out Text charterEntryLabel);
            charterEntryLabel.gameObject.name = "SettlementCharterSiteEntryStatus";
            Button returnButton = SceneBuildSupport.CreateButton("ReturnToWorldButton", panel.transform, "返回主世界", out _);
            Button saveButton = SceneBuildSupport.CreateButton("SaveAndReturnButton", panel.transform, "保存并返回菜单", out _);
            ApplyTextSkin(name, 52);
            ApplyTextSkin(detail, 64);
            ApplyTextSkin(status, 64);
            foreach (Button button in new[] { feature, adventure, charterEntry, returnButton, saveButton })
                ApplyButtonSkin(button, button == feature);

            BountyBoardView bounty = BuildBountyPanel(canvas.transform);
            CharterSiteView charter = BuildCharterPanel(canvas.transform);
            view.Configure(
                name,
                detail,
                status,
                feature,
                featureLabel,
                adventure,
                adventureLabel,
                returnButton,
                saveButton,
                bounty,
                charterEntry,
                charterEntryLabel,
                charter);
            SceneBuildSupport.SetObject(view, "languageTable", SceneBuildSupport.RequireAsset<TextAsset>("Assets/DataConfig/Language.csv"));
            SceneBuildSupport.SetObject(installer, "contentCatalog", SceneBuildSupport.RequireAsset<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset"));
            SceneBuildSupport.SetObject(installer, "controller", controller);
            SceneBuildSupport.SetObject(installer, "view", view);
            SceneBuildSupport.SetObject(installer, "dispatcher", dispatcher);
            SceneBuildSupport.Save(SceneBuildSupport.SettlementScenePath);
        }

        private static BountyBoardView BuildBountyPanel(Transform parent)
        {
            GameObject panel = SceneBuildSupport.CreatePanel("BountyBoardPanel", parent, Vector2.zero, Vector2.one);
            PlacePanel(panel, new Vector2(1288, 184), new Vector2(1872, 896));
            ApplyPanelSkin(panel);
            ConfigureColumn(panel);
            BountyBoardView view = panel.AddComponent<BountyBoardView>();
            Text title = SceneBuildSupport.CreateText("BountyTitle", panel.transform, "悬赏榜", 26);
            GameObject paper = SceneBuildSupport.CreatePanel("BountyPaper", panel.transform, Vector2.zero, Vector2.one);
            ApplyPanelSkin(paper, true);
            ConfigureColumn(paper);
            SetHeight(paper, 176);
            Text entries = SceneBuildSupport.CreateText("BountyEntries", paper.transform, string.Empty, 20);
            Text result = SceneBuildSupport.CreateText("BountyResult", panel.transform, string.Empty, 18);
            Button accept = SceneBuildSupport.CreateButton("AcceptBountyButton", panel.transform, "接取", out _);
            Button claim = SceneBuildSupport.CreateButton("ClaimBountyButton", panel.transform, "领取", out _);
            Button close = SceneBuildSupport.CreateButton("CloseBountyButton", panel.transform, "关闭", out _);
            ApplyTextSkin(title, 52);
            ApplyTextSkin(entries, 128, true);
            ApplyTextSkin(result, 64);
            foreach (Button button in new[] { accept, claim, close }) ApplyButtonSkin(button, button != close);
            view.Configure(title, entries, result, accept, claim, close);
            panel.SetActive(false);
            return view;
        }

        // Local to the two Guanzhong builders; shared scene defaults remain owned by SceneBuildSupport.
        internal static Sprite UiSprite(string name)
        {
            return AssetDatabase.LoadAllAssetsAtPath("Assets/Art/UI/Guanzhong/Guanzhong_UI_Atlas.png")
                .OfType<Sprite>().Single(sprite => sprite.name == name);
        }

        internal static Font UiFont => SceneBuildSupport.RequireAsset<Font>("Assets/Art/UI/Guanzhong/Fonts/GuanzhongChinese.otf");

        internal static void PlacePanel(GameObject panel, Vector2 min, Vector2 max)
        {
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = min / new Vector2(1920, 1080);
            rect.anchorMax = max / new Vector2(1920, 1080);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        internal static void ConfigureColumn(GameObject target, int padding = 24)
        {
            VerticalLayoutGroup layout = target.GetComponent<VerticalLayoutGroup>() ?? target.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        internal static void SetHeight(GameObject target, float height)
        {
            LayoutElement layout = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = height;
            layout.flexibleHeight = 0;
        }

        internal static void ApplyPanelSkin(GameObject panel, bool paper = false)
        {
            Image image = panel.GetComponent<Image>();
            image.sprite = UiSprite(paper ? "Guanzhong_Panel_Paper" : "Guanzhong_Panel_Dark");
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 4;
            image.color = Color.white;
        }

        internal static void ApplyTextSkin(Text text, float height, bool paper = false)
        {
            text.font = UiFont;
            text.color = paper ? new Color32(50, 77, 73, 255) : new Color32(244, 234, 212, 255);
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            SetHeight(text.gameObject, height);
        }

        internal static void ApplyButtonSkin(Button button, bool primary = false)
        {
            Image image = button.GetComponent<Image>();
            Sprite normal = UiSprite("Guanzhong_Button_Normal");
            Sprite selected = UiSprite("Guanzhong_Button_Selected");
            image.sprite = primary ? selected : normal;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 6.4f;
            image.color = Color.white;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = primary ? selected : UiSprite("Guanzhong_Button_Hover"),
                pressedSprite = selected,
                selectedSprite = selected,
                disabledSprite = normal,
            };
            Text label = button.GetComponentInChildren<Text>();
            label.font = UiFont;
            label.fontSize = 20;
            label.color = new Color32(244, 234, 212, 255);
            label.raycastTarget = false;
            label.rectTransform.offsetMin = new Vector2(16, 4);
            label.rectTransform.offsetMax = new Vector2(-16, -4);
            SetHeight(button.gameObject, 56);
        }

        internal static void AddPortrait(Transform parent, string name, float height)
        {
            var portrait = new GameObject(name, typeof(RectTransform), typeof(Image));
            portrait.transform.SetParent(parent, false);
            Image image = portrait.GetComponent<Image>();
            image.sprite = SceneBuildSupport.RequireAsset<Sprite>("Assets/Art/UI/Guanzhong/FormalPlayer_Default_Portrait.png");
            image.preserveAspect = true;
            image.raycastTarget = false;
            SetHeight(portrait, height);
        }

        private static CharterSiteView BuildCharterPanel(Transform parent)
        {
            GameObject panel = SceneBuildSupport.CreatePanel("CharterSitePanel", parent, new Vector2(0.55f, 0.04f), new Vector2(0.97f, 0.96f));
            SceneBuildSupport.AddVerticalLayout(panel, 4);
            CharterSiteView view = panel.AddComponent<CharterSiteView>();
            CharterSiteController controller = panel.AddComponent<CharterSiteController>();
            Text title = SceneBuildSupport.CreateText("CharterTitle", panel.transform, "册界旧水驿", 22);
            Text site = SceneBuildSupport.CreateText("CharterSite", panel.transform, string.Empty, 13);
            Text step = SceneBuildSupport.CreateText("CharterStep", panel.transform, string.Empty, 13);
            Text identity = SceneBuildSupport.CreateText("CharterIdentity", panel.transform, string.Empty, 13);
            Text authority = SceneBuildSupport.CreateText("CharterAuthority", panel.transform, string.Empty, 13);
            Text node = SceneBuildSupport.CreateText("CharterNode", panel.transform, string.Empty, 13);
            Text supply = SceneBuildSupport.CreateText("CharterSupply", panel.transform, string.Empty, 13);
            Text environment = SceneBuildSupport.CreateText("CharterEnvironment", panel.transform, string.Empty, 13);
            Text result = SceneBuildSupport.CreateText("CharterResult", panel.transform, string.Empty, 13);
            Button passage = SceneBuildSupport.CreateButton("CharterPassage", panel.transform, "通行", out _);
            Button management = SceneBuildSupport.CreateButton("CharterManagement", panel.transform, "管理", out _);
            Button connect = SceneBuildSupport.CreateButton("CharterConnect", panel.transform, "连接节点", out _);
            Button register = SceneBuildSupport.CreateButton("CharterRegister", panel.transform, "登记规则", out _);
            Button prepare = SceneBuildSupport.CreateButton("CharterPrepare", panel.transform, "准备现实供给", out _);
            Button jindan = SceneBuildSupport.CreateButton("CharterJindan", panel.transform, "金丹评估", out _);
            Button yuanying = SceneBuildSupport.CreateButton("CharterYuanying", panel.transform, "元婴评估", out _);
            Button formal = SceneBuildSupport.CreateButton("CharterFormal", panel.transform, "正式提交", out _);
            Button close = SceneBuildSupport.CreateButton("CharterClose", panel.transform, "关闭", out _);
            controller.Configure(view);
            view.Configure(
                title, site, step, identity, authority, node, supply, environment, result,
                passage, management, connect, register, prepare, jindan, yuanying, formal, close, controller);
            panel.SetActive(false);
            return view;
        }
    }
}
