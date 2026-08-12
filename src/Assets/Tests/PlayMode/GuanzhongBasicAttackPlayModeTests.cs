using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Adventure;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Game;
using TianZhang.Game.CharacterCreation;
using TianZhang.Map;
using UnityEngine;
using UnityEngine.TestTools;

using TianZhang.Spatial;

namespace TianZhang.Tests
{
    /// <summary>
    /// 正式关中野外基础攻击闸门（D-COMBAT-PROD-01）：从空会话用已批准创建规则生成新玩家，
    /// 进入正式 guanzhong_wild，接近石甲兽建立会话，双方在相邻格消费同一 basic_unarmed 档案
    /// 执行基础攻击。只驱动既有正式生产入口与公开控制器 API；不复制遭遇、会话、绑定或攻击
    /// 实现，也不在战斗调用点补默认档案。
    /// 环境边说明：负责人范围决定（2026-08-06）已授权把 env_guanzhong_wild 既有相邻格的反向
    /// 有向边纳入本卡路径（EnvironmentProfiles.csv 共 12 条，保留原 6 条并补齐反向 6 条）。
    /// 测试把石甲兽摆到受击格 (1,-1)，沿真实移动把玩家走到攻击源 (1,0)，两格间正反方向均为
    /// 已声明边，因此玩家与石甲兽在相邻格均完成基础攻击，且不返回三类基础攻击装配错误。
    /// </summary>
    public sealed class GuanzhongBasicAttackPlayModeTests
    {
        private const string AdventureId = "guanzhong_wild";
        private const string SettlementId = "guanzhong_city";
        private const string BasicUnarmedProfileId = "basic_unarmed";

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
        public IEnumerator FormalGuanzhongWildEncounterConsumesTheBasicUnarmedProfileOnBothSides()
        {
            flowGo = new GameObject("GuanzhongBasicAttackFlow");
            var flow = flowGo.AddComponent<SceneFlowManager>();
            Assert.IsNotNull(GameSession.Instance, "SceneFlowManager must ensure the single session.");

            var playerProfile = CharacterCreationRules.BuildCharacterData(
                CharacterCreationCatalog.CreateDefaultDraft());
            GameSession.Instance.BeginNewGame(playerProfile, "jiangzuo_hub");

            flow.EnterAdventure(AdventureId, SceneReturnTarget.Settlement(SettlementId));
            yield return null;

            var adventure = Object.FindFirstObjectByType<AdventureSceneController>();
            Assert.IsNotNull(adventure, "The formal AdventureScene must bind its adventure controller.");
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState,
                "The formal guanzhong_wild entry must reach exploration.");

            var exploration = Object.FindFirstObjectByType<ExplorationController>();
            Assert.IsNotNull(exploration);
            Assert.IsNotNull(exploration.attackProfiles,
                "The formal scene must serialize the production attack profile reference.");
            Assert.AreEqual(1, exploration.attackProfiles.Length);
            Assert.AreEqual(BasicUnarmedProfileId, exploration.attackProfiles[0].attackProfileId);

            // 等待探索初始化完成（玩家与石甲兽都已生成）。
            yield return WaitUntilSpawned(exploration);

            var player = (Character)ReadField(exploration, "player");
            Assert.AreEqual(BasicUnarmedProfileId, player.BasicAttackProfileId);
            Assert.AreEqual("unarmed_fallback", player.BasicAttackBindingKind);

            var enemyUnit = GetSpawnedEnemyUnit(exploration);
            var enemy = (Character)ReadField(enemyUnit, "character");
            Assert.AreEqual("enemy_shijiahou", ((EnemyData)ReadField(enemyUnit, "enemyData")).enemyId);
            Assert.AreEqual(BasicUnarmedProfileId, enemy.BasicAttackProfileId);
            Assert.AreEqual("unarmed_fallback", enemy.BasicAttackBindingKind);

            // 接近石甲兽：沿既有寻路逐步移动到其相邻空格（正式点击路径的移动部分）。
            yield return MovePlayerToNeighbor(exploration, enemy);

            // 相邻格摆位：把石甲兽移到受击格 (1,-1)，玩家沿真实移动走到攻击源 (1,0)。
            RepositionEnemy(exploration, enemy, new HexCoord(1, -1));
            yield return MovePlayerTo(exploration, new HexCoord(1, 0));

            // 建立会话（等价于正式点击相邻石甲兽）。
            InvokeStartBattle(exploration, enemyUnit);
            Assert.AreEqual(AdventureSceneState.Combat, adventure.CurrentState);

            var session = (CombatSession)ReadField(exploration, "combatSession");
            Assert.IsNotNull(session, "The formal encounter must establish a CombatSession.");
            Assert.AreEqual(2, session.Combatants.All.Count);
            Assert.IsTrue(session.TryGetProfile(BasicUnarmedProfileId, out var basicProfile));
            Assert.AreEqual(CombatAttackKind.Basic, basicProfile.Kind);

            // 玩家侧只通过正式 Gameplay 请求入口执行，不回落到遗留控制器。
            var service = (CombatCommandService)ReadField(exploration, "combatCommandService");
            for (int step = 0; step < 5 && !session.TurnScheduler.IsReady("player"); step++)
                service.AdvanceUntilAction(session);
            Assert.IsTrue(session.TurnScheduler.IsReady("player"));
            SetField(exploration, "waitingForPlayerCombatAction", true);
            string enemyId = (string)ReadField(enemyUnit, "combatantId");
            int enemyHealthBefore = session.Combatants.TryGet(enemyId, out var enemySnapshot)
                ? enemySnapshot.CurrentHealth
                : -1;
            exploration.RequestBasicAttack("player", enemyId);
            Assert.Less(session.Combatants.TryGet(enemyId, out enemySnapshot) ? enemySnapshot.CurrentHealth : int.MaxValue, enemyHealthBefore);
        }

        private static IEnumerator WaitUntilSpawned(ExplorationController exploration)
        {
            for (int frame = 0; frame < 120; frame++)
            {
                var enemies = (IList)ReadField(exploration, "enemies");
                if (enemies != null && enemies.Count == 1)
                    yield break;
                yield return null;
            }

            Assert.Fail("formal guanzhong_wild must spawn the shijiahou enemy.");
        }

        private static void RepositionEnemy(ExplorationController exploration, Character enemy, HexCoord coord)
        {
            var grid = (HexGrid)ReadMember(ReadField(exploration, "tilemapManager"), "Grid");
            grid.ClearOccupied(enemy.Position);
            enemy.Position = coord;
            grid.SetOccupied(coord, (int)ReadField(GetSpawnedEnemyUnit(exploration), "gridUnitId"));
        }

        private static IEnumerator MovePlayerToNeighbor(ExplorationController exploration, Character enemy)
        {
            var grid = (HexGrid)ReadMember(ReadField(exploration, "tilemapManager"), "Grid");
            var target = FirstFreeNeighbor(grid, enemy.Position);
            Assert.IsNotNull(target, "the enemy must have a free adjacent cell for the player.");
            yield return MovePlayerTo(exploration, target.Value);
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

        private static List<HexCoord> StepToward(HexGrid grid, HexCoord from, HexCoord target)
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

        private static HexCoord? FirstFreeNeighbor(HexGrid grid, HexCoord position)
        {
            foreach (var neighbor in position.AllNeighbors())
            {
                if (!grid.IsBlocked(neighbor) && !grid.IsOccupied(neighbor))
                    return neighbor;
            }

            return null;
        }

        private static object GetSpawnedEnemyUnit(ExplorationController exploration)
        {
            var enemies = (IList)ReadField(exploration, "enemies");
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

        private static void InvokeMovePlayer(ExplorationController exploration, List<HexCoord> path)
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

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "field " + fieldName + " must exist on " + target.GetType().Name);
            field.SetValue(target, value);
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
    }
}
