using System;
using System.Collections.Generic;
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

        if (suite == "stage-matrix-b3")
            return RunChecked(suite, RunStageMatrixB3);

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

    static Character StageCharacter(string name, string realm, int subIndex)
    {
        var character = Character.Create(name, new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8 }, "physical");
        character.Realm = realm;
        character.SubIndex = subIndex;
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
}
