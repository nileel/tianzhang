using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Game;
using TianZhang.Settlement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TianZhang.Tests
{
    /// <summary>
    /// 正式薄切片玩家显示文本 EditMode 闸门（U-GZ-UI-TEXT-01）：生产实体的中文显示映射、
    /// 缺键回退、未知原因回退且原始稳定原因保留在开发日志、已证明显示字段（遭遇错误文本）的
    /// 嵌入键解析、战斗日志原样保留（自定义名称与技术日志含 Language 键时不改写）、场景重建后
    /// 正式控件仍呈现中文。显示边界只做单向映射，不修改稳定 ID / 原因字段。
    /// </summary>
    public sealed class GuanzhongFormalUiTextTests
    {
        private static TextAsset LoadLanguageTable()
        {
            var table = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/DataConfig/Language.csv");
            Assert.IsNotNull(table, "Language.csv must be importable as a TextAsset.");
            return table;
        }

        [Test]
        public void ProductionEntitiesResolveToApprovedChineseNames()
        {
            UiText.Load(LoadLanguageTable());

            Assert.AreEqual("关中城", UiText.Resolve("settlement_guanzhong_city"));
            Assert.AreEqual("悬赏板", UiText.Resolve("settlement_feature_bounty_board"));
            Assert.AreEqual("石甲兽", UiText.Resolve("enemy_shijiahou"));
            Assert.AreEqual("劣质灵石", UiText.Resolve("item_lingshi_low"));
            Assert.AreEqual("石甲碎片", UiText.Resolve("item_shijia_piece"));
            Assert.AreEqual("旧水驿", UiText.Resolve("charter_site_old_water_station"));
            Assert.AreEqual("水府地纪", UiText.Resolve("charter_entry_suifu_diji"));
            Assert.AreEqual("徒手", UiText.Resolve("attack_profile_basic_unarmed"));
            Assert.AreEqual("石甲兽悬赏 · 一次性除害令", UiText.Resolve("bounty_guanzhong_shijiahou_title"));
            Assert.AreEqual("关中", UiText.ResolveId("region_", "guanzhong"));
            Assert.AreEqual("关中野外", UiText.ResolveId("adventure_", "guanzhong_wild"));
            Assert.AreEqual("草地", UiText.Resolve("surface_grassland"));
            Assert.AreEqual("黄土", UiText.Resolve("surface_loess"));
            Assert.AreEqual("区域枢纽", UiText.Resolve("world_node_type_hub"));
        }

        [Test]
        public void MissingKeysFallBackToTheKeyItselfWithoutFabricatedText()
        {
            UiText.Load(LoadLanguageTable());

            Assert.AreEqual("key_that_does_not_exist", UiText.Resolve("key_that_does_not_exist"));
            Assert.AreEqual("unknown_settlement", UiText.ResolveId("settlement_", "unknown_settlement"));
        }

        [Test]
        public void ReasonDisplayMapsKnownReasonsAndKeepsUnknownReasonsInLogs()
        {
            UiText.Load(LoadLanguageTable());

            // 带 ":稳定ID" 后缀的原因取前缀映射，后缀 ID 不属于显示事实。
            Assert.AreEqual(
                "单据点不存在",
                UiText.ReasonDisplay(
                    "settlement_charter_site_missing:charter_site_old_water_station",
                    "不可用"));
            Assert.AreEqual("该悬赏已经领取", UiText.ReasonDisplay("bounty_claim_repeated", "操作失败"));
            Assert.AreEqual("正式提交成功", UiText.ReasonDisplay("charter_panel_formal_committed", "操作失败"));
            Assert.AreEqual("步骤顺序不正确", UiText.ReasonDisplay("charter_interaction_action_out_of_order", "操作失败"));
            Assert.AreEqual("册界候选未获胜", UiText.ReasonDisplay("charter_conflict_not_won", "操作失败"));
            Assert.AreEqual(string.Empty, UiText.ReasonDisplay(string.Empty, "操作失败"));

            // 未知原因显示可理解回退，原始稳定原因保留到开发日志，不伪造成功。
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("未映射的稳定原因"));
            Assert.AreEqual("操作失败", UiText.ReasonDisplay("some_unknown_reason_code", "操作失败"));
        }

        [Test]
        public void ResolveEmbeddedReplacesKnownKeysAndPreservesUnknownText()
        {
            UiText.Load(LoadLanguageTable());

            // ResolveEmbedded 只用于已证明的显示字段（AdventureSourceText 遭遇错误文本等），
            // 不用于任意整条战斗日志；战斗日志按原样保留（见 BattleLog...Verbatim 用例）。
            string logLine = "无名修士 攻击 attack_profile_basic_unarmed → 石甲兽: 命中 10";
            Assert.AreEqual("无名修士 攻击 徒手 → 石甲兽: 命中 10", UiText.ResolveEmbedded(logLine));

            string unknown = "unknown_machine_token 保持原样";
            Assert.AreEqual(unknown, UiText.ResolveEmbedded(unknown));
        }

        [Test]
        public void BattleLogPreservesCustomNamesAndTechnicalLogsVerbatim()
        {
            UiText.Load(LoadLanguageTable());

            // 整条日志不做全局键替换（复审返工项）：玩家自定义名称或技术日志只要含有 Language 键
            // 子串（如 attack_profile_basic_unarmed）就必须原样保留，不得被改写为"徒手"。
            var host = new GameObject("BattleLogVerbatimHost");
            try
            {
                var ui = host.AddComponent<BattleUIManager>();
                typeof(BattleUIManager)
                    .GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(ui, null);

                var logText = GameObject.Find("LogText").GetComponent<Text>();
                Assert.IsNotNull(logText, "the battle log text must be built by Awake.");

                ui.AddLog("我的角色 attack_profile_basic_unarmed 的存档已保存");
                ui.AddLog("技术日志: settlement_guanzhong_city 已加载 attack_profile_basic_unarmed");

                Assert.AreEqual(
                    "我的角色 attack_profile_basic_unarmed 的存档已保存\n" +
                    "技术日志: settlement_guanzhong_city 已加载 attack_profile_basic_unarmed\n",
                    logText.text);
            }
            finally
            {
                var canvas = GameObject.Find("UICanvas");
                if (canvas != null)
                    Object.DestroyImmediate(canvas);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void BattleUiDoesNotResolveProfileKeysAtButtonOrLogFields()
        {
            UiText.Load(LoadLanguageTable());

            // 已证明的战斗档案显示字段（术法/神通按钮标签）由 ExplorationController 源端
            // `UiText.Resolve` 解析为中文后传入；BattleUIManager 本身是透传层，原始档案键
            // 传入时保持原样，不得在 UI 层再做整串替换。
            var host = new GameObject("BattleUiRawFieldHost");
            try
            {
                var ui = host.AddComponent<BattleUIManager>();
                typeof(BattleUIManager)
                    .GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(ui, null);

                ui.RefreshSpellButtons(
                    new[] { "attack_profile_basic_unarmed" },
                    new[] { 0 },
                    10,
                    new[] { 0 },
                    1);
                Assert.AreEqual("attack_profile_basic_unarmed", ButtonLabel("BtnSpell0"));

                var logText = GameObject.Find("LogText").GetComponent<Text>();
                ui.AddLog("未知原因 attack_profile_basic_unarmed 保持原样");
                StringAssert.Contains("attack_profile_basic_unarmed", logText.text);

                // 对照：同一键的玩家显示映射仍由源端 UiText.Resolve 提供（显示字段专用）。
                Assert.AreEqual("徒手", UiText.Resolve("attack_profile_basic_unarmed"));
            }
            finally
            {
                var canvas = GameObject.Find("UICanvas");
                if (canvas != null)
                    Object.DestroyImmediate(canvas);
                Object.DestroyImmediate(host);
            }
        }

        private static string ButtonLabel(string buttonName)
        {
            // 动作栏默认隐藏（BuildUI 关闭 ActionBar），需用 FindObjectsOfTypeAll 查找非激活控件。
            foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
            {
                if (button.name != buttonName)
                    continue;
                var label = button.GetComponentInChildren<Text>();
                Assert.IsNotNull(label, buttonName + " must carry a label text.");
                return label.text;
            }

            Assert.Fail(buttonName + " must exist after building the battle UI.");
            return null;
        }

        [Test]
        public void CharterStepAndBountyStatusDisplayKeysExistForEveryReachableState()
        {
            UiText.Load(LoadLanguageTable());

            foreach (string key in new[]
            {
                "charter_step_unopened", "charter_step_passage", "charter_step_management",
                "charter_step_nodes", "charter_step_registration", "charter_step_supplies",
                "charter_step_evaluation", "charter_step_committed",
                "bounty_status_available", "bounty_status_accepted", "bounty_status_completed",
                "bounty_status_claimed",
            })
            {
                Assert.IsTrue(UiText.TryResolve(key, out string text), key);
                Assert.IsFalse(string.IsNullOrWhiteSpace(text), key);
            }
        }

        [Test]
        public void ProductionSceneTextsRemainChineseAfterRebuild()
        {
            // 场景重建后正式控件仍呈现中文：SceneBuilder 把 Language.csv 序列化为语言表引用，
            // 视图显示不再恢复内部字段 / 稳定 ID。
            SceneBuilder.BuildSettlementScene();
            EditorSceneManager.OpenScene(
                "Assets/Scenes/SettlementScene.unity",
                OpenSceneMode.Single);
            var sessionGo = new GameObject("UiTextSceneSession");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetWorldNode("guanzhong_hub");
                session.SetSettlementId("guanzhong_city");
                var controller = Object.FindFirstObjectByType<SettlementSceneController>();
                controller.GetType()
                    .GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(controller, null);

                Assert.AreEqual("关中城", GameObject.Find("SettlementNameText").GetComponent<Text>().text);
                StringAssert.Contains("据点: 关中城", GameObject.Find("SettlementDetailText").GetComponent<Text>().text);
                StringAssert.Contains("区域: 关中", GameObject.Find("SettlementDetailText").GetComponent<Text>().text);
                StringAssert.Contains("悬赏板", GameObject.Find("SettlementFeature_bounty_board").GetComponentInChildren<Text>().text);
                StringAssert.Contains("进入副本: 关中野外", GameObject.Find("SettlementAdventure_guanzhong_wild").GetComponentInChildren<Text>().text);
                StringAssert.DoesNotContain("guanzhong_city", GameObject.Find("SettlementNameText").GetComponent<Text>().text);
            }
            finally
            {
                Object.DestroyImmediate(sessionGo);
                if (GameSession.Instance != null)
                    Object.DestroyImmediate(GameSession.Instance.gameObject);
                if (SceneFlowManager.Instance != null)
                    Object.DestroyImmediate(SceneFlowManager.Instance.gameObject);
                // 恢复套件既有的"最后打开 AdventureScene"状态，避免后续顺序相关用例（如
                // AdventurePanelDoesNotOverlapPlayerHud）受本用例遗留的据点场景影响。
                EditorSceneManager.OpenScene(
                    "Assets/Scenes/AdventureScene.unity",
                    OpenSceneMode.Single);
            }
        }
    }
}
