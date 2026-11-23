using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Features.CombatPresentation;
using TianZhang.Spatial;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace TianZhang.Tests.PlayMode
{
    public sealed partial class D12ChallengeRoutesPlayModeTests
    {
        [UnityTest, Timeout(300000)]
        public IEnumerator HighGroundByStairsAndAirAttack([ValueSource(nameof(Widths))] int width)
        {
            yield return StartChallenge(width, Earth, "stage1");
            yield return UntilActor(Player);
            var enemy = session.Combatants.All.Single(a => a.Team == CombatTeam.Enemy);
            for (int turns = 0; turns < 3 && enemy.AltitudeBand == 0; turns++) yield return UntilNext(Player);
            Assert.Greater(enemy.AltitudeBand, 0, "对手须实际起飞");
            var path = Movement(High);
            Assert.IsTrue(path.IsLegal, path.RejectionReason);
            Assert.IsTrue(path.MovementSteps.Any(step => surface.Cells.Any(c => c.q == step.To.Anchor.Q &&
                c.r == step.To.Anchor.R && c.kind != 0)), "登高路径必须经过坂道或阶梯");
            yield return Move(High);
            yield return Capture("01-ground-ascent");
            var health = enemy.CurrentHealth;
            yield return Attack(Current.BasicAttackProfileId, enemy.Position);
            Assert.Less(enemy.CurrentHealth, health, "空中对手应实际受到攻击");
            yield return Victory();
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator HighGroundByFlightAndHeavyLanding([ValueSource(nameof(Widths))] int width)
        {
            yield return StartChallenge(width, Earth, "stage1");
            yield return UntilActor(Player);
            var enemy = session.Combatants.All.Single(a => a.Team == CombatTeam.Enemy);
            for (int turns = 0; turns < 3 && enemy.AltitudeBand == 0; turns++) yield return UntilNext(Player);
            Assert.Greater(enemy.AltitudeBand, 0);
            yield return Fly(CombatFlightAction.Takeoff);
            Assert.Greater(Actor(Player).AltitudeBand, 0);
            yield return Move(High);
            yield return Capture("01-flight-arrival");
            yield return Fly(CombatFlightAction.Land);
            if (Current.ActionPointsRemainingUnits < 250000) yield return UntilNext(Player);
            int altitude = enemy.AltitudeBand;
            var heavy = new CombatJindanRequest(Seat("EARTH_GRAVITY").Definition.SeatId,
                earthMode: CombatEarthMode.Heavy, earthTargetId: enemy.Id, selectedTargetCell: enemy.Position);
            yield return Use("earth_tab", "earth_heavy", Jindan("EARTH_GRAVITY", heavy), enemy.Position);
            Assert.Less(enemy.AltitudeBand, altitude);
            yield return Victory();
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
            yield return Victory();
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

        [UnityTest, Timeout(300000)]
        public IEnumerator TidalPassageByBreakingCenter([ValueSource(nameof(Widths))] int width)
        {
            yield return StartChallenge(width, Earth, "stage2");
            var fire = Seat("FIRE_MANIFEST");
            for (int turns = 0; turns < 12 && fire.MaintenancePayments == 0; turns++) yield return End();
            Assert.Greater(fire.MaintenancePayments, 0, "有实际维护的火域才可反制");
            yield return UntilActor(Player);
            var portrait = GameObject.Find("CombatPlayerPortrait").GetComponent<UnityEngine.UI.Image>();
            var playerPortrait = portrait.sprite;
            Assert.IsNotNull(playerPortrait);
            yield return Fly(CombatFlightAction.Takeoff);
            yield return Move(new HexCoord(7, 11));
            yield return Fly(CombatFlightAction.Land);
            Assert.AreEqual(600, fire.FireDomain.Support.Durability);
            yield return Attack(Demolish, Center);
            Assert.AreEqual(225, fire.FireDomain.Support.Durability);
            yield return Attack(Demolish, Center);
            Assert.AreEqual(0, fire.FireDomain.Support.Durability);
            var tail = fire.FireDomain.Tails.SelectMany(t => t.Cells).First();
            Assert.AreEqual(CombatSurfaceKind.Burning, session.Surfaces.Get(tail).Kind);
            yield return Capture("01-center-broken-tail-burning");
            yield return UntilActor(Water);
            var waterPortrait = view.GetButton("tide_tab").transform.Find("ActionIcon").GetComponent<UnityEngine.UI.Image>().sprite;
            Assert.AreSame(waterPortrait, portrait.sprite, "行动者切至具名蓄澜同伴");
            Assert.AreNotSame(playerPortrait, portrait.sprite);
            yield return Move(new HexCoord(8, 7));
            yield return TideOn(tail);
            yield return Capture("02-paid-tail-extinguished");
            yield return Move(tail);
            yield return Victory();
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator GateByDetourAndNearSight([ValueSource(nameof(Widths))] int width)
        {
            yield return StartChallenge(width, Yang, "stage3");
            var target = Actor(Yin);
            yield return UntilActor(Player);
            AssertVisibleRoleLabels();
            Assert.NotNull(Seat("SPACE_SEAL").ActiveSeal);
            yield return Move(High);
            var detour = Movement(GateTo);
            Assert.Greater(detour.MovementSteps.Count, 1);
            Assert.IsFalse(detour.MovementSteps.Any(s => s.From.Anchor == High && s.To.Anchor == GateTo));
            yield return Move(GateTo);
            yield return Capture("01-around-seal");
            if (!ResultOpen) yield return Approach(target);
            yield return Victory();
        }

        private IEnumerator Approach(CombatantSnapshot enemy)
        {
            var cell = enemy.Position.Neighbors().Where(c => Movement(c).IsLegal)
                .OrderBy(c => Movement(c).ActionPointCostUnits).ThenBy(c => c.Q).ThenBy(c => c.R).First();
            yield return Move(cell);
            Assert.That(Actor(Player).Position.TopologicalDistanceTo(enemy.Position), Is.LessThanOrEqualTo(1));
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator GateByAnchorAndSunObservation([ValueSource(nameof(Widths))] int width)
        {
            yield return StartChallenge(width, Yang, "stage3");
            yield return UntilActor(Player);
            var a = Current.Position;
            var edge = new[] { a, a.Step(0) };
            yield return Press("scale_tab"); yield return Press("compress"); yield return Press("pick_edges");
            foreach (var cell in edge) yield return Pick(cell);
            yield return Pay(encounter.PreviewJindan(Jindan("SPACE_SCALE",
                new CombatJindanRequest(Seat("SPACE_SCALE").Definition.SeatId, edge, 1))), "付费定尺");
            yield return Use("visibility_tab", "visibility_deploy", Jindan(CombatVisibility.Yang));
            yield return WinObservation();
            yield return Capture("01-sun-observation");
            yield return UntilNext(Player);
            yield return Move(High);
            var seal = Seat("SPACE_SEAL").ActiveSeal;
            Assert.NotNull(seal);
            Assert.AreEqual(200, seal.Support.Durability);
            for (int i = 0; i < 7; i++) yield return Attack(Stone, GateTo);
            Assert.AreEqual(0, seal.Support.Durability);
            var opening = Movement(GateTo);
            Assert.AreEqual(1, opening.MovementSteps.Count);
            yield return Capture("02-anchor-open");
            yield return Move(GateTo);
            yield return Victory();
        }

        private IEnumerator WinObservation()
        {
            var conflict = session.Visibility.Current;
            Assert.NotNull(conflict, "付费阳照和藏真必须导致实际观察冲突");
            if (conflict.IsPending)
            {
                yield return Keys(Key.Space);
                for (int pulse = 1; pulse <= 2; pulse++)
                {
                    float limit = Time.realtimeSinceStartup + 3;
                    while (conflict.IsPending && !(conflict.WindowOpen && conflict.PulseCount == pulse) &&
                        Time.realtimeSinceStartup < limit) yield return null;
                    Assert.IsTrue(conflict.WindowOpen, "等待显隐键窗 " + pulse);
                    yield return Keys(Key.Space, pulse == 1 ? Key.A : Key.D);
                    yield return Keys(Key.Space);
                }
                yield return Keys();
            }
            Assert.AreEqual(conflict.PlayerSide == 1 ? CombatConflictOutcome.Left : CombatConflictOutcome.Right,
                conflict.Outcome);
            Record("显隐结算 " + string.Join(" | ", conflict.Log));
            yield return Ready();
        }
        private IEnumerator Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); yield return null;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator DefendFireByMaintaining([ValueSource(nameof(Widths))] int width) => DefendFire(width, false);
        [UnityTest, Timeout(300000)]
        public IEnumerator DefendFireByClosingAndRekindling([ValueSource(nameof(Widths))] int width) => DefendFire(width, true);

        private IEnumerator Guard()
        {
            yield return Click(Find<CombatActionBarView>().GuardButton);
            yield return Pay(encounter.PreviewJindan(new CombatCommand(CombatCommandKind.Guard, Player)), "防御");
        }

        private IEnumerator DefendFire(int width, bool close)
        {
            yield return StartChallenge(width, Fire, "stage2");
            yield return UntilActor(Player);
            var seat = Seat("FIRE_MANIFEST");
            // The north-east terrace has four level exits, so two pursuers cannot seal the retreat.
            yield return Move(new HexCoord(9, 13));
            yield return Use("fire_tab", "fire_preview", Jindan("FIRE_MANIFEST"));
            yield return Guard();
            yield return Capture("01-defend-flank");
            yield return UntilNext(Player);
            Assert.AreEqual(600, seat.FireDomain.Support.Durability, "守侧方应让厚载首轮追到阵心射程外");
            // Guard the western approach. Both attackers can engage here without occupying the exit.
            yield return Move(new HexCoord(6, 14));
            yield return Guard();
            yield return UntilNext(Player);
            Assert.Greater(seat.MaintenancePayments, 0);
            Assert.Greater(seat.FireDomain.Main.Cells.Count, 0);
            yield return Capture("01-guarded-center");
            if (close)
            {
                yield return Use("close_tab", "fire_close", Jindan("FIRE_MANIFEST", close: true));
                Assert.Greater(seat.FireDomain.Tails.SelectMany(t => t.Cells).Count(), 0);
                yield return Capture("02-finite-embers");
            }
            yield return Move(High);
            if (close)
            {
                var reopening = encounter.PreviewJindan(Jindan("FIRE_MANIFEST"));
                if (!reopening.IsLegal)
                {
                    var objective = Find<CombatBountyTrackerView>().Summary;
                    StringAssert.Contains("可燃格", objective);
                    Record($"重展开报价 {reopening.RejectionReason}，当前 CD {Current.GetCooldown("FIRE_MANIFEST")} tick，防御等待");
                    yield return Guard();
                    yield return UntilNext(Player);
                }
                yield return Use("fire_tab", "fire_preview", Jindan("FIRE_MANIFEST"));
            }
            yield return Victory();
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator DefendYinByHoldingGate([ValueSource(nameof(Widths))] int width) => DefendYin(width, false);
        [UnityTest, Timeout(300000)]
        public IEnumerator DefendYinByOpeningGate([ValueSource(nameof(Widths))] int width) => DefendYin(width, true);

        private IEnumerator DefendYin(int width, bool close)
        {
            yield return StartChallenge(width, Yin, "stage3");
            yield return UntilActor(Player);
            var seal = Seat("SPACE_SEAL");
            var hidden = Seat(CombatVisibility.Yin);
            yield return Use("seal_tab", "seal_deploy", Jindan("SPACE_SEAL"));
            yield return Use("visibility_tab", "visibility_deploy", Jindan(CombatVisibility.Yin));
            if (session.Visibility.IsPaused) yield return WinObservation();
            yield return Move(new HexCoord(8, 14));
            if (session.Visibility.IsPaused) yield return WinObservation();
            yield return Capture("01-seal-and-conceal");
            // Let the observer actually approach and attempt the sealed edge before the defender retreats.
            yield return End();
            if (session.Visibility.IsPaused) yield return WinObservation();
            yield return UntilActor(Player);
            var opponent = Actor(Yang);
            if (!session.Visibility.NaturallyRevealed(Actor(Player))) yield return Approach(opponent);
            Assert.IsTrue(session.Visibility.NaturallyRevealed(Actor(Player)), "主动近身应形成近距观察");
            Record($"近距观察 {Current.Position} 对 {opponent.Position} @ tick {session.TurnScheduler.CurrentTick}");
            if (seal.ActiveSeal == null) yield return Use("seal_tab", "seal_deploy", Jindan("SPACE_SEAL"));
            if (!close && hidden.ActiveVisibility == null)
                yield return Use("visibility_tab", "visibility_deploy", Jindan(CombatVisibility.Yin));
            if (session.Visibility.IsPaused) yield return WinObservation();
            if (close)
            {
                yield return Use("close_tab", "seal_close", Jindan("SPACE_SEAL", close: true));
                Assert.IsNull(seal.ActiveSeal);
                yield return Capture("02-seal-open");
                if (opponent.Position == High || opponent.Position == GateTo)
                {
                    // Lure the observer away from the open doorway while still visible.
                    yield return Move(new HexCoord(6, 14));
                    yield return UntilNext(Player);
                    Assert.That(opponent.Position, Is.Not.EqualTo(GateTo).And.Not.EqualTo(High),
                        "阳方应已追出门格，实际 " + opponent.Position);
                }
                yield return Move(High);
                if (session.Visibility.IsPaused) yield return WinObservation();
                yield return Move(GateTo);
                if (hidden.ActiveVisibility == null)
                    yield return Use("visibility_tab", "visibility_deploy", Jindan(CombatVisibility.Yin));
                if (session.Visibility.IsPaused) yield return WinObservation();
            }
            if (!ResultOpen)
            {
                var retreat = (from c in surface.Cells
                    let cell = new HexCoord(c.q, c.r)
                    let distance = cell.TopologicalDistanceTo(opponent.Position)
                    where distance > 1 && distance <= 4 && (close || seal.ActiveSeal.Definition.ContainsOwner(cell))
                    where Movement(cell).IsLegal
                    let line = session.SceneObjects.Board.QueryLineOfSight(opponent.Position, cell).HasLineOfSight
                    orderby line, distance descending, Movement(cell).ActionPointCostUnits
                    select cell).First();
                yield return Move(retreat);
                if (session.Visibility.IsPaused) yield return WinObservation();
            }
            if (!ResultOpen)
            {
                Record("撤出后阳方可见=" + session.Visibility.IsVisible(opponent.Id, Player) +
                    "，近距=" + session.Visibility.NaturallyRevealed(Actor(Player)) +
                    "，观察=" + string.Join("/", session.Visibility.History.Select(c => c.PriorityStep + ":" + c.Outcome)));
                yield return End(); // The older paid Yang domain expires before the freshly deployed concealment.
                if (session.Visibility.IsPaused) yield return WinObservation();
            }
            yield return Victory();
        }
    }
}
