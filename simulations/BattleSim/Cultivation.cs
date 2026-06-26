using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim;

static class Cultivation
{
    public record Result(
        string Realm,
        int SubIdx,
        int TotalSubs,
        string DFQuality,
        int DFScore,
        string LegacyGCGrade = "",
        int GCScore = 0,
        string FormedState = "未成丹",
        string DanJiType = "",
        string OccupancyState = "未成丹",
        string DanName = "",
        string DanNature = "",
        string TargetBranch = "",
        string TargetSeat = "",
        string SeatName = "",
        string DanPivot = "",
        double DanJiStabilityMultiplier = 1.0,
        double DanJiArtAffinityMultiplier = 1.0);

    public static Result Simulate(
        Dictionary<string, int> baseInnate, Dictionary<string, double> weights, int seed,
        string spiritGrade = "中品", string techGrade = "上品", string treasureGrade = "", int maxCycles = -1)
    {
        var rng = new Random(seed);
        int cycles = maxCycles < 0 ? GameData.CultivationCycles : maxCycles;
        double cpp = 0;
        string realm = "凡人"; int subIdx = 0;
        string dfQuality = "无道基"; int dfScore = 0; int gcScore = 0;
        var goldenCore = new GameData.GoldenCoreProfile("未成丹", "", "未成丹", "", "", "", "", "", "", "", 1.0, 1.0);
        bool dfGenerated = false; bool gcGenerated = false;

        double 资质 = baseInnate["资质"], 气运 = baseInnate["气运"];
        double 修炼速度 = 1.0 + 资质 * 0.03;
        double 悟性 = 1.0 + 资质 * 0.015;
        double treasureBonus = treasureGrade switch { "下品" => 10, "中品" => 15, "上品" => 20, "极品" => 25, _ => 0 };
        double 突破率 = Clamp(GameData.BreakthroughBaseRate * 100 + (悟性 - 1.0) * 50 + 气运 * 0.05 + treasureBonus, 20, 95) / 100.0;
        int nextMs = 0;

        for (int cycle = 0; cycle < cycles; cycle++)
        {
            double gain = GameData.BaseGainPerCycle * 修炼速度 * (0.85 + rng.NextDouble() * 0.30);
            cpp += gain;
            bool progressed = true;
            while (progressed && nextMs < GameData.Milestones.Length)
            {
                var ms = GameData.Milestones[nextMs];
                if (cpp < ms.cpp) { progressed = false; break; }
                if (rng.NextDouble() < 突破率)
                {
                    string prevRealm = realm;
                    realm = ms.realm; subIdx = ms.subIdx; cpp -= ms.cpp;
                    nextMs = FindNext(realm, subIdx);

                    // v4.0: 突破到金丹初阶时凝结金丹
                    if (!gcGenerated && realm == "金丹" && subIdx == 0)
                    {
                        gcGenerated = true;
                        // totalCpp = 里程碑消耗 + 剩余溢出（反映总积累）
                        double totalCpp = ms.cpp + Math.Max(0, cpp);
                        (goldenCore, gcScore) = GenerateGoldenCore(
                            baseInnate, spiritGrade, techGrade, dfQuality, totalCpp, treasureGrade, rng, weights);
                    }

                    // v3.5: 突破到筑基初阶时生成道基
                    if (!dfGenerated && realm == "筑基" && subIdx == 0)
                    {
                        dfGenerated = true;
                        double overflowCpp = Math.Max(0, cpp); // 突破后剩余灵力视为超额
                        (dfQuality, dfScore) = GenerateDaoFoundation(
                            baseInnate, spiritGrade, techGrade, overflowCpp, treasureGrade, rng);
                    }
                }
                else { double penalty = ms.cpp * (0.10 + rng.NextDouble() * 0.10); cpp = Math.Max(0, cpp - penalty); progressed = false; }
            }
        }
        return new Result(
            realm,
            subIdx,
            GameData.TotalSubs(realm, subIdx),
            dfQuality,
            dfScore,
            goldenCore.LegacyGrade,
            gcScore,
            goldenCore.FormedState,
            goldenCore.DanJiType,
            goldenCore.OccupancyState,
            goldenCore.DanName,
            goldenCore.DanNature,
            goldenCore.TargetBranch,
            goldenCore.TargetSeat,
            goldenCore.SeatName,
            goldenCore.DanPivot,
            goldenCore.StabilityMultiplier,
            goldenCore.ArtAffinityMultiplier);
    }

    // ═══════════════════════════════════════
    // 道基凝聚值计算 + 品级判定 (v3.5)
    // ═══════════════════════════════════════
    static (string quality, int score) GenerateDaoFoundation(
        Dictionary<string, int> innate, string spiritGrade, string techGrade,
        double overflowCpp, string treasureGrade, Random rng)
    {
        // ① 灵根基数
        int spiritBase = GameData.DFSpiritBase.GetValueOrDefault(spiritGrade, 30);
        // ② 功法修正
        int techMod = GameData.DFTechMod.GetValueOrDefault(techGrade, 10);
        // ③ 灵力超额修正
        int overflowMod = (int)Math.Floor(overflowCpp / 50.0);
        // ④ 随机骰 1d30
        int dice = rng.Next(1, 31);
        // ⑤ 天材地宝加成
        int treasureBonus = treasureGrade switch
        {
            "下品" => 10, "中品" => 20, "上品" => 30, "极品" => 40, _ => 0
        };

        int score = spiritBase + techMod + overflowMod + dice + treasureBonus;
        string tentative = GameData.DFQualityFromScore(score);

        // 功法品级上下限钳制
        if (GameData.DFLimits.TryGetValue(techGrade, out var limits))
        {
            int tRank = GameData.DFQualityRank(tentative);
            int minRank = GameData.DFQualityRank(limits.min);
            int maxRank = GameData.DFQualityRank(limits.max);
            int clamped = Math.Max(minRank, Math.Min(maxRank, tRank));
            tentative = GameData.DFQualities[clamped];
        }

        return (tentative, score);
    }

    // ═══════════════════════════════════════
    // 金丹成丹判定值 + 丹籍兼容层 (TQ-013B)
    // ═══════════════════════════════════════
    static (GameData.GoldenCoreProfile profile, int score) GenerateGoldenCore(
        Dictionary<string, int> innate, string spiritGrade, string techGrade,
        string dfQuality, double overflowCpp, string treasureGrade, Random rng, Dictionary<string, double> weights)
    {
        // ① 道基延续分
        int dfContinue = GameData.GCDFContinue.GetValueOrDefault(dfQuality, 0);
        // ② 灵根基数
        int spiritBase = GameData.DFSpiritBase.GetValueOrDefault(spiritGrade, 30);
        // ③ 灵力投入分 (分段: <=2000每100灵力+3, >2000每100灵力+1)
        // cpp→有效灵力转换: 金丹初阶门槛=1200灵力对应cpp≈500, 比率≈2.4
        double mpOver = overflowCpp * 2.4;
        int mpScore = 0;
        if (mpOver <= 2000)
            mpScore = (int)Math.Floor((mpOver - GameData.GCMinMP) / 100.0 * 3);
        else
            mpScore = (int)Math.Floor((2000 - GameData.GCMinMP) / 100.0 * 3 +
                                       (mpOver - 2000) / 100.0 * 1);
        mpScore = Math.Max(0, mpScore);
        // ④ 随机骰 1d30
        int dice = rng.Next(1, 31);
        // ⑤ 天材地宝
        int treasureBonus = GameData.GCTreasure.GetValueOrDefault(treasureGrade, 0);

        int score = dfContinue + spiritBase + mpScore + dice + treasureBonus;

        return (GameData.ResolveGoldenCoreProfile(score, dfQuality, weights), score);
    }

    static int FindNext(string realm, int subIdx)
    {
        for (int i = 0; i < GameData.Milestones.Length; i++)
        {
            var ms = GameData.Milestones[i];
            if (ms.realm == realm && ms.subIdx == subIdx + 1) return i;
            int ri = Array.IndexOf(GameData.RealmOrder, realm), mri = Array.IndexOf(GameData.RealmOrder, ms.realm);
            if (mri == ri + 1 && ms.subIdx == 0) return i;
        }
        return GameData.Milestones.Length;
    }
    static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
}
