using System.Collections;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Features.Adventure;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TianZhang.Tests.PlayMode
{
    public sealed partial class D12ChallengeRoutesPlayModeTests
    {
        [UnityTest, Timeout(180000)]
        public IEnumerator FallenPlayerHandsControlToLivingCompanion()
        {
            yield return StartChallenge(1366, Fire, "stage1");
            var player = Actor(Player);
            player.ApplyStatus(CombatStatusKind.DamageOverTime, 2, player.MaximumHealth);
            for (int i = 0; i < 6 && player.IsAlive && !ResultOpen; i++) yield return End();

            Assert.IsFalse(player.IsAlive, "本人应在行动开始结算时倒下");
            AssertBattleContinues();
            Assert.AreEqual(Earth, encounter.ControlledActorId);
            StringAssert.Contains("同伴行动", GameObject.Find("CombatPlayerText").GetComponent<Text>().text);
            yield return Guard();
            Assert.IsTrue(Actor(Earth).IsGuarding, "玩家能从正式 HUD 指挥存活同伴");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator SimultaneousWipeShowsDefeat()
        {
            yield return StartChallenge(1366, Water, "basic");
            // Arrange both teams falling between two coordinator frames. The next update must resolve the tie.
            foreach (var actor in session.Combatants.All) actor.ReceiveDamage(actor.CurrentHealth);
            for (int i = 0; i < 120 && !ResultOpen; i++) yield return null;
            Assert.IsTrue(ResultOpen);
            Assert.AreEqual(CombatSessionOutcome.Defeat, Find<AdventureController>().LastEncounterOutcome);
            StringAssert.Contains("战斗失败", Panel);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator BasicPracticeCanWinWithoutFollowingPrompts()
        {
            yield return StartChallenge(1366, Earth, "basic");
            var rival = session.Combatants.All.Single(a => a.Team == CombatTeam.Enemy);
            var initialRivalPosition = rival.Position;
            yield return End();
            Assert.AreEqual(initialRivalPosition, rival.Position, "低压演练对手守在原位");
            if (Current.Position.TopologicalDistanceTo(rival.Position) > 1) yield return Approach(rival);
            Assert.That(Current.Position, Is.Not.EqualTo(new TianZhang.Spatial.HexCoord(4, 1)));
            // Isolate the last-hit boundary. The high-ground test plays the full fight at normal health.
            rival.ReceiveDamage(rival.CurrentHealth - 1);
            for (int i = 0; i < 8 && rival.IsAlive; i++)
            {
                var hit = new CombatCommand(CombatCommandKind.BasicAttack, Current.Id, rival.Id, Current.BasicAttackProfileId);
                if (encounter.PreviewJindan(hit).IsLegal) yield return Attack(Current.BasicAttackProfileId, rival.Position);
                else yield return End();
            }
            Assert.IsFalse(rival.IsAlive);
            StringAssert.Contains("战斗胜利", Panel);
            yield return Press("retry");
            yield return Entered();
            var controller = Find<AdventureController>();
            Assert.AreEqual(CombatSessionOutcome.Ongoing, controller.LastEncounterOutcome);
            yield return Named("ReturnButton");
            yield return Named("ConfirmWithdrawalButton");
            yield return Ready();
            StringAssert.Contains("已撤离", Panel);
            Assert.AreEqual(CombatSessionOutcome.Ongoing, controller.LastEncounterOutcome);
        }
    }
}
