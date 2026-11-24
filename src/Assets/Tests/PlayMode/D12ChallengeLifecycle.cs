using System.Collections;
using System.Linq;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using TianZhang.Spatial;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TianZhang.Tests.PlayMode
{
    public sealed partial class D12ChallengeRoutesPlayModeTests
    {
        [UnityTest, Timeout(300000)]
        public IEnumerator BasicPracticeWithdrawRetryReloadAndContinue([ValueSource(nameof(Widths))] int width)
        {
            yield return StartChallenge(width, Water, "basic");
            var bootstrap = GameBootstrap.RequireInstance();
            Assert.IsFalse(bootstrap.SaveActiveSlot().Succeeded, "只允许在挑战外存档");
            string initial = EntranceState();
            yield return Move(new HexCoord(4, 1));
            yield return Attack(Star, new HexCoord(5, 1));
            Assert.IsFalse(ResultOpen, "施法后应继续交战");
            yield return Named("ReturnButton");
            yield return Named("ConfirmWithdrawalButton");
            yield return Ready();
            Assert.IsTrue(ResultOpen);
            StringAssert.Contains("已撤离", Panel);
            Assert.AreEqual("关中阶地 · 战斗结算", Heading());
            yield return Capture("01-withdrawn");
            yield return Press("retry");
            yield return Entered();
            Assert.AreEqual(initial, EntranceState(), "相同开局、种子和版本重试应恢复相同局面");
            yield return Named("ReturnButton");
            yield return Named("ConfirmWithdrawalButton");
            yield return Ready();
            yield return Press("choose_challenge");
            yield return Press("stage2");
            Assert.That(Panel, Does.Contain("同伴").And.Contain("由你操控")
                .And.Contain("提示").And.Contain("敌方全灭").And.Contain("我方全灭"));
            Assert.That(Panel, Does.Not.Contain("_seat"));
            yield return Press("prep_details");
            Assert.That(Panel, Does.Contain("金丹源位 · 渊蓄池").And.Contain("演武·蓄澜")
                .And.Contain("250 AP / 400 MP").And.Contain("秋水游心经"));
            Assert.AreEqual("关中阶地 · 出战角色", Heading());
            Assert.That(Panel, Does.Not.Contain("player:").And.Not.Contain("demo_").And.Not.Contain("JD_"));
            yield return Capture("02-details");
            yield return Press("prep_details");
            yield return Press("prep_save");
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu); yield return null;
            Assert.IsTrue(Find<StartMenuSceneInstaller>().LoadPlayer("route").Succeeded);
            yield return Scene(GameplaySceneNames.Adventure);
            view = Find<JindanDemoView>();
            Assert.IsTrue(view.GetButton("enter").gameObject.activeInHierarchy);
            Assert.AreEqual("关中阶地 · 战前准备", Heading());
            Assert.IsFalse(Find<TianZhang.Features.Adventure.EncounterCoordinator>().IsRunning);
            Assert.AreEqual(savedPlayer, JsonUtility.ToJson(GameBootstrap.RequireRuntime().CaptureSave().player));
            yield return Press("basic");
            yield return Press("enter");
            yield return Entered();
            Assert.AreEqual(initial, EntranceState(), "读档后重开须清空临时态");
            yield return Move(new HexCoord(4, 1));
            yield return Attack(Star, new HexCoord(5, 1));
            yield return Capture("03-basic-action");
            yield return End();
            AssertBattleContinues();
            StringAssert.Contains("操作练习已完成", Find<CombatBountyTrackerView>().Summary);
            yield return Capture("04-practice-complete-battle-continues");
            yield return End();
            AssertBattleContinues();
        }

        private static string Heading() => GameObject.Find("JindanPreparationDialog/Heading")
            .GetComponent<UnityEngine.UI.Text>().text;

        [UnityTest, Timeout(300000)]
        public IEnumerator FireDefeatAndRetry([ValueSource(nameof(Widths))] int width)
        {
            yield return StartChallenge(width, Fire, "stage2");
            yield return UntilActor(Player);
            string initial = EntranceState();
            yield return Move(Center);
            yield return Use("fire_tab", "fire_preview", Jindan("FIRE_MANIFEST"));
            yield return Capture("01-fire-unguarded");
            for (int turn = 0; turn < 20 && !ResultOpen; turn++) yield return End();
            Assert.IsTrue(ResultOpen, "持续结束行动后，敌方应能歼灭守方");
            Assert.IsFalse(session.Combatants.HasLivingMembers(CombatTeam.Player));
            StringAssert.Contains("战斗失败", Panel);
            yield return Capture("02-defeat");
            yield return Press("retry");
            yield return Entered();
            Assert.AreEqual(initial, EntranceState());
        }

        private string EntranceState()
        {
            return string.Join("|", session.Combatants.All.Select(a =>
                $"{a.Id},{a.Team},{a.Position},{a.CurrentHealth},{a.CurrentSpirit},{a.ActionPointsRemainingUnits},{a.AltitudeBand}")) +
                "|" + string.Join("|", session.Jindan.Seats.Select(s =>
                    $"{s.Definition.ActorId},{s.EffectId},{s.SuccessfulDeployments},{s.TotalManaSpent}")) +
                $"|{session.TurnScheduler.CurrentTick},{session.CurrentActorId}";
        }
    }
}
