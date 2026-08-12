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
            GameObject panel = SceneBuildSupport.CreatePanel("SettlementPanel", canvas.transform, new Vector2(0.08f, 0.08f), new Vector2(0.55f, 0.92f));
            SceneBuildSupport.AddVerticalLayout(panel);
            Text name = SceneBuildSupport.CreateText("SettlementNameText", panel.transform, "据点", 32);
            Text detail = SceneBuildSupport.CreateText("SettlementDetailText", panel.transform, string.Empty, 18);
            Text status = SceneBuildSupport.CreateText("SettlementStatusText", panel.transform, string.Empty, 16);
            Button feature = SceneBuildSupport.CreateButton("SettlementFeature_bounty_board", panel.transform, "功能", out Text featureLabel);
            Button adventure = SceneBuildSupport.CreateButton("SettlementAdventure_guanzhong_wild", panel.transform, "副本", out Text adventureLabel);
            Button charterEntry = SceneBuildSupport.CreateButton("SettlementCharterSiteEntry", panel.transform, "旧水驿入口", out Text charterEntryLabel);
            charterEntryLabel.gameObject.name = "SettlementCharterSiteEntryStatus";
            Button returnButton = SceneBuildSupport.CreateButton("ReturnToWorldButton", panel.transform, "返回主世界", out _);
            Button saveButton = SceneBuildSupport.CreateButton("SaveAndReturnButton", panel.transform, "保存并返回菜单", out _);

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
            GameObject panel = SceneBuildSupport.CreatePanel("BountyBoardPanel", parent, new Vector2(0.57f, 0.1f), new Vector2(0.95f, 0.9f));
            SceneBuildSupport.AddVerticalLayout(panel);
            BountyBoardView view = panel.AddComponent<BountyBoardView>();
            Text title = SceneBuildSupport.CreateText("BountyTitle", panel.transform, "悬赏榜", 26);
            Text entries = SceneBuildSupport.CreateText("BountyEntries", panel.transform, string.Empty, 16);
            Text result = SceneBuildSupport.CreateText("BountyResult", panel.transform, string.Empty, 16);
            Button accept = SceneBuildSupport.CreateButton("AcceptBountyButton", panel.transform, "接取", out _);
            Button claim = SceneBuildSupport.CreateButton("ClaimBountyButton", panel.transform, "领取", out _);
            Button close = SceneBuildSupport.CreateButton("CloseBountyButton", panel.transform, "关闭", out _);
            view.Configure(title, entries, result, accept, claim, close);
            panel.SetActive(false);
            return view;
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
