using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Features.Adventure
{
    /// <summary>Tracks only paid commands, actual traversal and resolved effects in one player rehearsal.</summary>
    public sealed class JindanChallengeProgress
    {
        public static readonly HexCoord HighGoal = new HexCoord(8, 12);
        private static readonly HexCoord BasicGoal = new HexCoord(4, 1);
        private static readonly HexCoord FirstFireGoal = new HexCoord(5, 6);
        public HexCoord? MovementTarget(CombatSession session)
        {
            if (Outcome != CombatSessionOutcome.Ongoing) return null;
            if (SegmentIndex == -1 && !reached) return BasicGoal;
            if (SegmentIndex != 1 || FireDefender || session?.Surfaces == null) return null;
            return extinguished.Where(cell => session.Surfaces.Get(cell).Kind != CombatSurfaceKind.Burning)
                .OrderByDescending(cell => cell == FirstFireGoal).ThenBy(cell => cell.Q).ThenBy(cell => cell.R)
                .Select(cell => (HexCoord?)cell).FirstOrDefault();
        }
        private const string FirePreset = "demo_20261024_fire";
        private const string YinPreset = "demo_20261024_seal_yin";
        private readonly HashSet<HexCoord> extinguished = new HashSet<HexCoord>();
        private readonly HashSet<HexCoord> emberCells = new HashSet<HexCoord>();
        private readonly HashSet<HexCoord> previousFireCells = new HashSet<HexCoord>();
        private readonly HashSet<long> scaledObservations = new HashSet<long>();
        private readonly HashSet<string> detouring = new HashSet<string>(StringComparer.Ordinal);
        private long lastSurfaceReceipt;
        private long lastObservation;
        private string sealSupportId;
        private bool reached, cast, airHit, pressure, flewUp, fireClosed, movedWithEmbers;
        private bool paidFireBurned, fireResisted;
        private bool gateBroken, crossedBroken, bypassed, sealDelayed, sealClosed, nearBefore, retreated, crossedOpen;

        public JindanChallengeProgress(int segmentIndex, string presetId)
        {
            if (segmentIndex < -1 || segmentIndex > 2) throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            SegmentIndex = segmentIndex;
            PresetId = presetId;
            ProgressText = FirstAction(segmentIndex, presetId);
        }

        public int SegmentIndex { get; }
        public string PresetId { get; }
        public int Seed => 20261119 + SegmentIndex + 1;
        public int ConfigVersion => 1;
        public CombatSessionOutcome Outcome { get; private set; }
        public string ProgressText { get; private set; }
        public string ResultText { get; private set; } = string.Empty;
        internal bool FireDefender => SegmentIndex == 1 && PresetId == FirePreset;
        internal bool YinDefender => SegmentIndex == 2 && PresetId == YinPreset;

        public static string DescribeGoal(int segmentIndex, string presetId) => segmentIndex switch
        {
            -1 => "本人到达 (4,1)，施放飞星指，主动结束行动。",
            0 => "登上高台 (8,12)，命中飞行对手或用极重将其压低。",
            1 when presetId == FirePreset => "守住阵心，使火域阻滞通路，再从陆路撤到 (8,12)。",
            1 => "步行穿过任一被潮水浇灭的火格，或破坏阵心、处理余火后步行通过。",
            2 when presetId == YinPreset => "封关延缓对手，利用藏真从近距观察中撤出。",
            2 => "绕过封边近距看见隐者，或破锚过门、用定尺与阳照完成观察。",
            _ => throw new ArgumentOutOfRangeException(nameof(segmentIndex))
        };

        public static string FirstAction(int segmentIndex, string presetId) => segmentIndex switch
        {
            -1 => "点移动，走到浅水格 (4,1)。",
            0 => "沿坡阶向高台 (8,12) 移动，或让持有御风的角色起飞。",
            1 when presetId == FirePreset => "守阵 · 接近阵心 (7,12)，展开长明。",
            1 => "用蓄澜涨潮浇灭 " + Cell(FirstFireGoal) + " 的火，或让厚载飞至阵心 (7,12) 附近。",
            2 when presetId == YinPreset => "在门枢 (9,12) 附近展开封关，并在月坛旁展开藏真。",
            2 => "先到 (8,12)，再绕过封边到 (9,12)，或用贯石击破锚。",
            _ => throw new ArgumentOutOfRangeException(nameof(segmentIndex))
        };

        public static string DescribeFailure(int segmentIndex, string presetId) =>
            segmentIndex == 1 && presetId == FirePreset ? "本人倒下、阵心被毁，或未达成守阵目标就击败全部对手。" :
            "本人倒下，或未达成目标就击败全部对手。";

        internal void Observe(CombatSession session, CombatCommand command = null, CombatActionResult result = null,
            int previousTargetAltitude = 0)
        {
            if (Outcome != CombatSessionOutcome.Ongoing || !session.Combatants.TryGet("player", out var player)) return;
            if (!player.IsAlive) { Finish(false, "本人已倒下。"); return; }
            CombatantSnapshot actor = null;
            if (command != null) session.Combatants.TryGet(command.ActorId, out actor);
            switch (SegmentIndex)
            {
                case -1:
                    if (actor == player)
                    {
                        reached |= command.Kind == CombatCommandKind.Move && player.Position == BasicGoal;
                        cast |= reached && command.Kind == CombatCommandKind.Art && command.ProfileId == "spell_feixingzhi";
                        if (cast && command.Kind == CombatCommandKind.EndTurn) Finish(true, "到达浅水格，飞星已施放，并已主动结束行动。");
                        else if (reached) ProgressText = cast ? "飞星已施放。点结束行动。" : "到达浅水格。施放飞星指，可以向空格出手。";
                    }
                    break;
                case 0: ObserveHeight(session, actor, command, result, previousTargetAltitude); break;
                case 1: ObserveFire(session, player, actor, command, result); break;
                case 2: ObserveGate(session, player, actor, command, result); break;
            }
            if (Outcome == CombatSessionOutcome.Ongoing && !session.Combatants.HasLivingMembers(CombatTeam.Enemy))
                Finish(false, "对手已全部倒下，演武目标尚未完成。");
        }

        private void ObserveHeight(CombatSession session, CombatantSnapshot actor, CombatCommand command,
            CombatActionResult result, int previousTargetAltitude)
        {
            if (actor?.Team != CombatTeam.Player) return;
            if (result.MovementSteps.Count > 0 && actor.Position == HighGoal) reached = true;
            foreach (var step in result.MovementSteps)
                if (step.To.MovementMode == SpatialMovementMode.Fly &&
                    session.SceneObjects.Board.TryGetCell(step.From.Anchor, out var from) &&
                    session.SceneObjects.Board.TryGetCell(step.To.Anchor, out var to) && to.HeightLevel > from.HeightLevel)
                    flewUp = true;
            airHit |= result.Damage.Any(hit => hit.IsHit && hit.FinalDamage > 0 &&
                session.Combatants.TryGet(hit.TargetId, out var target) && target.Team == CombatTeam.Enemy && target.AltitudeBand > 0);
            if (command.Jindan?.EarthMode == CombatEarthMode.Heavy &&
                session.Combatants.TryGet(command.Jindan.EarthTargetId, out var lowered) && lowered.Team == CombatTeam.Enemy)
                pressure |= previousTargetAltitude > lowered.AltitudeBand;
            if (reached && (airHit || pressure))
                Finish(true, pressure ? "高台已到达，极重实际压低了飞行对手。" :
                    flewUp ? "御风跨高接近，在高台完成对空命中。" : "沿地面登上高台，完成对空命中。");
            else if (reached) ProgressText = "已登上高台。命中飞行对手，或用极重将其压低。";
            else if (airHit || pressure) ProgressText = "已应对飞行对手。前往高台 (8,12)。";
        }

        private void ObserveFire(CombatSession session, CombatantSnapshot player, CombatantSnapshot actor,
            CombatCommand command, CombatActionResult result)
        {
            var fire = session.Jindan.Seats.First(s => s.EffectId == CombatFireDomainRules.Effect);
            var domain = fire.FireDomain;
            if (FireDefender && domain.Support.IsDestroyed) { Finish(false, "守护的长明阵心已被破坏。"); return; }
            foreach (var tail in domain.Tails) emberCells.UnionWith(tail.Cells);
            if (FireDefender)
            {
                if (actor?.Team == CombatTeam.Enemy && result.SceneObjectChanges.Any(change =>
                    change.Id == domain.Support.Id && change.Damage > 0)) fireResisted = true;
                foreach (var receipt in session.Surfaces.Settlements)
                {
                    if (receipt.Sequence <= lastSurfaceReceipt) continue;
                    lastSurfaceReceipt = receipt.Sequence;
                    if (receipt.SourceRef == fire.Definition.AbilityInstanceId)
                    {
                        paidFireBurned |= receipt.Changes.Any(change => change.After == CombatSurfaceKind.Burning);
                        fireResisted |= receipt.Damage.Any(hit => hit.FinalDamage > 0 &&
                            session.Combatants.TryGet(hit.TargetId, out var burned) && burned.Team == CombatTeam.Enemy);
                    }
                    if (receipt.TideAction.HasValue && receipt.Changes.Any(change => previousFireCells.Contains(change.Cell) &&
                        change.Before == CombatSurfaceKind.Burning && change.After == CombatSurfaceKind.Wet))
                        fireResisted = true;
                }
                previousFireCells.Clear();
                if (domain.Main != null) previousFireCells.UnionWith(domain.Main.Cells);
                foreach (var tail in domain.Tails) previousFireCells.UnionWith(tail.Cells);
                if (actor == player && command.Kind == CombatCommandKind.CloseJindan && command.ProfileId == fire.EffectId)
                    fireClosed |= domain.Tails.Count > 0;
                if (actor == player && result.MovementSteps.Count > 0 && player.MovementMode == SpatialMovementMode.Ground)
                {
                    movedWithEmbers |= fireClosed && domain.Tails.Count > 0;
                    reached |= player.Position == HighGoal && fire.MaintenancePayments > 0 && paidFireBurned && fireResisted;
                }
                if (fire.MaintenancePayments == 0)
                {
                    if (domain.Main != null) ProgressText = "守阵 · 护住阵心，结束行动以维持长明火域。";
                    return;
                }
                if (paidFireBurned && fireResisted && reached && player.Position == HighGoal && domain.Main != null &&
                    (!fireClosed || movedWithEmbers && fire.SuccessfulDeployments > 1))
                    Finish(true, fireClosed ? "收势留下余火，借余火转移后重新展开，已安全撤至高台。" : "长明维护生效，阵心仍在，已沿陆路撤至高台。");
                else if (!fireResisted) ProgressText = "护住阵心。让火格牵制来敌，或抵住对手破阵后再向 (8,12) 转移。";
                else ProgressText = fireClosed ? "借余火步行撤到 (8,12)。有可燃格且长明冷却结束后，重新展开。" : "火域已维护。护住阵心，沿陆路撤到 (8,12)。";
                return;
            }
            var water = session.Jindan.Seats.First(s => s.EffectId == "WATER_TIDE");
            foreach (var receipt in session.Surfaces.Settlements)
            {
                if (receipt.Sequence <= lastSurfaceReceipt) continue;
                lastSurfaceReceipt = receipt.Sequence;
                if (!receipt.TideAction.HasValue || receipt.SourceRef != water.Definition.AbilityInstanceId) continue;
                foreach (var change in receipt.Changes)
                    if (change.Before == CombatSurfaceKind.Burning && change.After == CombatSurfaceKind.Wet) extinguished.Add(change.Cell);
            }
            bool clearedTail = domain.Support.IsDestroyed && domain.Tails.Count == 0 && emberCells.Count != 0;
            if (actor?.Team == CombatTeam.Player && actor.MovementMode == SpatialMovementMode.Ground)
            {
                foreach (var step in result.MovementSteps)
                {
                    if (step.From.Anchor == step.To.Anchor || session.Surfaces.Get(step.To.Anchor).Kind == CombatSurfaceKind.Burning) continue;
                    if (clearedTail && emberCells.Contains(step.To.Anchor))
                    { Finish(true, "破坏阵心、处理余火后，已从地面穿过原余火格。"); return; }
                    if (extinguished.Contains(step.To.Anchor))
                    { Finish(true, "潮水将燃烧格转湿，已沿地面通过这条路线。"); return; }
                }
            }
            if (domain.Support.IsDestroyed && emberCells.Count > 0)
                ProgressText = clearedTail ? "余火已尽。步行通过原余火格 " + Cell(emberCells.OrderBy(p => p.Q).ThenBy(p => p.R).First()) + "。" : "阵心已破。用潮水熄灭余火，或待其耗尽。";
            else if (MovementTarget(session) is { } crossing)
                ProgressText = "潮水已灭火。步行穿过任一熄火格，如 " + Cell(crossing) + "。";
        }

        private void ObserveGate(CombatSession session, CombatantSnapshot player, CombatantSnapshot actor,
            CombatCommand command, CombatActionResult result)
        {
            var seal = session.Jindan.Seats.First(s => s.EffectId == "SPACE_SEAL");
            var gate = session.Jindan.Ability(seal.EffectId).Seal;
            var yin = session.Jindan.Seats.First(s => s.EffectId == CombatVisibility.Yin);
            var yang = session.Jindan.Seats.First(s => s.EffectId == CombatVisibility.Yang);
            session.Combatants.TryGet(yin.Definition.ActorId, out var hidden);
            session.Combatants.TryGet(yang.Definition.ActorId, out var observer);
            foreach (var receipt in session.Visibility.History)
            {
                if (receipt.EventId <= lastObservation) continue;
                lastObservation = receipt.EventId;
                if (receipt.TargetId == hidden.Id && session.Combatants.TryGet(receipt.ObserverId, out var eye) &&
                    eye.Team == CombatTeam.Player && session.Jindan.Seats.Any(s =>
                        s.Definition.ActorId == yang.Definition.ActorId && s.ActiveScale != null))
                    scaledObservations.Add(receipt.EventId);
            }
            if (seal.ActiveSeal != null) sealSupportId = seal.ActiveSeal.Support.Id;
            bool wasNear = nearBefore;
            nearBefore = YinDefender && session.Visibility.NaturallyRevealed(hidden);
            if (actor == null) { CheckObservation(); return; }
            if (YinDefender)
            {
                if (actor.Team == CombatTeam.Enemy && seal.ActiveSeal != null &&
                    result.MovementSteps.Count > 1 && result.MovementSteps[0].From.Anchor == gate.From &&
                    actor.Position == gate.To && result.MovementSteps.Any(step =>
                        step.To.Anchor != gate.From && step.To.Anchor != gate.To))
                    sealDelayed = true;
                if (actor == player && command.Kind == CombatCommandKind.CloseJindan && command.ProfileId == seal.EffectId) sealClosed = true;
                if (actor == player && command.Kind == CombatCommandKind.Move && sealDelayed && wasNear && !nearBefore)
                    retreated = true;
            }
            else if (actor.Team == CombatTeam.Player && result.SceneObjectChanges.Any(c => c.Id == sealSupportId && c.After == 0)) gateBroken = true;
            if (actor.Team == CombatTeam.Player)
                foreach (var step in result.MovementSteps)
                {
                    if (step.From.Anchor == step.To.Anchor) continue;
                    bool forward = step.From.Anchor == gate.From && step.To.Anchor == gate.To;
                    bool reverse = step.From.Anchor == gate.To && step.To.Anchor == gate.From;
                    if (sealClosed && seal.ActiveSeal == null && (forward || reverse)) crossedOpen = true;
                    if (gateBroken && forward) crossedBroken = true;
                    if (seal.ActiveSeal == null) continue;
                    if (step.From.Anchor == gate.From) detouring.Add(actor.Id);
                    if (detouring.Contains(actor.Id) && step.To.Anchor == gate.To && !forward) bypassed = true;
                }
            CheckObservation();

            void CheckObservation()
            {
                if (session.Visibility.IsPaused) return;
                if (YinDefender)
                {
                    if (sealDelayed && retreated && yin.ActiveVisibility != null && !session.Visibility.NaturallyRevealed(hidden) &&
                        !session.Visibility.IsVisible(observer.Id, hidden.Id) && (sealClosed ? crossedOpen : seal.ActiveSeal != null))
                        Finish(true, sealClosed ? "封关延缓对手，收关通行后以藏真撤出了近距观察。" : "门枢仍在封关，已利用藏真撤出对手的近距观察。");
                    else if (sealDelayed) ProgressText = "对手已受封关牵制。以藏真退离近距，或收关过门后退离。";
                    return;
                }
                if (crossedBroken && session.Visibility.History.Any(c => scaledObservations.Contains(c.EventId) && c.Visible))
                    Finish(true, "两层门锚已破，已过封边；定尺与阳照配合完成有效观察。");
                else if (bypassed && yin.ActiveVisibility != null && session.Visibility.NaturallyRevealed(hidden) &&
                    session.Combatants.All.Any(a => a.IsAlive && a.Team == CombatTeam.Player && session.Visibility.IsVisible(a.Id, hidden.Id)))
                    Finish(true, "绕过禁入边后，近距观察使藏真对手自然现形。");
                else if (crossedBroken) ProgressText = "已破锚过门。用定尺、阳照与敌方藏真交锋，取得可见结果。";
                else if (gateBroken) ProgressText = "两层门锚已破。从 (8,12) 直接走到 (9,12)。";
                else if (bypassed) ProgressText = "已绕过封边。靠近藏真对手至一格内完成观察。";
            }
        }

        private static string Cell(HexCoord cell) => $"({cell.Q},{cell.R})";

        private void Finish(bool success, string text)
        {
            Outcome = success ? CombatSessionOutcome.Victory : CombatSessionOutcome.Defeat;
            ProgressText = ResultText = text;
        }
    }
}
