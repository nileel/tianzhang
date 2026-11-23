using System;
using System.Linq;
using System.Text;
using TianZhang.Combat;
using TianZhang.Gameplay.Contracts;
using TianZhang.Spatial;

namespace TianZhang.Features.Adventure
{
    /// <summary>Formatting and DTO projection only; all selected results come from the Combat tide query.</summary>
    internal static class JindanTidePresentation
    {
        internal static CombatSurfacePresentationSnapshot Project(CombatSession session, CombatCommandPreview quote,
            CombatCommand command)
        {
            if (session?.Surfaces == null) return null;
            var tide = quote?.IsLegal == true ? quote.Tide : null;
            HexCoord? source = tide == null ? null : command.Jindan.TideSource;
            int sourceHeight = source.HasValue && session.SceneObjects.Board.TryGetCell(source.Value, out var cell) ? cell.HeightLevel : 0;
            return new CombatSurfacePresentationSnapshot(session.Surfaces.Revision,
                session.Surfaces.Cells.Select(value => new CombatSurfaceCellSnapshot(Hex(value.Cell),
                    (CombatSurfaceDisplayKind)value.Kind, value.Fuel)).ToArray(),
                quote?.IsLegal == true && command.ProfileId == CombatFireDomainRules.Effect && command.Kind == CombatCommandKind.JindanAbility
                    ? CombatFireDomainRules.Candidates(session, session.Jindan.Find(command.Jindan.SeatId),
                        session.Jindan.Ability(command.ProfileId), command.Jindan.SelectedTargetCell).Select(Hex).ToArray()
                    : tide?.Changes.Select(value => Hex(value.Cell)).ToArray(),
                tide?.Pushes.Select(value => new CombatTidePushSnapshot(value.ActorId, Hex(value.Movement.Origin.Anchor),
                    Hex(value.Movement.Destination.Anchor), value.Direction, value.Movement.Reason)).ToArray(),
                source.HasValue ? Hex(source.Value) : null, sourceHeight, tide?.WaterlineHeight ?? 0);
        }

        internal static string Details(CombatSession session, CombatantSnapshot actor, HexCoord? source,
            CombatTideAction action, bool charged, bool eye)
        {
            var text = new StringBuilder();
            var seat = session.Jindan.Seats.FirstOrDefault(value => value.Definition.ActorId == actor.Id && value.EffectId == "WATER_TIDE");
            if (seat == null) text.AppendLine("当前角色没有蓄澜装配；第二段水修可施放。土修可筑墙阻水或镇岳抗推。");
            else
            {
                var definition = session.Jindan.Ability("WATER_TIDE");
                text.AppendLine("源·渊蓄池 / 蓄澜  CD " + actor.GetCooldown("WATER_TIDE") + "tick");
                text.AppendLine("本源地表 " + session.Surfaces.OwnedCount(seat.Definition.AbilityInstanceId) + "/" + definition.Capacity +
                    "；累计 " + (seat.TotalActionPointSpent / 1000m).ToString("0.###") + "AP / " + seat.TotalManaSpent + "MP");
                if (seat.PendingTide != null) text.AppendLine("正在蓄力：t=" + seat.PendingTide.DueTick + " 完成时重验；已支付，打断不退费。");
                if (seat.LastEndReason.Length > 0) text.AppendLine("状态：" + Explain(seat.LastEndReason));
            }
            text.AppendLine("待选：" + Name(action, charged, eye) + "；潮源 " + (source.HasValue ? Cell(source.Value) : "未选择"));
            text.AppendLine("唯一地表：焦地／干地 → 湿泥 → 浅水 → 深水。深水可涉行，额外移动负担2。");
            text.AppendLine("潮推力档2；锚定只挡推移，水击照常。飞行未接地避开普通潮推、水击和燃烧。");
            foreach (var fire in session.Surfaces.FireSources)
                text.AppendLine("环境弱火：剩余燃料" + fire.RemainingFuel + "，剩余" + fire.RemainingTicks + "tick；每" +
                    fire.Definition.PeriodTicks + "tick结算。水隔断或毁低植可断燃料路径。");
            foreach (var cell in session.Surfaces.Cells.Where(value => value.Fuel > 0 || value.Kind == CombatSurfaceKind.Burning))
                text.AppendLine(Cell(cell.Cell) + " " + Surface(cell.Kind) + " · 燃料" + cell.Fuel);
            return text.ToString();
        }

        internal static string Quote(CombatTidePreview tide, CombatJindanRequest request)
        {
            if (tide == null || !tide.IsLegal) return string.Empty;
            var text = new StringBuilder();
            text.AppendLine("水线 H" + tide.WaterlineHeight + "；变化" + tide.Changes.Count + "格：" +
                string.Join("、", tide.Changes.Select(value => Cell(value.Cell) + Surface(value.Before) + "→" + Surface(value.After))));
            foreach (var push in tide.Pushes)
                text.AppendLine(Short(push.ActorId) + " 推向" + (push.Direction < 0 ? "潮源原位" : push.Direction.ToString()) + " " +
                    Cell(push.Movement.Origin.Anchor) + "→" + Cell(push.Movement.Destination.Anchor) +
                    (push.Movement.Path.Count == 0 ? "（" + Explain(push.Movement.Reason) + "）" : "") +
                    (push.Movement.CrossedDrop ? "，跨落差另结算10%最大生命坠落" : ""));
            text.AppendLine("水击：" + string.Join("、", tide.WaterTargetIds.Select(Short)) +
                "；冲蚀：" + string.Join("、", tide.ErosionObjectIds.Select(Short)));
            if (request.Charged) text.AppendLine("先付全费，1全局tick后按当时地形重验；水击×1.25，其余不变。");
            if (request.CreateTideEye) text.AppendLine("有费潮眼与本次涨潮一次提交；范围内有合法水源则拒绝。");
            return text.ToString().TrimEnd();
        }

        internal static string Name(CombatTideAction action, bool charged, bool eye) =>
            (charged ? "蓄力·" : "普通·") + (action == CombatTideAction.Rise ? "涨潮" : "退潮") + (eye ? "＋潮眼" : "");
        internal static string Explain(string reason) => reason switch
        {
            "tide_selection_invalid" => "请选择水源和涨／退潮模式。",
            "tide_source_invalid" => "涨潮需要浅／深水，退潮需要湿／浅／深水。",
            "tide_eye_cell_invalid" => "潮眼只可涨潮，并需无不兼容对象的合法干湿地格。",
            "tide_real_source_available" => "射程内已有合法真实水源，请使用该水源。",
            "tide_no_changes" => "模板内没有可变化地表。",
            "tide_process_pending" => "同源蓄力尚未结束，不能再次施放。",
            "tide_process_cancelled" => "蓄力中断，已支付费用不返还。",
            "tide_completed" => "潮汐已完成。",
            "tide_at_source" => "已在潮源，不再向内拉动。",
            "earth_anchored" => "镇岳锚定受阻，仍承受水击。",
            "occupied" or "cell_occupied" => "落点被占，仍承受水击。",
            "entity_wall_blocks_movement" or "movement_blocked" or "scene_object_blocks_movement" => "墙或格边阻挡，仍承受水击。",
            "jindan_capacity_full" => "本来源地表容量已满；旧水面仍占容量。",
            _ => reason,
        };
        internal static string Surface(CombatSurfaceKind kind) => kind switch
        {
            CombatSurfaceKind.None => "干地", CombatSurfaceKind.Scorched => "焦地", CombatSurfaceKind.Burning => "燃烧",
            CombatSurfaceKind.Wet => "湿泥", CombatSurfaceKind.ShallowWater => "浅水", CombatSurfaceKind.DeepWater => "深水", _ => kind.ToString(),
        };
        private static CombatUnitPresentationHex Hex(HexCoord value) => new(value.Q, value.R);
        private static string Cell(HexCoord value) => "(" + value.Q + "," + value.R + ")";
        private static string Short(string value) => value.Replace("demo_20261024_", "");
    }
}
