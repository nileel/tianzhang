using System;
using System.Reflection;

namespace BattleSim;

static class BattleSimSelfTests
{
    public static int Run(string suite)
    {
        if (suite != "element-v510")
        {
            Console.Error.WriteLine($"Unknown self-test suite: {suite}");
            return 2;
        }

        try
        {
            RunElementV510();
            Console.WriteLine("SELFTEST element-v510 PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SELFTEST element-v510 FAIL: {ex.Message}");
            return 1;
        }
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

    static void AssertEqual<T>(T expected, object actual, string label)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual ?? "<null>"}.");
    }

    static void AssertClose(double expected, double actual, double tolerance, string label)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{label}: expected {expected:F4}, got {actual:F4}.");
    }
}
