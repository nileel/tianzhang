using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TianZhang.Character;
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
        private readonly CharacterRuntimeProfile trialPlayer;
        private readonly string trialPresetId;
        private bool showDetails;
        private string resultText;
        public bool IsPlayerChallenge => trialPlayer != null;
        public bool ShowingResult => resultText != null;
        public bool ShowingDetails => showDetails;
        private readonly List<HexCoord> path = new List<HexCoord>(3);
        private int segmentIndex = 2;
        private int presetIndex;
        private string earthEffect = "EARTH_GRAVITY";
        private int metric = 1;
        private HexCoord? remoteOrigin;
        private int edgesWhenOriginPicked;
        private SelectionMode selection;
        private CombatCommand frozenCommand;
        private CombatCommandPreview frozenQuote;
        private HexCoord? hoveredCell;
        private CombatCommand hoverCommand;
        private CombatCommandPreview hoverQuote;
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
        private enum SelectionMode { None, Edges, Origin, EarthTarget, BreakTarget, TideSource, Support }
        private string selectedEffect;
        private CombatJindanSeatState supportSeat;
        private IReadOnlyList<HexCoord> supportChoices = Array.Empty<HexCoord>();
        public IReadOnlyList<HexCoord> SelectableSupports => selection == SelectionMode.Support ? supportChoices : Array.Empty<HexCoord>();

        public JindanRehearsalPresenter(JindanDemoCatalogData catalog, EncounterCoordinator encounter,
            Action<int, string> enter, Func<string, string> text, CharacterRuntimeProfile trialPlayer = null, string trialPresetId = null)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.encounter = encounter ?? throw new ArgumentNullException(nameof(encounter));
            this.enter = enter ?? throw new ArgumentNullException(nameof(enter));
            this.text = text ?? throw new ArgumentNullException(nameof(text));
            this.trialPlayer = trialPlayer;
            this.trialPresetId = trialPresetId;
            if (IsPlayerChallenge)
            {
                segmentIndex = -1;
                earthEffect = null; // Enter with each saved build's catalog defaults.
            }
            SelectSegmentPreset();
            Refresh();
        }

        public bool PreparationOpen { get; private set; }
        public bool ControlFire { get; private set; }
        public bool IsSelecting => selection != SelectionMode.None;
        public IReadOnlyList<HexCoord> SelectedEndpoints => path;
        private HexCoord? FixedSupport => supportSeat?.EffectId switch
        {
            "FIRE_MANIFEST" => supportSeat.FireDomain.HasPlacedSupport ? supportSeat.FireDomain.Support.Cell : (HexCoord?)null,
            "SPACE_SEAL" => encounter.CurrentSession.Jindan.Ability(supportSeat.EffectId).Seal.To,
            "YIN_CONCEAL" or "YANG_RADIANCE" => encounter.CurrentSession.Jindan.Ability(supportSeat.EffectId).Visibility.Center,
            _ => null,
        };
        public HexCoord? TargetCell => frozenCommand?.Jindan?.SelectedTargetCell ?? FixedSupport ?? earthCell ?? tideSource ?? remoteOrigin ?? frozenCommand?.TargetCell;
        public string InteractionHint
        {
            get
            {
                if (selection == SelectionMode.Support)
                {
                    if (hoverQuote != null && hoverQuote.IsLegal)
                        return SupportLabel + " " + CellText(hoveredCell.Value) + " · 左键锁定，确认后施行";
                    if (hoverQuote != null && FixedSupport != hoveredCell)
                        return (FixedSupport.HasValue ? "请选择" + SupportLabel + " " + CellText(FixedSupport.Value) : Explain(hoverQuote.RejectionReason));
                    return message;
                }
                if (hoverQuote != null && frozenQuote != null)
                    return "第三格 " + CellText(hoveredCell.Value) + " · " +
                        (hoverQuote.IsLegal ? "左键追加并锁定" : Explain(hoverQuote.RejectionReason));
                if (frozenQuote?.IsLegal == true)
                {
                    if (frozenQuote.Tide is { } tide && tide.Pushes.Count > 0)
                    {
                        var visible = tide.Pushes.Where(push => encounter.CurrentSession.Visibility?.IsVisible(frozenQuote.ActorId, push.ActorId) != false);
                        string landings = string.Join("；", visible.Select(push =>
                            (push.ActorId == frozenQuote.ActorId ? "本人" : encounter.ActorName(push.ActorId)) +
                            (push.Movement.Path.Count == 0 ? "留在 " : "推至 ") + CellText(push.Movement.Destination.Anchor) +
                            (push.Movement.Reason == "earth_anchored" ? "（镇岳挡推，仍受水击）" : "")));
                        if (landings.Length > 0) return "潮源 " + CellText(frozenCommand.Jindan.TideSource.Value) + " · " + landings;
                    }
                    if (selection == SelectionMode.Edges && !earthPage && path.Count == 2)
                        return "已锁定 " + PathText(path) + " · 可确认或再选相邻第三格";
                    string target = path.Count > 0 ? PathText(path) : TargetCell is {} cell
                        ? (earthTarget == null ? "" : Short(earthTarget) + " ") + CellText(cell) : "自身操作";
                    if (remoteOrigin.HasValue && path.Count > 0) target += " · 原点 " + CellText(remoteOrigin.Value);
                    return "已锁定 " + target + " · 点击确认施行";
                }
                if (selection == SelectionMode.Edges)
                {
                    string step = path.Count == 0 ? "选择第一个格" : "首格 " + CellText(path[0]) + " · 选择相邻第二格";
                    return hoverQuote?.IsLegal == false ? step + " · " + Explain(hoverQuote.RejectionReason) : step;
                }
                if (hoverQuote?.IsLegal == false) return Explain(hoverQuote.RejectionReason);
                if (selection == SelectionMode.EarthTarget)
                {
                    if (!hoveredCell.HasValue) return "选择可受力的单位身体格或土石构件";
                    return hoverQuote == null ? "此格没有合法受力目标，请重新选择" :
                        Short(hoverCommand.Jindan.EarthTargetId) + " " + CellText(hoveredCell.Value) + " · 左键锁定";
                }
                if (selection == SelectionMode.TideSource) return "选择合法潮源格";
                if (selection == SelectionMode.Origin) return message;
                return message;
            }
        }
        public string PreparationDetails { get; private set; } = string.Empty;
        public string CombatDetails { get; private set; } = string.Empty;
        public string QuoteDetails { get; private set; } = string.Empty;
        private CombatCommand DisplayCommand => hoverCommand ?? frozenCommand;
        private CombatCommandPreview DisplayQuote => hoverQuote ?? frozenQuote;
        public string QuoteTitle => DisplayCommand?.Kind == CombatCommandKind.CloseJindan ? "收势" :
            (DisplayCommand?.ProfileId ?? selectedEffect) switch
            {
                CombatFlight.ProfileId => "御风", "SPACE_SCALE" => "定尺", "SPACE_SEAL" => "封关",
                "YANG_RADIANCE" => "阳照", "YIN_CONCEAL" => "藏真", null => "收势", var effect => Effect(effect),
            };
        public string CompactQuote
        {
            get
            {
                var quote = DisplayQuote;
                if (quote == null) return IsSelecting ? "选定目标后报价" : "选择操作，取得报价";
                if (!quote.IsLegal) return "不可施行\n<size=16>" + Explain(quote.RejectionReason) + "</size>";
                string cost = Ap(quote.ActionPointCostUnits) + " AP · " + quote.SpiritCost + " MP";
                var session = encounter.CurrentSession;
                if (session == null || !session.Combatants.TryGet(quote.ActorId, out var actor)) return cost;
                return cost + "\n<size=16>AP " + Ap(actor.ActionPointsRemainingUnits) + " → " +
                    Ap(actor.ActionPointsRemainingUnits - quote.ActionPointCostUnits) + "</size>";
            }
        }
        public TianZhang.Gameplay.Contracts.JindanHudSnapshot Hud => JindanHudPresentation.Project(
            encounter.CurrentSession, encounter.ControlledActorId, encounter.CurrentSession != null && IsPlayerChallenge ?
                encounter.Challenge?.ProgressText : StageName, !IsPlayerChallenge && ControlFire);

        private string StageName => IsPlayerChallenge ? JindanChallengeProgress.StageName(segmentIndex) :
            Local(catalog.segments[segmentIndex].displayNameKey);
        public void ShowResult(string label, string explanation)
        {
            var challenge = encounter.Challenge;
            resultText = label + "\n" + StageName + "\n\n" + explanation +
                "\n\n配置 v" + challenge.ConfigVersion + " · 种子 " + challenge.Seed;
            PreparationOpen = true;
            showDetails = false;
            ClearTargeting();
        }
        public void ShowNotice(string notice) => message = notice;
        public TianZhang.Gameplay.Contracts.CombatSurfacePresentationSnapshot SurfacePresentation =>
            JindanTidePresentation.Project(encounter.CurrentSession, DisplayQuote, DisplayCommand);
        public bool CanConfirm => frozenQuote != null && hoverQuote == null && frozenQuote.IsLegal && encounter.AcceptsPlayerCommand &&
            frozenCommand.ActorId == encounter.ControlledActorId && observedSession == encounter.CurrentSession &&
            frozenQuote.StateVersion == observedSession.StateVersion && frozenQuote.TurnSequence == observedSession.TurnSequence;

        public void OpenPreparation()
        {
            if (encounter.IsRunning) return;
            PreparationOpen = true;
            showDetails = false;
            resultText = null;
            ClearTargeting();
            message = string.Empty;
            Refresh();
        }

        public void HandleAction(string key)
        {
            Refresh();
            if (PreparationOpen)
            {
                if (ShowingResult && key != "retry" && key != "choose_challenge") return;
                if (IsPlayerChallenge && key is "preset_next" or "fire_side" or "earth_toggle") return;
                if (key is "basic" or "stage1" or "stage2" or "stage3") { showDetails = false; message = string.Empty; }
                switch (key)
                {
                    case "choose_challenge": OpenPreparation(); break;
                    case "prep_details": showDetails = !showDetails; break;
                    case "basic": if (IsPlayerChallenge) { segmentIndex = -1; SelectSegmentPreset(); } break;
                    case "stage1": segmentIndex = 0; SelectSegmentPreset(); break;
                    case "stage2": segmentIndex = 1; SelectSegmentPreset(); break;
                    case "stage3": segmentIndex = 2; SelectSegmentPreset(); break;
                    case "preset_next": presetIndex = (presetIndex + 1) % catalog.presets.Length; break;
                    case "earth_toggle": earthEffect = earthEffect == "EARTH_GRAVITY" ? "EARTH_MANIFEST" : "EARTH_GRAVITY"; break;
                    case "fire_side": segmentIndex = 1; ControlFire = !ControlFire; SelectSegmentPreset(); break;
                    case "cancel_preparation": PreparationOpen = false; message = string.Empty; break;
                    case "retry": case "enter":
                        resultText = null;
                        enter(segmentIndex, earthEffect);
                        PreparationOpen = false;
                        ClearTargeting();
                        message = IsPlayerChallenge ? "阶地战斗开始。选定目标后确认行动。" : "进入演武；能力均先预演再确认。";
                        break;
                }
                Refresh();
                return;
            }
            if (encounter.CurrentSession?.Jindan == null) return;
            if (key is "flight_tab" or "earth_tab" or "scale_tab" or "seal_tab" or "tide_tab" or "visibility_tab" or "fire_tab" or "close_tab")
            {
                earthPage = key == "earth_tab"; tidePage = key == "tide_tab";
                ClearTargeting();
                selectedEffect = Hud?.Abilities.FirstOrDefault(ability => ability.Key == key)?.EffectId;
                message = JindanHudPresentation.Description(selectedEffect);
                Refresh();
                return;
            }
            if (key == "cancel_targeting")
            { ClearTargeting(); message = "已取消，未扣费。"; Refresh(); return; }
            if (!encounter.AcceptsPlayerCommand)
            { message = "等待当前演出或敌方行动结束。"; Refresh(); return; }
            switch (key)
            {
                case "visibility_deploy": BeginSupportSelection(selectedEffect); break;
                case "visibility_close": PreviewVisibility(CombatCommandKind.CloseJindan); break;
                case "seal_deploy": BeginSupportSelection("SPACE_SEAL"); break;
                case "seal_close": PreviewVisibility(CombatCommandKind.CloseJindan, true); break;
                case "tide_rise": case "tide_ebb": case "tide_eye":
                    ClearTargeting(); tidePage = true; earthPage = false;
                    tideAction = key == "tide_ebb" ? CombatTideAction.Ebb : CombatTideAction.Rise;
                    tideEye = key == "tide_eye"; selection = SelectionMode.TideSource;
                    message = tideEye ? "点击无源时的合法潮眼格，再预演。" : "点击一个合法潮源，再预演；青色显示变化格，箭头显示实际落点。"; break;
                case "tide_charge":
                    tideCharged = !tideCharged;
                    if (tideSource.HasValue) Preview(CombatCommandKind.JindanAbility);
                    else { ClearQuote(); message = tideCharged ? "一档蓄力，选择潮源" : "普通潮汐，选择潮源"; }
                    break;
                case "tide_preview": Preview(CombatCommandKind.JindanAbility); break;
                case "tide_close": earthPage = false; tidePage = true; Preview(CombatCommandKind.CloseJindan); break;
                case "fire_preview": BeginSupportSelection(CombatFireDomainRules.Effect); break;
                case "fire_close": Preview(CombatCommandKind.CloseJindan, true); break;
                case "earth_wall": case "earth_ramp":
                    ClearTargeting(); earthPage=true; earthMode=key=="earth_wall"?CombatEarthMode.Wall:CombatEarthMode.Ramp;
                    selection=SelectionMode.Edges; message="选择一条空边的第一个格"; break;
                case "earth_heavy": case "earth_light": case "earth_anchor":
                    ClearTargeting(); earthPage=true; earthMode=key=="earth_heavy"?CombatEarthMode.Heavy:key=="earth_light"?CombatEarthMode.Light:CombatEarthMode.Anchor;
                    selection=SelectionMode.EarthTarget; message="点击单位的明确身体格或土石构件；同格对象可用切换目标浏览。"; break;
                case "earth_next_target": if (earthCell.HasValue) LockNextEarthTarget(); break;
                case "break_object": ClearTargeting(); selection=SelectionMode.BreakTarget; message="点击相邻墙／土石支点所在格，预演破垒；两次命中破750耐久。"; break;
                case "compress": case "expand":
                    metric = key == "compress" ? 1 : 4;
                    ClearQuote();
                    if (path.Count >= 2) Preview(CombatCommandKind.JindanAbility);
                    else selection = SelectionMode.Edges;
                    message = metric == 1 ? "压缩到每边0.5距离，选相连格。" : "扩张到每边2距离，选相连格。";
                    break;
                case "pick_edges":
                    path.Clear(); edgesWhenOriginPicked = 0; selection = SelectionMode.Edges; ClearQuote();
                    message = "依次选择2–3个相连格，右键或 Esc 撤销末格"; break;
                case "pick_origin":
                    selection = SelectionMode.Origin; ClearQuote();
                    message = "点击当前视野内空格作遥施原点；点自身改回自身。"; break;
                case "preview": case "earth_preview": Preview(CombatCommandKind.JindanAbility); break;
                case "close_effect": case "earth_close":
                    earthPage = key == "earth_close"; tidePage = false; Preview(CombatCommandKind.CloseJindan); break;
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

        public void HoverCell(int? q, int? r)
        {
            HexCoord? cell = q.HasValue && r.HasValue ? new HexCoord(q.Value, r.Value) : (HexCoord?)null;
            if (!IsSelecting || !encounter.AcceptsPlayerCommand) cell = null;
            if (hoveredCell == cell) return;
            ClearHover();
            hoveredCell = cell;
            if (selection == SelectionMode.Origin && path.Count < 2)
            {
                string reason = cell.HasValue ? CheckOrigin(cell.Value) : string.Empty;
                message = reason.Length > 0 ? Explain(reason) : "选择遥施原点格，或点自身恢复默认";
                return;
            }
            if (!cell.HasValue || selection == SelectionMode.Edges &&
                (path.Count == 0 || path[path.Count - 1] == cell.Value)) return;
            if (selection == SelectionMode.EarthTarget)
            {
                foreach (var candidate in GravityQuotes(cell.Value))
                {
                    // Keep a rejection reason for an occupied but unreachable cell.
                    hoverCommand = candidate.command;
                    hoverQuote = candidate.quote;
                    if (hoverQuote.IsLegal) break;
                }
                return;
            }
            if (selection == SelectionMode.Support)
            {
                hoverCommand = SupportCommand(cell.Value);
                hoverQuote = encounter.PreviewJindan(hoverCommand);
                return;
            }
            hoverCommand = selection == SelectionMode.BreakTarget
                ? new CombatCommand(CombatCommandKind.Art, encounter.ControlledActorId,
                    profileId: catalog.demolition.profileId, targetCell: cell)
                : BuildCommand(CombatCommandKind.JindanAbility, pointedCell: cell);
            if (hoverCommand != null) hoverQuote = encounter.PreviewJindan(hoverCommand);
        }

        public void PickCell(int q, int r)
        {
            Refresh();
            if (!IsSelecting || !encounter.AcceptsPlayerCommand) return;
            var cell = new HexCoord(q, r);
            if (selection == SelectionMode.Edges && path.Count == 0)
            {
                path.Add(cell);
                ClearHover();
                message = "首格 " + CellText(cell) + " · 选择相邻第二格";
                return;
            }
            if (selection == SelectionMode.Origin && path.Count < 2)
            {
                string reason = CheckOrigin(cell);
                if (reason.Length > 0) { message = Explain(reason); return; }
                remoteOrigin = OriginAt(cell);
                edgesWhenOriginPicked = path.Count;
                selection = SelectionMode.Edges;
                ClearHover();
                message = "已选原点，请选择相连格";
                return;
            }
            HoverCell(q, r);
            if (hoverQuote?.IsLegal != true) return;
            if (selection == SelectionMode.Edges) path.Add(cell);
            if (selection == SelectionMode.EarthTarget)
            {
                earthCell = cell;
                earthTarget = hoverCommand.Jindan.EarthTargetId;
            }
            if (selection == SelectionMode.TideSource) tideSource = cell;
            if (selection == SelectionMode.Origin)
            {
                remoteOrigin = OriginAt(cell);
                edgesWhenOriginPicked = path.Count;
            }
            frozenCommand = hoverCommand;
            frozenQuote = hoverQuote;
            ClearHover();
            if (selection != SelectionMode.Edges || earthPage || path.Count == 3) selection = SelectionMode.None;
            message = "已锁定 " + CellText(cell) + " · 点击确认施行";
            Refresh();
        }

        private HexCoord? OriginAt(HexCoord cell)
        {
            encounter.CurrentSession.Combatants.TryGet(encounter.ControlledActorId, out var actor);
            return actor.Pose.Contains(cell) ? (HexCoord?)null : cell;
        }

        private string CheckOrigin(HexCoord cell)
        {
            var session = encounter.CurrentSession;
            session.Combatants.TryGet(encounter.ControlledActorId, out var actor);
            return actor.Pose.Contains(cell) ? string.Empty :
                CombatScaleRules.CheckRemoteOrigin(session, actor, cell, session.Jindan.Ability("SPACE_SCALE"));
        }

        private IEnumerable<(CombatCommand command, CombatCommandPreview quote)> GravityQuotes(HexCoord cell)
        {
            var session = encounter.CurrentSession;
            var units = session.Combatants.All.Where(unit => unit.IsAlive && unit.OccupiedCells.Contains(cell) &&
                session.Visibility?.IsVisible(encounter.ControlledActorId, unit.Id) != false).Select(unit => unit.Id);
            var objects = session.SceneObjects.All.Where(obj => !obj.IsDestroyed && obj.Contains(cell) &&
                obj.Definition.MaterialType is "stone" or "earth").Select(obj => obj.Id);
            foreach (string id in units.Concat(objects))
            {
                var command = BuildCommand(CombatCommandKind.JindanAbility, pointedCell: cell, gravityTarget: id);
                if (command != null) yield return (command, encounter.PreviewJindan(command));
            }
        }

        private void LockNextEarthTarget()
        {
            var choices = GravityQuotes(earthCell.Value).Where(candidate => candidate.quote.IsLegal).ToArray();
            if (choices.Length == 0) return;
            int index = Array.FindIndex(choices, candidate => candidate.command.Jindan.EarthTargetId == earthTarget);
            (frozenCommand, frozenQuote) = choices[(index + 1) % choices.Length];
            earthTarget = frozenCommand.Jindan.EarthTargetId;
            message = "已锁定 " + Short(earthTarget) + " " + CellText(earthCell.Value);
        }

        public bool Refresh()
        {
            var session = encounter.CurrentSession;
            string actorId = session?.Jindan == null ? null : encounter.ControlledActorId;
            bool invalidated = false;
            if (session != observedSession || actorId != observedActor)
            {
                bool changedActor = observedActor != null && actorId != observedActor;
                invalidated = changedActor && (IsSelecting || frozenQuote != null);
                ClearTargeting();
                if (changedActor) message = "战况已变，请重新选取（行动角色已改变）";
                observedSession = session; observedActor = actorId;
            }
            else if (frozenQuote != null &&
                (frozenQuote.StateVersion != session.StateVersion || frozenQuote.TurnSequence != session.TurnSequence))
            {
                ClearTargeting();
                message = "战况已变，请重新选取";
                invalidated = true;
            }
            else if (hoverQuote != null && hoverQuote.StateVersion != session.StateVersion)
                ClearHover();
            if (PreparationOpen) PreparationDetails = resultText ?? PreparationText();
            CombatDetails = JindanVisibilityPresentation.Details(session) + CombatText(session, actorId);
            QuoteDetails = QuoteText();
            return invalidated;
        }

        private void Preview(CombatCommandKind kind, bool fire = false)
        {
            var command = BuildCommand(kind, fire);
            if (kind != CombatCommandKind.JindanAbility || fire) ClearTargeting();
            else ClearQuote();
            if (command == null)
            {
                message = kind == CombatCommandKind.SwapJindan ? "当前角色没有可切换的同席候选。" :
                    "当前角色没有本页装配。";
                return;
            }
            frozenCommand = command;
            frozenQuote = encounter.PreviewJindan(command);
            if (frozenQuote?.IsLegal == true) selection = SelectionMode.None;
            message = frozenQuote?.IsLegal == true ? "报价已锁定，请核对后确认。" : Explain(frozenQuote?.RejectionReason);
        }

        private CombatCommand BuildCommand(CombatCommandKind kind, bool fire = false, HexCoord? pointedCell = null,
            string gravityTarget = null)
        {
            var session = encounter.CurrentSession;
            string actorId = encounter.ControlledActorId;
            var owned = session.Jindan.Seats.Where(value => value.Definition.ActorId == actorId);
            var seat = kind == CombatCommandKind.SwapJindan
                ? owned.FirstOrDefault(value => value.Definition.Candidates.Count > 1)
                : owned.FirstOrDefault(value => fire ? value.EffectId == CombatFireDomainRules.Effect : tidePage ? value.EffectId == "WATER_TIDE" : earthPage ? value.EffectId is "EARTH_MANIFEST" or "EARTH_GRAVITY" : value.EffectId == "SPACE_SCALE");
            if (seat == null) return null;
            string effect = kind == CombatCommandKind.SwapJindan
                ? seat.Definition.Candidates.First(value => value != seat.EffectId) : seat.EffectId;
            string seatId = seat.Definition.SeatId;
            CombatJindanRequest request;
            if (kind != CombatCommandKind.JindanAbility || fire)
                request = new CombatJindanRequest(seatId);
            else if (tidePage)
                request = new CombatJindanRequest(seatId, tideAction: tideAction, tideSource: pointedCell ?? tideSource,
                    charged: tideCharged, createTideEye: tideEye);
            else
            {
                IEnumerable<HexCoord> edges = path;
                if (selection == SelectionMode.Edges && pointedCell.HasValue) edges = path.Append(pointedCell.Value);
                var origin = selection == SelectionMode.Origin && pointedCell.HasValue ? OriginAt(pointedCell.Value) : remoteOrigin;
                request = new CombatJindanRequest(seatId, edges, metric, origin, earthPage ? earthMode : null,
                    gravityTarget ?? earthTarget, gravityTarget != null ? pointedCell : earthCell);
            }
            return new CombatCommand(kind, actorId, profileId: effect, jindan: request);
        }

        public bool Confirm()
        {
            if (!CanConfirm) return false;
            // Confirmation deliberately sends the original quote, including its original state version.
            var result = encounter.ConfirmJindan(frozenCommand, frozenQuote);
            if (result.Succeeded)
            {
                message = "已执行：AP −" + Ap(result.ActionPointSpentUnits) + "，MP −" + result.SpiritSpent + "。";
                ClearTargeting();
            }
            else if (result.RejectionReason == "combat_quote_stale")
            {
                ClearTargeting();
                message = "战况已变，请重新选取";
            }
            else { message = Explain(result.RejectionReason); ClearQuote(); }
            return result.Succeeded;
        }

        private string SupportLabel => supportSeat?.EffectId switch
        {
            "YANG_RADIANCE" => "日坛", "YIN_CONCEAL" => "月坛", "SPACE_SEAL" => "门枢", _ => "阵心",
        };

        private CombatCommand SupportCommand(HexCoord cell) =>
            new CombatCommand(CombatCommandKind.JindanAbility, encounter.ControlledActorId,
                profileId: supportSeat.EffectId, jindan: new CombatJindanRequest(supportSeat.Definition.SeatId, selectedTargetCell: cell));

        private void BeginSupportSelection(string effect)
        {
            ClearTargeting();
            var session = encounter.CurrentSession;
            supportSeat = session.Jindan.Seats.FirstOrDefault(seat =>
                seat.Definition.ActorId == encounter.ControlledActorId && seat.EffectId == effect);
            if (supportSeat == null) { message = "当前角色没有这项能力。"; return; }
            selection = SelectionMode.Support;
            if (FixedSupport is { } center)
            {
                string label = SupportLabel + " " + CellText(center);
                var quote = encounter.PreviewJindan(SupportCommand(center));
                message = quote.IsLegal ? "点棋盘上标记的" + label + "，锁定后确认" :
                    quote.RejectionReason == "above_max_range" || quote.RejectionReason == "seal_owner_outside_domain"
                        ? "点「移动」靠近标记的" + label + "，再展开" + Effect(effect)
                        : label + " · " + Explain(quote.RejectionReason);
            }
            else
            {
                supportChoices = session.Surfaces.Cells.Where(cell => encounter.PreviewJindan(SupportCommand(cell.Cell)).IsLegal)
                    .Select(cell => cell.Cell).ToArray();
                message = supportChoices.Count > 0
                    ? "点蓝框地格安置阵心；青色轮廓是下次受热的地格，干地可燃、水地先退水。"
                    : "当前无法安置阵心，指向地格查看原因。";
            }
        }

        private void PreviewVisibility(CombatCommandKind kind, bool seal = false)
        {
            ClearTargeting();
            var seat = encounter.CurrentSession.Jindan.Seats.FirstOrDefault(value =>
                value.Definition.ActorId == encounter.ControlledActorId && (seal ? value.EffectId == "SPACE_SEAL" : CombatVisibility.IsEffect(value.EffectId)));
            if (seat == null) { message = "当前角色没有本页装配。"; return; }
            frozenCommand = new CombatCommand(kind, encounter.ControlledActorId, profileId: seat.EffectId,
                jindan: new CombatJindanRequest(seat.Definition.SeatId));
            frozenQuote = encounter.PreviewJindan(frozenCommand);
            message = frozenQuote?.IsLegal == true ? "支点和费用已复核，请确认。" : Explain(frozenQuote?.RejectionReason);
        }

        private void PreviewFlight(CombatFlightAction action)
        {
            ClearTargeting();
            frozenCommand = new CombatCommand(CombatCommandKind.Flight, encounter.ControlledActorId,
                profileId: CombatFlight.ProfileId, flightAction: action);
            frozenQuote = encounter.PreviewJindan(frozenCommand);
            message = frozenQuote?.IsLegal == true ? "报价已冻结，请核对后确认。" : Explain(frozenQuote?.RejectionReason);
        }

        public bool CancelLayer()
        {
            Refresh();
            bool hadQuote = frozenQuote != null;
            if (hadQuote && supportSeat != null)
            {
                BeginSupportSelection(supportSeat.EffectId);
                return true;
            }
            bool wasPicking = IsSelecting;
            ClearQuote();
            if (selection == SelectionMode.Origin || remoteOrigin.HasValue && path.Count == edgesWhenOriginPicked)
            {
                if (selection != SelectionMode.Origin) remoteOrigin = null;
                ResumeEdgeSelection();
                message = "已返回选边";
                return true;
            }
            if (path.Count > 0)
            {
                path.RemoveAt(path.Count - 1);
                ResumeEdgeSelection();
                message = "已撤回末格";
                return true;
            }
            if (earthCell.HasValue || tideSource.HasValue)
            {
                selection = earthCell.HasValue ? SelectionMode.EarthTarget : SelectionMode.TideSource;
                earthCell = null; earthTarget = null; tideSource = null;
                message = "已解除锁定，请重新选取";
                return true;
            }
            if (remoteOrigin.HasValue)
            {
                remoteOrigin = null; selection = SelectionMode.Origin;
                message = "已撤回遥施原点";
                return true;
            }
            ClearTargeting();
            message = "已取消，未扣费。";
            return hadQuote || wasPicking;
        }

        private void ResumeEdgeSelection()
        {
            if (path.Count >= 2) Preview(CombatCommandKind.JindanAbility);
            selection = path.Count == (earthPage ? 2 : 3) ? SelectionMode.None : SelectionMode.Edges;
        }

        private void ClearHover() { hoveredCell = null; hoverCommand = null; hoverQuote = null; }
        private void ClearQuote() { frozenCommand = null; frozenQuote = null; ClearHover(); }
        private void ClearTargeting()
        { ClearQuote(); path.Clear(); remoteOrigin = null; earthCell=null; earthTarget=null; tideSource = null; supportSeat = null; supportChoices = Array.Empty<HexCoord>(); selection = SelectionMode.None; }
        private void SelectSegmentPreset()
        {
            if (IsPlayerChallenge) return;
            if (segmentIndex != 1) ControlFire = false;
            string id = catalog.segments[segmentIndex].actors.First(value => value.team == (ControlFire ? 1 : 0)).characterId;
            presetIndex = Array.FindIndex(catalog.presets, value => value.characterId == id);
        }

        private string PreparationText()
        {
            var preset = IsPlayerChallenge ? catalog.GetPreset(trialPresetId) : catalog.presets[presetIndex];
            var result = new StringBuilder();
            if (IsPlayerChallenge)
            {
                var enemies = JindanRehearsalFactory.PlayerPlacements(segmentIndex).Where(actor => actor.team == 1);
                result.AppendLine("出战角色  " + trialPlayer.Identity.DisplayName + " · " + trialPlayer.Progression.RealmStage +
                    (preset == null ? "" : " · " + Local(preset.displayNameKey)));
                string terrain = segmentIndex switch
                {
                    -1 => "浅水滩", 0 => "高台坡阶", 1 => "水岸火阵", _ => "坛前关隘"
                };
                result.AppendLine("遭遇  " + StageName + " · " + terrain);
                result.AppendLine("同伴  独自行动");
                result.AppendLine("对手  " + string.Join("、", enemies.Select(actor => Local(actor.characterId))));
                result.AppendLine();
                result.AppendLine("获胜  " + JindanChallengeProgress.VictoryCondition);
                result.AppendLine("失败  " + JindanChallengeProgress.DefeatCondition);
                if (segmentIndex < 0) result.AppendLine("基础演练可跳过，直接选择其他遭遇。");
                result.AppendLine("提示  " + JindanChallengeProgress.FirstAction(segmentIndex));
                if (message.Length > 0) result.AppendLine(message);
                if (!showDetails) return result.ToString();
                result.Clear();
                result.AppendLine(trialPlayer.Identity.DisplayName + " · " + trialPlayer.Progression.RealmStage);
                result.AppendLine("生命 " + trialPlayer.Resources.CurrentHealth + "/" + trialPlayer.Resources.MaximumHealth +
                    " · MP " + trialPlayer.Resources.CurrentSpirit + "/" + trialPlayer.Resources.MaximumSpirit);
                result.AppendLine("主手  " + (string.IsNullOrEmpty(trialPlayer.MainEquipmentItemId) ? "徒手" : Local(trialPlayer.MainEquipmentItemId)));
                var arts = trialPlayer.AbilityLoadout.EquippedSpells.Concat(trialPlayer.AbilityLoadout.EquippedSkills);
                result.AppendLine("装配  " + (arts.Any() ? string.Join("、", arts.Select(Local)) : "无"));
                if (preset == null) return result.ToString();
                result.AppendLine(Local(preset.sourceGongFaId));
            }
            else
            {
                var segment = catalog.segments[segmentIndex];
                result.AppendLine("当前选段：" + Local(segment.displayNameKey));
                if (segmentIndex == 1) result.AppendLine(ControlFire ? "执火守阵：操控火修，可展开、维持和关闭长明。" : "破火阵：操控土／水修，可绕行、灭火或破中心。");
                result.AppendLine("阵容：" + string.Join("、", segment.actors.Select(value =>
                    (value.team == (ControlFire ? 1 : 0) ? "我方·" : "对手·") + Local(catalog.GetPreset(value.characterId).displayNameKey))));
                result.AppendLine("土系战前装配：" + Effect(earthEffect) + "；浏览预设 " + (presetIndex + 1) + "/" + catalog.presets.Length);
            }
            result.AppendLine(Local(preset.displayNameKey) + " · " + preset.realmSubstage + " · " + Local(preset.danxiangNameKey));
            result.AppendLine("AP " + Ap(preset.actionPointMaximumUnits) + " · MP " + preset.manaMaximum +
                " · 视野 " + preset.normalVisionRange + " · 完整紫府容量 " + preset.naturalMansionCapacity);
            if (!IsPlayerChallenge) result.AppendLine("身份：" + preset.characterId);
            foreach (string id in preset.seatInstanceIds)
            {
                var seat = catalog.GetSeat(id);
                string effect = seat.candidateEffectIds.Contains(earthEffect) ? earthEffect : seat.defaultEffectId;
                var ability = catalog.GetAbility(seat.guardianDefinitionId);
                if (IsPlayerChallenge)
                {
                    result.AppendLine("金丹" + Position(seat.positionType.ToString()) + "位 · " + PositionName(seat.positionId));
                    result.AppendLine("镇府神通  " + Local(ability.displayNameKey) + "　当前装配  " + Effect(effect));
                    if (seat.candidateEffectIds.Length > 1)
                        result.AppendLine("可换装配  " + string.Join(" / ", seat.candidateEffectIds.Where(candidate => candidate != effect).Select(Effect)));
                }
                else
                {
                    result.AppendLine("席位 " + id + " · " + Position(seat.positionType.ToString()) + " · " + seat.positionId);
                    result.AppendLine("来源：" + Local(ability.displayNameKey) + " · " + seat.guardianAbilityInstanceId);
                    result.AppendLine("候选：" + string.Join(" / ", seat.candidateEffectIds.Select(Effect)));
                }
                var command = DeployDefinition(effect);
                result.Append("施放  " + Ap(command.actionPointCostUnits) + " AP / " + command.manaCost +
                    " MP · 冷却 " + command.cooldownTicks + " 刻");
                if (command.upkeepMana > 0)
                    result.Append(" · " + (command.upkeepClock == "OWN_TURN_END" ? "后续每个本人回合结束" : "每刻") +
                        "维持消耗 " + command.upkeepMana + " MP");
                result.AppendLine(command.enabled ? "" : " · " + Explain(command.unavailableReasonKey));
            }
            if (!string.IsNullOrEmpty(preset.flightAbilityInstanceId) && preset.flightAbilityInstanceId != "none")
                result.AppendLine((IsPlayerChallenge ? "御风飞行  " : "独立飞行：" + preset.flightAbilityInstanceId + "；") +
                    "起飞 250 AP / 200 MP，后续本人回合开始维持消耗 100 MP，最多 3 回合。");
            result.AppendLine("徒手可在地面或空中出手，也可对空，高度相差最多一档。");
            if (preset.seatInstanceIds.Select(catalog.GetSeat).Any(seat => seat.candidateEffectIds.Length > 1))
                result.AppendLine("战中换装需耗用一个完整的本人行动回合。新装配从下个本人回合起可用。");
            if (PreparationOpen && message.Length > 0) result.Append(message);
            return result.ToString();
        }

        private string CombatText(CombatSession session, string actorId)
        {
            if (session?.Jindan == null || !session.Combatants.TryGet(actorId, out var actor)) return string.Empty;
            var preset = catalog.GetPreset(actorId);
            var result = new StringBuilder();
            result.AppendLine((actorId == "player" ? trialPlayer.Identity.DisplayName : preset == null ? encounter.ActorName(actorId) : Local(preset.displayNameKey)) + "  t=" + session.TurnScheduler.CurrentTick +
                (encounter.AcceptsPlayerCommand ? " · 你的行动" : " · 等待行动"));
            result.AppendLine("AP " + Ap(actor.ActionPointsRemainingUnits) + "/" + Ap(actor.ActionPointLimitAtTurnStartUnits) +
                "  MP " + actor.CurrentSpirit + "/" + actor.MaximumSpirit);
            result.Append(JindanSealPresentation.Details(session));
            result.Append(JindanFireDomainPresentation.Details(session));
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
            var quote = DisplayQuote;
            var command = DisplayCommand;
            if (quote == null) return InteractionHint;
            string action = command.Kind == CombatCommandKind.Flight ? FlightName(command.FlightAction.Value) :
                command.Kind == CombatCommandKind.SwapJindan ? "切装→" + Effect(command.ProfileId) :
                command.Kind == CombatCommandKind.CloseJindan ? "关闭"+Effect(command.ProfileId) :
                command.Kind == CombatCommandKind.Art ? "演武·破垒" : command.ProfileId == CombatFireDomainRules.Effect ? "展开长明火界" : command.Jindan?.TideAction is {} water
                    ? JindanTidePresentation.Name(water, command.Jindan.Charged, command.Jindan.CreateTideEye)
                    : command.Jindan?.EarthMode is {} mode ? EarthName(mode) : "展开" + Effect(command.ProfileId);
            string result = action + "  " + Ap(quote.ActionPointCostUnits) + "AP / " + quote.SpiritCost +
                "MP / CT " + quote.CtPenalty + " / CD " + quote.ReuseCooldownTicks + "tick";
            if (command.Kind == CombatCommandKind.SwapJindan) result += "\n旧持续效果结束；新装配下一本人回合生效。";
            else if(command.Kind==CombatCommandKind.Art) result+="\n环境命中目标："+string.Join("、",quote.AttackArea?.EnvironmentTargetIds??Array.Empty<string>());
            else if (command.Kind == CombatCommandKind.CloseJindan && command.ProfileId != CombatFireDomainRules.Effect) result += "\n移除本来源，按尚存规则重算；不返还资源或重置CD。";
            if (quote.Tide != null) result += "\n" + JindanTidePresentation.Quote(quote.Tide, command.Jindan);
            if (command.ProfileId == CombatFireDomainRules.Effect)
                result += "\n" + (command.Kind == CombatCommandKind.CloseJindan
                    ? "仅结束主规则；合法本源燃烧转为已付余烬，最多再2个本人结束周期；不能重复关闭。"
                    : "整棋盘地表弱火热；施放当回合不跳动。预留6燃料，后续每次本人结束维护200MP，最多3次。") +
                    "\n" + JindanFireDomainPresentation.Candidates(encounter.CurrentSession, command.Jindan?.SelectedTargetCell);
            if (command.Kind == CombatCommandKind.JindanAbility && command.Jindan?.EarthMode is {} earth)
            {
                var session = encounter.CurrentSession;
                var seat = session.Jindan.Find(command.Jindan.SeatId);
                var active = seat?.ActiveEarth;
                if (earth is CombatEarthMode.Wall or CombatEarthMode.Ramp)
                    result += "\n接地受养：是；耐久750；容量" + (active?.Objects.Count ?? 0) + "→" + ((active?.Objects.Count ?? 0)+1) +
                        "/2；剩余施法者结束" + (active?.RemainingOwnTurnEnds ?? 3) + "（追加不刷新）。";
                else
                {
                    session.Combatants.TryGet(command.ActorId, out var caster);
                    session.Combatants.TryGet(command.Jindan.EarthTargetId ?? "", out var target);
                    bool nourished = caster.MovementMode == SpatialMovementMode.Ground || target == null || target.MovementMode == SpatialMovementMode.Ground;
                    result += "\n接地受养：" + (nourished ? "是" : "否") + "；有效档差" + (nourished ? 3 : 2) +
                        "；压力" + (target == null ? 0 : CombatForceMovement.Gravity(target)?.Pressure ?? 0) + "/2；持续2次" + (target == null ? "施法者" : "目标") + "本人结束。";
                }
            }
            return result + "\n" + InteractionHint;
        }

        private JindanDemoCommandDefinition DeployDefinition(string effect) =>
            catalog.commands.First(value => value.effectId == effect && value.kind == "DEPLOY");
        private string Local(string key) => string.IsNullOrEmpty(key) ? string.Empty : text(key);
        private static string Short(string id) => (id ?? string.Empty).Replace("demo_20261024_", string.Empty);
        private static string Ap(long units) => (units / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
        private static string CellText(HexCoord value) => "(" + value.Q + "," + value.R + ")";
        private static string PathText(IEnumerable<HexCoord> values)
        { var items = values.Select(CellText).ToArray(); return items.Length == 0 ? "未选边" : string.Join("→", items); }
        internal static string Metric(int units) => units == 1 ? "压缩0.5" : "扩张2";
        private static string EarthName(CombatEarthMode mode)=>mode switch { CombatEarthMode.Wall=>"岩墙",CombatEarthMode.Ramp=>"窄土坡",CombatEarthMode.Heavy=>"极重",CombatEarthMode.Light=>"失重",_=>"镇岳" };
        private static string PositionName(string id) => id switch
        {
            "JD_FiveElements_Earth_SOURCE" => "坤地髓",
            "JD_FiveElements_Water_SOURCE" => "渊蓄池",
            "JD_FiveElements_Fire_DOMAIN01" => "不熄炉",
            "JD_Cosmos_Space_SOURCE" => "六合枢",
            "JD_YinYang_Yang_DOMAIN01" => "昭明殿",
            "JD_Cosmos_Space_TRANSFORM01" => "叠嶂关",
            "JD_YinYang_Yin_DOMAIN01" => "藏真域",
            _ => string.Empty,
        };
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
            if (reason?.StartsWith("fire_", StringComparison.Ordinal) == true) return JindanFireDomainPresentation.Explain(reason);
            if (reason?.StartsWith("tide_", StringComparison.Ordinal) == true) return JindanTidePresentation.Explain(reason);
            string localized = Local(reason);
            if (!string.IsNullOrEmpty(localized) && localized != reason) return localized;
            return reason switch
            {
                "visibility_support_required" => "请点击标记的坛位。",
                "seal_support_required" => "请点击标记的门枢。",
                "seal_owner_outside_domain" => "请移动到门枢附近。",
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
