using System.Collections;
using System.Linq;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Features.CombatPresentation;
using TianZhang.Spatial;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests.PlayMode
{
    public sealed partial class D12ChallengeRoutesPlayModeTests
    {
        [UnityTest, Timeout(300000)]
        public IEnumerator HighGroundSoloVictoryDoesNotAdvanceBounty([ValueSource(nameof(Widths))] int width)
        {
            yield return StartChallenge(width, Fire, "stage1", acceptBounty: true);
            var enemy = session.Combatants.All.Single(a => a.Team == CombatTeam.Enemy);
            // Fight with the saved character and the fixed high-ground opponent.
            for (int action = 0; action < 60 && enemy.IsAlive && !ResultOpen; action++)
            {
                if (Current.Position.TopologicalDistanceTo(enemy.Position) > 1)
                    yield return Approach(enemy);
                Assert.IsTrue(enemy.IsAlive, "接敌时目标应仍存活");
                var attack = new CombatCommand(CombatCommandKind.BasicAttack, Current.Id, enemy.Id, Current.BasicAttackProfileId);
                if (encounter.PreviewJindan(attack).IsLegal)
                {
                    int hpBefore = enemy.CurrentHealth;
                    yield return Attack(Current.BasicAttackProfileId, enemy.Position);
                    Record($"{attack.ActorId} 普攻后，{enemy.Id} HP {hpBefore}→{enemy.CurrentHealth}");
                }
                else
                {
                    yield return End();
                    Assert.IsTrue(enemy.IsAlive, "最后一击须来自我方普攻");
                }
            }
            Assert.IsFalse(enemy.IsAlive, "玩家通过正式战斗界面普攻击倒最后一个敌人");
            yield return Victory();
            var order = GameBootstrap.RequireRuntime().Bounties.GetState("bounty_guanzhong_shijiahou");
            Assert.AreEqual(TianZhang.World.BountyStatus.Accepted, order.Status);
            Assert.AreEqual(0, order.Progress, "阶地歼敌不推进关中城悬赏");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator TidalPassageByWater([ValueSource(nameof(Widths))] int width)
        {
            yield return StartChallenge(width, Water, "stage2");
            yield return UntilActor(Player);
            var burning = new HexCoord(5, 6);
            Assert.AreEqual(CombatSurfaceKind.Burning, session.Surfaces.Get(burning).Kind);
            yield return TideOn(burning);
            yield return Capture("01-extinguished-route");
            yield return Move(burning);
            AssertBattleContinues();
        }

        private IEnumerator TideOn(HexCoord burning)
        {
            var choice = (from c in surface.Cells
                let cell = new HexCoord(c.q, c.r)
                orderby Current.Position.TopologicalDistanceTo(cell), cell.Q, cell.R
                from eye in new[] {false, true}
                let request = new CombatJindanRequest(Seat("WATER_TIDE").Definition.SeatId,
                    tideAction: CombatTideAction.Rise, tideSource: cell, createTideEye: eye)
                let command = Jindan("WATER_TIDE", request)
                let quote = encounter.PreviewJindan(command)
                where quote.IsLegal && quote.Tide.Changes.Any(change => change.Cell == burning &&
                    change.Before == CombatSurfaceKind.Burning && change.After == CombatSurfaceKind.Wet)
                select (cell, eye, command)).First();
            yield return Use("tide_tab", choice.eye ? "tide_eye" : "tide_rise", choice.command, choice.cell);
            Assert.AreEqual(CombatSurfaceKind.Wet, session.Surfaces.Get(burning).Kind);
        }

        private IEnumerator Approach(CombatantSnapshot enemy)
        {
            var cell = enemy.Position.Neighbors().Where(c => Movement(c).IsLegal)
                .OrderBy(c => Movement(c).ActionPointCostUnits).ThenBy(c => c.Q).ThenBy(c => c.R).First();
            yield return Move(cell);
            Assert.That(Current.Position.TopologicalDistanceTo(enemy.Position), Is.LessThanOrEqualTo(1));
        }

    }
}
