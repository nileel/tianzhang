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
        public IEnumerator BasicPracticeWithdrawRetryReloadAndWin([ValueSource(nameof(Widths))] int width)
        {
            yield return StartChallenge(width, Water, "basic");
            var bootstrap = GameBootstrap.RequireInstance();
            Assert.IsFalse(bootstrap.SaveActiveSlot().Succeeded, "只允许在挑战外存档");
            string initial = EntranceState();
            yield return Move(new HexCoord(4, 1));
            yield return Attack(Star, new HexCoord(5, 1));
            Assert.IsFalse(ResultOpen, "还须主动结束本人回合");
            yield return Named("ReturnButton");
            yield return Named("ConfirmWithdrawalButton");
            yield return Ready();
            Assert.IsTrue(ResultOpen);
            StringAssert.Contains("已撤离", Panel);
            yield return Capture("01-withdrawn");
            yield return Press("retry");
            yield return Entered();
            Assert.AreEqual(initial, EntranceState(), "相同开局、种子和版本重试应恢复相同局面");
            yield return Named("ReturnButton");
            yield return Named("ConfirmWithdrawalButton");
            yield return Ready();
            yield return Press("choose_challenge");
            yield return Press("stage2");
            Assert.That(Panel, Does.Contain("同伴").And.Contain("由你操控").And.Contain("突破方")
                .And.Contain("先做").And.Contain("获胜").And.Contain("失败"));
            Assert.That(Panel, Does.Not.Contain("_seat"));
            yield return Press("prep_details");
            Assert.That(Panel, Does.Contain("JD_FiveElements_Water_SOURCE").And.Contain("来源"));
            yield return Capture("02-details");
            yield return Press("prep_details");
            yield return Press("prep_save");
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu); yield return null;
            Assert.IsTrue(Find<StartMenuSceneInstaller>().LoadPlayer("route").Succeeded);
            yield return Scene(GameplaySceneNames.Adventure);
            view = Find<JindanDemoView>();
            Assert.IsTrue(view.GetButton("enter").gameObject.activeInHierarchy);
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
            yield return Victory();
        }

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
            Assert.IsTrue(ResultOpen, "防守方不保护阵心时，真实对手应能造成失败");
            StringAssert.Contains("挑战失败", Panel);
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
