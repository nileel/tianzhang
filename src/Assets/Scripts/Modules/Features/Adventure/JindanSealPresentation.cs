using System.Linq;
using TianZhang.Combat;

namespace TianZhang.Features.Adventure
{
    internal static class JindanSealPresentation
    {
        internal static string Details(CombatSession session)
        {
            var seat = session.Jindan.Seats.FirstOrDefault(value => value.EffectId == "SPACE_SEAL");
            if (seat == null) return string.Empty;
            var seal = seat.ActiveSeal;
            if (seal == null) return "门枢封关：未生效；可绕行或以贯石击拆锚。\n";
            var rule = seal.Definition;
            return "化·门枢封关 (" + rule.From.Q + "," + rule.From.R + ")→(" + rule.To.Q + "," + rule.To.R + ")\n" +
                "外" + seal.OuterDurability + "/" + rule.LayerDurability + " 内" + seal.InnerDurability + "/" + rule.LayerDurability +
                "；容量1/1；到期t" + seal.ExpiresTick + "\n" +
                "下次t" + (seal.LastPaidTick + 1) + "维护" + session.Jindan.Ability(seat.EffectId).UpkeepMana +
                "MP；到期不扣\n" +
                "敌方地面／飞行同判；外破缩时，内破通行\n";
        }
    }
}
