using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim;

static class GameData
{
    public static readonly Dictionary<string, int> Sublevels = new()
    {
        ["凡人"] = 1, ["练气"] = 9, ["筑基"] = 5, ["金丹"] = 3
    };
    public static readonly string[] RealmOrder = ["凡人", "练气", "筑基", "金丹"];
    public static readonly Dictionary<string, int> ExtensionSublevels = new()
    {
        ["元婴"] = 4, ["化神"] = 4
    };
    public static readonly string[] ExtensionRealmOrder = ["元婴", "化神"];
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
        ["元婴"] = new(15000, 4500, 2200, 2200, 1800, 1800, 600, 6, 18),
        ["化神"] = new(45000, 13000, 6500, 6500, 5500, 5500, 1500, 7, 25),
    };
    public record RealmFactor(double HP, double MP, double 攻, double 防, double 反应, double 神识);
    public static readonly Dictionary<string, RealmFactor> Factor = new()
    {
        ["凡人"] = new(4, 0.5, 1, 0.8, 0.6, 0.15),
        ["练气"] = new(8, 2, 3, 2, 0.75, 0.20),
        ["筑基"] = new(9, 7, 6.5, 4, 1.5, 0.25),
        ["金丹"] = new(22, 14, 12, 8, 3.0, 0.35),
        ["元婴"] = new(25, 16, 15, 10, 4, 0.18),
        ["化神"] = new(50, 25, 28, 18, 7, 0.20),
    };
    public record SubGrowth(double HP, double MP, double 肉攻, double 神攻, double 肉防, double 神防, double 反应, double 移力, double 神识);
    public static readonly Dictionary<string, SubGrowth> SubGrowthBase = new()
    {
        ["练气"] = new(8, 3, 4, 4, 3, 3, 2, 0.2, 0.3),
        ["筑基"] = new(120, 35, 30, 30, 25, 25, 12, 0.5, 1.0),
        ["金丹"] = new(1800, 700, 350, 350, 280, 280, 90, 0.8, 4.0),
        ["元婴"] = new(1300, 1000, 450, 420, 380, 340, 110, 1.0, 2.3),
        ["化神"] = new(4000, 3500, 1400, 1300, 1100, 1000, 280, 1.2, 2.8),
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
    // 金丹成丹兼容层 (TQ-013B)
    // ═══════════════════════════════════════
    public record GoldenCoreProfile(
        string FormedState,
        string DanJiType,
        string OccupancyState,
        string DanName,
        string DanNature,
        string LegacyGrade,
        string TargetBranch,
        string TargetSeat,
        string SeatName,
        string DanPivot,
        double StabilityMultiplier,
        double ArtAffinityMultiplier);

    public record GoldenCoreSeatProfile(
        string TargetBranch,
        string TargetSeat,
        string SeatName,
        string DanPivot);

    // 历史兼容：旧一至九品只用于调试回看，不再钳制道基，也不再驱动主输出或战斗强度。
    public static string LegacyGoldenCoreGradeFromScore(int score) => score switch
    {
        >= 120 => "一品", >= 105 => "二品", >= 90 => "三品", >= 75 => "四品",
        >= 60  => "五品", >= 48  => "六品", >= 36 => "七品", >= 25 => "八品",
        >= 15  => "九品", _      => ""
    };

    public static GoldenCoreProfile ResolveGoldenCoreProfile(int score, string dfQuality, Dictionary<string, double> weights)
    {
        if (score < 15)
            return new("未成丹", "", "未成丹", "", "", "", "", "", "", "", 1.0, 1.0);

        var (danName, danNature) = ResolveGoldenCoreTheme(weights);
        var seat = ResolveGoldenCoreSeat(weights);
        string legacyGrade = LegacyGoldenCoreGradeFromScore(score);
        bool hasFoundation = dfQuality != "无道基";

        if (!hasFoundation || score < 60)
            return new("成丹", "暂寄丹籍", "暂寄", danName, danNature, legacyGrade, seat.TargetBranch, seat.TargetSeat, seat.SeatName, seat.DanPivot, 0.92, 0.95);

        if (score >= 90)
            return new("成丹", "自然丹籍", "稳定占据", danName, danNature, legacyGrade, seat.TargetBranch, seat.TargetSeat, seat.SeatName, seat.DanPivot, 1.08, 1.10);

        return new("成丹", "敕封丹籍", "受敕承位", danName, danNature, legacyGrade, seat.TargetBranch, seat.TargetSeat, seat.SeatName, seat.DanPivot, 1.0, 1.0);
    }

    public static GoldenCoreSeatProfile ResolveGoldenCoreSeat(Dictionary<string, double> weights)
    {
        var top = weights.OrderByDescending(kv => kv.Value).First().Key;
        return top switch
        {
            "根骨" => new("土", "source", "土·源位（安忍地）", "承接范围、排异规则与过载症状"),
            "魂魄" => new("火", "transform", "火·化位（周天焰）", "传递载体、放大对象与反噬路径"),
            "神识" => new("识", "domain", "识·界位（共见证）", "存证方式、遗忘机制与公开层级"),
            "资质" => new("木", "source", "木·源位（报春根）", "种子来源、续生媒介与反哺比例"),
            "气运" => new("水", "domain", "水·界位（永动泉）", "余裕规模、启封条件与低潮策略"),
            _ => new("金", "source", "金·源位（分真鉴）", "标准来源、容错尺度与待定区")
        };
    }

    static (string danName, string danNature) ResolveGoldenCoreTheme(Dictionary<string, double> weights)
    {
        var top = weights.OrderByDescending(kv => kv.Value).First().Key;
        return top switch
        {
            "根骨" => ("坤岳丹", "土"),
            "魂魄" => ("烛魂丹", "火"),
            "神识" => ("星识丹", "星"),
            "资质" => ("青华丹", "木"),
            "气运" => ("沧流丹", "水"),
            _ => ("素真丹", "金")
        };
    }

    public static readonly Dictionary<string, int> GCDFContinue = new()
    {
        ["天品"] = 60, ["地品"] = 40, ["玄品"] = 25, ["黄品"] = 10
    };
    public static readonly Dictionary<string, int> GCTreasure = new()
    {
        ["下品"] = 5, ["中品"] = 10, ["上品"] = 20, ["极品"] = 30, [""] = 0
    };
    public const double GCMinMP = 1200.0;
    public static string StageName(string realm, int subIdx) => realm switch
    {
        "筑基" => subIdx switch
        {
            0 => "筑基初期",
            1 => "筑基中期",
            2 => "筑基后期",
            3 => "紫府初开",
            4 => "紫府圆满",
            _ => $"筑基{subIdx}"
        },
        "金丹" => subIdx switch
        {
            0 => "初结金丹",
            1 => "温养金丹",
            2 => "金丹圆满",
            _ => $"金丹{subIdx}"
        },
        "练气" => $"练气{subIdx + 1}层",
        _ => realm
    };

public static readonly (string realm, int subIdx, int cpp)[] Milestones = new (string, int, int)[]
    {
        ("练气", 0, 10), ("练气", 1, 22), ("练气", 2, 36), ("练气", 3, 52),
        ("练气", 4, 70), ("练气", 5, 90), ("练气", 6, 112), ("练气", 7, 136), ("练气", 8, 162),
        ("筑基", 0, 200), ("筑基", 1, 250), ("筑基", 2, 310), ("筑基", 3, 390), ("筑基", 4, 490),
        ("金丹", 0, 620), ("金丹", 1, 800), ("金丹", 2, 1020),
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
    public record ArtConfig(string Name, string Type, double Mult, int MPCost, int Cooldown, string Element = "");
    public record DivineConfig(string Name, string Type, double Mult, double DefPen, int Cooldown, string Element = "");
    public readonly record struct ElementMatch(double DamageMultiplier, double CritRateBonus, double CritDamageBonus);
    public static readonly ArtConfig PhysicalArt = new("裂石拳", "物理", 1.3, 20, 3, "土");

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

    public static readonly Dictionary<string, string> GongFaElements = new()
    {
        ["抱元守一经"] = "水",
        ["云篆度人经"] = "风",
        ["秋水游心经"] = "水",
        ["九霄雷劫录"] = "雷",
        ["苦行剑典"] = "金",
        ["疾雷破山经"] = "雷",
        ["雷池淬体功"] = "雷",
        ["含弘光大典"] = "土",
        ["白屋青云录"] = "土",
        ["混元同尘典"] = "土",
        ["绳墨正法录"] = "土",
        ["万物不迁法"] = "暗",
        ["不真自虚法"] = "暗",
        ["南华玄感录"] = "暗",
        ["心无性有法"] = "暗",
        ["南华大梦书"] = "水",
        ["南华阐衍典"] = "风",
        ["大洞炼真经"] = "土",
        ["太易山藏经"] = "土",
        ["太易玄义笺"] = "土",
        ["玄牝道藏"] = "混沌",
        ["空无般若经"] = "水",
        ["见素抱朴经"] = "木",
        ["通神三玄礼录"] = "金",
    };

    public static string GetGongFaElement(string gongFaName)
    {
        if (string.IsNullOrEmpty(gongFaName)) return "";
        return GongFaElements.TryGetValue(gongFaName, out var element) ? element : "";
    }

    public static double ElementDamageMultiplier(string skillElement, string attackerGongFa, string defenderGongFa) =>
        GetElementMatch(skillElement, attackerGongFa, defenderGongFa).DamageMultiplier;

    public static ElementMatch GetElementMatch(string skillElement, string attackerGongFa, string defenderGongFa)
    {
        string actionElement = NormalizeElement(skillElement);
        if (string.IsNullOrEmpty(actionElement) || actionElement == "混沌")
            return new ElementMatch(1.0, 0.0, 0.0);

        double damageMultiplier = 1.0;
        double critRateBonus = 0.0;
        double critDamageBonus = 0.0;

        string attackerElement = NormalizeElement(GetGongFaElement(attackerGongFa));
        if (!string.IsNullOrEmpty(attackerElement) && attackerElement != "混沌")
        {
            string attackerBase = ToBaseElement(attackerElement);
            string actionBase = ToBaseElement(actionElement);
            if (attackerBase != actionBase)
            {
                if (Generates(attackerBase, actionBase))
                {
                    damageMultiplier *= 1.10;
                }
                else if (Overcomes(attackerBase, actionBase))
                {
                    damageMultiplier *= 0.90;
                    critRateBonus += 5.0;
                    critDamageBonus += 10.0;
                }
            }
        }

        string defenderElement = NormalizeElement(GetGongFaElement(defenderGongFa));
        if (!string.IsNullOrEmpty(defenderElement) && defenderElement != "混沌")
        {
            string actionBase = ToBaseElement(actionElement);
            string defenderBase = ToBaseElement(defenderElement);
            bool variant = IsVariantElement(actionElement);

            if (actionBase != defenderBase)
            {
                if (Overcomes(actionBase, defenderBase))
                    damageMultiplier *= variant ? 1.15 : 1.10;
                else if (Overcomes(defenderBase, actionBase))
                    damageMultiplier *= variant ? 0.85 : 0.90;
                else if (Generates(actionBase, defenderBase))
                    damageMultiplier *= 0.95;
                else if (Generates(defenderBase, actionBase))
                    damageMultiplier *= 1.05;
            }
        }

        return new ElementMatch(damageMultiplier, critRateBonus, critDamageBonus);
    }

    public static string NormalizeElement(string element)
    {
        if (string.IsNullOrWhiteSpace(element)) return "";
        return element.Trim() switch
        {
            "金" or "木" or "水" or "火" or "土" or "风" or "雷" or "冰" or "暗" or "星" or "毒" or "混沌" => element.Trim(),
            "element_metal" or "element_metal_root" => "金",
            "element_wood" or "element_wood_root" => "木",
            "element_water" or "element_water_root" => "水",
            "element_fire" or "element_fire_root" => "火",
            "element_earth" or "element_earth_root" => "土",
            "element_wind" or "element_wind_root" => "风",
            "element_thunder" or "element_thunder_root" => "雷",
            "element_ice" or "element_ice_root" => "冰",
            "element_dark" or "element_dark_root" => "暗",
            "element_star" or "element_star_root" => "星",
            "element_poison" or "element_poison_root" => "毒",
            "element_chaos" or "element_chaos_root" => "混沌",
            "element_none" or "-" => "",
            _ => ""
        };
    }

    static string ToBaseElement(string element) => element switch
    {
        "风" or "毒" => "木",
        "雷" => "金",
        "冰" => "水",
        "暗" => "土",
        "星" => "火",
        _ => element,
    };

    static bool IsVariantElement(string element) =>
        element is "风" or "雷" or "冰" or "暗" or "星";

    static bool Generates(string source, string target) => (source, target) switch
    {
        ("木", "火") or ("火", "土") or ("土", "金") or ("金", "水") or ("水", "木") => true,
        _ => false
    };

    static bool Overcomes(string source, string target) => (source, target) switch
    {
        ("木", "土") or ("土", "水") or ("水", "火") or ("火", "金") or ("金", "木") => true,
        _ => false
    };

    public static readonly ArtConfig MagicArt = new("灵光闪", "神魂", 1.2, 20, 3, "");
    public static readonly DivineConfig PhysicalDivine = new("碎岳", "物理", 1.5, 10, 5, "土");
    public static readonly ArtConfig WaterArt = new("川流劲", "物理", 1.25, 20, 3, "水");
    public static readonly DivineConfig WaterDivine = new("逝水千击", "物理", 1.5, 10, 5, "水");
    public static readonly DivineConfig MagicDivine = new("灵光贯日", "神魂", 1.4, 10, 5, "");
    public static readonly ArtConfig TaiyiArt = new("玄元正气诀", "神魂", 1.4, 25, 3, "");
    public static readonly ArtConfig TaiyiFuxiuArt = new("安神符", "神魂", 0.5, 20, 3, "风");
    public static readonly DivineConfig TaiyiDivine = new("万法归宗", "神魂", 1.8, 15, 5, "");
    public static readonly DivineConfig TaiyiFuxiuDivine = new("天符镇岳", "神魂", 1.5, 20, 5, "土");
    public static readonly ArtConfig TaixuArt = new("业火咒", "神魂", 1.5, 25, 3, "暗");
    public static readonly DivineConfig TaixuDivine = new("魂归彼岸", "神魂", 1.8, 15, 5, "暗");
    public static readonly ArtConfig YuqingArt = new("九霄太乙斩", "物理", 1.5, 25, 3, "雷");
    public static readonly DivineConfig YuqingDivine = new("万剑朝宗", "物理", 1.8, 15, 5, "金");
    public static readonly ArtConfig YuqingLeijieArt = new("九霄雷罚", "物理", 1.4, 25, 3, "雷");
    public static readonly DivineConfig YuqingLeijieDivine = new("雷剑破虚", "物理", 1.6, 15, 5, "雷");
}
