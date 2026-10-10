using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using TianZhang.World;
using TianZhang.Combat;

namespace BattleSim;

static class BattleSimSelfTests
{
    public static int Run(string suite)
    {
        if (suite == "unarmed-config") return RunChecked(suite, RunUnarmedConfig);
        if (suite == "ap2") return RunChecked(suite,RunAp2);
        if (suite == "equipment-production") return RunChecked(suite, RunEquipmentProduction);
        if (suite == "action-economy-n-combat-ap-sim-01")
            return RunChecked(suite, RunActionEconomyNCombatApSim01);
        if (suite == "realm-tq013a")
            return RunChecked(suite, RunRealmTq013A);

        if (suite == "golden-core-tq013b")
            return RunChecked(suite, RunGoldenCoreTq013B);

        if (suite == "golden-core-tq015c")
            return RunChecked(suite, RunGoldenCoreTq015C);

        if (suite == "golden-core-tq015c-3")
            return RunChecked(suite, RunGoldenCoreTq015C3);

        if (suite == "golden-core-tq015c-4")
            return RunChecked(suite, RunGoldenCoreTq015C4);

        if (suite == "golden-core-tq015c-6")
            return RunChecked(suite, RunGoldenCoreTq015C6);

        if (suite == "golden-core-validation-base-n-gc-base-01")
            return RunChecked(suite, RunGoldenCoreValidationBaseNgcBase01);

        if (suite == "golden-core-assembly-n-jd-rule-01a")
            return RunChecked(suite, RunGoldenCoreAssemblyNjdrule01A);

        if (suite == "golden-core-conflict-n-jd-rule-01b")
            return RunChecked(suite, RunGoldenCoreConflictNjdrule01B);

        if (suite == "golden-core-challenge-death-n-jd-rule-01c")
            return RunChecked(suite, RunGoldenCoreChallengeDeathNjdrule01C);

        if (suite == "golden-core-seat-n-seat-01b")
            return RunChecked(suite, RunGoldenCoreSeatNSeat01B);

        if (suite == "stage-matrix-b3")
            return RunChecked(suite, RunStageMatrixB3);

        if (suite == "secondary-detach-a4")
            return RunChecked(suite, RunSecondaryDetachA4);

        if (suite == "golden-core-suppression-tq015c-8")
            return RunChecked(suite, RunGoldenCoreSuppressionTq015C8);

        if (suite == "golden-core-suppression-tq015c-9")
            return RunChecked(suite, RunGoldenCoreSuppressionTq015C9);

        if (suite == "golden-core-suppression-tq015c-10")
            return RunChecked(suite, RunGoldenCoreSuppressionTq015C10);

        if (suite == "ct-reaction-tq052")
            return RunChecked(suite, RunCtReactionTq052);

        if (suite == "state-rewind-causal-n-state-01")
            return RunChecked(suite, RunCombatStateRewindCausalNState01);

        if (suite == "crit-multiplier-tq053")
            return RunChecked(suite, RunCritMultiplierTq053);

        if (suite == "build-input-tq054")
            return RunChecked(suite, RunBuildInputTq054);

        if (suite == "growth-integrity-tq044")
            return RunChecked(suite, RunGrowthIntegrityTq044);

        if (suite == "g2-coverage-tq055")
            return RunChecked(suite, RunG2CoverageTq055);

        if (suite == "g2-audit-cycles-tq055")
            return RunChecked(suite, RunG2AuditCyclesTq055);

        if (suite == "g2-attribution-tq055")
            return RunChecked(suite, RunG2AttributionTq055);

        if (suite == "g2-reproducibility-tq055")
            return RunChecked(suite, RunG2ReproducibilityTq055);

        if (suite == "distance-model-tq055")
            return RunChecked(suite, RunDistanceModelTq055);

        if (suite == "battlefield-foundation-n-dist-01")
            return RunChecked(suite, RunBattlefieldFoundationNDist01);

        if (suite == "position-control-n-dist-02")
            return RunChecked(suite, RunPositionControlNDist02);

        if (suite == "environment-rules-n-env-01")
            return RunChecked(suite, RunEnvironmentRulesNEnv01);

        if (suite == "group-positioning-n-group-01")
            return RunChecked(suite, RunGroupPositioningNGroup01);

        if (suite == "group-area-targeting-n-group-02")
            return RunChecked(suite, RunGroupAreaTargetingNGroup02);

        if (suite == "attack-profile-d-combat-02")
            return RunChecked(suite, RunAttackProfileDCombat02);

        if (suite == "basic-unarmed-production-d-combat-prod-01")
            return RunChecked(suite, RunBasicUnarmedProductionDCombatProd01);

        if (suite == "group-action-priority-n-group-02")
            return RunChecked(suite, RunGroupActionPriorityNGroup02);

        if (suite == "duel-bounds")
            return RunChecked(suite, RunDuelBounds);

        if (suite == "combat-parity-v1")
            return RunChecked(suite, RunCombatParityV1);

        if (suite != "element-v510")
        {
            Console.Error.WriteLine($"Unknown self-test suite: {suite}");
            return 2;
        }

        return RunChecked(suite, RunElementV510);
    }

    static int RunChecked(string suite, Action body)
    {
        try
        {
            body();
            Console.WriteLine($"SELFTEST {suite} PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SELFTEST {suite} FAIL: {ex}");
            return 1;
        }
    }

    static void RunActionEconomyNCombatApSim01()
    {
        var board = new HexBattlefield();
        var adjacent = new[] { new HexCoord(0, 0), new HexCoord(1, 0) };
        Character Fixture(string name, int speed = 50)
        {
            var c = GroupTestCharacter(name, speed, hp: 10000, attack: 100, movement: 0);
            c.Primary["MP"] = 100; c.Primary["神攻"] = 100; c.Primary["神识"] = 25;
            return c;
        }
        var a = Fixture("ap-a");
        var b = Fixture("ap-b");
        Combat.ResetDeterministicRandom();
        var duel = Combat.SimulateDetailed(board, a, b, adjacent, turnLimit: 6);
        var first = duel.Economy.Where(e => e.Turn == 1 && e.Event == "action_committed").ToArray();
        AssertEqual(2, first.Length, "100 AP pays for two actual basic attacks");
        AssertSequence(new[] { 50000, 0 }, first.Select(e => e.ActionPointsRemainingUnits).ToArray(), "AP decreases without reissuing");
        AssertEqual(true, first.All(e => e.GlobalTick == 2 && e.CostUnits == 50000 && e.ReductionUnits == 0 && e.RefundUnits == 0), "commands do not advance time or invent modifiers");
        AssertSequence(new[] { 0, 1 }, duel.Economy.Where(e => e.Event == "own_turn_started").Take(2).Select(e => e.ActorIndex).ToArray(), "stable-ID ready order");
        AssertEqual(2, duel.Economy.First(e => e.Event == "own_turn_started" && e.ActorIndex == 1).GlobalTick, "same-tick queue drains before charging");
        AssertEqual(true, duel.Economy.Where(e => e.Event == "own_turn_ended").All(e => e.ActionPointsRemainingUnits == 0 && e.ChargeTime == 0), "normal end clears both budgets and overshoot");

        var deadA = Fixture("inactive-a"); deadA.Primary["HP"] = 0;
        var deadB = Fixture("inactive-b"); deadB.Primary["HP"] = 0;
        var groupPositions = new[] { adjacent[0], new HexCoord(0, 1), adjacent[1], new HexCoord(1, -1) };
        Combat.ResetDeterministicRandom();
        var group = Combat.Simulate2v2Detailed(board, a, deadA, b, deadB, groupPositions, turnLimit: 6);
        object Comparable(Combat.ActionEconomyObservation e, bool isGroup) => new
        {
            e.Turn, actor = isGroup && e.ActorIndex == 2 ? 1 : e.ActorIndex,
            e.GlobalTick, e.Event, e.DefinitionRef, e.ActionPointsBeforeUnits, e.ActionPointsRemainingUnits,
            e.CostUnits, e.ReductionUnits, e.RefundUnits, e.SpiritBefore, e.SpiritAfter, e.HealthBefore, e.HealthAfter,
            e.PendingCtPenalty, e.ChargeTime, e.NextThreshold, e.OwnTurns, e.CommittedActions,
            cooldowns = e.Cooldowns.OrderBy(k => k.Key).Select(k => new { id = k.Key.Substring(k.Key.IndexOf(':') + 1), k.Value }).ToArray()
        };
        AssertEqual(JsonSerializer.Serialize(duel.Economy.Select(e => Comparable(e, false))),
            JsonSerializer.Serialize(group.Economy.Select(e => Comparable(e, true))), "both public detailed entries execute identical per-command trajectories with inactive partners");
        AssertEqual(duel.GlobalTick, group.GlobalTick, "entry parity includes integer time");
        AssertEqual(duel.WinnerTeam, group.WinnerTeam, "entry parity includes timeout settlement");

        Combat.ResetDeterministicRandom();
        var waiting = Combat.SimulateDetailed(board, Fixture("wait-actor"), Fixture("wait-target", 1),
            new[] { new HexCoord(0, 0), new HexCoord(6, 0) }, turnLimit: 1);
        var waitEnd = waiting.Economy.Last();
        AssertEqual("wait_ended", waitEnd.Event, "no committed command permits waiting");
        AssertEqual(50f, waitEnd.ChargeTime, "waiting keeps exactly half the ready CT");
        AssertEqual(100f, waitEnd.NextThreshold, "waiting resets the threshold without a penalty");
        AssertEqual(0, waitEnd.ActionPointsRemainingUnits, "waiting discards the AP balance");
        var mover = Fixture("move-actor"); mover.SetActionEconomyInputs(100000, 3000);
        Combat.ResetDeterministicRandom();
        var moving = Combat.SimulateDetailed(board, mover, Fixture("move-target", 1),
            new[] { new HexCoord(0, 0), new HexCoord(6, 0) }, turnLimit: 1);
        AssertEqual(99999, moving.Economy.Single(e => e.Event == "move_committed").CostUnits,
            "an actual movement command commits the native path quote");
        AssertEqual("own_turn_ended", moving.Economy.Last().Event, "successful movement prevents wait retention");
        AssertEqual(0f, moving.Economy.Last().ChargeTime, "movement-only normal end clears CT");

        var mage = Fixture("ct-mage"); mage.Style = "magic"; mage.AssignArts(); mage.Primary["MP"] = 20;
        mage.BasicAttackProfile = new("ct-basic-fixture", "物理", 1, "", 1, 2,
            Cost: new("ct-basic-fixture", "fixture", 50000, 50000, 30, 0));
        Combat.ResetDeterministicRandom();
        var penalties = Combat.SimulateDetailed(board, mage, Fixture("ct-target"),
            new[] { new HexCoord(0, 0), new HexCoord(2, 0) }, turnLimit: 6);
        var penaltyActions = penalties.Economy.Where(e => e.Turn == 1 && e.Event == "action_committed").ToArray();
        AssertSequence(new[] { "MagicArt", "ct-basic-fixture" }, penaltyActions.Select(e => e.DefinitionRef).ToArray(), "actual actions combine distinct CT penalties");
        var penaltyEnd = penalties.Economy.Single(e => e.Turn == 1 && e.Event == "own_turn_ended");
        AssertEqual(30, penaltyEnd.PendingCtPenalty, "CT penalties sum across commands");
        AssertEqual(130f, penaltyEnd.NextThreshold, "one end settles 100+10+30");
        AssertEqual(0, penaltyEnd.Cooldowns["actor-0:MagicArt"], "the second command does not tick the first ability cooldown");
        var nextMageStart = penalties.Economy.First(e => e.ActorIndex == 0 && e.OwnTurns == 2 && e.Event == "own_turn_started");
        AssertEqual(Math.Max(0, 0 - (nextMageStart.GlobalTick - penaltyEnd.GlobalTick)),
            nextMageStart.Cooldowns["actor-0:MagicArt"], "cooldown decrements by actual elapsed ticks");

        var production = Fixture("production-three"); production.Primary["MP"] = 20; production.Innate["魂魄"] = 25;
        production.EquipProductionArts("spell_guanshiji", "spell_julingshu");
        Combat.ResetDeterministicRandom();
        var productionRun = Combat.SimulateDetailed(board, production, Fixture("production-target"), adjacent, turnLimit: 2);
        var productionActions = productionRun.Economy.Where(e => e.Turn == 1 && e.Event == "action_committed").ToArray();
        AssertSequence(new[] { "spell_guanshiji", "spell_julingshu" }, productionActions.Select(e => e.DefinitionRef).ToArray(), "production attack and recovery are actually selected by the generic entry");
        AssertEqual((20, 8), (productionActions[0].SpiritBefore, productionActions[0].SpiritAfter), "production attack spends exactly CSV MP");
        AssertEqual((8, 20), (productionActions[1].SpiritBefore, productionActions[1].SpiritAfter), "production self recovery uses raw spirit and caps at maximum");
        AssertEqual(25000, productionActions[1].ActionPointsRemainingUnits, "production recovery has independent 25 AP cost");
        var healer = Fixture("other-recovery", 51); healer.Innate["魂魄"] = 25; healer.EquipProductionArts("spell_julingshu");
        var allyCaster = Fixture("ally-caster", 100); allyCaster.Style = "magic"; allyCaster.AssignArts();
        Combat.ResetDeterministicRandom();
        var allyRecovery = Combat.Simulate2v2Detailed(board, healer, allyCaster, Fixture("ally-enemy-a", 1), Fixture("ally-enemy-b", 1),
            new[] { new HexCoord(0, 0), new HexCoord(0, 1), new HexCoord(2, 1), new HexCoord(2, 0) }, turnLimit: 3);
        var otherRecovery = allyRecovery.Economy.First(e => e.ActorIndex == 0 && e.DefinitionRef == "spell_julingshu");
        AssertEqual((100, 80), (otherRecovery.SpiritBefore, otherRecovery.SpiritAfter), "ally recovery spends the production 20 MP");
        AssertEqual(26, otherRecovery.SpiritByActor[1], "four zero-CD casts precede the slow healer, which restores the capped half output");

        var path = board.QueryMovementPath(new(0, 0), new(3, 0), 100000, 3000);
        AssertEqual(99999, path.ActionPointCostUnits, "three ordinary edges at v3 fit 100 AP");
        int split = board.QueryMovementPath(new(0, 0), new(1, 0), 100000, 3000).ActionPointCostUnits +
            board.QueryMovementPath(new(1, 0), new(3, 0), 100000, 3000).ActionPointCostUnits;
        AssertEqual(path.ActionPointCostUnits, split, "movement commands do not reset edge rounding");
        AssertEqual(TianZhang.Spatial.SpatialQueryReasons.MovementEfficiencyZero,
            board.QueryMovementPath(new(0, 0), new(1, 0), 100000, 0).Reason, "v0 is legal but cannot voluntarily move");
        AssertEqual(1, board.QueryMovementPath(new(0, 0), new(1, 0), 100000, int.MaxValue).ActionPointCostUnits, "positive edge minimum is one unit, not one AP");
        AssertEqual(true, board.FindReachableWithinActionPoints(new(0, 0), 100000, 3000).ContainsKey(new(1, 0)), "initial path is reachable");
        AssertEqual(false, board.FindReachableWithinActionPoints(new(0, 0), 100000, 3000, new[] { new HexCoord(1, 0) }).ContainsKey(new(1, 0)), "new occupancy invalidates reachable input");
        AssertEqual(true, board.FindReachableWithinActionPoints(new(0, 0), 100000, 3000).ContainsKey(new(1, 0)), "removing occupancy restores actual reachability");
        AssertEqual(false, board.FindReachableWithinActionPoints(new(0, 0), 1000, 3000).ContainsKey(new(1, 0)), "changing AP cannot reuse a larger budget result");

        AssertEqual(4111, GameData.ProjectMovement("练气", 0, "苦行剑典").EffectiveMilliUnits, "exact first ninth projected once");
        AssertEqual(5000, GameData.ProjectMovement("练气", 8, "苦行剑典").EffectiveMilliUnits, "nine exact ninths reach one whole bonus");
        AssertEqual(4953, GameData.ProjectMovement("筑基", 1, "苦行剑典").EffectiveMilliUnits, "prior nodes plus current 30/32 share");
        AssertEqual(6400, GameData.ProjectMovement("金丹", 2, "苦行剑典").EffectiveMilliUnits, "approved golden-core cumulative movement");
        AssertEqual(4800, GameData.ProjectMovement("金丹", 2, "含弘光大典").EffectiveMilliUnits, "switching gongfa removes its passive movement");
        AssertEqual(6400, GameData.ProjectMovement("金丹", 2, "苦行剑典").EffectiveMilliUnits, "rebuilding does not double grant old nodes");
        AssertEqual(true, GameData.ProjectMovement("练气", 0, "抱元守一经").UnlockedChapters.Contains("练气守一"), "the first legal chapter is recorded");
        AssertEqual(0, GameData.ProjectMovement("筑基", 0, "万物不迁法").UnlockedChapters.Count, "learning eligibility does not unlock the golden-core chapter early");
        bool unknownRejected = false;
        try { GameData.ProjectMovement("练气", 0, "unknown"); } catch (InvalidDataException) { unknownRejected = true; }
        AssertEqual(true, unknownRejected, "unknown gongfa never gets a default movement curve");
        var missing = Fixture("missing-input"); missing.Primary.Remove("AP上限单位");
        bool missingRejected = false;
        try { Combat.SimulateDetailed(board, missing, b, adjacent, 1); } catch (InvalidOperationException e) { missingRejected = e.Message == "action_economy_character_input_invalid"; }
        AssertEqual(true, missingRejected, "missing long-term AP is rejected rather than filled");

        Character Water() { var c = Fixture("water", 50); c.GongFaName = "秋水游心经"; c.Style = "water_physical"; c.Realm = "筑基"; c.FixedTestAbilityTierId = "tier_lianqi"; c.Primary["神识"] = 1000; c.AssignArts(); return c; }
        Character Lightning() { var c = Fixture("lightning", 49); c.Realm = "筑基"; c.GongFaName = "九霄雷劫录"; c.Style = "yuqing_leijie"; c.HealthRecoveryPerOwnTurn = 7; c.AssignArts(); return c; }
        Combat.ResetDeterministicRandom();
        var forward = Combat.SimulateDetailed(board, Water(), Lightning(), adjacent, 12);
        Combat.ResetDeterministicRandom();
        var reverse = Combat.SimulateDetailed(board, Lightning(), Water(), adjacent.Reverse().ToArray(), 12);
        object StateSymmetry(Combat.ActionEconomyObservation e, bool swapped) => new
        {
            e.Turn, actor = swapped ? 1 - e.ActorIndex : e.ActorIndex, e.Event, e.GlobalTick,
            e.DefinitionRef, e.CostUnits, e.SpiritAfter, e.HealthAfter, e.Shouyi, e.Fudan, e.Leijie, e.Stunned, e.OwnTurns
        };
        AssertEqual(JsonSerializer.Serialize(forward.Economy.Select(e => StateSymmetry(e, false))),
            JsonSerializer.Serialize(reverse.Economy.Select(e => StateSymmetry(e, true))), "water control and received lightning stacks are symmetric after swapping sides");
        AssertEqual(true, forward.Economy.Any(e => e.ActorIndex == 1 && e.Leijie > 0), "positive received damage adds a lightning stack");
        AssertEqual(true, forward.Economy.Any(e => e.Event == "own_turn_started_control_skip"), "water stun skips an entire actual own turn");
        AssertEqual(true, forward.Economy.Where(e => e.Event == "own_turn_started_control_skip").All(e => e.ActionPointsRemainingUnits == 0), "controlled turns cannot obtain AP or wait retention");
        AssertEqual(true, forward.Economy.Where(e => e.Event == "own_turn_started_control_skip").All(e => e.HealthAfter == Math.Min(10000, e.HealthBefore + 7)), "control skips still receive one declared own-start recovery");

        var assembly = GoldenCoreAssembly.Create(LoadGoldenCoreFixture("jd.valid.three-mansion-three-seats"));
        var sharedActor = Fixture("shared-instance"); sharedActor.Realm = "金丹"; sharedActor.FormedState = "成丹"; sharedActor.DanJiType = "fixture";
        sharedActor.FixedTestAbilityTierId = "tier_lianqi"; sharedActor.AssignArts(); sharedActor.SetActionEconomyInputs(150000, 0);
        sharedActor.AssignGoldenCoreAssembly(assembly, "guardian_ming", "guardian_ming");
        Combat.ResetDeterministicRandom();
        var sharedRun = Combat.SimulateDetailed(board, sharedActor, Fixture("shared-target"), adjacent, 2);
        AssertEqual(1, sharedRun.Economy.Count(e => e.Turn == 1 && e.Event == "action_committed" && e.AbilityInstanceId == "guardian_ming"), "two external forms share one actual cooldown ledger");
        AssertEqual(4, sharedRun.Economy.First(e => e.Event == "own_turn_ended").Cooldowns["guardian_ming"], "shared ledger is not double ticked by forms");

        var shouyi = Fixture("shouyi"); shouyi.Realm = "金丹"; shouyi.GongFaName = "抱元守一经";
        Combat.ResetDeterministicRandom();
        var shouyiRun = Combat.SimulateDetailed(board, shouyi, Fixture("passive-target", 1), adjacent, 1);
        AssertEqual(3, shouyiRun.Economy.Last().Shouyi, "two attacks grant only one own-end stack");
        var fuxiu = Fixture("fudan"); fuxiu.Realm = "筑基"; fuxiu.GongFaName = "云篆度人经";
        Combat.ResetDeterministicRandom();
        var fudanRun = Combat.SimulateDetailed(board, fuxiu, Fixture("fudan-target", 1), adjacent, 1);
        AssertEqual(1, fudanRun.Economy.Last().Fudan, "two basic attacks grant one own-end symbol stack");
        var waterHeal = Fixture("own-heal", 50); waterHeal.GongFaName = "秋水游心经";
        Combat.ResetDeterministicRandom();
        var healRun = Combat.SimulateDetailed(board, waterHeal, Fixture("heal-attacker", 100), adjacent, 4);
        AssertEqual(true, healRun.Economy.Where(e => e.ActorIndex == 0 && e.Event == "action_committed").All(e => e.HealthBefore == e.HealthAfter), "multiple child actions do not repeat the end-of-turn heal");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            seed = 20260712,
            inputs = new { GameData.Rules.RuleSetVersion,GameData.Rules.SourceDigest },
            dualEntryFixture = "adjacent 50/50 speed, 100 AP, v0, two inactive group partners; normalized actor indices only",
            duel = duel.Economy, group = group.Economy, ct = penalties.Economy, production = productionRun.Economy,
            symmetry = forward.Economy, shared = sharedRun.Economy, allyRecovery = allyRecovery.Economy
            , waiting = waiting.Economy, moving = moving.Economy
        }));
    }

    internal static Character Ap2Fixture(string realm = "金丹", string style = "physical", string tier = "tier_lianqi", int hp = 10000)
    {
        var c = new Character { Name="ap2-fixture",Realm=realm,Style=style,FixedTestAbilityTierId=tier,
            FormedState=realm=="金丹" ? "成丹" : "",DanJiType=realm=="金丹" ? "explicit-test-qualification" : "",
            Innate=new() { ["根骨"]=8,["魂魄"]=25,["神识"]=10,["资质"]=8,["气运"]=8 },
            Primary=new() { ["HP"]=hp,["MP"]=100000,["肉攻"]=5000,["神攻"]=5000,["肉防"]=1000,["神防"]=1000,["反应"]=100,["神识"]=25 } };
        var movement=GameData.ProjectMovement(realm,realm=="练气" ? 8 : 2,"");
        c.SetActionEconomyInputs(movement.ActionPointMaximumUnits,movement.EffectiveMilliUnits); c.AssignArts(); return c;
    }
    static void RunAp2()
    {
        var service=new CombatCommandService(); var board=new HexBattlefield();
        AssertEqual(51,GameData.Rules.Economy.Count(r=>r.RecordKind=="test_action"),"51 explicit test triples");
        AssertEqual(4,GameData.ProductionAttacks.Count,"four actual production IDs");
        AssertEqual(false,GameData.ProjectMovement("金丹",2,"万法都箓经").UnlockedChapters.Any(p=>p.Contains("元婴")),"future chapter is never granted early");
        AssertEqual(5333,GameData.ProjectMovement("筑基",2,"苦行剑典").EffectiveMilliUnits,"same table cumulative M rescale");
        foreach (var row in GameData.Rules.Economy.Where(r=>r.RecordKind=="test_action"))
        {
            string style=row.DefinitionRef.StartsWith("Physical") ? "physical" : row.DefinitionRef.StartsWith("Water") ? "water_physical" :
                row.DefinitionRef.StartsWith("TaiyiFuxiu") ? "taiyi_fuxiu" : row.DefinitionRef.StartsWith("Taiyi") ? "taiyi" :
                row.DefinitionRef.StartsWith("Taixu") ? "taixu" : row.DefinitionRef.StartsWith("YuqingLeijie") ? "yuqing_leijie" :
                row.DefinitionRef.StartsWith("Yuqing") ? "yuqing" : row.DefinitionRef=="KuxingAction" ? "yuqing_kuxing" : "magic";
            var actor=Ap2Fixture(style:style,tier:row.AbilityTierId,hp:1000000);
            if (row.DefinitionRef=="KuxingAction") actor.GongFaName="苦行剑典";
            if (row.DefinitionRef.EndsWith("Divine") || row.DefinitionRef=="KuxingAction") actor.ArtName="";
            if (!row.DefinitionRef.EndsWith("Divine")) actor.DivineName="";
            var target=Ap2Fixture(hp:1000000); target.ArtName=""; target.DivineName=""; target.Primary["反应"]=1;
            var p=GameData.GenericProfile(row.DefinitionRef,row.AbilityTierId);
            actor.SetActionEconomyInputs(p.Cost.ActionPointCostUnits,0);
            Combat.ResetDeterministicRandom();
            var full=Combat.SimulateDetailed(board,actor,target,new[] { new HexCoord(0,0),new HexCoord(p.MinRange,0) },1);
            var action=full.Economy.Single(e=>e.Event=="action_committed");
            AssertEqual(p.Cost.ActionPointCostUnits,action.CostUnits,"full exact base quote "+row.DefinitionRef);
            AssertEqual(row.AbilityTierId,action.AbilityTierId,"full fixed tier");
            var shared=Ap2CombatReport.Session(new[] { actor,target },false);
            shared.Combatants.All.Single(a=>a.Team==CombatTeam.Enemy).SetPosition(new TianZhang.Spatial.HexCoord(p.MinRange,0));
            AssertEqual("actor-0",service.AdvanceUntilAction(shared).ActorId,"shared current actor");
            var profile=shared.AttackProfiles.Single();
            var command=new CombatCommand(profile.Kind==CombatAttackKind.Divine ? CombatCommandKind.Divine : CombatCommandKind.Art,
                "actor-0","actor-1",profile.Id,new CombatResolutionRolls(0,100,100,100,100));
            var quote=service.Preview(shared,command);
            AssertEqual(true,quote.IsLegal,"shared legal triple "+row.DefinitionRef);
            AssertEqual(p.Cost.ActionPointCostUnits,quote.ActionPointCostUnits,"same CSV quote");
            var result=service.Execute(shared,command,quote); AssertEqual(true,result.Succeeded,"shared actual triple "+row.DefinitionRef);
            AssertEqual(p.Cost.ActionPointCostUnits,result.ActionPointSpentUnits,"shared exact payment");
        }
        var basicActor=Ap2Fixture(); basicActor.ArtName=""; basicActor.DivineName=""; basicActor.BasicAttackProfile=GameData.ProductionAttacks["basic_iron_sword"];
        var enemy=Ap2Fixture(hp:1000000); enemy.ArtName=""; enemy.DivineName=""; enemy.Primary["反应"]=1;
        Combat.ResetDeterministicRandom();
        var repeated=Combat.SimulateDetailed(board,basicActor,enemy,new[] { new HexCoord(0,0),new HexCoord(1,0) },1);
        var paid=repeated.Economy.Where(e=>e.Event=="action_committed").ToArray();
        AssertSequence(new[] { 40000,40000,50000,60000,60000 },paid.Take(5).Select(e=>e.CostUnits).ToArray(),"full 1/2/3/4/cap");
        AssertSequence(new[] { 0,1,2,3,4 },paid.Take(5).Select(e=>e.CommittedUseCount).ToArray(),"full use count");
        AssertEqual(true,paid.All(e=>e.ActionPointsBeforeUnits-e.ActionPointsRemainingUnits==e.CostUnits),"full quote/payment identity");
        var sharedRepeat=Ap2CombatReport.Session(new[] { basicActor,enemy },false); service.AdvanceUntilAction(sharedRepeat);
        var basicCommand=new CombatCommand(CombatCommandKind.BasicAttack,"actor-0","actor-1","basic_iron_sword",new CombatResolutionRolls(0,100,100,100,100));
        foreach (int cost in new[] { 40000,40000,50000,60000,60000 })
        {
            var q=service.Preview(sharedRepeat,basicCommand); AssertEqual(cost,q.ActionPointCostUnits,"shared cap sequence");
            AssertEqual(cost,service.Execute(sharedRepeat,basicCommand,q).ActionPointSpentUnits,"shared payment");
        }
        var unequipped=Ap2Fixture(); unequipped.ArtName=""; unequipped.DivineName="";
        var noWeapon=Ap2CombatReport.Session(new[] { unequipped,enemy },false); service.AdvanceUntilAction(noWeapon);
        AssertEqual("attack_profile_not_authorized",service.Preview(noWeapon,basicCommand).Validation.RejectionReason,"no implicit unarmed");
        foreach (string realm in new[] { "练气","筑基","金丹" })
        {
            var bitter=Ap2Fixture(realm,"yuqing_kuxing"); bitter.GongFaName="苦行剑典"; bitter.ArtName=""; bitter.DivineName=""; bitter.SetActionEconomyInputs(75000,0);
            Combat.ResetDeterministicRandom();
            var full=Combat.SimulateDetailed(board,bitter,enemy,new[] { new HexCoord(0,0),new HexCoord(1,0) },1);
            var e=full.Economy.Single(e=>e.Event=="action_committed");
            int hp=realm=="练气" ? 9000 : realm=="筑基" ? 8500 : 8600;
            AssertEqual(hp,e.HealthAfter,"full original realm life fee/recovery budget");
            AssertEqual(300,e.KuxingDebtByActor[0],"full next physical defense debt");
            var ss=Ap2CombatReport.Session(new[] { bitter,enemy },false); service.AdvanceUntilAction(ss);
            var caster=ss.Combatants.All.First(); var p=ss.AttackProfiles.Single();
            var command=new CombatCommand(CombatCommandKind.Art,caster.Id,"actor-1",p.Id,new CombatResolutionRolls(0,100,0,100,100));
            var q=service.Preview(ss,command); var prepared=service.Prepare(ss,command,q,out var commit);
            AssertEqual(true,prepared.Succeeded,"shared Kuxing prepare"); AssertEqual(10000,caster.CurrentHealth,"preparation leaks no health fee");
            AssertEqual(0,caster.Statuses.Count,"preparation leaks no defense debt"); commit();
            AssertEqual(hp,caster.CurrentHealth,"shared same original realm life fee"); AssertEqual(true,caster.HasStatus(CombatStatusKind.KuxingDefenseDebt),"shared published debt");
        }
        var water=Ap2Fixture(style:"water_physical"); water.ArtName=""; water.SetActionEconomyInputs(1000000,0);
        var ws=Ap2CombatReport.Session(new[] { water,enemy },false); service.AdvanceUntilAction(ws);
        var wa=ws.Combatants.All.First(); var wt=ws.Combatants.All.Last(); var wp=ws.AttackProfiles.Single();
        var waterCommand=new CombatCommand(CombatCommandKind.Divine,wa.Id,wt.Id,wp.Id,new CombatResolutionRolls(0,100,100,100,100));
        for(int n=0;n<4;n++)
        {
            if(n>0) wa.AdvanceCooldowns(12);
            AssertEqual(true,service.Execute(ws,waterCommand).Succeeded,"shared water root");
            AssertEqual(Math.Min(3,n+1),wt.Statuses.Single(s=>s.Kind==CombatStatusKind.WaterMark).StackCount,"three layer limit");
            AssertEqual(1,wa.Statuses.Count(s=>s.Kind==CombatStatusKind.WaterReduction),"reapply overwrites rather than stacks");
        }
        AssertEqual(982,wt.PhysicalDefense,"low tier 15 percent reduces only capacity120");
        AssertEqual("tier_lianqi",wt.Statuses.Single().AbilityTierId,"water source tier retained");
        var frozen=Ap2Fixture("练气",tier:"tier_lianqi"); frozen.Realm="金丹";
        AssertEqual("tier_lianqi",frozen.AbilityTierId,"realm change alone never upgrades a granted test instance");
        AssertEqual(50000,frozen.ArtCost.ActionPointCostUnits,"frozen cost remains from its original tier");
        var misses=Ap2Fixture(); misses.ArtName=""; misses.DivineName=""; misses.BasicAttackProfile=GameData.ProductionAttacks["basic_iron_sword"];
        enemy.Secondary["闪避率"]=1000;
        Combat.ResetDeterministicRandom();
        var missed=Combat.SimulateDetailed(board,misses,enemy,new[] { new HexCoord(0,0),new HexCoord(1,0) },1);
        var missPaid=missed.Economy.Where(e=>e.Event=="action_committed").ToArray();
        AssertEqual(true,missPaid.Any(e=>e.HealthByActor[1]==1000000 && e.CommittedUseCount>0),"legal miss still advances the full root-use count");
        enemy.Secondary.Clear();
        foreach (string realm in new[] { "练气","筑基","金丹" })
        {
            var caster=Ap2Fixture(realm,"yuqing_kuxing"); caster.GongFaName="苦行剑典"; caster.ArtName=""; caster.DivineName=""; caster.SetActionEconomyInputs(75000,0);
            var low=Ap2Fixture(style:"yuqing_kuxing"); low.GongFaName="苦行剑典"; low.ArtName=""; low.DivineName=""; low.Primary["反应"]=200;
            Combat.ResetDeterministicRandom();
            var battle=Combat.SimulateDetailed(board,caster,low,new[] { new HexCoord(0,0),new HexCoord(1,0) },2);
            var root=battle.Economy.First(e=>e.ActorIndex==0 && e.Event=="action_committed");
            int before=battle.Economy.TakeWhile(e=>e!=root).Last().HealthByActor[1];
            AssertEqual(true,before<5000,"target actually below half after its own life fees");
            AssertEqual(realm=="练气" ? 26 : 34,before-root.HealthByActor[1],"full fixed-Qi damage uses original realm low-health qualification");
        }
        var passive=Ap2Fixture(); passive.ArtName=""; passive.DivineName=""; passive.GongFaName="抱元守一经";
        passive.BasicAttackProfile=GameData.ProductionAttacks["basic_iron_sword"]; passive.HealthRecoveryPerOwnTurn=7; passive.SpiritRecoveryPerOwnTurn=3;
        var passiveSession=Ap2CombatReport.Session(new[] { passive,enemy },false); var owner=passiveSession.Combatants.All.First();
        owner.ReceiveDamage(100); owner.SetSpirit(100,10); service.AdvanceUntilAction(passiveSession);
        AssertEqual(9907,owner.CurrentHealth,"shared declared health restoration once at own start"); AssertEqual(13,owner.CurrentSpirit,"shared declared spirit restoration once at own start");
        for(int n=0;n<5;n++) AssertEqual(true,service.Execute(passiveSession,basicCommand).Succeeded,"shared multiple own commands");
        AssertEqual(9907,owner.CurrentHealth,"shared commands do not multiply passive HP"); AssertEqual(13,owner.CurrentSpirit,"shared commands do not multiply passive MP");
        service.Execute(passiveSession,new CombatCommand(CombatCommandKind.EndTurn,owner.Id));
        AssertEqual(3,owner.ShouyiStacks,"shared multiple attacks give one own-end stack, from initial2");
        var fullWater=Ap2Fixture(style:"water_physical",hp:1000000); fullWater.ArtName=""; fullWater.SetActionEconomyInputs(75000,0);
        var waterEnemy=Ap2Fixture(hp:1000000); waterEnemy.ArtName=""; waterEnemy.DivineName="";
        waterEnemy.BasicAttackProfile=GameData.ProductionAttacks["basic_iron_sword"]; waterEnemy.SetActionEconomyInputs(40000,0);
        Combat.ResetDeterministicRandom();
        var waterFull=Combat.SimulateDetailed(board,fullWater,waterEnemy,new[] { new HexCoord(0,0),new HexCoord(1,0) },80);
        var waterRoots=waterFull.Economy.Where(e=>e.DefinitionRef=="WaterDivine" && e.Event=="action_committed").ToArray();
        AssertEqual(true,waterRoots.Length>=4,"full water reapplication executes after global cooldown");
        AssertEqual(true,waterRoots.All(e=>e.WaterStacksByActor[1]<=3 && e.WaterReductionByActor[0]==.30m),"full total stack peak and one overwrite");
        AssertEqual(true,waterFull.Economy.Any(e=>e.ActorIndex==1 && e.Event=="action_committed" && e.WaterReductionByActor[0]==0),"full next positive damage consumes reduction");
        var bitterDebt=Ap2Fixture(style:"yuqing_kuxing"); bitterDebt.GongFaName="苦行剑典"; bitterDebt.ArtName=""; bitterDebt.DivineName="";
        bitterDebt.SetActionEconomyInputs(75000,0); Combat.ResetDeterministicRandom();
        var debtFull=Combat.SimulateDetailed(board,bitterDebt,waterEnemy,new[] { new HexCoord(0,0),new HexCoord(1,0) },2);
        AssertEqual(300,debtFull.Economy.First(e=>e.ActorIndex==0 && e.Event=="action_committed").KuxingDebtByActor[0],"full published defense debt");
        AssertEqual(0,debtFull.Economy.First(e=>e.ActorIndex==1 && e.Event=="action_committed").KuxingDebtByActor[0],"full next physical received hit consumes debt");
        var circle=new GameData.AreaTargetingConfig("ap2-fixture-circle",GameData.AreaCenterKind.TargetCell,1,1,
            new GameData.AreaShapeConfig(GameData.AreaShapeKind.Circle,1,0,0,null,0),GameData.AreaEffectBlocker.None,
            GameData.AreaTargetFaction.Enemy,GameData.AreaTargetState.Alive);
        var areaActor=Ap2Fixture(); areaActor.ArtName=""; areaActor.DivineName=""; areaActor.SetActionEconomyInputs(100000,0);
        areaActor.BasicAttackProfile=GameData.ProductionAttacks["basic_iron_sword"] with { AreaTargeting=circle };
        var inactive=Ap2Fixture(); inactive.Primary["HP"]=0; inactive.ArtName=""; inactive.DivineName="";
        var areaOther=Ap2Fixture(hp:1000000); areaOther.ArtName=""; areaOther.DivineName="";
        Combat.ResetDeterministicRandom();
        var area=Combat.Simulate2v2Detailed(board,areaActor,inactive,enemy,areaOther,
            new[] { new HexCoord(0,0),new HexCoord(0,1),new HexCoord(1,0),new HexCoord(1,-1) },1);
        AssertEqual(2,area.Actions.Count(a=>a.ActorIndex==0 && a.AreaHitTargetIndexes.Count==2),"full two-target root actually resolves twice");
        AssertSequence(new[] { 40000,40000 },area.Economy.Where(e=>e.Event=="action_committed").Select(e=>e.CostUnits).ToArray(),"area pays once per root rather than per target");
        AssertSequence(new[] { 0,1 },area.Economy.Where(e=>e.Event=="action_committed").Select(e=>e.CommittedUseCount).ToArray(),"area counts once per root");
        areaActor.BasicAttackProfile=GameData.ProductionAttacks["basic_iron_sword"];
        var sharedGroup=Ap2CombatReport.Session(new[] { areaActor,Ap2Fixture(),enemy,areaOther },true); service.AdvanceUntilAction(sharedGroup);
        var groupQuote=service.Preview(sharedGroup,basicCommand);
        var groupTarget=new CombatCommand(CombatCommandKind.BasicAttack,"actor-0","actor-2","basic_iron_sword",new CombatResolutionRolls(0,100,100,100,100));
        groupQuote=service.Preview(sharedGroup,groupTarget);
        AssertEqual(40000,service.Execute(sharedGroup,groupTarget,groupQuote).ActionPointSpentUnits,"shared group single target root pays once");
        AssertEqual(1000000,sharedGroup.Combatants.All.Single(a=>a.Id=="actor-3").CurrentHealth,"shared group does not invent an area payload");
        // Area root and both team layouts execute the existing full simulation owner.
        RunGroupAreaTargetingNGroup02();
        Console.WriteLine(JsonSerializer.Serialize(new { GameData.Rules.RuleSetVersion,GameData.Rules.SourceDigest,
            triples=51,fullRepeat=paid.Take(5).Select(e=>new { e.CostUnits,e.CommittedUseCount,e.RepeatMultiplierPermille }),
            fullArea="existing AreaTargeting root",sharedWater=wt.Statuses,coverage="actual full and shared transactions" }));
    }

    static void RunRealmTq013A()
    {
        AssertSequence(new[] { "凡人", "练气", "筑基", "金丹" }, GameData.RealmOrder, "default player realm order");
        AssertEqual(1, GameData.Sublevels["凡人"], "凡人 sublevels");
        AssertEqual(9, GameData.Sublevels["练气"], "练气 sublevels");
        AssertEqual(5, GameData.Sublevels["筑基"], "筑基 sublevels");
        AssertEqual(3, GameData.Sublevels["金丹"], "金丹 sublevels");

        var stageName = typeof(GameData).GetMethod(
            "StageName",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string), typeof(int) },
            modifiers: null);
        if (stageName == null)
            throw new InvalidOperationException("GameData.StageName is missing.");

        string Stage(string realm, int subIdx) => (string)stageName.Invoke(null, new object[] { realm, subIdx })!;
        AssertEqual("筑基初期", Stage("筑基", 0), "筑基 stage 0");
        AssertEqual("筑基中期", Stage("筑基", 1), "筑基 stage 1");
        AssertEqual("筑基后期", Stage("筑基", 2), "筑基 stage 2");
        AssertEqual("紫府初开", Stage("筑基", 3), "筑基 stage 3");
        AssertEqual("紫府圆满", Stage("筑基", 4), "筑基 stage 4");
        AssertEqual("初结金丹", Stage("金丹", 0), "金丹 stage 0");
        AssertEqual("温养金丹", Stage("金丹", 1), "金丹 stage 1");
        AssertEqual("金丹圆满", Stage("金丹", 2), "金丹 stage 2");

        if (GameData.Milestones.Any(ms => ms.realm is "元婴" or "化神" or "炼虚"))
            throw new InvalidOperationException("default player milestones still include high realms.");
        AssertEqual(("金丹", 2), (GameData.Milestones[^1].realm, GameData.Milestones[^1].subIdx), "last default milestone");
    }

    static void RunElementV510()
    {
        var artElement = typeof(GameData.ArtConfig).GetProperty("Element");
        if (artElement == null)
            throw new InvalidOperationException("ArtConfig.Element is missing.");

        var divineElement = typeof(GameData.DivineConfig).GetProperty("Element");
        if (divineElement == null)
            throw new InvalidOperationException("DivineConfig.Element is missing.");

        AssertEqual("土", artElement.GetValue(GameData.PhysicalArt), "裂石拳 element");
        AssertEqual("水", artElement.GetValue(GameData.WaterArt), "川流劲 element");
        AssertEqual("雷", artElement.GetValue(GameData.YuqingLeijieArt), "九霄雷罚 element");

        var multiplier = typeof(GameData).GetMethod(
            "ElementDamageMultiplier",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string), typeof(string), typeof(string) },
            modifiers: null);
        if (multiplier == null)
            throw new InvalidOperationException("GameData.ElementDamageMultiplier is missing.");

        double FireWoodVsMetal() => (double)multiplier.Invoke(null, new object[] { "火", "见素抱朴经", "通神三玄礼录" })!;
        double ThunderVsWood() => (double)multiplier.Invoke(null, new object[] { "雷", "疾雷破山经", "见素抱朴经" })!;
        double WaterThunderVsWood() => (double)multiplier.Invoke(null, new object[] { "水", "疾雷破山经", "见素抱朴经" })!;
        double Chaos() => (double)multiplier.Invoke(null, new object[] { "混沌", "疾雷破山经", "见素抱朴经" })!;
        double None() => (double)multiplier.Invoke(null, new object[] { "", "疾雷破山经", "见素抱朴经" })!;

        AssertClose(1.21, FireWoodVsMetal(), 0.0001, "木功法施火术打金功法");
        AssertClose(1.15, ThunderVsWood(), 0.0001, "雷术法打木功法触发变异外圈");
        AssertClose(1.045, WaterThunderVsWood(), 0.0001, "雷功法施水术打木功法");
        AssertClose(1.0, Chaos(), 0.0001, "混沌属性不参与匹配");
        AssertClose(1.0, None(), 0.0001, "无属性不参与匹配");
    }

    static void RunGoldenCoreTq013B()
    {
        var resolver = typeof(GameData).GetMethod(
            "ResolveGoldenCoreProfile",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(int), typeof(string), typeof(Dictionary<string, double>) },
            modifiers: null);
        if (resolver == null)
            throw new InvalidOperationException("GameData.ResolveGoldenCoreProfile is missing.");

        var physicalWeights = new Dictionary<string, double>
        {
            ["根骨"] = 0.45, ["魂魄"] = 0.15, ["神识"] = 0.15, ["资质"] = 0.15, ["气运"] = 0.10
        };
        var profile = resolver.Invoke(null, new object[] { 130, "黄品", physicalWeights })!;
        AssertProfile(profile, "成丹", "自然丹籍", "候选未占据", "坤岳丹", "土", "一品");

        var temporary = resolver.Invoke(null, new object[] { 52, "无道基", physicalWeights })!;
        AssertProfile(temporary, "成丹", "暂寄丹籍", "暂寄", "坤岳丹", "土", "六品");

        var failed = resolver.Invoke(null, new object[] { 12, "无道基", physicalWeights })!;
        AssertProfile(failed, "未成丹", "", "未成丹", "", "", "");

        var character = Character.Create("TQ-013B", new() { ["根骨"] = 20, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 11, ["气运"] = 8 }, "physical");
        character.Realm = "金丹";
        WriteProperty(character, "FormedState", "成丹");
        WriteProperty(character, "DanJiType", "暂寄丹籍");
        character.AssignArts();
        if (string.IsNullOrEmpty(character.DivineName))
            throw new InvalidOperationException("Character.AssignArts still depends on legacy golden core grade.");
    }

    static void RunGoldenCoreTq015C()
    {
        var resolver = typeof(GameData).GetMethod(
            "ResolveGoldenCoreProfile",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(int), typeof(string), typeof(Dictionary<string, double>) },
            modifiers: null);
        if (resolver == null)
            throw new InvalidOperationException("GameData.ResolveGoldenCoreProfile is missing.");

        var physicalWeights = new Dictionary<string, double>
        {
            ["根骨"] = 0.45, ["魂魄"] = 0.15, ["神识"] = 0.15, ["资质"] = 0.15, ["气运"] = 0.10
        };
        var profile = resolver.Invoke(null, new object[] { 130, "黄品", physicalWeights })!;
        AssertEqual("土", ReadProperty(profile, "TargetBranch"), "golden core target branch");
        AssertEqual("source", ReadProperty(profile, "TargetSeat"), "golden core target seat");
        AssertEqual("土·源位（安忍地）", ReadProperty(profile, "SeatName"), "golden core seat name");
        AssertEqual("承接范围、排异规则与过载症状", ReadProperty(profile, "DanPivot"), "golden core dan pivot");

        var character = Character.Create("TQ-015C", new() { ["根骨"] = 20, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 11, ["气运"] = 8 }, "physical");
        AssertEqual("", ReadProperty(character, "TargetBranch"), "character target branch default");
        WriteProperty(character, "TargetBranch", "土");
        WriteProperty(character, "TargetSeat", "source");
        WriteProperty(character, "SeatName", "土·源位（安忍地）");
        WriteProperty(character, "DanPivot", "承接范围、排异规则与过载症状");
        AssertEqual("土·源位（安忍地）", ReadProperty(character, "SeatName"), "character stores seat name");
    }

    static void RunGoldenCoreTq015C3()
    {
        var resolver = typeof(GameData).GetMethod(
            "ResolveGoldenCoreProfile",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(int), typeof(string), typeof(Dictionary<string, double>) },
            modifiers: null);
        if (resolver == null)
            throw new InvalidOperationException("GameData.ResolveGoldenCoreProfile is missing.");

        var physicalWeights = new Dictionary<string, double>
        {
            ["根骨"] = 0.45, ["魂魄"] = 0.15, ["神识"] = 0.15, ["资质"] = 0.15, ["气运"] = 0.10
        };

        var natural = resolver.Invoke(null, new object[] { 130, "黄品", physicalWeights })!;
        AssertEqual("自然候选", ReadProperty(natural, "NaturalDanJiCandidateState"), "natural candidate state");
        AssertEqual("natural_candidate", ReadProperty(natural, "SeatAccessState"), "natural seat access state");
        AssertEqual("待争席", ReadProperty(natural, "SeatCompetitionState"), "natural seat competition state");
        AssertEqual("未占据", ReadProperty(natural, "FinalOccupancyState"), "natural final occupancy state");
        AssertEqual("未接入紫府神通/府位闭环，阈值待验证", ReadProperty(natural, "ZifuEligibilityNote"), "zifu eligibility note");

        var naturalScore = ReadProperty(natural, "SeatCompetitionScore");
        AssertEqual(0, naturalScore, "natural seat competition score stays diagnostic-only");

        var granted = resolver.Invoke(null, new object[] { 75, "黄品", physicalWeights })!;
        AssertEqual("非自然候选", ReadProperty(granted, "NaturalDanJiCandidateState"), "granted natural candidate state");
        AssertEqual("granted", ReadProperty(granted, "SeatAccessState"), "granted seat access state");
        AssertEqual("不参与自然争席", ReadProperty(granted, "SeatCompetitionState"), "granted competition state");
        AssertEqual("受敕承位", ReadProperty(granted, "FinalOccupancyState"), "granted final occupancy state");

        var temporary = resolver.Invoke(null, new object[] { 52, "无道基", physicalWeights })!;
        AssertEqual("temporary", ReadProperty(temporary, "SeatAccessState"), "temporary seat access state");
        AssertEqual("暂寄", ReadProperty(temporary, "FinalOccupancyState"), "temporary final occupancy state");

        var failed = resolver.Invoke(null, new object[] { 12, "无道基", physicalWeights })!;
        AssertEqual("none", ReadProperty(failed, "SeatAccessState"), "failed seat access state");
        AssertEqual("未成丹", ReadProperty(failed, "FinalOccupancyState"), "failed final occupancy state");

        var character = Character.Create("TQ-015C-3", new() { ["根骨"] = 20, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 11, ["气运"] = 8 }, "physical");
        WriteProperty(character, "NaturalDanJiCandidateState", "自然候选");
        WriteProperty(character, "SeatAccessState", "natural_candidate");
        WriteProperty(character, "SeatCompetitionState", "待争席");
        WriteProperty(character, "FinalOccupancyState", "未占据");
        WriteProperty(character, "SeatCompetitionScore", naturalScore);
        WriteProperty(character, "ZifuEligibilityNote", "未接入紫府神通/府位闭环，阈值待验证");
        AssertEqual("待争席", ReadProperty(character, "SeatCompetitionState"), "character stores competition state");
    }

    static void RunGoldenCoreTq015C4()
    {
        var resolver = typeof(GameData).GetMethod(
            "ResolveGoldenCoreProfile",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(int), typeof(string), typeof(Dictionary<string, double>) },
            modifiers: null);
        if (resolver == null)
            throw new InvalidOperationException("GameData.ResolveGoldenCoreProfile is missing.");

        var physicalWeights = new Dictionary<string, double>
        {
            ["根骨"] = 0.45, ["魂魄"] = 0.15, ["神识"] = 0.15, ["资质"] = 0.15, ["气运"] = 0.10
        };

        var natural = resolver.Invoke(null, new object[] { 130, "黄品", physicalWeights })!;
        AssertEqual(0, ReadProperty(natural, "ZifuDivineArtCount"), "natural zifu divine art count");
        AssertEqual(0, ReadProperty(natural, "ZifuPalaceCoverageCount"), "natural zifu palace coverage count");
        AssertEqual("未接入", ReadProperty(natural, "ZifuCoreLoopState"), "natural zifu core loop state");
        AssertEqual("未接入紫府神通/府位闭环，阈值待验证", ReadProperty(natural, "ZifuEligibilityNote"), "natural zifu eligibility note");

        var character = Character.Create("TQ-015C-4", new() { ["根骨"] = 20, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 11, ["气运"] = 8 }, "physical");
        AssertEqual(0, ReadProperty(character, "ZifuDivineArtCount"), "character zifu divine art count default");
        AssertEqual(0, ReadProperty(character, "ZifuPalaceCoverageCount"), "character zifu palace coverage count default");
        AssertEqual("未接入", ReadProperty(character, "ZifuCoreLoopState"), "character zifu core loop default");
        AssertEqual("未接入紫府神通/府位闭环，阈值待验证", ReadProperty(character, "ZifuEligibilityNote"), "character zifu note default");
    }

    static void RunGoldenCoreTq015C6()
    {
        var statsType = Type.GetType("BattleSim.SeatCompetitionSampleStats");
        if (statsType == null)
            throw new InvalidOperationException("SeatCompetitionSampleStats is missing.");

        var summarize = statsType.GetMethod(
            "Summarize",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(IEnumerable<Character>), typeof(int) },
            modifiers: null);
        if (summarize == null)
            throw new InvalidOperationException("SeatCompetitionSampleStats.Summarize is missing.");

        var natural = Character.Create("natural", new() { ["根骨"] = 20, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 11, ["气运"] = 8 }, "physical");
        WriteProperty(natural, "FormedState", "成丹");
        WriteProperty(natural, "SeatName", "土·源位（安忍地）");
        WriteProperty(natural, "NaturalDanJiCandidateState", "自然候选");
        WriteProperty(natural, "SeatAccessState", "natural_candidate");
        WriteProperty(natural, "SeatCompetitionState", "待争席");
        WriteProperty(natural, "FinalOccupancyState", "未占据");
        WriteProperty(natural, "ZifuCoreLoopState", "未接入");
        WriteProperty(natural, "ZifuEligibilityNote", "未接入紫府神通/府位闭环，阈值待验证");

        var granted = Character.Create("granted", new() { ["根骨"] = 20, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 11, ["气运"] = 8 }, "physical");
        WriteProperty(granted, "FormedState", "成丹");
        WriteProperty(granted, "SeatName", "土·源位（安忍地）");
        WriteProperty(granted, "SeatAccessState", "granted");
        WriteProperty(granted, "FinalOccupancyState", "受敕承位");

        var temporary = Character.Create("temporary", new() { ["根骨"] = 20, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 11, ["气运"] = 8 }, "physical");
        WriteProperty(temporary, "FormedState", "成丹");
        WriteProperty(temporary, "SeatName", "土·源位（安忍地）");
        WriteProperty(temporary, "SeatAccessState", "temporary");
        WriteProperty(temporary, "FinalOccupancyState", "暂寄");

        var sparse = Character.Create("sparse", new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 20, ["气运"] = 8 }, "physical");
        WriteProperty(sparse, "FormedState", "成丹");
        WriteProperty(sparse, "SeatName", "木·源位（报春根）");
        WriteProperty(sparse, "NaturalDanJiCandidateState", "自然候选");
        WriteProperty(sparse, "SeatAccessState", "natural_candidate");

        var unformed = Character.Create("unformed", new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8 }, "physical");

        var rows = (System.Collections.IEnumerable)summarize.Invoke(null, new object[] { new[] { natural, granted, temporary, sparse, unformed }, 2 })!;
        var bySeat = rows.Cast<object>().ToDictionary(row => (string)ReadProperty(row, "SeatName"));

        var earth = bySeat["土·源位（安忍地）"];
        AssertEqual(3, ReadProperty(earth, "SampleCount"), "earth seat sample count");
        AssertEqual(1, ReadProperty(earth, "NaturalCandidateCount"), "earth natural candidates");
        AssertEqual(1, ReadProperty(earth, "GrantedCount"), "earth granted count");
        AssertEqual(1, ReadProperty(earth, "TemporaryCount"), "earth temporary count");
        AssertEqual(0, ReadProperty(earth, "UnformedCount"), "earth unformed count");
        AssertEqual("样本可用", ReadProperty(earth, "NaReason"), "earth NA reason");
        AssertEqual(3, ReadProperty(earth, "ZifuPendingCount"), "earth zifu pending count");

        var wood = bySeat["木·源位（报春根）"];
        AssertEqual("样本不足(<2)", ReadProperty(wood, "NaReason"), "wood NA reason");

        var missing = bySeat["未成丹/无目标席位"];
        AssertEqual(1, ReadProperty(missing, "UnformedCount"), "unformed row count");
        AssertEqual("未成丹", ReadProperty(missing, "NaReason"), "unformed NA reason");
    }

    static void RunGoldenCoreValidationBaseNgcBase01()
    {
        var fixtureType = Type.GetType("BattleSim.GoldenCoreValidationFixture");
        if (fixtureType == null)
            throw new InvalidOperationException("GoldenCoreValidationFixture is missing.");

        var create = fixtureType.GetMethod("CreateNaturalCandidate", BindingFlags.Public | BindingFlags.Static);
        if (create == null)
            throw new InvalidOperationException("GoldenCoreValidationFixture.CreateNaturalCandidate is missing.");

        var sample = create.Invoke(null, null)!;
        var profile = ReadProperty(sample, "GoldenCore");
        AssertEqual("成丹", ReadProperty(profile, "FormedState"), "fixture golden core formed state");
        AssertEqual("自然候选", ReadProperty(profile, "NaturalDanJiCandidateState"), "fixture natural candidate state");
        AssertEqual("未占据", ReadProperty(profile, "FinalOccupancyState"), "fixture occupancy remains unresolved");
        AssertEqual("承接范围、排异规则与过载症状", ReadProperty(profile, "DanPivot"), "fixture dan pivot");

        var describe = fixtureType.GetMethod("DescribeInputCoverage", BindingFlags.Public | BindingFlags.Static);
        if (describe == null)
            throw new InvalidOperationException("GoldenCoreValidationFixture.DescribeInputCoverage is missing.");

        var rows = ((System.Collections.IEnumerable)describe.Invoke(null, new[] { sample })!)
            .Cast<object>()
            .ToDictionary(row => (string)ReadProperty(row, "Field"));
        AssertEqual("已接入", ReadProperty(rows["成丹状态"], "Availability"), "formed state availability");
        AssertEqual("已接入", ReadProperty(rows["目标席位"], "Availability"), "target seat availability");
        AssertEqual("已接入", ReadProperty(rows["占据状态"], "Availability"), "occupancy availability");
        AssertEqual("已接入", ReadProperty(rows["丹枢接口"], "Availability"), "dan pivot availability");
        AssertEqual("未接入", ReadProperty(rows["紫府神通数量"], "Availability"), "divine art availability");
        AssertEqual("未接入", ReadProperty(rows["府位覆盖"], "Availability"), "palace coverage availability");
    }

    static void RunStageMatrixB3()
    {
        var reportType = Type.GetType("BattleSim.StageCombatReport");
        if (reportType == null)
            throw new InvalidOperationException("StageCombatReport is missing.");

        var selectPools = reportType.GetMethod(
            "SelectPools",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(IEnumerable<IReadOnlyList<Character>>), typeof(string), typeof(int?) },
            modifiers: null);
        if (selectPools == null)
            throw new InvalidOperationException("StageCombatReport.SelectPools is missing.");

        var pools = new[]
        {
            new List<Character>
            {
                StageCharacter("a", "筑基", 0),
                StageCharacter("b", "筑基", 4),
                StageCharacter("c", "金丹", 0)
            },
            new List<Character>
            {
                StageCharacter("d", "筑基", 4),
                StageCharacter("e", "练气", 8)
            }
        };

        var zhujiPools = ((System.Collections.IEnumerable)selectPools.Invoke(null, new object[] { pools, "筑基", null })!)
            .Cast<IReadOnlyList<Character>>()
            .ToArray();
        AssertEqual(2, zhujiPools[0].Count, "zhuji pool 0 count");
        AssertEqual(1, zhujiPools[1].Count, "zhuji pool 1 count");

        var zifuPools = ((System.Collections.IEnumerable)selectPools.Invoke(null, new object[] { pools, "筑基", 4 })!)
            .Cast<IReadOnlyList<Character>>()
            .ToArray();
        AssertEqual(1, zifuPools[0].Count, "zifu pool 0 count");
        AssertEqual("b", zifuPools[0][0].Name, "zifu pool 0 sample");
        AssertEqual(1, zifuPools[1].Count, "zifu pool 1 count");
        AssertEqual("d", zifuPools[1][0].Name, "zifu pool 1 sample");
    }

    static void RunSecondaryDetachA4()
    {
        var weights = new Dictionary<string, double>
        {
            ["根骨"] = 0.45,
            ["魂魄"] = 0.30,
            ["神识"] = 0.10,
            ["资质"] = 0.10,
            ["气运"] = 0.05
        };

        Character Finalized(string name, int mind, int talent, int luck)
        {
            var character = Character.Create(name, new()
            {
                ["根骨"] = 12,
                ["魂魄"] = 12,
                ["神识"] = mind,
                ["资质"] = talent,
                ["气运"] = luck
            }, "physical");
            character.FinalizeStats("筑基", 0, "中品", weights);
            return character;
        }

        var baseline = Finalized("baseline", 3, 3, 3);
        var highInnate = Finalized("high-innate", 40, 40, 40);

        AssertClose(9.0, highInnate.Secondary.GetValueOrDefault("格挡率", 0), 0.0001, "gongfa-derived block remains");
        AssertClose(9.0, highInnate.Secondary.GetValueOrDefault("魂盾率", 0), 0.0001, "gongfa-derived soul shield remains");
        AssertClose(1.5, highInnate.Secondary.GetValueOrDefault("闪避率", 0), 0.0001, "gongfa luck affinity adds dodge");
        AssertClose(3.0, highInnate.Secondary.GetValueOrDefault("暴击率", 0), 0.0001, "gongfa mind affinity adds crit rate");
        AssertClose(8.0, highInnate.Secondary.GetValueOrDefault("暴击伤害", 0), 0.0001, "gongfa talent affinity adds crit damage");
        AssertClose(
            baseline.Secondary.GetValueOrDefault("闪避率", 0),
            highInnate.Secondary.GetValueOrDefault("闪避率", 0),
            0.0001,
            "dodge is independent from innate luck");
    }

    static void RunGoldenCoreSuppressionTq015C8()
    {
        var statsType = Type.GetType("BattleSim.GoldenCoreSuppressionExitStats");
        if (statsType == null)
            throw new InvalidOperationException("GoldenCoreSuppressionExitStats is missing.");

        var classify = statsType.GetMethod(
            "ClassifyExit",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(Character), typeof(Character) },
            modifiers: null);
        if (classify == null)
            throw new InvalidOperationException("GoldenCoreSuppressionExitStats.ClassifyExit is missing.");

        var zifu = StageCharacter("zifu", "筑基", 4);
        zifu.Style = "taiyi_fuxiu";
        zifu.GongFaName = "云篆度人经";
        zifu.ArtElement = "风";

        var gold = StageCharacter("gold", "金丹", 0);
        WriteProperty(gold, "FormedState", "成丹");
        WriteProperty(gold, "DanJiType", "自然丹籍");
        WriteProperty(gold, "SeatCompetitionState", "待争席");
        WriteProperty(gold, "FinalOccupancyState", "未占据");

        var route = classify.Invoke(null, new object[] { zifu, gold })!;
        AssertEqual("削位+封丹", ReadProperty(route, "ExitRoute"), "suppression exit route");
        AssertEqual("待争席丹籍可被削位；符修具备封丹手段", ReadProperty(route, "Reason"), "suppression exit reason");
        AssertEqual(true, ReadProperty(route, "IsTacticalExit"), "suppression exit flag");
    }

    static void RunGoldenCoreSuppressionTq015C9()
    {
        var statsType = Type.GetType("BattleSim.GoldenCoreSuppressionExitStats");
        if (statsType == null)
            throw new InvalidOperationException("GoldenCoreSuppressionExitStats is missing.");

        var switchesType = Type.GetType("BattleSim.GoldenCoreSuppressionSwitches");
        if (switchesType == null)
            throw new InvalidOperationException("GoldenCoreSuppressionSwitches is missing.");

        var createScenario = statsType.GetMethod(
            "CreateTacticalScenario",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(Character), typeof(Character), switchesType },
            modifiers: null);
        if (createScenario == null)
            throw new InvalidOperationException("GoldenCoreSuppressionExitStats.CreateTacticalScenario is missing.");

        var disabledSwitches = Activator.CreateInstance(switchesType, new object[] { false, false })!;
        var enabledSwitches = Activator.CreateInstance(switchesType, new object[] { true, true })!;

        var zifu = StageCharacter("zifu", "筑基", 4);
        zifu.Style = "taiyi_fuxiu";
        zifu.GongFaName = "云篆度人经";

        var gold = StageCharacter("gold", "金丹", 0);
        gold.Primary["HP"] = 1000;
        gold.Primary["MP"] = 100;
        gold.Primary["肉攻"] = 120;
        gold.Primary["神攻"] = 160;
        gold.DivineName = "丹域镇压";
        WriteProperty(gold, "SeatCompetitionState", "待争席");
        WriteProperty(gold, "FinalOccupancyState", "未占据");

        var disabled = createScenario.Invoke(null, new object[] { zifu, gold, disabledSwitches })!;
        AssertEqual(false, ReadProperty(disabled, "HasActiveSwitch"), "disabled switch active flag");
        AssertEqual("未启用", ReadProperty(disabled, "AppliedRoutes"), "disabled switch routes");
        var disabledGold = (Character)ReadProperty(disabled, "Gold");
        AssertEqual("丹域镇压", disabledGold.DivineName, "disabled switch keeps gold divine art");
        AssertEqual(100, disabledGold.Primary["MP"], "disabled switch keeps gold MP");

        var enabled = createScenario.Invoke(null, new object[] { zifu, gold, enabledSwitches })!;
        AssertEqual(true, ReadProperty(enabled, "HasActiveSwitch"), "enabled switch active flag");
        AssertEqual("削位+封丹", ReadProperty(enabled, "AppliedRoutes"), "enabled switch routes");
        var enabledGold = (Character)ReadProperty(enabled, "Gold");
        AssertEqual("", enabledGold.DivineName, "dan seal disables gold divine art");
        AssertEqual(85, enabledGold.Primary["MP"], "seat erosion reduces gold MP only");
        AssertEqual(1000, enabledGold.Primary["HP"], "tactical switches do not change gold HP");
        AssertEqual(120, enabledGold.Primary["肉攻"], "tactical switches do not change gold physical attack");
        AssertEqual("丹域镇压", gold.DivineName, "tactical scenario does not mutate source gold");
        AssertEqual(100, gold.Primary["MP"], "tactical scenario keeps source gold MP");
    }

    static void RunGoldenCoreSuppressionTq015C10()
    {
        var statsType = Type.GetType("BattleSim.GoldenCoreSuppressionExitStats");
        if (statsType == null)
            throw new InvalidOperationException("GoldenCoreSuppressionExitStats is missing.");

        var switchesType = Type.GetType("BattleSim.GoldenCoreSuppressionSwitches");
        if (switchesType == null)
            throw new InvalidOperationException("GoldenCoreSuppressionSwitches is missing.");

        var profileType = Type.GetType("BattleSim.GoldenCoreSuppressionSwitchProfile");
        if (profileType == null)
            throw new InvalidOperationException("GoldenCoreSuppressionSwitchProfile is missing.");

        var defaultProfiles = statsType.GetMethod(
            "DefaultSwitchProfiles",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (defaultProfiles == null)
            throw new InvalidOperationException("GoldenCoreSuppressionExitStats.DefaultSwitchProfiles is missing.");

        var profileRows = ((System.Collections.IEnumerable)defaultProfiles.Invoke(null, Array.Empty<object>())!)
            .Cast<object>()
            .ToArray();
        AssertSequence(
            new[] { "削位15", "削位30", "削位45" },
            profileRows.Select(row => (string)ReadProperty(row, "Label")).ToArray(),
            "default switch profile labels");
        AssertClose(0.85, (double)ReadProperty(profileRows[0], "SeatErosionMpRetainRate"), 0.0001, "default light profile retain rate");
        AssertClose(0.70, (double)ReadProperty(profileRows[1], "SeatErosionMpRetainRate"), 0.0001, "default middle profile retain rate");
        AssertClose(0.55, (double)ReadProperty(profileRows[2], "SeatErosionMpRetainRate"), 0.0001, "default heavy profile retain rate");

        var createScenario = statsType.GetMethod(
            "CreateTacticalScenario",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(Character), typeof(Character), profileType },
            modifiers: null);
        if (createScenario == null)
            throw new InvalidOperationException("GoldenCoreSuppressionExitStats.CreateTacticalScenario profile overload is missing.");

        var enabledSwitches = Activator.CreateInstance(switchesType, new object[] { true, true })!;
        var heavyProfile = Activator.CreateInstance(profileType, new object[] { "削位45", enabledSwitches, 0.45 })!;

        var zifu = StageCharacter("zifu", "筑基", 4);
        zifu.Style = "taiyi_fuxiu";
        zifu.GongFaName = "云篆度人经";

        var gold = StageCharacter("gold", "金丹", 0);
        gold.Primary["HP"] = 1000;
        gold.Primary["MP"] = 100;
        gold.Primary["肉攻"] = 120;
        gold.Primary["神攻"] = 160;
        gold.DivineName = "丹域镇压";
        WriteProperty(gold, "SeatCompetitionState", "待争席");
        WriteProperty(gold, "FinalOccupancyState", "未占据");

        var scenario = createScenario.Invoke(null, new object[] { zifu, gold, heavyProfile })!;
        AssertEqual(true, ReadProperty(scenario, "HasActiveSwitch"), "profile switch active flag");
        AssertEqual("削位+封丹", ReadProperty(scenario, "AppliedRoutes"), "profile switch routes");
        AssertEqual("削位45", ReadProperty(scenario, "ProfileLabel"), "profile label");
        var scenarioGold = (Character)ReadProperty(scenario, "Gold");
        AssertEqual("", scenarioGold.DivineName, "profile dan seal disables gold divine art");
        AssertEqual(45, scenarioGold.Primary["MP"], "profile seat erosion strength reduces gold MP");
        AssertEqual(1000, scenarioGold.Primary["HP"], "profile strength does not change gold HP");
        AssertEqual(120, scenarioGold.Primary["肉攻"], "profile strength does not change gold physical attack");
        AssertEqual("丹域镇压", gold.DivineName, "profile scenario does not mutate source gold");
        AssertEqual(100, gold.Primary["MP"], "profile scenario keeps source gold MP");
    }

    static void RunCtReactionTq052()
    {
        var (fastWins, slowWins, _) = Combat.Simulate2v2(
            HexBattlefield.CreateTechnicalFixture(),
            CtTestCharacter("fast-1", 20),
            CtTestCharacter("fast-2", 20),
            CtTestCharacter("slow-1", 10),
            CtTestCharacter("slow-2", 10),
            rounds: 1);

        AssertEqual(100.0, fastWins, "higher reaction team takes the first action");
        AssertEqual(0.0, slowWins, "lower reaction team cannot take the first action");

        var (firstWins, secondWins, _) = Combat.Simulate2v2(
            HexBattlefield.CreateTechnicalFixture(),
            CtTestCharacter("first-1", 10),
            CtTestCharacter("first-2", 10),
            CtTestCharacter("second-1", 10),
            CtTestCharacter("second-2", 10),
            rounds: 1);

        AssertEqual(100.0, firstWins, "equal reaction resolves in input order");
        AssertEqual(0.0, secondWins, "equal reaction order is stable");
    }

    static void RunGoldenCoreAssemblyNjdrule01A()
    {
        var oneSeat = LoadGoldenCoreFixture("jd.valid.one-mansion-one-seat");
        var threeSeats = LoadGoldenCoreFixture("jd.valid.three-mansion-three-seats");
        var fiveMansions = LoadGoldenCoreFixture("jd.valid.five-mansion-three-seats");

        AssertAssembly(oneSeat, expectedMansionAbilities: 1, expectedSeats: 1, "one mansion fixture");
        AssertAssembly(threeSeats, expectedMansionAbilities: 3, expectedSeats: 3, "three mansion fixture");
        AssertAssembly(fiveMansions, expectedMansionAbilities: 5, expectedSeats: 3, "five mansion fixture");

        AssertFixtureRejected("jd.invalid.fourth-stable-position", "JD_STABLE_POSITION_LIMIT");
        AssertFixtureRejected("jd.invalid.second-core", "JD_CORE_NOT_UNIQUE");
        AssertFixtureRejected("jd.invalid.second-danxiang", "JD_DANXIANG_NOT_UNIQUE");
        AssertFixtureRejected("jd.invalid.shared-instance-ledger", "JD_ABILITY_LEDGER_OWNERSHIP_INVALID");

        var sharedReferenceInput = threeSeats with
        {
            StableSeats = threeSeats.StableSeats
                .Select(seat => seat.PositionType == GoldenCoreSeatType.Transformation
                    ? seat with { AuxiliaryCarrierAbilityInstanceIds = new[] { "guardian_ming" } }
                    : seat)
                .ToArray(),
            DanxiangAbilityInstanceIds = new[] { "guardian_ming" },
        };
        var sharedReferenceAssembly = GoldenCoreAssembly.Create(sharedReferenceInput);
        var runtimeLedger = sharedReferenceAssembly.CreateRuntimeLedger(initialResource: 100);
        var mingLedger = runtimeLedger.Get("guardian_ming");
        if (!ReferenceEquals(mingLedger, runtimeLedger.Get("guardian_ming")))
            throw new InvalidOperationException("same ability instance must resolve to one runtime ledger.");
        if (!mingLedger.TrySpendResource(30) || mingLedger.Resource != 70 || runtimeLedger.Get("guardian_hun").Resource != 100)
            throw new InvalidOperationException("ability resource ledgers must be independent by ability instance id.");
        mingLedger.StartCooldown(3);
        runtimeLedger.TickCooldowns();
        if (mingLedger.Cooldown != 2 || runtimeLedger.Get("guardian_hun").Cooldown != 0)
            throw new InvalidOperationException("ability cooldown ledgers must be independent by ability instance id.");
        mingLedger.AddConflictReserve(9);
        if (!mingLedger.TrySpendConflictReserve(4) || mingLedger.ConflictReserve != 5 || runtimeLedger.Get("guardian_hun").ConflictReserve != 0)
            throw new InvalidOperationException("conflict reserves must remain on the unique ability instance ledger.");

        var character = Character.Create(
            "N-JD-RULE-01A",
            new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8 },
            "physical");
        character.AssignGoldenCoreAssembly(sharedReferenceAssembly, "guardian_ming", "guardian_hun");
        AssertEqual("guardian_ming", character.ArtAbilityInstanceId, "character art ability ledger binding");
        AssertEqual("guardian_hun", character.DivineAbilityInstanceId, "character divine ability ledger binding");
    }

    static void RunGoldenCoreConflictNjdrule01B()
    {
        var missingConflictAssembly = GoldenCoreAssembly.Create(LoadGoldenCoreFixture("jd.valid.one-mansion-one-seat"));
        var missingConflictRuntime = missingConflictAssembly.CreateRuntimeLedger(initialResource: 100);
        var illegal = GoldenCoreConflictCandidates.Prepare(
            missingConflictAssembly,
            missingConflictRuntime,
            CreateConflictCandidateInput("illegal", "conflict-cost-illegal"));
        if (illegal.IsEligible || illegal.Candidate != null || illegal.RejectionCode != "JD_CONFLICT_RESERVE_UNAVAILABLE")
            throw new InvalidOperationException("candidates without paired conflict reserve data must be filtered before sorting.");

        var first = CreateConflictParticipant("first", reserve: 6);
        var second = CreateConflictParticipant("second", reserve: 4);
        var firstInput = CreateConflictCandidateInput("left", "conflict-cost-first");
        var secondInput = CreateConflictCandidateInput("right", "conflict-cost-second");
        var qte = CreateRuleConflictInstance(
            first.Character.PrepareGoldenCoreConflictCandidate(first.RuntimeLedger, firstInput),
            second.Character.PrepareGoldenCoreConflictCandidate(second.RuntimeLedger, secondInput)).Decide(null);

        var skippedFirst = CreateConflictParticipant("skip-first", reserve: 6);
        var skippedSecond = CreateConflictParticipant("skip-second", reserve: 4);
        var skippedFirstInput = CreateConflictCandidateInput("left", "conflict-cost-skip-first");
        var skippedSecondInput = CreateConflictCandidateInput("right", "conflict-cost-skip-second");
        var skipped = CreateRuleConflictInstance(
            skippedFirst.Character.PrepareGoldenCoreConflictCandidate(skippedFirst.RuntimeLedger, skippedFirstInput),
            skippedSecond.Character.PrepareGoldenCoreConflictCandidate(skippedSecond.RuntimeLedger, skippedSecondInput)).Decide(null);

        AssertConflictDecision(qte, "left", "PULSE_ADVANTAGE", 3, 2, 6, 4, true, "QTE direct decision");
        AssertConflictDecision(skipped, "left", "PULSE_ADVANTAGE", 3, 2, 6, 4, true, "skip direct decision");
        if (first.RuntimeLedger.Get("guardian_ming").ConflictReserve != 6 || second.RuntimeLedger.Get("guardian_ming").ConflictReserve != 4)
            throw new InvalidOperationException("shared conflict decisions must not mutate BattleSim ledgers.");

        var appliedQte = Combat.ResolveGoldenCoreConflict(
            first.Character, first.RuntimeLedger, firstInput, second.Character, second.RuntimeLedger, secondInput, GoldenCoreConflictInputMode.Qte);
        var appliedSkip = Combat.ResolveGoldenCoreConflict(
            skippedFirst.Character, skippedFirst.RuntimeLedger, skippedFirstInput, skippedSecond.Character, skippedSecond.RuntimeLedger, skippedSecondInput, GoldenCoreConflictInputMode.Skip);
        AssertConflictDecision(appliedQte, "left", "PULSE_ADVANTAGE", 3, 2, 6, 4, true, "QTE adapter decision");
        AssertConflictDecision(appliedSkip, "left", "PULSE_ADVANTAGE", 3, 2, 6, 4, true, "skip adapter decision");
        AssertConflictRuntime(first.RuntimeLedger, 0, 3, "QTE left");
        AssertConflictRuntime(second.RuntimeLedger, 0, 3, "QTE right");
        AssertConflictRuntime(skippedFirst.RuntimeLedger, 0, 3, "skip left");
        AssertConflictRuntime(skippedSecond.RuntimeLedger, 0, 3, "skip right");

        var sorting = CreateThreeSeatConflictParticipant("sorting", sourceReserve: 7, transformationReserve: 5);
        var sortingResult = CreateRuleConflictInstance(
            sorting.Character.PrepareGoldenCoreConflictCandidate(
                sorting.RuntimeLedger,
                CreateConflictCandidateInput("source", "conflict-cost-sorting-guardian_ming")),
            sorting.Character.PrepareGoldenCoreConflictCandidate(
                sorting.RuntimeLedger,
                CreateConflictCandidateInput(
                "transformation",
                "conflict-cost-sorting-guardian_hun",
                abilityInstanceId: "guardian_hun",
                positionType: GoldenCoreSeatType.Transformation,
                compatibilityProfileId: "compat_transformation"))).Decide(null);
        if (sortingResult.Outcome != RuleConflictOutcome.LeftWins || sortingResult.Reason != "POSITION_TIER" ||
            sorting.RuntimeLedger.Get("guardian_ming").ConflictReserve != 7 || sorting.RuntimeLedger.Get("guardian_hun").ConflictReserve != 5)
        {
            throw new InvalidOperationException("fixed seat ranking must decide before pulse spending and retain independent ledgers.");
        }
    }

    static void RunGoldenCoreChallengeDeathNjdrule01C()
    {
        var grant = CreateChallengeGrant();
        var archive = new CrossTierChallengeArchive(new[] { grant });
        var validRequest = CreateChallengeRequest();
        var authorized = archive.Resolve(validRequest);
        var repeatedAuthorization = archive.Resolve(validRequest);
        if (!authorized.IsEligible || authorized.Reason != "JD_CHALLENGE_AUTHORIZED" ||
            !repeatedAuthorization.IsEligible || repeatedAuthorization.Reason != authorized.Reason ||
            !ReferenceEquals(authorized.Grant, repeatedAuthorization.Grant))
            throw new InvalidOperationException("a valid versioned challenge must authorize deterministically without mutating repeated events.");

        AssertChallengeRejection(
            archive.Resolve(CreateChallengeRequest(grantId: "unknown-grant")),
            "JD_CHALLENGE_GRANT_UNKNOWN",
            "unknown challenge grant");
        AssertChallengeRejection(
            archive.Resolve(CreateChallengeRequest(expectedDefinitionVersion: grant.DefinitionVersion - 1)),
            "JD_CHALLENGE_VERSION_MISMATCH",
            "version mismatch challenge grant");
        AssertChallengeRejection(
            new CrossTierChallengeArchive(new[] { CreateChallengeGrant(expiresAtTick: 11) }).Resolve(validRequest),
            "JD_CHALLENGE_EXPIRED",
            "expired challenge grant");
        AssertChallengeRejection(
            new CrossTierChallengeArchive(new[] { CreateChallengeGrant(isRevoked: true, revocationReason: "fixture-revoked") }).Resolve(validRequest),
            "JD_CHALLENGE_REVOKED",
            "revoked challenge grant");
        AssertChallengeRejection(
            archive.Resolve(CreateChallengeRequest(targetVariableId: "wrong-variable")),
            "JD_CHALLENGE_TARGET_MISMATCH",
            "target mismatch challenge grant");

        foreach (var positionType in new[]
        {
            GoldenCoreSeatType.Source,
            GoldenCoreSeatType.Transformation,
            GoldenCoreSeatType.Domain,
        })
        {
            var participant = CreateThreeSeatConflictParticipant($"death-{positionType}", sourceReserve: 4, transformationReserve: 5);
            var seat = participant.Character.GoldenCoreAssembly.StableSeats[positionType];
            var carrierLedger = participant.RuntimeLedger.Get(seat.PrimaryCarrierAbilityInstanceId);
            if (!carrierLedger.TrySpendResource(25))
                throw new InvalidOperationException("death fixture must establish a resource debit before settlement.");
            carrierLedger.StartCooldown(4);
            carrierLedger.AddConflictReserve(9);

            string deathEventId = $"death-event-{positionType}";
            var death = Combat.ResolveGoldenCoreCarrierDeath(
                participant.Character,
                participant.RuntimeLedger,
                new GoldenCoreCarrierDeathInput(deathEventId, positionType, seat.PrimaryCarrierAbilityInstanceId));
            if (!death.IsSettled || death.Reason != "JD_CARRIER_DEATH_SETTLED" ||
                !participant.Character.IsDead || participant.Character.GoldenCoreAssembly != null ||
                participant.Character.ProtectedGoldenCoreDeath != death)
            {
                throw new InvalidOperationException($"{positionType} carrier death must atomically remove the real position and close the character.");
            }

            var released = death.LedgerDispositions.Single(disposition => disposition.AbilityInstanceId == seat.PrimaryCarrierAbilityInstanceId);
            if (!released.IsReleased || released.Resource != 75 || released.Cooldown != 4 || released.ConflictReserve < 9 ||
                !carrierLedger.IsReleased)
            {
                throw new InvalidOperationException($"{positionType} carrier ledger must release its exact resource, cooldown, and conflict-reserve state once.");
            }
            if (death.LedgerDispositions.Count != 3 || death.LedgerDispositions.Count(disposition => disposition.IsReleased) != 1 ||
                death.LedgerDispositions.Where(disposition => !disposition.IsReleased).Any(disposition => participant.RuntimeLedger.Get(disposition.AbilityInstanceId).IsReleased))
            {
                throw new InvalidOperationException($"{positionType} carrier death must retain unrelated owned ledgers without copying or releasing them.");
            }

            var repeatedDeath = Combat.ResolveGoldenCoreCarrierDeath(
                participant.Character,
                participant.RuntimeLedger,
                new GoldenCoreCarrierDeathInput(deathEventId, positionType, seat.PrimaryCarrierAbilityInstanceId));
            if (!ReferenceEquals(death, repeatedDeath))
                throw new InvalidOperationException($"{positionType} carrier death must be idempotent for the same event id.");

            var (deadWins, survivorWins, turns) = Combat.Simulate(participant.Character, Character.Create("survivor", participant.Character.Innate, "physical"), rounds: 1);
            if (deadWins != 0 || survivorWins != 100 || turns != 0)
                throw new InvalidOperationException("a character closed by real-position death cannot re-enter combat.");
            AssertThrows<InvalidOperationException>(
                () => participant.Character.AssignGoldenCoreAssembly(GoldenCoreAssembly.Create(LoadGoldenCoreFixture("jd.valid.one-mansion-one-seat"))),
                "a protected golden-core death cannot rebind a replacement assembly");
        }
    }

    static void AssertChallengeRejection(CrossTierChallengeResolution resolution, string expectedReason, string label)
    {
        if (resolution.IsEligible || resolution.Reason != expectedReason || resolution.Grant != null)
            throw new InvalidOperationException($"{label} must reject with {expectedReason}.");
    }

    static CrossTierChallengeGrant CreateChallengeGrant(
        string allowedOperationId = "fixture-direct-conflict",
        string targetId = "fixture-higher-rule-target",
        string scopeId = "fixture-scope",
        string beneficiaryId = "challenger-n-jd-rule-01c",
        string realityAnchorId = "fixture-anchor",
        string resourceLedgerRef = "fixture-resource-ledger",
        string capacityLedgerRef = "fixture-capacity-ledger",
        int expiresAtTick = 20,
        bool isRevoked = false,
        string revocationReason = "")
    {
        return new CrossTierChallengeGrant(
            "grant-n-jd-rule-01c",
            7,
            "fixture-higher-rule-variable",
            "challenger-n-jd-rule-01c",
            CrossTierChallengeSourceKind.YuanyingOrthodoxy,
            allowedOperationId,
            targetId,
            scopeId,
            beneficiaryId,
            realityAnchorId,
            resourceLedgerRef,
            capacityLedgerRef,
            2,
            10,
            expiresAtTick,
            isRevoked,
            revocationReason,
            "fixture-yuanying-orthodoxy");
    }

    static CrossTierChallengeRequest CreateChallengeRequest(
        string grantId = "grant-n-jd-rule-01c",
        int expectedDefinitionVersion = 7,
        string targetVariableId = "fixture-higher-rule-variable")
    {
        return new CrossTierChallengeRequest(
            "challenge-event-n-jd-rule-01c",
            grantId,
            expectedDefinitionVersion,
            targetVariableId,
            "challenger-n-jd-rule-01c",
            12);
    }

    static RuleConflictInstance CreateRuleConflictInstance(
        GoldenCoreConflictCandidatePreparation leftPreparation,
        GoldenCoreConflictCandidatePreparation rightPreparation)
    {
        if (!leftPreparation.IsEligible || !rightPreparation.IsEligible)
            throw new InvalidOperationException("direct shared conflict fixtures require prepared candidates.");

        GoldenCoreConflictCandidate left = leftPreparation.Candidate;
        GoldenCoreConflictCandidate right = rightPreparation.Candidate;
        return new RuleConflictInstance(
            RuleConflictInstance.ContractVersionV1,
            "battle-sim:" + left.Input.CandidateId + ":" + right.Input.CandidateId,
            RuleConflictKind.JindanSameVariable,
            "battle-sim-jindan-conflict",
            left.Input.VariableId,
            "jindan-same-variable",
            left.Input.TargetId,
            "battle-sim",
            "battle-sim",
            "battle-sim",
            "battle-sim",
            "battle-sim",
            0,
            ToRuleConflictCandidate(left),
            ToRuleConflictCandidate(right),
            null);
    }

    static RuleConflictCandidate ToRuleConflictCandidate(GoldenCoreConflictCandidate candidate)
    {
        GoldenCoreConflictCandidateInput input = candidate.Input;
        return new RuleConflictCandidate(
            input.CandidateId,
            input.VariableId,
            input.TargetId,
            input.HasVariableAuthority,
            input.HasLegalTarget,
            ConflictPositionRank(input.PositionType),
            input.RealityAnchorRank,
            input.AlreadyPaidCost,
            input.HasActiveContinuousCarrier,
            candidate.RuntimeState.ConflictReserve,
            input.PulseCost,
            input.SettlementCooldown);
    }

    static int ConflictPositionRank(GoldenCoreSeatType positionType)
    {
        return positionType == GoldenCoreSeatType.Source ? 3 :
            positionType == GoldenCoreSeatType.Transformation ? 2 :
            positionType == GoldenCoreSeatType.Domain ? 1 : 0;
    }

    static void AssertConflictDecision(
        RuleConflictDecision decision,
        string winnerCandidateId,
        string reason,
        int leftPulses,
        int rightPulses,
        int leftReserveSpent,
        int rightReserveSpent,
        bool requiresLedgerSettlement,
        string label)
    {
        if (decision.Outcome != RuleConflictOutcome.LeftWins || decision.WinnerCandidateId != winnerCandidateId ||
            decision.Reason != reason || decision.LeftPulses != leftPulses || decision.RightPulses != rightPulses ||
            decision.LeftReserveSpent != leftReserveSpent || decision.RightReserveSpent != rightReserveSpent ||
            decision.LeftSettlementCooldown != 3 || decision.RightSettlementCooldown != 3 ||
            decision.RejectedCandidateCount != 0 || decision.RequiresLedgerSettlement != requiresLedgerSettlement)
        {
            throw new InvalidOperationException(label + " must preserve the shared deterministic conflict decision.");
        }
    }

    static (Character Character, GoldenCoreRuntimeLedger RuntimeLedger) CreateConflictParticipant(string prefix, int reserve)
    {
        var input = LoadGoldenCoreFixture("jd.valid.one-mansion-one-seat") with
        {
            AbilityLedgers = LoadGoldenCoreFixture("jd.valid.one-mansion-one-seat").AbilityLedgers
                .Select(binding => binding with
                {
                    ConflictReserveLedgerRef = $"conflict-reserve-{prefix}",
                    ConflictCostProfileId = $"conflict-cost-{prefix}",
                })
                .ToArray(),
        };
        var character = Character.Create(
            $"N-JD-RULE-01B-{prefix}",
            new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8 },
            "physical");
        character.AssignGoldenCoreAssembly(GoldenCoreAssembly.Create(input));
        var runtimeLedger = character.CreateGoldenCoreRuntimeLedger(initialResource: 100);
        runtimeLedger.Get("guardian_ming").AddConflictReserve(reserve);
        return (character, runtimeLedger);
    }

    static (Character Character, GoldenCoreRuntimeLedger RuntimeLedger) CreateThreeSeatConflictParticipant(
        string prefix,
        int sourceReserve,
        int transformationReserve)
    {
        var fixture = LoadGoldenCoreFixture("jd.valid.three-mansion-three-seats");
        var input = fixture with
        {
            AbilityLedgers = fixture.AbilityLedgers
                .Select(binding => binding with
                {
                    ConflictReserveLedgerRef = $"conflict-reserve-{prefix}-{binding.AbilityInstanceId}",
                    ConflictCostProfileId = $"conflict-cost-{prefix}-{binding.AbilityInstanceId}",
                })
                .ToArray(),
        };
        var character = Character.Create(
            $"N-JD-RULE-01B-{prefix}",
            new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8 },
            "physical");
        character.AssignGoldenCoreAssembly(GoldenCoreAssembly.Create(input));
        var runtimeLedger = character.CreateGoldenCoreRuntimeLedger(initialResource: 100);
        runtimeLedger.Get("guardian_ming").AddConflictReserve(sourceReserve);
        runtimeLedger.Get("guardian_hun").AddConflictReserve(transformationReserve);
        return (character, runtimeLedger);
    }

    static GoldenCoreConflictCandidateInput CreateConflictCandidateInput(
        string candidateId,
        string conflictCostProfileId,
        string abilityInstanceId = "guardian_ming",
        GoldenCoreSeatType positionType = GoldenCoreSeatType.Source,
        string compatibilityProfileId = "compat_source") =>
        new(
            candidateId,
            abilityInstanceId,
            positionType,
            compatibilityProfileId,
            conflictCostProfileId,
            "fixture-variable",
            "fixture-target",
            HasVariableAuthority: true,
            HasLegalTarget: true,
            RealityAnchorRank: 1,
            AlreadyPaidCost: 2,
            HasActiveContinuousCarrier: true,
            PulseCost: 2,
            SettlementCooldown: 3);

    static void AssertConflictRuntime(GoldenCoreRuntimeLedger runtimeLedger, int expectedReserve, int expectedCooldown, string label)
    {
        var state = runtimeLedger.Get("guardian_ming");
        if (state.ConflictReserve != expectedReserve || state.Cooldown != expectedCooldown)
            throw new InvalidOperationException($"{label} must share the selected candidate's reserve and cooldown settlement.");
    }

    static void AssertAssembly(GoldenCoreAssemblyInput input, int expectedMansionAbilities, int expectedSeats, string label)
    {
        var assembly = GoldenCoreAssembly.Create(input);
        AssertEqual(expectedMansionAbilities, assembly.AbilityLedgers.Count, $"{label} ability ledger count");
        AssertEqual(expectedSeats, assembly.StableSeats.Count, $"{label} stable seat count");
        AssertEqual(input.CoreBinding.JindanInstanceId, assembly.Danxiang.JindanInstanceId, $"{label} unique core and danxiang");
        if (assembly.StableSeats.Values.Any(seat => !assembly.AbilityLedgers.ContainsKey(seat.PrimaryCarrierAbilityInstanceId)))
            throw new InvalidOperationException($"{label}: a stable seat has no owned primary carrier ledger.");
    }

    static void AssertFixtureRejected(string fixtureId, string expectedCode)
    {
        try
        {
            GoldenCoreAssembly.Create(LoadGoldenCoreFixture(fixtureId));
        }
        catch (GoldenCoreAssemblyException ex) when (ex.Code == expectedCode)
        {
            return;
        }

        throw new InvalidOperationException($"{fixtureId} must fail with {expectedCode}.");
    }

    static GoldenCoreAssemblyInput LoadGoldenCoreFixture(string fixtureId)
    {
        var jindanRow = ReadFixtureRows("JindanStaticStates.fixture.csv")
            .Single(row => row["fixtureId"] == fixtureId);
        var foundationRow = ReadFixtureRows("FoundationPurpleMansionStates.fixture.csv")
            .Single(row => row["fixtureId"] == fixtureId);
        if (jindanRow["characterId"] != foundationRow["characterId"] ||
            jindanRow["foundationPurpleMansionStateRef"] != foundationRow["foundationInstanceId"])
        {
            throw new InvalidOperationException($"{fixtureId} does not pair one Jindan row with its frozen foundation input.");
        }

        var mansionAbilityIds = ParseCompleteMansionAbilityIds(jindanRow["mansionInputs"], fixtureId);
        int foundationCompleteMansionCount = foundationRow["mansionStates"]
            .Split('|')
            .Count(item => item.Split('~').Length > 1 && item.Split('~')[1] == "COMPLETE");
        if (mansionAbilityIds.Count != foundationCompleteMansionCount)
            throw new InvalidOperationException($"{fixtureId} has inconsistent Jindan and foundation mansion inputs.");

        var core = ParseParts(jindanRow["jindanCoreBinding"], 5, "JD_CORE_NOT_UNIQUE", fixtureId, "jindanCoreBinding");
        var danxiang = ParseParts(jindanRow["danxiang"], 5, "JD_DANXIANG_NOT_UNIQUE", fixtureId, "danxiang");
        var seats = jindanRow["stablePositionBindings"]
            .Split('|')
            .Select(item => ParseSeat(item, fixtureId))
            .ToArray();
        var ledgers = jindanRow["abilityLedgerBindings"]
            .Split('|')
            .Select(item => ParseLedger(item, fixtureId))
            .ToArray();

        return new GoldenCoreAssemblyInput(
            new JindanCoreBinding(core[0], core[1], core[2], core[3], int.Parse(core[4])),
            new JindanDanxiangBinding(danxiang[0], danxiang[1], danxiang[2], NoneToEmpty(danxiang[3]), danxiang[4]),
            seats,
            ledgers,
            mansionAbilityIds,
            Array.Empty<string>());
    }

    static readonly string[] AttackProfileHeaders = CombatRuleSet.AttackColumns;
    static readonly string[] RecoveryFixtureFields = { "spiritRecoveryMultiplier", "minimumSpiritRecovery", "otherTargetResourceCost", "otherTargetRecoveryMultiplier" };

    static IReadOnlyList<Dictionary<string, string>> ReadFixtureRows(string fileName)
    {
        var lines = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), "src", "Assets", "Tests", "EditMode", "Fixtures", fileName))
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .ToArray();
        var headers = lines[0].Split(',');
        return lines.Skip(1).Select(line =>
        {
            var values = line.Split(',');
            if (values.Length != headers.Length)
                throw new InvalidOperationException($"{fileName} has a malformed CSV row.");
            var row = headers.Select((header, index) => (header, value: values[index]))
                .ToDictionary(pair => pair.header, pair => pair.value, StringComparer.Ordinal);
            if (fileName == "AttackProfiles.fixture.csv" && row["effectBudgetMode"] == "damage_budget")
            {
                var rules = GameData.LoadProductionRuleSet(Path.Combine(FindRepositoryRoot(),"src/Assets/DataConfig/AttackProfiles.csv"));
                float physical = GameData.ResolveAttackMultiplier(row,rules,false), soul = GameData.ResolveAttackMultiplier(row,rules,true);
                row["physicalDamageMultiplier"] = row["effectType"] is "physical" or "hybrid" ? physical.ToString("R",System.Globalization.CultureInfo.InvariantCulture) : "";
                row["soulDamageMultiplier"] = row["effectType"] is "magic" or "hybrid" ? soul.ToString("R",System.Globalization.CultureInfo.InvariantCulture) : "";
            }
            return row;
        }).ToArray();
    }

    static void RunAttackProfileDCombat02()
    {
        const string fixtureName = "AttackProfiles.fixture.csv";
        // 旧 Cooldown 留作历史输入，本 suite 不把全局刻回填进去；四个新字段进入独立 Cost。
        const int GeneralizedTurnCooldown = 0;
        var rawFixtureLines = File.ReadAllLines(Path.Combine(
                FindRepositoryRoot(), "src", "Assets", "Tests", "EditMode", "Fixtures", fixtureName))
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .ToArray();
        ValidateAttackProfileTable(rawFixtureLines);
        var rows = ReadFixtureRows(fixtureName);
        AssertEqual(6, rows.Count, $"{fixtureName} row count");

        var basicMain = rows.Single(row => row["attackProfileId"] == "basic_fixture_main");
        var basicUnarmed = rows.Single(row => row["attackProfileId"] == "basic_fixture_unarmed");
        var artSingle = rows.Single(row => row["attackProfileId"] == "art_fixture_single");
        var artCircle = rows.Single(row => row["attackProfileId"] == "art_fixture_circle");
        var artFan = rows.Single(row => row["attackProfileId"] == "art_fixture_fan");
        var divineLine = rows.Single(row => row["attackProfileId"] == "divine_fixture_line");
        GameData.ActionEconomyCost Cost(IReadOnlyDictionary<string, string> row) => new(row["attackProfileId"], "fixture",
            ParseInt(row["actionPointCostUnits"]), ParseInt(row["minimumNetCostUnits"]), ParseInt(row["ctPenalty"]), ParseInt(row["reuseCooldownTicks"]));

        // 正例：同一 CSV 行逐字段投影到既有三类载体（basic -> AttackProfile，
        // art -> ArtConfig，divine -> DivineConfig），不读取任何旧 GameData 静态配置。
        var basicProfile = new GameData.AttackProfile(
            basicMain["attackProfileId"],
            "物理",
            ParseDouble(basicMain["physicalDamageMultiplier"]),
            GameData.NormalizeElement(basicMain["damageElementId"]),
            ParseInt(basicMain["minCastRange"]),
            ParseInt(basicMain["maxCastRange"]));
        basicProfile = basicProfile with { Cost = Cost(basicMain) };
        AssertBasicProjection(basicMain, basicProfile, "basic_fixture_main");

        var artSingleProfile = new GameData.ArtConfig(
            artSingle["attackProfileId"],
            "神魂",
            ParseDouble(artSingle["soulDamageMultiplier"]),
            ParseInt(artSingle["resourceCost"]),
            GeneralizedTurnCooldown,
            GameData.NormalizeElement(artSingle["damageElementId"]),
            ParseInt(artSingle["minCastRange"]),
            ParseInt(artSingle["maxCastRange"]));
        artSingleProfile = artSingleProfile with { Cost = Cost(artSingle) };
        AssertArtProjection(artSingle, artSingleProfile, "art_fixture_single");
        AssertEqual((GameData.AreaTargetingConfig)null, artSingleProfile.AreaTargeting, "art_fixture_single must be single target");

        var artCircleProfile = new GameData.ArtConfig(
            artCircle["attackProfileId"],
            "神魂",
            ParseDouble(artCircle["soulDamageMultiplier"]),
            ParseInt(artCircle["resourceCost"]),
            GeneralizedTurnCooldown,
            GameData.NormalizeElement(artCircle["damageElementId"]),
            ParseInt(artCircle["minCastRange"]),
            ParseInt(artCircle["maxCastRange"]),
            new GameData.AreaTargetingConfig(
                artCircle["attackProfileId"],
                GameData.AreaCenterKind.Caster,
                ParseInt(artCircle["minCastRange"]),
                ParseInt(artCircle["maxCastRange"]),
                new GameData.AreaShapeConfig(
                    GameData.AreaShapeKind.Circle,
                    ParseInt(artCircle["areaRadius"]),
                    ParseInt(artCircle["areaLength"]),
                    ParseInt(artCircle["areaFanHalfAngleSteps"]),
                    Facing: null,
                    ParseInt(artCircle["areaInnerRadius"])),
                ParseAreaEffectBlockers(artCircle["areaEffectBlockers"]),
                ParseAreaTargetFactions(artCircle["areaAllowedFactions"]),
                ParseAreaTargetStates(artCircle["areaAllowedStates"])));
        artCircleProfile = artCircleProfile with { Cost = Cost(artCircle) };
        AssertArtProjection(artCircle, artCircleProfile, "art_fixture_circle");
        AssertAreaProjection(artCircle, artCircleProfile.AreaTargeting, "art_fixture_circle");

        var artFanProfile = new GameData.ArtConfig(
            artFan["attackProfileId"],
            "神魂",
            ParseDouble(artFan["soulDamageMultiplier"]),
            ParseInt(artFan["resourceCost"]),
            GeneralizedTurnCooldown,
            GameData.NormalizeElement(artFan["damageElementId"]),
            ParseInt(artFan["minCastRange"]),
            ParseInt(artFan["maxCastRange"]),
            new GameData.AreaTargetingConfig(
                artFan["attackProfileId"],
                GameData.AreaCenterKind.TargetCell,
                ParseInt(artFan["minCastRange"]),
                ParseInt(artFan["maxCastRange"]),
                new GameData.AreaShapeConfig(
                    GameData.AreaShapeKind.Fan,
                    ParseInt(artFan["areaRadius"]),
                    ParseInt(artFan["areaLength"]),
                    ParseInt(artFan["areaFanHalfAngleSteps"]),
                    ParseHexDirection(artFan["areaFacing"]),
                    ParseInt(artFan["areaInnerRadius"])),
                ParseAreaEffectBlockers(artFan["areaEffectBlockers"]),
                ParseAreaTargetFactions(artFan["areaAllowedFactions"]),
                ParseAreaTargetStates(artFan["areaAllowedStates"])));
        artFanProfile = artFanProfile with { Cost = Cost(artFan) };
        AssertArtProjection(artFan, artFanProfile, "art_fixture_fan");
        AssertAreaProjection(artFan, artFanProfile.AreaTargeting, "art_fixture_fan");

        var divineLineProfile = new GameData.DivineConfig(
            divineLine["attackProfileId"],
            "物理",
            ParseDouble(divineLine["physicalDamageMultiplier"]),
            ParseDouble(divineLine["defensePenetration"]),
            GeneralizedTurnCooldown,
            GameData.NormalizeElement(divineLine["damageElementId"]),
            ParseInt(divineLine["minCastRange"]),
            ParseInt(divineLine["maxCastRange"]),
            new GameData.AreaTargetingConfig(
                divineLine["attackProfileId"],
                GameData.AreaCenterKind.TargetCell,
                ParseInt(divineLine["minCastRange"]),
                ParseInt(divineLine["maxCastRange"]),
                new GameData.AreaShapeConfig(
                    GameData.AreaShapeKind.Line,
                    ParseInt(divineLine["areaRadius"]),
                    ParseInt(divineLine["areaLength"]),
                    ParseInt(divineLine["areaFanHalfAngleSteps"]),
                    ParseHexDirection(divineLine["areaFacing"]),
                    ParseInt(divineLine["areaInnerRadius"])),
                ParseAreaEffectBlockers(divineLine["areaEffectBlockers"]),
                ParseAreaTargetFactions(divineLine["areaAllowedFactions"]),
                ParseAreaTargetStates(divineLine["areaAllowedStates"])),
            MPCost: ParseInt(divineLine["resourceCost"]));
        divineLineProfile = divineLineProfile with { Cost = Cost(divineLine) };
        AssertDivineProjection(divineLine, divineLineProfile, "divine_fixture_line");
        AssertAreaProjection(divineLine, divineLineProfile.AreaTargeting, "divine_fixture_line");

        // 圆形无朝向：直线/扇形必须非空朝向；圆形必须为空，任何默认方向都失败。
        AssertEqual((HexDirection?)null, artCircleProfile.AreaTargeting.Shape.Facing, "circle facing must be null");
        AssertEqual(false, artCircleProfile.AreaTargeting.Shape.Facing.HasValue, "circle facing must not have a value");

        // 负例：从合法 fixture 行一次只变异一个条件，全部稳定失败且不回落到旧静态配置。
        var fixtureHeaders = rawFixtureLines[0].Split(',');
        var basicMainLine = rawFixtureLines[1];
        var basicUnarmedLine = rawFixtureLines[2];
        var artSingleLine = rawFixtureLines[3];
        var artCircleLine = rawFixtureLines[4];
        var artFanLine = rawFixtureLines[5];
        var divineLineLine = rawFixtureLines[6];

        string MutateRowLine(string line, string field, string value)
        {
            var columns = line.Split(',');
            columns[Array.IndexOf(fixtureHeaders, field)] = value;
            return string.Join(",", columns);
        }

        string ShortRowLine(string line)
        {
            var columns = line.Split(',');
            return string.Join(",", columns.Take(columns.Length - 1));
        }

        string[] TableWith(params string[] replacedRows)
        {
            var rows = new List<string> { basicMainLine, basicUnarmedLine, artSingleLine, artCircleLine, artFanLine, divineLineLine };
            foreach (var replaced in replacedRows)
            {
                for (int index = 0; index < rows.Count; index++)
                {
                    if (rows[index].Split(',')[0] == replaced.Split(',')[0])
                        rows[index] = replaced;
                }
            }
            // ValidateAttackProfileTable 把 lines[0] 当表头；负例表必须带原表头行。
            return new[] { rawFixtureLines[0] }.Concat(rows).ToArray();
        }

        // 重复 ID：变异后的行以同 ID 追加进合法整表，表头行保持为 lines[0]。
        string[] TableWithDuplicatedId(string mutatedLine)
        {
            return new[] { rawFixtureLines[0], basicMainLine, basicUnarmedLine, artSingleLine, artCircleLine, artFanLine, divineLineLine, mutatedLine };
        }

        var renamedHeaders = (string[])fixtureHeaders.Clone();
        renamedHeaders[1] = "renamedDisplayNameKey";
        AssertTableRejected(new[] { string.Join(",", renamedHeaders) }.Concat(rawFixtureLines.Skip(1)).ToArray(), "attack_profile_header_not_exact");
        AssertTableRejected(TableWithDuplicatedId(MutateRowLine(artSingleLine, "attackProfileId", "basic_fixture_main")), "attack_profile_id_duplicate");
        AssertTableRejected(TableWith(ShortRowLine(artSingleLine)), "attack_profile_short_or_extra_row");
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "realmRequirementId", "realm_unknown")), "attack_profile_requirement_reference_unknown");
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "damageElementId", "element_unknown")), "attack_profile_damage_element_unknown");
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "resourceCost", "-1")), "attack_profile_resourceCost_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "minCastRange", "4")), "attack_profile_cast_range_invalid");
        // 三种非法形状：圆形带朝向、直线带半径、扇形扇角越界。
        AssertTableRejected(TableWith(MutateRowLine(artCircleLine, "areaFacing", "east")), "attack_profile_area_shape_contract_invalid");
        AssertTableRejected(TableWith(MutateRowLine(divineLineLine, "areaRadius", "1")), "attack_profile_area_shape_contract_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artFanLine, "areaFanHalfAngleSteps", "2")), "attack_profile_area_shape_contract_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artCircleLine, "areaShapeKind", "hex")), "attack_profile_area_shape_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artFanLine, "areaFacing", "north")), "attack_profile_area_facing_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artFanLine, "areaFacing", "")), "attack_profile_area_facing_invalid");
        AssertTableRejected(TableWith(MutateRowLine(divineLineLine, "areaEffectBlockers", "sight_blocked")), "attack_profile_area_blocker_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artCircleLine, "areaAllowedFactions", "neutral")), "attack_profile_areaAllowedFactions_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artCircleLine, "areaAllowedStates", "ghost")), "attack_profile_areaAllowedStates_invalid");
        ValidateAttackProfileTable(TableWith(MutateRowLine(artSingleLine, "ctPenalty", "4")));
        ValidateAttackProfileTable(TableWith(MutateRowLine(artSingleLine, "reuseCooldownTicks", "4")));
        // 统一行动力四列的整数合法性：零／负主动费、越界净费下限、分数、溢出与 CT 上界。
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "actionPointCostUnits", "0")), "attack_profile_actionPointCostUnits_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "actionPointCostUnits", "-1")), "attack_profile_actionPointCostUnits_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "actionPointCostUnits", "2.5")), "attack_profile_actionPointCostUnits_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "actionPointCostUnits", "2147483648")), "attack_profile_actionPointCostUnits_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "minimumNetCostUnits", "")), "attack_profile_minimumNetCostUnits_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "minimumNetCostUnits", "0")), "attack_profile_minimumNetCostUnits_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "minimumNetCostUnits", "99999")), "attack_profile_minimumNetCostUnits_exceeds_actionPointCostUnits");
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "ctPenalty", "-1")), "attack_profile_ctPenalty_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "ctPenalty", "2147483548")), "attack_profile_ctPenalty_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "reuseCooldownTicks", "-1")), "attack_profile_reuseCooldownTicks_invalid");
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "reuseCooldownTicks", "3.5")), "attack_profile_reuseCooldownTicks_invalid");
        AssertTableRejected(TableWith(MutateRowLine(basicMainLine, "ctPenalty", "1")), "basic_attack_resource_or_cooldown_invalid");
        AssertTableRejected(TableWith(MutateRowLine(basicMainLine, "reuseCooldownTicks", "1")), "basic_attack_resource_or_cooldown_invalid");
        // 单目标混入范围列：只把单目标行填入一个范围列。
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "areaCenterKind", "caster")), "single_target_area_fields_present");
        // 未支持效果语义：heal 行在既有 BattleSim 载体上没有对应字段。
        AssertTableRejected(TableWith(MutateRowLine(artCircleLine, "effectType", "heal")), "unsupported_effect_semantics");
        AssertTableRejected(TableWith(MutateRowLine(artSingleLine, "effectType", "restore_spirit")), "unsupported_effect_semantics");
        foreach (string field in RecoveryFixtureFields)
            AssertTableRejected(TableWith(MutateRowLine(artSingleLine, field, "0")), "unsupported_effect_semantics");
    }

    // WEAPON: production has four explicit profiles and no default basic attack.
    static void RunBasicUnarmedProductionDCombatProd01()
    {
        var rows = ReadCsvRows("src/Assets/DataConfig/AttackProfiles.csv");
        AssertEqual(false, rows.Any(row => row["attackProfileId"] == "basic_unarmed" || row["basicBindingKind"] == "unarmed_fallback"), "no production unarmed row");
        AssertEqual(4, GameData.ProductionAttacks.Count, "four current production profiles");
        var actor = CtTestCharacter("no-weapon", 20);
        var enemy = CtTestCharacter("no-weapon-enemy", 10);
        actor.BasicAttackProfile = enemy.BasicAttackProfile = null;
        var battle = Combat.SimulateDetailed(new HexBattlefield(), actor, enemy, new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, 4);
        AssertEqual(false, battle.Economy.Any(value => value.Event == "action_committed"), "no weapon means no fabricated attack");
        AssertEqual(true, battle.Economy.Any(value => value.Event == "own_turn_started"), "unequipped actors still enter turns");
        AssertEqual(-1, Combat.SelectAction(new HexBattlefield(), new(0,0), new(1,0), false,1,1,false,false,1,1,null,out _,out _), "no attack selection ends legally");
    }

    // Explicit old-rule test fixture; never installed by production Character.Create.
    static GameData.AttackProfile LegacyBasicFixture => new("fixture-legacy-basic", "物理", 1, "", 1, 1,
        Cost: new("fixture-legacy-basic", "fixture-old-rule", 50000, 25000, 0, 0));


    static void RunUnarmedConfig()
    {
        string root = FindRepositoryRoot();
        string data = Path.Combine(root, "Temp", "unarmed-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        string attackPath = Path.Combine(data, "AttackProfiles.csv");
        foreach (string name in new[] { "RealmCombatGrowth", "CombatEffectTierRules", "CombatActionEconomyRules" })
            File.Copy(Path.Combine(root, "src/Assets/DataConfig/" + name + ".csv"), Path.Combine(data, name + ".csv"));
        var production = File.ReadAllLines(Path.Combine(root, "src/Assets/DataConfig/AttackProfiles.csv"));
        string header = production.Single(line => line.StartsWith("attackProfileId,", StringComparison.Ordinal));
        string[] fields = header.Split(',');
        var baseline = production.Single(line => line.StartsWith("basic_iron_sword,", StringComparison.Ordinal)).Split(',');
        var fixture = new List<string> { header };
        foreach (var entry in new[] {
            ("body_human", "realm_fanren", "tier_lianqi", 20000, 30),
            ("body_human", "realm_lianqi", "tier_lianqi", 25000, 120),
            ("body_human", "realm_zhuji", "tier_zhuji", 75000, 500),
            ("body_human", "realm_jindan", "tier_jindan", 250000, 2400),
            ("body_shijiahou", "realm_lianqi", "tier_lianqi", 25000, 120) })
        {
            var row = (string[])baseline.Clone();
            void Set(string key, string value) => row[Array.IndexOf(fields, key)] = value;
            Set("attackProfileId", "basic_" + entry.Item1 + "_" + entry.Item2.Substring(6));
            Set("displayNameKey", "attack_profile_" + entry.Item1); Set("basicBindingKind", "unarmed_body");
            Set("effectBudgetMode", "fixed_unarmed"); Set("abilityTierId", entry.Item3);
            Set("unarmedFamilyId", entry.Item1); Set("unarmedRealmId", entry.Item2);
            Set("actionPointCostUnits", entry.Item4.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Set("minimumNetCostUnits", entry.Item4.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Set("unarmedPhysicalAttackCapacity", entry.Item5.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Set("physicalDamageMultiplier", "1"); Set("reuseCooldownTicks", "0");
            fixture.Add(string.Join(",", row));
        }
        void Write(IEnumerable<string> lines) => File.WriteAllText(attackPath, string.Join("\n", lines) + "\n", new System.Text.UTF8Encoding(false));
        bool Rejects(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
        try
        {
            Write(fixture);
            var rules = GameData.LoadProductionRuleSet(attackPath);
            var profiles = GameData.LoadAttackProfileProjection(attackPath);
            AssertEqual(5, profiles.Count, "explicit fixture body rows");
            foreach (var profile in profiles.Values)
            {
                var mapping = rules.GetUnarmed(profile.UnarmedFamilyId, profile.UnarmedRealmId);
                AssertEqual(mapping.ProfileId, profile.Name, "profile identity");
                AssertEqual(mapping.ActionPointCostUnits, profile.Cost.ActionPointCostUnits, "same-row fee");
                AssertEqual(mapping.MinimumNetCostUnits, profile.Cost.MinimumNetCostUnits, "same-row net floor");
                AssertEqual(mapping.PhysicalAttackCapacity, profile.UnarmedPhysicalAttackCapacity, "same-row capacity");
                AssertEqual(rules.SourceDigest, profile.RuleSetSourceDigest, "four-source fixture digest");
                AssertEqual(2, profile.AttackSchemaVersion, "attack schema version");
                AssertEqual(1d, profile.Mult, "declared physical multiplier");
            }
            Console.WriteLine("unarmed-config fixture sourceDigest=" + rules.SourceDigest);
            AssertEqual(true, Rejects(() => GameData.LoadProductionAttacks(attackPath)), "fixture cannot enable production body content");
            AssertEqual(true, Rejects(() => rules.GetUnarmed("body_shijiahou", "realm_zhuji")), "beast cannot borrow human realm");
            AssertEqual(true, Rejects(() => rules.GetUnarmed("body_human", "realm_yuanying")), "reserved realm grants no body row");
            var variants = new List<string[]> { fixture.Take(5).ToArray(), fixture.Concat(new[] { fixture[1] }).ToArray(),
                fixture.Select(line => string.Join(",", line.Split(',').Take(59))).ToArray() };
            foreach (var change in new[] {
                ("attackSchemaVersion", ""), ("attackSchemaVersion", "1"), ("unarmedFamilyId", ""), ("unarmedFamilyId", "body_unknown"),
                ("unarmedRealmId", "realm_yuanying"), ("attackProfileId", "basic_unarmed"), ("abilityTierId", "tier_zhuji"),
                ("basicBindingKind", "main_equipment"), ("effectBudgetMode", "legacy_unarmed"), ("displayNameKey", "attack_profile_body_shijiahou"),
                ("unarmedPhysicalAttackCapacity", ""), ("unarmedPhysicalAttackCapacity", "0"), ("unarmedPhysicalAttackCapacity", "-1"),
                ("unarmedPhysicalAttackCapacity", "1.5"), ("unarmedPhysicalAttackCapacity", "1e2"), ("unarmedPhysicalAttackCapacity", "2147483648"),
                ("unarmedPhysicalAttackCapacity", "120"), ("actionPointCostUnits", "25000"), ("actionPointCostUnits", "0"), ("minimumNetCostUnits", "20001"),
                ("physicalDamageMultiplier", "NaN"), ("physicalDamageMultiplier", "Infinity"), ("physicalDamageMultiplier", "0"),
                ("defensePenetration", "0"), ("controlChancePermille", "0"), ("controlUsesMindContest", "0"), ("statusId", "status_test"),
                ("isBloodline", "0"), ("efficiencyNumerator", "1"), ("spiritRecoveryMultiplier", "0"), ("soulDamageMultiplier", "0"),
                ("resourceCost", "1"), ("ctPenalty", "1"), ("reuseCooldownTicks", ""), ("reuseCooldownTicks", "1"),
                ("minCastRange", "0"), ("sourceAffiliation", "body_human") })
            {
                var changed = fixture.ToArray(); var row = changed[1].Split(','); row[Array.IndexOf(fields, change.Item1)] = change.Item2;
                changed[1] = string.Join(",", row); variants.Add(changed);
            }
            foreach (var variant in variants)
            {
                Write(variant);
                AssertEqual(true, Rejects(() => GameData.LoadProductionRuleSet(attackPath)), "shared body schema rejects malformed fixture");
                AssertEqual(true, Rejects(() => GameData.LoadAttackProfileProjection(attackPath)), "projection rejects malformed fixture");
            }
            var adjustable = fixture.ToArray(); var first = adjustable[1].Split(',');
            first[Array.IndexOf(fields, "actionPointCostUnits")] = first[Array.IndexOf(fields, "minimumNetCostUnits")] = "20001";
            first[Array.IndexOf(fields, "unarmedPhysicalAttackCapacity")] = "60"; first[Array.IndexOf(fields, "physicalDamageMultiplier")] = "1.1";
            adjustable[1] = string.Join(",", first); Write(adjustable);
            var adjusted = GameData.LoadAttackProfileProjection(attackPath)["basic_body_human_fanren"];
            AssertEqual(20001, adjusted.Cost.ActionPointCostUnits, "fee remains editable in its sole row");
            AssertEqual(60, adjusted.UnarmedPhysicalAttackCapacity, "capacity is not a parser constant");
            AssertEqual((double)1.1f, adjusted.Mult, "multiplier is not a parser constant");
            AssertEqual(false, adjusted.RuleSetSourceDigest == rules.SourceDigest, "parameter change updates digest");
            Write(production);
            AssertEqual(4, GameData.LoadProductionAttacks(attackPath).Count, "unchanged four production IDs");
            AssertEqual(0, GameData.LoadProductionRuleSet(attackPath).UnarmedRows.Count, "no production body mapping");
            var wrong = production.Where(line => !line.StartsWith('#')).ToArray(); var weapon = wrong[3].Split(',');
            weapon[Array.IndexOf(fields, "unarmedPhysicalAttackCapacity")] = "0"; wrong[3] = string.Join(",", weapon); Write(wrong);
            AssertEqual(true, Rejects(() => GameData.LoadProductionAttacks(attackPath)), "even zero foreign body field is rejected");
            Console.WriteLine("unarmed-config: five explicit rows, " + variants.Count + " invalid fixtures and unchanged production accepted.");
        }
        finally { Directory.Delete(data, true); }
    }

    static void RunEquipmentProduction()
    {
        string root = FindRepositoryRoot();
        string temporary = Path.Combine(Path.GetTempPath(), "tzg-equipment-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(temporary, "src/Assets/DataConfig");
        Directory.CreateDirectory(data);
        string attacks = Path.Combine(data, "AttackProfiles.csv");
        string input = Path.Combine(temporary, "ordinary-input.json");
        File.Copy(Path.Combine(root, "开发管理/关中首战普通角色输入.json"), input);
        File.Copy(Path.Combine(root, "src/Assets/DataConfig/Enemies.csv"), Path.Combine(data, "Enemies.csv"));
        foreach (string name in new[] { "RealmCombatGrowth","CombatEffectTierRules","CombatActionEconomyRules" })
            File.Copy(Path.Combine(root,"src/Assets/DataConfig/"+name+".csv"),Path.Combine(data,name+".csv"));
        string[] baseline = File.ReadAllLines(Path.Combine(root, "src/Assets/DataConfig/AttackProfiles.csv"));
        MethodInfo audit = typeof(GuanzhongPlaytestAudit).GetMethod("LoadProductionInputs", BindingFlags.Static | BindingFlags.NonPublic);
        TianZhang.Combat.CombatAttackProfile[] LoadAudit()
        {
            object result = audit.Invoke(null, new object[] { input, null });
            return (TianZhang.Combat.CombatAttackProfile[])result.GetType().GetProperty("Profiles").GetValue(result);
        }
        try
        {
            File.WriteAllLines(attacks, baseline);
            var loaded = GameData.LoadProductionAttacks(attacks);
            var projected = LoadAudit();
            AssertEqual(4, loaded.Count, "real production loader four rows");
            AssertEqual(4, projected.Length, "real audit loader four rows");
            foreach (string id in new[] { "basic_iron_sword", "basic_iron_blade" })
            {
                var row = ReadCsvRows("src/Assets/DataConfig/AttackProfiles.csv").Single(value => value["attackProfileId"] == id);
                var actual = loaded[id]; var combat = projected.Single(value => value.Id == id);
                AssertEqual(row["basicBindingKind"], actual.BasicBindingKind, id + " binding");
                AssertEqual(int.Parse(row["actionPointCostUnits"]), actual.Cost.ActionPointCostUnits, id + " cost");
                AssertEqual(int.Parse(row["minimumNetCostUnits"]), actual.Cost.MinimumNetCostUnits, id + " floor");
                AssertEqual(double.Parse(row["physicalDamageMultiplier"], System.Globalization.CultureInfo.InvariantCulture), actual.Mult, id + " multiplier");
                AssertEqual(actual.Cost.ActionPointCostUnits, combat.ActionPointCostUnits, id + " audit cost");
                AssertEqual((float)actual.Mult, combat.PhysicalMultiplier, id + " audit multiplier");
            }
            AssertEqual(false, loaded.ContainsKey("basic_unarmed"), "unarmed removed");
            AssertEqual(12, loaded["spell_guanshiji"].SpiritCost, "legacy attack unchanged");
            AssertEqual(0.5m, loaded["spell_julingshu"].SpiritRecoveryMultiplier, "legacy recovery unchanged");
            var rules = GameData.LoadProductionRuleSet(attacks);
            AssertEqual("AP-GROWTH@0.1",rules.RuleSetVersion,"production rule version");
            AssertEqual(52,rules.Economy.Count,"51 explicit action triples plus global");
            AssertEqual(0,loaded["spell_guanshiji"].Cost.CtPenalty,"new guanshiji CT");
            AssertEqual(0,loaded["spell_guanshiji"].Cost.ReuseCooldownTicks,"new guanshiji CD");
            AssertEqual(16,loaded["spell_julingshu"].Cost.ReuseCooldownTicks,"derived recovery CD");
            foreach (var entry in loaded.Values) AssertEqual(rules.SourceDigest,entry.RuleSetSourceDigest,"shared production digest");
            foreach (var profile in projected)
                AssertEqual(rules.SourceDigest,profile.RuleMetadata.SourceDigest,"audit metadata digest");
            string header = baseline.First(value => value.StartsWith("attackProfileId,"));
            string blade = baseline.Single(value => value.StartsWith("basic_iron_blade,"));
            var variants = new List<string[]>
            {
                baseline.Where(value => !value.StartsWith("basic_iron_blade,")).ToArray(),
                baseline.Concat(new[] { blade }).ToArray(),
                baseline.Concat(new[] { blade.Replace("basic_iron_blade", "unknown_attack") }).ToArray(),
            };
            foreach (var change in new[] { ("actionPointCostUnits", "0"), ("actionPointCostUnits", "-1"),
                ("actionPointCostUnits", "1.5"), ("minimumNetCostUnits", "999999"), ("reuseCooldownTicks", "-1"),
                ("basicBindingKind", "unarmed_fallback"), ("physicalDamageMultiplier", "NaN"),
                ("cooldownClass", "typo"), ("abilityTierId", "tier_unknown"), ("ruleSetVersion", "unknown") })
            {
                var columns = blade.Split(','); columns[Array.IndexOf(header.Split(','), change.Item1)] = change.Item2;
                variants.Add(baseline.Select(value => value == blade ? string.Join(",", columns) : value).ToArray());
            }
            foreach (string[] variant in variants)
            {
                File.WriteAllLines(attacks, variant);
                bool mainRejected = false, auditRejected = false;
                try { GameData.LoadProductionAttacks(attacks); } catch (Exception) { mainRejected = true; }
                try { LoadAudit(); } catch (TargetInvocationException) { auditRejected = true; }
                AssertEqual(true, mainRejected, "real loader rejects invalid input");
                AssertEqual(true, auditRejected, "audit entry rejects invalid input");
            }
            File.WriteAllLines(attacks,baseline);
            foreach (var invalid in new[] { ("RealmCombatGrowth","baseMovementEfficiencyMilliUnits","0"),
                ("CombatEffectTierRules","abilityTierId","tier_unknown"), ("CombatActionEconomyRules","repeatThirdPermille","1") })
            {
                string rulePath = Path.Combine(data,invalid.Item1+".csv"); byte[] original = File.ReadAllBytes(rulePath);
                try
                {
                    var lines = File.ReadAllLines(rulePath); int hi = TianZhang.Editor.CsvTableReader.FindHeaderIndex(lines);
                    var row = lines[hi+1].Split(','); row[Array.IndexOf(lines[hi].Split(','),invalid.Item2)] = invalid.Item3;
                    lines[hi+1] = string.Join(",",row); File.WriteAllLines(rulePath,lines);
                    bool mainRejected=false,auditRejected=false;
                    try { GameData.LoadProductionAttacks(attacks); } catch (InvalidDataException) { mainRejected=true; }
                    try { LoadAudit(); } catch (TargetInvocationException) { auditRejected=true; }
                    AssertEqual(true,mainRejected,"bad rule source main"); AssertEqual(true,auditRejected,"bad rule source audit");
                }
                finally { File.WriteAllBytes(rulePath,original); }
            }
            Console.WriteLine("equipment-production: both real loaders accepted four production rows and rejected " + variants.Count + " malformed inputs each.");
        }
        finally { Directory.Delete(temporary, true); }
    }

    static IReadOnlyList<Dictionary<string, string>> ReadCsvRows(string relativePath)
    {
        var lines = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), relativePath))
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .ToArray();
        var headers = lines[0].Split(',');
        return lines.Skip(1).Select(line =>
        {
            var values = line.Split(',');
            if (values.Length != headers.Length)
                throw new InvalidOperationException($"{relativePath} has a malformed CSV row.");
            return headers.Select((header, index) => (header, value: values[index]))
                .ToDictionary(pair => pair.header, pair => pair.value, StringComparer.Ordinal);
        }).ToArray();
    }

    static IReadOnlyDictionary<string, string> ReadLanguageCsv()
    {
        var lines = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), "src", "Assets", "DataConfig", "Language.csv"))
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
            .ToArray();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            int separator = line.IndexOf(',');
            if (separator <= 0)
                continue;
            result[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
        }

        return result;
    }

    static void AssertBasicProjection(
        IReadOnlyDictionary<string, string> row,
        GameData.AttackProfile profile,
        string label)
    {
        AssertEqual(row["attackProfileId"], profile.Name, $"{label} id");
        AssertEqual(ParseDouble(row["physicalDamageMultiplier"]), profile.Mult, $"{label} mult");
        AssertEqual(GameData.NormalizeElement(row["damageElementId"]), profile.Element, $"{label} element");
        AssertEqual(ParseInt(row["minCastRange"]), profile.MinRange, $"{label} min range");
        AssertEqual(ParseInt(row["maxCastRange"]), profile.MaxRange, $"{label} max range");
        AssertEqual((GameData.AreaTargetingConfig)null, profile.AreaTargeting, $"{label} must be single target");
        AssertCostProjection(row, profile.Cost, label);
    }

    static void AssertArtProjection(
        IReadOnlyDictionary<string, string> row,
        GameData.ArtConfig profile,
        string label)
    {
        AssertEqual(row["attackProfileId"], profile.Name, $"{label} id");
        AssertEqual(ParseDouble(row["soulDamageMultiplier"]), profile.Mult, $"{label} mult");
        AssertEqual(ParseInt(row["resourceCost"]), profile.MPCost, $"{label} resource cost");
        // 遗留回合 CD 不参与执行；新 Cost 独立保有 AP／CT／全局刻。
        AssertEqual(0, profile.Cooldown, $"{label} generalized turn cooldown");
        AssertCostProjection(row, profile.Cost, label);
        AssertActionCostColumnsAreUsable(row, label);
        AssertEqual(GameData.NormalizeElement(row["damageElementId"]), profile.Element, $"{label} element");
        AssertEqual(ParseInt(row["minCastRange"]), profile.MinRange, $"{label} min range");
        AssertEqual(ParseInt(row["maxCastRange"]), profile.MaxRange, $"{label} max range");
    }

    static void AssertDivineProjection(
        IReadOnlyDictionary<string, string> row,
        GameData.DivineConfig profile,
        string label)
    {
        AssertEqual(row["attackProfileId"], profile.Name, $"{label} id");
        AssertEqual(ParseDouble(row["physicalDamageMultiplier"]), profile.Mult, $"{label} mult");
        AssertEqual(ParseDouble(row["defensePenetration"]), profile.DefPen, $"{label} defense penetration");
        AssertEqual(ParseInt(row["resourceCost"]), profile.MPCost, $"{label} resource cost");
        AssertEqual(0, profile.Cooldown, $"{label} generalized turn cooldown");
        AssertCostProjection(row, profile.Cost, label);
        AssertActionCostColumnsAreUsable(row, label);
        AssertEqual(GameData.NormalizeElement(row["damageElementId"]), profile.Element, $"{label} element");
        AssertEqual(ParseInt(row["minCastRange"]), profile.MinRange, $"{label} min range");
        AssertEqual(ParseInt(row["maxCastRange"]), profile.MaxRange, $"{label} max range");
    }

    static void AssertCostProjection(IReadOnlyDictionary<string, string> row, GameData.ActionEconomyCost cost, string label)
    {
        cost.Validate();
        AssertEqual(ParseInt(row["actionPointCostUnits"]), cost.ActionPointCostUnits, label + " AP");
        AssertEqual(ParseInt(row["minimumNetCostUnits"]), cost.MinimumNetCostUnits, label + " net minimum");
        AssertEqual(ParseInt(row["ctPenalty"]), cost.CtPenalty, label + " CT");
        AssertEqual(ParseInt(row["reuseCooldownTicks"]), cost.ReuseCooldownTicks, label + " global ticks");
    }

    /// <summary>四个新列的整数合法性：主动费与净费下限为整数且 0 &lt; 下限 ≤ 基础费。</summary>
    static void AssertActionCostColumnsAreUsable(IReadOnlyDictionary<string, string> row, string label)
    {
        int cost = ParseInt(row["actionPointCostUnits"]);
        int minimum = ParseInt(row["minimumNetCostUnits"]);
        AssertEqual(true, cost > 0, $"{label} action point cost must be positive");
        AssertEqual(true, minimum > 0 && minimum <= cost, $"{label} minimum net cost must be positive and bounded by the base cost");
        AssertEqual(true, ParseInt(row["reuseCooldownTicks"]) >= 0, $"{label} reuse ticks must not be negative");
    }

    static void AssertAreaProjection(
        IReadOnlyDictionary<string, string> row,
        GameData.AreaTargetingConfig config,
        string label)
    {
        AssertEqual(row["attackProfileId"], config.Name, $"{label} area name");
        AssertEqual(ParseAreaCenterKind(row["areaCenterKind"]), config.CenterKind, $"{label} center kind");
        AssertEqual(ParseInt(row["minCastRange"]), config.MinCastRange, $"{label} area min range");
        AssertEqual(ParseInt(row["maxCastRange"]), config.MaxCastRange, $"{label} area max range");
        AssertEqual(ParseAreaShapeKind(row["areaShapeKind"]), config.Shape.Kind, $"{label} shape kind");
        AssertEqual(ParseInt(row["areaRadius"]), config.Shape.Radius, $"{label} radius");
        AssertEqual(ParseInt(row["areaLength"]), config.Shape.Length, $"{label} length");
        AssertEqual(ParseInt(row["areaFanHalfAngleSteps"]), config.Shape.FanHalfAngleSteps, $"{label} fan half angle");
        AssertEqual(ParseInt(row["areaInnerRadius"]), config.Shape.InnerRadius, $"{label} inner radius");
        AssertEqual(ParseAreaEffectBlockers(row["areaEffectBlockers"]), config.EffectBlockers, $"{label} blockers");
        AssertEqual(ParseAreaTargetFactions(row["areaAllowedFactions"]), config.AllowedFactions, $"{label} factions");
        AssertEqual(ParseAreaTargetStates(row["areaAllowedStates"]), config.AllowedStates, $"{label} states");
        if (string.IsNullOrEmpty(row["areaFacing"]))
        {
            AssertEqual((HexDirection?)null, config.Shape.Facing, $"{label} facing must be null");
        }
        else
        {
            AssertEqual((HexDirection?)ParseHexDirection(row["areaFacing"]), config.Shape.Facing, $"{label} facing");
        }
    }

    static void AssertTableRejected(IReadOnlyList<string> lines, string expectedCode)
    {
        try
        {
            ValidateAttackProfileTable(lines);
        }
        catch (InvalidOperationException ex)
        {
            if (!string.Equals(ex.Message, expectedCode, StringComparison.Ordinal))
                throw new InvalidOperationException($"expected rejection '{expectedCode}' but got '{ex.Message}'.");
            return;
        }

        throw new InvalidOperationException($"table must be rejected with '{expectedCode}'.");
    }

    static void ValidateAttackProfileTable(IReadOnlyList<string> lines)
    {
        var headers = lines[0].Split(',');
        if (!headers.SequenceEqual(AttackProfileHeaders, StringComparer.Ordinal))
            throw new InvalidOperationException("attack_profile_header_not_exact");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(1))
        {
            var values = line.Split(',');
            if (values.Length != headers.Length)
                throw new InvalidOperationException("attack_profile_short_or_extra_row");
            var row = headers.Select((header, index) => (header, value: values[index]))
                .ToDictionary(pair => pair.header, pair => pair.value, StringComparer.Ordinal);

            // 旧投影无恢复/他人费用语义；新增四列必须严格留空，不能丢列。
            if (RecoveryFixtureFields.Any(field => row[field] != ""))
                throw new InvalidOperationException("unsupported_effect_semantics");

            string profileKind = row["profileKind"];
            string targetingMode = row["targetingMode"];
            if (profileKind is not ("basic" or "art" or "divine"))
                throw new InvalidOperationException("attack_profile_kind_invalid");
            if (!ids.Add(row["attackProfileId"]))
                throw new InvalidOperationException("attack_profile_id_duplicate");
            if (row["realmRequirementId"] is not (
                "" or "realm_fanren" or "realm_lianqi" or "realm_zhuji" or
                "realm_jindan" or "realm_yuanying" or "realm_huashen"))
                throw new InvalidOperationException("attack_profile_requirement_reference_unknown");

            // 四个统一行动力字段逐行整数合法性：主动费与净费下限为正且 0 < 下限 <= 基础费，
            // CT 在 0..int.MaxValue-100，重用刻非负；空值、分数与 32 位溢出都在此失败。
            if (!ParsePositiveInt(row["actionPointCostUnits"]))
                throw new InvalidOperationException("attack_profile_actionPointCostUnits_invalid");
            if (!ParsePositiveInt(row["minimumNetCostUnits"]))
                throw new InvalidOperationException("attack_profile_minimumNetCostUnits_invalid");
            if (ParseInt(row["minimumNetCostUnits"]) > ParseInt(row["actionPointCostUnits"]))
                throw new InvalidOperationException("attack_profile_minimumNetCostUnits_exceeds_actionPointCostUnits");
            if (!ParseCtPenalty(row["ctPenalty"]))
                throw new InvalidOperationException("attack_profile_ctPenalty_invalid");
            if (!ParseNonNegativeInt(row["reuseCooldownTicks"], "reuseCooldownTicks"))
                throw new InvalidOperationException("attack_profile_reuseCooldownTicks_invalid");

            if (profileKind == "basic")
            {
                if (row["resourceKind"] != "none" || row["resourceCost"] != "0" ||
                    row["ctPenalty"] != "0" || row["reuseCooldownTicks"] != "0")
                    throw new InvalidOperationException("basic_attack_resource_or_cooldown_invalid");
            }

            // 效果、资源与冷却、距离合法性对每一行生效（含单目标），
            // 与 Unity 导入器 ParseAttackProfileRow 的逐行校验顺序一致。
            string effectType = row["effectType"];
            if (effectType is "physical" or "magic" or "hybrid")
            {
                if (string.IsNullOrEmpty(row["damageElementId"]) || !IsKnownElement(row["damageElementId"]))
                    throw new InvalidOperationException("attack_profile_damage_element_unknown");
                if (row["effectBudgetMode"] != "damage_budget" && effectType is "physical" or "hybrid" && !ParseNonNegativeDouble(row["physicalDamageMultiplier"], "physicalDamageMultiplier"))
                    throw new InvalidOperationException("attack_profile_physicalDamageMultiplier_invalid");
                if (row["effectBudgetMode"] != "damage_budget" && effectType is "magic" or "hybrid" && !ParseNonNegativeDouble(row["soulDamageMultiplier"], "soulDamageMultiplier"))
                    throw new InvalidOperationException("attack_profile_soulDamageMultiplier_invalid");
            }
            else if (effectType is "heal" or "buff" or "debuff" or "movement" or "restore_spirit")
            {
                // 既有 BattleSim 载体（AttackProfile/ArtConfig/DivineConfig）没有治疗量、增益倍率
                // 或移动语义字段；当前投影不能表达已声明效果时必须拒绝，不能丢列或读旧默认值。
                throw new InvalidOperationException("unsupported_effect_semantics");
            }
            else
            {
                throw new InvalidOperationException("attack_profile_effect_type_invalid");
            }

            string resourceKind = row["resourceKind"];
            if (resourceKind is not ("none" or "mp"))
                throw new InvalidOperationException("attack_profile_resource_kind_invalid");
            if (!ParseNonNegativeInt(row["resourceCost"], "resourceCost"))
                throw new InvalidOperationException("attack_profile_resourceCost_invalid");

            int minCastRange = ParseInt(row["minCastRange"]);
            int maxCastRange = ParseInt(row["maxCastRange"]);
            if (minCastRange < 0 || maxCastRange < minCastRange)
                throw new InvalidOperationException("attack_profile_cast_range_invalid");

            if (targetingMode == "single")
            {
                if (row["areaCenterKind"] != "" || row["areaShapeKind"] != "" ||
                    row["areaRadius"] != "" || row["areaLength"] != "" ||
                    row["areaFanHalfAngleSteps"] != "" || row["areaFacing"] != "" ||
                    row["areaInnerRadius"] != "" || row["areaEffectBlockers"] != "" ||
                    row["areaAllowedFactions"] != "" || row["areaAllowedStates"] != "")
                    throw new InvalidOperationException("single_target_area_fields_present");
                continue;
            }

            if (targetingMode != "area")
                throw new InvalidOperationException("attack_profile_targeting_mode_invalid");

            var centerKind = ParseAreaCenterKind(row["areaCenterKind"]);
            var shapeKind = ParseAreaShapeKind(row["areaShapeKind"]);
            if (centerKind == GameData.AreaCenterKind.Caster && (minCastRange != 0 || maxCastRange != 0))
                throw new InvalidOperationException("caster_center_range_invalid");
            if (string.IsNullOrEmpty(row["areaAllowedFactions"]))
                throw new InvalidOperationException("attack_profile_areaAllowedFactions_invalid");
            if (string.IsNullOrEmpty(row["areaAllowedStates"]))
                throw new InvalidOperationException("attack_profile_areaAllowedStates_invalid");

            int radius = ParseInt(row["areaRadius"]);
            int length = ParseInt(row["areaLength"]);
            int fanSteps = ParseInt(row["areaFanHalfAngleSteps"]);
            int innerRadius = ParseInt(row["areaInnerRadius"]);
            switch (shapeKind)
            {
                case GameData.AreaShapeKind.Circle:
                    if (radius < 0 || length != 0 || fanSteps != 0 || innerRadius < 0 || innerRadius > radius)
                        throw new InvalidOperationException("attack_profile_area_shape_contract_invalid");
                    if (!string.IsNullOrEmpty(row["areaFacing"]))
                        throw new InvalidOperationException("attack_profile_area_shape_contract_invalid");
                    break;
                case GameData.AreaShapeKind.Line:
                    if (radius != 0 || length <= 0 || fanSteps != 0 || innerRadius < 0 || innerRadius >= length)
                        throw new InvalidOperationException("attack_profile_area_shape_contract_invalid");
                    if (string.IsNullOrEmpty(row["areaFacing"]) || !IsDefinedHexDirection(row["areaFacing"]))
                        throw new InvalidOperationException("attack_profile_area_facing_invalid");
                    break;
                case GameData.AreaShapeKind.Fan:
                    if (radius != 0 || length <= 0 || fanSteps is < 0 or > 1 || innerRadius < 0 || innerRadius >= length)
                        throw new InvalidOperationException("attack_profile_area_shape_contract_invalid");
                    if (string.IsNullOrEmpty(row["areaFacing"]) || !IsDefinedHexDirection(row["areaFacing"]))
                        throw new InvalidOperationException("attack_profile_area_facing_invalid");
                    break;
            }

            ParseAreaEffectBlockers(row["areaEffectBlockers"]);
            ParseAreaTargetFactions(row["areaAllowedFactions"]);
            ParseAreaTargetStates(row["areaAllowedStates"]);
        }
    }

    static bool IsKnownElement(string value) => value is
        "element_metal" or "element_wood" or "element_water" or "element_fire" or
        "element_earth" or "element_wind" or "element_thunder" or "element_ice" or
        "element_dark" or "element_star" or "element_poison" or "element_chaos" or
        "element_none";

    static bool ParseNonNegativeInt(string value, string fieldName)
    {
        return int.TryParse(value, out int parsed) && parsed >= 0;
    }

    /// <summary>统一行动力的主动费与净费下限：严格大于 0，空值／分数／溢出都失败。</summary>
    static bool ParsePositiveInt(string value)
    {
        return int.TryParse(value, out int parsed) && parsed > 0;
    }

    /// <summary>CT 惩罚上界与公共合同一致：0～int.MaxValue-100。</summary>
    static bool ParseCtPenalty(string value)
    {
        return int.TryParse(value, out int parsed) && parsed >= 0 && parsed <= int.MaxValue - 100;
    }

    static bool ParseNonNegativeDouble(string value, string fieldName)
    {
        return double.TryParse(value, out double parsed) && parsed >= 0;
    }

    static int ParseInt(string value) => int.Parse(value);

    static double ParseDouble(string value) => double.Parse(value);

    static bool IsDefinedHexDirection(string value) => value is
        "east" or "north_east" or "north_west" or "west" or "south_west" or "south_east";

    static HexDirection ParseHexDirection(string value) => value switch
    {
        "east" => HexDirection.East,
        "north_east" => HexDirection.NorthEast,
        "north_west" => HexDirection.NorthWest,
        "west" => HexDirection.West,
        "south_west" => HexDirection.SouthWest,
        "south_east" => HexDirection.SouthEast,
        _ => throw new InvalidOperationException("attack_profile_area_facing_invalid"),
    };

    static GameData.AreaCenterKind ParseAreaCenterKind(string value) => value switch
    {
        "caster" => GameData.AreaCenterKind.Caster,
        "target_cell" => GameData.AreaCenterKind.TargetCell,
        _ => throw new InvalidOperationException("attack_profile_area_center_invalid"),
    };

    static GameData.AreaShapeKind ParseAreaShapeKind(string value) => value switch
    {
        "circle" => GameData.AreaShapeKind.Circle,
        "line" => GameData.AreaShapeKind.Line,
        "fan" => GameData.AreaShapeKind.Fan,
        _ => throw new InvalidOperationException("attack_profile_area_shape_invalid"),
    };

    static GameData.AreaEffectBlocker ParseAreaEffectBlockers(string value) => value switch
    {
        "none" => GameData.AreaEffectBlocker.None,
        "directed_edge" => GameData.AreaEffectBlocker.DirectedEdge,
        _ => throw new InvalidOperationException("attack_profile_area_blocker_invalid"),
    };

    static GameData.AreaTargetFaction ParseAreaTargetFactions(string value)
    {
        var parts = value.Split(',');
        if (parts.Length == 0 || parts.Any(string.IsNullOrEmpty) ||
            !parts.SequenceEqual(parts.OrderBy(item => item, StringComparer.Ordinal)))
            throw new InvalidOperationException("attack_profile_areaAllowedFactions_invalid");
        long result = 0;
        foreach (var part in parts)
        {
            result |= part switch
            {
                "enemy" => (long)GameData.AreaTargetFaction.Enemy,
                "ally" => (long)GameData.AreaTargetFaction.Ally,
                "self" => (long)GameData.AreaTargetFaction.Self,
                _ => throw new InvalidOperationException("attack_profile_areaAllowedFactions_invalid"),
            };
        }
        return (GameData.AreaTargetFaction)result;
    }

    static GameData.AreaTargetState ParseAreaTargetStates(string value)
    {
        var parts = value.Split(',');
        if (parts.Length == 0 || parts.Any(string.IsNullOrEmpty) ||
            !parts.SequenceEqual(parts.OrderBy(item => item, StringComparer.Ordinal)))
            throw new InvalidOperationException("attack_profile_areaAllowedStates_invalid");
        long result = 0;
        foreach (var part in parts)
        {
            result |= part switch
            {
                "alive" => (long)GameData.AreaTargetState.Alive,
                "corpse" => (long)GameData.AreaTargetState.Corpse,
                _ => throw new InvalidOperationException("attack_profile_areaAllowedStates_invalid"),
            };
        }
        return (GameData.AreaTargetState)result;
    }

    static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "Assets", "Tests", "EditMode", "Fixtures", "JindanStaticStates.fixture.csv")))
                return directory.FullName;
        }
        throw new InvalidOperationException("BattleSim could not locate the non-production Jindan fixture directory.");
    }

    static IReadOnlyList<string> ParseCompleteMansionAbilityIds(string raw, string fixtureId)
    {
        var completeAbilities = new List<string>();
        foreach (var item in raw.Split('|'))
        {
            var parts = item.Split('~');
            if (parts.Length < 2)
                throw new GoldenCoreAssemblyException("JD_UNKNOWN_STATIC_REFERENCE", $"{fixtureId} has an invalid mansion input.");
            if (parts[1] != "COMPLETE")
                continue;
            if (parts.Length < 5 || string.IsNullOrWhiteSpace(parts[4]))
                throw new GoldenCoreAssemblyException("JD_CARRIER_REFERENCE_INVALID", $"{fixtureId} has a complete mansion without guardian ability.");
            completeAbilities.Add(parts[4]);
        }
        return completeAbilities;
    }

    static GoldenCoreSeatBinding ParseSeat(string raw, string fixtureId)
    {
        var parts = ParseParts(raw, 9, "JD_STABLE_POSITION_LIMIT", fixtureId, "stablePositionBindings");
        var positionType = parts[3] switch
        {
            "SOURCE" => GoldenCoreSeatType.Source,
            "TRANSFORMATION" => GoldenCoreSeatType.Transformation,
            "DOMAIN" => GoldenCoreSeatType.Domain,
            _ => throw new GoldenCoreAssemblyException("JD_STABLE_POSITION_LIMIT", $"{fixtureId} has an unknown stable position type."),
        };
        return new GoldenCoreSeatBinding(
            parts[0],
            parts[2],
            positionType,
            parts[5],
            parts[6],
            parts[7],
            parts[8] == "none" ? Array.Empty<string>() : parts[8].Split('+'));
    }

    static GoldenCoreAbilityLedgerBinding ParseLedger(string raw, string fixtureId)
    {
        var parts = ParseParts(raw, 7, "JD_ABILITY_LEDGER_OWNERSHIP_INVALID", fixtureId, "abilityLedgerBindings");
        return new GoldenCoreAbilityLedgerBinding(
            parts[0],
            NoneToEmpty(parts[1]),
            NoneToEmpty(parts[2]),
            NoneToEmpty(parts[3]),
            NoneToEmpty(parts[4]),
            NoneToEmpty(parts[5]),
            NoneToEmpty(parts[6]));
    }

    static string[] ParseParts(string raw, int expectedCount, string errorCode, string fixtureId, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Contains('|'))
            throw new GoldenCoreAssemblyException(errorCode, $"{fixtureId} has multiple or empty {fieldName} records.");
        var parts = raw.Split('~');
        if (parts.Length != expectedCount)
            throw new GoldenCoreAssemblyException(errorCode, $"{fixtureId} has an invalid {fieldName} record.");
        return parts;
    }

    static string NoneToEmpty(string value) => value == "none" ? "" : value;

    static void RunDuelBounds()
    {
        var tieA = DuelTimeoutCharacter("tie-a", ranged: false);
        var tieB = DuelTimeoutCharacter("tie-b", ranged: false);
        var (tieWinsA, tieWinsB, tieTurns) = Combat.Simulate(tieA, tieB, rounds: 1);
        AssertEqual(50.0, tieWinsA, "equal timeout awards half a win to A");
        AssertEqual(50.0, tieWinsB, "equal timeout awards half a win to B");
        AssertEqual((double)Combat.DuelTurnLimit, tieTurns, "equal timeout stops at the duel turn limit");

        var ranged = DuelTimeoutCharacter("ranged", ranged: true);
        var stalled = DuelTimeoutCharacter("stalled", ranged: false);
        var (rangedWinsFirst, stalledWinsSecond, firstTurns) = Combat.Simulate(ranged, stalled, rounds: 1);
        AssertEqual(100.0, rangedWinsFirst, "higher remaining HP ratio wins from A");
        AssertEqual(0.0, stalledWinsSecond, "lower remaining HP ratio loses from B");
        AssertEqual((double)Combat.DuelTurnLimit, firstTurns, "asymmetric timeout stops at the duel turn limit");

        var (stalledWinsFirst, rangedWinsSecond, secondTurns) = Combat.Simulate(stalled, ranged, rounds: 1);
        AssertEqual(0.0, stalledWinsFirst, "lower remaining HP ratio loses from A");
        AssertEqual(100.0, rangedWinsSecond, "higher remaining HP ratio wins from B");
        AssertEqual((double)Combat.DuelTurnLimit, secondTurns, "swapped timeout stops at the duel turn limit");

        var quickA = CtTestCharacter("quick-a", 10);
        var quickB = CtTestCharacter("quick-b", 10);
        var (quickWinsA, quickWinsB, quickTurns) = Combat.Simulate(quickA, quickB, rounds: 1);
        AssertEqual(100.0, quickWinsA, "kill before the limit keeps the original winner");
        AssertEqual(0.0, quickWinsB, "kill before the limit keeps the original loser");
        if (quickTurns >= Combat.DuelTurnLimit)
            throw new InvalidOperationException($"kill before the limit took {quickTurns} turns.");

        var directionalRounds = Program.AllocateDirectionalBattleRounds(totalBattles: 100, pairs: 20);
        AssertEqual(40, directionalRounds.Length, "directional battle allocation slot count");
        AssertEqual(100, directionalRounds.Sum(), "directional battle allocation exact total");
        AssertEqual(2, directionalRounds.Min(), "directional battle allocation minimum");
        AssertEqual(3, directionalRounds.Max(), "directional battle allocation maximum");
        AssertSequence(
            directionalRounds,
            Program.AllocateDirectionalBattleRounds(totalBattles: 100, pairs: 20),
            "directional battle allocation is deterministic");
    }

    static void RunCritMultiplierTq053()
    {
        AssertClose(1.50, Combat.GetCritMultiplier(0), 0.0001, "zero critDamage keeps base multiplier");
        AssertClose(1.65, Combat.GetCritMultiplier(15), 0.0001, "15 critDamage adds percentage points");
        AssertClose(1.75, Combat.GetCritMultiplier(15, 10), 0.0001, "element bonus adds percentage points");
    }

    static void RunCombatParityV1()
    {
        string path = Path.Combine(FindRepositoryRoot(), "simulations", "BattleSim", "combat-parity-cases.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        AssertEqual("combat-parity-v1", root.GetProperty("version").GetString(), "combat parity version");
        JsonElement defaults = root.GetProperty("defaults");

        foreach (JsonElement caseElement in root.GetProperty("cases").EnumerateArray())
        {
            string caseId = caseElement.GetProperty("caseId").GetString() ?? "";
            int attack = (int)ReadParityNumber(caseElement, defaults, "attack");
            int defense = (int)ReadParityNumber(caseElement, defaults, "defense");
            double multiplier = ReadParityNumber(caseElement, defaults, "multiplier");
            double resistance = ReadParityNumber(caseElement, defaults, "resistance");
            double defensePenetration = ReadParityNumber(caseElement, defaults, "defensePenetration");
            double hitRateBonus = ReadParityNumber(caseElement, defaults, "hitRateBonus");
            double dodgeRate = ReadParityNumber(caseElement, defaults, "dodgeRate");
            double critRate = ReadParityNumber(caseElement, defaults, "critRate");
            double critDamage = ReadParityNumber(caseElement, defaults, "critDamage");
            double blockRate = ReadParityNumber(caseElement, defaults, "blockRate");
            double blockReduction = ReadParityNumber(caseElement, defaults, "blockReduction");
            double hitPercent = ReadParityNumber(caseElement, defaults, "hitPercent");
            double criticalPercent = ReadParityNumber(caseElement, defaults, "criticalPercent");
            double blockPercent = ReadParityNumber(caseElement, defaults, "blockPercent");

            JsonElement expected = caseElement.GetProperty("expected");
            bool expectedHit = expected.GetProperty("hit").GetBoolean();
            bool expectedCritical = expected.GetProperty("critical").GetBoolean();
            bool expectedBlocked = expected.GetProperty("blocked").GetBoolean();
            int expectedDamage = expected.GetProperty("damage").GetInt32();
            double expectedHitRate = expected.GetProperty("hitRate").GetDouble();
            double expectedCritMultiplier = expected.GetProperty("critMultiplier").GetDouble();
            double expectedBlockMultiplier = expected.GetProperty("blockMultiplier").GetDouble();

            var attacker = Character.Create(caseId + "-attacker", new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8 }, "physical");
            attacker.Primary["肉攻"] = attack;
            attacker.Secondary["命中率"] = hitRateBonus;
            attacker.Secondary["暴击率"] = critRate;
            attacker.Secondary["暴击伤害"] = critDamage;

            var defender = Character.Create(caseId + "-defender", new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8 }, "physical");
            defender.Primary["肉防"] = defense;
            defender.Secondary["闪避率"] = dodgeRate;
            defender.Secondary["格挡率"] = blockRate;
            defender.Secondary["格挡减伤率"] = blockReduction;

            int rawDmg = Combat.Dmg(attack, defense, resistance, defensePenetration, multiplier);
            var result = Combat.ResolveDefenseSettlement(
                rawDmg, attacker, defender, "物理",
                new Combat.CombatSettlementSamples(hitPercent, criticalPercent, blockPercent, 0));

            AssertEqual(expectedHit, result.Hit, $"{caseId} hit");
            AssertEqual(expectedCritical, result.Critical, $"{caseId} critical");
            AssertEqual(expectedBlocked, result.Blocked, $"{caseId} blocked");
            AssertEqual(expectedDamage, result.Damage, $"{caseId} damage");
            AssertClose(expectedHitRate, Combat.EffectiveHitRate(hitRateBonus, dodgeRate), 1e-4, $"{caseId} hit rate");
            AssertClose(expectedCritMultiplier, Combat.GetCritMultiplier(critDamage), 1e-4, $"{caseId} crit multiplier");
            AssertClose(expectedBlockMultiplier, Combat.ShieldReductionMultiplier(blockReduction), 1e-4, $"{caseId} block multiplier");
        }
    }

    static double ReadParityNumber(JsonElement caseElement, JsonElement defaults, string field) =>
        caseElement.TryGetProperty(field, out JsonElement value) ? value.GetDouble() : defaults.GetProperty(field).GetDouble();

    static void RunBuildInputTq054()
    {
        var balanced = BuildInputRules.Validate(new Dictionary<string, int>
        {
            ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8
        });
        AssertEqual(true, balanced.IsValid, "balanced point-buy input is valid");
        AssertEqual(25, balanced.PurchaseCost, "balanced input uses all purchase points");

        var aboveCap = BuildInputRules.Validate(new Dictionary<string, int>
        {
            ["根骨"] = 16, ["魂魄"] = 6, ["神识"] = 3, ["资质"] = 3, ["气运"] = 3
        });
        AssertEqual(false, aboveCap.IsValid, "above-cap input is invalid");
        AssertEqual("根骨必须在3到15之间。", aboveCap.Error, "above-cap input diagnostic");

        var overBudget = BuildInputRules.Validate(new Dictionary<string, int>
        {
            ["根骨"] = 15, ["魂魄"] = 7, ["神识"] = 3, ["资质"] = 3, ["气运"] = 3
        });
        AssertEqual(false, overBudget.IsValid, "over-budget input is invalid");
        AssertEqual("先天属性购买点数不能超过25。", overBudget.Error, "over-budget input diagnostic");

        var missingAttribute = BuildInputRules.Validate(new Dictionary<string, int>
        {
            ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8
        });
        AssertEqual(false, missingAttribute.IsValid, "missing attribute input is invalid");
        AssertEqual("缺少必填先天属性：气运。", missingAttribute.Error, "missing attribute diagnostic");

        var matrixInputs = Program.MatrixBuildInputs;
        AssertEqual(21, matrixInputs.Count, "matrix build input count");
        foreach (var input in matrixInputs)
        {
            var result = BuildInputRules.Validate(input);
            if (!result.IsValid)
                throw new InvalidOperationException($"matrix input should be valid: {result.Error}");
        }
    }

    static void RunGrowthIntegrityTq044()
    {
        AssertThrows<InvalidOperationException>(
            () => GameData.WeightsFromGongFa("不存在功法"),
            "unknown gongfa must not silently receive the generic 0.2 weight fallback");

        foreach (var name in new[] { "九霄雷劫录", "苦行剑典", "雷池淬体功", "南华玄感录", "绳墨正法录" })
        {
            var weights = GameData.WeightsFromGongFa(name);
            AssertEqual(5, weights.Count, $"approved fallback has all attributes for {name}");
        }

        var symbolCultivator = Character.Create("符修", new()
        {
            ["根骨"] = 3, ["魂魄"] = 3, ["神识"] = 3, ["资质"] = 3, ["气运"] = 3
        }, "taiyi_fuxiu");
        symbolCultivator.GongFaName = "云篆度人经";
        symbolCultivator.FinalizeStats("筑基", 0, "中品", GameData.WeightsFromGongFa(symbolCultivator.GongFaName));
        AssertEqual(false, symbolCultivator.Primary["HP"] <= 607, "筑基起始功法仍获得其筑基成长");
    }

    static void RunG2CoverageTq055()
    {
        var evaluator = typeof(Program).GetMethod(
            "EvaluateG2Coverage",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (evaluator == null)
            throw new InvalidOperationException("Program.EvaluateG2Coverage is missing.");

        var sufficient = evaluator.Invoke(null, new object[] { 200, 20, 2000 })!;
        AssertEqual("SUFFICIENT", ReadProperty(sufficient, "Status"), "threshold coverage status");
        AssertEqual(true, ReadProperty(sufficient, "MeetsThreshold"), "threshold coverage is accepted");

        var insufficient = evaluator.Invoke(null, new object[] { 199, 20, 2000 })!;
        AssertEqual("INSUFFICIENT", ReadProperty(insufficient, "Status"), "under-seeded coverage status");
        AssertEqual(false, ReadProperty(insufficient, "MeetsThreshold"), "under-seeded coverage is rejected");

        var interval = typeof(Program).GetMethod(
            "Wilson95Percent",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (interval == null)
            throw new InvalidOperationException("Program.Wilson95Percent is missing.");

        var bounds = ((ValueTuple<double, double>)interval.Invoke(null, new object[] { 0, 2000 })!);
        AssertEqual(true, bounds.Item1 >= 0.0 && bounds.Item2 > 0.0 && bounds.Item2 < 1.0,
            "zero-win Wilson interval remains bounded above zero");

        var targetStages = typeof(Program).GetProperty(
            "G2AuditTargetStages",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (targetStages == null)
            throw new InvalidOperationException("Program.G2AuditTargetStages is missing.");
        AssertSequence(new[] { "金丹" }, (IReadOnlyList<string>)targetStages.GetValue(null)!,
            "G2 audit targets the long-horizon gold-core matrix");
    }

    static void RunG2AuditCyclesTq055()
    {
        var parser = typeof(Program).GetMethod(
            "ParseG2AuditCycles",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (parser == null)
            throw new InvalidOperationException("Program.ParseG2AuditCycles is missing.");

        AssertEqual(200, parser.Invoke(null, new object[] { new[] { "--g2-audit" } }),
            "G2 audit keeps the 200-cycle default");
        AssertEqual(800, parser.Invoke(null, new object[] { new[] { "--g2-audit", "--cycles", "800" } }),
            "G2 audit accepts an explicit cycle horizon");
    }

    static void RunG2AttributionTq055()
    {
        var parser = typeof(Program).GetMethod(
            "ParseG2AttributionCycles",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (parser == null)
            throw new InvalidOperationException("Program.ParseG2AttributionCycles is missing.");

        AssertEqual(200, parser.Invoke(null, new object[] { new[] { "--g2-attribution" } }),
            "attribution keeps the 200-cycle default");
        AssertEqual(400, parser.Invoke(null, new object[] { new[] { "--g2-attribution", "--cycles", "400" } }),
            "attribution accepts an explicit positive horizon");

        AssertEqual(true, G2AttributionAudit.IsCoveredExtreme(200, 200, 2000, 0.0),
            "covered zero result is selected");
        AssertEqual(true, G2AttributionAudit.IsCoveredExtreme(200, 200, 2000, 100.0),
            "covered full-win result is selected");
        AssertEqual(false, G2AttributionAudit.IsCoveredExtreme(199, 200, 2000, 0.0),
            "under-seeded result is rejected");
        AssertEqual(false, G2AttributionAudit.IsCoveredExtreme(200, 200, 2000, 50.0),
            "non-extreme result is rejected");

        AssertEqual(false, G2AttributionAudit.Layers.Contains("远程资格统一"),
            "distance model removes the retired range-eligibility counterfactual");
        AssertSequence(new[] { "先天交换", "功法包交换", "风格机制交换" }, G2AttributionAudit.Layers,
            "remaining attribution layers are ordered");
    }

    static void RunG2ReproducibilityTq055()
    {
        var resetRandom = typeof(Combat).GetMethod(
            "ResetDeterministicRandom",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (resetRandom == null)
            throw new InvalidOperationException("Combat.ResetDeterministicRandom is missing.");

        var left = Character.Create("deterministic-left", new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8 }, "physical");
        var right = Character.Create("deterministic-right", new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8 }, "physical");
        foreach (var character in new[] { left, right })
        {
            character.Realm = "练气";
            character.Primary["HP"] = 1000;
            character.Primary["MP"] = 0;
            character.Primary["肉攻"] = 100;
            character.Primary["神攻"] = 0;
            character.Primary["肉防"] = 100;
            character.Primary["神防"] = 0;
            character.Primary["反应"] = 20;
            character.Primary["移力"] = 6;
            character.SetActionEconomyInputs(100000, 6000);
        }
        left.Secondary["暴击率"] = 50;
        right.Secondary["闪避率"] = 20;

        resetRandom.Invoke(null, null);
        var first = Combat.Simulate(left, right, 200);
        resetRandom.Invoke(null, null);
        var second = Combat.Simulate(left, right, 200);
        AssertEqual(first, second, "reset deterministic random reproduces a combat result");
    }

    static void RunDistanceModelTq055()
    {
        var unarmed = LegacyBasicFixture;
        AssertEqual(1, unarmed.MinRange, "unarmed basic attack minimum range");
        AssertEqual(1, unarmed.MaxRange, "unarmed basic attack maximum range");
        AssertEqual(2, GameData.TaiyiFuxiuArt.MinRange, "symbol art keeps an independent minimum range");
        AssertEqual(4, GameData.TaiyiFuxiuArt.MaxRange, "symbol art keeps an independent maximum range");
        AssertEqual(1, GameData.WaterDivine.MinRange, "water divine keeps an independent minimum range");
        AssertEqual(3, GameData.WaterDivine.MaxRange, "water divine keeps an independent maximum range");
        var character = Character.Create("basic-profile", new() { ["根骨"] = 1, ["魂魄"] = 1, ["神识"] = 1, ["资质"] = 1, ["气运"] = 1 }, "physical");
        AssertEqual<GameData.AttackProfile>(null, character.BasicAttackProfile, "unequipped character has no basic attack");
        character.BasicAttackProfile = new("测试主战法宝", "神魂", 1.1, "火", 2, 3);
        AssertEqual(3, character.BasicAttackProfile.MaxRange, "main combat artifact profile replaces unarmed fallback");
        var battlefield = new HexBattlefield();
        AssertEqual(6, Combat.InitialPositionA.DistanceTo(Combat.InitialPositionB), "distance-model opening separation");
        AssertEqual(new HexCoord(5, 0), battlefield.FindAttackPosition(new HexCoord(0, 0), new HexCoord(6, 0), 5, 1, 1), "melee moves before attacking");
        AssertEqual(new HexCoord(4, 0), battlefield.FindAttackPosition(new HexCoord(5, 0), new HexCoord(6, 0), 3, 2, 4), "minimum range forces retreat");
        AssertEqual(new HexCoord(0, 0), battlefield.FindAttackPosition(new HexCoord(0, 0), new HexCoord(6, 0), 0, 2, 4), "zero movement keeps position");
        int selected = Combat.SelectAction(battlefield, new HexCoord(0, 0), new HexCoord(4, 0), true, 1, 1, false, true, 2, 4, LegacyBasicFixture,
            out int selectedMinRange, out int selectedMaxRange);
        AssertEqual(Combat.ArtAction, selected, "legal art takes priority over out-of-range divine");
        AssertEqual((2, 4), (selectedMinRange, selectedMaxRange), "selected art range is retained for movement");
        int longRangeAction = Combat.SelectAction(battlefield, new HexCoord(0, 0), new HexCoord(6, 0), false, 1, 1, false, true, 2, 6, LegacyBasicFixture,
            out int longRangeMin, out int longRangeMax);
        AssertEqual(Combat.ArtAction, longRangeAction, "opening-range art is selected without a close-range fallback");
        AssertEqual(new HexCoord(0, 0), battlefield.FindAttackPosition(new HexCoord(0, 0), new HexCoord(6, 0), 5, longRangeMin, longRangeMax), "opening-range art needs no movement before casting");
    }

    static void RunBattlefieldFoundationNDist01()
    {
        var origin = new HexCoord(0, 0);
        AssertEqual(3, origin.DistanceTo(new HexCoord(2, -3)), "axial coordinates use six-direction hex distance");

        var terrain = new HexBattlefield(new Dictionary<HexCoord, HexCellRules>
        {
            [new HexCoord(1, 0)] = new(MovementCost: 3),
            [new HexCoord(0, 1)] = new(BlocksMovement: true),
        });
        var twoPointReach = terrain.FindReachable(origin, 2);
        AssertEqual(false, twoPointReach.ContainsKey(new HexCoord(1, 0)), "terrain entry cost consumes movement budget");
        AssertEqual(false, twoPointReach.ContainsKey(new HexCoord(0, 1)), "movement blocker cannot be entered");
        AssertEqual(true, terrain.FindReachable(origin, 3).ContainsKey(new HexCoord(1, 0)), "terrain becomes reachable when its explicit cost is paid");

        var sight = new HexBattlefield(new Dictionary<HexCoord, HexCellRules>
        {
            [new HexCoord(1, 0)] = new(BlocksSight: true),
        });
        AssertEqual(false, sight.HasLineOfSight(origin, new HexCoord(2, 0)), "intermediate sight blocker cuts line of sight");
        AssertEqual(true, sight.HasLineOfSight(origin, new HexCoord(0, 2)), "unblocked hex line remains visible");

        var blockedAdvance = new HexBattlefield(new Dictionary<HexCoord, HexCellRules>
        {
            [new HexCoord(1, 0)] = new(BlocksMovement: true, BlocksSight: true),
        });
        var firingPosition = blockedAdvance.FindAttackPosition(origin, new HexCoord(3, 0), 2, 1, 2);
        AssertEqual(2, firingPosition.DistanceTo(new HexCoord(3, 0)), "pathfinding sidesteps a blocked direct line to regain attack range");
        AssertEqual(false, firingPosition == new HexCoord(1, 0), "selected attack position does not enter the blocker");
        AssertEqual(true, blockedAdvance.HasLineOfSight(firingPosition, new HexCoord(3, 0)), "selected attack position has observable line of sight");
    }

    static void RunPositionControlNDist02()
    {
        var origin = new HexCoord(0, 0);
        var battlefield = new HexBattlefield();
        var chargeControl = GameData.BreakFormationChargeKnockback;
        AssertEqual(1, chargeControl.ForcedMovementDistance, "破阵冲锋 uses its documented one-hex knockback");

        var pushed = battlefield.ResolveForcedMovement(
            new HexCoord(2, 0),
            HexDirection.East,
            chargeControl.ForcedMovementDistance);
        AssertEqual(new HexCoord(3, 0), pushed, "knockback advances along the specified hex direction");
        AssertEqual(3, origin.DistanceTo(pushed), "knockback changes observable combat distance");

        var blockedPush = new HexBattlefield(new Dictionary<HexCoord, HexCellRules>
        {
            [new HexCoord(4, 0)] = new(BlocksMovement: true),
        });
        AssertEqual(
            new HexCoord(3, 0),
            blockedPush.ResolveForcedMovement(new HexCoord(2, 0), HexDirection.East, 3),
            "forced movement stops at the first blocked edge or landing cell");

        var rootedControl = GameData.RootedControl;
        AssertEqual(true, rootedControl.PreventsVoluntaryMovement, "定身 prevents voluntary movement");
        var rootedMelee = Combat.ResolveActionPosition(
            battlefield, origin, new HexCoord(3, 0), movementBudget: 3, minRange: 1, maxRange: 1,
            rootedControl.PreventsVoluntaryMovement);
        AssertEqual(origin, rootedMelee.Position, "rooted combatant does not move into range");

        var rootedLegalAttack = Combat.ResolveActionPosition(
            battlefield, origin, new HexCoord(3, 0), movementBudget: 3, minRange: 2, maxRange: 4,
            rootedControl.PreventsVoluntaryMovement);
        AssertEqual(origin, rootedLegalAttack.Position, "rooted combatant stays in place for a legal attack");
        AssertEqual(true, rootedLegalAttack.CanAttack, "rooted combatant may attack from a legal current position");

        var rootedInsideMinimum = Combat.ResolveActionPosition(
            battlefield, origin, new HexCoord(1, 0), movementBudget: 3, minRange: 2, maxRange: 4,
            rootedControl.PreventsVoluntaryMovement);
        AssertEqual(origin, rootedInsideMinimum.Position, "rooted combatant cannot retreat to satisfy minimum range");
        AssertEqual(false, rootedInsideMinimum.CanAttack, "minimum range rejects a rooted point-blank attack");
    }

    static void RunGroupAreaTargetingNGroup02()
    {
        var caster = new HexCoord(0, 0);
        var targetCell = new HexCoord(2, 0);
        var areaConfig = new GameData.AreaTargetingConfig(
            "fixture-circle",
            GameData.AreaCenterKind.TargetCell,
            MinCastRange: 1,
            MaxCastRange: 2,
            new GameData.AreaShapeConfig(
                GameData.AreaShapeKind.Circle,
                Radius: 2,
                Length: 0,
                FanHalfAngleSteps: 0,
                Facing: null,
                InnerRadius: 1),
            GameData.AreaEffectBlocker.DirectedEdge,
            GameData.AreaTargetFaction.Enemy,
            GameData.AreaTargetState.Alive);
        var candidates = new[]
        {
            new GameData.AreaTargetCandidate(Index: 0, Team: 0, Position: caster, IsAlive: true),
            new GameData.AreaTargetCandidate(Index: 1, Team: 0, Position: new HexCoord(3, -1), IsAlive: true),
            new GameData.AreaTargetCandidate(Index: 2, Team: 1, Position: new HexCoord(4, 0), IsAlive: true),
            new GameData.AreaTargetCandidate(Index: 3, Team: 1, Position: targetCell, IsAlive: true),
        };

        var sightBlockedOnly = new HexBattlefield(new Dictionary<HexCoord, HexCellRules>
        {
            [new HexCoord(3, 0)] = new(IsEntityObstacle: true),
        });
        AssertEqual(false, sightBlockedOnly.QueryLineOfSight(targetCell, new HexCoord(4, 0)).HasLineOfSight,
            "ordinary sight records an entity obstacle between area center and target");
        var circle = sightBlockedOnly.ResolveAreaTargeting(
            areaConfig, caster, casterTeam: 0, casterIndex: 0, targetCell, effectiveRangeModifier: 0, candidates);
        AssertEqual(targetCell, circle.Center, "target-cell skill exposes its resolved area center");
        AssertSequence(new[] { 2 }, circle.HitTargetIndexes,
            "circle uses its explicit inner hole and does not require ordinary unit sight");
        AssertEqual("", circle.RejectionReason, "legal area hit has no rejection reason");

        var boundedCells = new HashSet<HexCoord>
        {
            caster,
            new HexCoord(1, 0),
            targetCell,
            new HexCoord(3, 0),
            new HexCoord(4, 0),
        };
        var declaredEffectBlocker = new HexBattlefield(
            edgeRules: new Dictionary<DirectedHexEdge, HexEdgeRules>
            {
                [new(caster, new HexCoord(1, 0))] = new(GameData.EnvironmentRules.StandardEdgeUnits),
                [new(new HexCoord(1, 0), targetCell)] = new(GameData.EnvironmentRules.StandardEdgeUnits),
                [new(targetCell, new HexCoord(3, 0))] = new(
                    GameData.EnvironmentRules.StandardEdgeUnits,
                    EffectBlockers: GameData.AreaEffectBlocker.DirectedEdge),
                [new(new HexCoord(3, 0), new HexCoord(4, 0))] = new(GameData.EnvironmentRules.StandardEdgeUnits),
            },
            validCells: boundedCells);
        var blocked = declaredEffectBlocker.ResolveAreaTargeting(
            areaConfig, caster, casterTeam: 0, casterIndex: 0, targetCell, effectiveRangeModifier: 0, candidates);
        AssertEqual("declared_effect_blocker", blocked.RejectionReason,
            "declared effect blockers reject the area before target eligibility");

        var invalidCenter = new HexBattlefield(validCells: new HashSet<HexCoord> { caster }).ResolveAreaTargeting(
            areaConfig, caster, casterTeam: 0, casterIndex: 0, targetCell, effectiveRangeModifier: 0, candidates);
        AssertEqual("target_cell_invalid_or_out_of_bounds", invalidCenter.RejectionReason,
            "invalid target cells are the highest-priority area rejection");

        var restrictedTargets = new[]
        {
            new GameData.AreaTargetCandidate(Index: 2, Team: 1, Position: targetCell, IsAlive: false),
            new GameData.AreaTargetCandidate(Index: 1, Team: 0, Position: new HexCoord(3, 0), IsAlive: true),
        };
        var stateBeforeFaction = new HexBattlefield().ResolveAreaTargeting(
            areaConfig with { Shape = areaConfig.Shape with { InnerRadius = 0 } },
            caster,
            casterTeam: 0,
            casterIndex: 0,
            targetCell,
            effectiveRangeModifier: 0,
            restrictedTargets);
        AssertEqual("target_state_or_corpse_ineligible", stateBeforeFaction.RejectionReason,
            "state and corpse eligibility outrank faction rejection when no area target survives");

        var lineConfig = new GameData.AreaTargetingConfig(
            "fixture-line",
            GameData.AreaCenterKind.Caster,
            MinCastRange: 0,
            MaxCastRange: 0,
            new GameData.AreaShapeConfig(
                GameData.AreaShapeKind.Line,
                Radius: 0,
                Length: 2,
                FanHalfAngleSteps: 0,
                Facing: HexDirection.East,
                InnerRadius: 0),
            GameData.AreaEffectBlocker.None,
            GameData.AreaTargetFaction.Enemy,
            GameData.AreaTargetState.Alive);
        var line = new HexBattlefield().ResolveAreaTargeting(
            lineConfig,
            caster,
            casterTeam: 0,
            casterIndex: 0,
            targetCell: caster,
            effectiveRangeModifier: 0,
            new[]
            {
                new GameData.AreaTargetCandidate(Index: 2, Team: 1, Position: new HexCoord(1, 0), IsAlive: true),
                new GameData.AreaTargetCandidate(Index: 3, Team: 1, Position: new HexCoord(2, 0), IsAlive: true),
                new GameData.AreaTargetCandidate(Index: 4, Team: 1, Position: new HexCoord(1, -1), IsAlive: true),
            });
        AssertSequence(new[] { 2, 3 }, line.HitTargetIndexes,
            "line shape keeps only cells along its configured facing and length");

        var lineBlockedAtFront = new HexBattlefield(
            edgeRules: new Dictionary<DirectedHexEdge, HexEdgeRules>
            {
                [new(caster, new HexCoord(1, 0))] = new(
                    GameData.EnvironmentRules.StandardEdgeUnits,
                    EffectBlockers: GameData.AreaEffectBlocker.DirectedEdge),
            }).ResolveAreaTargeting(
                lineConfig with
                {
                    Name = "fixture-blocked-line",
                    Shape = lineConfig.Shape with { Length = 1 },
                    EffectBlockers = GameData.AreaEffectBlocker.DirectedEdge,
                },
                caster,
                casterTeam: 0,
                casterIndex: 0,
                targetCell: caster,
                effectiveRangeModifier: 0,
                new[]
                {
                    new GameData.AreaTargetCandidate(
                        Index: 2,
                        Team: 1,
                        Position: new HexCoord(1, 0),
                        IsAlive: true),
                });
        AssertEqual("declared_effect_blocker", lineBlockedAtFront.RejectionReason,
            "line propagation cannot leave its shape to route around a blocked front edge");

        var fan = new HexBattlefield().ResolveAreaTargeting(
            lineConfig with
            {
                Name = "fixture-fan",
                Shape = new GameData.AreaShapeConfig(
                    GameData.AreaShapeKind.Fan,
                    Radius: 0,
                    Length: 2,
                    FanHalfAngleSteps: 1,
                    Facing: HexDirection.East,
                    InnerRadius: 0),
            },
            caster,
            casterTeam: 0,
            casterIndex: 0,
            targetCell: caster,
            effectiveRangeModifier: 0,
            new[]
            {
                new GameData.AreaTargetCandidate(Index: 2, Team: 1, Position: new HexCoord(2, -1), IsAlive: true),
                new GameData.AreaTargetCandidate(Index: 3, Team: 1, Position: new HexCoord(-1, 0), IsAlive: true),
                new GameData.AreaTargetCandidate(Index: 4, Team: 1, Position: new HexCoord(0, 1), IsAlive: true),
            });
        AssertSequence(new[] { 2, 4 }, fan.HitTargetIndexes,
            "fan shape honors both boundaries of its local facing and angle parameter");

        var shortCast = new HexBattlefield().ResolveAreaTargeting(
            areaConfig with { MaxCastRange = 1 },
            caster,
            casterTeam: 0,
            casterIndex: 0,
            targetCell,
            effectiveRangeModifier: 0,
            candidates);
        AssertEqual("cast_distance_out_of_range", shortCast.RejectionReason,
            "cast distance is checked before target propagation and eligibility");
        AssertThrows<ArgumentException>(
            () => new HexBattlefield().ResolveAreaTargeting(
                areaConfig with { CenterKind = (GameData.AreaCenterKind)999 },
                caster,
                casterTeam: 0,
                casterIndex: 0,
                targetCell,
                effectiveRangeModifier: 0,
                candidates),
            "area targeting rejects an unknown center kind");
        AssertThrows<ArgumentException>(
            () => new HexBattlefield().ResolveAreaTargeting(
                areaConfig with { AllowedFactions = (GameData.AreaTargetFaction)8 },
                caster,
                casterTeam: 0,
                casterIndex: 0,
                targetCell,
                effectiveRangeModifier: 0,
                candidates),
            "area targeting rejects an unknown faction flag");
        AssertThrows<ArgumentException>(
            () => new HexBattlefield().ResolveAreaTargeting(
                areaConfig with { AllowedStates = (GameData.AreaTargetState)4 },
                caster,
                casterTeam: 0,
                casterIndex: 0,
                targetCell,
                effectiveRangeModifier: 0,
                candidates),
            "area targeting rejects an unknown state flag");
        AssertThrows<ArgumentException>(
            () => new HexBattlefield().ResolveAreaTargeting(
                lineConfig with { Shape = lineConfig.Shape with { Facing = (HexDirection)999 } },
                caster,
                casterTeam: 0,
                casterIndex: 0,
                targetCell: caster,
                effectiveRangeModifier: 0,
                new[]
                {
                    new GameData.AreaTargetCandidate(Index: 2, Team: 1, Position: new HexCoord(1, 0), IsAlive: true),
                }),
            "line area targeting rejects an unknown non-null facing");
        AssertThrows<ArgumentException>(
            () => new HexBattlefield().ResolveAreaTargeting(
                lineConfig with
                {
                    Name = "fixture-fan",
                    Shape = new GameData.AreaShapeConfig(
                        GameData.AreaShapeKind.Fan,
                        Radius: 0,
                        Length: 2,
                        FanHalfAngleSteps: 1,
                        Facing: (HexDirection)999,
                        InnerRadius: 0),
                },
                caster,
                casterTeam: 0,
                casterIndex: 0,
                targetCell: caster,
                effectiveRangeModifier: 0,
                new[]
                {
                    new GameData.AreaTargetCandidate(Index: 2, Team: 1, Position: new HexCoord(2, -1), IsAlive: true),
                }),
            "fan area targeting rejects an unknown non-null facing");

        AssertThrows<ArgumentException>(
            () => new HexBattlefield().ResolveAreaTargeting(
                areaConfig with { Shape = areaConfig.Shape with { Facing = HexDirection.East } },
                caster,
                casterTeam: 0,
                casterIndex: 0,
                targetCell,
                effectiveRangeModifier: 0,
                candidates),
            "circle area targeting rejects any non-null facing");
        AssertThrows<ArgumentException>(
            () => new HexBattlefield().ResolveAreaTargeting(
                lineConfig with { Shape = lineConfig.Shape with { Facing = null } },
                caster,
                casterTeam: 0,
                casterIndex: 0,
                targetCell: caster,
                effectiveRangeModifier: 0,
                new[]
                {
                    new GameData.AreaTargetCandidate(Index: 2, Team: 1, Position: new HexCoord(1, 0), IsAlive: true),
                }),
            "line area targeting rejects a null facing");
        AssertThrows<ArgumentException>(
            () => new HexBattlefield().ResolveAreaTargeting(
                lineConfig with
                {
                    Shape = new GameData.AreaShapeConfig(
                        GameData.AreaShapeKind.Fan,
                        Radius: 0,
                        Length: 2,
                        FanHalfAngleSteps: 1,
                        Facing: null,
                        InnerRadius: 0),
                },
                caster,
                casterTeam: 0,
                casterIndex: 0,
                targetCell: caster,
                effectiveRangeModifier: 0,
                new[]
                {
                    new GameData.AreaTargetCandidate(Index: 2, Team: 1, Position: new HexCoord(2, -1), IsAlive: true),
                }),
            "fan area targeting rejects a null facing");

        var areaActor = GroupTestCharacter(
            "area-observation-a1",
            reaction: 100,
            hp: 10000,
            attack: 1000,
            movement: 0);
        areaActor.BasicAttackProfile = new GameData.AttackProfile(
            "fixture-area-basic",
            "物理",
            1.0,
            "",
            MinRange: 1,
            MaxRange: 6,
            areaConfig with
            {
                Name = "fixture-observed-circle",
                MinCastRange = 1,
                MaxCastRange = 6,
                Shape = areaConfig.Shape with { Radius = 1, InnerRadius = 0 },
                EffectBlockers = GameData.AreaEffectBlocker.None,
            }, Cost: new("fixture-observed-circle", "fixture", 50000, 50000, 0, 0));
        var sightBlockedGroupBattlefield = new HexBattlefield(
            cells: new Dictionary<HexCoord, HexCellRules>
            {
                [new HexCoord(3, 0)] = new(IsEntityObstacle: true),
            });
        AssertEqual(
            false,
            sightBlockedGroupBattlefield.QueryLineOfSight(
                Combat.InitialGroupPositions[0],
                Combat.InitialGroupPositions[2]).HasLineOfSight,
            "group fixture blocks ordinary sight between the area actor and primary target");
        var areaRound = Combat.Simulate2v2Detailed(
            sightBlockedGroupBattlefield,
            areaActor,
            GroupTestCharacter("area-observation-a2", reaction: 1, hp: 10000, attack: 1, movement: 0),
            GroupTestCharacter("area-observation-b1", reaction: 1, hp: 10000, attack: 1, movement: 0),
            GroupTestCharacter("area-observation-b2", reaction: 1, hp: 10000, attack: 1, movement: 0));
        var observedAreaAction = areaRound.Actions.First(action => action.ActorIndex == 0);
        AssertEqual(14, sightBlockedGroupBattlefield.QueryAreaEffectDistance(Combat.InitialGroupPositions[0],
            Combat.InitialGroupPositions[2], GameData.AreaEffectBlocker.None).DistanceUnits,
            "entity obstacle makes the first center seven actual range units away even without sight blocking");
        AssertEqual(
            Combat.InitialGroupPositions[3],
            observedAreaAction.AreaCenter,
            "formal 2v2 records the resolved target-cell area center without ordinary sight");
        AssertSequence(
            new[] { 2, 3 },
            observedAreaAction.AreaHitTargetIndexes,
            "formal 2v2 records every legal enemy hit by the configured area");
        var areaCommit = areaRound.Economy.First(e => e.ActorIndex == 0 && e.Event == "action_committed");
        AssertEqual(50000, areaCommit.CostUnits, "one area root command pays once for both targets");
        AssertSequence(new[] { 9500, 9500 }, areaCommit.HealthByActor.Skip(2).ToArray(), "the selected area actually settles both enemy hits");
        AssertEqual("", observedAreaAction.AreaRejectionReason,
            "formal 2v2 records no rejection for a legal area action");

        AssertEqual(true, typeof(Combat).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Any(method => method.Name == "Simulate2v2" &&
                               method.GetParameters().Length == 6 &&
                               method.GetParameters()[0].ParameterType == typeof(HexBattlefield)),
            "formal 2v2 entry requires an explicit battlefield input");
        AssertEqual(true, new[] { "AreaCenter", "AreaHitTargetIndexes", "AreaRejectionReason" }
                .All(name => typeof(Combat.GroupActionObservation).GetProperty(name) != null),
            "group action observations expose area center, hit set, and rejection reason");
    }

    static void RunGroupActionPriorityNGroup02()
    {
        var confirmedKill = Combat.SelectGroupTarget(
            actorTeam: 0,
            new[]
            {
                GroupPriorityCandidate(
                    primaryTargetIndex: 2,
                    hp: 10,
                    inputOrder: 0,
                    legalHitTargetIndexes: new[] { 2, 3 },
                    settlementEvidence: Combat.GroupActionSettlementEvidence.Resolved(new[] { 2 })),
                GroupPriorityCandidate(
                    primaryTargetIndex: 3,
                    hp: 1,
                    inputOrder: 1,
                    legalHitTargetIndexes: new[] { 3 },
                    settlementEvidence: Combat.GroupActionSettlementEvidence.Resolved(Array.Empty<int>())),
                GroupPriorityCandidate(
                    primaryTargetIndex: 4,
                    hp: 1,
                    inputOrder: 2,
                    legalHitTargetIndexes: new[] { 3, 4 },
                    settlementEvidence: Combat.GroupActionSettlementEvidence.Unavailable()),
            });
        AssertEqual(
            "selected_kill_count_then_legal_hit_count_then_lowest_hp_then_input_order",
            confirmedKill.Reason,
            "group selection exposes the approved deterministic priority reason");
        AssertEqual(2, confirmedKill.TargetIndex,
            "a confirmed kill count outranks legal-hit count and current HP");
        var confirmedPriority = confirmedKill.Priority
            ?? throw new InvalidOperationException("selected candidate priority is observable");
        AssertEqual(1, confirmedPriority.ConfirmedKillTargetCount,
            "selected priority exposes confirmed kill count");
        AssertEqual(2, confirmedPriority.LegalHitTargetCount,
            "selected priority exposes legal hit count");
        AssertEqual(10, confirmedPriority.PrimaryTargetHP,
            "selected priority exposes current primary target HP");
        AssertEqual(0, confirmedPriority.CandidateInputOrder,
            "selected priority exposes input order");
        AssertEqual(Combat.GroupActionSettlementEvidenceStatus.Resolved,
            confirmedPriority.SettlementEvidenceStatus,
            "resolved evidence remains distinguishable from unavailable evidence");

        var legalHits = Combat.SelectGroupTarget(
            actorTeam: 0,
            new[]
            {
                GroupPriorityCandidate(2, hp: 1, inputOrder: 0, legalHitTargetIndexes: new[] { 2 },
                    settlementEvidence: Combat.GroupActionSettlementEvidence.Unavailable()),
                GroupPriorityCandidate(3, hp: 100, inputOrder: 1, legalHitTargetIndexes: new[] { 2, 3 },
                    settlementEvidence: Combat.GroupActionSettlementEvidence.Unavailable()),
            });
        AssertEqual(3, legalHits.TargetIndex,
            "legal hit count is compared before primary target HP");
        var unavailablePriority = legalHits.Priority
            ?? throw new InvalidOperationException("unavailable candidate priority is observable");
        AssertEqual(0, unavailablePriority.ConfirmedKillTargetCount,
            "unavailable evidence contributes zero without becoming resolved evidence");
        AssertEqual(2, unavailablePriority.LegalHitTargetCount,
            "legal hit targets are not interpreted as kill targets");
        AssertEqual(Combat.GroupActionSettlementEvidenceStatus.Unavailable,
            unavailablePriority.SettlementEvidenceStatus,
            "unavailable evidence status stays observable");
        AssertEqual("settlement_evidence_unavailable", unavailablePriority.SettlementEvidenceReason,
            "unavailable evidence keeps its stable reason");

        var lowestHp = Combat.SelectGroupTarget(
            actorTeam: 0,
            new[]
            {
                GroupPriorityCandidate(2, hp: 50, inputOrder: 3, legalHitTargetIndexes: new[] { 2, 3 },
                    settlementEvidence: Combat.GroupActionSettlementEvidence.Unavailable()),
                GroupPriorityCandidate(3, hp: 10, inputOrder: 4, legalHitTargetIndexes: new[] { 2, 3 },
                    settlementEvidence: Combat.GroupActionSettlementEvidence.Unavailable()),
            });
        AssertEqual(3, lowestHp.TargetIndex,
            "primary target HP is compared after kill and legal-hit counts");

        var inputOrder = Combat.SelectGroupTarget(
            actorTeam: 0,
            new[]
            {
                GroupPriorityCandidate(2, hp: 10, inputOrder: 9, legalHitTargetIndexes: new[] { 2 },
                    settlementEvidence: Combat.GroupActionSettlementEvidence.Unavailable()),
                GroupPriorityCandidate(3, hp: 10, inputOrder: 8, legalHitTargetIndexes: new[] { 3 },
                    settlementEvidence: Combat.GroupActionSettlementEvidence.Unavailable()),
            });
        AssertEqual(3, inputOrder.TargetIndex,
            "the existing candidate input order breaks complete priority ties");

        var samePrimaryDifferentHits = Combat.SelectGroupTarget(
            actorTeam: 0,
            new[]
            {
                GroupPriorityCandidate(2, hp: 10, inputOrder: 3, legalHitTargetIndexes: new[] { 2 },
                    settlementEvidence: Combat.GroupActionSettlementEvidence.Unavailable()),
                GroupPriorityCandidate(2, hp: 10, inputOrder: 4, legalHitTargetIndexes: new[] { 2, 3 },
                    settlementEvidence: Combat.GroupActionSettlementEvidence.Unavailable()),
            });
        AssertEqual(2, samePrimaryDifferentHits.TargetIndex,
            "multiple action candidates may retain the same primary target");
        AssertEqual(4, samePrimaryDifferentHits.Priority?.CandidateInputOrder,
            "same-primary candidates remain separate so their legal hit sets can decide selection");

        var observedRound = Combat.Simulate2v2Detailed(
            HexBattlefield.CreateTechnicalFixture(),
            GroupTestCharacter("priority-a1", reaction: 100, hp: 10000, attack: 1, movement: 6),
            GroupTestCharacter("priority-a2", reaction: 1, hp: 10000, attack: 1, movement: 6),
            GroupTestCharacter("priority-b1", reaction: 1, hp: 10000, attack: 1, movement: 6),
            GroupTestCharacter("priority-b2", reaction: 1, hp: 10000, attack: 1, movement: 6));
        var observedPriority = observedRound.Actions.First(action => action.TargetSelectionPriority != null)
            .TargetSelectionPriority.Value;
        AssertEqual(Combat.GroupActionSettlementEvidenceStatus.Unavailable,
            observedPriority.SettlementEvidenceStatus,
            "selection-period 2v2 candidates do not claim resolved settlement evidence");
        AssertEqual("settlement_evidence_unavailable", observedPriority.SettlementEvidenceReason,
            "selection-period 2v2 candidates keep the unavailable evidence reason");
    }

    static void RunGroupPositioningNGroup01()
    {
        AssertSequence(
            new[]
            {
                new HexCoord(0, 0),
                new HexCoord(0, 1),
                new HexCoord(6, 0),
                new HexCoord(6, -1),
            },
            Combat.InitialGroupPositions,
            "approved mirrored adjacent deployment");
        AssertEqual(4, Combat.InitialGroupPositions.Distinct().Count(),
            "four combatants start on unique cells");

        var origin = new HexCoord(0, 0);
        var friendlyBlocker = new HexCoord(1, 0);
        var enemyBlocker = new HexCoord(1, -1);
        var occupied = new HashSet<HexCoord> { friendlyBlocker, enemyBlocker };
        var reachable = new HexBattlefield().FindReachable(origin, movementBudget: 4, occupied);
        AssertEqual(false, reachable.ContainsKey(friendlyBlocker),
            "friendly occupancy blocks entry and traversal");
        AssertEqual(false, reachable.ContainsKey(enemyBlocker),
            "enemy occupancy blocks entry and traversal");
        AssertEqual(true, reachable.ContainsKey(new HexCoord(2, -1)),
            "reachable query exposes a deterministic route around occupied cells");

        var targetSelection = Combat.SelectGroupTarget(
            actorTeam: 0,
            new[]
            {
                new Combat.GroupTargetCandidate(1, Team: 0, HP: 1, IsAlive: true, IsLegal: true),
                GroupPriorityCandidate(2, hp: 10, inputOrder: 2, legalHitTargetIndexes: new[] { 2 },
                    settlementEvidence: Combat.GroupActionSettlementEvidence.Unavailable()),
                GroupPriorityCandidate(3, hp: 10, inputOrder: 3, legalHitTargetIndexes: new[] { 3 },
                    settlementEvidence: Combat.GroupActionSettlementEvidence.Unavailable()),
            });
        AssertEqual(2, targetSelection.TargetIndex,
            "equal-HP legal enemies resolve by input order");
        AssertEqual("selected_kill_count_then_legal_hit_count_then_lowest_hp_then_input_order", targetSelection.Reason,
            "target selection reason is observable");

        var reselection = Combat.SelectGroupTarget(
            actorTeam: 0,
            new[]
            {
                new Combat.GroupTargetCandidate(2, Team: 1, HP: 0, IsAlive: false, IsLegal: false),
                GroupPriorityCandidate(3, hp: 20, inputOrder: 3, legalHitTargetIndexes: new[] { 3 },
                    settlementEvidence: Combat.GroupActionSettlementEvidence.Unavailable()),
            });
        AssertEqual(3, reselection.TargetIndex,
            "a dead target is deterministically replaced by a surviving legal enemy");

        var noTarget = Combat.SelectGroupTarget(
            actorTeam: 0,
            new[]
            {
                new Combat.GroupTargetCandidate(2, Team: 1, HP: 20, IsAlive: true, IsLegal: false),
                new Combat.GroupTargetCandidate(3, Team: 1, HP: 30, IsAlive: false, IsLegal: false),
            });
        AssertEqual(-1, noTarget.TargetIndex, "no legal enemy produces no target");
        AssertEqual("no_legal_target", noTarget.Reason,
            "no-target reason is observable");

        var orderedRound = Combat.Simulate2v2Detailed(
            HexBattlefield.CreateTechnicalFixture(),
            GroupTestCharacter("a1", reaction: 20, hp: 10000, attack: 1, movement: 6),
            GroupTestCharacter("a2", reaction: 20, hp: 10000, attack: 1, movement: 6),
            GroupTestCharacter("b1", reaction: 20, hp: 10000, attack: 1, movement: 6),
            GroupTestCharacter("b2", reaction: 20, hp: 10000, attack: 1, movement: 6));
        AssertSequence(new[] { 0, 1, 2, 3 },
            orderedRound.Economy.Where(action => action.Event == "own_turn_started").Take(4).Select(action => action.ActorIndex).ToArray(),
            "equal CT resolves in stable actor ID order without advancing between ready actors");
        AssertEqual(true, orderedRound.Actions[0].ReachablePositions.Count > 0,
            "each action exposes its reachable cells");
        AssertEqual(true, orderedRound.Actions.All(action =>
                action.PositionsAfterAction.Distinct().Count() == action.PositionsAfterAction.Count),
            "single-cell occupancy remains unique after every action");
        AssertEqual(true, orderedRound.Actions.All(action =>
                !string.IsNullOrWhiteSpace(action.TargetSelectionReason)),
            "each action exposes target selection");

        var unableRound = Combat.Simulate2v2Detailed(
            HexBattlefield.CreateTechnicalFixture(),
            GroupTestCharacter("rooted-a1", reaction: 20, hp: 10000, attack: 1, movement: 0),
            GroupTestCharacter("rooted-a2", reaction: 20, hp: 10000, attack: 1, movement: 0),
            GroupTestCharacter("rooted-b1", reaction: 20, hp: 10000, attack: 1, movement: 0),
            GroupTestCharacter("rooted-b2", reaction: 20, hp: 10000, attack: 1, movement: 0));
        AssertEqual(-1, unableRound.Actions[0].TargetIndex,
            "an action with no reachable legal enemy selects no target");
        AssertEqual("no_legal_target_after_move", unableRound.Actions[0].InactionReason,
            "an action exposes why it could not attack");

        var strongA1 = GroupTestCharacter("strong-a1", reaction: 100, hp: 100, attack: 1000, movement: 6);
        var strongA2 = GroupTestCharacter("strong-a2", reaction: 100, hp: 100, attack: 1000, movement: 6);
        var weakB1 = GroupTestCharacter("weak-b1", reaction: 1, hp: 1, attack: 1, movement: 0);
        var weakB2 = GroupTestCharacter("weak-b2", reaction: 1, hp: 2, attack: 1, movement: 0);
        Combat.ResetDeterministicRandom();
        var first = Combat.Simulate2v2(
            HexBattlefield.CreateTechnicalFixture(), strongA1, strongA2, weakB1, weakB2, rounds: 1);
        Combat.ResetDeterministicRandom();
        var second = Combat.Simulate2v2(
            HexBattlefield.CreateTechnicalFixture(), strongA1, strongA2, weakB1, weakB2, rounds: 1);
        AssertEqual((100.0, 0.0), (first.winsA, first.winsB),
            "group victory settlement keeps the surviving team");
        AssertEqual(first, second, "fixed seed reproduces the same group result");
    }

    static void RunCombatStateRewindCausalNState01()
    {
        CombatLocalStateSnapshot Snapshot(string position, int hitPoints, int mana) => new(
            position,
            "walk",
            "ground",
            "alive",
            hitPoints,
            mana,
            "stance:none",
            "process:none",
            new Dictionary<string, int> { ["art"] = 2, ["divine"] = 4 },
            new Dictionary<string, int> { ["art"] = 1 },
            new Dictionary<string, int> { ["potion"] = 1 });

        var actor = Character.Create(
            "state-actor",
            new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8 },
            "physical");
        var state = actor.StartCombatState(Snapshot("0,0", 100, 50));
        AssertEqual(1, state.AutomaticResponseCapacity, "configured response capacity is explicit");

        var buffProfile = new CombatTemporaryStatusProfile(
            "fixture-guard",
            CombatStatusCarrierKind.TemporaryStatus,
            CombatStatusPolarity.Buff,
            new[] { CombatStatusTag.Defense },
            CombatStatusSourceKind.SelfAbility,
            CombatStatusRemovalPolicy.Normal,
            DefinitionVersion: 1);
        var wrongCarrier = state.TryApplyTemporaryStatus(new CombatStatusApplication(
            "fixture-wrong-carrier",
            buffProfile with { CarrierKind = CombatStatusCarrierKind.StanceState },
            "state-actor",
            "state-actor",
            2));
        AssertEqual(false, wrongCarrier.IsApplied, "stance state cannot enter temporary-status carrier");
        AssertEqual("STATE_CARRIER_NOT_TEMPORARY_STATUS", wrongCarrier.Reason, "carrier rejection is stable");

        var guard = state.TryApplyTemporaryStatus(new CombatStatusApplication(
            "fixture-guard-instance",
            buffProfile,
            "state-actor",
            "state-actor",
            2));
        AssertEqual(true, guard.IsApplied, "complete buff status is accepted by its owner");
        var mixed = state.TryApplyTemporaryStatus(new CombatStatusApplication(
            "fixture-mixed-instance",
            new CombatTemporaryStatusProfile(
                "fixture-mixed",
                CombatStatusCarrierKind.TemporaryStatus,
                CombatStatusPolarity.Mixed,
                new[] { CombatStatusTag.Offense, CombatStatusTag.Resource },
                CombatStatusSourceKind.OtherAbility,
                CombatStatusRemovalPolicy.Normal,
                DefinitionVersion: 1),
            "other-actor",
            "state-actor",
            1));
        AssertEqual(true, mixed.IsApplied, "mixed polarity is explicit rather than neutral");
        var polarityConflict = state.TryApplyTemporaryStatus(new CombatStatusApplication(
            "fixture-guard-debuff-instance",
            buffProfile with { Polarity = CombatStatusPolarity.Debuff },
            "other-actor",
            "state-actor",
            1));
        AssertEqual(false, polarityConflict.IsApplied, "one status id cannot change polarity by source or target");
        AssertEqual("STATE_POLARITY_CONFLICT", polarityConflict.Reason, "polarity rejection is stable");

        state.RecordOwnActionCheckpoint("fixture-checkpoint", ownActionSequence: 1);
        var checkpointGuard = state.TemporaryStatuses.Single(status => status.InstanceId == "fixture-guard-instance");
        state.SynchronizeLocalState(Snapshot("3,1", 40, 10));
        var localDebuff = state.TryApplyTemporaryStatus(new CombatStatusApplication(
            "fixture-local-debuff",
            new CombatTemporaryStatusProfile(
                "fixture-local-debuff",
                CombatStatusCarrierKind.TemporaryStatus,
                CombatStatusPolarity.Debuff,
                new[] { CombatStatusTag.Control },
                CombatStatusSourceKind.OtherAbility,
                CombatStatusRemovalPolicy.Normal,
                DefinitionVersion: 1),
            "other-actor",
            "state-actor",
            1));
        AssertEqual(true, localDebuff.IsApplied, "post-checkpoint local state can change");
        AssertEqual(true, state.RecordLedgerEntry(new CombatStateLedgerEntry(
            "fixture-external-cost", 2, CombatStateLedgerScope.ExternalCommitted, HitPointCost: 8, ManaCost: 5)).IsApplied,
            "external committed cost is classified");
        AssertEqual(true, state.RecordLedgerEntry(new CombatStateLedgerEntry(
            "fixture-protected-cost", 3, CombatStateLedgerScope.ProtectedHistory, HitPointCost: 2, ManaCost: 3)).IsApplied,
            "protected history cost is classified");

        var createdDebt = state.CreateCausalDebt(new CausalDebtSpec(
            "fixture-debt",
            "fixture-action",
            "fixture-result",
            "state-actor",
            ResourceCost: 12,
            ResultBudget: 7,
            DueOwnActionSequence: 2));
        AssertEqual(true, createdDebt.IsApplied, "causal debt is created before rewind");

        var rewind = state.Rewind("fixture-rewind", "fixture-checkpoint");
        AssertEqual(true, rewind.IsApplied, "local checkpoint rewind succeeds");
        AssertEqual(1, rewind.ReappliedExternalEntryCount, "external costs are replayed after local restore");
        AssertEqual(1, rewind.ReappliedProtectedEntryCount, "protected costs are replayed after local restore");
        AssertEqual("0,0", state.CurrentLocalState.PositionId, "rewind restores local position only");
        AssertEqual(90, state.CurrentLocalState.HitPoints, "rewind replays external and protected health costs");
        AssertEqual(42, state.CurrentLocalState.Mana, "rewind replays external and protected mana costs");
        AssertEqual(false, state.TemporaryStatuses.Any(status => status.InstanceId == "fixture-local-debuff"),
            "post-checkpoint target-local status is removed");
        AssertEqual(true, ReferenceEquals(checkpointGuard, state.TemporaryStatuses.Single(status => status.InstanceId == "fixture-guard-instance")),
            "rewind restores the same unique status instance without copying it");
        AssertEqual(1, state.CausalDebts.Count, "causal debt remains protected history across rewind");

        var repeatedRewind = state.Rewind("fixture-rewind", "fixture-checkpoint");
        AssertEqual(rewind, repeatedRewind, "same rewind event is idempotent");
        AssertEqual(42, state.CurrentLocalState.Mana, "idempotent rewind does not replay costs twice");

        var acceptedResponse = state.TryConsumeAutomaticResponse(new AutomaticResponseAttempt(
            "fixture-root-1", "fixture-rule-1", true, true, true, true, true));
        AssertEqual(true, acceptedResponse.IsAccepted, "fully legal response consumes capacity only on settlement");
        AssertEqual(0, acceptedResponse.RemainingCapacity, "accepted response consumes exactly one capacity");
        var exhaustedResponse = state.TryConsumeAutomaticResponse(new AutomaticResponseAttempt(
            "fixture-root-2", "fixture-rule-1", true, true, true, true, true));
        AssertEqual(false, exhaustedResponse.IsAccepted, "capacity exhaustion rejects later response");
        AssertEqual("CAUSAL_RESPONSE_CAPACITY_EXHAUSTED", exhaustedResponse.Reason, "capacity rejection is stable");
        state.CompleteOwnActiveAction();
        var restoredResponse = state.TryConsumeAutomaticResponse(new AutomaticResponseAttempt(
            "fixture-root-3", "fixture-rule-1", true, true, true, true, true));
        AssertEqual(true, restoredResponse.IsAccepted, "completed own action restores configured response capacity");
        var invalidResponse = state.TryConsumeAutomaticResponse(new AutomaticResponseAttempt(
            "fixture-root-4", "fixture-rule-1", false, true, true, true, true));
        AssertEqual(false, invalidResponse.IsAccepted, "invalid response never spends capacity");
        AssertEqual("CAUSAL_RESPONSE_TARGET_INVALID", invalidResponse.Reason, "pre-settlement rejection is stable");

        var recipientCharacter = Character.Create(
            "state-recipient",
            new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8 },
            "physical");
        var recipient = recipientCharacter.StartCombatState(Snapshot("1,0", 80, 100));
        var transfer = state.TransferCausalDebt("fixture-transfer", "fixture-debt", recipient, true, true);
        AssertEqual(true, transfer.IsApplied, "eligible participant receives the existing debt instance");
        AssertEqual(0, state.CausalDebts.Count, "transfer removes debt from prior holder instead of copying it");
        AssertEqual(1, recipient.CausalDebts.Count, "transfer leaves exactly one debt owner");
        AssertEqual("state-recipient", recipient.CausalDebts[0].CurrentHolderCombatantId, "debt holder changes deterministically");
        AssertEqual(12, recipient.CausalDebts[0].OutstandingResourceCost, "transfer preserves outstanding debt conservation");
        AssertEqual(transfer, state.TransferCausalDebt("fixture-transfer", "fixture-debt", recipient, true, true),
            "same transfer operation is idempotent");

        var repaid = recipient.RepayCausalDebt("fixture-repayment", "fixture-debt");
        AssertEqual(true, repaid.IsApplied, "current holder pays the full debt exactly once");
        AssertEqual(CausalDebtSettlementState.Repaid, recipient.CausalDebts[0].State, "debt closes as repaid");
        AssertEqual(0, recipient.CausalDebts[0].OutstandingResourceCost, "repayment clears outstanding resource cost");
        AssertEqual(88, recipient.CurrentLocalState.Mana, "repayment debits the current holder once");
        AssertEqual(repaid, recipient.RepayCausalDebt("fixture-repayment", "fixture-debt"),
            "same repayment operation is idempotent");
        AssertEqual(88, recipient.CurrentLocalState.Mana, "repeated repayment does not double-pay");
    }

    static void RunEnvironmentRulesNEnv01()
    {
        var origin = new HexCoord(0, 0);
        var east = origin.Step(HexDirection.East);
        var eastTwo = east.Step(HexDirection.East);
        var eastThree = eastTwo.Step(HexDirection.East);
        var metric = GameData.EnvironmentRules;

        AssertEqual(2, metric.UnitsPerRange, "environment metric uses fixed-point range units");
        AssertEqual(1, metric.CompressedEdgeUnits, "compressed edge fixture tier");
        AssertEqual(2, metric.StandardEdgeUnits, "standard edge fixture tier");
        AssertEqual(4, metric.ExpandedEdgeUnits, "expanded edge fixture tier");
        AssertEqual(true, metric.MaxQueryRange > 0, "weighted queries have a configured bound");

        var directed = new HexBattlefield(edgeRules: new Dictionary<DirectedHexEdge, HexEdgeRules>
        {
            [new(origin, east)] = new(metric.StandardEdgeUnits, AllowsMovement: true),
            [new(east, origin)] = new(metric.StandardEdgeUnits, AllowsMovement: false),
        });
        AssertEqual(true, directed.InspectEdge(origin, east, SpatialQueryKind.Movement).IsLegal,
            "directed edge allows its declared direction");
        var reverseEdge = directed.InspectEdge(east, origin, SpatialQueryKind.Movement);
        AssertEqual(false, reverseEdge.IsLegal, "directed edge rejects its reverse direction");
        AssertEqual("directed_edge_blocks_movement", reverseEdge.Reason,
            "directed edge rejection is observable");

        var compressed = new HexBattlefield(edgeRules: new Dictionary<DirectedHexEdge, HexEdgeRules>
        {
            [new(origin, east)] = new(metric.CompressedEdgeUnits),
            [new(east, eastTwo)] = new(metric.CompressedEdgeUnits),
        });
        AssertEqual(2, compressed.QueryMetricDistance(origin, eastTwo, SpatialQueryKind.Attack).DistanceUnits,
            "compressed edges shorten the minimum weighted attack path");
        AssertEqual(2, compressed.QueryMetricDistance(origin, eastTwo, SpatialQueryKind.Area).DistanceUnits,
            "area queries reuse the weighted distance service");
        AssertEqual(2, compressed.QueryMetricDistance(origin, eastTwo, SpatialQueryKind.Sight).DistanceUnits,
            "sight queries reuse the weighted distance service");
        AssertEqual(true, compressed.FindReachable(origin, movementBudget: 1).ContainsKey(eastTwo),
            "movement reuses weighted edge distance");
        var compressedAttack = Combat.ResolveActionPosition(
            compressed, origin, eastTwo, movementBudget: 0, minRange: 1, maxRange: 1,
            preventsVoluntaryMovement: false);
        AssertEqual(true, compressedAttack.CanAttack,
            "combat range checks reuse weighted edge distance");

        var expanded = new HexBattlefield(edgeRules: new Dictionary<DirectedHexEdge, HexEdgeRules>
        {
            [new(origin, east)] = new(metric.ExpandedEdgeUnits),
        });
        AssertEqual(4, expanded.QueryMetricDistance(origin, east, SpatialQueryKind.Attack).DistanceUnits,
            "expanded edge increases the minimum weighted path");

        var forced = compressed.ResolveForcedMovementDetailed(
            origin, HexDirection.East, distanceBudget: 1);
        AssertEqual(eastTwo, forced.Position,
            "forced movement crosses multiple compressed edges within one range unit");
        AssertEqual(2, forced.ConsumedDistanceUnits,
            "forced movement reports exact weighted distance consumption");
        AssertEqual("directed_edge_not_configured", forced.StopReason,
            "forced movement fails closed when the next directed edge is not configured");

        var obstacle = new HexBattlefield(new Dictionary<HexCoord, HexCellRules>
        {
            [east] = new(IsEntityObstacle: true),
        });
        var obstacleEdge = obstacle.InspectEdge(origin, east, SpatialQueryKind.Movement);
        AssertEqual(false, obstacleEdge.IsLegal, "entity obstacle rejects movement");
        AssertEqual("entity_obstacle", obstacleEdge.Reason, "entity obstacle reason is observable");
        var blockedSight = obstacle.QueryLineOfSight(origin, eastTwo);
        AssertEqual(false, blockedSight.HasLineOfSight, "entity obstacle rejects ordinary sight");
        AssertEqual("entity_obstacle", blockedSight.Reason, "sight obstruction reason is observable");

        var coverTarget = eastTwo;
        var cover = new HexBattlefield(
            cellCover: new Dictionary<DirectionalCellCover, CoverTier>
            {
                [new(coverTarget, HexDirection.West)] = CoverTier.Light,
            },
            edgeCover: new Dictionary<DirectedHexEdge, CoverTier>
            {
                [new(east, coverTarget)] = CoverTier.Heavy,
            });
        var coveredFromWest = cover.QueryCover(origin, coverTarget);
        AssertEqual(CoverTier.Heavy, coveredFromWest.Tier,
            "multiple directional covers keep only the highest configured tier");
        AssertEqual(true, coveredFromWest.Reason.Contains("edge:Heavy", StringComparison.Ordinal),
            "cover result reports its winning source");
        AssertEqual(CoverTier.None, cover.QueryCover(eastThree.Step(HexDirection.East), coverTarget).Tier,
            "directional cover does not protect the opposite side");

        var surfaceBoard = new HexBattlefield();
        surfaceBoard.SetSurface(origin, new SurfaceState("fixture-wet", 2, "test", "neutral", "surface-a"));
        surfaceBoard.SetSurface(origin, new SurfaceState("fixture-burning", 1, "test", "neutral", "surface-b"));
        AssertEqual("fixture-burning", surfaceBoard.GetSurface(origin)?.SurfaceType,
            "explicit final surface replaces the previous single slot");
        AssertEqual(1, surfaceBoard.SurfaceCountAt(origin), "a cell exposes only one final surface");

        var phenomena = new HexBattlefield();
        foreach (var channel in Enum.GetValues<PhenomenonChannel>())
        {
            var applied = phenomena.ApplyPhenomenon(
                origin,
                new PhenomenonState($"fixture-{channel}", channel, StrengthTier: 1, DurationCycles: 2));
            AssertEqual(true, applied.Applied, $"{channel} channel accepts its first result");
        }
        var sixChannels = phenomena.GetPhenomena(origin, heightLevel: 0);
        AssertEqual(6, sixChannels.Count, "all six phenomenon channels expose one final result each");
        AssertEqual(6, sixChannels.Keys.Distinct().Count(), "phenomenon channel results are unique");

        var mergedAirflow = phenomena.ApplyPhenomenon(
            origin,
            new PhenomenonState("fixture-Airflow", PhenomenonChannel.Airflow, StrengthTier: 2, DurationCycles: 3));
        AssertEqual(true, mergedAirflow.Applied, "same phenomenon type merges deterministically");
        AssertEqual(3, mergedAirflow.FinalState?.StrengthTier,
            "same phenomenon type combines strength within the configured tier cap");
        AssertEqual(3, mergedAirflow.FinalState?.DurationCycles,
            "same phenomenon type refreshes to the longer duration");

        var beforeMissingPair = phenomena.GetPhenomena(origin, heightLevel: 0)[PhenomenonChannel.Visibility];
        var missingPair = phenomena.ApplyPhenomenon(
            origin,
            new PhenomenonState("fixture-unpaired-visibility", PhenomenonChannel.Visibility, StrengthTier: 1, DurationCycles: 1));
        AssertEqual(false, missingPair.Applied, "missing same-channel pair fails closed");
        AssertEqual("missing_pair", missingPair.Reason, "missing pair failure is observable");
        AssertEqual(beforeMissingPair, phenomena.GetPhenomena(origin, heightLevel: 0)[PhenomenonChannel.Visibility],
            "missing pair preserves the prior final phenomenon");

        var pairBoard = new HexBattlefield();
        pairBoard.ApplyPhenomenon(origin, new PhenomenonState(
            "fixture-visibility-a", PhenomenonChannel.Visibility, StrengthTier: 1, DurationCycles: 1));
        var paired = pairBoard.ApplyPhenomenon(origin, new PhenomenonState(
            "fixture-visibility-b", PhenomenonChannel.Visibility, StrengthTier: 1, DurationCycles: 1));
        AssertEqual(true, paired.Applied, "registered same-channel fixture pair resolves");
        AssertEqual("fixture-visibility-result", paired.FinalState?.PhenomenonType,
            "registered pair exposes its unique final result");

        AssertSequence(
            new[]
            {
                EnvironmentCyclePhase.AirflowMovement,
                EnvironmentCyclePhase.TemperatureChangesPrecipitation,
                EnvironmentCyclePhase.PrecipitationWashAndSurface,
                EnvironmentCyclePhase.VisibilityAndSuspendedHazard,
                EnvironmentCyclePhase.CloudDischarge,
                EnvironmentCyclePhase.DurationCleanup,
            },
            phenomena.AdvanceEnvironmentCycle(),
            "environment cycle order is fixed and observable");
    }

    static Character StageCharacter(string name, string realm, int subIndex)
    {
        var character = Character.Create(name, new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8 }, "physical");
        character.Realm = realm;
        character.SubIndex = subIndex;
        return character;
    }

    static Character CtTestCharacter(string name, int reaction)
    {
        var character = Character.Create(name, new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8 }, "physical");
        character.Realm = "练气";
        character.Primary["HP"] = 1;
        character.Primary["MP"] = 0;
        character.Primary["肉攻"] = 1000;
        character.Primary["神攻"] = 0;
        character.Primary["肉防"] = 0;
        character.Primary["神防"] = 0;
        character.Primary["反应"] = reaction;
        character.Primary["移力"] = 6;
        character.SetActionEconomyInputs(100000, 12000);
        character.BasicAttackProfile = LegacyBasicFixture;
        return character;
    }

    static Character DuelTimeoutCharacter(string name, bool ranged)
    {
        var character = Character.Create(name, new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8 }, "physical");
        character.Realm = "筑基";
        character.Primary["HP"] = 10000;
        character.Primary["MP"] = 0;
        character.Primary["肉攻"] = 1;
        character.Primary["神攻"] = 0;
        character.Primary["肉防"] = 0;
        character.Primary["神防"] = 0;
        character.Primary["反应"] = 10;
        character.Primary["移力"] = 0;
        character.SetActionEconomyInputs(100000, 0);
        character.BasicAttackProfile = ranged
            ? new GameData.AttackProfile("timeout-ranged", "物理", 1.0, "", 1, 6, Cost: new("timeout-ranged", "fixture", 50000, 50000, 0, 0))
            : LegacyBasicFixture;
        return character;
    }

    static Combat.GroupTargetCandidate GroupPriorityCandidate(
        int primaryTargetIndex,
        int hp,
        int inputOrder,
        IReadOnlyList<int> legalHitTargetIndexes,
        Combat.GroupActionSettlementEvidence settlementEvidence) => new(
            primaryTargetIndex,
            Team: 1,
            hp,
            IsAlive: true,
            IsLegal: true,
            new Combat.GroupActionSettlementCandidate(
                Turn: 1,
                ActorIndex: 0,
                PrimaryTargetIndex: primaryTargetIndex,
                LegalHitTargetIndexes: legalHitTargetIndexes,
                InputOrder: inputOrder,
                SettlementEvidence: settlementEvidence));

    static Character GroupTestCharacter(string name, int reaction, int hp, int attack, int movement)
    {
        var character = CtTestCharacter(name, reaction);
        character.Primary["HP"] = hp;
        character.Primary["肉攻"] = attack;
        character.Primary["肉防"] = 1000;
        character.Primary["移力"] = movement;
        character.SetActionEconomyInputs(100000, checked(movement * 1000));
        return character;
    }

    static void RunGoldenCoreSeatNSeat01B()
    {
        const string positionId = "seat-earth-source";
        var profile = new SeatProofProfileDefinition(
            "proof-earth-source",
            GoldenCoreSeatType.Source,
            regularProgressTarget: 2,
            criticalProgressTarget: 2,
            new[] { "dao-proof-earth" });
        var ledgerA = new SeatProofLedger("actor-a", new[] { "dao-proof-earth" });

        // 常规唯一完成必须在关闭世界 tick 后才就绪，且未关闭时没有候选绑定资格。
        var uniqueCoordinator = new SeatCompetitionCoordinator();
        var unique = CreateAttempt("attempt-unique", positionId, "actor-a", "carrier-a", expectedVersion: 3, profile);
        uniqueCoordinator.Register(unique);
        unique.AdvanceRegular(2, profile, ledgerA);
        AssertEqual(SeatCompetitionAttemptStatus.AwaitingRegularTickClose, unique.Status, "regular completion awaits tick close");
        uniqueCoordinator.SubmitRegularCompletion(unique.AttemptId, 10);
        AssertEqual(SeatCompetitionAttemptStatus.AwaitingRegularTickClose, unique.Status, "submitted regular completion stays unbound");
        var uniqueResolution = uniqueCoordinator.CloseRegularTick(positionId, 10);
        AssertEqual(SeatCompetitionResolutionKind.UniqueReady, uniqueResolution.Kind, "unique regular close resolution");
        AssertEqual("attempt-unique", uniqueResolution.WinningAttemptId, "unique regular winner");
        AssertEqual(SeatCompetitionAttemptStatus.ReadyToBind, unique.Status, "unique regular close makes ready");
        AssertEqual(SeatCompetitionResolutionKind.NoCompletion,
            uniqueCoordinator.CloseRegularTick(positionId, 10).Kind,
            "closed regular tick is idempotent");
        AssertThrows<InvalidOperationException>(
            () => uniqueCoordinator.SubmitRegularCompletion(unique.AttemptId, 10),
            "late regular submission is rejected");

        // 同 tick 常规完成只进入临界轮；展示排序不参与胜者判断。
        var criticalCoordinator = new SeatCompetitionCoordinator();
        var first = CreateAttempt("attempt-z", positionId, "actor-z", "carrier-z", 0, profile);
        var second = CreateAttempt("attempt-a", positionId, "actor-a", "carrier-a", 0, profile);
        var ledgerZ = new SeatProofLedger("actor-z", new[] { "dao-proof-earth" });
        criticalCoordinator.Register(first);
        criticalCoordinator.Register(second);
        first.AdvanceRegular(2, profile, ledgerZ);
        second.AdvanceRegular(2, profile, ledgerA);
        criticalCoordinator.SubmitRegularCompletion(first.AttemptId, 20);
        criticalCoordinator.SubmitRegularCompletion(second.AttemptId, 20);
        var criticalStart = criticalCoordinator.CloseRegularTick(positionId, 20);
        AssertEqual(SeatCompetitionResolutionKind.CriticalContestContinues, criticalStart.Kind, "same-tick regular close continues");
        AssertSequence(new[] { "attempt-a", "attempt-z" }, criticalStart.AttemptIds, "critical participants use display-only ordinal order");
        AssertEqual(1, first.CriticalRound, "first critical round starts at one");
        AssertEqual(1, second.CriticalRound, "second critical round starts at one");

        // 再次同刻完成重开稳定轮；下一轮唯一完成者才成为可绑定对象。
        first.AdvanceCritical(2, profile, ledgerZ);
        second.AdvanceCritical(2, profile, ledgerA);
        criticalCoordinator.SubmitCriticalCompletion(first.AttemptId, 21);
        criticalCoordinator.SubmitCriticalCompletion(second.AttemptId, 21);
        var criticalRestart = criticalCoordinator.CloseCriticalTick(positionId, 21);
        AssertEqual(SeatCompetitionResolutionKind.CriticalContestContinues, criticalRestart.Kind, "same-tick critical close restarts");
        AssertEqual(2, first.CriticalRound, "first critical round increments");
        AssertEqual(2, second.CriticalRound, "second critical round increments");
        first.AdvanceCritical(2, profile, ledgerZ);
        criticalCoordinator.SubmitCriticalCompletion(first.AttemptId, 22);
        var criticalWinner = criticalCoordinator.CloseCriticalTick(positionId, 22);
        AssertEqual(SeatCompetitionResolutionKind.UniqueReady, criticalWinner.Kind, "later unique critical completion wins");
        AssertEqual("attempt-z", criticalWinner.WinningAttemptId, "unique critical winner is input-driven");
        AssertEqual(SeatCompetitionAttemptStatus.CriticalContest, second.Status, "other contender continues until atomic bind");
        AssertThrows<InvalidOperationException>(
            () => criticalCoordinator.SubmitCriticalCompletion(first.AttemptId, 23),
            "one attempt cannot join another open tick after ready");

        // 版本、空位与前置条件拒绝都不得改变核心、占据或尝试状态。
        AssertRejectedWithoutMutation(
            SeatCompetitionBindFailureReason.StalePositionVersion,
            CreateReadyContext(profile, ledgerA, positionId, version: 0, advanceVersion: true),
            "position version change rejects binding");
        AssertRejectedWithoutMutation(
            SeatCompetitionBindFailureReason.PositionUnavailable,
            CreateReadyContext(profile, ledgerA, positionId, version: 0, holderActorId: "existing-holder"),
            "occupied position rejects binding");
        AssertRejectedWithoutMutation(
            SeatCompetitionBindFailureReason.PreconditionsNotMet,
            CreateReadyContext(profile, ledgerA, positionId, version: 0, siteStillValid: false),
            "invalid site rejects binding");

        var notReadyRegistry = new SeatCompetitionPositionRegistry();
        notReadyRegistry.Add(new SeatCompetitionPositionRecord(positionId, profile.ProfileId, profile.SeatType));
        var notReadyCoordinator = new SeatCompetitionCoordinator();
        var notReady = CreateAttempt("attempt-not-ready", positionId, "actor-a", "carrier-a", 0, profile);
        notReadyCoordinator.Register(notReady);
        var notReadyCore = new SeatCompetitionCoreState("actor-a");
        var notReadyResult = notReadyRegistry.TryBind(
            ValidRequest(notReady.AttemptId, "core-not-ready"), profile, ledgerA, notReadyCore, notReadyCoordinator);
        AssertEqual(SeatCompetitionBindFailureReason.AttemptNotReady, notReadyResult.FailureReason, "unready attempt rejection");
        AssertEqual(0, notReadyCore.Bindings.Count, "unready attempt leaves core empty");

        var invariantContext = CreateReadyContext(profile, ledgerA, positionId, version: 0);
        var duplicateCarrierCore = SeatCompetitionCoreState.RestoreState(new SeatCompetitionCoreSnapshot(
            "actor-a",
            "core-existing",
            new[] { new SeatCompetitionCoreSeatBinding("other-source", GoldenCoreSeatType.Source, "carrier-a") }));
        var invariantResult = invariantContext.Registry.TryBind(
            ValidRequest(invariantContext.Attempt.AttemptId, ""), profile, ledgerA, duplicateCarrierCore, invariantContext.Coordinator);
        AssertEqual(SeatCompetitionBindFailureReason.CoreInvariantViolation, invariantResult.FailureReason, "duplicate carrier rejects atomically");
        AssertEqual(1, duplicateCarrierCore.Bindings.Count, "core invariant failure does not append binding");
        AssertEqual(SeatCompetitionAttemptStatus.ReadyToBind, invariantContext.Attempt.Status, "core invariant failure keeps attempt ready");

        // 第一位建唯一核心，第二位复用核心；成功绑定失效同位其他候选且没有双重占据。
        var bindRegistry = new SeatCompetitionPositionRegistry();
        bindRegistry.Add(new SeatCompetitionPositionRecord(positionId, profile.ProfileId, profile.SeatType));
        var bindCoordinator = new SeatCompetitionCoordinator();
        var winner = CreateAttempt("attempt-bind", positionId, "actor-a", "carrier-a", 0, profile);
        var loser = CreateAttempt("attempt-loser", positionId, "actor-b", "carrier-b", 0, profile);
        var ledgerB = new SeatProofLedger("actor-b", new[] { "dao-proof-earth" });
        bindCoordinator.Register(winner);
        bindCoordinator.Register(loser);
        winner.AdvanceRegular(2, profile, ledgerA);
        bindCoordinator.SubmitRegularCompletion(winner.AttemptId, 30);
        bindCoordinator.CloseRegularTick(positionId, 30);
        var core = new SeatCompetitionCoreState("actor-a");
        var firstBind = bindRegistry.TryBind(
            ValidRequest(winner.AttemptId, "core-a"), profile, ledgerA, core, bindCoordinator);
        AssertEqual(true, firstBind.Succeeded, "first position binds");
        AssertEqual("core-a", core.CoreBindingId, "first position creates unique core");
        AssertEqual("actor-a", bindRegistry.Get(positionId).HolderActorId, "position holder set atomically");
        AssertEqual(SeatCompetitionAttemptStatus.Bound, winner.Status, "winner becomes bound");
        AssertEqual(SeatCompetitionAttemptStatus.Invalidated, loser.Status, "other same-position candidate invalidated");

        var secondProfile = new SeatProofProfileDefinition("proof-fire-transform", GoldenCoreSeatType.Transformation, 1, 1, new[] { "dao-proof-fire" });
        var secondLedger = new SeatProofLedger("actor-a", new[] { "dao-proof-fire" });
        const string secondPositionId = "seat-fire-transform";
        bindRegistry.Add(new SeatCompetitionPositionRecord(secondPositionId, secondProfile.ProfileId, secondProfile.SeatType));
        var secondAttempt = CreateAttempt("attempt-second", secondPositionId, "actor-a", "carrier-c", 0, secondProfile);
        bindCoordinator.Register(secondAttempt);
        secondAttempt.AdvanceRegular(1, secondProfile, secondLedger);
        bindCoordinator.SubmitRegularCompletion(secondAttempt.AttemptId, 31);
        bindCoordinator.CloseRegularTick(secondPositionId, 31);
        var secondBind = bindRegistry.TryBind(
            ValidRequest(secondAttempt.AttemptId, ""), secondProfile, secondLedger, core, bindCoordinator);
        AssertEqual(true, secondBind.Succeeded, "second position reuses core");
        AssertEqual("core-a", core.CoreBindingId, "second position cannot replace core");
        AssertEqual(2, core.Bindings.Count, "two distinct stable positions share one core");

        // 保存/恢复保持关闭 tick、临界完成集、版本、占据和核心；恢复后不得重复关闭或绑定。
        var openCoordinator = new SeatCompetitionCoordinator();
        var savedAttempt = CreateAttempt("attempt-save", positionId, "actor-a", "carrier-save", 4, profile);
        var savedRival = CreateAttempt("attempt-save-rival", positionId, "actor-z", "carrier-save-rival", 4, profile);
        var savedRivalLedger = new SeatProofLedger("actor-z", new[] { "dao-proof-earth" });
        openCoordinator.Register(savedAttempt);
        openCoordinator.Register(savedRival);
        savedAttempt.AdvanceRegular(2, profile, ledgerA);
        savedRival.AdvanceRegular(2, profile, savedRivalLedger);
        openCoordinator.SubmitRegularCompletion(savedAttempt.AttemptId, 40);
        openCoordinator.SubmitRegularCompletion(savedRival.AttemptId, 40);
        openCoordinator.CloseRegularTick(positionId, 40);
        savedAttempt.AdvanceCritical(2, profile, ledgerA);
        openCoordinator.SubmitCriticalCompletion(savedAttempt.AttemptId, 41);
        var restoredCoordinator = SeatCompetitionCoordinator.RestoreState(openCoordinator.CaptureState());
        AssertEqual(SeatCompetitionResolutionKind.UniqueReady,
            restoredCoordinator.CloseCriticalTick(positionId, 41).Kind,
            "restored open critical completion closes once");
        AssertEqual(SeatCompetitionResolutionKind.NoCompletion,
            restoredCoordinator.CloseCriticalTick(positionId, 41).Kind,
            "restored closed critical tick stays idempotent");
        var restoredRegistry = SeatCompetitionPositionRegistry.RestoreState(bindRegistry.CaptureState());
        var restoredCore = SeatCompetitionCoreState.RestoreState(core.CaptureState());
        AssertEqual("actor-a", restoredRegistry.Get(positionId).HolderActorId, "restored occupied position holder");
        AssertEqual(1L, restoredRegistry.Get(positionId).Version, "restored position version");
        AssertEqual(2, restoredCore.Bindings.Count, "restored core carrier bindings");
        AssertEqual(SeatCompetitionBindFailureReason.AttemptNotReady,
            restoredRegistry.TryBind(ValidRequest(winner.AttemptId, ""), profile, ledgerA, restoredCore, bindCoordinator).FailureReason,
            "bound attempt cannot bind again");
    }

    static SeatCompetitionAttempt CreateAttempt(
        string attemptId,
        string positionId,
        string actorId,
        string carrierId,
        long expectedVersion,
        SeatProofProfileDefinition profile) => new(
            attemptId,
            positionId,
            actorId,
            profile.ProfileId,
            "proof-site",
            carrierId,
            expectedVersion,
            profile.RegularProgressTarget,
            profile.CriticalProgressTarget);

    static SeatCompetitionBindRequest ValidRequest(
        string attemptId,
        string newCoreBindingId,
        bool siteStillValid = true) => new(
            attemptId,
            newCoreBindingId,
            siteStillValid,
            realityAnchorStillValid: true,
            carrierStillCompatible: true,
            keyEventsResolved: true);

    static (SeatCompetitionPositionRegistry Registry, SeatCompetitionCoordinator Coordinator, SeatCompetitionAttempt Attempt, SeatCompetitionCoreState Core) CreateReadyContext(
        SeatProofProfileDefinition profile,
        SeatProofLedger ledger,
        string positionId,
        long version,
        bool advanceVersion = false,
        string holderActorId = "",
        bool siteStillValid = true)
    {
        var registry = new SeatCompetitionPositionRegistry();
        var position = new SeatCompetitionPositionRecord(positionId, profile.ProfileId, profile.SeatType, version, holderActorId);
        if (advanceVersion)
            position.AdvanceVersionForWorldChange();
        registry.Add(position);
        var coordinator = new SeatCompetitionCoordinator();
        var attempt = CreateAttempt("attempt-ready", positionId, ledger.ActorId, "carrier-ready", version, profile);
        coordinator.Register(attempt);
        attempt.AdvanceRegular(profile.RegularProgressTarget, profile, ledger);
        coordinator.SubmitRegularCompletion(attempt.AttemptId, 50);
        coordinator.CloseRegularTick(positionId, 50);
        return (registry, coordinator, attempt, new SeatCompetitionCoreState(ledger.ActorId));
    }

    static void AssertRejectedWithoutMutation(
        SeatCompetitionBindFailureReason expectedReason,
        (SeatCompetitionPositionRegistry Registry, SeatCompetitionCoordinator Coordinator, SeatCompetitionAttempt Attempt, SeatCompetitionCoreState Core) context,
        string label)
    {
        var profile = new SeatProofProfileDefinition("proof-earth-source", GoldenCoreSeatType.Source, 2, 2, new[] { "dao-proof-earth" });
        var ledger = new SeatProofLedger("actor-a", new[] { "dao-proof-earth" });
        var position = context.Registry.Get(context.Attempt.PositionId);
        var versionBefore = position.Version;
        var holderBefore = position.HolderActorId;
        var result = context.Registry.TryBind(
            ValidRequest(context.Attempt.AttemptId, "core-rejected", siteStillValid: expectedReason != SeatCompetitionBindFailureReason.PreconditionsNotMet),
            profile,
            ledger,
            context.Core,
            context.Coordinator);
        AssertEqual(expectedReason, result.FailureReason, label);
        AssertEqual(0, context.Core.Bindings.Count, label + " keeps core unchanged");
        AssertEqual(versionBefore, position.Version, label + " keeps version unchanged");
        AssertEqual(holderBefore, position.HolderActorId, label + " keeps holder unchanged");
        AssertEqual(SeatCompetitionAttemptStatus.ReadyToBind, context.Attempt.Status, label + " keeps attempt ready");
    }

    static void AssertProfile(object profile, string formedState, string danJiType, string occupancyState, string danName, string danNature, string legacyGrade)
    {
        AssertEqual(formedState, ReadProperty(profile, "FormedState"), "golden core formed state");
        AssertEqual(danJiType, ReadProperty(profile, "DanJiType"), "danji type");
        AssertEqual(occupancyState, ReadProperty(profile, "OccupancyState"), "occupancy state");
        AssertEqual(danName, ReadProperty(profile, "DanName"), "dan name");
        AssertEqual(danNature, ReadProperty(profile, "DanNature"), "dan nature");
        AssertEqual(legacyGrade, ReadProperty(profile, "LegacyGrade"), "legacy grade");
    }

    static object ReadProperty(object instance, string propertyName)
    {
        var prop = instance.GetType().GetProperty(propertyName);
        if (prop == null)
            throw new InvalidOperationException($"{instance.GetType().Name}.{propertyName} is missing.");
        return prop.GetValue(instance) ?? "";
    }

    static void WriteProperty(object instance, string propertyName, object value)
    {
        var prop = instance.GetType().GetProperty(propertyName);
        if (prop == null)
            throw new InvalidOperationException($"{instance.GetType().Name}.{propertyName} is missing.");
        prop.SetValue(instance, value);
    }

    static void AssertEqual<T>(T expected, object actual, string label)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual ?? "<null>"}.");
    }

    static void AssertSequence<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string label)
    {
        if (expected.Count != actual.Count || expected.Where((t, i) => !Equals(t, actual[i])).Any())
            throw new InvalidOperationException($"{label}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    static void AssertClose(double expected, double actual, double tolerance, string label)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{label}: expected {expected:F4}, got {actual:F4}.");
    }

    static void AssertThrows<TException>(Action action, string label) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}.");
    }
}
