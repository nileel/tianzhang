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
