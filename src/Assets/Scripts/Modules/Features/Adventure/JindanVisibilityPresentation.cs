using System.Linq;
using System.Text;
using TianZhang.Combat;

namespace TianZhang.Features.Adventure
{
    public static class JindanVisibilityPresentation
    {
        public static string Details(CombatSession session)
        {
            if (session?.Visibility == null) return string.Empty;
            var text = new StringBuilder();
            foreach (var seat in session.Jindan.Seats.Where(value => value.VisibilityLedger != null))
            {
                var instance = seat.ActiveVisibility;
                var ability = session.Jindan.Ability(seat.EffectId);
                text.AppendLine(JindanRehearsalPresenter.Effect(seat.EffectId) + "：" + (instance == null ? "未展开" :
                    "支点" + instance.Support.Durability + "/" + ability.SupportDurability + "；到期t" + instance.ExpiresTick + "；" + ability.UpkeepMana + "MP/tick"));
                text.AppendLine("对抗额度余" + seat.VisibilityLedger.ReserveRemaining + "MP；实际支出" + seat.VisibilityLedger.ConflictManaSpent + "MP");
            }
            var result = session.Visibility.Current;
            if (result != null && !result.IsPending)
                text.AppendLine("本次观察：" + (result.Visible ? "可见" : "不可见") + "；" + Outcome(result.Outcome) + "／" + Reason(result.EndReason));
            return text.ToString();
        }
        public static string Conflict(CombatVisibilityConflict conflict, int tick)
        {
            if (conflict == null) return string.Empty;
            return "昭明殿 ↔ 藏真域\n" +
                "同一观察结果冲突；t=" + tick + "，正常战斗已暂停\n" +
                "五项比较：" + Reason(conflict.PriorityStep) + "；中性结果：正常可见\n\n" +
                "阳照可付 " + conflict.LeftAvailable + "MP　阴潜伏可付 " + conflict.RightAvailable + "MP\n" +
                "每脉冲双方各付 " + conflict.Settings.PulseMana + "MP；已付 " + conflict.LeftSpent + " / " + conflict.RightSpent + "\n" +
                "推进 " + conflict.Gauge + " / " + conflict.Settings.GaugeWin + "\n\n" +
                (!conflict.Started ? "按住维持键开始；等待不扣费。" : conflict.WindowOpen ? "破绽已开：按 [破绽键]" : "继续按住维持键，等待破绽。") +
                "\n松开维持键放弃；错键不加费、不倒退。";
        }
        private static string Outcome(CombatConflictOutcome result) => result switch
        {
            CombatConflictOutcome.Left => "阳照胜出", CombatConflictOutcome.Right => "阴潜伏胜出",
            CombatConflictOutcome.Neutral => "中性", CombatConflictOutcome.Cancelled => "已取消", _ => "等待结算",
        };
        private static string Reason(string reason) => reason switch
        {
            "equal" => "全部相等", "permission" => "权限与目标资格", "position" => "席位",
            "support" => "实际依托", "paid_cost" => "实际投入", "carrying" => "有效承载",
            "gauge" => "破绽推进完成", "released" => "松开维持键", "one_cannot_pay" => "一方无法续付",
            "both_cannot_pay" => "双方无法续付", "battle_ended" => "战斗结束", _ => reason,
        };
    }
}
