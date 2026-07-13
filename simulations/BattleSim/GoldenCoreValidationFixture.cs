using System.Collections.Generic;

namespace BattleSim;

// 仅供 BattleSim 回归使用：复用既有成丹解析，不写入默认修炼、战斗或席位结算路径。
static class GoldenCoreValidationFixture
{
    public record ControlledSample(GameData.GoldenCoreProfile GoldenCore);

    public record InputCoverage(string Field, string Source, string Availability, string Detail);

    static readonly Dictionary<string, double> NaturalCandidateWeights = new()
    {
        ["根骨"] = 0.45,
        ["魂魄"] = 0.15,
        ["神识"] = 0.15,
        ["资质"] = 0.15,
        ["气运"] = 0.10
    };

    public static ControlledSample CreateNaturalCandidate()
    {
        // 130 / 黄品是现有 golden-core-tq015c 回归已覆盖的输入，
        // 这里只将它固定为诊断夹具，不改变任何成丹阈值或倍率。
        return new(GameData.ResolveGoldenCoreProfile(130, "黄品", NaturalCandidateWeights));
    }

    public static IReadOnlyList<InputCoverage> DescribeInputCoverage(ControlledSample sample)
    {
        var profile = sample.GoldenCore;
        return new InputCoverage[]
        {
            new("成丹状态", "GameData.ResolveGoldenCoreProfile", Available(profile.FormedState), profile.FormedState),
            new("目标席位", "GameData.ResolveGoldenCoreProfile", Available(profile.TargetSeat), profile.SeatName),
            new("占据状态", "GameData.ResolveGoldenCoreProfile", Available(profile.FinalOccupancyState), profile.FinalOccupancyState),
            new("丹枢接口", "GameData.ResolveGoldenCoreProfile", Available(profile.DanPivot), profile.DanPivot),
            new("紫府神通数量", "GoldenCoreProfile.ZifuDivineArtCount", ZifuAvailability(profile), $"{profile.ZifuDivineArtCount}；{profile.ZifuEligibilityNote}"),
            new("府位覆盖", "GoldenCoreProfile.ZifuPalaceCoverageCount", ZifuAvailability(profile), $"{profile.ZifuPalaceCoverageCount}；{profile.ZifuEligibilityNote}")
        };
    }

    static string Available(string value) => string.IsNullOrEmpty(value) ? "未接入" : "已接入";

    static string ZifuAvailability(GameData.GoldenCoreProfile profile) =>
        profile.ZifuCoreLoopState == GameData.ZifuCoreLoopPendingState ? "未接入" : "已接入";
}
