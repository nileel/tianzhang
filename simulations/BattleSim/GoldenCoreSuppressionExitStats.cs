using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim;

static class GoldenCoreSuppressionExitStats
{
    public record ExitClassification(string ExitRoute, string Reason, bool IsTacticalExit);

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
}
