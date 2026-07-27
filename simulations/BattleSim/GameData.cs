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
        string NaturalDanJiCandidateState,
        string SeatAccessState,
        string SeatCompetitionState,
        string FinalOccupancyState,
        int SeatCompetitionScore,
        int ZifuDivineArtCount,
        int ZifuPalaceCoverageCount,
        string ZifuCoreLoopState,
        string ZifuEligibilityNote,
        double StabilityMultiplier,
        double ArtAffinityMultiplier);

    public record GoldenCoreSeatProfile(
        string TargetBranch,
        string TargetSeat,
        string SeatName,
        string DanPivot);

    public const string ZifuCoreLoopPendingState = "未接入";
    public const string ZifuEligibilityPendingNote = "未接入紫府神通/府位闭环，阈值待验证";

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
            return new(
                "未成丹", "", "未成丹", "", "", "", "", "", "", "",
                "非自然候选", "none", "未进入", "未成丹", 0, 0, 0, ZifuCoreLoopPendingState, ZifuEligibilityPendingNote,
                1.0, 1.0);

        var (danName, danNature) = ResolveGoldenCoreTheme(weights);
        var seat = ResolveGoldenCoreSeat(weights);
        string legacyGrade = LegacyGoldenCoreGradeFromScore(score);
        bool hasFoundation = dfQuality != "无道基";

        if (!hasFoundation || score < 60)
            return new(
                "成丹", "暂寄丹籍", "暂寄", danName, danNature, legacyGrade, seat.TargetBranch, seat.TargetSeat, seat.SeatName, seat.DanPivot,
                "非自然候选", "temporary", "不参与自然争席", "暂寄", 0, 0, 0, ZifuCoreLoopPendingState, ZifuEligibilityPendingNote,
                0.92, 0.95);

        if (score >= 90)
            return new(
                "成丹", "自然丹籍", "稳定占据", danName, danNature, legacyGrade, seat.TargetBranch, seat.TargetSeat, seat.SeatName, seat.DanPivot,
                "自然候选", "natural_candidate", "待争席", "未占据", SeatCompetitionScore(score, weights), 0, 0, ZifuCoreLoopPendingState, ZifuEligibilityPendingNote,
                1.08, 1.10);

        return new(
            "成丹", "敕封丹籍", "受敕承位", danName, danNature, legacyGrade, seat.TargetBranch, seat.TargetSeat, seat.SeatName, seat.DanPivot,
            "非自然候选", "granted", "不参与自然争席", "受敕承位", 0, 0, 0, ZifuCoreLoopPendingState, ZifuEligibilityPendingNote,
            1.0, 1.0);
    }

    static int SeatCompetitionScore(int score, Dictionary<string, double> weights)
    {
        double topWeight = weights.Count == 0 ? 0 : weights.Values.Max();
        return score + (int)Math.Round(topWeight * 20);
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

    // N-ENV-01 环境规则 fixture：只定义固定点档位、查询边界和显式配对，不承载具体环境内容。
    public sealed record EnvironmentRulesConfig(
        int UnitsPerRange,
        int CompressedEdgeUnits,
        int StandardEdgeUnits,
        int ExpandedEdgeUnits,
        int MaxQueryRange,
        int MaxPhenomenonStrengthTier);

    public sealed record PhenomenonPairFixture(
        PhenomenonChannel Channel,
        string FirstType,
        string SecondType,
        string ResultType,
        int ResultStrengthTier,
        int ResultDurationCycles,
        bool Cancels = false,
        HexDirection? ResultDirection = null)
    {
        public bool Matches(PhenomenonState first, PhenomenonState second) =>
            first.Channel == Channel && second.Channel == Channel &&
            ((string.Equals(first.PhenomenonType, FirstType, StringComparison.Ordinal) &&
              string.Equals(second.PhenomenonType, SecondType, StringComparison.Ordinal)) ||
             (string.Equals(first.PhenomenonType, SecondType, StringComparison.Ordinal) &&
              string.Equals(second.PhenomenonType, FirstType, StringComparison.Ordinal)));
    }

    public static readonly EnvironmentRulesConfig EnvironmentRules = new(
        UnitsPerRange: 2,
        CompressedEdgeUnits: 1,
        StandardEdgeUnits: 2,
        ExpandedEdgeUnits: 4,
        MaxQueryRange: 16,
        MaxPhenomenonStrengthTier: 3);

    public static readonly IReadOnlyDictionary<CoverTier, int> EnvironmentCoverTierRanks =
        new Dictionary<CoverTier, int>
        {
            [CoverTier.None] = 0,
            [CoverTier.Light] = 1,
            [CoverTier.Heavy] = 2,
        };

    public static readonly PhenomenonPairFixture[] EnvironmentPhenomenonPairFixtures =
    {
        new(
            PhenomenonChannel.Visibility,
            "fixture-visibility-a",
            "fixture-visibility-b",
            "fixture-visibility-result",
            ResultStrengthTier: 2,
            ResultDurationCycles: 2),
    };

    public static readonly EnvironmentCyclePhase[] EnvironmentCycleOrder =
    {
        EnvironmentCyclePhase.AirflowMovement,
        EnvironmentCyclePhase.TemperatureChangesPrecipitation,
        EnvironmentCyclePhase.PrecipitationWashAndSurface,
        EnvironmentCyclePhase.VisibilityAndSuspendedHazard,
        EnvironmentCyclePhase.CloudDischarge,
        EnvironmentCyclePhase.DurationCleanup,
    };

    // 术法与神通配置
    public enum AreaShapeKind
    {
        Circle,
        Line,
        Fan,
    }

    public enum AreaCenterKind
    {
        Caster,
        TargetCell,
    }

    [Flags]
    public enum AreaTargetFaction
    {
        None = 0,
        Enemy = 1,
        Ally = 2,
        Self = 4,
    }

    [Flags]
    public enum AreaTargetState
    {
        None = 0,
        Alive = 1,
        Corpse = 2,
    }

    [Flags]
    public enum AreaEffectBlocker
    {
        None = 0,
        DirectedEdge = 1,
        All = DirectedEdge,
    }

    public sealed record AreaShapeConfig(
        AreaShapeKind Kind,
        int Radius,
        int Length,
        int FanHalfAngleSteps,
        HexDirection Facing,
        int InnerRadius);

    public sealed record AreaTargetingConfig(
        string Name,
        AreaCenterKind CenterKind,
        int MinCastRange,
        int MaxCastRange,
        AreaShapeConfig Shape,
        AreaEffectBlocker EffectBlockers,
        AreaTargetFaction AllowedFactions,
        AreaTargetState AllowedStates);

    public readonly record struct AreaTargetCandidate(int Index, int Team, HexCoord Position, bool IsAlive);
    public sealed record AreaTargetingResult(
        HexCoord? Center,
        IReadOnlyList<int> HitTargetIndexes,
        string RejectionReason);

    public record ForcedMovementConfig(string Name, int ForcedMovementDistance);
    public record MovementControlConfig(string Name, bool PreventsVoluntaryMovement);
    public record AttackProfile(
        string Name,
        string Type,
        double Mult,
        string Element,
        int MinRange,
        int MaxRange,
        AreaTargetingConfig AreaTargeting = null);
    public record ArtConfig(
        string Name,
        string Type,
        double Mult,
        int MPCost,
        int Cooldown,
        string Element = "",
        int MinRange = 1,
        int MaxRange = 1,
        AreaTargetingConfig AreaTargeting = null);
    public record DivineConfig(
        string Name,
        string Type,
        double Mult,
        double DefPen,
        int Cooldown,
        string Element = "",
        int MinRange = 1,
        int MaxRange = 1,
        AreaTargetingConfig AreaTargeting = null);
    public readonly record struct ElementMatch(double DamageMultiplier, double CritRateBonus, double CritDamageBonus);
    public static readonly ForcedMovementConfig BreakFormationChargeKnockback = new("破阵冲锋·击退", 1);
    public static readonly MovementControlConfig RootedControl = new("定身", true);
    public static readonly ArtConfig PhysicalArt = new("裂石拳", "物理", 1.3, 20, 3, "土", 1, 1);
    public static readonly AttackProfile UnarmedBasicAttack = new("徒手", "物理", 1.0, "", 1, 1);

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

    // TQ-044：这些已进入矩阵、但尚未完成专属星级设计的功法使用可追溯的临时基准。
    // N-BAL-02B 必须以各自的设计星级替换本表；未登记功法一律失败关闭。
    public static readonly Dictionary<string, Dictionary<string, double>> ApprovedGongFaStarFallbacks = new()
    {
        ["九霄雷劫录"] = UniformStarFallback(),
        ["苦行剑典"] = UniformStarFallback(),
        ["雷池淬体功"] = UniformStarFallback(),
        ["南华玄感录"] = UniformStarFallback(),
        ["绳墨正法录"] = UniformStarFallback(),
    };

    // TQ-044：与星级回退相同，未建专属成长表的已入矩阵功法只能使用这份登记的基准成长回退。
    public static readonly Dictionary<string, string> ApprovedGongFaGrowthFallbacks = new()
    {
        ["九霄雷劫录"] = "N-BAL-02B 待补专属成长表前，采用 SubGrowthBase 加权基准。",
        ["苦行剑典"] = "N-BAL-02B 待补专属成长表前，采用 SubGrowthBase 加权基准。",
        ["雷池淬体功"] = "N-BAL-02B 待补专属成长表前，采用 SubGrowthBase 加权基准。",
        ["南华玄感录"] = "N-BAL-02B 待补专属成长表前，采用 SubGrowthBase 加权基准。",
        ["绳墨正法录"] = "N-BAL-02B 待补专属成长表前，采用 SubGrowthBase 加权基准。",
    };

    // 星数→归一化权重
    public static Dictionary<string, double> WeightsFromGongFa(string name)
    {
        if (!GongFaStars.TryGetValue(name, out var stars) && !ApprovedGongFaStarFallbacks.TryGetValue(name, out stars))
            throw new InvalidOperationException($"功法「{name}」缺少星级权重；必须补齐星级表或登记显式回退。");
        ValidateStars(name, stars);
        double sum = stars.Values.Sum();
        return stars.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / sum, 2));
    }

    static Dictionary<string, double> UniformStarFallback() => new()
    {
        ["根骨"] = 1, ["魂魄"] = 1, ["神识"] = 1, ["资质"] = 1, ["气运"] = 1
    };

    static void ValidateStars(string name, IReadOnlyDictionary<string, double> stars)
    {
        var required = Character.InnateKeys;
        if (stars.Count != required.Length || required.Any(key => !stars.TryGetValue(key, out var value) || double.IsNaN(value) || double.IsInfinity(value) || value <= 0))
            throw new InvalidOperationException($"功法「{name}」的星级权重必须完整且为有限正数。");
    }

    public static bool HasApprovedGrowthFallback(string name) => ApprovedGongFaGrowthFallbacks.ContainsKey(name);

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

    public static readonly ArtConfig MagicArt = new("灵光闪", "神魂", 1.2, 20, 3, "", 2, 4);
    public static readonly DivineConfig PhysicalDivine = new("碎岳", "物理", 1.5, 10, 5, "土", 1, 1);
    public static readonly ArtConfig WaterArt = new("川流劲", "物理", 1.25, 20, 3, "水", 1, 3);
    public static readonly DivineConfig WaterDivine = new("逝水千击", "物理", 1.5, 10, 5, "水", 1, 3);
    public static readonly DivineConfig MagicDivine = new("灵光贯日", "神魂", 1.4, 10, 5, "", 2, 4);
    public static readonly ArtConfig TaiyiArt = new("玄元正气诀", "神魂", 1.4, 25, 3, "", 2, 4);
    public static readonly ArtConfig TaiyiFuxiuArt = new("安神符", "神魂", 0.5, 20, 3, "风", 2, 4);
    public static readonly DivineConfig TaiyiDivine = new("万法归宗", "神魂", 1.8, 15, 5, "", 2, 4);
    public static readonly DivineConfig TaiyiFuxiuDivine = new("天符镇岳", "神魂", 1.5, 20, 5, "土", 2, 4);
    public static readonly ArtConfig TaixuArt = new("业火咒", "神魂", 1.5, 25, 3, "暗", 2, 4);
    public static readonly DivineConfig TaixuDivine = new("魂归彼岸", "神魂", 1.8, 15, 5, "暗", 2, 4);
    public static readonly ArtConfig YuqingArt = new("九霄太乙斩", "物理", 1.5, 25, 3, "雷", 1, 3);
    public static readonly DivineConfig YuqingDivine = new("万剑朝宗", "物理", 1.8, 15, 5, "金", 1, 3);
    public static readonly ArtConfig YuqingLeijieArt = new("九霄雷罚", "物理", 1.4, 25, 3, "雷", 1, 3);
    public static readonly DivineConfig YuqingLeijieDivine = new("雷剑破虚", "物理", 1.6, 15, 5, "雷", 1, 3);
}

public enum GoldenCoreSeatType
{
    Source,
    Transformation,
    Domain,
}

public sealed record JindanCoreBinding(
    string BindingId,
    string JindanInstanceId,
    string DanshuCoreId,
    string FormationTransactionId,
    int FormationVersion);

public sealed record JindanDanxiangBinding(
    string DanxiangInstanceId,
    string JindanInstanceId,
    string DanxiangNameKey,
    string DanxingDefinitionId,
    string PresentationProfileId);

public sealed record GoldenCoreSeatBinding(
    string PositionId,
    string RoadId,
    GoldenCoreSeatType PositionType,
    string EquippedEffectId,
    string CompatibilityProfileId,
    string PrimaryCarrierAbilityInstanceId,
    IReadOnlyList<string> AuxiliaryCarrierAbilityInstanceIds);

public sealed record GoldenCoreAbilityLedgerBinding(
    string AbilityInstanceId,
    string ResourceDebitLedgerRef,
    string CooldownLedgerRef,
    string ChargeLedgerRef,
    string CostLedgerRef,
    string ConflictReserveLedgerRef,
    string ConflictCostProfileId);

public sealed record GoldenCoreAssemblyInput(
    JindanCoreBinding CoreBinding,
    JindanDanxiangBinding Danxiang,
    IReadOnlyList<GoldenCoreSeatBinding> StableSeats,
    IReadOnlyList<GoldenCoreAbilityLedgerBinding> AbilityLedgers,
    IReadOnlyList<string> CompleteMansionAbilityInstanceIds,
    IReadOnlyList<string> DanxiangAbilityInstanceIds);

/// <summary>
/// 丹相内单一稳定位格的主、辅预算。辅助连接共享一份上限，不因引用数量复制效果实例。
/// </summary>
public sealed record GoldenCoreSeatCarrierBudget(
    GoldenCoreSeatType PositionType,
    int AuxiliaryInputReferenceCount,
    double PrimaryCarrierBudgetUnits,
    double AuxiliaryInputBudgetUnits);

/// <summary>
/// 唯一丹相的审计预算；只描述已存在实例的组织边界，不创建普通效果槽或第二份运行账本。
/// </summary>
public sealed record GoldenCoreDanxiangBudget(
    int CompleteMansionInputCount,
    double CompleteMansionInputBudgetUnits,
    int StableSeatCount,
    IReadOnlyList<GoldenCoreSeatCarrierBudget> SeatBudgets,
    int PrimaryCarrierManifestationLimit,
    int AuxiliaryManifestationLimit,
    int AddedOrdinaryEffectSlots,
    int UniqueCoreCount,
    int UniqueDanxiangCount);

public sealed record GoldenCoreReforgeResolution(
    bool IsApplied,
    string Reason,
    GoldenCoreSeatType? PositionType,
    string PrimaryCarrierAbilityInstanceId)
{
    public static GoldenCoreReforgeResolution Applied(GoldenCoreSeatType positionType, string primaryCarrierAbilityInstanceId) =>
        new(true, "JD_REFORGE_APPLIED", positionType, primaryCarrierAbilityInstanceId);

    public static GoldenCoreReforgeResolution Rejected(string reason) =>
        new(false, reason, null, "");
}

public sealed class GoldenCoreAssemblyException : InvalidOperationException
{
    public GoldenCoreAssemblyException(string code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>
/// 金丹稳定实位的运行前装配：只承接已验证的静态绑定，不生成第四位、第二丹枢或第二丹相。
/// </summary>
public sealed class GoldenCoreAssembly
{
    GoldenCoreAssembly(
        JindanCoreBinding coreBinding,
        JindanDanxiangBinding danxiang,
        IReadOnlyDictionary<GoldenCoreSeatType, GoldenCoreSeatBinding> stableSeats,
        IReadOnlyDictionary<string, GoldenCoreAbilityLedgerBinding> abilityLedgers,
        IReadOnlyList<string> completeMansionAbilityInstanceIds,
        IReadOnlyList<string> danxiangAbilityInstanceIds)
    {
        CoreBinding = coreBinding;
        Danxiang = danxiang;
        StableSeats = stableSeats;
        AbilityLedgers = abilityLedgers;
        CompleteMansionAbilityInstanceIds = completeMansionAbilityInstanceIds;
        DanxiangAbilityInstanceIds = danxiangAbilityInstanceIds;
    }

    public const double CompleteMansionInputBudgetUnitsPerMansion = 1.0;
    public const double PrimaryCarrierBudgetUnitsPerStableSeat = 1.0;
    public const double AuxiliaryInputBudgetCapPerStableSeat = 0.25;

    public JindanCoreBinding CoreBinding { get; }
    public JindanDanxiangBinding Danxiang { get; }
    public IReadOnlyDictionary<GoldenCoreSeatType, GoldenCoreSeatBinding> StableSeats { get; }
    public IReadOnlyDictionary<string, GoldenCoreAbilityLedgerBinding> AbilityLedgers { get; }
    public IReadOnlyList<string> CompleteMansionAbilityInstanceIds { get; }
    public IReadOnlyList<string> DanxiangAbilityInstanceIds { get; }

    public GoldenCoreDanxiangBudget DanxiangBudget => new(
        CompleteMansionAbilityInstanceIds.Count,
        CompleteMansionAbilityInstanceIds.Count * CompleteMansionInputBudgetUnitsPerMansion,
        StableSeats.Count,
        StableSeats.Values
            .OrderBy(seat => seat.PositionType)
            .Select(seat => new GoldenCoreSeatCarrierBudget(
                seat.PositionType,
                seat.AuxiliaryCarrierAbilityInstanceIds.Count,
                PrimaryCarrierBudgetUnitsPerStableSeat,
                seat.AuxiliaryCarrierAbilityInstanceIds.Count == 0 ? 0.0 : AuxiliaryInputBudgetCapPerStableSeat))
            .ToArray(),
        StableSeats.Count,
        AuxiliaryManifestationLimit: 0,
        AddedOrdinaryEffectSlots: 0,
        UniqueCoreCount: 1,
        UniqueDanxiangCount: 1);

    public static GoldenCoreAssembly Create(GoldenCoreAssemblyInput input)
    {
        if (input == null || input.CoreBinding == null || input.Danxiang == null ||
            input.StableSeats == null || input.AbilityLedgers == null ||
            input.CompleteMansionAbilityInstanceIds == null || input.DanxiangAbilityInstanceIds == null)
        {
            throw new GoldenCoreAssemblyException("JD_UNKNOWN_STATIC_REFERENCE", "assembly input is incomplete.");
        }

        RequireReference(input.CoreBinding.BindingId, "JD_CORE_NOT_UNIQUE", "core binding id is required.");
        RequireReference(input.CoreBinding.JindanInstanceId, "JD_CORE_NOT_UNIQUE", "jindan instance id is required.");
        RequireReference(input.CoreBinding.DanshuCoreId, "JD_CORE_NOT_UNIQUE", "danshu core id is required.");
        RequireReference(input.Danxiang.DanxiangInstanceId, "JD_DANXIANG_NOT_UNIQUE", "danxiang instance id is required.");
        RequireReference(input.Danxiang.JindanInstanceId, "JD_DANXIANG_NOT_UNIQUE", "danxiang jindan instance id is required.");
        if (!string.Equals(input.CoreBinding.JindanInstanceId, input.Danxiang.JindanInstanceId, StringComparison.Ordinal))
            throw new GoldenCoreAssemblyException("JD_DANXIANG_NOT_UNIQUE", "danxiang must bind the unique jindan instance.");

        if (input.StableSeats.Count is < 1 or > 3)
            throw new GoldenCoreAssemblyException("JD_STABLE_POSITION_LIMIT", "stable seats must contain one to three entries.");

        var mansionAbilities = RequireDistinctReferences(
            input.CompleteMansionAbilityInstanceIds,
            "JD_ABILITY_LEDGER_OWNERSHIP_INVALID",
            "complete mansion ability");
        var ledgers = new Dictionary<string, GoldenCoreAbilityLedgerBinding>(StringComparer.Ordinal);
        var mutableLedgerOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var binding in input.AbilityLedgers)
        {
            if (binding == null || string.IsNullOrWhiteSpace(binding.AbilityInstanceId) || !ledgers.TryAdd(binding.AbilityInstanceId, binding))
                throw new GoldenCoreAssemblyException("JD_ABILITY_LEDGER_OWNERSHIP_INVALID", "ability ledger ids must be unique and non-empty.");

            foreach (var ledgerRef in MutableLedgerReferences(binding))
            {
                if (mutableLedgerOwners.TryGetValue(ledgerRef, out var owner))
                    throw new GoldenCoreAssemblyException("JD_ABILITY_LEDGER_OWNERSHIP_INVALID", $"mutable ledger '{ledgerRef}' is already owned by '{owner}'.");
                mutableLedgerOwners.Add(ledgerRef, binding.AbilityInstanceId);
            }

            bool hasConflictReserve = !string.IsNullOrWhiteSpace(binding.ConflictReserveLedgerRef);
            bool hasConflictCost = !string.IsNullOrWhiteSpace(binding.ConflictCostProfileId);
            if (hasConflictReserve != hasConflictCost)
                throw new GoldenCoreAssemblyException("JD_CONFLICT_REFERENCE_INVALID", "conflict reserve and cost profile must be declared together.");
        }

        if (!ledgers.Keys.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(mansionAbilities.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new GoldenCoreAssemblyException("JD_ABILITY_LEDGER_OWNERSHIP_INVALID", "every complete mansion ability requires exactly one owned ledger.");

        var seats = new Dictionary<GoldenCoreSeatType, GoldenCoreSeatBinding>();
        var primaryCarriers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seat in input.StableSeats)
        {
            if (seat == null || seats.ContainsKey(seat.PositionType))
                throw new GoldenCoreAssemblyException("JD_STABLE_POSITION_LIMIT", "each source, transformation, or domain seat may occur once.");
            RequireReference(seat.PositionId, "JD_UNKNOWN_STATIC_REFERENCE", "position id is required.");
            RequireReference(seat.RoadId, "JD_UNKNOWN_STATIC_REFERENCE", "road id is required.");
            RequireReference(seat.EquippedEffectId, "JD_EFFECT_LOADOUT_INVALID", "equipped effect id is required.");
            RequireReference(seat.CompatibilityProfileId, "JD_UNKNOWN_STATIC_REFERENCE", "compatibility profile id is required.");
            RequireReference(seat.PrimaryCarrierAbilityInstanceId, "JD_CARRIER_REFERENCE_INVALID", "primary carrier is required.");
            if (!ledgers.ContainsKey(seat.PrimaryCarrierAbilityInstanceId) || !primaryCarriers.Add(seat.PrimaryCarrierAbilityInstanceId))
                throw new GoldenCoreAssemblyException("JD_PRIMARY_CARRIER_DUPLICATE", "stable seats require different owned primary carrier instances.");

            var auxiliaries = RequireDistinctReferences(
                seat.AuxiliaryCarrierAbilityInstanceIds ?? Array.Empty<string>(),
                "JD_CARRIER_REFERENCE_INVALID",
                "auxiliary carrier");
            if (auxiliaries.Count > mansionAbilities.Count - 1 ||
                auxiliaries.Contains(seat.PrimaryCarrierAbilityInstanceId, StringComparer.Ordinal) ||
                auxiliaries.Any(id => !ledgers.ContainsKey(id)))
                throw new GoldenCoreAssemblyException("JD_CARRIER_REFERENCE_INVALID", "auxiliary carriers must reference other owned ability instances.");

            seats.Add(seat.PositionType, seat with
            {
                AuxiliaryCarrierAbilityInstanceIds = auxiliaries.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            });
        }

        var declaredDanxiangReferences = RequireDistinctReferences(
            input.DanxiangAbilityInstanceIds,
            "JD_CARRIER_REFERENCE_INVALID",
            "danxiang ability");
        if (declaredDanxiangReferences.Any(id => !ledgers.ContainsKey(id)))
            throw new GoldenCoreAssemblyException("JD_CARRIER_REFERENCE_INVALID", "danxiang may only reference owned ability instances.");

        // 丹相是唯一聚合体：显式引用只能补充展示/连接意图，不能排除任何完整紫府输入。
        var allDanxiangReferences = new HashSet<string>(mansionAbilities, StringComparer.Ordinal);
        allDanxiangReferences.UnionWith(declaredDanxiangReferences);
        return new GoldenCoreAssembly(
            input.CoreBinding,
            input.Danxiang,
            seats,
            ledgers,
            mansionAbilities.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            allDanxiangReferences.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    internal GoldenCoreAssembly ReforgePrimaryCarrier(
        GoldenCoreSeatType positionType,
        string replacementAbilityInstanceId,
        IReadOnlyList<string> auxiliaryCarrierAbilityInstanceIds,
        string verifiedCompatibilityProfileId)
    {
        if (!StableSeats.TryGetValue(positionType, out var targetSeat))
            throw new GoldenCoreAssemblyException("JD_REFORGE_POSITION_INVALID", "target stable seat is not occupied.");
        RequireReference(replacementAbilityInstanceId, "JD_REFORGE_CARRIER_INVALID", "replacement primary carrier is required.");
        RequireReference(verifiedCompatibilityProfileId, "JD_REFORGE_COMPATIBILITY_INVALID", "verified compatibility profile is required.");
        if (!string.Equals(targetSeat.CompatibilityProfileId, verifiedCompatibilityProfileId, StringComparison.Ordinal))
            throw new GoldenCoreAssemblyException("JD_REFORGE_COMPATIBILITY_INVALID", "replacement must use the target seat's verified compatibility profile.");
        if (!AbilityLedgers.ContainsKey(replacementAbilityInstanceId))
            throw new GoldenCoreAssemblyException("JD_REFORGE_CARRIER_INVALID", "replacement primary carrier must be an owned complete-mansion ability.");
        if (StableSeats.Values.Any(seat => seat.PositionType != positionType &&
                                           string.Equals(seat.PrimaryCarrierAbilityInstanceId, replacementAbilityInstanceId, StringComparison.Ordinal)))
        {
            throw new GoldenCoreAssemblyException("JD_REFORGE_PRIMARY_DUPLICATE", "replacement primary carrier is already bound to another stable seat.");
        }

        var replacementAuxiliaries = RequireDistinctReferences(
            auxiliaryCarrierAbilityInstanceIds ?? Array.Empty<string>(),
            "JD_REFORGE_CARRIER_INVALID",
            "reforged auxiliary carrier");
        if (replacementAuxiliaries.Count > CompleteMansionAbilityInstanceIds.Count - 1 ||
            replacementAuxiliaries.Contains(replacementAbilityInstanceId, StringComparer.Ordinal) ||
            replacementAuxiliaries.Any(id => !AbilityLedgers.ContainsKey(id)))
        {
            throw new GoldenCoreAssemblyException("JD_REFORGE_CARRIER_INVALID", "reforged auxiliaries must be distinct owned abilities other than the replacement primary carrier.");
        }

        var reforgedSeats = StableSeats.Values
            .Select(seat => seat.PositionType == positionType
                ? seat with
                {
                    PrimaryCarrierAbilityInstanceId = replacementAbilityInstanceId,
                    AuxiliaryCarrierAbilityInstanceIds = replacementAuxiliaries.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                }
                : seat)
            .OrderBy(seat => seat.PositionType)
            .ToArray();

        return Create(new GoldenCoreAssemblyInput(
            CoreBinding,
            Danxiang,
            reforgedSeats,
            AbilityLedgers.Values.OrderBy(binding => binding.AbilityInstanceId, StringComparer.Ordinal).ToArray(),
            CompleteMansionAbilityInstanceIds,
            DanxiangAbilityInstanceIds));
    }

    public GoldenCoreRuntimeLedger CreateRuntimeLedger(int initialResource)
    {
        if (initialResource < 0)
            throw new ArgumentOutOfRangeException(nameof(initialResource));
        return new GoldenCoreRuntimeLedger(this, AbilityLedgers.Values, initialResource);
    }

    static IEnumerable<string> MutableLedgerReferences(GoldenCoreAbilityLedgerBinding binding) =>
        new[]
        {
            binding.ResourceDebitLedgerRef,
            binding.CooldownLedgerRef,
            binding.ChargeLedgerRef,
            binding.CostLedgerRef,
            binding.ConflictReserveLedgerRef,
        }.Where(reference => !string.IsNullOrWhiteSpace(reference));

    static HashSet<string> RequireDistinctReferences(IEnumerable<string> values, string code, string label)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            RequireReference(value, code, $"{label} id is required.");
            if (!result.Add(value))
                throw new GoldenCoreAssemblyException(code, $"{label} ids must be unique.");
        }
        return result;
    }

    static void RequireReference(string value, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new GoldenCoreAssemblyException(code, message);
    }
}

/// <summary>每个 abilityInstanceId 的可变战斗账本；同一实例始终返回同一个状态对象。</summary>
public sealed class GoldenCoreRuntimeLedger
{
    readonly Dictionary<string, GoldenCoreAbilityRuntimeState> states;

    internal GoldenCoreRuntimeLedger(GoldenCoreAssembly assembly, IEnumerable<GoldenCoreAbilityLedgerBinding> bindings, int initialResource)
    {
        Assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        states = bindings.ToDictionary(
            binding => binding.AbilityInstanceId,
            binding => new GoldenCoreAbilityRuntimeState(binding.AbilityInstanceId, initialResource),
            StringComparer.Ordinal);
    }

    internal GoldenCoreAssembly Assembly { get; private set; }

    public GoldenCoreAbilityRuntimeState Get(string abilityInstanceId) =>
        states.TryGetValue(abilityInstanceId, out var state)
            ? state
            : throw new KeyNotFoundException($"Unknown golden-core ability instance: {abilityInstanceId}");

    public void TickCooldowns()
    {
        foreach (var state in states.Values)
            state.TickCooldown();
    }

    internal IReadOnlyList<GoldenCoreLedgerDisposition> CloseForCarrierDeath(string carrierAbilityInstanceId)
    {
        if (string.IsNullOrWhiteSpace(carrierAbilityInstanceId) || !states.ContainsKey(carrierAbilityInstanceId))
            throw new InvalidOperationException("A carrier death requires an owned ability runtime ledger.");

        var dispositions = states.Values
            .OrderBy(state => state.AbilityInstanceId, StringComparer.Ordinal)
            .Select(state => new GoldenCoreLedgerDisposition(
                state.AbilityInstanceId,
                state.AbilityInstanceId == carrierAbilityInstanceId,
                state.Resource,
                state.Cooldown,
                state.ConflictReserve))
            .ToArray();

        states[carrierAbilityInstanceId].Release();
        return dispositions;
    }

    internal void RebindAssembly(GoldenCoreAssembly assembly)
    {
        if (assembly == null ||
            !states.Keys.OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(assembly.AbilityLedgers.Keys.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException("A safe reforge must preserve the owned ability-instance ledger set.");
        }
        Assembly = assembly;
    }
}

public sealed record GoldenCoreLedgerDisposition(
    string AbilityInstanceId,
    bool IsReleased,
    int Resource,
    int Cooldown,
    int ConflictReserve);

public sealed class GoldenCoreAbilityRuntimeState
{
    internal GoldenCoreAbilityRuntimeState(string abilityInstanceId, int initialResource)
    {
        AbilityInstanceId = abilityInstanceId;
        Resource = initialResource;
    }

    public string AbilityInstanceId { get; }
    public int Resource { get; private set; }
    public int Cooldown { get; private set; }
    public int ConflictReserve { get; private set; }
    public bool IsReleased { get; private set; }

    public bool TrySpendResource(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (IsReleased)
            return false;
        if (Resource < amount)
            return false;
        Resource -= amount;
        return true;
    }

    public void StartCooldown(int turns)
    {
        if (turns < 0)
            throw new ArgumentOutOfRangeException(nameof(turns));
        EnsureActive();
        Cooldown = Math.Max(Cooldown, turns);
    }

    public void TickCooldown()
    {
        if (!IsReleased && Cooldown > 0)
            Cooldown--;
    }

    public void AddConflictReserve(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        EnsureActive();
        ConflictReserve += amount;
    }

    public bool TrySpendConflictReserve(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (IsReleased)
            return false;
        if (ConflictReserve < amount)
            return false;
        ConflictReserve -= amount;
        return true;
    }

    internal void Release()
    {
        IsReleased = true;
    }

    void EnsureActive()
    {
        if (IsReleased)
            throw new InvalidOperationException($"Golden-core ability runtime '{AbilityInstanceId}' has been released.");
    }
}

public enum CrossTierChallengeSourceKind
{
    JindanProtection,
    YuanyingOrthodoxy,
    DedicatedGreatFormation,
    NarrativeRelic,
}

/// <summary>
/// 已冻结的跨阶例外资格。它只允许档案明确的挑战进入直接冲突，不改变角色境界、位格或胜负结果。
/// </summary>
public sealed record CrossTierChallengeGrant(
    string GrantId,
    int DefinitionVersion,
    string TargetVariableId,
    string ChallengerId,
    CrossTierChallengeSourceKind QualificationSource,
    string AllowedOperationId,
    string TargetId,
    string ScopeId,
    string BeneficiaryId,
    string RealityAnchorId,
    string ResourceLedgerRef,
    string CapacityLedgerRef,
    int ChallengeRuleTier,
    int EffectiveAtTick,
    int ExpiresAtTick,
    bool IsRevoked,
    string RevocationReason,
    string DisplaySource);

public sealed record CrossTierChallengeRequest(
    string ChallengeEventId,
    string GrantId,
    int ExpectedDefinitionVersion,
    string TargetVariableId,
    string ChallengerId,
    int WorldTick);

public sealed record CrossTierChallengeResolution(
    bool IsEligible,
    string Reason,
    CrossTierChallengeGrant Grant)
{
    internal static CrossTierChallengeResolution Rejected(string reason) => new(false, reason, null);
}

/// <summary>
/// 版本化档案只做资格重验；相同输入不扣除资源、不写入胜负，重复事件因此保持幂等。
/// </summary>
public sealed class CrossTierChallengeArchive
{
    readonly IReadOnlyDictionary<string, CrossTierChallengeGrant> grants;

    public CrossTierChallengeArchive(IEnumerable<CrossTierChallengeGrant> grants)
    {
        if (grants == null)
            throw new ArgumentNullException(nameof(grants));

        var indexed = new Dictionary<string, CrossTierChallengeGrant>(StringComparer.Ordinal);
        foreach (var grant in grants)
        {
            if (grant == null || string.IsNullOrWhiteSpace(grant.GrantId) || !indexed.TryAdd(grant.GrantId, grant))
                throw new ArgumentException("Cross-tier challenge grant ids must be unique and non-empty.", nameof(grants));
        }
        this.grants = indexed;
    }

    public CrossTierChallengeResolution Resolve(CrossTierChallengeRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ChallengeEventId) || string.IsNullOrWhiteSpace(request.GrantId) ||
            string.IsNullOrWhiteSpace(request.TargetVariableId) || string.IsNullOrWhiteSpace(request.ChallengerId) ||
            request.ExpectedDefinitionVersion <= 0 || request.WorldTick < 0)
        {
            return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_REQUEST_INVALID");
        }
        if (!grants.TryGetValue(request.GrantId, out var grant))
            return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_GRANT_UNKNOWN");
        if (!IsWellFormed(grant))
            return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_GRANT_INVALID");
        if (grant.DefinitionVersion != request.ExpectedDefinitionVersion)
            return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_VERSION_MISMATCH");
        if (grant.IsRevoked)
            return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_REVOKED");
        if (request.WorldTick < grant.EffectiveAtTick)
            return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_NOT_YET_EFFECTIVE");
        if (request.WorldTick > grant.ExpiresAtTick)
            return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_EXPIRED");
        if (!string.Equals(grant.TargetVariableId, request.TargetVariableId, StringComparison.Ordinal))
            return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_TARGET_MISMATCH");
        if (!string.Equals(grant.ChallengerId, request.ChallengerId, StringComparison.Ordinal))
            return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_CHALLENGER_MISMATCH");
        return new CrossTierChallengeResolution(true, "JD_CHALLENGE_AUTHORIZED", grant);
    }

    static bool IsWellFormed(CrossTierChallengeGrant grant)
    {
        return grant.DefinitionVersion > 0 && grant.ChallengeRuleTier > 0 && grant.EffectiveAtTick >= 0 &&
               grant.ExpiresAtTick >= grant.EffectiveAtTick &&
               grant.QualificationSource is CrossTierChallengeSourceKind.JindanProtection or
                   CrossTierChallengeSourceKind.YuanyingOrthodoxy or
                   CrossTierChallengeSourceKind.DedicatedGreatFormation or
                   CrossTierChallengeSourceKind.NarrativeRelic &&
               !string.IsNullOrWhiteSpace(grant.TargetVariableId) &&
               !string.IsNullOrWhiteSpace(grant.ChallengerId) &&
               !string.IsNullOrWhiteSpace(grant.AllowedOperationId) &&
               !string.IsNullOrWhiteSpace(grant.TargetId) &&
               !string.IsNullOrWhiteSpace(grant.ScopeId) &&
               !string.IsNullOrWhiteSpace(grant.BeneficiaryId) &&
               !string.IsNullOrWhiteSpace(grant.RealityAnchorId) &&
               !string.IsNullOrWhiteSpace(grant.ResourceLedgerRef) &&
               !string.IsNullOrWhiteSpace(grant.CapacityLedgerRef) &&
               !string.IsNullOrWhiteSpace(grant.DisplaySource);
    }
}

public sealed record GoldenCoreCarrierDeathInput(
    string DeathEventId,
    GoldenCoreSeatType PositionType,
    string CarrierAbilityInstanceId);

public sealed record GoldenCoreCarrierDeathResolution(
    bool IsSettled,
    string Reason,
    string DeathEventId,
    GoldenCoreSeatType PositionType,
    string CarrierAbilityInstanceId,
    IReadOnlyList<GoldenCoreLedgerDisposition> LedgerDispositions)
{
    internal static GoldenCoreCarrierDeathResolution Rejected(string reason) =>
        new(false, reason, "", default, "", Array.Empty<GoldenCoreLedgerDisposition>());
}

public enum GoldenCoreConflictInputMode
{
    Qte,
    Skip,
}

public enum GoldenCoreConflictOutcome
{
    LeftWins,
    RightWins,
    Neutral,
    Rejected,
}

/// <summary>冲突候选只记录已冻结的静态装配和公开的结算输入，不从阶段、随机数或部署顺序取裁定。</summary>
public sealed record GoldenCoreConflictCandidateInput(
    string CandidateId,
    string AbilityInstanceId,
    GoldenCoreSeatType PositionType,
    string CompatibilityProfileId,
    string ConflictCostProfileId,
    string VariableId,
    string TargetId,
    bool HasVariableAuthority,
    bool HasLegalTarget,
    int RealityAnchorRank,
    int AlreadyPaidCost,
    bool HasActiveContinuousCarrier,
    int PulseCost,
    int SettlementCooldown);

public sealed class GoldenCoreConflictCandidate
{
    internal GoldenCoreConflictCandidate(GoldenCoreConflictCandidateInput input, GoldenCoreAbilityRuntimeState runtimeState)
    {
        Input = input;
        RuntimeState = runtimeState;
    }

    public GoldenCoreConflictCandidateInput Input { get; }
    internal GoldenCoreAbilityRuntimeState RuntimeState { get; }
}

public sealed record GoldenCoreConflictCandidatePreparation(GoldenCoreConflictCandidate Candidate, string RejectionCode)
{
    public bool IsEligible => Candidate != null && string.IsNullOrEmpty(RejectionCode);

    public static GoldenCoreConflictCandidatePreparation Rejected(string rejectionCode) =>
        new(null, rejectionCode);
}

public sealed record GoldenCoreConflictResolution(
    GoldenCoreConflictOutcome Outcome,
    string Reason,
    string WinnerCandidateId,
    int LeftPulses,
    int RightPulses,
    int LeftReserveSpent,
    int RightReserveSpent,
    int RejectedCandidateCount);

public static class GoldenCoreConflictCandidates
{
    public static GoldenCoreConflictCandidatePreparation Prepare(
        GoldenCoreAssembly assembly,
        GoldenCoreRuntimeLedger runtimeLedger,
        GoldenCoreConflictCandidateInput input)
    {
        if (assembly == null || runtimeLedger == null || input == null)
            return GoldenCoreConflictCandidatePreparation.Rejected("JD_CONFLICT_INPUT_INVALID");
        if (!ReferenceEquals(runtimeLedger.Assembly, assembly))
            return GoldenCoreConflictCandidatePreparation.Rejected("JD_CONFLICT_RUNTIME_LEDGER_INVALID");
        if (string.IsNullOrWhiteSpace(input.CandidateId) ||
            string.IsNullOrWhiteSpace(input.AbilityInstanceId) ||
            string.IsNullOrWhiteSpace(input.CompatibilityProfileId) ||
            string.IsNullOrWhiteSpace(input.ConflictCostProfileId) ||
            string.IsNullOrWhiteSpace(input.VariableId) ||
            string.IsNullOrWhiteSpace(input.TargetId) ||
            input.RealityAnchorRank < 0 ||
            input.AlreadyPaidCost < 0 ||
            input.PulseCost <= 0 ||
            input.SettlementCooldown < 0)
        {
            return GoldenCoreConflictCandidatePreparation.Rejected("JD_CONFLICT_INPUT_INVALID");
        }
        if (!input.HasVariableAuthority || !input.HasLegalTarget)
            return GoldenCoreConflictCandidatePreparation.Rejected("JD_CONFLICT_AUTHORITY_INVALID");
        if (!assembly.StableSeats.TryGetValue(input.PositionType, out var seat) ||
            !string.Equals(seat.PrimaryCarrierAbilityInstanceId, input.AbilityInstanceId, StringComparison.Ordinal))
        {
            return GoldenCoreConflictCandidatePreparation.Rejected("JD_CONFLICT_STABLE_POSITION_INVALID");
        }
        if (!string.Equals(seat.CompatibilityProfileId, input.CompatibilityProfileId, StringComparison.Ordinal))
            return GoldenCoreConflictCandidatePreparation.Rejected("JD_CONFLICT_STATIC_COMPATIBILITY_INVALID");
        if (!assembly.AbilityLedgers.TryGetValue(input.AbilityInstanceId, out var binding))
            return GoldenCoreConflictCandidatePreparation.Rejected("JD_CONFLICT_ABILITY_LEDGER_INVALID");
        if (string.IsNullOrWhiteSpace(binding.ConflictReserveLedgerRef) ||
            string.IsNullOrWhiteSpace(binding.ConflictCostProfileId))
        {
            return GoldenCoreConflictCandidatePreparation.Rejected("JD_CONFLICT_RESERVE_UNAVAILABLE");
        }
        if (!string.Equals(binding.ConflictCostProfileId, input.ConflictCostProfileId, StringComparison.Ordinal))
            return GoldenCoreConflictCandidatePreparation.Rejected("JD_CONFLICT_COST_PROFILE_INVALID");

        return new GoldenCoreConflictCandidatePreparation(
            new GoldenCoreConflictCandidate(input, runtimeLedger.Get(input.AbilityInstanceId)),
            "");
    }
}

public static class GoldenCoreConflictResolver
{
    public static GoldenCoreConflictResolution Resolve(
        GoldenCoreConflictCandidatePreparation left,
        GoldenCoreConflictCandidatePreparation right,
        GoldenCoreConflictInputMode inputMode)
    {
        if (inputMode != GoldenCoreConflictInputMode.Qte && inputMode != GoldenCoreConflictInputMode.Skip)
            return Rejected("JD_CONFLICT_INPUT_MODE_INVALID", 0);

        int rejectedCandidateCount = (left?.IsEligible == true ? 0 : 1) + (right?.IsEligible == true ? 0 : 1);
        if (rejectedCandidateCount > 0)
        {
            string rejectionReason = left?.IsEligible == false
                ? left.RejectionCode
                : right?.RejectionCode ?? "JD_CONFLICT_INPUT_INVALID";
            return Rejected(rejectionReason, rejectedCandidateCount);
        }

        var leftCandidate = left.Candidate;
        var rightCandidate = right.Candidate;
        var comparison = ComparePriority(leftCandidate, rightCandidate, out var reason);
        if (comparison > 0)
            return new GoldenCoreConflictResolution(GoldenCoreConflictOutcome.LeftWins, reason, leftCandidate.Input.CandidateId, 0, 0, 0, 0, 0);
        if (comparison < 0)
            return new GoldenCoreConflictResolution(GoldenCoreConflictOutcome.RightWins, reason, rightCandidate.Input.CandidateId, 0, 0, 0, 0, 0);

        int leftPulses = leftCandidate.RuntimeState.ConflictReserve / leftCandidate.Input.PulseCost;
        int rightPulses = rightCandidate.RuntimeState.ConflictReserve / rightCandidate.Input.PulseCost;
        int leftReserveSpent = leftPulses * leftCandidate.Input.PulseCost;
        int rightReserveSpent = rightPulses * rightCandidate.Input.PulseCost;
        if (leftReserveSpent > 0 && !leftCandidate.RuntimeState.TrySpendConflictReserve(leftReserveSpent))
            throw new InvalidOperationException("conflict reserve changed during deterministic settlement.");
        if (rightReserveSpent > 0 && !rightCandidate.RuntimeState.TrySpendConflictReserve(rightReserveSpent))
            throw new InvalidOperationException("conflict reserve changed during deterministic settlement.");
        leftCandidate.RuntimeState.StartCooldown(leftCandidate.Input.SettlementCooldown);
        rightCandidate.RuntimeState.StartCooldown(rightCandidate.Input.SettlementCooldown);

        if (leftPulses > rightPulses)
        {
            return new GoldenCoreConflictResolution(
                GoldenCoreConflictOutcome.LeftWins,
                "PULSE_ADVANTAGE",
                leftCandidate.Input.CandidateId,
                leftPulses,
                rightPulses,
                leftReserveSpent,
                rightReserveSpent,
                0);
        }
        if (rightPulses > leftPulses)
        {
            return new GoldenCoreConflictResolution(
                GoldenCoreConflictOutcome.RightWins,
                "PULSE_ADVANTAGE",
                rightCandidate.Input.CandidateId,
                leftPulses,
                rightPulses,
                leftReserveSpent,
                rightReserveSpent,
                0);
        }
        return new GoldenCoreConflictResolution(
            GoldenCoreConflictOutcome.Neutral,
            "PULSE_NEUTRAL",
            "",
            leftPulses,
            rightPulses,
            leftReserveSpent,
            rightReserveSpent,
            0);
    }

    static GoldenCoreConflictResolution Rejected(string reason, int rejectedCandidateCount) =>
        new(GoldenCoreConflictOutcome.Rejected, reason, "", 0, 0, 0, 0, rejectedCandidateCount);

    static int ComparePriority(
        GoldenCoreConflictCandidate left,
        GoldenCoreConflictCandidate right,
        out string reason)
    {
        int comparison = CompareAuthorityAndTarget(left.Input, right.Input);
        if (comparison != 0)
        {
            reason = "VARIABLE_AUTHORITY_AND_TARGET";
            return comparison;
        }
        comparison = PositionRank(left.Input.PositionType).CompareTo(PositionRank(right.Input.PositionType));
        if (comparison != 0)
        {
            reason = "POSITION_TIER";
            return comparison;
        }
        comparison = left.Input.RealityAnchorRank.CompareTo(right.Input.RealityAnchorRank);
        if (comparison != 0)
        {
            reason = "REALITY_ANCHOR";
            return comparison;
        }
        comparison = left.Input.AlreadyPaidCost.CompareTo(right.Input.AlreadyPaidCost);
        if (comparison != 0)
        {
            reason = "ALREADY_PAID_COST";
            return comparison;
        }
        comparison = left.Input.HasActiveContinuousCarrier.CompareTo(right.Input.HasActiveContinuousCarrier);
        reason = comparison == 0 ? "PULSE" : "ACTIVE_CONTINUOUS_CARRIER";
        return comparison;
    }

    static int CompareAuthorityAndTarget(GoldenCoreConflictCandidateInput left, GoldenCoreConflictCandidateInput right)
    {
        int leftRank = (left.HasVariableAuthority ? 2 : 0) + (left.HasLegalTarget ? 1 : 0);
        int rightRank = (right.HasVariableAuthority ? 2 : 0) + (right.HasLegalTarget ? 1 : 0);
        return leftRank.CompareTo(rightRank);
    }

    static int PositionRank(GoldenCoreSeatType positionType) => positionType switch
    {
        GoldenCoreSeatType.Source => 3,
        GoldenCoreSeatType.Transformation => 2,
        GoldenCoreSeatType.Domain => 1,
        _ => 0,
    };
}
