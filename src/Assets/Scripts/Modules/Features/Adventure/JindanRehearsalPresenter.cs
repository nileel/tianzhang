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
        private CombatEarthMode earthMode = CombatEarthMode.Wall;
        private HexCoord? earthCell;
        private string earthTarget;
        private bool earthPage;
        private bool tidePage, tideCharged, tideEye;
        private HexCoord? tideSource;
        private CombatTideAction tideAction = CombatTideAction.Rise;
        private enum SelectionMode { None, Edges, Origin, EarthTarget, BreakTarget, TideSource }

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
        public TianZhang.Gameplay.Contracts.CombatSurfacePresentationSnapshot SurfacePresentation =>
            JindanTidePresentation.Project(encounter.CurrentSession, frozenQuote, frozenCommand);
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
                        message = "进入演武；飞行与方位定尺均先预演再确认。";
                        break;
                }
                Refresh();
                return;
            }
            if (encounter.CurrentSession?.Jindan == null) return;
            if (key is "flight_tab" or "earth_tab" or "scale_tab" or "tide_tab")
            { earthPage = key == "earth_tab"; tidePage = key == "tide_tab"; ClearTargeting(); Refresh(); return; }
            if (key == "cancel_targeting")
            { ClearTargeting(); message = "已取消，未扣费。"; Refresh(); return; }
            if (!encounter.AcceptsPlayerCommand)
            { message = "等待当前演出或敌方行动结束。"; Refresh(); return; }
            switch (key)
            {
                case "tide_rise": case "tide_ebb": case "tide_eye":
                    ClearTargeting(); tidePage = true; earthPage = false;
                    tideAction = key == "tide_ebb" ? CombatTideAction.Ebb : CombatTideAction.Rise;
                    tideEye = key == "tide_eye"; selection = SelectionMode.TideSource;
                    message = tideEye ? "点击无源时的合法潮眼格，再预演。" : "点击一个合法潮源，再预演；青色显示变化格，箭头显示实际落点。"; break;
                case "tide_charge": tideCharged = !tideCharged; ClearQuote(); message = tideCharged ? "一档蓄力：先付费，1全局tick后完成时重验。" : "普通潮汐：确认即结算。"; break;
                case "tide_preview": Preview(CombatCommandKind.JindanAbility); break;
                case "tide_close": Preview(CombatCommandKind.CloseJindan); break;
                case "earth_tab": earthPage=true; ClearTargeting(); break;
                case "scale_tab": earthPage=false; ClearTargeting(); break;
                case "earth_wall": case "earth_ramp":
                    ClearTargeting(); earthPage=true; earthMode=key=="earth_wall"?CombatEarthMode.Wall:CombatEarthMode.Ramp;
                    selection=SelectionMode.Edges; message="依次选择一条空边的两个相邻落点，再预演。"; break;
                case "earth_heavy": case "earth_light": case "earth_anchor":
                    ClearTargeting(); earthPage=true; earthMode=key=="earth_heavy"?CombatEarthMode.Heavy:key=="earth_light"?CombatEarthMode.Light:CombatEarthMode.Anchor;
                    selection=SelectionMode.EarthTarget; message="点击单位的明确身体格或土石构件；同格对象可用切换目标浏览。"; break;
                case "earth_next_target": if(earthCell.HasValue) SelectEarthTarget(earthCell.Value,true); break;
                case "break_object": ClearTargeting(); selection=SelectionMode.BreakTarget; message="点击相邻墙／土石支点所在格，预演破垒；两次命中破750耐久。"; break;
                case "compress": metric = 1; ClearQuote(); message = "已选压缩：每边0.5距离；请选择2–3个相连格。"; break;
                case "expand": metric = 4; ClearQuote(); message = "已选扩张：每边2距离；请选择2–3个相连格。"; break;
                case "pick_edges":
                    path.Clear(); selection = SelectionMode.Edges; ClearQuote();
                    message = "在地图依次点击2–3个相连格；点末格可撤销。"; break;
                case "pick_origin":
                    selection = SelectionMode.Origin; ClearQuote();
                    message = "点击当前视野内空格作遥施原点；点自身改回自身。"; break;
                case "preview": case "earth_preview": Preview(CombatCommandKind.JindanAbility); break;
                case "close_effect": case "earth_close": Preview(CombatCommandKind.CloseJindan); break;
                case "swap_effect": case "earth_swap": Preview(CombatCommandKind.SwapJindan); break;
                case "confirm": Confirm(); break;
                case "flight_takeoff": PreviewFlight(CombatFlightAction.Takeoff); break;
                case "flight_ascend": PreviewFlight(CombatFlightAction.Ascend); break;
                case "flight_descend": PreviewFlight(CombatFlightAction.Descend); break;
                case "flight_land": PreviewFlight(CombatFlightAction.Land); break;
                case "flight_recover": PreviewFlight(CombatFlightAction.Recover); break;
            }
            Refresh();
        }

        public void PickCell(int q, int r)
        {
            Refresh();
            if (!IsSelecting || !encounter.AcceptsPlayerCommand || encounter.CurrentSession?.Jindan == null) return;
            var cell = new HexCoord(q, r);
            ClearQuote();
            if (selection == SelectionMode.TideSource)
            { tideSource = cell; selection = SelectionMode.None; message = "潮源已选" + CellText(cell) + "；点击预演查看变化格、推移与水击。"; Refresh(); return; }
            if (selection == SelectionMode.EarthTarget)
            { SelectEarthTarget(cell,false); Refresh(); return; }
            if (selection == SelectionMode.BreakTarget)
            {
                selection=SelectionMode.None;
                frozenCommand=new CombatCommand(CombatCommandKind.Art,encounter.ControlledActorId,
                    profileId:catalog.demolition.profileId,targetCell:cell);
                frozenQuote=encounter.PreviewJindan(frozenCommand);
                message=frozenQuote?.IsLegal==true?"报价已冻结，请核对环境目标后确认。":Explain(frozenQuote?.RejectionReason);
                Refresh(); return;
            }
            if (selection == SelectionMode.Origin)
            {
                encounter.CurrentSession.Combatants.TryGet(encounter.ControlledActorId, out var actor);
                remoteOrigin = actor.Pose.Contains(cell) ? (HexCoord?)null : cell;
                selection = SelectionMode.None;
                message = remoteOrigin.HasValue ? "已选遥施原点；预演时检查视野、空格与费用。" : "已恢复自身原点。";
            }
            else if (path.Count > 0 && path[path.Count - 1] == cell)
            { path.RemoveAt(path.Count - 1); message = "已撤销末格。"; }
            else if (path.Count == (earthPage?2:3)) message = "已达当前形态选格上限；点末格撤销或重新选边。";
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

        private void SelectEarthTarget(HexCoord cell,bool next)
        {
            var s=encounter.CurrentSession;
            var ids=s.Combatants.All.Where(a=>a.IsAlive && a.OccupiedCells.Contains(cell)).Select(a=>a.Id)
                .Concat(s.SceneObjects.All.Where(o=>!o.IsDestroyed && o.Contains(cell) && o.Definition.MaterialType is "stone" or "earth").Select(o=>o.Id)).ToArray();
            earthCell=cell;
            earthTarget=ids.Length==0?null:ids[next?(Array.IndexOf(ids,earthTarget)+1)%ids.Length:0];
            ClearQuote(); message=earthTarget==null?"此格没有合法受力对象。":"已选"+Short(earthTarget)+"，身体格"+CellText(cell)+"；点击预演。";
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
                : owned.FirstOrDefault(value => tidePage ? value.EffectId == "WATER_TIDE" : earthPage ? value.EffectId is "EARTH_MANIFEST" or "EARTH_GRAVITY" : value.EffectId == "SPACE_SCALE");
            if (seat == null)
            {
                message = kind == CombatCommandKind.SwapJindan ? "当前角色没有可切换的同席候选。" :
                    "当前角色没有本页装配；请切换位格／土系页或查看能力。";
                return;
            }
            string effect = kind == CombatCommandKind.SwapJindan
                ? seat.Definition.Candidates.First(value => value != seat.EffectId) : seat.EffectId;
            var request = kind == CombatCommandKind.JindanAbility
                ? tidePage ? new CombatJindanRequest(seat.Definition.SeatId, tideAction: tideAction, tideSource: tideSource,
                    charged: tideCharged, createTideEye: tideEye)
                : new CombatJindanRequest(seat.Definition.SeatId, path, metric, remoteOrigin,earthPage?earthMode:null,earthTarget,earthCell)
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

        private void PreviewFlight(CombatFlightAction action)
        {
            ClearTargeting();
            frozenCommand = new CombatCommand(CombatCommandKind.Flight, encounter.ControlledActorId,
                profileId: CombatFlight.ProfileId, flightAction: action);
            frozenQuote = encounter.PreviewJindan(frozenCommand);
            message = frozenQuote?.IsLegal == true ? "报价已冻结，请核对后确认。" : Explain(frozenQuote?.RejectionReason);
        }

        private void ClearQuote() { frozenCommand = null; frozenQuote = null; }
        private void ClearTargeting()
        { ClearQuote(); path.Clear(); remoteOrigin = null; earthCell=null; earthTarget=null; tideSource = null; selection = SelectionMode.None; }
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
                result.AppendLine("独立飞行：" + Short(preset.flightAbilityInstanceId) + "；起飞250AP/200MP，后续本人回合开始维护100MP，最多3回合。");
            result.AppendLine("演武徒手允许地面／空中出手与对空，垂直触及1；费用、伤害与射程沿原金丹徒手。");
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
            result.Append(JindanSealPresentation.Details(session));
            if (tidePage) return result + JindanTidePresentation.Details(session, actor, tideSource, tideAction, tideCharged, tideEye);
            var flight = session.Flight?.Find(actorId);
            if (flight != null)
            {
                session.SceneObjects.Board.TryGetCell(actor.Position, out var cell);
                result.AppendLine("飞行·" + (actor.AltitudeBand == 0 ? "地面" : actor.AltitudeBand == 1 ? "低空" : "高空") +
                    "  有效高度 " + (cell.HeightLevel + actor.AltitudeBand) + "  接地 " + (actor.AltitudeBand == 0 ? "是" : "否") +
                    "  受力"+CombatForceMovement.LoadRank(actor)+"／升力2");
                if (!earthPage)
                {
                result.AppendLine("来源 " + Short(flight.InstanceId) + "  CD " + actor.GetCooldown(CombatFlight.ProfileId) + "tick");
                result.AppendLine("剩余后续回合结束 " + flight.RemainingOwnTurnEnds + "；下一本人回合维护 " +
                    (flight.Active ? session.Flight.UpkeepMana : 0) + "MP；已付 " + flight.TotalManaSpent + "MP");
                string landing = session.Flight.LandingReason(session, actor);
                result.AppendLine("本柱落地：" + (landing.Length == 0 ? "合法" : Explain(landing)));
                if (flight.FailedLanding) result.AppendLine("失效滞空：不能自主移动／升降；后续本人回合结束损失20%最大生命，可有费续御。");
                else if (flight.LastEndReason.Length > 0) result.AppendLine(Explain(flight.LastEndReason));
                result.AppendLine("失效先尝试原柱落地并扣10%最大生命；无落点保留占柱进入有限失效过程。");
                }
            }
            foreach (var seat in session.Jindan.Seats.Where(value => value.Definition.ActorId == actorId))
            {
                var definition = session.Jindan.Ability(seat.EffectId);
                var active = seat.ActiveScale;
                result.AppendLine(Position(seat.Definition.PositionType) + "·" + Effect(seat.EffectId) +
                    "  CD " + actor.GetCooldown(seat.EffectId) + "tick  容量 " + (seat.ActiveEarth!=null ? Math.Max(1,seat.ActiveEarth.Objects.Count) : active == null ? 0 : active.Path.Count - 1) + "/" + definition.Capacity);
                if (definition.Enabled)
                {
                    result.AppendLine("累计支出 " + Ap(seat.TotalActionPointSpent) + "AP / " + seat.TotalManaSpent +
                        "MP；展开 " + seat.SuccessfulDeployments + "，维护 " + seat.MaintenancePayments + "次");
                    result.AppendLine(seat.ActiveEarth!=null ? "土系实例已展开，维护0MP" +
                        (seat.ActiveEarth.TargetId == null ? "；剩余施法者结束 " + seat.ActiveEarth.RemainingOwnTurnEnds : "；期限见受力目标") : active == null ? "未展开；维护 " + definition.UpkeepMana + "MP" :
                        "规则 " + Metric(active.MetricDistanceUnits) + "；支点 " + active.Support.Durability + "/" +
                        active.Support.Definition.MaxDurability + "；维护 " + definition.UpkeepMana + "MP/tick");
                    if (active != null) result.AppendLine("生效边：" + PathText(active.Path) + "；原点 " + CellText(active.Origin));
                    else if (seat.LastEndReason.Length > 0) result.AppendLine("结束：" + Explain(seat.LastEndReason));
                }
                else result.AppendLine(Explain(definition.UnavailableReason));
                result.AppendLine("共源账本 " + Short(seat.Definition.AbilityInstanceId));
                if(seat.ActiveEarth!=null) foreach(var obj in seat.ActiveEarth.Objects)
                    result.AppendLine((obj.Definition.IsTemporaryRamp?"窄坡":"岩墙")+" "+CellText(obj.Cell)+"→"+CellText(obj.EdgeOther.Value)+" 耐久"+obj.Durability+"/"+obj.Definition.MaxDurability);
                if(seat.ActiveEarth?.ObjectStatus is {} objectStatus)
                    result.AppendLine("构件 "+Short(seat.ActiveEarth.TargetId)+" "+EarthName(objectStatus.EarthMode.Value)+
                        " 档差"+objectStatus.GravityShiftRank+"；剩余施法者结束"+seat.ActiveEarth.RemainingOwnTurnEnds);
            }
            foreach(var unit in session.Combatants.All)
            {
                var gravity=CombatForceMovement.Gravity(unit); if(gravity==null)continue;
                result.AppendLine(Short(unit.Id)+" "+EarthName(gravity.EarthMode.Value)+" 档差"+gravity.GravityShiftRank+
                    " 受力"+CombatForceMovement.LoadRank(unit)+" 压力"+gravity.Pressure+"/2；剩余目标结束"+gravity.RemainingOwnTurnEnds);
            }
            if(earthPage) { result.Append("待选："+EarthName(earthMode)+"；"+PathText(path)+"；对象"+Short(earthTarget)); return result.ToString(); }
            result.Append("待选：" + Metric(metric) + "；" + PathText(path) + "；原点 " +
                (remoteOrigin.HasValue ? CellText(remoteOrigin.Value) + "（+" + DeployDefinition("SPACE_SCALE").remoteOriginManaCost + "MP）" : "自身"));
            return result.ToString();
        }

        private string QuoteText()
        {
            if (frozenQuote == null) return message;
            string action = frozenCommand.Kind == CombatCommandKind.Flight ? FlightName(frozenCommand.FlightAction.Value) :
                frozenCommand.Kind == CombatCommandKind.SwapJindan ? "切装→" + Effect(frozenCommand.ProfileId) :
                frozenCommand.Kind == CombatCommandKind.CloseJindan ? "关闭"+Effect(frozenCommand.ProfileId) :
                frozenCommand.Kind == CombatCommandKind.Art ? "演武·破垒" : frozenCommand.Jindan?.TideAction is {} water
                    ? JindanTidePresentation.Name(water, frozenCommand.Jindan.Charged, frozenCommand.Jindan.CreateTideEye)
                    : frozenCommand.Jindan?.EarthMode is {} mode ? EarthName(mode) : "展开方位定尺";
            string result = action + "  " + Ap(frozenQuote.ActionPointCostUnits) + "AP / " + frozenQuote.SpiritCost +
                "MP / CT " + frozenQuote.CtPenalty + " / CD " + frozenQuote.ReuseCooldownTicks + "tick";
            if (frozenCommand.Kind == CombatCommandKind.SwapJindan) result += "\n旧持续效果结束；新装配下一本人回合生效。";
            else if(frozenCommand.Kind==CombatCommandKind.Art) result+="\n环境命中目标："+string.Join("、",frozenQuote.AttackArea?.EnvironmentTargetIds??Array.Empty<string>());
            else if (frozenCommand.Kind == CombatCommandKind.CloseJindan) result += "\n移除本来源，按尚存规则重算；不返还资源或重置CD。";
            if (frozenQuote.Tide != null) result += "\n" + JindanTidePresentation.Quote(frozenQuote.Tide, frozenCommand.Jindan);
            if (frozenCommand.Kind == CombatCommandKind.JindanAbility && frozenCommand.Jindan?.EarthMode is {} earth)
            {
                var session = encounter.CurrentSession;
                var seat = session.Jindan.Find(frozenCommand.Jindan.SeatId);
                var active = seat?.ActiveEarth;
                if (earth is CombatEarthMode.Wall or CombatEarthMode.Ramp)
                    result += "\n接地受养：是；耐久750；容量" + (active?.Objects.Count ?? 0) + "→" + ((active?.Objects.Count ?? 0)+1) +
                        "/2；剩余施法者结束" + (active?.RemainingOwnTurnEnds ?? 3) + "（追加不刷新）。";
                else
                {
                    session.Combatants.TryGet(frozenCommand.ActorId, out var caster);
                    session.Combatants.TryGet(frozenCommand.Jindan.EarthTargetId ?? "", out var target);
                    bool nourished = caster.MovementMode == SpatialMovementMode.Ground || target == null || target.MovementMode == SpatialMovementMode.Ground;
                    result += "\n接地受养：" + (nourished ? "是" : "否") + "；有效档差" + (nourished ? 3 : 2) +
                        "；压力" + (target == null ? 0 : CombatForceMovement.Gravity(target)?.Pressure ?? 0) + "/2；持续2次" + (target == null ? "施法者" : "目标") + "本人结束。";
                }
            }
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
        private static string EarthName(CombatEarthMode mode)=>mode switch { CombatEarthMode.Wall=>"岩墙",CombatEarthMode.Ramp=>"窄土坡",CombatEarthMode.Heavy=>"极重",CombatEarthMode.Light=>"失重",_=>"镇岳" };
        private static string Position(string type) => type.ToUpperInvariant() switch
        { "SOURCE" => "源", "TRANSFORMATION" => "化", "DOMAIN" => "界", _ => type };
        internal static string Effect(string effect) => effect switch
        {
            "SPACE_SCALE" => "方位定尺", "EARTH_GRAVITY" => "厚载·重力", "EARTH_MANIFEST" => "厚载·造物",
            "WATER_TIDE" => "蓄澜", "FIRE_MANIFEST" => "长明", "YANG_RADIANCE" => "日坛昭明",
            "SPACE_SEAL" => "门枢封关", "YIN_CONCEAL" => "朔坛藏真", _ => effect,
        };
        private static string FlightName(CombatFlightAction action) => action switch
        {
            CombatFlightAction.Takeoff => "原地起飞", CombatFlightAction.Ascend => "升至高空",
            CombatFlightAction.Descend => "降至低空", CombatFlightAction.Land => "原柱降落", _ => "续御脱险",
        };
        private string Explain(string reason)
        {
            if (reason?.StartsWith("tide_", StringComparison.Ordinal) == true) return JindanTidePresentation.Explain(reason);
            string localized = Local(reason);
            if (!string.IsNullOrEmpty(localized) && localized != reason) return localized;
            return reason switch
            {
                "combat_quote_stale" => "状态已变化，本次未执行；请重新预演。",
                "earth_selection_invalid"=>"选择须符合当前装配：显化选墙／坡和空边，重力选模式与对象。",
                "earth_anchor_occupied"=>"该边或落点已有主场景物。", "earth_edge_blocked"=>"该边被作用封锁。",
                "earth_ramp_height_invalid"=>"土坡只接相邻高差1，不能覆盖固定坡阶。", "earth_target_invalid"=>"受力对象或所选身体格无效。",
                "earth_anchored"=>"受锚定限制，当前力量不足以移动。", "earth_pressure_limit"=>"极重压力已满，须完成一个无位移本人回合。",
                "flight_load_exceeded"=>"当前受力超过该飞行高度的升力。",
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
                "flight_source_missing" => "没有可用的御风飞行来源。",
                "flight_not_active" => "飞行已失效，不能自主移动。",
                "flight_takeoff_requires_ground" => "只能从地面起飞，不能重复起飞刷新期限。",
                "flight_recovery_requires_failure" => "仅失效滞空时可续御。",
                "flight_landing_requires_low" => "主动降落须先降低至低空。",
                "flight_altitude_invalid" => "当前高度不满足本次升降要求。",
                "flight_ceiling_exceeded" => "超出该柱允许的最高有效高度。",
                "flight_cell_not_permitted" or "flight_edge_not_permitted" => "该柱或格边未允许飞行。",
                "flight_height_difference_exceeded" => "当前飞行档位不能跨越此高差。",
                "cell_movement_blocked" or "movement_blocked" => "本柱地面不可站立。",
                "cell_occupied" or "occupied" => "该柱已有单位，不能叠放或落地。",
                "flight_upkeep_failed" => "维护不足，已执行失效落地处理。",
                "flight_expired" => "飞行期限已到，已执行失效落地处理。",
                "flight_landed" or "flight_rescued_landing" => "已接地，停止飞行维护。",
                "flight_owner_dead" => "飞行持有者死亡。",
                "flight_session_ended" => "演武结束，飞行已清理。",
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
