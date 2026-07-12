using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim;

static class G2AttributionAudit
{
    internal sealed record Result(string LeftName, string RightName, string Layer, double BaselineWinRate, double CounterfactualWinRate, int Battles)
    {
        internal double DeltaPercentagePoints => CounterfactualWinRate - BaselineWinRate;
    }

    internal static IReadOnlyList<string> Layers => ["先天交换", "功法包交换"];

    internal static bool IsCoveredExtreme(int leftSamples, int rightSamples, int battles, double winRate) =>
        leftSamples >= 200
        && rightSamples >= 200
        && battles >= 2000
        && (Math.Abs(winRate) < 0.000001 || Math.Abs(winRate - 100.0) < 0.000001);

    internal static void Print(IReadOnlyList<Program.BuildDef> builds, IReadOnlyList<Character>[] goldPools, double[,] baseline, int battles, int cycles)
    {
        var results = new List<Result>();
        Console.WriteLine();
        Console.WriteLine("【G2 极端对局归因审计：距离模型基线】");
        for (int i = 0; i < builds.Count; i++)
        for (int j = i + 1; j < builds.Count; j++)
        {
            if (!IsCoveredExtreme(goldPools[i].Count, goldPools[j].Count, battles, baseline[i, j])) continue;
            Console.WriteLine($"【归因：{builds[i].Name} vs {builds[j].Name}】 基线={baseline[i, j]:F2}% 配对={Math.Min(goldPools[i].Count, goldPools[j].Count)} 场次={battles}");
            AddResult(results, builds[i], builds[j], "先天交换", baseline[i, j],
                BuildGoldPool(builds[i], i, builds[j].Innate, builds[i].GongFaName, cycles),
                BuildGoldPool(builds[j], j, builds[i].Innate, builds[j].GongFaName, cycles), battles);
            AddResult(results, builds[i], builds[j], "功法包交换", baseline[i, j],
                BuildGoldPool(builds[i], i, builds[i].Innate, builds[j].GongFaName, cycles),
                BuildGoldPool(builds[j], j, builds[j].Innate, builds[i].GongFaName, cycles), battles);
        }
        Console.WriteLine($"归因汇总：极端对局={results.Select(r => (r.LeftName, r.RightName)).Distinct().Count()}，反事实={results.Count}，平均绝对变化={results.Select(r => Math.Abs(r.DeltaPercentagePoints)).DefaultIfEmpty(0).Average():F2}pp，最大变化={results.OrderByDescending(r => Math.Abs(r.DeltaPercentagePoints)).Select(r => $"{r.LeftName} vs {r.RightName}/{r.Layer} {r.DeltaPercentagePoints:+0.00;-0.00;0.00}pp").FirstOrDefault() ?? "无"}");
    }

    static void AddResult(List<Result> results, Program.BuildDef leftBuild, Program.BuildDef rightBuild, string layer, double baseline, IReadOnlyList<Character> left, IReadOnlyList<Character> right, int battles)
    {
        double rate = WinRate(left, right, battles);
        var row = new Result(leftBuild.Name, rightBuild.Name, layer, baseline, rate, battles);
        results.Add(row);
        Console.WriteLine($"  {layer}：{rate:F2}%（{row.DeltaPercentagePoints:+0.00;-0.00;0.00}pp）");
    }

    static List<Character> BuildGoldPool(Program.BuildDef build, int buildIndex, IReadOnlyDictionary<string, int> innate, string gongFaName, int cycles)
    {
        var weights = GameData.WeightsFromGongFa(gongFaName);
        var pool = new List<Character>();
        for (int seed = 0; seed < 200; seed++)
        {
            var input = innate.ToDictionary(pair => pair.Key, pair => pair.Value);
            var result = Cultivation.Simulate(input, weights, seed * 100 + buildIndex, "中品", "上品", maxCycles: cycles);
            var character = Character.Create(build.Name, input, build.Style);
            character.ApplyGrowth(result.Realm, "上品", weights);
            character.GongFaName = gongFaName;
            character.DFQuality = result.DFQuality; character.DFMult = GameData.DFMultiplier[result.DFQuality]; character.DFScore = result.DFScore;
            ApplyGoldenCore(character, result);
            character.FinalizeStats(result.Realm, result.SubIdx, "中品", weights); character.AssignArts();
            if (character.Realm == "金丹") pool.Add(character);
        }
        return pool;
    }

    static void ApplyGoldenCore(Character character, Cultivation.Result result)
    {
        character.FormedState = result.FormedState; character.DanJiType = result.DanJiType; character.DanJiStabilityMult = result.DanJiStabilityMultiplier; character.DanJiArtAffinityMult = result.DanJiArtAffinityMultiplier;
    }

    static double WinRate(IReadOnlyList<Character> left, IReadOnlyList<Character> right, int battles)
    {
        int pairs = Math.Min(left.Count, right.Count); if (pairs == 0) return double.NaN;
        int rounds = Math.Max(1, battles / pairs / 2), wins = 0, total = 0;
        for (int index = 0; index < pairs; index++)
        {
            var (forward, _, _) = Combat.Simulate(left[index], right[index], rounds);
            var (_, reverse, _) = Combat.Simulate(right[index], left[index], rounds);
            wins += (int)Math.Round((forward + reverse) / 2.0 * rounds * 2 / 100.0); total += rounds * 2;
        }
        return wins * 100.0 / total;
    }
}
