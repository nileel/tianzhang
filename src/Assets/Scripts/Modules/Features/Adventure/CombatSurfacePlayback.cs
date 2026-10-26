using System;
using System.Collections;
using System.Linq;
using TianZhang.Combat;
using TianZhang.Gameplay.Contracts;
using TianZhang.Spatial;

namespace TianZhang.Features.Adventure
{
    /// <summary>Consumes committed environment receipts once through the existing unit presentation port.</summary>
    internal sealed class CombatSurfacePlayback
    {
        private CombatSession observed;
        private long sequence;
        internal bool Busy { get; private set; }
        private void Observe(CombatSession session)
        { if (observed != session) { observed = session; sequence = 0; } }

        internal int PendingDamage(CombatSession session, string actorId)
        {
            Observe(session);
            return session?.Surfaces?.Settlements.Where(value => value.Sequence > sequence)
                .SelectMany(value => value.Damage.Concat(value.FallDamage)).Where(value => value.TargetId == actorId).Sum(value => value.FinalDamage) ?? 0;
        }

        internal IEnumerator Play(CombatSession session, ICombatUnitPresentationPort port, Action<string> log)
        {
            Observe(session);
            var receipts = session?.Surfaces?.Settlements.Where(value => value.Sequence > sequence).ToArray();
            if (receipts == null || receipts.Length == 0) yield break;
            Busy = true;
            try
            {
                var activity = port as ICombatPresentationActivity;
                foreach (var receipt in receipts)
                {
                    while (activity?.IsPresenting == true) yield return null;
                    foreach (var push in receipt.Pushes.Where(value => value.Movement.Path.Count > 0))
                    {
                        var movement = push.Movement;
                        var previous = movement.Origin;
                        var steps = movement.Path.Select(pose =>
                        {
                            var step = new CombatUnitPresentationMovementStep(Pose(previous), Pose(pose), isForced: true);
                            previous = pose; return step;
                        }).ToArray();
                        port.Present(new CombatUnitPresentationEventProjection(push.ActorId, CombatUnitPresentationEvent.Move,
                            Hex(movement.Origin.Anchor), Hex(movement.Destination.Anchor), movement.Destination.Facing, null,
                            movement.Path.Select(value => Hex(value.Anchor)).ToArray(), movementSteps: steps,
                            altitudeBand: movement.Destination.AltitudeBand));
                    }
                    while (activity?.IsPresenting == true) yield return null;
                    foreach (var group in receipt.Damage.Concat(receipt.FallDamage).GroupBy(value => value.TargetId))
                    {
                        var actor = receipt.Targets.Single(value => value.ActorId == group.Key);
                        int damage = group.Sum(value => value.FinalDamage);
                        port.Present(new CombatUnitPresentationEventProjection(actor.ActorId,
                            actor.IsAlive ? CombatUnitPresentationEvent.Hit : CombatUnitPresentationEvent.Death,
                            Hex(actor.Pose.Anchor), Hex(actor.Pose.Anchor), actor.Pose.Facing,
                            new[] { new CombatUnitPresentationTargetResult(actor.ActorId, damage, !actor.IsAlive) },
                            altitudeBand: actor.Pose.AltitudeBand));
                    }
                    log("地表结算 t=" + receipt.Tick + "：变化" + receipt.Changes.Count + "格，推移" +
                        receipt.Pushes.Count(value => value.Movement.Path.Count > 0) + "人，伤害" +
                        receipt.Damage.Sum(value => value.FinalDamage) + "，坠落" + receipt.FallDamage.Sum(value => value.FinalDamage) +
                        "，冲蚀" + receipt.SceneObjectChanges.Count + "件。");
                    sequence = receipt.Sequence;
                    while (activity?.IsPresenting == true) yield return null;
                }
            }
            finally { Busy = false; }
        }
        internal void Clear() { Busy = false; observed = null; sequence = 0; }
        private static CombatUnitPresentationHex Hex(HexCoord value) => new(value.Q, value.R);
        private static CombatUnitPresentationPose Pose(SpatialPose value) =>
            new(Hex(value.Anchor), value.Facing, value.Footprint == SpatialFootprintKind.AdjacentPair, value.AltitudeBand);
    }
}
