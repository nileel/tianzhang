using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim;

static class GoldenCoreSuppressionExitStats
{
    public record ExitClassification(string ExitRoute, string Reason, bool IsTacticalExit);

    public static GoldenCoreSuppressionTacticalScenario CreateTacticalScenario(
        Character zifu,
        Character gold,
        GoldenCoreSuppressionSwitches switches)
    {
        var route = ClassifyExit(zifu, gold);
        var scenarioZifu = CloneCharacter(zifu);
        var scenarioGold = CloneCharacter(gold);
        if (!route.IsTacticalExit || (!switches.EnableSeatErosion && !switches.EnableDanSeal))
        {
            return new GoldenCoreSuppressionTacticalScenario(
                scenarioZifu,
                scenarioGold,
                "未启用",
                route.IsTacticalExit ? "未启用战术开关" : route.Reason,
                false);
        }

        var routeSet = route.ExitRoute.Split('+', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var applied = new List<string>();
        var reasons = new List<string>();

        if (switches.EnableSeatErosion && routeSet.Contains("削位"))
        {
            if (scenarioGold.Primary.ContainsKey("MP"))
                scenarioGold.Primary["MP"] = (int)Math.Round(scenarioGold.Primary["MP"] * 0.85);
            applied.Add("削位");
            reasons.Add("削位开关：压低金丹灵力承载，不改HP/攻击");
        }

        if (switches.EnableDanSeal && routeSet.Contains("封丹"))
        {
            scenarioGold.DivineName = "";
            scenarioGold.DivineType = "";
            scenarioGold.DivineElement = "";
            scenarioGold.DivineMult = 1.0;
            scenarioGold.DivineDefPen = 0;
            scenarioGold.DivineCooldown = 0;
            applied.Add("封丹");
            reasons.Add("封丹开关：禁用金丹神通，不改基础倍率");
        }

        if (applied.Count == 0)
        {
            return new GoldenCoreSuppressionTacticalScenario(
                scenarioZifu,
                scenarioGold,
                "未启用",
                "已启用开关但当前样本无匹配机械出口",
                false);
        }

        return new GoldenCoreSuppressionTacticalScenario(
            scenarioZifu,
            scenarioGold,
            string.Join("+", applied.Distinct()),
            string.Join("；", reasons.Distinct()),
            true);
    }

    public static ExitClassification ClassifyExit(Character zifu, Character gold)
    {
        if (zifu.Realm != "筑基" || zifu.SubIndex != 4 || gold.Realm != "金丹" || gold.SubIndex != 0)
            return new ExitClassification("NA", "样本不属于紫府圆满压制战", false);

        var routes = new List<string>();
        var reasons = new List<string>();

        if (gold.SeatCompetitionState == "待争席" ||
            gold.FinalOccupancyState == "未占据" ||
            gold.SeatAccessState == "natural_candidate")
        {
            routes.Add("削位");
            reasons.Add("待争席丹籍可被削位");
        }

        if (zifu.ZifuCoreLoopState != GameData.ZifuCoreLoopPendingState && zifu.ZifuPalaceCoverageCount >= 3)
        {
            routes.Add("破府");
            reasons.Add("紫府闭环可攻击府位承载");
        }

        if (zifu.Style == "taiyi_fuxiu" || zifu.GongFaName == "云篆度人经")
        {
            routes.Add("封丹");
            reasons.Add("符修具备封丹手段");
        }
        else if (zifu.Style == "taixu_xuangan" || zifu.GongFaName == "南华玄感录")
        {
            routes.Add("封丹");
            reasons.Add("玄感扰动具备封丹手段");
        }

        if (zifu.GongFaName == "绳墨正法录")
        {
            routes.Add("阵法");
            reasons.Add("阵法协同可制造越阶窗口");
        }

        if (routes.Count == 0)
            return new ExitClassification("剧情条件", "仅剧情条件，未接入机械出口", false);

        return new ExitClassification(string.Join("+", routes.Distinct()), string.Join("；", reasons.Distinct()), true);
    }

    static Character CloneCharacter(Character source)
    {
        return new Character
        {
            Name = source.Name,
            Realm = source.Realm,
            Style = source.Style,
            SubIndex = source.SubIndex,
            DFQuality = source.DFQuality,
            DFMult = source.DFMult,
            DFScore = source.DFScore,
            FormedState = source.FormedState,
            DanJiType = source.DanJiType,
            OccupancyState = source.OccupancyState,
            DanName = source.DanName,
            DanNature = source.DanNature,
            TargetBranch = source.TargetBranch,
            TargetSeat = source.TargetSeat,
            SeatName = source.SeatName,
            DanPivot = source.DanPivot,
            NaturalDanJiCandidateState = source.NaturalDanJiCandidateState,
            SeatAccessState = source.SeatAccessState,
            SeatCompetitionState = source.SeatCompetitionState,
            FinalOccupancyState = source.FinalOccupancyState,
            SeatCompetitionScore = source.SeatCompetitionScore,
            ZifuDivineArtCount = source.ZifuDivineArtCount,
            ZifuPalaceCoverageCount = source.ZifuPalaceCoverageCount,
            ZifuCoreLoopState = source.ZifuCoreLoopState,
            ZifuEligibilityNote = source.ZifuEligibilityNote,
            LegacyGCGrade = source.LegacyGCGrade,
            DanJiStabilityMult = source.DanJiStabilityMult,
            GCScore = source.GCScore,
            DanJiArtAffinityMult = source.DanJiArtAffinityMult,
            GongFaName = source.GongFaName,
            ArtName = source.ArtName,
            ArtType = source.ArtType,
            ArtElement = source.ArtElement,
            ArtMult = source.ArtMult,
            ArtMPCost = source.ArtMPCost,
            ArtCooldown = source.ArtCooldown,
            DivineName = source.DivineName,
            DivineType = source.DivineType,
            DivineElement = source.DivineElement,
            DivineMult = source.DivineMult,
            DivineDefPen = source.DivineDefPen,
            DivineCooldown = source.DivineCooldown,
            Innate = new Dictionary<string, int>(source.Innate),
            Primary = new Dictionary<string, int>(source.Primary),
            Secondary = new Dictionary<string, double>(source.Secondary),
        };
    }
}

readonly record struct GoldenCoreSuppressionSwitches(bool EnableSeatErosion, bool EnableDanSeal);

record GoldenCoreSuppressionTacticalScenario(
    Character Zifu,
    Character Gold,
    string AppliedRoutes,
    string Reason,
    bool HasActiveSwitch);
