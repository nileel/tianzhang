using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TianZhang.Combat;
using TianZhang.Entity;
using TianZhang.Spatial;

namespace TianZhang.Features.Adventure
{
    /// <summary>Adventure-owned selection and text. Only an explicit preview creates a combat quote.</summary>
    public sealed class JindanRehearsalPresenter
    {
        private readonly JindanDemoCatalogData catalog;
        private readonly EncounterCoordinator encounter;
        private readonly Action<int, string> enter;
        private readonly Func<string, string> text;
        private readonly List<HexCoord> path = new List<HexCoord>(3);
        private int segmentIndex = 2;
        private int presetIndex;
        private string earthEffect = "EARTH_GRAVITY";
        private int metric = 1;
        private HexCoord? remoteOrigin;
        private SelectionMode selection;
        private CombatCommand frozenCommand;
        private CombatCommandPreview frozenQuote;
        private CombatSession observedSession;
        private string observedActor;
        private string message = string.Empty;
        private enum SelectionMode { None, Edges, Origin }

        public JindanRehearsalPresenter(JindanDemoCatalogData catalog, EncounterCoordinator encounter,
            Action<int, string> enter, Func<string, string> text)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.encounter = encounter ?? throw new ArgumentNullException(nameof(encounter));
            this.enter = enter ?? throw new ArgumentNullException(nameof(enter));
            this.text = text ?? throw new ArgumentNullException(nameof(text));
            SelectSegmentPreset();
            Refresh();
        }

        public bool PreparationOpen { get; private set; }
        public bool IsSelecting => selection != SelectionMode.None;
        public string PreparationDetails { get; private set; } = string.Empty;
        public string CombatDetails { get; private set; } = string.Empty;
        public string QuoteDetails { get; private set; } = string.Empty;
        public bool CanConfirm => frozenQuote != null && frozenQuote.IsLegal && encounter.AcceptsPlayerCommand &&
            frozenCommand.ActorId == encounter.ControlledActorId && observedSession == encounter.CurrentSession;

        public void OpenPreparation()
        {
            if (encounter.IsRunning) return;
            PreparationOpen = true;
            ClearTargeting();
            message = "选段决定固定阵容；浏览预设可查看五人的来源。";
            Refresh();
        }

        public void HandleAction(string key)
        {
            Refresh();
            if (PreparationOpen)
            {
                switch (key)
                {
                    case "stage1": segmentIndex = 0; SelectSegmentPreset(); break;
                    case "stage2": segmentIndex = 1; SelectSegmentPreset(); break;
                    case "stage3": segmentIndex = 2; SelectSegmentPreset(); break;
                    case "preset_next": presetIndex = (presetIndex + 1) % catalog.presets.Length; break;
                    case "earth_toggle": earthEffect = earthEffect == "EARTH_GRAVITY" ? "EARTH_MANIFEST" : "EARTH_GRAVITY"; break;
                    case "cancel_preparation": PreparationOpen = false; message = string.Empty; break;
                    case "enter":
                        enter(segmentIndex, earthEffect);
                        PreparationOpen = false;
                        ClearTargeting();
                        message = "进入演武；方位定尺可选边预演，其余能力显示接入进度。";
                        break;
                }
                Refresh();
                return;
            }
            if (encounter.CurrentSession?.Jindan == null) return;
            if (key == "cancel_targeting")
            { ClearTargeting(); message = "已取消，未扣费。"; Refresh(); return; }
            if (!encounter.AcceptsPlayerCommand)
            { message = "等待当前演出或敌方行动结束。"; Refresh(); return; }
            switch (key)
            {
                case "compress": metric = 1; ClearQuote(); message = "已选压缩：每边0.5距离；请选择2–3个相连格。"; break;
                case "expand": metric = 4; ClearQuote(); message = "已选扩张：每边2距离；请选择2–3个相连格。"; break;
                case "pick_edges":
                    path.Clear(); selection = SelectionMode.Edges; ClearQuote();
                    message = "在地图依次点击2–3个相连格；点末格可撤销。"; break;
                case "pick_origin":
                    selection = SelectionMode.Origin; ClearQuote();
                    message = "点击当前视野内空格作遥施原点；点自身改回自身。"; break;
                case "preview": Preview(CombatCommandKind.JindanAbility); break;
                case "close_effect": Preview(CombatCommandKind.CloseJindan); break;
                case "swap_effect": Preview(CombatCommandKind.SwapJindan); break;
                case "confirm": Confirm(); break;
            }
            Refresh();
        }

        public void PickCell(int q, int r)
        {
            Refresh();
            if (!IsSelecting || !encounter.AcceptsPlayerCommand || encounter.CurrentSession?.Jindan == null) return;
            var cell = new HexCoord(q, r);
            ClearQuote();
            if (selection == SelectionMode.Origin)
            {
                encounter.CurrentSession.Combatants.TryGet(encounter.ControlledActorId, out var actor);
                remoteOrigin = actor.Pose.Contains(cell) ? (HexCoord?)null : cell;
                selection = SelectionMode.None;
                message = remoteOrigin.HasValue ? "已选遥施原点；预演时检查视野、空格与费用。" : "已恢复自身原点。";
            }
            else if (path.Count > 0 && path[path.Count - 1] == cell)
            { path.RemoveAt(path.Count - 1); message = "已撤销末格。"; }
            else if (path.Count == 3) message = "最多选3个相连格；点末格撤销或重新选边。";
            else if (path.Contains(cell)) message = "同一格不可重复；请沿相连边继续选择。";
            else if (path.Count > 0 && !path[path.Count - 1].TryGetDirectionTo(cell, out _))
                message = "下一格必须与末格相邻。";
            else
            {
                path.Add(cell);
                message = path.Count == 1 ? "已选首格，请再点相邻格。" : "已选" + (path.Count - 1) + "条边，可继续选格或点击预演。";
            }
            Refresh();
        }

        public void Refresh()
        {
            var session = encounter.CurrentSession;
            string actorId = session?.Jindan == null ? null : encounter.ControlledActorId;
            if (session != observedSession || actorId != observedActor)
            {
                bool changedActor = observedActor != null && actorId != observedActor;
                ClearTargeting();
                if (changedActor) message = "行动角色已改变，请重新选择目标并预演。";
                observedSession = session; observedActor = actorId;
            }
            PreparationDetails = PreparationText();
            CombatDetails = CombatText(session, actorId);
            QuoteDetails = QuoteText();
        }

        private void Preview(CombatCommandKind kind)
        {
            ClearQuote(); selection = SelectionMode.None;
            var session = encounter.CurrentSession;
            string actorId = encounter.ControlledActorId;
            var owned = session.Jindan.Seats.Where(value => value.Definition.ActorId == actorId).ToArray();
            var seat = kind == CombatCommandKind.SwapJindan
                ? owned.FirstOrDefault(value => value.Definition.Candidates.Count > 1)
                : owned.FirstOrDefault(value => value.EffectId == "SPACE_SCALE");
            if (seat == null)
            {
                message = kind == CombatCommandKind.SwapJindan ? "当前角色没有可切换的同席候选。" :
                    "当前角色没有装配方位定尺；请查看下方能力进度。";
                return;
            }
            string effect = kind == CombatCommandKind.SwapJindan
                ? seat.Definition.Candidates.First(value => value != seat.EffectId) : seat.EffectId;
            var request = kind == CombatCommandKind.JindanAbility
                ? new CombatJindanRequest(seat.Definition.SeatId, path, metric, remoteOrigin)
                : new CombatJindanRequest(seat.Definition.SeatId);
            frozenCommand = new CombatCommand(kind, actorId, profileId: effect, jindan: request);
            frozenQuote = encounter.PreviewJindan(frozenCommand);
            message = frozenQuote == null ? "当前战斗已结束。" : frozenQuote.IsLegal
                ? "报价已冻结，请核对后确认。" : Explain(frozenQuote.RejectionReason);
        }

        private void Confirm()
        {
            if (frozenCommand == null || frozenQuote == null)
            { message = "请先点击预演，生成本次报价。"; return; }
            // Confirmation deliberately sends the original quote, including its original state version.
            var result = encounter.ConfirmJindan(frozenCommand, frozenQuote);
            if (result.Succeeded)
            {
                message = "已执行：AP −" + Ap(result.ActionPointSpentUnits) + "，MP −" + result.SpiritSpent + "。";
                ClearTargeting();
            }
            else { message = Explain(result.RejectionReason); ClearQuote(); }
        }

        private void ClearQuote() { frozenCommand = null; frozenQuote = null; }
        private void ClearTargeting()
        { ClearQuote(); path.Clear(); remoteOrigin = null; selection = SelectionMode.None; }
        private void SelectSegmentPreset()
        {
            string id = catalog.segments[segmentIndex].actors.First(value => value.team == 0).characterId;
            presetIndex = Array.FindIndex(catalog.presets, value => value.characterId == id);
        }

        private string PreparationText()
        {
            var preset = catalog.presets[presetIndex];
            var segment = catalog.segments[segmentIndex];
            var result = new StringBuilder();
            result.AppendLine("当前选段：" + Local(segment.displayNameKey));
            result.AppendLine("阵容：" + string.Join("、", segment.actors.Select(value =>
                (value.team == 0 ? "我方·" : "对手·") + Local(catalog.GetPreset(value.characterId).displayNameKey))));
            result.AppendLine("土系战前装配：" + Effect(earthEffect) + "；浏览预设 " + (presetIndex + 1) + "/" + catalog.presets.Length);
            result.AppendLine(Local(preset.displayNameKey) + " · " + preset.realmSubstage + " · " + Local(preset.danxiangNameKey));
            result.AppendLine("AP " + Ap(preset.actionPointMaximumUnits) + " · MP " + preset.manaMaximum +
                " · 视野 " + preset.normalVisionRange + " · 完整紫府容量 " + preset.naturalMansionCapacity);
            result.AppendLine("身份：" + preset.characterId);
            foreach (string id in preset.seatInstanceIds)
            {
                var seat = catalog.GetSeat(id);
                string effect = seat.candidateEffectIds.Contains(earthEffect) ? earthEffect : seat.defaultEffectId;
                var ability = catalog.GetAbility(seat.guardianDefinitionId);
                result.AppendLine(Position(seat.positionType.ToString()) + " · " + Effect(effect) + " · " + seat.positionId);
                result.AppendLine("来源：" + Local(ability.displayNameKey) + "；同源账本 " + Short(seat.guardianAbilityInstanceId));
                result.AppendLine("候选：" + string.Join(" / ", seat.candidateEffectIds.Select(Effect)));
                var command = DeployDefinition(effect);
                result.AppendLine("展开 " + Ap(command.actionPointCostUnits) + "AP / " + command.manaCost + "MP，CD " +
                    command.cooldownTicks + "tick；" + (command.enabled ? "已接入" : Explain(command.unavailableReasonKey)));
            }
            if (!string.IsNullOrEmpty(preset.flightAbilityInstanceId) && preset.flightAbilityInstanceId != "none")
                result.AppendLine("独立飞行：" + Short(preset.flightAbilityInstanceId) + "；" + Explain("demo_20261024_unavailable_d6"));
            result.AppendLine("战中换装：未动用完整本人回合预算；新效果下一本人回合才可发动。结束销毁演武会话。");
            if (PreparationOpen && message.Length > 0) result.Append(message);
            return result.ToString();
        }

        private string CombatText(CombatSession session, string actorId)
        {
            if (session?.Jindan == null || !session.Combatants.TryGet(actorId, out var actor)) return string.Empty;
            var preset = catalog.GetPreset(actorId);
            var result = new StringBuilder();
            result.AppendLine((preset == null ? actorId : Local(preset.displayNameKey)) + "  t=" + session.TurnScheduler.CurrentTick +
                (encounter.AcceptsPlayerCommand ? " · 你的行动" : " · 等待行动"));
            result.AppendLine("AP " + Ap(actor.ActionPointsRemainingUnits) + "/" + Ap(actor.ActionPointLimitAtTurnStartUnits) +
                "  MP " + actor.CurrentSpirit + "/" + actor.MaximumSpirit);
            foreach (var seat in session.Jindan.Seats.Where(value => value.Definition.ActorId == actorId))
            {
                var definition = session.Jindan.Ability(seat.EffectId);
                var active = seat.ActiveScale;
                result.AppendLine(Position(seat.Definition.PositionType) + "·" + Effect(seat.EffectId) +
                    "  CD " + actor.GetCooldown(seat.EffectId) + "tick  容量 " + (active == null ? 0 : active.Path.Count - 1) + "/" + definition.Capacity);
                if (definition.Enabled)
                {
                    result.AppendLine("累计支出 " + Ap(seat.TotalActionPointSpent) + "AP / " + seat.TotalManaSpent +
                        "MP；展开 " + seat.SuccessfulDeployments + "，维护 " + seat.MaintenancePayments + "次");
                    result.AppendLine(active == null ? "未展开；后续每全局tick维护 " + definition.UpkeepMana + "MP" :
                        "规则 " + Metric(active.MetricDistanceUnits) + "；支点 " + active.Support.Durability + "/" +
                        active.Support.Definition.MaxDurability + "；维护 " + definition.UpkeepMana + "MP/tick");
                    if (active != null) result.AppendLine("生效边：" + PathText(active.Path) + "；原点 " + CellText(active.Origin));
                    else if (seat.LastEndReason.Length > 0) result.AppendLine("结束：" + Explain(seat.LastEndReason));
                }
                else result.AppendLine(Explain(definition.UnavailableReason));
                result.AppendLine("共源账本 " + Short(seat.Definition.AbilityInstanceId));
            }
            result.Append("待选：" + Metric(metric) + "；" + PathText(path) + "；原点 " +
                (remoteOrigin.HasValue ? CellText(remoteOrigin.Value) + "（+" + DeployDefinition("SPACE_SCALE").remoteOriginManaCost + "MP）" : "自身"));
            return result.ToString();
        }

        private string QuoteText()
        {
            if (frozenQuote == null) return message;
            string action = frozenCommand.Kind == CombatCommandKind.SwapJindan ? "切装→" + Effect(frozenCommand.ProfileId) :
                frozenCommand.Kind == CombatCommandKind.CloseJindan ? "关闭方位定尺" : "展开方位定尺";
            string result = action + "  " + Ap(frozenQuote.ActionPointCostUnits) + "AP / " + frozenQuote.SpiritCost +
                "MP / CT " + frozenQuote.CtPenalty + " / CD " + frozenQuote.ReuseCooldownTicks + "tick";
            if (frozenCommand.Kind == CombatCommandKind.SwapJindan) result += "\n旧持续效果结束；新装配下一本人回合生效。";
            else if (frozenCommand.Kind == CombatCommandKind.CloseJindan) result += "\n移除本来源，按尚存规则重算；不返还资源或重置CD。";
            return result + "\n" + message;
        }

        private JindanDemoCommandDefinition DeployDefinition(string effect) =>
            catalog.commands.First(value => value.effectId == effect && value.kind == "DEPLOY");
        private string Local(string key) => string.IsNullOrEmpty(key) ? string.Empty : text(key);
        private static string Short(string id) => (id ?? string.Empty).Replace("demo_20261024_", string.Empty);
        private static string Ap(long units) => (units / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
        private static string CellText(HexCoord value) => "(" + value.Q + "," + value.R + ")";
        private static string PathText(IEnumerable<HexCoord> values)
        { var items = values.Select(CellText).ToArray(); return items.Length == 0 ? "未选边" : string.Join("→", items); }
        private static string Metric(int units) => units == 1 ? "压缩0.5" : "扩张2";
        private static string Position(string type) => type.ToUpperInvariant() switch
        { "SOURCE" => "源", "TRANSFORMATION" => "化", "DOMAIN" => "界", _ => type };
        private static string Effect(string effect) => effect switch
        {
            "SPACE_SCALE" => "方位定尺", "EARTH_GRAVITY" => "厚载·重力", "EARTH_MANIFEST" => "厚载·造物",
            "WATER_TIDE" => "蓄澜", "FIRE_MANIFEST" => "长明", "YANG_RADIANCE" => "日坛昭明",
            "SPACE_SEAL" => "门枢封关", "YIN_CONCEAL" => "朔坛藏真", _ => effect,
        };
        private string Explain(string reason)
        {
            string localized = Local(reason);
            if (!string.IsNullOrEmpty(localized) && localized != reason) return localized;
            return reason switch
            {
                "combat_quote_stale" => "状态已变化，本次未执行；请重新预演。",
                "action_points_insufficient" => "行动力不足。", "spirit_insufficient" => "共享灵力不足。",
                "combat_ability_on_cooldown" => "同源能力仍在冷却。", "combat_actor_sealed" => "当前封灵，不能展开。",
                "combat_actor_stunned" => "当前眩晕，不能行动。", "combat_turn_actor_invalid" => "当前不是该角色的行动。",
                "jindan_capacity_full" => "已有持续实例；先关闭，不能重复展开。",
                "jindan_instance_missing" => "当前没有可关闭的实例。",
                "jindan_scale_selection_invalid" => "请选择1–2条相连的既有边，并使用同一尺度档。",
                "jindan_scale_edges_not_adjacent" => "选中格必须依次相邻。",
                "jindan_scale_cell_invalid" => "选中格不是合法作用位置。",
                "jindan_scale_edge_blocked" => "该边阻挡作用，不能改写尺度。",
                "jindan_remote_origin_not_empty" => "遥施原点必须是合法空格。",
                "jindan_remote_origin_not_visible" => "遥施原点不在本人当前合法视野内。",
                "jindan_effect_wait_next_own_turn" => "新装配须等下一本人回合才能发动。",
                "jindan_switch_requires_unused_full_turn" => "只能用未提交收益命令的完整本人回合预算切装。",
                "jindan_effect_not_equipped" => "该效果当前未装配。", "jindan_seat_not_owned" => "当前角色不持有该席。",
                "jindan_effect_candidate_invalid" => "该效果不是可切换的同席候选。",
                "jindan_upkeep_failed" => "维护无法完整支付，实例已结束。",
                "jindan_support_destroyed" => "方位标被破坏，尺度已恢复。",
                "jindan_owner_dead" => "持有者死亡。", "jindan_source_disabled" => "来源失效。",
                "jindan_session_ended" => "演武会话已结束。", "jindan_closed" => "已主动关闭。",
                "jindan_switched_end" => "切装结束了旧效果。",
                "above_max_range" => "目标超出当前加权射程。", "below_min_range" => "目标低于最小射程。",
                "attack_source_mode_not_allowed" => "当前移动模式不能发动该能力。",
                "attack_target_mode_not_allowed" => "目标移动模式不符合能力要求。",
                "cell_not_configured" => "目标不在合法地图格内。",
                "attack_vertical_reach_exceeded" => "目标超过垂直触及高度。",
                "scene_object_blocks_effects" or "entity_wall_blocks_effects" or "sight_blocked" => "实体墙或遮挡阻断作用路径。",
                "demo_20261024_unavailable_d6" => "飞行待D6接入。", "demo_20261024_unavailable_d7" => "土能力待D7接入。",
                "demo_20261024_unavailable_d8" => "水能力待D8接入。", "demo_20261024_unavailable_d9" => "封锁待D9接入。",
                "demo_20261024_unavailable_d10" => "火能力待D10接入。", "demo_20261024_unavailable_d11" => "显隐待D11接入。",
                "jindan_effect_not_implemented" => "该能力尚未接入，可查看来源和费用。",
                _ => "本次条件不满足，请重新选择并预演。",
            };
        }
    }
}
