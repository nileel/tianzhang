using System.Collections.Generic;
using System.Linq;

namespace BattleSim;

static class SeatCompetitionSampleStats
{
    public const string UnformedSeatName = "未成丹/无目标席位";

    public record Row(
        string SeatName,
        int SampleCount,
        int NaturalCandidateCount,
        int GrantedCount,
        int TemporaryCount,
        int UnformedCount,
        string NaReason,
        int ZifuPendingCount,
        string ZifuInputState);

    public static IReadOnlyList<Row> Summarize(IEnumerable<Character> characters, int minimumSamples)
    {
        return characters
            .GroupBy(c => string.IsNullOrEmpty(c.SeatName) ? UnformedSeatName : c.SeatName)
            .Select(g => BuildRow(g.Key, g.ToList(), minimumSamples))
            .OrderBy(r => r.SeatName == UnformedSeatName ? 1 : 0)
            .ThenBy(r => r.SeatName)
            .ToList();
    }

    static Row BuildRow(string seatName, IReadOnlyList<Character> group, int minimumSamples)
    {
        int sampleCount = group.Count;
        int naturalCandidates = group.Count(c =>
            c.NaturalDanJiCandidateState == "自然候选" || c.SeatAccessState == "natural_candidate");
        int granted = group.Count(c => c.SeatAccessState == "granted" || c.FinalOccupancyState == "受敕承位");
        int temporary = group.Count(c => c.SeatAccessState == "temporary" || c.FinalOccupancyState == "暂寄");
        int unformed = group.Count(c => c.FormedState != "成丹" || string.IsNullOrEmpty(c.SeatName));
        int zifuPending = group.Count(c =>
            c.ZifuCoreLoopState == GameData.ZifuCoreLoopPendingState ||
            c.ZifuEligibilityNote == GameData.ZifuEligibilityPendingNote);

        string naReason = unformed == sampleCount
            ? "未成丹"
            : sampleCount < minimumSamples
                ? $"样本不足(<{minimumSamples})"
                : "样本可用";
        string zifuInputState = zifuPending == 0
            ? "已接入"
            : zifuPending == sampleCount
                ? "未接入"
                : "部分未接入";

        return new Row(
            seatName,
            sampleCount,
            naturalCandidates,
            granted,
            temporary,
            unformed,
            naReason,
            zifuPending,
            zifuInputState);
    }
}
