using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    /// <summary>Battle-local observation receipts and optional contest. Read queries never charge or create events.</summary>
    public sealed class CombatVisibility
    {
        public const string Yang = "YANG_RADIANCE", Yin = "YIN_CONCEAL";
        private readonly CombatSession session;
        private readonly Dictionary<string, bool> receipts = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly List<CombatVisibilityConflict> history = new List<CombatVisibilityConflict>();
        private long nextEvent;
        private bool ended;
        internal CombatVisibility(CombatSession session) => this.session = session;
        public CombatVisibilityConflict Current { get; private set; }
        public IReadOnlyList<CombatVisibilityConflict> History => history.AsReadOnly();
        public bool IsPaused => Current?.IsPending == true;
        public bool AutoSkip { get; private set; }
        public long Revision { get; private set; }
        public static bool IsEffect(string effect) => effect is Yang or Yin;

        public bool IsVisible(string observerId, string targetId)
        {
            if (!session.Combatants.TryGet(observerId, out var observer) || !session.Combatants.TryGet(targetId, out var target)) return false;
            if (observer.Team == target.Team) return true;
            if (!HasDomain) return true; // Existing encounters have no visibility mechanic.
            if (!NormalSight(observer, target)) return false;
            var yin = Concealing(target);
            if (yin == null || NaturallyRevealed(target)) return true;
            var yang = Radiance;
            if (yang == null) return false;
            return receipts.TryGetValue(Key(observer, target, yang, yin), out bool visible) && visible;
        }
        public bool HasDomain => session.Jindan.Seats.Any(seat => seat.ActiveVisibility != null);
        private CombatJindanSeatState Radiance => session.Jindan.Seats.FirstOrDefault(seat => seat.EffectId == Yang && seat.ActiveVisibility != null);
        private CombatJindanSeatState Concealing(CombatantSnapshot target) => session.Jindan.Seats.FirstOrDefault(seat =>
            seat.EffectId == Yin && seat.ActiveVisibility != null && session.Combatants.TryGet(seat.Definition.ActorId, out var owner) && owner.Team == target.Team);

        public bool NormalSight(CombatantSnapshot observer, CombatantSnapshot target)
        {
            if (!observer.IsAlive || !target.IsAlive || observer.MovementMode is not (SpatialMovementMode.Ground or SpatialMovementMode.Fly) ||
                target.MovementMode is not (SpatialMovementMode.Ground or SpatialMovementMode.Fly)) return false;
            var ownSeat = session.Jindan.Seats.FirstOrDefault(seat => seat.Definition.ActorId == observer.Id);
            if (ownSeat == null) return false;
            int range = session.Jindan.Ability(ownSeat.EffectId).VisionRange;
            var board = session.SceneObjects.Board;
            return observer.OccupiedCells.Any(from => target.OccupiedCells.Any(to =>
                board.QueryMetricDistance(from, to, SpatialQueryKind.Sight, range).IsReachable &&
                board.QueryLineOfSight(from, to).HasLineOfSight));
        }
        public bool NaturallyRevealed(CombatantSnapshot target) => session.Combatants.All.Any(enemy =>
            enemy.IsAlive && enemy.Team != target.Team && Near(enemy, target, SpatialQueryKind.Sight));
        private bool Near(CombatantSnapshot source, CombatantSnapshot target, SpatialQueryKind kind) =>
            source.OccupiedCells.Any(from => target.OccupiedCells.Any(to => from.TopologicalDistanceTo(to) <= 1 ||
                session.SceneObjects.Board.QueryMetricDistance(from, to, kind, 1).IsReachable));

        /// <summary>Called only after an authoritative command or turn boundary, never from a preview or UI read.</summary>
        public void Refresh()
        {
            if (ended || IsPaused || session.IsFinished) return;
            var yang = Radiance;
            if (yang == null) return;
            foreach (var observer in session.Combatants.All.Where(actor => actor.IsAlive))
            foreach (var target in session.Combatants.All.Where(actor => actor.IsAlive && actor.Team != observer.Team))
            {
                var yin = Concealing(target);
                if (yin == null || yang.Definition.ActorId == yin.Definition.ActorId || !NormalSight(observer, target) || NaturallyRevealed(target)) continue;
                string key = Key(observer, target, yang, yin);
                if (receipts.ContainsKey(key)) continue;
                session.Combatants.TryGet(yang.Definition.ActorId, out var left);
                session.Combatants.TryGet(yin.Definition.ActorId, out var right);
                Current = new CombatVisibilityConflict(++nextEvent, key, observer.Id, target.Id, left, yang, right, yin,
                    yang.ActiveVisibility.Definition.Conflict, Priority(yang), Priority(yin), true);
                history.Add(Current); Revision++;
                if (AutoSkip || Current.PlayerSide == 0) Current.Skip(Current.EventId);
                CommitResult();
                if (IsPaused) return;
            }
        }
        private static CombatConflictPriority Priority(CombatJindanSeatState seat)
        {
            var instance = seat.ActiveVisibility;
            // This finite contract admits DOMAIN only; it does not invent a cross-position ranking.
            if (seat.Definition.PositionType != "Domain") throw new InvalidOperationException("visibility_requires_domain");
            return new CombatConflictPriority(true, 1, instance.Definition.SupportRank,
                instance.PaidActionPoints, instance.PaidMana, instance.Definition.Carrying);
        }
        private string Key(CombatantSnapshot observer, CombatantSnapshot target, CombatJindanSeatState yang, CombatJindanSeatState yin) =>
            observer.Id + ":" + target.Id + ":" + session.TurnScheduler.CurrentTick + ":" +
            yang.ActiveVisibility.Support.Id + ":" + yin.ActiveVisibility.Support.Id + ":" +
            string.Join("/", observer.OccupiedCells) + ":" + observer.AltitudeBand + ":" +
            string.Join("/", target.OccupiedCells) + ":" + target.AltitudeBand + ":" +
            session.SceneObjects.Board.QueryMetricDistance(observer.Position, target.Position, SpatialQueryKind.Sight).DistanceUnits + ":" +
            yang.ActiveVisibility.Support.Durability + ":" + yin.ActiveVisibility.Support.Durability;

        public void Input(long eventId, long milliseconds, bool holdPressed, bool holdReleased, int pressedKeyMask)
        {
            if (!IsPaused || Current.EventId != eventId) return;
            int pulses = Current.PulseCount, gauge = Current.Gauge;
            Current.Input(eventId, milliseconds, holdPressed, holdReleased, pressedKeyMask);
            if (Current.PulseCount != pulses || Current.Gauge != gauge) Revision++;
            CommitResult();
        }
        public void Skip(long eventId, bool wholeBattle)
        {
            if (!IsPaused || Current.EventId != eventId) return;
            AutoSkip |= wholeBattle; Current.Skip(eventId); Revision++; CommitResult();
        }
        private void CommitResult()
        {
            if (Current == null || Current.IsPending || Current.Outcome == CombatConflictOutcome.Cancelled || receipts.ContainsKey(Current.QueryKey)) return;
            receipts.Add(Current.QueryKey, Current.Visible); Revision++;
        }
        public void Clear()
        { ended = true; Current?.Cancel(); receipts.Clear(); Revision++; }

        internal static string Validate(CombatSession session, CombatantSnapshot actor, CombatJindanRequest request, CombatJindanAbilityDefinition ability)
        {
            if (request.EdgePath.Count != 0 || request.RemoteOrigin.HasValue || request.MetricDistanceUnits != 0 ||
                request.EarthMode.HasValue || request.TideAction.HasValue ||
                request.EarthTargetId != null || request.TideSource.HasValue || request.Charged || request.CreateTideEye)
                return "visibility_fixed_domain_required";
            var definition = ability.Visibility;
            if (request.SelectedTargetCell is { } selected && selected != definition.Center) return "visibility_support_required";
            var policy = new SpatialAttackPolicy(SpatialMovementMode.Ground | SpatialMovementMode.Fly, SpatialMovementMode.Ground, ability.VerticalReach);
            var contact = session.SceneObjects.Board.QueryContact(actor.Pose, new SpatialPose(definition.Center), 0, ability.Range, policy);
            if (!contact.IsInRange || !contact.HasLineOfSight) return contact.Reason;
            if (session.TurnScheduler.CurrentTick > int.MaxValue - definition.DurationTicks) return "combat_effect_overflow";
            return string.Empty;
        }
        internal static CombatVisibilityInstance Prepare(CombatSession session, CombatJindanSeatState seat, CombatJindanAbilityDefinition ability)
        {
            var definition = ability.Visibility;
            var support = new CombatSceneObject(seat.Definition.AbilityInstanceId + ":" + definition.SupportId + ":" + (seat.SuccessfulDeployments + 1),
                new CombatSceneObjectDefinition("stone_support", "stone", ability.SupportDurability, 0, 0, false, false, "failed", isSupportMarker: true),
                definition.Center, sourceRef: seat.Definition.AbilityInstanceId);
            return new CombatVisibilityInstance(definition, support, session.TurnScheduler.CurrentTick, ability.ActionPoints, ability.Mana);
        }
    }
}
