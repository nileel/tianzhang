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
                "成丹", "自然丹籍", "候选未占据", danName, danNature, legacyGrade, seat.TargetBranch, seat.TargetSeat, seat.SeatName, seat.DanPivot,
                "自然候选", "natural_candidate", "待争席", "未占据", 0, 0, 0, ZifuCoreLoopPendingState, ZifuEligibilityPendingNote,
                1.08, 1.10);

        return new(
            "成丹", "敕封丹籍", "受敕承位", danName, danNature, legacyGrade, seat.TargetBranch, seat.TargetSeat, seat.SeatName, seat.DanPivot,
            "非自然候选", "granted", "不参与自然争席", "受敕承位", 0, 0, 0, ZifuCoreLoopPendingState, ZifuEligibilityPendingNote,
            1.0, 1.0);
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

    // N-STATE-01: capacities are explicit rule data; combat state never invents a fallback value.
    public static readonly CombatStateRulesConfig CombatStateRules = new(
        CheckpointCapacity: 3,
        AutomaticResponseCapacity: 1,
        MaxCausalGraftRules: 2);
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

// N-SEAT-01B：争位候选与占据分离。协调器只关闭同一世界 tick 的完成集；
// 注册表是唯一能改变位格占据、核心承载和候选终态的入口。
public enum SeatCompetitionAttemptStatus
{
    Active,
    AwaitingRegularTickClose,
    CriticalContest,
    AwaitingCriticalTickClose,
    ReadyToBind,
    Bound,
    Invalidated,
}

public enum SeatCompetitionResolutionKind
{
    NoCompletion,
    CriticalContestContinues,
    UniqueReady,
}

public enum SeatCompetitionBindFailureReason
{
    None,
    AttemptNotReady,
    PositionUnavailable,
    StalePositionVersion,
    PreconditionsNotMet,
    CoreInvariantViolation,
}

public sealed record SeatCompetitionTickResolution(
    SeatCompetitionResolutionKind Kind,
    string WinningAttemptId,
    IReadOnlyList<string> AttemptIds);

public sealed record SeatCompetitionBindResult(
    bool Succeeded,
    SeatCompetitionBindFailureReason FailureReason,
    string BoundPositionId = "");

public sealed class SeatProofProfileDefinition
{
    readonly HashSet<string> requiredAchievementIds;

    public SeatProofProfileDefinition(
        string profileId,
        GoldenCoreSeatType seatType,
        int regularProgressTarget,
        int criticalProgressTarget,
        IEnumerable<string> requiredAchievementIds)
    {
        RequireId(profileId, nameof(profileId));
        if (regularProgressTarget <= 0)
            throw new ArgumentOutOfRangeException(nameof(regularProgressTarget));
        if (criticalProgressTarget <= 0)
            throw new ArgumentOutOfRangeException(nameof(criticalProgressTarget));

        ProfileId = profileId;
        SeatType = seatType;
        RegularProgressTarget = regularProgressTarget;
        CriticalProgressTarget = criticalProgressTarget;
        this.requiredAchievementIds = new HashSet<string>(
            requiredAchievementIds ?? Array.Empty<string>(),
            StringComparer.Ordinal);
        if (this.requiredAchievementIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Achievement IDs must be non-empty.", nameof(requiredAchievementIds));
    }

    public string ProfileId { get; }
    public GoldenCoreSeatType SeatType { get; }
    public int RegularProgressTarget { get; }
    public int CriticalProgressTarget { get; }
    public IReadOnlyCollection<string> RequiredAchievementIds => requiredAchievementIds;

    public bool IsSatisfiedBy(SeatProofLedger ledger) =>
        ledger != null && requiredAchievementIds.All(ledger.HasAchievement);

    static void RequireId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty ID is required.", parameterName);
    }
}

public sealed class SeatProofLedger
{
    readonly HashSet<string> achievementIds;

    public SeatProofLedger(string actorId, IEnumerable<string> achievementIds)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("An actor ID is required.", nameof(actorId));

        ActorId = actorId;
        this.achievementIds = new HashSet<string>(
            achievementIds ?? Array.Empty<string>(),
            StringComparer.Ordinal);
        if (this.achievementIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Achievement IDs must be non-empty.", nameof(achievementIds));
    }

    public string ActorId { get; }
    public bool HasAchievement(string achievementId) => achievementIds.Contains(achievementId);
}

public sealed record SeatCompetitionAttemptSnapshot(
    string AttemptId,
    string PositionId,
    string ActorId,
    string ProfileId,
    string SiteId,
    string CarrierAbilityInstanceId,
    long ExpectedPositionVersion,
    int RegularProgressTarget,
    int CriticalProgressTarget,
    int RegularProgress,
    int CriticalProgress,
    int CriticalRound,
    SeatCompetitionAttemptStatus Status);

public sealed class SeatCompetitionAttempt
{
    public SeatCompetitionAttempt(
        string attemptId,
        string positionId,
        string actorId,
        string profileId,
        string siteId,
        string carrierAbilityInstanceId,
        long expectedPositionVersion,
        int regularProgressTarget,
        int criticalProgressTarget)
    {
        RequireId(attemptId, nameof(attemptId));
        RequireId(positionId, nameof(positionId));
        RequireId(actorId, nameof(actorId));
        RequireId(profileId, nameof(profileId));
        RequireId(siteId, nameof(siteId));
        RequireId(carrierAbilityInstanceId, nameof(carrierAbilityInstanceId));
        if (expectedPositionVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedPositionVersion));
        if (regularProgressTarget <= 0)
            throw new ArgumentOutOfRangeException(nameof(regularProgressTarget));
        if (criticalProgressTarget <= 0)
            throw new ArgumentOutOfRangeException(nameof(criticalProgressTarget));

        AttemptId = attemptId;
        PositionId = positionId;
        ActorId = actorId;
        ProfileId = profileId;
        SiteId = siteId;
        CarrierAbilityInstanceId = carrierAbilityInstanceId;
        ExpectedPositionVersion = expectedPositionVersion;
        RegularProgressTarget = regularProgressTarget;
        CriticalProgressTarget = criticalProgressTarget;
        Status = SeatCompetitionAttemptStatus.Active;
    }

    public string AttemptId { get; }
    public string PositionId { get; }
    public string ActorId { get; }
    public string ProfileId { get; }
    public string SiteId { get; }
    public string CarrierAbilityInstanceId { get; }
    public long ExpectedPositionVersion { get; }
    public int RegularProgressTarget { get; }
    public int CriticalProgressTarget { get; }
    public int RegularProgress { get; private set; }
    public int CriticalProgress { get; private set; }
    public int CriticalRound { get; private set; }
    public SeatCompetitionAttemptStatus Status { get; private set; }

    public void AdvanceRegular(int amount, SeatProofProfileDefinition profile, SeatProofLedger ledger)
    {
        if (Status != SeatCompetitionAttemptStatus.Active)
            throw new InvalidOperationException("Only an active attempt can advance regular proof.");
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (!Matches(profile) || !Matches(ledger))
            throw new InvalidOperationException("Attempt profile or proof ledger does not match.");

        RegularProgress = Math.Min(RegularProgressTarget, checked(RegularProgress + amount));
        if (RegularProgress == RegularProgressTarget && profile.IsSatisfiedBy(ledger))
            Status = SeatCompetitionAttemptStatus.AwaitingRegularTickClose;
    }

    public void EnterCriticalContest()
    {
        if (Status != SeatCompetitionAttemptStatus.AwaitingRegularTickClose)
            throw new InvalidOperationException("Critical contest requires regular completion.");

        Status = SeatCompetitionAttemptStatus.CriticalContest;
        CriticalProgress = 0;
        CriticalRound = 1;
    }

    public void AdvanceCritical(int amount, SeatProofProfileDefinition profile, SeatProofLedger ledger)
    {
        if (Status != SeatCompetitionAttemptStatus.CriticalContest)
            throw new InvalidOperationException("Attempt is not in critical contest.");
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (!Matches(profile) || !Matches(ledger) || !profile.IsSatisfiedBy(ledger))
            throw new InvalidOperationException("Attempt profile or proof ledger does not match.");

        CriticalProgress = Math.Min(CriticalProgressTarget, checked(CriticalProgress + amount));
        if (CriticalProgress == CriticalProgressTarget)
            Status = SeatCompetitionAttemptStatus.AwaitingCriticalTickClose;
    }

    internal void RestartCriticalRound()
    {
        if (Status != SeatCompetitionAttemptStatus.AwaitingCriticalTickClose)
            throw new InvalidOperationException("Only simultaneous critical completion restarts a round.");

        Status = SeatCompetitionAttemptStatus.CriticalContest;
        CriticalProgress = 0;
        CriticalRound = checked(CriticalRound + 1);
    }

    internal void MarkReadyToBind()
    {
        if (Status != SeatCompetitionAttemptStatus.AwaitingRegularTickClose &&
            Status != SeatCompetitionAttemptStatus.AwaitingCriticalTickClose)
        {
            throw new InvalidOperationException("Attempt has not completed a closable stage.");
        }

        Status = SeatCompetitionAttemptStatus.ReadyToBind;
    }

    internal void MarkBound()
    {
        if (Status != SeatCompetitionAttemptStatus.ReadyToBind)
            throw new InvalidOperationException("Only a ready attempt can bind.");
        Status = SeatCompetitionAttemptStatus.Bound;
    }

    internal void Invalidate()
    {
        if (Status != SeatCompetitionAttemptStatus.Bound)
            Status = SeatCompetitionAttemptStatus.Invalidated;
    }

    internal bool Matches(SeatProofProfileDefinition profile) =>
        profile != null &&
        string.Equals(ProfileId, profile.ProfileId, StringComparison.Ordinal) &&
        RegularProgressTarget == profile.RegularProgressTarget &&
        CriticalProgressTarget == profile.CriticalProgressTarget;

    bool Matches(SeatProofLedger ledger) =>
        ledger != null && string.Equals(ActorId, ledger.ActorId, StringComparison.Ordinal);

    internal SeatCompetitionAttemptSnapshot CaptureState() => new(
        AttemptId, PositionId, ActorId, ProfileId, SiteId, CarrierAbilityInstanceId,
        ExpectedPositionVersion, RegularProgressTarget, CriticalProgressTarget,
        RegularProgress, CriticalProgress, CriticalRound, Status);

    internal static SeatCompetitionAttempt RestoreState(SeatCompetitionAttemptSnapshot snapshot)
    {
        if (snapshot == null || !Enum.IsDefined(snapshot.Status) ||
            snapshot.RegularProgress < 0 || snapshot.RegularProgress > snapshot.RegularProgressTarget ||
            snapshot.CriticalProgress < 0 || snapshot.CriticalProgress > snapshot.CriticalProgressTarget ||
            snapshot.CriticalRound < 0)
        {
            throw new ArgumentException("Invalid seat competition attempt snapshot.", nameof(snapshot));
        }

        var result = new SeatCompetitionAttempt(
            snapshot.AttemptId, snapshot.PositionId, snapshot.ActorId, snapshot.ProfileId,
            snapshot.SiteId, snapshot.CarrierAbilityInstanceId, snapshot.ExpectedPositionVersion,
            snapshot.RegularProgressTarget, snapshot.CriticalProgressTarget)
        {
            RegularProgress = snapshot.RegularProgress,
            CriticalProgress = snapshot.CriticalProgress,
            CriticalRound = snapshot.CriticalRound,
            Status = snapshot.Status,
        };
        if (!result.HasConsistentState())
            throw new ArgumentException("Inconsistent seat competition attempt snapshot.", nameof(snapshot));
        return result;
    }

    bool HasConsistentState() => Status switch
    {
        SeatCompetitionAttemptStatus.Active => CriticalProgress == 0 && CriticalRound == 0,
        SeatCompetitionAttemptStatus.AwaitingRegularTickClose =>
            RegularProgress == RegularProgressTarget && CriticalProgress == 0 && CriticalRound == 0,
        SeatCompetitionAttemptStatus.CriticalContest =>
            RegularProgress == RegularProgressTarget && CriticalProgress < CriticalProgressTarget && CriticalRound > 0,
        SeatCompetitionAttemptStatus.AwaitingCriticalTickClose =>
            RegularProgress == RegularProgressTarget && CriticalProgress == CriticalProgressTarget && CriticalRound > 0,
        SeatCompetitionAttemptStatus.ReadyToBind or SeatCompetitionAttemptStatus.Bound =>
            RegularProgress == RegularProgressTarget &&
            ((CriticalRound == 0 && CriticalProgress == 0) ||
             (CriticalRound > 0 && CriticalProgress == CriticalProgressTarget)),
        SeatCompetitionAttemptStatus.Invalidated => true,
        _ => false,
    };

    static void RequireId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty ID is required.", parameterName);
    }
}

public sealed record SeatCompetitionCompletionSnapshot(
    string PositionId,
    long WorldTick,
    IReadOnlyList<string> AttemptIds);

public sealed record SeatCompetitionCoordinatorSnapshot(
    IReadOnlyList<SeatCompetitionAttemptSnapshot> Attempts,
    IReadOnlyList<SeatCompetitionCompletionSnapshot> RegularCompletions,
    IReadOnlyList<SeatCompetitionCompletionSnapshot> CriticalCompletions,
    IReadOnlyList<SeatCompetitionCompletionSnapshot> ClosedRegularTicks,
    IReadOnlyList<SeatCompetitionCompletionSnapshot> ClosedCriticalTicks);

public sealed record SeatCompetitionCompletionKey
{
    public SeatCompetitionCompletionKey(string positionId, long worldTick)
    {
        if (string.IsNullOrWhiteSpace(positionId))
            throw new ArgumentException("A position ID is required.", nameof(positionId));
        if (worldTick < 0)
            throw new ArgumentOutOfRangeException(nameof(worldTick));
        PositionId = positionId;
        WorldTick = worldTick;
    }

    public string PositionId { get; }
    public long WorldTick { get; }
}

public sealed class SeatCompetitionCoordinator
{
    readonly Dictionary<string, SeatCompetitionAttempt> attempts = new(StringComparer.Ordinal);
    readonly Dictionary<SeatCompetitionCompletionKey, List<string>> regularCompletions = new();
    readonly Dictionary<SeatCompetitionCompletionKey, List<string>> criticalCompletions = new();
    readonly HashSet<SeatCompetitionCompletionKey> closedRegularTicks = new();
    readonly HashSet<SeatCompetitionCompletionKey> closedCriticalTicks = new();

    public void Register(SeatCompetitionAttempt attempt)
    {
        if (attempt == null)
            throw new ArgumentNullException(nameof(attempt));
        if (!attempts.TryAdd(attempt.AttemptId, attempt))
            throw new ArgumentException("Attempt ID already exists.", nameof(attempt));
    }

    public SeatCompetitionAttempt GetAttempt(string attemptId) =>
        attemptId != null && attempts.TryGetValue(attemptId, out var attempt) ? attempt : null;

    public void SubmitRegularCompletion(string attemptId, long worldTick)
    {
        var attempt = RequireAttempt(attemptId);
        if (attempt.Status != SeatCompetitionAttemptStatus.AwaitingRegularTickClose)
            throw new InvalidOperationException("Attempt has not completed the regular stage.");
        AddCompletion(regularCompletions, criticalCompletions, closedRegularTicks, attempt.PositionId, worldTick, attemptId);
    }

    public void SubmitCriticalCompletion(string attemptId, long worldTick)
    {
        var attempt = RequireAttempt(attemptId);
        if (attempt.Status != SeatCompetitionAttemptStatus.AwaitingCriticalTickClose)
            throw new InvalidOperationException("Attempt has not completed the critical stage.");
        AddCompletion(criticalCompletions, regularCompletions, closedCriticalTicks, attempt.PositionId, worldTick, attemptId);
    }

    public SeatCompetitionTickResolution CloseRegularTick(string positionId, long worldTick) =>
        CloseTick(positionId, worldTick, regularCompletions, closedRegularTicks, false);

    public SeatCompetitionTickResolution CloseCriticalTick(string positionId, long worldTick) =>
        CloseTick(positionId, worldTick, criticalCompletions, closedCriticalTicks, true);

    internal void InvalidateOthers(string positionId, string winningAttemptId)
    {
        foreach (var attempt in attempts.Values)
        {
            if (string.Equals(attempt.PositionId, positionId, StringComparison.Ordinal) &&
                !string.Equals(attempt.AttemptId, winningAttemptId, StringComparison.Ordinal))
            {
                attempt.Invalidate();
            }
        }
    }

    public SeatCompetitionCoordinatorSnapshot CaptureState() => new(
        attempts.Values.OrderBy(attempt => attempt.AttemptId, StringComparer.Ordinal)
            .Select(attempt => attempt.CaptureState()).ToArray(),
        CaptureOpen(regularCompletions),
        CaptureOpen(criticalCompletions),
        CaptureClosed(closedRegularTicks),
        CaptureClosed(closedCriticalTicks));

    public static SeatCompetitionCoordinator RestoreState(SeatCompetitionCoordinatorSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        var result = new SeatCompetitionCoordinator();
        foreach (var attempt in snapshot.Attempts ?? Array.Empty<SeatCompetitionAttemptSnapshot>())
            result.Register(SeatCompetitionAttempt.RestoreState(attempt));
        RestoreClosed(result.closedRegularTicks, snapshot.ClosedRegularTicks);
        RestoreClosed(result.closedCriticalTicks, snapshot.ClosedCriticalTicks);
        var openAttemptIds = new HashSet<string>(StringComparer.Ordinal);
        RestoreOpen(result, result.regularCompletions, result.closedRegularTicks,
            snapshot.RegularCompletions, SeatCompetitionAttemptStatus.AwaitingRegularTickClose, openAttemptIds);
        RestoreOpen(result, result.criticalCompletions, result.closedCriticalTicks,
            snapshot.CriticalCompletions, SeatCompetitionAttemptStatus.AwaitingCriticalTickClose, openAttemptIds);
        return result;
    }

    SeatCompetitionTickResolution CloseTick(
        string positionId,
        long worldTick,
        IDictionary<SeatCompetitionCompletionKey, List<string>> completions,
        ISet<SeatCompetitionCompletionKey> closedTicks,
        bool isCritical)
    {
        var key = new SeatCompetitionCompletionKey(positionId, worldTick);
        if (!closedTicks.Add(key))
            return Empty();

        var completed = TakeCompletions(completions, key);
        if (completed.Count == 0)
            return Empty();
        if (completed.Count == 1)
        {
            completed[0].MarkReadyToBind();
            return new SeatCompetitionTickResolution(
                SeatCompetitionResolutionKind.UniqueReady,
                completed[0].AttemptId,
                new[] { completed[0].AttemptId });
        }

        foreach (var attempt in completed)
        {
            if (isCritical)
                attempt.RestartCriticalRound();
            else
                attempt.EnterCriticalContest();
        }
        return new SeatCompetitionTickResolution(
            SeatCompetitionResolutionKind.CriticalContestContinues,
            "",
            completed.Select(attempt => attempt.AttemptId).OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    SeatCompetitionAttempt RequireAttempt(string attemptId) =>
        GetAttempt(attemptId) ?? throw new KeyNotFoundException($"Unknown attempt: {attemptId}");

    void AddCompletion(
        IDictionary<SeatCompetitionCompletionKey, List<string>> store,
        IReadOnlyDictionary<SeatCompetitionCompletionKey, List<string>> otherStore,
        ISet<SeatCompetitionCompletionKey> closedTicks,
        string positionId,
        long worldTick,
        string attemptId)
    {
        var key = new SeatCompetitionCompletionKey(positionId, worldTick);
        if (closedTicks.Contains(key))
            throw new InvalidOperationException("World tick is already closed.");
        if (store.Any(pair => pair.Value.Contains(attemptId, StringComparer.Ordinal) && !Equals(pair.Key, key)) ||
            otherStore.Values.Any(ids => ids.Contains(attemptId, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("Attempt already belongs to another open tick.");
        }
        if (!store.TryGetValue(key, out var ids))
        {
            ids = new List<string>();
            store.Add(key, ids);
        }
        if (!ids.Contains(attemptId, StringComparer.Ordinal))
            ids.Add(attemptId);
    }

    List<SeatCompetitionAttempt> TakeCompletions(
        IDictionary<SeatCompetitionCompletionKey, List<string>> store,
        SeatCompetitionCompletionKey key)
    {
        if (!store.Remove(key, out var ids))
            return new List<SeatCompetitionAttempt>();
        return ids.Select(RequireAttempt).ToList();
    }

    static IReadOnlyList<SeatCompetitionCompletionSnapshot> CaptureOpen(
        IReadOnlyDictionary<SeatCompetitionCompletionKey, List<string>> source) =>
        source.OrderBy(pair => pair.Key.PositionId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.WorldTick)
            .Select(pair => new SeatCompetitionCompletionSnapshot(
                pair.Key.PositionId,
                pair.Key.WorldTick,
                pair.Value.OrderBy(id => id, StringComparer.Ordinal).ToArray()))
            .ToArray();

    static IReadOnlyList<SeatCompetitionCompletionSnapshot> CaptureClosed(
        IEnumerable<SeatCompetitionCompletionKey> source) =>
        source.OrderBy(key => key.PositionId, StringComparer.Ordinal)
            .ThenBy(key => key.WorldTick)
            .Select(key => new SeatCompetitionCompletionSnapshot(key.PositionId, key.WorldTick, Array.Empty<string>()))
            .ToArray();

    static void RestoreClosed(
        ISet<SeatCompetitionCompletionKey> destination,
        IEnumerable<SeatCompetitionCompletionSnapshot> source)
    {
        foreach (var item in source ?? Array.Empty<SeatCompetitionCompletionSnapshot>())
        {
            if (item == null || item.AttemptIds == null || item.AttemptIds.Count != 0 ||
                !destination.Add(new SeatCompetitionCompletionKey(item.PositionId, item.WorldTick)))
            {
                throw new ArgumentException("Invalid closed tick snapshot.", nameof(source));
            }
        }
    }

    static void RestoreOpen(
        SeatCompetitionCoordinator coordinator,
        IDictionary<SeatCompetitionCompletionKey, List<string>> destination,
        ISet<SeatCompetitionCompletionKey> closed,
        IEnumerable<SeatCompetitionCompletionSnapshot> source,
        SeatCompetitionAttemptStatus expectedStatus,
        ISet<string> openAttemptIds)
    {
        foreach (var item in source ?? Array.Empty<SeatCompetitionCompletionSnapshot>())
        {
            var key = item == null ? null : new SeatCompetitionCompletionKey(item.PositionId, item.WorldTick);
            if (item == null || item.AttemptIds == null || item.AttemptIds.Count == 0 ||
                key == null || closed.Contains(key) || destination.ContainsKey(key))
            {
                throw new ArgumentException("Invalid open completion snapshot.", nameof(source));
            }

            var ids = new List<string>();
            foreach (var attemptId in item.AttemptIds)
            {
                var attempt = coordinator.GetAttempt(attemptId);
                if (string.IsNullOrWhiteSpace(attemptId) || attempt == null ||
                    !openAttemptIds.Add(attemptId) || attempt.Status != expectedStatus ||
                    !string.Equals(attempt.PositionId, item.PositionId, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Invalid open completion attempt.", nameof(source));
                }
                ids.Add(attemptId);
            }
            destination.Add(key, ids);
        }
    }

    static SeatCompetitionTickResolution Empty() => new(
        SeatCompetitionResolutionKind.NoCompletion,
        "",
        Array.Empty<string>());
}

public sealed record SeatCompetitionPositionSnapshot(
    string PositionId,
    string ProfileId,
    GoldenCoreSeatType SeatType,
    string HolderActorId,
    long Version);

public sealed class SeatCompetitionPositionRecord
{
    public SeatCompetitionPositionRecord(
        string positionId,
        string profileId,
        GoldenCoreSeatType seatType,
        long version = 0,
        string holderActorId = "")
    {
        if (string.IsNullOrWhiteSpace(positionId) || string.IsNullOrWhiteSpace(profileId) ||
            (!string.IsNullOrEmpty(holderActorId) && string.IsNullOrWhiteSpace(holderActorId)) ||
            version < 0)
        {
            throw new ArgumentException("Position input is invalid.");
        }
        PositionId = positionId;
        ProfileId = profileId;
        SeatType = seatType;
        Version = version;
        HolderActorId = string.IsNullOrEmpty(holderActorId) ? "" : holderActorId;
    }

    public string PositionId { get; }
    public string ProfileId { get; }
    public GoldenCoreSeatType SeatType { get; }
    public string HolderActorId { get; private set; }
    public long Version { get; private set; }
    internal bool IsAvailable => string.IsNullOrEmpty(HolderActorId);
    internal bool CanAdvanceVersion => Version < long.MaxValue;

    public void AdvanceVersionForWorldChange()
    {
        Version = checked(Version + 1);
    }

    internal void Bind(string actorId)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(actorId))
            throw new InvalidOperationException("Position binding is invalid.");
        HolderActorId = actorId;
        Version = checked(Version + 1);
    }

    internal SeatCompetitionPositionSnapshot CaptureState() =>
        new(PositionId, ProfileId, SeatType, HolderActorId, Version);

    internal static SeatCompetitionPositionRecord RestoreState(SeatCompetitionPositionSnapshot snapshot) =>
        snapshot == null
            ? throw new ArgumentNullException(nameof(snapshot))
            : new SeatCompetitionPositionRecord(
                snapshot.PositionId,
                snapshot.ProfileId,
                snapshot.SeatType,
                snapshot.Version,
                snapshot.HolderActorId);
}

public sealed record SeatCompetitionCoreSeatBinding(
    string PositionId,
    GoldenCoreSeatType SeatType,
    string CarrierAbilityInstanceId);

public sealed record SeatCompetitionCoreSnapshot(
    string ActorId,
    string CoreBindingId,
    IReadOnlyList<SeatCompetitionCoreSeatBinding> Bindings);

public sealed class SeatCompetitionCoreState
{
    readonly List<SeatCompetitionCoreSeatBinding> bindings = new();

    public SeatCompetitionCoreState(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("An actor ID is required.", nameof(actorId));
        ActorId = actorId;
    }

    public string ActorId { get; }
    public string CoreBindingId { get; private set; } = "";
    public IReadOnlyList<SeatCompetitionCoreSeatBinding> Bindings => bindings;

    internal bool CanAdd(
        SeatCompetitionPositionRecord position,
        SeatCompetitionAttempt attempt,
        string newCoreBindingId)
    {
        if (position == null || attempt == null ||
            !string.Equals(ActorId, attempt.ActorId, StringComparison.Ordinal) ||
            bindings.Count >= 3 ||
            (string.IsNullOrEmpty(CoreBindingId) && string.IsNullOrWhiteSpace(newCoreBindingId)) ||
            (!string.IsNullOrEmpty(CoreBindingId) && !string.IsNullOrWhiteSpace(newCoreBindingId)))
        {
            return false;
        }

        return bindings.All(binding =>
            binding.SeatType != position.SeatType &&
            !string.Equals(binding.PositionId, position.PositionId, StringComparison.Ordinal) &&
            !string.Equals(binding.CarrierAbilityInstanceId, attempt.CarrierAbilityInstanceId, StringComparison.Ordinal));
    }

    internal void Add(SeatCompetitionPositionRecord position, SeatCompetitionAttempt attempt, string newCoreBindingId)
    {
        if (string.IsNullOrEmpty(CoreBindingId))
            CoreBindingId = newCoreBindingId;
        bindings.Add(new SeatCompetitionCoreSeatBinding(
            position.PositionId,
            position.SeatType,
            attempt.CarrierAbilityInstanceId));
    }

    public SeatCompetitionCoreSnapshot CaptureState() => new(
        ActorId,
        CoreBindingId,
        bindings.OrderBy(binding => binding.PositionId, StringComparer.Ordinal).ToArray());

    public static SeatCompetitionCoreState RestoreState(SeatCompetitionCoreSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        var result = new SeatCompetitionCoreState(snapshot.ActorId);
        var source = snapshot.Bindings ?? Array.Empty<SeatCompetitionCoreSeatBinding>();
        if ((string.IsNullOrWhiteSpace(snapshot.CoreBindingId) && source.Count != 0) ||
            (!string.IsNullOrWhiteSpace(snapshot.CoreBindingId) && (source.Count < 1 || source.Count > 3)))
        {
            throw new ArgumentException("Core and position bindings are inconsistent.", nameof(snapshot));
        }

        var types = new HashSet<GoldenCoreSeatType>();
        var positions = new HashSet<string>(StringComparer.Ordinal);
        var carriers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in source)
        {
            if (binding == null || string.IsNullOrWhiteSpace(binding.PositionId) ||
                string.IsNullOrWhiteSpace(binding.CarrierAbilityInstanceId) ||
                !types.Add(binding.SeatType) || !positions.Add(binding.PositionId) ||
                !carriers.Add(binding.CarrierAbilityInstanceId))
            {
                throw new ArgumentException("Core bindings are invalid or duplicated.", nameof(snapshot));
            }
        }

        result.CoreBindingId = string.IsNullOrWhiteSpace(snapshot.CoreBindingId) ? "" : snapshot.CoreBindingId;
        result.bindings.AddRange(source);
        return result;
    }
}

public sealed class SeatCompetitionBindRequest
{
    public SeatCompetitionBindRequest(
        string attemptId,
        string newCoreBindingId,
        bool siteStillValid,
        bool realityAnchorStillValid,
        bool carrierStillCompatible,
        bool keyEventsResolved)
    {
        if (string.IsNullOrWhiteSpace(attemptId))
            throw new ArgumentException("An attempt ID is required.", nameof(attemptId));
        AttemptId = attemptId;
        NewCoreBindingId = string.IsNullOrWhiteSpace(newCoreBindingId) ? "" : newCoreBindingId;
        SiteStillValid = siteStillValid;
        RealityAnchorStillValid = realityAnchorStillValid;
        CarrierStillCompatible = carrierStillCompatible;
        KeyEventsResolved = keyEventsResolved;
    }

    public string AttemptId { get; }
    public string NewCoreBindingId { get; }
    public bool SiteStillValid { get; }
    public bool RealityAnchorStillValid { get; }
    public bool CarrierStillCompatible { get; }
    public bool KeyEventsResolved { get; }
}

public sealed record SeatCompetitionPositionRegistrySnapshot(
    IReadOnlyList<SeatCompetitionPositionSnapshot> Positions);

public sealed class SeatCompetitionPositionRegistry
{
    readonly Dictionary<string, SeatCompetitionPositionRecord> positions = new(StringComparer.Ordinal);

    public void Add(SeatCompetitionPositionRecord position)
    {
        if (position == null)
            throw new ArgumentNullException(nameof(position));
        if (!positions.TryAdd(position.PositionId, position))
            throw new ArgumentException("Position ID already exists.", nameof(position));
    }

    public SeatCompetitionPositionRecord Get(string positionId) =>
        positionId != null && positions.TryGetValue(positionId, out var position) ? position : null;

    public SeatCompetitionBindResult TryBind(
        SeatCompetitionBindRequest request,
        SeatProofProfileDefinition profile,
        SeatProofLedger ledger,
        SeatCompetitionCoreState core,
        SeatCompetitionCoordinator coordinator)
    {
        if (request == null || profile == null || ledger == null || core == null || coordinator == null)
            throw new ArgumentNullException("Binding input is required.");

        var attempt = coordinator.GetAttempt(request.AttemptId);
        if (attempt == null || attempt.Status != SeatCompetitionAttemptStatus.ReadyToBind)
            return Failed(SeatCompetitionBindFailureReason.AttemptNotReady);

        var position = Get(attempt.PositionId);
        if (position == null || !position.IsAvailable)
            return Failed(SeatCompetitionBindFailureReason.PositionUnavailable);
        if (position.Version != attempt.ExpectedPositionVersion)
            return Failed(SeatCompetitionBindFailureReason.StalePositionVersion);

        if (!position.CanAdvanceVersion || !attempt.Matches(profile) ||
            !string.Equals(position.ProfileId, profile.ProfileId, StringComparison.Ordinal) ||
            position.SeatType != profile.SeatType ||
            !string.Equals(ledger.ActorId, attempt.ActorId, StringComparison.Ordinal) ||
            !request.SiteStillValid || !request.RealityAnchorStillValid ||
            !request.CarrierStillCompatible || !request.KeyEventsResolved ||
            !profile.IsSatisfiedBy(ledger))
        {
            return Failed(SeatCompetitionBindFailureReason.PreconditionsNotMet);
        }

        if (!core.CanAdd(position, attempt, request.NewCoreBindingId))
            return Failed(SeatCompetitionBindFailureReason.CoreInvariantViolation);

        // 以上检查完成后，以下四项是不可分割的成功提交；任何拒绝均不会写入半状态。
        core.Add(position, attempt, request.NewCoreBindingId);
        position.Bind(attempt.ActorId);
        attempt.MarkBound();
        coordinator.InvalidateOthers(position.PositionId, attempt.AttemptId);
        return new SeatCompetitionBindResult(true, SeatCompetitionBindFailureReason.None, position.PositionId);
    }

    public SeatCompetitionPositionRegistrySnapshot CaptureState() => new(
        positions.Values.OrderBy(position => position.PositionId, StringComparer.Ordinal)
            .Select(position => position.CaptureState()).ToArray());

    public static SeatCompetitionPositionRegistry RestoreState(SeatCompetitionPositionRegistrySnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        var result = new SeatCompetitionPositionRegistry();
        foreach (var position in snapshot.Positions ?? Array.Empty<SeatCompetitionPositionSnapshot>())
            result.Add(SeatCompetitionPositionRecord.RestoreState(position));
        return result;
    }

    static SeatCompetitionBindResult Failed(SeatCompetitionBindFailureReason reason) =>
        new(false, reason);
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

// N-STATE-01: temporary combat effects are represented only by this carrier.
public enum CombatStatusCarrierKind
{
    TemporaryStatus,
    StanceState,
    ProcessState,
    TaskMarker,
    LifeState,
}

public enum CombatStatusPolarity
{
    Buff,
    Debuff,
    Mixed,
}

public enum CombatStatusTag
{
    Attribute,
    Offense,
    Defense,
    Mobility,
    Action,
    Control,
    Perception,
    Resource,
    Recovery,
    Periodic,
}

public enum CombatStatusSourceKind
{
    SelfAbility,
    OtherAbility,
    EquipmentProc,
    Environment,
    SystemCost,
}

public enum CombatStatusRemovalPolicy
{
    Normal,
    Anchored,
    SourceOnly,
}

public enum CombatStateLedgerScope
{
    TargetLocal,
    ExternalCommitted,
    ProtectedHistory,
}

public enum CausalDebtSettlementState
{
    Pending,
    Repaid,
    Defaulted,
}

/// <summary>Immutable rule data for a temporary status. Missing content is rejected instead of inferred at runtime.</summary>
public sealed record CombatTemporaryStatusProfile(
    string StatusId,
    CombatStatusCarrierKind CarrierKind,
    CombatStatusPolarity Polarity,
    IReadOnlyList<CombatStatusTag> Tags,
    CombatStatusSourceKind SourceKind,
    CombatStatusRemovalPolicy RemovalPolicy,
    int DefinitionVersion)
{
    public bool IsWellFormed =>
        !string.IsNullOrWhiteSpace(StatusId) &&
        CarrierKind == CombatStatusCarrierKind.TemporaryStatus &&
        Enum.IsDefined(typeof(CombatStatusPolarity), Polarity) &&
        Tags != null && Tags.Count > 0 && Tags.Distinct().Count() == Tags.Count &&
        Tags.All(tag => Enum.IsDefined(typeof(CombatStatusTag), tag)) &&
        Enum.IsDefined(typeof(CombatStatusSourceKind), SourceKind) &&
        Enum.IsDefined(typeof(CombatStatusRemovalPolicy), RemovalPolicy) &&
        DefinitionVersion > 0 &&
        (RemovalPolicy != CombatStatusRemovalPolicy.SourceOnly || SourceKind == CombatStatusSourceKind.SystemCost);

    public string InvalidReason => CarrierKind != CombatStatusCarrierKind.TemporaryStatus
        ? "STATE_CARRIER_NOT_TEMPORARY_STATUS"
        : !IsWellFormed
            ? "STATE_STATUS_PROFILE_INVALID"
            : "";
}

/// <summary>All capacities are declared by profile data; the runtime has no fallback values.</summary>
public sealed record CombatStateRulesConfig(
    int CheckpointCapacity,
    int AutomaticResponseCapacity,
    int MaxCausalGraftRules)
{
    public bool IsWellFormed => CheckpointCapacity > 0 && AutomaticResponseCapacity >= 0 && MaxCausalGraftRules is > 0 and <= 2;
}

public sealed record CombatTemporaryStatus(
    string InstanceId,
    CombatTemporaryStatusProfile Profile,
    string SourceCombatantId,
    string TargetCombatantId,
    int RemainingActions);

public sealed record CombatStatusApplication(
    string InstanceId,
    CombatTemporaryStatusProfile Profile,
    string SourceCombatantId,
    string TargetCombatantId,
    int RemainingActions);

public sealed record CombatStatusMutation(
    bool IsApplied,
    string Reason,
    CombatTemporaryStatus Status)
{
    internal static CombatStatusMutation Rejected(string reason) => new(false, reason, null);
}

/// <summary>Only character-local facts are eligible for a rewind checkpoint.</summary>
public sealed record CombatLocalStateSnapshot(
    string PositionId,
    string MovementMode,
    string AltitudeBand,
    string LifeState,
    int HitPoints,
    int Mana,
    string StanceStateId,
    string ProcessStateId,
    IReadOnlyDictionary<string, int> Cooldowns,
    IReadOnlyDictionary<string, int> Charges,
    IReadOnlyDictionary<string, int> Consumables);

public sealed record CombatStateCheckpoint(
    string CheckpointId,
    int OwnActionSequence,
    CombatLocalStateSnapshot LocalState,
    IReadOnlyList<CombatTemporaryStatus> TemporaryStatuses);

/// <summary>Costs from non-local results are replayed after a rewind; no world result is stored here.</summary>
public sealed record CombatStateLedgerEntry(
    string EntryId,
    int OwnActionSequence,
    CombatStateLedgerScope Scope,
    int HitPointCost,
    int ManaCost);

public sealed record CombatStateMutationResult(bool IsApplied, string Reason)
{
    internal static CombatStateMutationResult Rejected(string reason) => new(false, reason);
}

public sealed record CombatStateRewindResolution(
    bool IsApplied,
    string Reason,
    string CheckpointId,
    int ReappliedExternalEntryCount,
    int ReappliedProtectedEntryCount)
{
    internal static CombatStateRewindResolution Rejected(string reason, string checkpointId) =>
        new(false, reason, checkpointId, 0, 0);
}

public sealed record AutomaticResponseAttempt(
    string RootEventId,
    string RuleId,
    bool HasLegalTarget,
    bool HasResources,
    bool IsCooldownReady,
    bool MeetsAllConditions,
    bool EntersSettlement);

public sealed record AutomaticResponseResolution(bool IsAccepted, string Reason, int RemainingCapacity)
{
    internal static AutomaticResponseResolution Rejected(string reason, int remainingCapacity) =>
        new(false, reason, remainingCapacity);
}

public sealed record CausalDebtSpec(
    string DebtId,
    string OriginalActionInstanceId,
    string ResultInstanceId,
    string HolderCombatantId,
    int ResourceCost,
    int ResultBudget,
    int DueOwnActionSequence);

public sealed record CausalDebtSnapshot(
    string DebtId,
    string OriginalActionInstanceId,
    string ResultInstanceId,
    string OriginalHolderCombatantId,
    string CurrentHolderCombatantId,
    int OriginalResourceCost,
    int OutstandingResourceCost,
    int ResultBudget,
    int DueOwnActionSequence,
    CausalDebtSettlementState State);

public sealed record CausalDebtResolution(
    bool IsApplied,
    string Reason,
    CausalDebtSnapshot Debt)
{
    internal static CausalDebtResolution Rejected(string reason) => new(false, reason, null);
}

/// <summary>
/// One character owns one runtime instance. It owns temporary statuses, local checkpoints, response capacity and debts;
/// it deliberately has no world-state store, so a local rewind cannot roll external facts back.
/// </summary>
public sealed class CombatStateRuntime
{
    sealed class MutableCausalDebt
    {
        public MutableCausalDebt(CausalDebtSpec spec)
        {
            DebtId = spec.DebtId;
            OriginalActionInstanceId = spec.OriginalActionInstanceId;
            ResultInstanceId = spec.ResultInstanceId;
            OriginalHolderCombatantId = spec.HolderCombatantId;
            CurrentHolderCombatantId = spec.HolderCombatantId;
            OriginalResourceCost = spec.ResourceCost;
            OutstandingResourceCost = spec.ResourceCost;
            ResultBudget = spec.ResultBudget;
            DueOwnActionSequence = spec.DueOwnActionSequence;
        }

        public string DebtId { get; }
        public string OriginalActionInstanceId { get; }
        public string ResultInstanceId { get; }
        public string OriginalHolderCombatantId { get; }
        public string CurrentHolderCombatantId { get; set; }
        public int OriginalResourceCost { get; }
        public int OutstandingResourceCost { get; set; }
        public int ResultBudget { get; }
        public int DueOwnActionSequence { get; }
        public CausalDebtSettlementState State { get; set; } = CausalDebtSettlementState.Pending;

        public CausalDebtSnapshot Snapshot() => new(
            DebtId,
            OriginalActionInstanceId,
            ResultInstanceId,
            OriginalHolderCombatantId,
            CurrentHolderCombatantId,
            OriginalResourceCost,
            OutstandingResourceCost,
            ResultBudget,
            DueOwnActionSequence,
            State);
    }

    readonly CombatStateRulesConfig rules;
    readonly Dictionary<string, CombatTemporaryStatus> statuses = new(StringComparer.Ordinal);
    readonly Dictionary<string, CombatStatusPolarity> polarityByStatusId = new(StringComparer.Ordinal);
    readonly Queue<CombatStateCheckpoint> checkpoints = new();
    readonly Dictionary<string, CombatStateLedgerEntry> ledgerEntries = new(StringComparer.Ordinal);
    readonly Dictionary<string, CombatStateRewindResolution> processedRewinds = new(StringComparer.Ordinal);
    readonly Dictionary<string, AutomaticResponseResolution> acceptedResponses = new(StringComparer.Ordinal);
    readonly Dictionary<string, MutableCausalDebt> debts = new(StringComparer.Ordinal);
    readonly Dictionary<string, CausalDebtResolution> processedDebtOperations = new(StringComparer.Ordinal);
    CombatLocalStateSnapshot currentState;

    public CombatStateRuntime(string ownerCombatantId, CombatStateRulesConfig rules)
    {
        if (string.IsNullOrWhiteSpace(ownerCombatantId))
            throw new ArgumentException("A combat-state owner id is required.", nameof(ownerCombatantId));
        if (rules == null || !rules.IsWellFormed)
            throw new ArgumentException("Combat-state rules must declare valid checkpoint and response capacities.", nameof(rules));

        OwnerCombatantId = ownerCombatantId;
        this.rules = rules;
        AutomaticResponseCapacity = rules.AutomaticResponseCapacity;
    }

    public string OwnerCombatantId { get; }
    public int AutomaticResponseCapacity { get; private set; }
    public CombatLocalStateSnapshot CurrentLocalState => CloneLocalState(RequireCurrentState());
    public IReadOnlyList<CombatTemporaryStatus> TemporaryStatuses =>
        statuses.Values.OrderBy(status => status.InstanceId, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<CombatStateCheckpoint> Checkpoints => checkpoints.ToArray();
    public IReadOnlyList<CausalDebtSnapshot> CausalDebts =>
        debts.Values.OrderBy(debt => debt.DebtId, StringComparer.Ordinal).Select(debt => debt.Snapshot()).ToArray();

    public void Initialize(CombatLocalStateSnapshot initialState)
    {
        currentState = CloneAndValidateLocalState(initialState);
        statuses.Clear();
        polarityByStatusId.Clear();
        checkpoints.Clear();
        ledgerEntries.Clear();
        processedRewinds.Clear();
        acceptedResponses.Clear();
        debts.Clear();
        processedDebtOperations.Clear();
        AutomaticResponseCapacity = rules.AutomaticResponseCapacity;
    }

    /// <summary>Syncs simulation-owned scalar data without replacing the sole temporary-status carrier.</summary>
    public void SynchronizeLocalState(CombatLocalStateSnapshot localState)
    {
        currentState = CloneAndValidateLocalState(localState);
    }

    public void RecordOwnActionCheckpoint(string checkpointId, int ownActionSequence)
    {
        RequireCurrentState();
        if (string.IsNullOrWhiteSpace(checkpointId) || ownActionSequence < 0)
            throw new ArgumentException("A checkpoint requires a stable id and non-negative own-action sequence.");
        if (checkpoints.Any(checkpoint => string.Equals(checkpoint.CheckpointId, checkpointId, StringComparison.Ordinal)))
            throw new InvalidOperationException("A self-action checkpoint id may only be recorded once.");

        checkpoints.Enqueue(new CombatStateCheckpoint(
            checkpointId,
            ownActionSequence,
            CloneLocalState(currentState),
            statuses.Values.OrderBy(status => status.InstanceId, StringComparer.Ordinal).ToArray()));
        while (checkpoints.Count > rules.CheckpointCapacity)
            checkpoints.Dequeue();
    }

    public void CompleteOwnActiveAction()
    {
        RequireCurrentState();
        AutomaticResponseCapacity = rules.AutomaticResponseCapacity;
    }

    public CombatStatusMutation TryApplyTemporaryStatus(CombatStatusApplication application)
    {
        RequireCurrentState();
        if (application == null || application.Profile == null || string.IsNullOrWhiteSpace(application.InstanceId) ||
            string.IsNullOrWhiteSpace(application.SourceCombatantId) || string.IsNullOrWhiteSpace(application.TargetCombatantId) ||
            application.RemainingActions <= 0)
        {
            return CombatStatusMutation.Rejected("STATE_STATUS_APPLICATION_INVALID");
        }
        if (!application.Profile.IsWellFormed)
            return CombatStatusMutation.Rejected(application.Profile.InvalidReason);
        if (!string.Equals(application.TargetCombatantId, OwnerCombatantId, StringComparison.Ordinal))
            return CombatStatusMutation.Rejected("STATE_TARGET_OWNER_MISMATCH");
        if (statuses.ContainsKey(application.InstanceId))
            return CombatStatusMutation.Rejected("STATE_INSTANCE_ALREADY_EXISTS");
        if (polarityByStatusId.TryGetValue(application.Profile.StatusId, out var knownPolarity) &&
            knownPolarity != application.Profile.Polarity)
        {
            return CombatStatusMutation.Rejected("STATE_POLARITY_CONFLICT");
        }

        var status = new CombatTemporaryStatus(
            application.InstanceId,
            application.Profile,
            application.SourceCombatantId,
            application.TargetCombatantId,
            application.RemainingActions);
        statuses.Add(status.InstanceId, status);
        polarityByStatusId[status.Profile.StatusId] = status.Profile.Polarity;
        return new CombatStatusMutation(true, "STATE_STATUS_APPLIED", status);
    }

    public CombatStateMutationResult TryRemoveTemporaryStatus(string instanceId, bool conflictAuthorized)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || !statuses.TryGetValue(instanceId, out var status))
            return CombatStateMutationResult.Rejected("STATE_STATUS_INSTANCE_UNKNOWN");
        if (status.Profile.RemovalPolicy == CombatStatusRemovalPolicy.SourceOnly)
            return CombatStateMutationResult.Rejected("STATE_SOURCE_ONLY_PROTECTED");
        if (status.Profile.RemovalPolicy == CombatStatusRemovalPolicy.Anchored && !conflictAuthorized)
            return CombatStateMutationResult.Rejected("STATE_ANCHORED_CONFLICT_REQUIRED");

        statuses.Remove(instanceId);
        return new CombatStateMutationResult(true, "STATE_STATUS_REMOVED");
    }

    public CombatStateMutationResult RecordLedgerEntry(CombatStateLedgerEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.EntryId) || entry.OwnActionSequence < 0 ||
            entry.HitPointCost < 0 || entry.ManaCost < 0 ||
            !Enum.IsDefined(typeof(CombatStateLedgerScope), entry.Scope))
        {
            return CombatStateMutationResult.Rejected("STATE_LEDGER_ENTRY_INVALID");
        }
        if (!ledgerEntries.TryAdd(entry.EntryId, entry))
            return CombatStateMutationResult.Rejected("STATE_LEDGER_ENTRY_DUPLICATE");
        return new CombatStateMutationResult(true, "STATE_LEDGER_ENTRY_RECORDED");
    }

    public CombatStateRewindResolution Rewind(string rewindEventId, string checkpointId)
    {
        RequireCurrentState();
        if (string.IsNullOrWhiteSpace(rewindEventId) || string.IsNullOrWhiteSpace(checkpointId))
            return CombatStateRewindResolution.Rejected("STATE_REWIND_REQUEST_INVALID", checkpointId ?? "");
        if (processedRewinds.TryGetValue(rewindEventId, out var processed))
            return processed;

        var checkpoint = checkpoints.SingleOrDefault(candidate =>
            string.Equals(candidate.CheckpointId, checkpointId, StringComparison.Ordinal));
        if (checkpoint == null)
            return CombatStateRewindResolution.Rejected("STATE_REWIND_CHECKPOINT_UNKNOWN", checkpointId);

        RestoreCheckpoint(checkpoint);
        int externalCount = 0;
        int protectedCount = 0;
        foreach (var entry in ledgerEntries.Values
                     .Where(entry => entry.OwnActionSequence > checkpoint.OwnActionSequence &&
                                     entry.Scope != CombatStateLedgerScope.TargetLocal)
                     .OrderBy(entry => entry.OwnActionSequence)
                     .ThenBy(entry => entry.EntryId, StringComparer.Ordinal))
        {
            currentState = currentState with
            {
                HitPoints = Math.Max(0, currentState.HitPoints - entry.HitPointCost),
                Mana = Math.Max(0, currentState.Mana - entry.ManaCost),
            };
            if (entry.Scope == CombatStateLedgerScope.ExternalCommitted)
                externalCount++;
            else
                protectedCount++;
        }

        var resolution = new CombatStateRewindResolution(
            true,
            "STATE_REWIND_APPLIED",
            checkpointId,
            externalCount,
            protectedCount);
        processedRewinds.Add(rewindEventId, resolution);
        return resolution;
    }

    public AutomaticResponseResolution TryConsumeAutomaticResponse(AutomaticResponseAttempt attempt)
    {
        if (attempt == null || string.IsNullOrWhiteSpace(attempt.RootEventId) || string.IsNullOrWhiteSpace(attempt.RuleId))
            return AutomaticResponseResolution.Rejected("CAUSAL_RESPONSE_REQUEST_INVALID", AutomaticResponseCapacity);

        string responseKey = $"{attempt.RootEventId}\u001f{attempt.RuleId}";
        if (acceptedResponses.TryGetValue(responseKey, out var accepted))
            return accepted;
        if (!attempt.HasLegalTarget)
            return AutomaticResponseResolution.Rejected("CAUSAL_RESPONSE_TARGET_INVALID", AutomaticResponseCapacity);
        if (!attempt.HasResources)
            return AutomaticResponseResolution.Rejected("CAUSAL_RESPONSE_RESOURCE_UNAVAILABLE", AutomaticResponseCapacity);
        if (!attempt.IsCooldownReady)
            return AutomaticResponseResolution.Rejected("CAUSAL_RESPONSE_COOLDOWN_UNAVAILABLE", AutomaticResponseCapacity);
        if (!attempt.MeetsAllConditions)
            return AutomaticResponseResolution.Rejected("CAUSAL_RESPONSE_CONDITION_FAILED", AutomaticResponseCapacity);
        if (!attempt.EntersSettlement)
            return AutomaticResponseResolution.Rejected("CAUSAL_RESPONSE_NOT_SETTLED", AutomaticResponseCapacity);
        if (AutomaticResponseCapacity <= 0)
            return AutomaticResponseResolution.Rejected("CAUSAL_RESPONSE_CAPACITY_EXHAUSTED", AutomaticResponseCapacity);

        AutomaticResponseCapacity--;
        var resolution = new AutomaticResponseResolution(true, "CAUSAL_RESPONSE_ACCEPTED", AutomaticResponseCapacity);
        acceptedResponses.Add(responseKey, resolution);
        return resolution;
    }

    public CausalDebtResolution CreateCausalDebt(CausalDebtSpec spec)
    {
        RequireCurrentState();
        if (!IsValidDebtSpec(spec))
            return CausalDebtResolution.Rejected("CAUSAL_DEBT_SPEC_INVALID");
        if (!string.Equals(spec.HolderCombatantId, OwnerCombatantId, StringComparison.Ordinal))
            return CausalDebtResolution.Rejected("CAUSAL_DEBT_HOLDER_MISMATCH");
        if (debts.TryGetValue(spec.DebtId, out var existing))
        {
            return SameDebtSpec(existing, spec)
                ? new CausalDebtResolution(true, "CAUSAL_DEBT_ALREADY_REGISTERED", existing.Snapshot())
                : CausalDebtResolution.Rejected("CAUSAL_DEBT_ID_CONFLICT");
        }

        var debt = new MutableCausalDebt(spec);
        debts.Add(debt.DebtId, debt);
        return new CausalDebtResolution(true, "CAUSAL_DEBT_CREATED", debt.Snapshot());
    }

    public CausalDebtResolution TransferCausalDebt(
        string transferOperationId,
        string debtId,
        CombatStateRuntime recipient,
        bool recipientIsRealParticipant,
        bool recipientIsCompatible)
    {
        if (string.IsNullOrWhiteSpace(transferOperationId) || string.IsNullOrWhiteSpace(debtId))
            return CausalDebtResolution.Rejected("CAUSAL_DEBT_TRANSFER_INVALID");
        if (processedDebtOperations.TryGetValue(transferOperationId, out var processed))
            return processed;
        if (!recipientIsRealParticipant || !recipientIsCompatible || recipient == null || ReferenceEquals(recipient, this))
            return RememberDebtOperation(transferOperationId, CausalDebtResolution.Rejected("CAUSAL_DEBT_TRANSFER_INELIGIBLE"));
        if (!debts.TryGetValue(debtId, out var debt))
            return RememberDebtOperation(transferOperationId, CausalDebtResolution.Rejected("CAUSAL_DEBT_UNKNOWN"));
        if (debt.State != CausalDebtSettlementState.Pending)
            return RememberDebtOperation(transferOperationId, CausalDebtResolution.Rejected("CAUSAL_DEBT_ALREADY_CLOSED"));
        if (recipient.debts.ContainsKey(debtId))
            return RememberDebtOperation(transferOperationId, CausalDebtResolution.Rejected("CAUSAL_DEBT_RECIPIENT_CONFLICT"));

        debts.Remove(debtId);
        debt.CurrentHolderCombatantId = recipient.OwnerCombatantId;
        recipient.debts.Add(debtId, debt);
        return RememberDebtOperation(transferOperationId,
            new CausalDebtResolution(true, "CAUSAL_DEBT_TRANSFERRED", debt.Snapshot()));
    }

    public CausalDebtResolution RepayCausalDebt(string repaymentOperationId, string debtId)
    {
        RequireCurrentState();
        if (string.IsNullOrWhiteSpace(repaymentOperationId) || string.IsNullOrWhiteSpace(debtId))
            return CausalDebtResolution.Rejected("CAUSAL_DEBT_REPAYMENT_INVALID");
        if (processedDebtOperations.TryGetValue(repaymentOperationId, out var processed))
            return processed;
        if (!debts.TryGetValue(debtId, out var debt))
            return RememberDebtOperation(repaymentOperationId, CausalDebtResolution.Rejected("CAUSAL_DEBT_UNKNOWN"));
        if (debt.State != CausalDebtSettlementState.Pending)
            return RememberDebtOperation(repaymentOperationId, CausalDebtResolution.Rejected("CAUSAL_DEBT_ALREADY_CLOSED"));
        if (currentState.Mana < debt.OutstandingResourceCost)
            return RememberDebtOperation(repaymentOperationId, CausalDebtResolution.Rejected("CAUSAL_DEBT_PAYMENT_UNAVAILABLE"));

        currentState = currentState with { Mana = currentState.Mana - debt.OutstandingResourceCost };
        debt.OutstandingResourceCost = 0;
        debt.State = CausalDebtSettlementState.Repaid;
        return RememberDebtOperation(repaymentOperationId,
            new CausalDebtResolution(true, "CAUSAL_DEBT_REPAID", debt.Snapshot()));
    }

    public CausalDebtResolution DefaultCausalDebt(string defaultOperationId, string debtId)
    {
        if (string.IsNullOrWhiteSpace(defaultOperationId) || string.IsNullOrWhiteSpace(debtId))
            return CausalDebtResolution.Rejected("CAUSAL_DEBT_DEFAULT_INVALID");
        if (processedDebtOperations.TryGetValue(defaultOperationId, out var processed))
            return processed;
        if (!debts.TryGetValue(debtId, out var debt))
            return RememberDebtOperation(defaultOperationId, CausalDebtResolution.Rejected("CAUSAL_DEBT_UNKNOWN"));
        if (debt.State != CausalDebtSettlementState.Pending)
            return RememberDebtOperation(defaultOperationId, CausalDebtResolution.Rejected("CAUSAL_DEBT_ALREADY_CLOSED"));

        debt.State = CausalDebtSettlementState.Defaulted;
        return RememberDebtOperation(defaultOperationId,
            new CausalDebtResolution(true, "CAUSAL_DEBT_DEFAULTED", debt.Snapshot()));
    }

    public CausalDebtSnapshot GetCausalDebt(string debtId) =>
        debts.TryGetValue(debtId, out var debt) ? debt.Snapshot() : null;

    void RestoreCheckpoint(CombatStateCheckpoint checkpoint)
    {
        currentState = CloneLocalState(checkpoint.LocalState);
        statuses.Clear();
        foreach (var status in checkpoint.TemporaryStatuses)
            statuses.Add(status.InstanceId, status);
    }

    CausalDebtResolution RememberDebtOperation(string operationId, CausalDebtResolution resolution)
    {
        processedDebtOperations.Add(operationId, resolution);
        return resolution;
    }

    CombatLocalStateSnapshot RequireCurrentState() => currentState ??
        throw new InvalidOperationException("Combat state must be initialized before it is used.");

    static bool IsValidDebtSpec(CausalDebtSpec spec) =>
        spec != null && !string.IsNullOrWhiteSpace(spec.DebtId) &&
        !string.IsNullOrWhiteSpace(spec.OriginalActionInstanceId) &&
        !string.IsNullOrWhiteSpace(spec.ResultInstanceId) &&
        !string.IsNullOrWhiteSpace(spec.HolderCombatantId) &&
        spec.ResourceCost >= 0 && spec.ResultBudget >= 0 && spec.DueOwnActionSequence > 0;

    static bool SameDebtSpec(MutableCausalDebt debt, CausalDebtSpec spec) =>
        debt.OriginalActionInstanceId == spec.OriginalActionInstanceId &&
        debt.ResultInstanceId == spec.ResultInstanceId &&
        debt.OriginalHolderCombatantId == spec.HolderCombatantId &&
        debt.OriginalResourceCost == spec.ResourceCost &&
        debt.ResultBudget == spec.ResultBudget &&
        debt.DueOwnActionSequence == spec.DueOwnActionSequence;

    static CombatLocalStateSnapshot CloneAndValidateLocalState(CombatLocalStateSnapshot state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.PositionId) || string.IsNullOrWhiteSpace(state.MovementMode) ||
            string.IsNullOrWhiteSpace(state.AltitudeBand) || string.IsNullOrWhiteSpace(state.LifeState) ||
            state.HitPoints < 0 || state.Mana < 0 || string.IsNullOrWhiteSpace(state.StanceStateId) ||
            string.IsNullOrWhiteSpace(state.ProcessStateId))
        {
            throw new ArgumentException("Combat local state must declare every rewindable field.", nameof(state));
        }
        return CloneLocalState(state);
    }

    static CombatLocalStateSnapshot CloneLocalState(CombatLocalStateSnapshot state) => new(
        state.PositionId,
        state.MovementMode,
        state.AltitudeBand,
        state.LifeState,
        state.HitPoints,
        state.Mana,
        state.StanceStateId,
        state.ProcessStateId,
        CloneLedger(state.Cooldowns, "cooldown"),
        CloneLedger(state.Charges, "charge"),
        CloneLedger(state.Consumables, "consumable"));

    static IReadOnlyDictionary<string, int> CloneLedger(IReadOnlyDictionary<string, int> source, string label)
    {
        if (source == null || source.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || entry.Value < 0))
            throw new ArgumentException($"Combat {label} slots must be complete and non-negative.");
        return new Dictionary<string, int>(source, StringComparer.Ordinal);
    }
}
