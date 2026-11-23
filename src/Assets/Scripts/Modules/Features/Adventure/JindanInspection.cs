using System.Collections.Generic;
using System.Linq;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Features.Adventure
{
    // Project public facts on selection and on HUD refresh. The combat session owns their lifetime.
    internal static class JindanInspection
    {
        internal static string Cell(CombatSession session, HexCoord cell)
        {
            if (session?.Jindan == null || !session.SceneObjects.Board.TryGetCell(cell, out var tile)) return null;
            var surface = session.Surfaces?.Get(cell);
            var lines = new List<string> { $"高度 {tile.HeightLevel} · {JindanTidePresentation.Surface(surface?.Kind ?? CombatSurfaceKind.None)}" };
            foreach (var seat in session.Jindan.Seats)
            {
                var fire = seat.FireDomain;
                if (fire?.HasPlacedSupport == true && fire.Support.Cell == cell)
                {
                    if (fire.Support.IsDestroyed) lines.Add("长明阵心 · 已破");
                    else if (fire.Main == null) lines.Add("长明阵位 · 未展开，当前不可攻击 · 展开仍在此格");
                    else lines.Add($"长明阵心 · 耐久 {fire.Support.Durability}/{fire.Support.Definition.MaxDurability}");
                }
                if (fire?.Main != null && surface?.Kind != CombatSurfaceKind.Burning)
                    lines.Add(CombatFireDomainRules.Candidates(session, seat, session.Jindan.Ability(seat.EffectId)).Contains(cell)
                        ? "长明火域 · 按当前地表，下次维护优先受热"
                        : "长明火域覆盖 · 此格当前未被引燃");
                if (surface?.SourceRef == seat.Definition.AbilityInstanceId)
                {
                    var tail = fire?.Tails.FirstOrDefault(value => value.Cells.Contains(cell));
                    if (tail != null) lines.Add($"长明余火 · 剩余 {tail.RemainingCycles} 次施法者回合结束");
                    else lines.Add("地表来源 · " + JindanRehearsalPresenter.Effect(seat.EffectId));
                }
                else if (surface?.Kind == CombatSurfaceKind.Burning && fire?.Main != null)
                    lines.Add("原有地火 · 不占长明燃烧格数");
                var earth = seat.ActiveEarth;
                if (earth != null)
                {
                    foreach (var obj in earth.Objects.Where(value => !value.IsDestroyed && value.Contains(cell)))
                    {
                        string kind = obj.Definition.IsTemporaryRamp ? "土坡 · 可跨高差" : "岩墙 · 阻挡通行";
                        lines.Add($"厚载{kind} · 通往 {Coord(obj.Cell == cell ? obj.EdgeOther.Value : obj.Cell)}");
                    }
                    if (earth.ObjectStatus != null && session.SceneObjects.Find(earth.TargetId)?.Contains(cell) == true)
                        lines.Add(Gravity(earth.ObjectStatus));
                }
                var scale = seat.ActiveScale;
                if (scale != null)
                    for (int i = 1; i < scale.Path.Count; i++)
                    {
                        var a = scale.Path[i - 1];
                        var b = scale.Path[i];
                        if (a == cell || b == cell)
                            lines.Add($"方位定尺 · 至 {Coord(a == cell ? b : a)} · {JindanRehearsalPresenter.Metric(scale.MetricDistanceUnits)}");
                    }
                var seal = seat.ActiveSeal;
                if (seal != null && (seal.Definition.From == cell || seal.Definition.To == cell))
                    lines.Add($"封关 · {(seal.OwnerTeam == CombatTeam.Player ? "敌方" : "我方")}不可从 {Coord(seal.Definition.From)} 进入 {Coord(seal.Definition.To)}");
                var sight = seat.ActiveVisibility;
                if (sight != null)
                {
                    string source = JindanRehearsalPresenter.Effect(seat.EffectId);
                    lines.Add(sight.Support.Cell == cell ? $"{source} · 坛体耐久 {sight.Support.Durability}" : $"{source} · 领域覆盖");
                }
                if (seat.PendingTide?.Request.TideSource == cell)
                    lines.Add($"蓄澜潮源 · 将在时刻 {seat.PendingTide.DueTick} 发动");
            }
            if (lines.Count == 1) lines.Add("当前无金丹效果");
            return string.Join("\n", lines);
        }

        internal static string Unit(CombatSession session, string id)
        {
            if (!session.Combatants.TryGet(id, out var unit)) return null;
            var lines = new List<string>();
            var gravity = CombatForceMovement.Gravity(unit);
            if (gravity != null) lines.Add(Gravity(gravity));
            var flight = session.Flight?.Find(id);
            if (flight?.Active == true)
                lines.Add($"御风 · {(unit.AltitudeBand > 1 ? "高空" : "低空")} · 剩余 {flight.RemainingOwnTurnEnds} 次本人回合结束");
            foreach (var seat in session.Jindan.Seats)
            {
                if (seat.Definition.ActorId == id && (seat.ActiveEarth != null || seat.ActiveScale != null ||
                    seat.ActiveSeal != null || seat.FireDomain?.Main != null || seat.PendingTide != null))
                    lines.Add("施法中 · " + JindanRehearsalPresenter.Effect(seat.EffectId));
                if (seat.ActiveVisibility != null && session.Combatants.TryGet(seat.Definition.ActorId, out var owner) && owner.Team == unit.Team)
                    lines.Add(seat.EffectId == CombatVisibility.Yang ? "阳照护持 · 窗口内首击可减伤" : "藏真护持 · 隐匿，远袭可减伤");
            }
            if (lines.Count == 0) lines.Add("人物身上无持续金丹效果");
            var recent = session.Surfaces?.Settlements.LastOrDefault(record =>
                record.Damage.Any(hit => hit.TargetId == id) || record.Pushes.Any(push => push.ActorId == id));
            if (recent != null)
            {
                int damage = recent.Damage.Concat(recent.FallDamage).Where(hit => hit.TargetId == id).Sum(hit => hit.FinalDamage);
                string outcome = $"{(recent.TideAction.HasValue ? "潮汐" : "地火")}伤害 {damage}";
                var push = recent.Pushes.FirstOrDefault(value => value.ActorId == id);
                if (push != null) outcome += push.Movement.Path.Count == 0 ? " · 未位移" : $" · 推至 {Coord(push.Movement.Destination.Anchor)}";
                lines.Add($"上次结算（时刻 {recent.Tick}）· {outcome}");
            }
            foreach (var cell in unit.OccupiedCells)
            {
                string details = Cell(session, cell);
                if (details != null) lines.Add((unit.MovementMode == SpatialMovementMode.Ground ? "脚下地块 " : "下方地块（离地）") +
                    Coord(cell) + "\n" + details);
            }
            return string.Join("\n", lines);
        }

        private static string Gravity(CombatOwnTurnStatus status)
        {
            string mode = status.EarthMode switch { CombatEarthMode.Heavy => "极重", CombatEarthMode.Light => "失重", _ => "镇岳" };
            return $"厚载·{mode} · 受力档差 {status.GravityShiftRank} · 剩余 {status.RemainingOwnTurnEnds} 次回合结束";
        }

        private static string Coord(HexCoord cell) => $"({cell.Q},{cell.R})";
    }
}
