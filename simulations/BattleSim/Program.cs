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

    // ═══════════════════════════════════════
    // v5.1: 功法小境界成长表（来源：docs/角色养成/功法/）
    // ═══════════════════════════════════════
    public record GongFaGrowth(int HP, int MP, int 肉攻, int 神攻, int 肉防, int 神防, int 反应, double 移力, double 神识);
    public static readonly Dictionary<string, Dictionary<string, GongFaGrowth>> GongFaTables = new()
    {
        // 玉清崖
        ["疾雷破山经"] = new() {
            ["练气"] = new(13, 5, 7, 5, 5, 3, 3, 0.2, 0.5),
            ["筑基"] = new(133, 58, 50, 42, 42, 33, 20, 0.5, 1.7),
            ["金丹"] = new(633, 583, 250, 233, 200, 183, 75, 0.8, 3.3),
            ["元婴"] = new(3000, 2333, 1000, 917, 833, 750, 250, 1, 5),
        },
        // 混元山
        ["含弘光大典"] = new() {
            ["练气"] = new(11, 4, 5, 4, 4, 2, 2, 0.2, 0.2),
            ["筑基"] = new(107, 47, 40, 33, 33, 17, 10, 0.5, 0.8),
            ["金丹"] = new(507, 467, 200, 187, 160, 92, 38, 0.8, 1.7),
            ["元婴"] = new(2400, 1867, 800, 733, 667, 375, 125, 1, 2.5),
        },
        ["白屋青云录"] = new() {
            ["练气"] = new(11, 4, 5, 4, 4, 3, 3, 0.2, 0.4),
            ["筑基"] = new(107, 47, 40, 33, 33, 27, 16, 0.5, 1.3),
            ["金丹"] = new(507, 467, 200, 187, 160, 147, 60, 0.8, 2.7),
            ["元婴"] = new(2400, 1867, 800, 733, 667, 600, 200, 1, 4),
        },
        ["混元同尘典"] = new() {
            ["练气"] = new(13, 2, 7, 2, 5, 2, 2, 0.2, 0.2),
            ["筑基"] = new(133, 29, 50, 21, 42, 17, 10, 0.5, 0.8),
            ["金丹"] = new(633, 292, 250, 117, 200, 92, 38, 0.8, 1.7),
            ["元婴"] = new(3000, 1167, 1000, 458, 833, 375, 125, 1, 2.5),
        },
        // 太虚观
        ["万物不迁法"] = new() {
            ["练气"] = new(5, 2, 3, 2, 2, 1, 1, 0.2, 0.2),
            ["筑基"] = new(53, 23, 20, 17, 17, 13, 8, 0.5, 0.7),
            ["金丹"] = new(253, 233, 100, 93, 80, 73, 30, 0.8, 1.3),
            ["元婴"] = new(1200, 933, 400, 367, 333, 300, 100, 1, 2),
        },
        // 散修
        ["秋水游心经"] = new() {
            ["练气"] = new(7, 3, 4, 3, 3, 2, 2, 0.2, 0.3),
            ["筑基"] = new(67, 29, 25, 21, 21, 17, 10, 0.5, 0.8),
            ["金丹"] = new(317, 292, 125, 117, 100, 92, 38, 0.8, 1.7),
            ["元婴"] = new(1500, 1167, 500, 458, 417, 375, 125, 1, 2.5),
        },
        // 太一道庭
        ["抱元守一经"] = new() {
            ["练气"] = new(3, 3, 2, 3, 1, 2, 1, 0.2, 0.3),
            ["筑基"] = new(40, 30, 15, 23, 13, 20, 8, 0.5, 1.0),
            ["金丹"] = new(200, 267, 73, 107, 67, 87, 30, 0.8, 1.7),
            ["元婴"] = new(1000, 1067, 300, 433, 267, 367, 100, 1, 2),
        },
        ["云篆度人经"] = new() {
            ["筑基"] = new(47, 23, 13, 20, 13, 17, 8, 0.5, 1.3),
            ["金丹"] = new(233, 233, 73, 100, 67, 80, 30, 0.8, 2.0),
            ["元婴"] = new(1067, 933, 300, 400, 267, 333, 100, 1, 3),
        },
        // 太虚观（续）
        ["不真自虚法"] = new() {
            ["练气"] = new(5, 2, 3, 2, 2, 1, 1, 0.2, 0.2),
            ["筑基"] = new(53, 23, 20, 17, 17, 13, 8, 0.5, 0.7),
            ["金丹"] = new(253, 233, 100, 93, 80, 73, 30, 0.8, 1.3),
            ["元婴"] = new(1200, 933, 400, 367, 333, 300, 100, 1, 2),
            ["化神"] = new(4667, 4333, 1667, 1533, 1333, 1200, 300, 1.2, 3),
        },
        ["心无性有法"] = new() {
            ["练气"] = new(5, 2, 3, 2, 2, 1, 1, 0.2, 0.2),
            ["筑基"] = new(53, 23, 20, 17, 17, 13, 8, 0.5, 0.7),
            ["金丹"] = new(253, 233, 100, 93, 80, 73, 30, 0.8, 1.3),
        },
    };

    // v5.2: 功法属性倾向星数（来源：docs/角色养成/功法/）
    public static readonly Dictionary<string, Dictionary<string, double>> GongFaStars = new()
    {
        ["疾雷破山经"] = new() { ["根骨"]=5, ["魂魄"]=3.5, ["神识"]=5, ["资质"]=2, ["气运"]=1 },
        ["含弘光大典"] = new() { ["根骨"]=4, ["魂魄"]=4, ["神识"]=3.5, ["资质"]=3, ["气运"]=2.5 },
        ["白屋青云录"] = new() { ["根骨"]=2.5, ["魂魄"]=3, ["神识"]=3.5, ["资质"]=4, ["气运"]=4 },
        ["混元同尘典"] = new() { ["根骨"]=5, ["魂魄"]=3.5, ["神识"]=2.5, ["资质"]=3, ["气运"]=3.5 },
        ["万物不迁法"] = new() { ["根骨"]=2, ["魂魄"]=3, ["神识"]=2, ["资质"]=3, ["气运"]=1 },
        ["秋水游心经"] = new() { ["根骨"]=3, ["魂魄"]=3, ["神识"]=3, ["资质"]=4, ["气运"]=4 },
        ["抱元守一经"] = new() { ["根骨"]=2, ["魂魄"]=4, ["神识"]=4, ["资质"]=3, ["气运"]=3 },
        ["云篆度人经"] = new() { ["根骨"]=2, ["魂魄"]=3, ["神识"]=5, ["资质"]=4, ["气运"]=3 },
        ["不真自虚法"] = new() { ["根骨"]=1, ["魂魄"]=3, ["神识"]=3, ["资质"]=2, ["气运"]=1 },
        ["心无性有法"] = new() { ["根骨"]=1, ["魂魄"]=3, ["神识"]=2, ["资质"]=3, ["气运"]=1 },
    };

    // 星数→归一化权重
    public static Dictionary<string, double> WeightsFromGongFa(string name)
    {
        if (!GongFaStars.TryGetValue(name, out var stars))
            return new() { ["根骨"]=0.2, ["魂魄"]=0.2, ["神识"]=0.2, ["资质"]=0.2, ["气运"]=0.2 };
        double sum = stars.Values.Sum();
        return stars.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / sum, 2));
    }
    public static readonly ArtConfig MagicArt = new("灵光闪", "神魂", 1.2, 20, 3);
    public static readonly DivineConfig PhysicalDivine = new("碎岳", "物理", 1.5, 10, 5);
    public static readonly ArtConfig WaterArt = new("川流劲", "物理", 1.25, 20, 3);
    public static readonly DivineConfig WaterDivine = new("逝水千击", "物理", 1.5, 10, 5);
    public static readonly DivineConfig MagicDivine = new("灵光贯日", "神魂", 1.4, 10, 5);
    public static readonly ArtConfig TaiyiArt = new("玄元正气诀", "神魂", 1.4, 25, 3);
    public static readonly ArtConfig TaiyiFuxiuArt = new("安神符", "神魂", 0.5, 20, 3);
    public static readonly DivineConfig TaiyiDivine = new("万法归宗", "神魂", 1.8, 15, 5);
    public static readonly DivineConfig TaiyiFuxiuDivine = new("天符镇岳", "神魂", 1.5, 20, 5);
    public static readonly ArtConfig TaixuArt = new("业火咒", "神魂", 1.5, 25, 3);
    public static readonly DivineConfig TaixuDivine = new("魂归彼岸", "神魂", 1.8, 15, 5);
    public static readonly ArtConfig YuqingArt = new("九霄太乙斩", "物理", 1.5, 25, 3);
    public static readonly DivineConfig YuqingDivine = new("万剑朝宗", "物理", 1.8, 15, 5);
    public static readonly ArtConfig YuqingLeijieArt = new("九霄雷罚", "物理", 1.4, 25, 3);
    public static readonly DivineConfig YuqingLeijieDivine = new("雷剑破虚", "物理", 1.6, 15, 5);
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
    // v5.1: 功法名称（用于读取实际小境界成长表）
    public string GongFaName = "";
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
        Primary["HP"] = (int)Math.Round((rb.HP + SubGrowthSum("HP", realm, subIdx, weights, GongFaName) + Innate["根骨"] * rf.HP * weights["根骨"] * 2.2) * sm);
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

    double SubGrowthSum(string attr, string realm, int subIdx, Dictionary<string, double> w, string gongFaName = "")
    {
        // v5.1: 优先使用功法实际成长表
        if (!string.IsNullOrEmpty(gongFaName) && GameData.GongFaTables.TryGetValue(gongFaName, out var table))
        {
            double sum = 0; int totalSubs = GameData.TotalSubs(realm, subIdx);
            int prevSubs = 0;
            foreach (var r in GameData.RealmOrder)
            {
                if (r == "凡人") continue;
                int subsHere = GameData.Sublevels[r];
                int effective = Math.Min(subsHere, Math.Max(0, totalSubs - prevSubs));
                if (effective <= 0) break;
                if (!table.TryGetValue(r, out var grow)) break;
                double val = attr switch
                {
                    "HP" => grow.HP, "MP" => grow.MP, "肉攻" => grow.肉攻, "神攻" => grow.神攻,
                    "肉防" => grow.肉防, "神防" => grow.神防, "反应" => grow.反应, "神识" => grow.神识, _ => 0
                };
                sum += val * effective;
                prevSubs += subsHere;
                if (r == realm) break;
            }
            return sum;
        }
        // 回退：原有权重近似计算
        double wsum = 0; int wtotalSubs = GameData.TotalSubs(realm, subIdx);
        int wprevSubs = 0;
        foreach (var r in GameData.RealmOrder)
        {
            if (r == "凡人") continue;
            int subsHere = GameData.Sublevels[r];
            int effective = Math.Min(subsHere, Math.Max(0, wtotalSubs - wprevSubs));
            if (effective <= 0) break;
            var sgb = GameData.SubGrowthBase[r];
            double val = attr switch
            {
                "HP" => sgb.HP, "MP" => sgb.MP, "肉攻" => sgb.肉攻, "神攻" => sgb.神攻,
                "肉防" => sgb.肉防, "神防" => sgb.神防, "反应" => sgb.反应, "神识" => sgb.神识, _ => 0
            };
            string innateKey = attr switch { "HP" or "肉攻" or "肉防" => "根骨", "MP" or "神攻" or "神防" => "魂魄", "神识" => "神识", "反应" => "根骨", _ => "根骨" };
            double scale = w[innateKey] / 0.6;
            wsum += val * scale * effective;
            wprevSubs += subsHere;
            if (r == realm) break;
        }
        return wsum;
    }

    // v4.1: 根据风格和境界分配术法与神通
    public void AssignArts()
    {
        var artCfg = Style switch { "water_physical" => GameData.WaterArt, "physical" => GameData.PhysicalArt, "taiyi_fuxiu" => GameData.TaiyiFuxiuArt, "taiyi" => GameData.TaiyiArt, "taixu" => GameData.TaixuArt, "taixu_xuangan" => GameData.TaixuArt, "yuqing" => GameData.YuqingArt, "yuqing_kuxing" => GameData.YuqingArt, "yuqing_leijie" => GameData.YuqingLeijieArt, _ => GameData.MagicArt };
        ArtName = artCfg.Name; ArtType = artCfg.Type; ArtMult = artCfg.Mult; ArtMPCost = artCfg.MPCost; ArtCooldown = artCfg.Cooldown;
        if (Realm == "金丹" && GCQuality != "")
        {
            var divCfg = Style switch { "water_physical" => GameData.WaterDivine, "physical" => GameData.PhysicalDivine, "taiyi_fuxiu" => GameData.TaiyiFuxiuDivine, "taiyi" => GameData.TaiyiDivine, "taixu" => GameData.TaixuDivine, "taixu_xuangan" => GameData.TaixuDivine, "yuqing" => GameData.YuqingDivine, "yuqing_kuxing" => GameData.YuqingDivine, "yuqing_leijie" => GameData.YuqingLeijieDivine, _ => GameData.MagicDivine };
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
    static int ApplyDefenses(int rawDmg, Character attacker, Character defender, string atkType, bool ignoreDodge = false, bool ignoreBlock = false)
    {
        bool isPhysical = atkType == "物理";
        double blockRate = defender.Secondary.GetValueOrDefault(isPhysical ? "格挡率" : "魂盾率", 0);
        if (!ignoreBlock && Rng.NextDouble() * 100 < blockRate)
        {
            double reduction = defender.Secondary.GetValueOrDefault(isPhysical ? "格挡减伤率" : "魂盾减伤率", 0);
            rawDmg = (int)Math.Round(rawDmg * (1 - reduction / 100));
        }
        if (!ignoreDodge && Rng.NextDouble() * 100 < Math.Max(0, defender.Secondary.GetValueOrDefault("闪避率", 0) - attacker.Secondary.GetValueOrDefault("命中率", 0)))
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
            // v4.2: 水系机制 & 眩晕
            int chuanliuA = 0, chuanliuB = 0;       // 川流之势: 下次受击减伤35%
            int shishuiOnB = 0, shishuiOnA = 0;     // 逝水印记层数(每层-5%物防)
            int maxShishui(string realm) => realm switch { "化神" => 99, "元婴" => 5, _ => 3 };
            bool stunnedA = false, stunnedB = false;
            // v5.0: 太一道庭守一 & 符胆机制
            int shouyiA = 0, shouyiB = 0;
            int maxShouyi(string realm) => realm switch { "金丹" => 5, "筑基" => 4, "练气" => 3, _ => 5 };
            if (ca.Style == "taiyi") { shouyiA = 2; }
            if (cb.Style == "taiyi") { shouyiB = 2; }
            int fudanA = 0, fudanB = 0;
            int maxFudan(string realm) => realm switch { "金丹" => 5, "筑基" => 3, _ => 5 };
            if (cb.Style == "taiyi_fuxiu") { fudanB = 2; }
            if (cb.Style == "taiyi_fuxiu") { fudanB = 2; } // 眩晕: 跳过下回合
            int qiushuiA = 0, qiushuiB = 0;          // 秋水护盾剩余触发次数
            int qiushuiMax(string realm) => realm switch { "元婴" => 3, "金丹" => 2, "筑基" => 1, _ => 0 };
            if (ca.Style == "water_physical") qiushuiA = qiushuiMax(ca.Realm);
            if (cb.Style == "water_physical") qiushuiB = qiushuiMax(cb.Realm);
            if (cb.Style == "taiyi_fuxiu") { fudanB = 2; }


            int kuxingDefReduceA = 0, kuxingDefReduceB = 0;

            // v5.6: 玉清崖 雷劫印记机制（受击叠层→出手消耗→满层魂防-20%）
            int leijieA = 0, leijieB = 0;
            int maxLeijie(string realm) => realm switch { "筑基" => 3, "金丹" => 5, "元婴" => 5, "化神" => 5, "炼虚" => 5, _ => 3 };
            double leijiePerStack(string realm) => realm switch { "筑基" => 0.15, "金丹" => 0.18, "元婴" => 0.22, "化神" => 0.30, "炼虚" => 0.35, _ => 0.15 };
            // v5.7: 太虚观 玄感机制（debuff清除+神识强度+玄同免疫+HP恢复）
            int xuanganShenshiA = 0, xuanganShenshiB = 0;
            int xuantongA = 0, xuantongB = 0;
            double xuanganClearRate(string realm) => realm switch { "元婴" => 0.80, "金丹" => 0.50, "筑基" => 0.30, "练气" => 0.20, _ => 0.20 };
            int xuanganShenshiVal(string realm) => realm switch { "元婴" => 12, "金丹" => 8, "筑基" => 5, "练气" => 3, _ => 3 };
            int xuanganXuantongDur(string realm) => realm switch { "元婴" => 2, "金丹" => 1, _ => 0 };
            bool xuanganCanHeal(string realm) => realm switch { "元婴" => true, "金丹" => true, "筑基" => true, _ => false };
            if (ca.Style == "taixu_xuangan") xuanganShenshiA = xuanganShenshiVal(ca.Realm);
            if (cb.Style == "taixu_xuangan") xuanganShenshiB = xuanganShenshiVal(cb.Realm);            // v5.8: 苦行剑典 血剑气机制
            double kuxingMult(string realm) => realm switch { "化神" => 3.5, "元婴" => 2.8, "金丹" => 2.2, "筑基" => 1.8, _ => 1.5 };
            double kuxingHpCostRate(string realm) => realm switch { "化神" => 0.25, "元婴" => 0.20, "金丹" => 0.20, "筑基" => 0.15, _ => 0.10 };
            bool kuxingHasRecover(string realm) => realm switch { "化神" => true, "元婴" => true, "金丹" => true, _ => false };
            bool kuxingHasDuanLong(string realm) => realm switch { "化神" => true, "元婴" => true, "金丹" => true, "筑基" => true, _ => false };
            // v5.9: 含弘光大典 机制
            double hanhongPhysDefBonus(string realm) => realm switch { "化神" => 0.30, "元婴" => 0.25, "金丹" => 0.20, "筑基" => 0.15, "练气" => 0.10, _ => 0.10 };
            double hanhongZaiwuCap(string realm) => realm switch { "化神" => 0.40, "元婴" => 0.30, _ => 0.20 };
            double hanhongCounterMult(string realm) => realm switch { "化神" => 1.30, "元婴" => 1.15, _ => 1.0 };
            // v5.9: 万物不迁法 机制 (不迁:受大伤害减半; 物理抗性)
            int buqianMaxTriggers(string realm) => realm switch { "元婴" => 5, "金丹" => 3, _ => 0 };
            int buqianA = buqianMaxTriggers(ca.Realm), buqianB = buqianMaxTriggers(cb.Realm);
            double buqianPhysResist(string realm) => realm switch { "元婴" => 10, "金丹" => 5, _ => 0 };
            // v5.9: 不真自虚法 机制 (虚化:伤害概率归零+反弹)
            double buzhenVoidRate(string realm) => realm switch { "化神" => 0.40, "元婴" => 0.25, _ => 0 };
            bool buzhenFirstVoidA = true, buzhenFirstVoidB = true; // 化神首虚必中
            // v5.9: 心无性有法 机制 (神识优势:命中/神攻/无法闪避)
            int xinwuShenshiBonus(string realm) => realm switch { "金丹" => 8, "筑基" => 5, _ => 0 };
            int xinwuShenshiA = ca.GongFaName == "心无性有法" ? xinwuShenshiBonus(ca.Realm) : 0;
            int xinwuShenshiB = cb.GongFaName == "心无性有法" ? xinwuShenshiBonus(cb.Realm) : 0;
            // v5.9: 元素克制表 (含弘光大典 克制增伤用)
            string gongFaElement(string name) => name switch {
                "含弘光大典" => "土", "白屋青云录" => "土", "混元同尘典" => "土", "绳墨正法录" => "土",
                "疾雷破山经" => "金", "九霄雷劫录" => "金", "雷池淬体功" => "金", "苦行剑典" => "金",
                "抱元守一经" => "火", "云篆度人经" => "火",
                "不真自虚法" => "水", "万物不迁法" => "水", "心无性有法" => "水", "南华玄感录" => "水",
                "秋水游心经" => "水",
                _ => ""
            };
            bool isElementCounter(string atkElem, string defElem) => (atkElem, defElem) switch {
                ("土", "水") => true, ("水", "火") => true, ("火", "金") => true, ("金", "土") => true, _ => false
            };
            // v5.9: 混元同尘典 过载机制 (元婴+)
            double tongchenOverloadRate(string realm) => realm switch { "炼虚" => 0.05, "化神" => 0.25, "元婴" => 0.15, _ => 0 };
            double tongchenOverloadMult(string realm) => realm switch { "炼虚" => 3.5, "化神" => 2.5, "元婴" => 2.0, _ => 1.0 };
            // v5.9: 白屋青云录 元素切换增伤 (金丹+)
            double baiwuSwitchDmg = 0; // A方累积切换增伤
            double baiwuSwitchDmgB = 0; // B方累积切换增伤
            double baiwuSwitchMax(string realm) => realm switch { "元婴" => 0.30, "金丹" => 0.25, _ => 0 };
            int turns = 0;
            while (hpA > 0 && hpB > 0)
            {
                turns++;
                // 秋水回血: water_physical每回合恢复1.5%最大HP
                if (ca.Style == "water_physical") hpA = Math.Min(ca.Primary["HP"], hpA + (int)(ca.Primary["HP"] * 0.015));
                if (cb.Style == "water_physical") hpB = Math.Min(cb.Primary["HP"], hpB + (int)(cb.Primary["HP"] * 0.015));
                if (artCdA > 0) artCdA--;
                if (artCdB > 0) artCdB--;
                if (divineCdA > 0) divineCdA--;
                if (divineCdB > 0) divineCdB--;

                if (ctA <= ctB)
                {
                    // 眩晕: 跳过回合, CT归零, 眩晕标记被消耗

                    // 玄感: 回合开始 debuff清除
                    if (ca.Style == "taixu_xuangan" && stunnedA && Rng.NextDouble() < xuanganClearRate(ca.Realm)) { stunnedA = false; if (xuanganCanHeal(ca.Realm)) hpA = Math.Min(ca.Primary["HP"], hpA + (int)(ca.Primary["HP"] * 0.05)); xuantongA = xuanganXuantongDur(ca.Realm); }                    if (stunnedA) { stunnedA = false; ctA += sA; continue; }
                    // AI决策: 神通 > 术法 > 平A
                    string atkType; double mult, defPen; int atk, def; double resist;
                    bool waterSkillA = false;                    bool kuxingUsedA = false; int kuxingHpRecoverA = 0; bool kuxingIgnoreBlockA = false;
                    int artMPCostA = (ca.Style == "taiyi_fuxiu" && fudanA == maxFudan(ca.Realm)) ? 0 : (xuantongA > 0 ? (int)(ca.ArtMPCost * 0.70) : ca.ArtMPCost);
                    if (ca.DivineName != "" && divineCdA == 0)
                    {
                        atkType = ca.DivineType; mult = ca.DivineMult; defPen = ca.DivineDefPen;
                        divineCdA = ca.DivineCooldown;
                        waterSkillA = ca.Style == "water_physical";
                        if (waterSkillA) { shishuiOnB = Math.Min(shishuiOnB + 1, maxShishui(ca.Realm)); chuanliuA = 1; }
                    }
                    else if (ca.Style == "yuqing_kuxing" && hpA > 2 && hpB > 0)
                    {
                        kuxingUsedA = true;
                        int hpCost = Math.Min(hpA - 1, (int)(hpA * kuxingHpCostRate(ca.Realm)));
                        hpA -= hpCost;
                        atkType = "物理"; mult = kuxingMult(ca.Realm); defPen = 0;
                        if (kuxingHasDuanLong(ca.Realm) && hpB < cb.Primary["HP"] * 0.50) mult *= 1.3;
                        if (kuxingHasRecover(ca.Realm)) kuxingHpRecoverA = (int)(hpCost * 0.30);
                        kuxingIgnoreBlockA = true;
                        kuxingDefReduceA = (int)(ca.Primary["肉防"] * 0.30);
                    }                    else if (mpA >= artMPCostA && artCdA == 0)
                    {
                        atkType = ca.ArtType; mult = ca.ArtMult; defPen = 0;
                        mpA -= artMPCostA; artCdA = ca.ArtCooldown;
                        waterSkillA = ca.Style == "water_physical";


                    }
                    else
                    {
                    bool isPhysicalA = ca.Style == "physical" || ca.Style == "water_physical" || ca.Style == "yuqing_leijie" || ca.Style == "yuqing_kuxing"; atkType = isPhysicalA ? "物理" : "神魂"; mult = 1.0; defPen = 0;
                    }
                    // 守一: 神魂攻击加成 (金丹致虚篇: 每层+5%)
                    if (ca.Style == "taiyi" && atkType == "神魂") mult *= (1 + shouyiA * 0.05);
                    // 符胆: 符箓术法效果加成(消耗全部) (按境界:筑基12%/金丹15%/元婴18%/化神22%)
                    bool fudanMaxA = false;
                    if (ca.Style == "taiyi_fuxiu") 
                    { 
                        fudanMaxA = fudanA == maxFudan(ca.Realm);
                        double fdpA = ca.Realm switch { "筑基" => 0.12, "金丹" => 0.15, "元婴" => 0.18, "化神" => 0.22, _ => 0.15 };
                        mult *= (1 + fudanA * fdpA); 
                        fudanA = ca.Realm == "化神" ? 2 : 0;
                    }
                    // 雷劫印记: 物理出手消耗全部印记, 每层伤害加成
                    if (ca.Style == "yuqing_leijie" && atkType == "物理" && leijieA > 0) { mult *= (1 + leijieA * leijiePerStack(ca.Realm)); leijieA = 0; }                    atk = atkType == "物理" ? ca.Primary["肉攻"] : ca.Primary["神攻"];                    if (ca.Style == "taixu_xuangan") atk += xuanganShenshiA;
                    // 云篆篇: 符胆满层时神识+30%加成
                    if (ca.Style == "taiyi_fuxiu" && fudanMaxA) atk += (int)(ca.Primary["神识"] * 0.30);
                    // 逝水印记: 降低目标物防 (每层-5%)
                    int rawDef = atkType == "物理" ? cb.Primary["肉防"] : cb.Primary["神防"];
                    // 血剑气防御惩罚: B若使用了血剑气则物防降低
                    if (kuxingDefReduceB > 0 && atkType == "物理") { rawDef -= kuxingDefReduceB; kuxingDefReduceB = 0; }
                    // v5.9: 含弘光大典 肉身防御+载物被动 (B防御方)
                    if (cb.GongFaName == "含弘光大典" && atkType == "物理") { rawDef = (int)(rawDef * (1 + hanhongPhysDefBonus(cb.Realm))); }
                    if (cb.GongFaName == "含弘光大典") { double hpLostB = 1.0 - (double)hpB / cb.Primary["HP"]; double zaiwuB = Math.Min(hpLostB * 20 / 100, hanhongZaiwuCap(cb.Realm)); rawDef = (int)(rawDef * (1 + zaiwuB)); }
                    def = atkType == "物理" ? (int)(rawDef * (1 - shishuiOnB * 0.05)) : rawDef;
                    // 雷劫印记满层: 魂防-20%
                    if (cb.Style == "yuqing_leijie" && leijieB == maxLeijie(cb.Realm) && atkType == "神魂") def = (int)(rawDef * 0.80);                    resist = atkType == "物理" ? cb.Secondary.GetValueOrDefault("物抗率", 0) : cb.Secondary.GetValueOrDefault("魂抗率", 0);
                    // v5.9: 万物不迁法 物理抗性 (B防御方)
                    if (cb.GongFaName == "万物不迁法" && atkType == "物理") resist += buqianPhysResist(cb.Realm);
                    // 守一满层: 神魂防御+15% (金丹致虚篇)
                    if (cb.Style == "taiyi" && shouyiB == maxShouyi(cb.Realm) && atkType == "神魂") resist += 15;
                    // 天书篇: 符胆满层时无视30%魂防
                    if (ca.Style == "taiyi_fuxiu" && fudanMaxA && atkType == "神魂") defPen = 30;
                    // v5.9: 白屋青云录 元素切换增伤 (A攻击方, 金丹+)
                    if (ca.GongFaName == "白屋青云录" && ca.Realm is "金丹" or "元婴") { baiwuSwitchDmg = Math.Min(baiwuSwitchDmg + 0.05, baiwuSwitchMax(ca.Realm)); mult *= (1 + baiwuSwitchDmg); }
                    // v5.9: 白屋青云录 寒门傲骨 (A攻击方, HP<阈值时暴伤+50%)
                    if (ca.GongFaName == "白屋青云录") { double baiwuHpThreshA = ca.Realm switch { "元婴" => 0.40, "金丹" => 0.35, "筑基" => 0.30, _ => 0.30 }; if ((double)hpA / ca.Primary["HP"] < baiwuHpThreshA) mult *= 1.50; }
                    // v5.9: 混元同尘典 同尘一击+过载 (A攻击方, 元婴+)
                    if (ca.GongFaName == "混元同尘典" && ca.Realm is "元婴" or "化神" or "炼虚") {
                        mult *= tongchenOverloadMult(ca.Realm);
                        if (Rng.NextDouble() < tongchenOverloadRate(ca.Realm)) { int selfDmg = (int)(hpA * 0.15); hpA -= selfDmg; }
                    }
                    // v5.9: 含弘光大典 属性克制增伤 (A攻击方, 元婴+)
                    if (ca.GongFaName == "含弘光大典" && (ca.Realm == "元婴" || ca.Realm == "化神")) {
                        string elemA = gongFaElement(ca.GongFaName); string elemB = gongFaElement(cb.GongFaName);
                        if (isElementCounter(elemA, elemB)) mult *= hanhongCounterMult(ca.Realm);
                    }
                    // v5.9: 心无性有法 神识优势:神攻+无法闪避 (A攻击方)
                    int effShenshiA = ca.Primary["神识"] + xinwuShenshiA;
                    int effShenshiB = cb.Primary["神识"] + xinwuShenshiB;
                    bool xinwuAdvA = ca.GongFaName == "心无性有法" && effShenshiA > effShenshiB;
                    if (xinwuAdvA && atkType == "神魂" && ca.Realm == "金丹") { atk = (int)(atk * 1.25); }
                    int dmg = Dmg(atk, def, resist, defPen, mult);
                    // 远程惩罚: 对方上轮使用远程术法/神通, 本轮需拉近距离
                    dmg = (int)(dmg * rangePenaltyA); rangePenaltyA = 1.0;
                    dmg = ApplyDefenses(dmg, ca, cb, atkType, ignoreDodge: fudanMaxA || xinwuAdvA, ignoreBlock: kuxingIgnoreBlockA);
                    // v5.9: 不真自虚法 虚化 (B防御方:伤害概率归零)
                    if (cb.GongFaName == "不真自虚法" && dmg > 0) {
                        double voidRateB = buzhenVoidRate(cb.Realm);
                        if (buzhenFirstVoidB) { voidRateB = 1.0; buzhenFirstVoidB = false; }
                        if (Rng.NextDouble() < voidRateB) {
                            if (cb.Realm == "化神") { int reflectDmg = (int)(dmg * 0.30); dmg = 0; hpA -= reflectDmg; }
                            else dmg = 0;
                        }
                    }
                    // v5.9: 万物不迁法 不迁 (B防御方:受30%HP以上伤害减半)
                    if (cb.GongFaName == "万物不迁法" && buqianB > 0 && dmg > cb.Primary["HP"] * 0.30) { int origDmg = dmg; dmg /= 2; buqianB--; if (cb.Realm == "元婴") hpA -= (int)(origDmg * 0.50); }
                    // 川流之势: B若持有则减伤35%并消耗
                    if (chuanliuB > 0 && dmg > 0) { dmg = (int)(dmg * 0.65); chuanliuB = 0; }
                    // 守一减伤: 受击消耗1层减伤20%
                    if (cb.Style == "taiyi" && shouyiB > 0 && dmg > 0) { dmg = (int)(dmg * 0.80); shouyiB--; }
                    // 远程优势: 术法/神通出手后对方需要拉近距离
                    bool isRanged = ca.Style == "magic";
                    if (isRanged) rangePenaltyB = 0.35;
                    hpB -= dmg;
                    // 川流劲眩晕: 10%概率, 玄同免疫 (v4.2补全)
                    if (waterSkillA && dmg > 0 && Rng.NextDouble() < 0.10) { if (xuantongB == 0) stunnedB = true; }
                    // 雷劫印记: 受伤叠1层
                    if (cb.Style == "yuqing_leijie" && dmg > 0) leijieB = Math.Min(leijieB + 1, maxLeijie(cb.Realm));
                    // 血剑气: HP恢复
                    if (kuxingUsedA) hpA = Math.Min(ca.Primary["HP"], hpA + kuxingHpRecoverA);
                    // 秋水护盾: B濒死触发 (HP<30%且还有触发次数)
                    if (qiushuiB > 0 && hpB > 0 && hpB < cb.Primary["HP"] * 0.30)
                    { hpB += (int)(cb.Primary["HP"] * 0.15); qiushuiB--; }


                    // 守一&符胆: 回合结束印记+1
                    if (ca.Style == "taiyi") shouyiA = Math.Min(shouyiA + 1, maxShouyi(ca.Realm));
                    if (ca.Style == "taiyi_fuxiu") fudanA = Math.Min(fudanA + 1, maxFudan(ca.Realm));
                    if (xuantongA > 0) xuantongA--;                    ctA += sA;
                }
                else
                {

                    // 玄感: 回合开始 debuff清除
                    if (cb.Style == "taixu_xuangan" && stunnedB && Rng.NextDouble() < xuanganClearRate(cb.Realm)) { stunnedB = false; if (xuanganCanHeal(cb.Realm)) hpB = Math.Min(cb.Primary["HP"], hpB + (int)(cb.Primary["HP"] * 0.05)); xuantongB = xuanganXuantongDur(cb.Realm); }                    if (stunnedB) { stunnedB = false; ctB += sB; continue; }
                    string atkType; double mult, defPen; int atk, def; double resist;
                    bool waterSkillB = false;                    bool kuxingUsedB = false; int kuxingHpRecoverB = 0; bool kuxingIgnoreBlockB = false;
                    int artMPCostB = (cb.Style == "taiyi_fuxiu" && fudanB == maxFudan(cb.Realm)) ? 0 : (xuantongB > 0 ? (int)(cb.ArtMPCost * 0.70) : cb.ArtMPCost);
                    if (cb.DivineName != "" && divineCdB == 0)
                    {
                        atkType = cb.DivineType; mult = cb.DivineMult; defPen = cb.DivineDefPen;
                        divineCdB = cb.DivineCooldown;
                        waterSkillB = cb.Style == "water_physical";
                        if (waterSkillB) { shishuiOnA = Math.Min(shishuiOnA + 1, maxShishui(cb.Realm)); chuanliuB = 1; }
                    }
                    else if (cb.Style == "yuqing_kuxing" && hpB > 2 && hpA > 0)
                    {
                        kuxingUsedB = true;
                        int hpCost = Math.Min(hpB - 1, (int)(hpB * kuxingHpCostRate(cb.Realm)));
                        hpB -= hpCost;
                        atkType = "物理"; mult = kuxingMult(cb.Realm); defPen = 0;
                        if (kuxingHasDuanLong(cb.Realm) && hpA < ca.Primary["HP"] * 0.50) mult *= 1.3;
                        if (kuxingHasRecover(cb.Realm)) kuxingHpRecoverB = (int)(hpCost * 0.30);
                        kuxingIgnoreBlockB = true;
                        kuxingDefReduceB = (int)(cb.Primary["肉防"] * 0.30);
                    }                    else if (mpB >= artMPCostB && artCdB == 0)
                    {
                        atkType = cb.ArtType; mult = cb.ArtMult; defPen = 0;
                        mpB -= artMPCostB; artCdB = cb.ArtCooldown;
                        waterSkillB = cb.Style == "water_physical";

                    }
                    else
                    {
                    bool isPhysicalB = cb.Style == "physical" || cb.Style == "water_physical" || cb.Style == "yuqing_leijie" || cb.Style == "yuqing_kuxing"; atkType = isPhysicalB ? "物理" : "神魂"; mult = 1.0; defPen = 0;
                    }
                    if (cb.Style == "taiyi" && atkType == "神魂") mult *= (1 + shouyiB * 0.05);
                    // 符胆: 符箓术法效果加成(消耗全部) (按境界:筑基12%/金丹15%/元婴18%/化神22%)
                    bool fudanMaxB = false;
                    if (cb.Style == "taiyi_fuxiu") 
                    { 
                        fudanMaxB = fudanB == maxFudan(cb.Realm);
                        double fdpB = cb.Realm switch { "筑基" => 0.12, "金丹" => 0.15, "元婴" => 0.18, "化神" => 0.22, _ => 0.15 };
                        mult *= (1 + fudanB * fdpB); 
                        fudanB = cb.Realm == "化神" ? 2 : 0;
                    }
                    // 雷劫印记: 物理出手消耗全部印记（B方向）
                    if (cb.Style == "yuqing_leijie" && atkType == "物理" && leijieB > 0) { mult *= (1 + leijieB * leijiePerStack(cb.Realm)); leijieB = 0; }                    atk = atkType == "物理" ? cb.Primary["肉攻"] : cb.Primary["神攻"];                    if (cb.Style == "taixu_xuangan") atk += xuanganShenshiB;
                    // 云篆篇: 符胆满层时神识+30%加成
                    if (cb.Style == "taiyi_fuxiu" && fudanMaxB) atk += (int)(cb.Primary["神识"] * 0.30);
                    int rawDefB = atkType == "物理" ? ca.Primary["肉防"] : ca.Primary["神防"];
                    // 血剑气防御惩罚: A若使用了血剑气则物防降低
                    if (kuxingDefReduceA > 0 && atkType == "物理") { rawDefB -= kuxingDefReduceA; kuxingDefReduceA = 0; }
                    // v5.9: 含弘光大典 肉身防御+载物被动 (A防御方)
                    if (ca.GongFaName == "含弘光大典" && atkType == "物理") { rawDefB = (int)(rawDefB * (1 + hanhongPhysDefBonus(ca.Realm))); }
                    if (ca.GongFaName == "含弘光大典") { double hpLostA = 1.0 - (double)hpA / ca.Primary["HP"]; double zaiwuA = Math.Min(hpLostA * 20 / 100, hanhongZaiwuCap(ca.Realm)); rawDefB = (int)(rawDefB * (1 + zaiwuA)); }
                    def = atkType == "物理" ? (int)(rawDefB * (1 - shishuiOnA * 0.05)) : rawDefB;
                    resist = atkType == "物理" ? ca.Secondary.GetValueOrDefault("物抗率", 0) : ca.Secondary.GetValueOrDefault("魂抗率", 0);
                    // v5.9: 万物不迁法 物理抗性 (A防御方)
                    if (ca.GongFaName == "万物不迁法" && atkType == "物理") resist += buqianPhysResist(ca.Realm);
                    if (ca.Style == "taiyi" && shouyiA == maxShouyi(ca.Realm) && atkType == "神魂") resist += 15;
                    // 天书篇: 符胆满层时无视30%魂防
                    if (cb.Style == "taiyi_fuxiu" && fudanMaxB && atkType == "神魂") defPen = 30;
                    // v5.9: 白屋青云录 元素切换增伤 (B攻击方, 金丹+)
                    if (cb.GongFaName == "白屋青云录" && cb.Realm is "金丹" or "元婴") { baiwuSwitchDmgB = Math.Min(baiwuSwitchDmgB + 0.05, baiwuSwitchMax(cb.Realm)); mult *= (1 + baiwuSwitchDmgB); }
                    // v5.9: 白屋青云录 寒门傲骨 (B攻击方, HP<阈值时暴伤+50%)
                    if (cb.GongFaName == "白屋青云录") { double baiwuHpThreshB = cb.Realm switch { "元婴" => 0.40, "金丹" => 0.35, "筑基" => 0.30, _ => 0.30 }; if ((double)hpB / cb.Primary["HP"] < baiwuHpThreshB) mult *= 1.50; }
                    // v5.9: 混元同尘典 同尘一击+过载 (B攻击方, 元婴+)
                    if (cb.GongFaName == "混元同尘典" && cb.Realm is "元婴" or "化神" or "炼虚") {
                        mult *= tongchenOverloadMult(cb.Realm);
                        if (Rng.NextDouble() < tongchenOverloadRate(cb.Realm)) { int selfDmg = (int)(hpB * 0.15); hpB -= selfDmg; }
                    }
                    // v5.9: 含弘光大典 属性克制增伤 (B攻击方, 元婴+)
                    if (cb.GongFaName == "含弘光大典" && (cb.Realm == "元婴" || cb.Realm == "化神")) {
                        string elemB2 = gongFaElement(cb.GongFaName); string elemA2 = gongFaElement(ca.GongFaName);
                        if (isElementCounter(elemB2, elemA2)) mult *= hanhongCounterMult(cb.Realm);
                    }
                    // v5.9: 心无性有法 神识优势 (B攻击方)
                    int effShenshiB2 = cb.Primary["神识"] + xinwuShenshiB;
                    int effShenshiA2 = ca.Primary["神识"] + xinwuShenshiA;
                    bool xinwuAdvB = cb.GongFaName == "心无性有法" && effShenshiB2 > effShenshiA2;
                    if (xinwuAdvB && atkType == "神魂" && cb.Realm == "金丹") { atk = (int)(atk * 1.25); }
                    int dmg = Dmg(atk, def, resist, defPen, mult);
                    // 远程惩罚: 对方上轮使用远程术法/神通, 本轮需拉近距离
                    dmg = (int)(dmg * rangePenaltyB); rangePenaltyB = 1.0;
                    dmg = ApplyDefenses(dmg, cb, ca, atkType, ignoreDodge: fudanMaxB || xinwuAdvB, ignoreBlock: kuxingIgnoreBlockB);
                    // v5.9: 不真自虚法 虚化 (A防御方:伤害概率归零)
                    if (ca.GongFaName == "不真自虚法" && dmg > 0) {
                        double voidRateA = buzhenVoidRate(ca.Realm);
                        if (buzhenFirstVoidA) { voidRateA = 1.0; buzhenFirstVoidA = false; }
                        if (Rng.NextDouble() < voidRateA) {
                            if (ca.Realm == "化神") { int reflectDmg = (int)(dmg * 0.30); dmg = 0; hpB -= reflectDmg; }
                            else dmg = 0;
                        }
                    }
                    // v5.9: 万物不迁法 不迁 (A防御方:受30%HP以上伤害减半)
                    if (ca.GongFaName == "万物不迁法" && buqianA > 0 && dmg > ca.Primary["HP"] * 0.30) { int origDmg2 = dmg; dmg /= 2; buqianA--; if (ca.Realm == "元婴") hpB -= (int)(origDmg2 * 0.50); }
                    if (chuanliuA > 0 && dmg > 0) { dmg = (int)(dmg * 0.65); chuanliuA = 0; }
                    if (ca.Style == "taiyi" && shouyiA > 0 && dmg > 0) { dmg = (int)(dmg * 0.80); shouyiA--; }
                    // 远程优势: B使用术法/神通后A需要拉近距离
                    bool bRanged = cb.Style == "magic";
                    if (bRanged) rangePenaltyA = 0.35;
                    hpA -= dmg;
                    // 血剑气: HP恢复
                    if (kuxingUsedB) hpB = Math.Min(cb.Primary["HP"], hpB + kuxingHpRecoverB);
                    if (qiushuiA > 0 && hpA > 0 && hpA < ca.Primary["HP"] * 0.30)
                    { hpA += (int)(ca.Primary["HP"] * 0.15); qiushuiA--; }

                    if (cb.Style == "taiyi") shouyiB = Math.Min(shouyiB + 1, maxShouyi(cb.Realm));
                    if (cb.Style == "taiyi_fuxiu") fudanB = Math.Min(fudanB + 1, maxFudan(cb.Realm));
                    if (xuantongB > 0) xuantongB--;                    ctB += sB;
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
    record BuildDef(string Name, string Desc, Dictionary<string, int> Innate, string Style, string GongFaName = "", Dictionary<string, double> Weights = null);

    static void Main()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const string TECH = "上品", SPIRIT = "中品";
        const int SEEDS = 20, SIM = 2000;

        var buildDefs = new BuildDef[]
        {
            new("物·纯战", "资质3根骨25", new() { ["根骨"]=25,["魂魄"]=8,["神识"]=5,["资质"]=3,["气运"]=5 }, "physical", "疾雷破山经"),
            new("物·均衡", "资质11根骨20", new() { ["根骨"]=20,["魂魄"]=8,["神识"]=8,["资质"]=11,["气运"]=8 }, "physical", "含弘光大典"),
            new("物·修炼", "资质19根骨15", new() { ["根骨"]=15,["魂魄"]=8,["神识"]=8,["资质"]=19,["气运"]=8 }, "physical", "白屋青云录"),
            new("肉盾型",  "根骨43极限", new() { ["根骨"]=43,["魂魄"]=5,["神识"]=5,["资质"]=5,["气运"]=5 }, "physical", "混元同尘典"),
            new("法·纯战", "资质3魂魄25", new() { ["根骨"]=5,["魂魄"]=25,["神识"]=8,["资质"]=3,["气运"]=5 }, "magic", "抱元守一经"),
            new("法·均衡", "资质11魂魄20", new() { ["根骨"]=8,["魂魄"]=20,["神识"]=8,["资质"]=11,["气运"]=8 }, "magic", "万物不迁法"),
            new("法·修炼", "资质19魂魄15", new() { ["根骨"]=8,["魂魄"]=15,["神识"]=8,["资质"]=19,["气运"]=8 }, "magic", "万物不迁法"),
            new("灵修型",  "魂魄43极限", new() { ["根骨"]=5,["魂魄"]=43,["神识"]=5,["资质"]=5,["气运"]=5 }, "magic", "万物不迁法"),
            new("水·散修", "资质18气运14", new() { ["根骨"]=10,["魂魄"]=9,["神识"]=9,["资质"]=18,["气运"]=14 }, "water_physical", "秋水游心经"),
            new("太一·法修", "资质14魂魄18", new() { ["根骨"]=6,["魂魄"]=18,["神识"]=10,["资质"]=14,["气运"]=8 }, "taiyi", "抱元守一经"),
            new("太一·符修", "神识18魂魄14", new() { ["根骨"]=5,["魂魄"]=14,["神识"]=18,["资质"]=12,["气运"]=10 }, "taiyi_fuxiu", "云篆度人经"),
            // v5.3: 太虚观（暗系神魂）
            new("太虚·魂修", "魂魄25神识14", new() { ["根骨"]=5,["魂魄"]=25,["神识"]=14,["资质"]=14,["气运"]=5 }, "taixu", "不真自虚法"),
            new("太虚·均衡", "魂魄18神识14", new() { ["根骨"]=8,["魂魄"]=18,["神识"]=14,["资质"]=12,["气运"]=5 }, "taixu", "万物不迁法"),
            new("太虚·宿慧", "资质16神识18", new() { ["根骨"]=5,["魂魄"]=12,["神识"]=18,["资质"]=16,["气运"]=8 }, "taixu", "心无性有法"),
            // v5.3: 玉清崖（雷剑双修）
            new("玉清·剑修", "根骨22神识18", new() { ["根骨"]=22,["魂魄"]=8,["神识"]=18,["资质"]=12,["气运"]=3 }, "yuqing", "疾雷破山经"),
            new("玉清·雷修", "根骨18魂魄12", new() { ["根骨"]=18,["魂魄"]=12,["神识"]=14,["资质"]=8,["气运"]=3 }, "yuqing", "疾雷破山经"),
            // v5.4: 玉清崖 BuildDef 补全
            new("玉清·雷劫", "根骨18神识18", new() { ["根骨"]=18,["魂魄"]=10,["神识"]=18,["资质"]=10,["气运"]=3 }, "yuqing_leijie", "九霄雷劫录"),
            new("玉清·苦行", "神识20资质14", new() { ["根骨"]=16,["魂魄"]=6,["神识"]=20,["资质"]=14,["气运"]=5 }, "yuqing_kuxing", "苦行剑典"),
            new("玉清·雷体", "根骨30神识12", new() { ["根骨"]=30,["魂魄"]=5,["神识"]=12,["资质"]=8,["气运"]=5 }, "yuqing", "雷池淬体功"),
            // 太虚观 / 混元山 BuildDef 补全
            new("太虚·玄感", "魂魄18神识14", new() { ["根骨"]=5,["魂魄"]=18,["神识"]=14,["资质"]=12,["气运"]=8 }, "taixu_xuangan", "南华玄感录"),
            new("混元·正法", "神识16根骨14", new() { ["根骨"]=14,["魂魄"]=12,["神识"]=16,["资质"]=10,["气运"]=8 }, "physical", "绳墨正法录"),
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
                var result = Cultivation.Simulate(bd.Innate, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName), seed * 100 + i, SPIRIT, TECH);
                var c = Character.Create(bd.Name, bd.Innate, bd.Style);
                c.ApplyGrowth(result.Realm, TECH, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName));
                c.GongFaName = bd.GongFaName;
                c.FinalizeStats(result.Realm, result.SubIdx, SPIRIT, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName));
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
                var result = Cultivation.Simulate(bd.Innate, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName), seed * 100 + i + 10000, SPIRIT, TECH, maxCycles: EARLY_CYCLES);
                var c = Character.Create(bd.Name, bd.Innate, bd.Style);
                c.ApplyGrowth(result.Realm, TECH, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName));
                c.GongFaName = bd.GongFaName;
                c.FinalizeStats(result.Realm, result.SubIdx, SPIRIT, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName));
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
