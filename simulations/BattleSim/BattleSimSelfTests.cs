using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BattleSim;

static class BattleSimSelfTests
{
    public static int Run(string suite)
    {
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

        if (suite == "group-action-priority-n-group-02")
            return RunChecked(suite, RunGroupActionPriorityNGroup02);

        if (suite == "duel-bounds")
            return RunChecked(suite, RunDuelBounds);

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
            Console.Error.WriteLine($"SELFTEST {suite} FAIL: {ex.Message}");
            return 1;
        }
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
        AssertProfile(profile, "成丹", "自然丹籍", "稳定占据", "坤岳丹", "土", "一品");

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
        if (naturalScore is not int naturalScoreInt || naturalScoreInt <= 0)
            throw new InvalidOperationException($"natural seat competition score should be positive, got {naturalScore}.");

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
        WriteProperty(character, "SeatCompetitionScore", naturalScoreInt);
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
        var qte = Combat.ResolveGoldenCoreConflict(
            first.Character,
            first.RuntimeLedger,
            CreateConflictCandidateInput("left", "conflict-cost-first"),
            second.Character,
            second.RuntimeLedger,
            CreateConflictCandidateInput("right", "conflict-cost-second"),
            GoldenCoreConflictInputMode.Qte);

        var skippedFirst = CreateConflictParticipant("skip-first", reserve: 6);
        var skippedSecond = CreateConflictParticipant("skip-second", reserve: 4);
        var skipped = Combat.ResolveGoldenCoreConflict(
            skippedFirst.Character,
            skippedFirst.RuntimeLedger,
            CreateConflictCandidateInput("left", "conflict-cost-skip-first"),
            skippedSecond.Character,
            skippedSecond.RuntimeLedger,
            CreateConflictCandidateInput("right", "conflict-cost-skip-second"),
            GoldenCoreConflictInputMode.Skip);

        var expected = new GoldenCoreConflictResolution(
            GoldenCoreConflictOutcome.LeftWins,
            "PULSE_ADVANTAGE",
            "left",
            3,
            2,
            6,
            4,
            0);
        if (!Equals(expected, qte) || !Equals(expected, skipped))
            throw new InvalidOperationException("QTE and skip must resolve the same selected candidates through one deterministic pulse result.");
        AssertConflictRuntime(first.RuntimeLedger, 0, 3, "QTE left");
        AssertConflictRuntime(second.RuntimeLedger, 0, 3, "QTE right");
        AssertConflictRuntime(skippedFirst.RuntimeLedger, 0, 3, "skip left");
        AssertConflictRuntime(skippedSecond.RuntimeLedger, 0, 3, "skip right");

        var sorting = CreateThreeSeatConflictParticipant("sorting", sourceReserve: 7, transformationReserve: 5);
        var sortingResult = Combat.ResolveGoldenCoreConflict(
            sorting.Character,
            sorting.RuntimeLedger,
            CreateConflictCandidateInput("source", "conflict-cost-sorting-guardian_ming"),
            sorting.Character,
            sorting.RuntimeLedger,
            CreateConflictCandidateInput(
                "transformation",
                "conflict-cost-sorting-guardian_hun",
                abilityInstanceId: "guardian_hun",
                positionType: GoldenCoreSeatType.Transformation,
                compatibilityProfileId: "compat_transformation"),
            GoldenCoreConflictInputMode.Skip);
        if (sortingResult.Outcome != GoldenCoreConflictOutcome.LeftWins || sortingResult.Reason != "POSITION_TIER" ||
            sorting.RuntimeLedger.Get("guardian_ming").ConflictReserve != 7 || sorting.RuntimeLedger.Get("guardian_hun").ConflictReserve != 5)
        {
            throw new InvalidOperationException("fixed seat ranking must decide before pulse spending and retain independent ledgers.");
        }
    }

    static void RunGoldenCoreChallengeDeathNjdrule01C()
    {
        var grant = new CrossTierChallengeGrant(
            "grant-n-jd-rule-01c",
            DefinitionVersion: 7,
            TargetVariableId: "fixture-higher-rule-variable",
            ChallengerId: "challenger-n-jd-rule-01c",
            QualificationSource: CrossTierChallengeSourceKind.YuanyingOrthodoxy,
            AllowedOperationId: "fixture-direct-conflict",
            TargetId: "fixture-higher-rule-target",
            ScopeId: "fixture-scope",
            BeneficiaryId: "challenger-n-jd-rule-01c",
            RealityAnchorId: "fixture-anchor",
            ResourceLedgerRef: "fixture-resource-ledger",
            CapacityLedgerRef: "fixture-capacity-ledger",
            ChallengeRuleTier: 2,
            EffectiveAtTick: 10,
            ExpiresAtTick: 20,
            IsRevoked: false,
            RevocationReason: "",
            DisplaySource: "fixture-yuanying-orthodoxy");
        var archive = new CrossTierChallengeArchive(new[] { grant });
        var validRequest = new CrossTierChallengeRequest(
            "challenge-event-n-jd-rule-01c",
            grant.GrantId,
            grant.DefinitionVersion,
            grant.TargetVariableId,
            grant.ChallengerId,
            WorldTick: 12);
        var authorized = Combat.ResolveCrossTierChallenge(archive, validRequest);
        var repeatedAuthorization = Combat.ResolveCrossTierChallenge(archive, validRequest);
        if (!authorized.IsEligible || authorized.Reason != "JD_CHALLENGE_AUTHORIZED" || !Equals(authorized, repeatedAuthorization))
            throw new InvalidOperationException("a valid versioned challenge must authorize deterministically without mutating repeated events.");

        AssertChallengeRejection(
            Combat.ResolveCrossTierChallenge(archive, validRequest with { GrantId = "unknown-grant" }),
            "JD_CHALLENGE_GRANT_UNKNOWN",
            "unknown challenge grant");
        AssertChallengeRejection(
            Combat.ResolveCrossTierChallenge(archive, validRequest with { ExpectedDefinitionVersion = grant.DefinitionVersion - 1 }),
            "JD_CHALLENGE_VERSION_MISMATCH",
            "version mismatch challenge grant");
        AssertChallengeRejection(
            Combat.ResolveCrossTierChallenge(
                new CrossTierChallengeArchive(new[] { grant with { ExpiresAtTick = 11 } }),
                validRequest),
            "JD_CHALLENGE_EXPIRED",
            "expired challenge grant");
        AssertChallengeRejection(
            Combat.ResolveCrossTierChallenge(
                new CrossTierChallengeArchive(new[] { grant with { IsRevoked = true, RevocationReason = "fixture-revoked" } }),
                validRequest),
            "JD_CHALLENGE_REVOKED",
            "revoked challenge grant");
        AssertChallengeRejection(
            Combat.ResolveCrossTierChallenge(archive, validRequest with { TargetVariableId = "wrong-variable" }),
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
            return headers.Select((header, index) => (header, value: values[index]))
                .ToDictionary(pair => pair.header, pair => pair.value, StringComparer.Ordinal);
        }).ToArray();
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
        var unarmed = GameData.UnarmedBasicAttack;
        AssertEqual(1, unarmed.MinRange, "unarmed basic attack minimum range");
        AssertEqual(1, unarmed.MaxRange, "unarmed basic attack maximum range");
        AssertEqual(2, GameData.TaiyiFuxiuArt.MinRange, "symbol art keeps an independent minimum range");
        AssertEqual(4, GameData.TaiyiFuxiuArt.MaxRange, "symbol art keeps an independent maximum range");
        AssertEqual(1, GameData.WaterDivine.MinRange, "water divine keeps an independent minimum range");
        AssertEqual(3, GameData.WaterDivine.MaxRange, "water divine keeps an independent maximum range");
        var character = Character.Create("basic-profile", new() { ["根骨"] = 1, ["魂魄"] = 1, ["神识"] = 1, ["资质"] = 1, ["气运"] = 1 }, "physical");
        AssertEqual(GameData.UnarmedBasicAttack, character.BasicAttackProfile, "unequipped character uses unarmed basic attack profile");
        character.BasicAttackProfile = new("测试主战法宝", "神魂", 1.1, "火", 2, 3);
        AssertEqual(3, character.BasicAttackProfile.MaxRange, "main combat artifact profile replaces unarmed fallback");
        var battlefield = new HexBattlefield();
        AssertEqual(6, Combat.InitialPositionA.DistanceTo(Combat.InitialPositionB), "distance-model opening separation");
        AssertEqual(new HexCoord(5, 0), battlefield.FindAttackPosition(new HexCoord(0, 0), new HexCoord(6, 0), 5, 1, 1), "melee moves before attacking");
        AssertEqual(new HexCoord(4, 0), battlefield.FindAttackPosition(new HexCoord(5, 0), new HexCoord(6, 0), 3, 2, 4), "minimum range forces retreat");
        AssertEqual(new HexCoord(0, 0), battlefield.FindAttackPosition(new HexCoord(0, 0), new HexCoord(6, 0), 0, 2, 4), "zero movement keeps position");
        int selected = Combat.SelectAction(battlefield, new HexCoord(0, 0), new HexCoord(4, 0), true, 1, 1, false, true, 2, 4, GameData.UnarmedBasicAttack,
            out int selectedMinRange, out int selectedMaxRange);
        AssertEqual(Combat.ArtAction, selected, "legal art takes priority over out-of-range divine");
        AssertEqual((2, 4), (selectedMinRange, selectedMaxRange), "selected art range is retained for movement");
        int longRangeAction = Combat.SelectAction(battlefield, new HexCoord(0, 0), new HexCoord(6, 0), false, 1, 1, false, true, 2, 6, GameData.UnarmedBasicAttack,
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
                Facing: HexDirection.East,
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
                areaConfig with { Shape = areaConfig.Shape with { Facing = (HexDirection)999 } },
                caster,
                casterTeam: 0,
                casterIndex: 0,
                targetCell,
                effectiveRangeModifier: 0,
                candidates),
            "area targeting rejects an unknown shape facing");

        var areaActor = GroupTestCharacter(
            "area-observation-a1",
            reaction: 100,
            hp: 10000,
            attack: 1,
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
            });
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
        AssertEqual(
            Combat.InitialGroupPositions[2],
            observedAreaAction.AreaCenter,
            "formal 2v2 records the resolved target-cell area center without ordinary sight");
        AssertSequence(
            new[] { 2, 3 },
            observedAreaAction.AreaHitTargetIndexes,
            "formal 2v2 records every legal enemy hit by the configured area");
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
        var observedPriority = observedRound.Actions[0].TargetSelectionPriority
            ?? throw new InvalidOperationException("2v2 selection priority is observable");
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
            orderedRound.Actions.Take(4).Select(action => action.ActorIndex).ToArray(),
            "equal CT resolves in fixed input order");
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
        character.BasicAttackProfile = ranged
            ? new GameData.AttackProfile("timeout-ranged", "物理", 1.0, "", 1, 6)
            : GameData.UnarmedBasicAttack;
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
        return character;
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
