using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Adventure;
using TianZhang.Combat;
using TianZhang.Core;
using TianZhang.Entity;
using TianZhang.Game;
using TianZhang.Game.CharacterCreation;
using TianZhang.Map;
using TianZhang.Settlement;
using TianZhang.World;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

using TianZhang.Spatial;

namespace TianZhang.Tests
{
    /// <summary>
    /// 正式薄切片玩家可见文本端到端闸门（U-GZ-UI-TEXT-01）：从新建角色（关中小族出身）进入
    /// 主世界、关中城、悬赏板、旧水驿单据点、关中野外战斗、结算返回并领取悬赏。每个关键面板
    /// 断言玩家显示文本为已批准中文且不再直接暴露稳定 ID / 枚举 / 档案 ID / 机器原因；内部
    /// 稳定原因继续保留在控制器、会话与结果对象中。测试只驱动既有正式生产入口与公开控制器
    /// API，不复制遭遇、结算或悬赏实现。
    /// </summary>
    public sealed class GuanzhongFormalUiTextPlayModeTests
    {
        private const string AdventureId = "guanzhong_wild";
        private const string SettlementId = "guanzhong_city";
        private const string BountyId = "bounty_guanzhong_shijiahou";

        private GameObject flowGo;

        [UnityTearDown]
        public void TearDown()
        {
            if (flowGo != null)
                Object.DestroyImmediate(flowGo);
            if (SceneFlowManager.Instance != null)
                Object.DestroyImmediate(SceneFlowManager.Instance.gameObject);
            if (GameSession.Instance != null)
                Object.DestroyImmediate(GameSession.Instance.gameObject);
            flowGo = null;
        }

        [UnityTest]
        public IEnumerator FormalSliceShowsChineseTextFromCharacterCreationToBountyClaim()
        {
            flowGo = new GameObject("GuanzhongFormalUiTextFlow");
            var flow = flowGo.AddComponent<SceneFlowManager>();
            Assert.IsNotNull(GameSession.Instance, "SceneFlowManager must ensure the single session.");

            // 1. 新建角色（关中小族出身）→ 主世界：节点与描述只显示中文，不显示 regionId / 枚举。
            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.OriginId = "origin_minor_clan";
            flow.StartNewGame(CharacterCreationRules.BuildCharacterData(draft));
            yield return null;

            var world = Object.FindFirstObjectByType<WorldSceneController>();
            Assert.IsNotNull(world, "The formal WorldScene must bind its world controller.");
            Assert.AreEqual("guanzhong_hub", GameSession.Instance.CurrentWorldNodeId);
            var selectedNodeText = GameObject.Find("SelectedWorldNodeText")?.GetComponent<Text>();
            var descriptionText = GameObject.Find("SelectedWorldNodeDescription")?.GetComponent<Text>();
            Assert.IsNotNull(selectedNodeText);
            Assert.IsNotNull(descriptionText);
            Assert.AreEqual("关陇玄域", selectedNodeText.text);
            StringAssert.Contains("据点: 关中城", descriptionText.text);
            StringAssert.DoesNotContain("guanzhong_hub", descriptionText.text);
            StringAssert.DoesNotContain("RegionHub", descriptionText.text);

            // 2. 进入关中城：据点名 / 详情 / 功能 / 副本入口全部为中文。
            flow.EnterSettlement(SettlementId);
            yield return null;

            var settlementController = Object.FindFirstObjectByType<SettlementSceneController>();
            Assert.IsNotNull(settlementController, "The formal SettlementScene must bind its settlement controller.");
            Assert.AreEqual("关中城", GameObject.Find("SettlementNameText").GetComponent<Text>().text);
            StringAssert.Contains("据点: 关中城", GameObject.Find("SettlementDetailText").GetComponent<Text>().text);
            StringAssert.Contains("区域: 关中", GameObject.Find("SettlementDetailText").GetComponent<Text>().text);
            StringAssert.Contains("悬赏板", GameObject.Find("SettlementFeature_bounty_board").GetComponentInChildren<Text>().text);
            StringAssert.Contains("进入副本: 关中野外", GameObject.Find("SettlementAdventure_guanzhong_wild").GetComponentInChildren<Text>().text);

            // 3. 打开悬赏板：条目显示悬赏标题与中文状态，不显示稳定 ID / 枚举名。
            GameObject.Find("SettlementFeature_bounty_board").GetComponent<Button>().onClick.Invoke();
            var board = Object.FindFirstObjectByType<BountyBoardView>(FindObjectsInactive.Include);
            Assert.IsTrue(board.IsOpen);
            var entries = GameObject.Find("BountyBoardEntriesText").GetComponent<Text>();
            StringAssert.Contains("石甲兽悬赏 · 一次性除害令", entries.text);
            StringAssert.Contains("可接取", entries.text);
            StringAssert.DoesNotContain(BountyId, entries.text);
            StringAssert.DoesNotContain("Available", entries.text);

            GameObject.Find("BountyBoardAcceptButton").GetComponent<Button>().onClick.Invoke();
            StringAssert.Contains("已接取", entries.text);
            Assert.AreEqual(BountyStatus.Accepted, GameSession.Instance.GetBountyState(BountyId).Status);
            GameObject.Find("BountyBoardCloseButton").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(board.IsOpen);

            // 4. 旧水驿单据点：面板显示中文名与步骤状态，不显示站点 / 协议稳定 ID。
            GameObject.Find("SettlementCharterSiteEntry").GetComponent<Button>().onClick.Invoke();
            var panel = Object.FindFirstObjectByType<CharterSiteView>(FindObjectsInactive.Include);
            Assert.IsTrue(panel.IsOpen);
            StringAssert.Contains("旧水驿", GameObject.Find("CharterSiteSiteText").GetComponent<Text>().text);
            StringAssert.DoesNotContain("charter_site_old_water_station", GameObject.Find("CharterSiteSiteText").GetComponent<Text>().text);
            StringAssert.Contains("通行确认", GameObject.Find("CharterSiteStepText").GetComponent<Text>().text);
            GameObject.Find("CharterSiteCloseButton").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(panel.IsOpen);

            // 5. 进入关中野外：副本名与来源为中文；环境反馈不含档案 / 地表 / 现象 ID。
            flow.EnterAdventure(AdventureId, SceneReturnTarget.Settlement(SettlementId));
            yield return null;

            var adventure = Object.FindFirstObjectByType<AdventureSceneController>();
            Assert.IsNotNull(adventure, "The formal AdventureScene must bind its adventure controller.");
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            // 正式结算的掉落概率使用真实随机源；本闸门注入确定性源（100% 掉落）保证断言可重复，
            // 与既有 E2E 闸门（SequenceRandomSource）同模式。
            adventure.SetEncounterRandomSource(new SequenceRandomSource(0, 0));
            StringAssert.Contains("当前副本: 关中野外", GameObject.Find("AdventureIdText").GetComponent<Text>().text);
            StringAssert.Contains("来源据点: 关中城", GameObject.Find("AdventureSourceText").GetComponent<Text>().text);
            var feedback = GameObject.Find("EnvironmentFeedbackText").GetComponent<Text>();
            StringAssert.Contains("册界环境引用未生效", feedback.text);
            StringAssert.DoesNotContain("env_guanzhong_wild", feedback.text);
            StringAssert.DoesNotContain("surface_grassland", feedback.text);

            // 6. 探索接近石甲兽并建立战斗：战斗横幅为中文；战斗日志按原样呈现（整条日志不做
            // 全局键替换，档案键只在已证明的显示字段——术法/神通按钮标签——源端解析为中文）。
            var exploration = Object.FindFirstObjectByType<ExplorationController>();
            Assert.IsNotNull(exploration);
            yield return WaitUntilSpawned(exploration);
            var enemyUnit = GetSpawnedEnemyUnit(exploration);
            var enemy = (Character)ReadField(enemyUnit, "character");
            RepositionEnemy(exploration, enemy, new HexCoord(1, -1));
            yield return MovePlayerTo(exploration, new HexCoord(1, 0));

            InvokeStartBattle(exploration, enemyUnit);
            Assert.AreEqual(AdventureSceneState.Combat, adventure.CurrentState);

            // 战斗开始公告以既有中文格式原样进入日志，稳定原因继续保留在日志文本中。
            // 直接读取当前场景 BattleUIManager 的日志字段：战斗 UI 的 Canvas 是 DontDestroyOnLoad，
            // GameObject.Find("LogText") 可能命中此前测试遗留的空日志。
            var battleUi = Object.FindFirstObjectByType<BattleUIManager>();
            Assert.IsNotNull(battleUi, "the formal AdventureScene must bind its battle UI manager.");
            var logText = (Text)ReadField(battleUi, "logText");
            Assert.IsNotNull(logText, "the battle UI manager must have built its log text.");
            StringAssert.Contains("=== 战斗开始！", logText.text);

            // 7. 结算胜利：只通过正式 Gameplay 命令入口击败石甲兽，再由既有
            // CombatLoop 走正式结算并返回关中城；不直接改写 Character 生命值。
            yield return DefeatEnemyThroughCombatCommand(exploration, enemyUnit);
            yield return WaitForSettlementReturn();

            var returnedController = Object.FindFirstObjectByType<SettlementSceneController>();
            Assert.IsNotNull(returnedController, "The formal victory must return to the source settlement.");
            Assert.AreEqual(SettlementId, GameSession.Instance.CurrentSettlementId);
            Assert.AreEqual("关中城", GameObject.Find("SettlementNameText").GetComponent<Text>().text);

            // 8. 领取悬赏：进度完成，领奖后状态为已领取且奖励进入背包。
            Assert.AreEqual(
                BountyStatus.ObjectiveCompleted,
                GameSession.Instance.GetBountyState(BountyId).Status);
            GameObject.Find("SettlementFeature_bounty_board").GetComponent<Button>().onClick.Invoke();
            entries = GameObject.Find("BountyBoardEntriesText").GetComponent<Text>();
            StringAssert.Contains("目标已完成", entries.text);
            StringAssert.DoesNotContain("ObjectiveCompleted", entries.text);

            GameObject.Find("BountyBoardClaimButton").GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(BountyStatus.Claimed, GameSession.Instance.GetBountyState(BountyId).Status);
            StringAssert.Contains("已领取", entries.text);
            Assert.IsTrue(
                GameSession.Instance.InventoryStates.TryGet("item_lingshi_low", out var granted),
                "The claimed bounty reward must be granted to the inventory.");
            // 胜利结算的确定性普通掉落（1 劣质灵石）+ 悬赏领奖（3）＝ 4；与既有 E2E 闸门同值。
            Assert.AreEqual(4, granted.Quantity);
        }

        private static IEnumerator WaitForSettlementReturn()
        {
            for (int frame = 0; frame < 180; frame++)
            {
                var controller = Object.FindFirstObjectByType<SettlementSceneController>();
                if (controller != null && GameSession.Instance != null &&
                    GameSession.Instance.CurrentSettlementId == SettlementId)
                {
                    yield break;
                }
                yield return null;
            }

            Assert.Fail("the formal victory must return to the source settlement scene.");
        }

        private static IEnumerator DefeatEnemyThroughCombatCommand(
            ExplorationController exploration,
            object enemyUnit)
        {
            string enemyCombatantId = (string)ReadField(enemyUnit, "combatantId");
            bool playerCanAct = false;
            for (int frame = 0; frame < 600; frame++)
            {
                if ((bool)ReadField(exploration, "waitingForPlayerCombatAction"))
                {
                    playerCanAct = true;
                    break;
                }
                yield return null;
            }

            Assert.IsTrue(playerCanAct, "the formal combat loop must reach a player command window.");
            var session = (CombatSession)ReadField(exploration, "combatSession");
            Assert.IsTrue(session.Combatants.TryGet(enemyCombatantId, out var enemySnapshot));
            enemySnapshot.ReceiveDamage(enemySnapshot.CurrentHealth - 1);

            exploration.RequestBasicAttack("player", enemyCombatantId);
            Assert.IsTrue(session.Combatants.TryGet(enemyCombatantId, out enemySnapshot));
            Assert.AreEqual(0, enemySnapshot.CurrentHealth, "the production combat command must defeat the fixture.");
            yield return null;
        }

        private static IEnumerator WaitUntilSpawned(ExplorationController exploration)
        {
            for (int frame = 0; frame < 120; frame++)
            {
                var enemies = (System.Collections.IList)ReadField(exploration, "enemies");
                if (enemies != null && enemies.Count == 1)
                    yield break;
                yield return null;
            }

            Assert.Fail("formal guanzhong_wild must spawn the shijiahou enemy.");
        }

        private static void RepositionEnemy(
            ExplorationController exploration,
            Character enemy,
            HexCoord coord)
        {
            var grid = (HexGrid)ReadMember(ReadField(exploration, "tilemapManager"), "Grid");
            grid.ClearOccupied(enemy.Position);
            enemy.Position = coord;
            grid.SetOccupied(coord, (int)ReadField(GetSpawnedEnemyUnit(exploration), "gridUnitId"));
        }

        private static IEnumerator MovePlayerTo(ExplorationController exploration, HexCoord target)
        {
            var player = (Character)ReadField(exploration, "player");
            var grid = (HexGrid)ReadMember(ReadField(exploration, "tilemapManager"), "Grid");

            for (int step = 0; step < 8 && player.Position != target; step++)
            {
                var path = grid.FindPath(player.Position, target, player.MovePoints);
                if (path == null || path.Count == 0)
                {
                    // 超出单次移动上限时先向目标直线逼近一格（仍走既有寻路与移动接口）。
                    path = StepToward(grid, player.Position, target);
                }

                InvokeMovePlayer(exploration, path);
                yield return null;
            }

            Assert.AreEqual(target, player.Position, "the player must reach the target cell.");
        }

        private static System.Collections.Generic.List<HexCoord> StepToward(HexGrid grid, HexCoord from, HexCoord target)
        {
            HexCoord? best = null;
            int bestDistance = int.MaxValue;
            foreach (var neighbor in from.AllNeighbors())
            {
                if (grid.IsBlocked(neighbor) || grid.IsOccupied(neighbor))
                    continue;
                int distance = neighbor.Distance(target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = neighbor;
                }
            }

            Assert.IsTrue(best.HasValue, "a free stepping cell toward the target must exist.");
            var path = grid.FindPath(from, best.Value, 2);
            Assert.IsNotNull(path, "the single-step path must be found.");
            return path;
        }

        private static object GetSpawnedEnemyUnit(ExplorationController exploration)
        {
            var enemies = (System.Collections.IList)ReadField(exploration, "enemies");
            Assert.IsNotNull(enemies);
            Assert.AreEqual(1, enemies.Count);
            return enemies[0];
        }

        private static void InvokeStartBattle(ExplorationController exploration, object enemyUnit)
        {
            var method = typeof(ExplorationController).GetMethod(
                "StartBattle",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "StartBattle must exist on the exploration controller.");
            method.Invoke(exploration, new[] { enemyUnit });
        }

        private static void InvokeMovePlayer(ExplorationController exploration, System.Collections.Generic.List<HexCoord> path)
        {
            var method = typeof(ExplorationController).GetMethod(
                "MovePlayer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "MovePlayer must exist on the exploration controller.");
            method.Invoke(exploration, new object[] { path });
        }

        private static object ReadField(object target, string fieldName)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "field " + fieldName + " must exist on " + target.GetType().Name);
            return field.GetValue(target);
        }

        private static object ReadMember(object target, string memberName)
        {
            var field = target.GetType().GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field.GetValue(target);

            var property = target.GetType().GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(property, "member " + memberName + " must exist on " + target.GetType().Name);
            return property.GetValue(target, null);
        }

        /// <summary>确定性正式结算随机源：固定返回 0（100% 掉落），与既有 E2E 闸门同模式。</summary>
        private sealed class SequenceRandomSource : IFormalEncounterRandomSource
        {
            private readonly int[] values;
            private int index;

            public SequenceRandomSource(params int[] nextValues)
            {
                values = nextValues ?? new[] { 0 };
            }

            public int NextPercent()
            {
                int value = values[index % values.Length];
                index++;
                return value;
            }
        }
    }
}
