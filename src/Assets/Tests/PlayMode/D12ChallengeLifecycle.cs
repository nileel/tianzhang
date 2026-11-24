using System.Collections;
using System.Linq;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Entity;
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
            Assert.That(Panel, Does.Contain("独自行动").And.Contain("敌方全灭").And.Contain("我方全灭"));
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

        private static readonly string[] CharacterSaves = { "ordinary", "legacy", Earth, Water, Fire, Yang, Yin };

        [UnityTest, Timeout(300000)]
        public IEnumerator TerraceEntryUsesSavedOwnerAndIndependentCosts([ValueSource(nameof(CharacterSaves))] string save)
        {
            yield return StartChallenge(1366, save, "basic");
            var runtime = GameBootstrap.RequireRuntime();
            var owner = runtime.Player.Capture();
            var demo = Resources.FindObjectsOfTypeAll<JindanDemoCatalogData>().First(c => c.presets?.Length == 5);
            var stages = new[] { "basic", "stage1", "stage2", "stage3" };
            for (int i = 0; i < stages.Length; i++)
            {
                if (i > 0)
                {
                    yield return Press(stages[i]);
                    StringAssert.Contains(i == 1 || i == 3 ? "裂石甲兽" : "石甲兽", Panel);
                    yield return Press("enter");
                    yield return Entered();
                }
                var player = Actor(Player);
                Assert.AreEqual(Player, encounter.ControlledActorId);
                var hud = Find<CombatHudPresenter>().Snapshot;
                Assert.AreEqual(playerName, hud.Player.DisplayName);
                StringAssert.Contains(i == 1 || i == 3 ? "裂石甲兽" : "石甲兽", hud.Enemies.Single().DisplayName);
                Assert.AreEqual(1, session.Combatants.All.Count(a => a.Team == CombatTeam.Player));
                Assert.AreEqual(1, session.Combatants.All.Count(a => a.Team == CombatTeam.Enemy));
                Assert.AreEqual(owner.Progression.RealmStage, player.RealmStage);
                CollectionAssert.AreEqual(owner.AbilityLoadout.EquippedSpells, player.EquippedArtProfileIds);
                Assert.AreEqual(string.IsNullOrEmpty(owner.MainEquipmentBasicAttackProfileId)
                    ? owner.UnarmedBasicAttackProfileId : owner.MainEquipmentBasicAttackProfileId, player.BasicAttackProfileId);
                Assert.AreEqual(144, surface.Cells.Count);
                if (string.IsNullOrEmpty(runtime.JindanDemoPresetId))
                {
                    Assert.IsNull(session.Jindan);
                    Assert.IsNull(session.Flight);
                }
                else
                {
                    var preset = demo.GetPreset(save);
                    CollectionAssert.AreEquivalent(preset.seatInstanceIds.Select(id => JindanDemoCatalogData.PlayerInstanceId(save, id)),
                        session.Jindan.Seats.Select(seat => seat.Definition.SeatId));
                    Assert.IsTrue(session.Jindan.Seats.All(seat => seat.Definition.ActorId == Player));
                    Assert.AreEqual(string.IsNullOrEmpty(preset.flightAbilityInstanceId), session.Flight?.Find(Player) == null);
                }
                if (i == 0)
                {
                    var enemy = Actor("enemy");
                    int enemySpirit = enemy.CurrentSpirit;
                    int beforeAP = player.ActionPointsRemainingUnits, beforeMP = player.CurrentSpirit;
                    if (player.EquippedArtProfileIds.Contains(Star))
                    {
                        yield return Attack(Star, new HexCoord(5, 1));
                        Assert.AreEqual(beforeMP - 10, player.CurrentSpirit);
                    }
                    else
                    {
                        yield return Move(new HexCoord(4, 1));
                        Assert.AreEqual(beforeMP, player.CurrentSpirit);
                    }
                    Assert.Less(player.ActionPointsRemainingUnits, beforeAP);
                    Assert.AreEqual(enemySpirit, enemy.CurrentSpirit);
                }
                yield return WithdrawToResult();
                yield return Press("choose_challenge");
                StringAssert.Contains("独自行动", Panel);
                Assert.AreEqual(savedPlayer, JsonUtility.ToJson(runtime.CaptureSave().player));
                Assert.AreEqual(savedCultivation, JsonUtility.ToJson(runtime.CaptureSave().cultivation));
            }
        }

        private IEnumerator WithdrawToResult()
        {
            yield return Named("ReturnButton");
            yield return Named("ConfirmWithdrawalButton");
            yield return Ready();
            Assert.IsTrue(ResultOpen);
            StringAssert.Contains("已撤离", Panel);
            Assert.IsNull(encounter.CurrentSession);
        }

        private static readonly string[] VisibilityPresets = { Yang, Yin };

        [UnityTest, Timeout(120000)]
        public IEnumerator VisibilityChargesOnlyItsOwnSeat([ValueSource(nameof(VisibilityPresets))] string preset)
        {
            yield return StartChallenge(1366, preset, "stage3");
            string effect = preset == Yang ? CombatVisibility.Yang : CombatVisibility.Yin;
            var seat = Seat(effect);
            var otherSeat = session.Jindan.Seats.Single(other => other != seat);
            var center = session.Jindan.Ability(effect).Visibility.Center;
            var command = Jindan(effect, new CombatJindanRequest(seat.Definition.SeatId, selectedTargetCell: center));
            if (!encounter.PreviewJindan(command).IsLegal) yield return Move(center);
            var quote = encounter.PreviewJindan(command);
            int enemyMana = Actor("enemy").CurrentSpirit;
            yield return Use("visibility_tab", "visibility_deploy", command, center);
            Assert.IsNotNull(seat.ActiveVisibility);
            Assert.AreEqual(quote.SpiritCost, seat.TotalManaSpent);
            Assert.AreEqual(quote.ActionPointCostUnits, seat.TotalActionPointSpent);
            Assert.AreEqual(0, otherSeat.TotalManaSpent);
            Assert.AreEqual(0, otherSeat.TotalActionPointSpent);
            Assert.AreEqual(enemyMana, Actor("enemy").CurrentSpirit);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator OrdinaryTerraceFireCanBeInspectedAndClearsOnWithdrawal()
        {
            yield return StartChallenge(1366, "ordinary", "stage2");
            var floor = session.Surfaces;
            var burning = new HexCoord(5, 6);
            Assert.AreEqual(CombatSurfaceKind.Burning, floor.Get(burning).Kind);
            yield return Pick(burning);
            StringAssert.Contains("燃烧", Find<CombatTargetView>().DetailsText);
            yield return Click(Find<CombatTargetView>().CloseButton);
            yield return WithdrawToResult();
            Assert.AreEqual(CombatSurfaceKind.None, floor.Get(burning).Kind);
            Assert.IsTrue(floor.FireSources.All(fire => fire.RemainingTicks == 0));
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator FireDomainClearsOnRetryTownReturnAndLoad()
        {
            yield return StartChallenge(1366, Fire, "basic");
            string initial = EntranceState();
            yield return OpenPlayerFire();
            yield return WithdrawToResult();
            yield return Press("retry");
            yield return Entered();
            Assert.AreEqual(initial, EntranceState());
            Assert.IsNull(Seat("FIRE_MANIFEST").FireDomain.Main);
            yield return OpenPlayerFire();
            yield return WithdrawToResult();
            yield return Press("choose_challenge");
            yield return Press("cancel_preparation");
            yield return Scene(GameplaySceneNames.Settlement);
            yield return Named("SettlementTerraceButton");
            yield return Scene(GameplaySceneNames.Adventure);
            view = Find<JindanDemoView>();
            yield return Press("basic");
            yield return Press("prep_save");
            yield return Press("enter");
            yield return Entered();
            Assert.AreEqual(initial, EntranceState());
            Assert.IsNull(Seat("FIRE_MANIFEST").FireDomain.Main);
            yield return OpenPlayerFire();
            yield return WithdrawToResult();
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu); yield return null;
            Assert.IsTrue(Find<StartMenuSceneInstaller>().LoadPlayer("route").Succeeded);
            yield return Scene(GameplaySceneNames.Adventure);
            view = Find<JindanDemoView>();
            Assert.IsFalse(Find<TianZhang.Features.Adventure.EncounterCoordinator>().IsRunning);
            yield return Press("basic");
            yield return Press("enter");
            yield return Entered();
            Assert.AreEqual(initial, EntranceState());
            Assert.IsNull(Seat("FIRE_MANIFEST").FireDomain.Main);
            Assert.AreEqual(savedPlayer, JsonUtility.ToJson(GameBootstrap.RequireRuntime().CaptureSave().player));
            Assert.AreEqual(savedCultivation, JsonUtility.ToJson(GameBootstrap.RequireRuntime().CaptureSave().cultivation));
            yield return Capture("03-reloaded-without-domain");
        }

        private IEnumerator OpenPlayerFire()
        {
            var seat = Seat("FIRE_MANIFEST");
            var landing = new HexCoord(5, 1);
            var request = new CombatJindanRequest(seat.Definition.SeatId, selectedTargetCell: landing);
            var quote = encounter.PreviewJindan(Jindan("FIRE_MANIFEST", request));
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            yield return Press("fire_tab"); yield return Press("fire_preview"); yield return Pick(landing);
            yield return Pay(quote, "展开长明");
            Assert.AreEqual(landing, seat.FireDomain.Support.Cell);
            Assert.NotNull(seat.FireDomain.Main);
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
