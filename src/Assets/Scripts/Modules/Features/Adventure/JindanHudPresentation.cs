using System.Collections.Generic;
using System.Linq;
using System.Text;
using TianZhang.Combat;
using TianZhang.Gameplay.Contracts;

namespace TianZhang.Features.Adventure
{
    internal static class JindanHudPresentation
    {
        internal static JindanHudSnapshot Project(CombatSession session, string actorId, string objective)
        {
            if (session?.Jindan == null || !session.Combatants.TryGet(actorId, out var actor)) return null;
            var abilities = new List<JindanHudAbility>();
            foreach (var seat in session.Jindan.Seats.Where(value => value.Definition.ActorId == actorId))
            {
                var definition = session.Jindan.Ability(seat.EffectId);
                string key = seat.EffectId switch
                {
                    "FIRE_MANIFEST" => "fire_tab", "WATER_TIDE" => "tide_tab",
                    "EARTH_MANIFEST" or "EARTH_GRAVITY" => "earth_tab", "SPACE_SCALE" => "scale_tab",
                    "YANG_RADIANCE" or "YIN_CONCEAL" => "visibility_tab", _ => null,
                };
                if (key == null) continue;
                bool active = seat.ActiveScale != null || seat.ActiveEarth != null || seat.ActiveVisibility != null ||
                    seat.FireDomain?.Main != null || seat.PendingTide != null ||
                    (session.Surfaces?.OwnedCount(seat.Definition.AbilityInstanceId) ?? 0) > 0;
                abilities.Add(new JindanHudAbility(key, seat.EffectId, JindanRehearsalPresenter.Effect(seat.EffectId),
                    !definition.Enabled ? "未接入" : active ? "生效中" : "选择能力", actor.GetCooldown(seat.EffectId),
                    definition.Enabled, seat.Definition.Candidates.Count > 1));
            }
            var flight = session.Flight?.Find(actorId);
            if (flight != null)
                abilities.Add(new JindanHudAbility("flight_tab", CombatFlight.ProfileId, "御风",
                    actor.AltitudeBand == 0 ? "地面" : actor.AltitudeBand == 1 ? "低空" : "高空",
                    actor.GetCooldown(CombatFlight.ProfileId), true));

            var status = new StringBuilder();
            foreach (var seat in session.Jindan.Seats)
            {
                var definition = session.Jindan.Ability(seat.EffectId);
                var fire = seat.FireDomain;
                if (fire?.Main != null)
                {
                    status.AppendLine("长明 · 主规则生效");
                    status.AppendLine("中心 " + fire.Support.Durability + "/" + fire.Support.Definition.MaxDurability +
                        " · 火 " + CombatFireDomainRules.BurningCount(session, seat) + "/" + definition.Capacity);
                    bool castingTurn = session.Combatants.TryGet(seat.Definition.ActorId, out var owner) &&
                        owner.OwnTurnsStarted <= fire.Main.CreatedOwnTurn;
                    status.AppendLine(castingTurn ? "本回合不维护 · 后续 " + definition.UpkeepMana + " MP" :
                        "下次本人结束 −" + definition.UpkeepMana + " MP");
                }
                if (fire != null)
                    foreach (var tail in fire.Tails)
                        status.AppendLine("余火 " + tail.Cells.Count + "格 · 剩余 " + tail.RemainingCycles + " 本人结束");
                var scale = seat.ActiveScale;
                if (scale != null)
                    status.AppendLine("定尺 " + (scale.MetricDistanceUnits == 1 ? "压缩" : "扩张") + " " + (scale.Path.Count - 1) +
                        "边 · 碑 " + scale.Support.Durability + " · " + definition.UpkeepMana + " MP/tick");
                var seal = seat.ActiveSeal;
                if (seal != null) status.AppendLine("封关 · 外 " + seal.OuterDurability + " / 内 " + seal.InnerDurability);
                var sight = seat.ActiveVisibility;
                if (sight != null)
                    status.AppendLine(JindanRehearsalPresenter.Effect(seat.EffectId) + " · 坛 " + sight.Support.Durability +
                        " · 到期 t" + sight.ExpiresTick + " · " + definition.UpkeepMana + " MP/tick");
                if (seat.ActiveEarth != null)
                {
                    var earth = seat.ActiveEarth;
                    var gravity = earth.ObjectStatus;
                    if (gravity == null && earth.TargetId != null && session.Combatants.TryGet(earth.TargetId, out var target))
                        gravity = target.Statuses.FirstOrDefault(value => value.Kind == CombatStatusKind.EarthGravity &&
                            value.SourceRef == seat.Definition.AbilityInstanceId);
                    status.AppendLine(JindanRehearsalPresenter.Effect(seat.EffectId) + " · " +
                        (earth.TargetId == null ? "构件 " + earth.Objects.Count : "受力生效") +
                        " · 剩余 " + (gravity?.RemainingOwnTurnEnds ?? earth.RemainingOwnTurnEnds) + " 次结束");
                }
                if (seat.EffectId == "WATER_TIDE")
                {
                    int count = session.Surfaces?.OwnedCount(seat.Definition.AbilityInstanceId) ?? 0;
                    if (count > 0) status.AppendLine("蓄澜 · 本源地表 " + count + "/" + definition.Capacity);
                    if (seat.PendingTide != null) status.AppendLine("蓄澜 · 蓄力至 t" + seat.PendingTide.DueTick);
                }
            }
            if (flight?.Active == true)
                status.AppendLine("御风 · 剩余 " + flight.RemainingOwnTurnEnds + " 次结束 · 下次维护 " + session.Flight.UpkeepMana + " MP");
            return new JindanHudSnapshot(actorId, objective, status.ToString().TrimEnd(), abilities);
        }
    }
}
