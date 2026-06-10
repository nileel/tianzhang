using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim;

static class GameData
{
    public static readonly Dictionary<string, int> Sublevels = new()
    {
        ["凡人"] = 1, ["练气"] = 9, ["筑基"] = 4, ["金丹"] = 4,
        ["元婴"] = 4, ["化神"] = 4, ["炼虚"] = 4
    };
    public static readonly string[] RealmOrder = ["凡人", "练气", "筑基", "金丹", "元婴", "化神", "炼虚"];
    public static readonly Dictionary<string, int> TechInnate = new() { ["上品"] = 4 };
    public static readonly Dictionary<string, double> SpiritMod = new()
    {
        ["凡品"] = 0.70, ["下品"] = 0.85, ["中品"] = 1.00, ["上品"] = 1.20, ["极品"] = 1.50
    };

    public record RealmBase(int HP, int MP, int 肉攻, int 神攻, int 肉防, int 神防, int 反应, int 移力, int 神识);
    public static readonly Dictionary<string, RealmBase> Base = new()
    {
        ["凡人"] = new(30, 0, 5, 5, 3, 3, 5, 2, 3),
        ["练气"] = new(100, 10, 25, 25, 20, 20, 15, 3, 5),
        ["筑基"] = new(600, 100, 120, 120, 100, 100, 50, 4, 8),
        ["金丹"] = new(7000, 2000, 1000, 1000, 800, 800, 300, 5, 15),
    };
    public record RealmFactor(double HP, double MP, double 攻, double 防, double 反应, double 神识);
    public static readonly Dictionary<string, RealmFactor> Factor = new()
    {
        ["凡人"] = new(4, 0.5, 1, 0.8, 0.6, 0.15),
        ["练气"] = new(8, 2, 3, 2, 0.75, 0.20),
        ["筑基"] = new(9, 7, 6.5, 4, 1.5, 0.25),
        ["金丹"] = new(22, 14, 12, 8, 3.0, 0.35),
    };
    public record SubGrowth(double HP, double MP, double 肉攻, double 神攻, double 肉防, double 神防, double 反应, double 移力, double 神识);
    public static readonly Dictionary<string, SubGrowth> SubGrowthBase = new()
    {
        ["练气"] = new(8, 3, 4, 4, 3, 3, 2, 0.2, 0.3),
        ["筑基"] = new(120, 35, 30, 30, 25, 25, 12, 0.5, 1.0),
        ["金丹"] = new(1800, 700, 350, 350, 280, 280, 90, 0.8, 4.0),
    };

    public const int InnatePerBreakthrough = 14;

    // ═══════════════════════════════════════
    // 道基品级体系 (v3.5)
    // ═══════════════════════════════════════
    public static readonly Dictionary<string, int> DFSpiritBase = new()
    {
        ["凡品"] = 10, ["下品"] = 20, ["中品"] = 30, ["上品"] = 40, ["极品"] = 50
    };
    public static readonly Dictionary<string, int> DFTechMod = new()
    {
        ["凡品"] = 0, ["下品"] = 5, ["中品"] = 10, ["上品"] = 15, ["极品"] = 20
    };
    // (下限, 上限) — 下限可<=上限
    public static readonly Dictionary<string, (string min, string max)> DFLimits = new()
    {
        ["凡品"] = ("无道基", "黄品"),
        ["下品"] = ("黄品",   "玄品"),
        ["中品"] = ("黄品",   "地品"),
        ["上品"] = ("玄品",   "地品"),
        ["极品"] = ("地品",   "天品"),
    };
    public static readonly string[] DFQualities = ["无道基", "黄品", "玄品", "地品", "天品"];
    public static int DFQualityRank(string q) => Array.IndexOf(DFQualities, q);

    // 凝聚值→品级阈值
    public static string DFQualityFromScore(int score) => score switch
    {
        >= 80 => "天品",
        >= 50 => "地品",
        >= 25 => "玄品",
        >= 15 => "黄品",
        _     => "无道基"
    };

    // 道基倍率（应用时乘到功法道基效果上）
        public static readonly Dictionary<string, double> DFMultiplier = new()
    {
        ["天品"] = 1.30, ["地品"] = 1.10, ["玄品"] = 1.00, ["黄品"] = 0.85, ["无道基"] = 0.0
    };

    // ═══════════════════════════════════════
    // 金丹品级体系 (v4.0)
    // ═══════════════════════════════════════
    public static readonly string[] GCQualities = ["", "九品", "八品", "七品", "六品", "五品", "四品", "三品", "二品", "一品"];
    public static int GCQualityRank(string q) => Array.IndexOf(GCQualities, q);
    public static readonly Dictionary<string, double> GCMultiplier = new()
    {
        ["一品"] = 3.0, ["二品"] = 2.5, ["三品"] = 2.0, ["四品"] = 1.7,
        ["五品"] = 1.4, ["六品"] = 1.2, ["七品"] = 1.0, ["八品"] = 0.85, ["九品"] = 0.70
    };
    public static readonly Dictionary<string, double> GCTypeScaling = new()
    {
        ["一品"] = 1.3, ["二品"] = 1.2, ["三品"] = 1.1, ["四品"] = 1.0,
        ["五品"] = 0.9, ["六品"] = 0.8, ["七品"] = 0.7, ["八品"] = 0.6, ["九品"] = 0.5
    };
    public static readonly Dictionary<string, string> GCDFCap = new()
    {
        ["天品"] = "一品", ["地品"] = "三品", ["玄品"] = "五品", ["黄品"] = "八品", ["无道基"] = ""
    };
    public static readonly Dictionary<string, int> GCDFContinue = new()
    {
        ["天品"] = 60, ["地品"] = 40, ["玄品"] = 25, ["黄品"] = 10
    };
    public static readonly Dictionary<string, int> GCTreasure = new()
    {
        ["下品"] = 5, ["中品"] = 10, ["上品"] = 20, ["极品"] = 30, [""] = 0
    };
    public static string GCQualityFromScore(int score) => score switch
    {
        >= 120 => "一品", >= 105 => "二品", >= 90 => "三品", >= 75 => "四品",
        >= 60  => "五品", >= 48  => "六品", >= 36 => "七品", >= 25 => "八品",
        >= 15  => "九品", _      => ""
    };
    public const double GCMinMP = 1200.0;
public static readonly (string realm, int subIdx, int cpp)[] Milestones = new (string, int, int)[]
    {
        ("练气", 0, 10), ("练气", 1, 22), ("练气", 2, 36), ("练气", 3, 52),
        ("练气", 4, 70), ("练气", 5, 90), ("练气", 6, 112), ("练气", 7, 136), ("练气", 8, 162),
        ("筑基", 0, 200), ("筑基", 1, 250), ("筑基", 2, 310), ("筑基", 3, 390),
        ("金丹", 0, 500), ("金丹", 1, 650), ("金丹", 2, 830), ("金丹", 3, 1050),
    };

    public const double BaseGainPerCycle = 10.0;
    public const int CultivationCycles = 200;
    public const double BreakthroughBaseRate = 0.70;

    public static int TotalSubs(string realm, int subIdx)
    {
        int t = 0;
        foreach (var r in RealmOrder) { if (r == realm) { t += subIdx + 1; break; } t += Sublevels[r]; }
        return t;
    }

    // 术法与神通配置
    public record ArtConfig(string Name, string Type, double Mult, int MPCost, int Cooldown);
    public record DivineConfig(string Name, string Type, double Mult, double DefPen, int Cooldown);
    public static readonly ArtConfig PhysicalArt = new("裂石拳", "物理", 1.3, 20, 3);
    public static readonly ArtConfig MagicArt = new("灵光闪", "神魂", 1.2, 20, 3);
    public static readonly DivineConfig PhysicalDivine = new("碎岳", "物理", 1.5, 10, 5);
    public static readonly ArtConfig WaterArt = new("川流劲", "物理", 1.25, 20, 3);
    public static readonly DivineConfig WaterDivine = new("逝水千击", "物理", 1.5, 10, 5);
    public static readonly DivineConfig MagicDivine = new("灵光贯日", "神魂", 1.4, 10, 5);
    public static readonly ArtConfig TaiyiArt = new("玄元正气诀", "神魂", 1.4, 25, 3);
    public static readonly ArtConfig TaiyiFuxiuArt = new("安神符", "神魂", 0.5, 20, 3);
    public static readonly DivineConfig TaiyiDivine = new("万法归宗", "神魂", 1.8, 15, 5);
    public static readonly DivineConfig TaiyiFuxiuDivine = new("天符镇岳", "神魂", 1.5, 20, 5);
}

class Character
{
    public string Name, Realm, Style;
    public int SubIndex;
    // v3.5: 道基
    public string DFQuality = "无道基";   // 天品/地品/玄品/黄品/无道基
    public double DFMult = 0.0;            // 道基效果倍率
    public int DFScore = 0;                // 凝聚值 (调试用)
    // v4.0: 金丹
    public string GCQuality = "";         // 一品~九品
    public double GCMult = 1.0;           // MP上限倍率
    public int GCScore = 0;               // 凝聚值 (调试用)
    public string GCType = "";            // 金丹类型
    public double GCTypeMult = 1.0;       // 金丹类型被动倍率
    // v4.1: 术法与神通
    public string ArtName = "";          // 术法名称
    public string ArtType = "";          // "物理" or "神魂"
    public double ArtMult = 1.0;         // 术法倍率
    public int ArtMPCost = 0;            // 术法灵力消耗
    public int ArtCooldown = 3;          // 术法冷却回合数
    public string DivineName = "";       // 神通名称
    public string DivineType = "";       // "物理" or "神魂"
    public double DivineMult = 1.0;      // 神通倍率
    public double DivineDefPen = 0;      // 神通防御穿透%
    public int DivineCooldown = 5;       // 神通冷却回合数

    public Dictionary<string, int> Innate = new();
    public Dictionary<string, int> Primary = new();
    public Dictionary<string, double> Secondary = new();
    public static string[] InnateKeys = ["根骨", "魂魄", "神识", "资质", "气运"];

    public static Character Create(string name, Dictionary<string, int> innate, string style)
    {
        var c = new Character { Name = name, Realm = "凡人", SubIndex = 0, Style = style };
        foreach (var k in InnateKeys) c.Innate[k] = innate[k];
        return c;
    }

    public void ApplyGrowth(string realm, string techGrade, Dictionary<string, double> weights)
    {
        int realmIdx = Array.IndexOf(GameData.RealmOrder, realm);
        int breakthroughs = realmIdx;
        if (breakthroughs == 0) return;
        int totalPts = breakthroughs * GameData.InnatePerBreakthrough;
        double tw = weights.Values.Sum();
        int sumAlloc = 0;
        var add = new Dictionary<string, int>();
        foreach (var k in InnateKeys) { int a = (int)Math.Round(totalPts * weights[k] / tw); add[k] = a; sumAlloc += a; }
        int diff = totalPts - sumAlloc;
        if (diff != 0) { string mk = InnateKeys[0]; foreach (var k in InnateKeys) if (weights[k] > weights[mk]) mk = k; add[mk] += diff; }
        foreach (var k in InnateKeys) Innate[k] += add[k];
    }

    static (string[] types, double pctPerChapter) ResolveSecondary(Dictionary<string, double> w)
    {
        var map = new Dictionary<string, string[]> {
            ["根骨"] = new[] { "格挡率" }, ["魂魄"] = new[] { "魂盾率" },
            ["神识"] = new[] { "命中率" }, ["资质"] = new[] { "暴击伤害" }, ["气运"] = new[] { "闪避率" }
        };
        var ordered = w.Where(kv => kv.Value > 0.15).OrderByDescending(kv => kv.Value).Take(2).ToList();
        if (ordered.Count == 0) ordered.Add(new("根骨", 1.0));
        var types = ordered.Select(kv => map[kv.Key][0]).ToArray();
        double pct = types.Length == 1 ? 5.0 : 3.0;
        return (types, pct);
    }

    public void FinalizeStats(string realm, int subIdx, string spiritGrade, Dictionary<string, double> weights)
    {
        Realm = realm; SubIndex = subIdx;
        var rb = GameData.Base[realm];
        var rf = GameData.Factor[realm];
        double sm = GameData.SpiritMod[spiritGrade];
        var (secTypes, secPctPerChapter) = ResolveSecondary(weights);

        // 一级属性 = 境界基础 + sum(小境界成长) + 先天×系数×权重
        Primary["HP"] = (int)Math.Round((rb.HP + SubGrowthSum("HP", realm, subIdx, weights) + Innate["根骨"] * rf.HP * weights["根骨"] * 2.2) * sm);
        Primary["MP"] = (int)Math.Round((rb.MP + SubGrowthSum("MP", realm, subIdx, weights) + Innate["魂魄"] * rf.MP * weights["魂魄"]) * sm * GCMult);
        Primary["肉攻"] = (int)Math.Round((rb.肉攻 + SubGrowthSum("肉攻", realm, subIdx, weights) + Innate["根骨"] * rf.攻 * weights["根骨"]) * sm);
        Primary["神攻"] = (int)Math.Round((rb.神攻 + SubGrowthSum("神攻", realm, subIdx, weights) + Innate["魂魄"] * rf.攻 * weights["魂魄"]) * sm);
        Primary["肉防"] = (int)Math.Round((rb.肉防 + SubGrowthSum("肉防", realm, subIdx, weights) + Innate["根骨"] * rf.防 * weights["根骨"]) * sm);
        Primary["神防"] = (int)Math.Round((rb.神防 + SubGrowthSum("神防", realm, subIdx, weights) + Innate["魂魄"] * rf.防 * weights["魂魄"]) * sm);
        Primary["反应"] = (int)Math.Round((rb.反应 + SubGrowthSum("反应", realm, subIdx, weights) + (Innate["根骨"] * weights["根骨"] + Innate["魂魄"] * weights["魂魄"] + Innate["神识"] * weights["神识"]) * rf.反应 / 3.0) * sm);
        Primary["移力"] = rb.移力;
        Primary["神识"] = (int)Math.Round((rb.神识 + SubGrowthSum("神识", realm, subIdx, weights) + Innate["神识"] * rf.神识 * weights["神识"]) * sm);

        // 二级属性
        int chapters = GameData.TotalSubs(realm, subIdx) / 4 + 1;
        foreach (var t in secTypes) Secondary[t] = chapters * secPctPerChapter;
        Secondary["格挡减伤率"] = Secondary.GetValueOrDefault("格挡率", 0) * 0.8;
        Secondary["魂盾减伤率"] = Secondary.GetValueOrDefault("魂盾率", 0) * 0.8;
        Secondary["闪避率"] = Secondary.GetValueOrDefault("闪避率", 0) + Innate["气运"] * 0.3;
        Secondary["命中率"] = Secondary.GetValueOrDefault("命中率", 0);
        Secondary["暴击率"] = Secondary.GetValueOrDefault("暴击率", 0) + Innate["神识"] * 0.3;
        Secondary["暴击伤害"] = Secondary.GetValueOrDefault("暴击伤害", 0) + Innate["资质"] * 0.8;
        Secondary["物抗率"] = 0; Secondary["魂抗率"] = 0;
    }

    double SubGrowthSum(string attr, string realm, int subIdx, Dictionary<string, double> w)
    {
        double sum = 0; int totalSubs = GameData.TotalSubs(realm, subIdx);
        int prevSubs = 0;
        foreach (var r in GameData.RealmOrder)
        {
            if (r == "凡人") continue;
            int subsHere = GameData.Sublevels[r];
            int effective = Math.Min(subsHere, Math.Max(0, totalSubs - prevSubs));
            if (effective <= 0) break;
            var sgb = GameData.SubGrowthBase[r];
            double val = attr switch
            {
                "HP" => sgb.HP, "MP" => sgb.MP, "肉攻" => sgb.肉攻, "神攻" => sgb.神攻,
                "肉防" => sgb.肉防, "神防" => sgb.神防, "反应" => sgb.反应, "神识" => sgb.神识, _ => 0
            };
            string innateKey = attr switch { "HP" or "肉攻" or "肉防" => "根骨", "MP" or "神攻" or "神防" => "魂魄", "神识" => "神识", "反应" => "根骨", _ => "根骨" };
            double scale = w[innateKey] / 0.6;
            sum += val * scale * effective;
            prevSubs += subsHere;
            if (r == realm) break;
        }
        return sum;
    }

    // v4.1: 根据风格和境界分配术法与神通
    public void AssignArts()
    {
        var artCfg = Style switch { "water_physical" => GameData.WaterArt, "physical" => GameData.PhysicalArt, "taiyi_fuxiu" => GameData.TaiyiFuxiuArt, "taiyi" => GameData.TaiyiArt, _ => GameData.MagicArt };
        ArtName = artCfg.Name; ArtType = artCfg.Type; ArtMult = artCfg.Mult; ArtMPCost = artCfg.MPCost; ArtCooldown = artCfg.Cooldown;
        if (Realm == "金丹" && GCQuality != "")
        {
            var divCfg = Style switch { "water_physical" => GameData.WaterDivine, "physical" => GameData.PhysicalDivine, "taiyi_fuxiu" => GameData.TaiyiFuxiuDivine, "taiyi" => GameData.TaiyiDivine, _ => GameData.MagicDivine };
            DivineName = divCfg.Name; DivineType = divCfg.Type; DivineMult = divCfg.Mult; DivineDefPen = divCfg.DefPen; DivineCooldown = divCfg.Cooldown;
        }
    }

}
static class Cultivation
{
    public record Result(string Realm, int SubIdx, int TotalSubs, string DFQuality, int DFScore, string GCQuality = "", int GCScore = 0, string GCType = "");

    public static Result Simulate(
        Dictionary<string, int> baseInnate, Dictionary<string, double> weights, int seed,
        string spiritGrade = "中品", string techGrade = "上品", string treasureGrade = "", int maxCycles = -1)
    {
        var rng = new Random(seed);
        int cycles = maxCycles < 0 ? GameData.CultivationCycles : maxCycles;
        double cpp = 0;
        string realm = "凡人"; int subIdx = 0;
        string dfQuality = "无道基"; int dfScore = 0; string gcQuality = ""; int gcScore = 0; string gcType = "";
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
                        (gcQuality, gcScore) = GenerateGoldenCore(
                            baseInnate, spiritGrade, techGrade, dfQuality, totalCpp, treasureGrade, rng);
                        gcType = ResolveGCType(weights);
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
        return new Result(realm, subIdx, GameData.TotalSubs(realm, subIdx), dfQuality, dfScore, gcQuality, gcScore, gcType);
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
    // 金丹凝聚值计算 + 品级判定 (v4.0)
    // ═══════════════════════════════════════
    static (string quality, int score) GenerateGoldenCore(
        Dictionary<string, int> innate, string spiritGrade, string techGrade,
        string dfQuality, double overflowCpp, string treasureGrade, Random rng)
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

        string tentative = GameData.GCQualityFromScore(score);

        // 道基硬上限钳制
        if (GameData.GCDFCap.TryGetValue(dfQuality, out var cap) && cap != "")
        {
            int tRank = GameData.GCQualityRank(tentative);
            int maxRank = GameData.GCQualityRank(cap);
            if (tRank > maxRank) tentative = GameData.GCQualities[maxRank];
        }
        // 无道基无法凝结
        if (dfQuality == "无道基") tentative = "";

        return (tentative, score);
    }

    // 简化金丹类型判定：取权重最高的先天属性映射
    static string ResolveGCType(Dictionary<string, double> weights)
    {
        var ordered = weights.OrderByDescending(kv => kv.Value).ToList();
        string top = ordered[0].Key;
        return top switch
        {
            "根骨" => "土", "魂魄" => "火", "神识" => "星",
            "资质" => "木", "气运" => "水", _ => "金"
        };
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

static class Combat
{
    static readonly Random Rng = new();
    // v4.1: 合并攻击结算（含穿透）
    static int Dmg(int atk, int def, double resist, double defPen = 0, double mult = 1.0)
    {
        int effectiveDef = (int)Math.Round(def * (1 - defPen / 100));
        double df = atk / (double)(atk + effectiveDef);
        return (int)Math.Max(0, Math.Round(atk * df * (1 - resist / 100.0) * mult));
    }

    // 格挡/魂盾/闪避/暴击 统一结算
    static int ApplyDefenses(int rawDmg, Character attacker, Character defender, string atkType)
    {
        bool isPhysical = atkType == "物理";
        double blockRate = defender.Secondary.GetValueOrDefault(isPhysical ? "格挡率" : "魂盾率", 0);
        if (Rng.NextDouble() * 100 < blockRate)
        {
            double reduction = defender.Secondary.GetValueOrDefault(isPhysical ? "格挡减伤率" : "魂盾减伤率", 0);
            rawDmg = (int)Math.Round(rawDmg * (1 - reduction / 100));
        }
        if (Rng.NextDouble() * 100 < Math.Max(0, defender.Secondary.GetValueOrDefault("闪避率", 0) - attacker.Secondary.GetValueOrDefault("命中率", 0)))
            rawDmg = 0;
        if (rawDmg > 0 && Rng.NextDouble() * 100 < attacker.Secondary.GetValueOrDefault("暴击率", 0))
            rawDmg = (int)Math.Round(rawDmg * (1 + attacker.Secondary.GetValueOrDefault("暴击伤害", 0) / 100));
        return rawDmg;
    }

    public static (double winsA, double winsB, double avgTurns) Simulate(Character ca, Character cb, int rounds)
    {
        int winsA = 0, winsB = 0;
        int totalTurns = 0;
        for (int r = 0; r < rounds; r++)
        {
            int hpA = ca.Primary["HP"], hpB = cb.Primary["HP"];
            int mpA = ca.Primary["MP"], mpB = cb.Primary["MP"];
            int artCdA = 0, artCdB = 0;
            int divineCdA = 0, divineCdB = 0;
            double rangePenaltyA = 1.0, rangePenaltyB = 1.0; // 远程优势: 对方下轮伤害折扣
            double ctA = 0, ctB = 0;
            double sA = 100.0 / ca.Primary["反应"], sB = 100.0 / cb.Primary["反应"];

            int turns = 0;
            while (hpA > 0 && hpB > 0)
            {
                turns++;
                if (artCdA > 0) artCdA--;
                if (artCdB > 0) artCdB--;
                if (divineCdA > 0) divineCdA--;
                if (divineCdB > 0) divineCdB--;

                if (ctA <= ctB)
                {
                    // AI决策: 神通 > 术法 > 平A
                    string atkType; double mult, defPen; int atk, def; double resist;
                    if (ca.DivineName != "" && divineCdA == 0)
                    {
                        atkType = ca.DivineType; mult = ca.DivineMult; defPen = ca.DivineDefPen;
                        divineCdA = ca.DivineCooldown;
                    }
                    else if (mpA >= ca.ArtMPCost && artCdA == 0)
                    {
                        atkType = ca.ArtType; mult = ca.ArtMult; defPen = 0;
                        mpA -= ca.ArtMPCost; artCdA = ca.ArtCooldown;
                    }
                    else
                    {
                    bool isPhysicalA = ca.Style == "physical" || ca.Style == "water_physical"; atkType = isPhysicalA ? "物理" : "神魂"; mult = 1.0; defPen = 0;
                    }
                    atk = atkType == "物理" ? ca.Primary["肉攻"] : ca.Primary["神攻"];
                    def = atkType == "物理" ? cb.Primary["肉防"] : cb.Primary["神防"];
                    resist = atkType == "物理" ? cb.Secondary.GetValueOrDefault("物抗率", 0) : cb.Secondary.GetValueOrDefault("魂抗率", 0);
                    int dmg = Dmg(atk, def, resist, defPen, mult);
                    // 远程惩罚: 对方上轮使用远程术法/神通, 本轮需拉近距离
                    dmg = (int)(dmg * rangePenaltyA); rangePenaltyA = 1.0;
                    dmg = ApplyDefenses(dmg, ca, cb, atkType);
                    // 远程优势: 术法/神通出手后对方需要拉近距离
                    bool isRanged = ca.Style == "magic";
                    if (isRanged) rangePenaltyB = 0.35;
                    hpB -= dmg;
                    ctA += sA;
                }
                else
                {
                    string atkType; double mult, defPen; int atk, def; double resist;
                    if (cb.DivineName != "" && divineCdB == 0)
                    {
                        atkType = cb.DivineType; mult = cb.DivineMult; defPen = cb.DivineDefPen;
                        divineCdB = cb.DivineCooldown;
                    }
                    else if (mpB >= cb.ArtMPCost && artCdB == 0)
                    {
                        atkType = cb.ArtType; mult = cb.ArtMult; defPen = 0;
                        mpB -= cb.ArtMPCost; artCdB = cb.ArtCooldown;
                    }
                    else
                    {
                    bool isPhysicalB = cb.Style == "physical" || cb.Style == "water_physical"; atkType = isPhysicalB ? "物理" : "神魂"; mult = 1.0; defPen = 0;
                    }
                    atk = atkType == "物理" ? cb.Primary["肉攻"] : cb.Primary["神攻"];
                    def = atkType == "物理" ? ca.Primary["肉防"] : ca.Primary["神防"];
                    resist = atkType == "物理" ? ca.Secondary.GetValueOrDefault("物抗率", 0) : ca.Secondary.GetValueOrDefault("魂抗率", 0);
                    int dmg = Dmg(atk, def, resist, defPen, mult);
                    // 远程惩罚: 对方上轮使用远程术法/神通, 本轮需拉近距离
                    dmg = (int)(dmg * rangePenaltyB); rangePenaltyB = 1.0;
                    dmg = ApplyDefenses(dmg, cb, ca, atkType);
                    // 远程优势: B使用术法/神通后A需要拉近距离
                    bool bRanged = cb.Style == "magic";
                    if (bRanged) rangePenaltyA = 0.35;
                    hpA -= dmg;
                    ctB += sB;
                }
            }
            totalTurns += turns;
            if (hpA > 0) winsA++; else winsB++;
        }
        return (winsA * 100.0 / rounds, winsB * 100.0 / rounds, (double)totalTurns / rounds);
    }
}

class Program
{
    record BuildDef(string Name, string Desc, Dictionary<string, int> Innate, string Style, Dictionary<string, double> Weights);

    static void Main()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const string TECH = "上品", SPIRIT = "中品";
        const int SEEDS = 20, SIM = 2000;

        var buildDefs = new BuildDef[]
        {
            new("物·纯战", "资质3根骨25", new() { ["根骨"]=25,["魂魄"]=8,["神识"]=5,["资质"]=3,["气运"]=5 }, "physical",
                new() { ["根骨"]=0.8,["魂魄"]=0.1,["神识"]=0.1,["资质"]=0.0,["气运"]=0.0 }),
            new("物·均衡", "资质11根骨20", new() { ["根骨"]=20,["魂魄"]=8,["神识"]=8,["资质"]=11,["气运"]=8 }, "physical",
                new() { ["根骨"]=0.4,["魂魄"]=0.1,["神识"]=0.1,["资质"]=0.2,["气运"]=0.2 }),
            new("物·修炼", "资质19根骨15", new() { ["根骨"]=15,["魂魄"]=8,["神识"]=8,["资质"]=19,["气运"]=8 }, "physical",
                new() { ["根骨"]=0.3,["魂魄"]=0.1,["神识"]=0.1,["资质"]=0.4,["气运"]=0.1 }),
            new("肉盾型",  "根骨43极限", new() { ["根骨"]=43,["魂魄"]=5,["神识"]=5,["资质"]=5,["气运"]=5 }, "physical",
                new() { ["根骨"]=1.0,["魂魄"]=0.0,["神识"]=0.0,["资质"]=0.0,["气运"]=0.0 }),
            new("法·纯战", "资质3魂魄25", new() { ["根骨"]=5,["魂魄"]=25,["神识"]=8,["资质"]=3,["气运"]=5 }, "magic",
                new() { ["根骨"]=0.0,["魂魄"]=0.8,["神识"]=0.1,["资质"]=0.0,["气运"]=0.1 }),
            new("法·均衡", "资质11魂魄20", new() { ["根骨"]=8,["魂魄"]=20,["神识"]=8,["资质"]=11,["气运"]=8 }, "magic",
                new() { ["根骨"]=0.1,["魂魄"]=0.4,["神识"]=0.1,["资质"]=0.2,["气运"]=0.2 }),
            new("法·修炼", "资质19魂魄15", new() { ["根骨"]=8,["魂魄"]=15,["神识"]=8,["资质"]=19,["气运"]=8 }, "magic",
                new() { ["根骨"]=0.1,["魂魄"]=0.3,["神识"]=0.1,["资质"]=0.4,["气运"]=0.1 }),
            new("灵修型",  "魂魄43极限", new() { ["根骨"]=5,["魂魄"]=43,["神识"]=5,["资质"]=5,["气运"]=5 }, "magic",
                new() { ["根骨"]=0.0,["魂魄"]=1.0,["神识"]=0.0,["资质"]=0.0,["气运"]=0.0 }),
            new("水·散修", "资质18气运14", new() { ["根骨"]=10,["魂魄"]=9,["神识"]=9,["资质"]=18,["气运"]=14 }, "water_physical",
                new() { ["根骨"]=0.15,["魂魄"]=0.15,["神识"]=0.15,["资质"]=0.35,["气运"]=0.20 }),
            new("太一·法修", "资质14魂魄18", new() { ["根骨"]=6,["魂魄"]=18,["神识"]=10,["资质"]=14,["气运"]=8 }, "taiyi",
                new() { ["根骨"]=0.05,["魂魄"]=0.45,["神识"]=0.15,["资质"]=0.25,["气运"]=0.10 }),
            new("太一·符修", "神识18魂魄14", new() { ["根骨"]=5,["魂魄"]=14,["神识"]=18,["资质"]=12,["气运"]=10 }, "taiyi_fuxiu",
                new() { ["根骨"]=0.05,["魂魄"]=0.30,["神识"]=0.40,["资质"]=0.15,["气运"]=0.10 }),
        };
        int N = buildDefs.Length;

        Console.WriteLine($"修炼模拟 ({SEEDS}种子 x {GameData.CultivationCycles}轮, 灵根={SPIRIT}, 功法={TECH})...");
        var pool = new List<Character>[N];
        var realmDist = new Dictionary<string, int>[N];
        // v3.5: 道基分布统计
                var dfDist = new Dictionary<string, int>[N];
        var gcDist = new Dictionary<string, int>[N];
        for (int i = 0; i < N; i++)
        {
            pool[i] = new List<Character>();
            realmDist[i] = new Dictionary<string, int> { ["练气"] = 0, ["筑基"] = 0, ["金丹"] = 0 };
            dfDist[i] = new Dictionary<string, int>();
                        foreach (var q in GameData.DFQualities) dfDist[i][q] = 0;
            gcDist[i] = new Dictionary<string, int>();
            foreach (var q in GameData.GCQualities.Skip(1)) gcDist[i][q] = 0;
            gcDist[i][""] = 0;
        }

        for (int seed = 0; seed < SEEDS; seed++)
        {
            for (int i = 0; i < N; i++)
            {
                var bd = buildDefs[i];
                var result = Cultivation.Simulate(bd.Innate, bd.Weights, seed * 100 + i, SPIRIT, TECH);
                var c = Character.Create(bd.Name, bd.Innate, bd.Style);
                c.ApplyGrowth(result.Realm, TECH, bd.Weights);
                c.FinalizeStats(result.Realm, result.SubIdx, SPIRIT, bd.Weights);
                // v3.5: 记录道基
                c.DFQuality = result.DFQuality;
                c.DFMult = GameData.DFMultiplier[result.DFQuality];
                c.DFScore = result.DFScore;
                c.GCQuality = result.GCQuality; c.GCMult = GameData.GCMultiplier.GetValueOrDefault(result.GCQuality, 1.0); c.GCScore = result.GCScore;
                c.GCType = result.GCType; c.AssignArts(); c.GCType = result.GCType;
                c.GCTypeMult = GameData.GCTypeScaling.GetValueOrDefault(result.GCQuality, 1.0);
                pool[i].Add(c);
                realmDist[i][c.Realm]++;
                dfDist[i][c.DFQuality]++;
                    if (c.GCQuality != "") gcDist[i][c.GCQuality]++;
                    else gcDist[i][""]++;
            }
        }
        Console.WriteLine("完成");

        // v3.5: 道基品质分布
        Console.WriteLine();
        Console.WriteLine("【道基品质分布】");
        Console.WriteLine($"{"Build",-10} {"无道基",-6} {"黄品",-6} {"玄品",-6} {"地品",-6} {"天品",-6} {"平均凝聚值",-8}");
        Console.WriteLine(new string('-', 52));
        for (int i = 0; i < N; i++)
        {
            double avgScore = pool[i].Average(c => (double)c.DFScore);
            Console.Write($"  {buildDefs[i].Name,-8}");
            foreach (var q in GameData.DFQualities)
                Console.Write($" {dfDist[i][q],4} ");
            Console.WriteLine($" {avgScore,7:F0}");
        }

        // 金丹品级分布
        Console.WriteLine();
        Console.WriteLine("【金丹品级分布（已结丹角色）】");
        Console.Write("{0,-10}", "Build");
        foreach (var q in GameData.GCQualities.Skip(1)) Console.Write("{0,5}", q);
        Console.WriteLine(" {0,-6} {1,-8}", "无金丹", "平均凝聚值");
        Console.WriteLine(new string('-', 10 + 6 * GameData.GCQualities.Length + 12));
        for (int i = 0; i < N; i++)
        {
            Console.Write("{0,-10}", buildDefs[i].Name);
            foreach (var q in GameData.GCQualities.Skip(1)) Console.Write("{0,5}", gcDist[i].GetValueOrDefault(q, 0));
            Console.Write(" {0,5}", gcDist[i].GetValueOrDefault("", 0));
            double avgGc = pool[i].Where(c => c.GCScore > 0).Select(c => (double)c.GCScore).DefaultIfEmpty(0).Average();
            Console.WriteLine(" {0,8:F0}", avgGc);
        }
        // 详细子境界分布
        Console.WriteLine();
        Console.WriteLine("【详细境界分布】");
        for (int i = 0; i < N; i++)
        {
            var groups = pool[i].GroupBy(c => $"{c.Realm}{c.SubIndex}").OrderBy(g => g.Key);
            Console.WriteLine($"  {buildDefs[i].Name,-8}: {string.Join(", ", groups.Select(g => $"{g.Key}({g.Count()})"))}");
        }
        Console.WriteLine();

        // 统计
        Console.WriteLine("【境界 & 属性分布】");
        Console.WriteLine($"{"Build",-10} {"资质",-5} {"练气%",-7} {"筑基%",-7} {"金丹%",-7} {"平均HP",-7} {"成长率",-7}");
        Console.WriteLine(new string('-', 65));
        for (int i = 0; i < N; i++)
        {
            double avgHp = pool[i].Average(c => (double)c.Primary["HP"]);
            double avg资 = pool[i].Average(c => (double)c.Innate["资质"]);
            double lq = realmDist[i]["练气"] * 100.0 / SEEDS;
            double zj = realmDist[i]["筑基"] * 100.0 / SEEDS;
            double jd = realmDist[i]["金丹"] * 100.0 / SEEDS;
            Console.WriteLine($"  {buildDefs[i].Name,-8} {avg资,4:F0}  {lq,5:F0}%  {zj,5:F0}%  {jd,5:F0}%  {avgHp,6:F0}  {buildDefs[i].Desc}");
        }
        Console.WriteLine();

        // 战斗矩阵
        Console.WriteLine($"正在计算 {N}x{N} 矩阵...");
        var rng = new Random(12345);
        double[,] mat = new double[N, N];
        for (int i = 0; i < N; i++)
        {
            for (int j = i + 1; j < N; j++)
            {
                int wI = 0, tot = 0;
                for (int s = 0; s < SEEDS; s++)
                {
                    var ci = pool[i][s]; var cj = pool[j][s];
                    int h = SIM / SEEDS / 2;
                    var (wi, wj, _) = Combat.Simulate(ci, cj, h);
                    var (wi2, wj2, _) = Combat.Simulate(cj, ci, h);
                    wI += (int)Math.Round((wi + wj2) / 2.0 * h * 2 / 100.0);
                    tot += h * 2;
                }
                mat[i, j] = wI * 100.0 / tot;
                mat[j, i] = 100.0 - mat[i, j];
            }
        }
        sw.Stop();

        Console.WriteLine();
        Console.WriteLine($"================================================================================");
        Console.WriteLine($"  修炼后战斗胜率矩阵 ({SEEDS}次修炼 x {SIM}轮, {sw.ElapsedMilliseconds}ms)");
        Console.WriteLine($"================================================================================");
        Console.WriteLine();

        string[] tags = buildDefs.Select(b => b.Name).ToArray();
        Console.Write($"{"",-10}");
        for (int j = 0; j < N; j++) Console.Write($"{tags[j],-8}");
        Console.WriteLine();
        Console.WriteLine(new string('-', 10 + 8 * N));

        for (int i = 0; i < N; i++)
        {
            Console.Write($"{tags[i],-10}");
            for (int j = 0; j < N; j++)
            {
                if (i == j) Console.Write($"{"---",-8}");
                else
                {
                    double p = mat[i, j];
                    string tag = p switch { >= 80 => "CR", >= 60 => "FV", >= 40 => "EV", _ => "WK" };
                    Console.Write($"{tag}{p,4:F0}% ");
                }
            }
            Console.WriteLine();
        }
        Console.WriteLine("  CR=碾压 FV=优势 EV=均势 WK=劣势");
        Console.WriteLine();

        Console.WriteLine("================================================================================");

        // DEBUG

        // 练气快照
        const int EARLY_CYCLES = 40;
        Console.WriteLine();
        Console.WriteLine("[练气快照 同境对战]");
        var earlyPool = new List<Character>[N];
        for (int i = 0; i < N; i++) earlyPool[i] = new List<Character>();
        for (int seed = 0; seed < SEEDS; seed++)
        {
            for (int i = 0; i < N; i++)
            {
                var bd = buildDefs[i];
                var result = Cultivation.Simulate(bd.Innate, bd.Weights, seed * 100 + i + 10000, SPIRIT, TECH, maxCycles: EARLY_CYCLES);
                var c = Character.Create(bd.Name, bd.Innate, bd.Style);
                c.ApplyGrowth(result.Realm, TECH, bd.Weights);
                c.FinalizeStats(result.Realm, result.SubIdx, SPIRIT, bd.Weights);
                c.DFQuality = result.DFQuality; c.DFMult = GameData.DFMultiplier[result.DFQuality];
                c.GCQuality = result.GCQuality; c.GCMult = GameData.GCMultiplier.GetValueOrDefault(result.GCQuality, 1.0);
                c.GCType = result.GCType; c.AssignArts(); c.GCType = result.GCType;
                c.GCTypeMult = GameData.GCTypeScaling.GetValueOrDefault(result.GCQuality, 1.0);
                earlyPool[i].Add(c);
            }
        }
        Console.WriteLine("  练气角色境界分布:");
        for (int i = 0; i < N; i++)
        {
            var groups = earlyPool[i].GroupBy(c => $"{c.Realm}{c.SubIndex}").OrderBy(g => g.Key);
            Console.WriteLine($"    {buildDefs[i].Name,-8}: {string.Join(", ", groups.Select(g => $"{g.Key}({g.Count()})"))}");
        }
        double earlyTotalTurns = 0; int earlyTurnCombats = 0;
        for (int i = 0; i < N; i++)
        {
            for (int j = i + 1; j < N; j++)
            {
                var ciList = earlyPool[i].Where(c => c.Realm == "练气").Take(5).ToList();
                var cjList = earlyPool[j].Where(c => c.Realm == "练气").Take(5).ToList();
                foreach (var ci in ciList)
                    foreach (var cj in cjList)
                    {
                        var (_, _, t) = Combat.Simulate(ci, cj, 5);
                        var (_, _, t2) = Combat.Simulate(cj, ci, 5);
                        earlyTotalTurns += t + t2; earlyTurnCombats += 2;
                    }
            }
        }
        if (earlyTurnCombats > 0)
            Console.WriteLine("  练气同境平均回合数: {0:F1} (样本={1}场)", earlyTotalTurns / earlyTurnCombats, earlyTurnCombats);
        else
            Console.WriteLine("  练气同境: 无练气角色可对战 (当前40轮均已突破至筑基+)");
        // 筑基同境（使用主池200轮数据）
        double baseTotalTurns = 0; int baseTurnCombats = 0;
        for (int i = 0; i < N; i++)
        {
            for (int j = i + 1; j < N; j++)
            {
                var ciList = pool[i].Where(c => c.Realm == "筑基").Take(5).ToList();
                var cjList = pool[j].Where(c => c.Realm == "筑基").Take(5).ToList();
                foreach (var ci in ciList)
                    foreach (var cj in cjList)
                    {
                        var (_, _, t) = Combat.Simulate(ci, cj, 5);
                        var (_, _, t2) = Combat.Simulate(cj, ci, 5);
                        baseTotalTurns += t + t2; baseTurnCombats += 2;
                    }
            }
        }
        if (baseTurnCombats > 0)
            Console.WriteLine("  筑基同境平均回合数: {0:F1} (样本={1}场)", baseTotalTurns / baseTurnCombats, baseTurnCombats);
        else
            Console.WriteLine("  筑基同境: 无筑基角色可对战");
        Console.WriteLine();
        // 平均回合数
        Console.WriteLine();
        Console.WriteLine("【金丹同境平均战斗回合数（含术法/神通）】");
        Console.WriteLine("（每对Build取前5个角色互搏，每场5轮）");
        Console.WriteLine();
        double totalTurnsAll = 0; int turnCombats = 0;
        for (int i = 0; i < N; i++)
        {
            for (int j = i + 1; j < N; j++)
            {
                foreach (var ci in pool[i].Take(5))
                    foreach (var cj in pool[j].Take(5))
                    {
                        var (_, _, t) = Combat.Simulate(ci, cj, 5);
                        var (_, _, t2) = Combat.Simulate(cj, ci, 5);
                        totalTurnsAll += t + t2; turnCombats += 2;
                    }
            }
        }
        Console.WriteLine("  总平均回合数: {0:F1} (样本={1}场)", totalTurnsAll / turnCombats, turnCombats);
        Console.WriteLine();

        // DEBUG
        Console.WriteLine("【调试：物·均衡 vs 物·纯战 属性对比（seed=0）】");
        var c_wl_jh = pool[1][0];
        var c_wl_cz = pool[0][0];
        void PrintStats(Character c) {
            Console.Write($"  {c.Name} ({c.Realm}{c.SubIndex}) 道基={c.DFQuality}({c.DFScore}) 金丹={c.GCQuality}({c.GCScore}):");
            foreach (var k in new[]{"HP","MP","肉攻","神攻","肉防","神防","反应"})
                Console.Write($" {k}={c.Primary[k]}");
            Console.Write("  二级:");
            foreach (var k in new[]{"格挡率","格挡减伤率","魂盾率","魂盾减伤率","闪避率","命中率","暴击率","暴击伤害"})
                Console.Write($" {k}={c.Secondary.GetValueOrDefault(k, 0):F0}");
            Console.WriteLine();
        }
        PrintStats(c_wl_jh);
        PrintStats(c_wl_cz);
        var (wa, wb, _) = Combat.Simulate(c_wl_jh, c_wl_cz, 100);
        var (wa2, wb2, _) = Combat.Simulate(c_wl_cz, c_wl_jh, 100);
        Console.WriteLine($"  均衡先手: {wa:F0}%  纯战先手: {wb2:F0}%  均: {(wa+wb2)/2:F0}%");
        Console.WriteLine("  v3.5: 属性双轨成长 + 道基品级（凝聚值判定+功法上下限钳制）");
        Console.WriteLine("================================================================================");
    }
}
