using System.Linq;
using System.Text;
using TianZhang.Combat;

namespace TianZhang.Features.Adventure
{
    internal static class JindanFireDomainPresentation
    {
        internal static string Details(CombatSession session)
        {
            var seat = session.Jindan.Seats.FirstOrDefault(value => value.FireDomain != null);
            if (seat == null) return string.Empty;
            var fire = seat.FireDomain;
            var text = new StringBuilder("界·不熄炉 / 长明：" + (fire.Main == null ? "主规则未生效" : "整棋盘地表·弱火热1") + "\n");
            text.AppendLine("中心(7,12) " + fire.Support.Durability + "/600；燃料可用" + fire.RemainingFuel + " / 余火预留" + fire.ReservedFuel);
            text.AppendLine("燃烧容量 " + CombatFireDomainRules.BurningCount(session, seat) + "/3；累计" + seat.TotalManaSpent + "MP");
            if (fire.Main != null) text.AppendLine("后续本人结束：维护200MP；主规则剩余" + fire.Main.RemainingCycles + "周期；关闭留有限余烬。");
            foreach (var tail in fire.Tails) text.AppendLine("余烬：" + tail.Cells.Count + "格，剩余" + tail.RemainingCycles + "本人结束周期 / " + tail.ReservedFuel + "燃料");
            if (seat.LastEndReason.Length > 0) text.AppendLine(Explain(seat.LastEndReason));
            text.AppendLine("离地避火；潮汐灭火／毁低植断燃料；破中心只终止主规则，真实位格保留。");
            return text.ToString();
        }

        internal static string Candidates(CombatSession session)
        {
            var seat = session.Jindan.Seats.FirstOrDefault(value => value.FireDomain != null);
            return seat == null ? string.Empty : "当前固定顺序作用格：" + string.Join("、",
                CombatFireDomainRules.Candidates(session, seat, session.Jindan.Ability(CombatFireDomainRules.Effect))
                    .Select(value => "(" + value.Q + "," + value.R + ")"));
        }

        internal static string Explain(string reason) => reason switch
        {
            "fire_fixed_rule_required" => "长明只按固定公开地表规则作用，无逐格点名。",
            "fire_surface_missing" => "领域地表或来源已不可用。",
            "fire_center_destroyed" => "长明之根已失效；主规则结束，已付余烬仍按原期限。",
            "fire_fuel_insufficient" => "中心燃料不足，不能支付完整预留或本周期作用。",
            "fire_tail_capacity_full" => "旧余烬仍占满3格容量，不能刷新或复制。",
            "fire_no_eligible_surface" => "没有合法可燃对象，未结算新作用。",
            "fire_domain_expired" => "主规则三周期已结束，合法燃烧转有限余烬。",
            "jindan_upkeep_failed" => "共享MP不足以完整维护，主规则结束。",
            "jindan_closed" => "已付费关闭主规则。",
            "jindan_owner_dead" => "持有者死亡，主规则与余烬已清除。",
            "jindan_source_disabled" => "主承载失效，主规则结束。",
            "jindan_session_ended" => "演武结束，领域与余烬已清除。",
            _ => reason,
        };
    }
}
