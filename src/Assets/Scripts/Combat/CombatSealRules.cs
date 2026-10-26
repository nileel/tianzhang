using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public sealed class CombatSealDefinition
    {
        public CombatSealDefinition(HexCoord from, HexCoord to, string supportId, int localRadius,
            int layerDurability, int brokenLayerTicks, int durationTicks)
        {
            if (!from.TryGetDirectionTo(to, out _) || string.IsNullOrWhiteSpace(supportId) ||
                localRadius < 0 || layerDurability <= 0 || brokenLayerTicks <= 0 || durationTicks <= 0)
                throw new ArgumentException("seal_definition_invalid");
            From = from; To = to; SupportId = supportId; LocalRadius = localRadius;
            LayerDurability = layerDurability; BrokenLayerTicks = brokenLayerTicks; DurationTicks = durationTicks;
        }
        public HexCoord From { get; }
        public HexCoord To { get; }
        public string SupportId { get; }
        public int LocalRadius { get; }
        public int LayerDurability { get; }
        public int BrokenLayerTicks { get; }
        public int DurationTicks { get; }
        public bool ContainsOwner(HexCoord cell) => cell.TopologicalDistanceTo(To) <= LocalRadius;
    }

    public sealed class CombatSealInstance
    {
        internal CombatSealInstance(CombatSealDefinition definition, CombatTeam ownerTeam, int tick, CombatSceneObject support)
        {
            Definition = definition; OwnerTeam = ownerTeam; CreatedTick = LastPaidTick = tick;
            ExpiresTick = checked(tick + definition.DurationTicks); Support = support;
        }
        public CombatSealDefinition Definition { get; }
        public CombatTeam OwnerTeam { get; }
        public int CreatedTick { get; }
        public int LastPaidTick { get; internal set; }
        public int ExpiresTick { get; internal set; }
        public bool OuterLayerBroken { get; internal set; }
        public CombatSceneObject Support { get; }
        public int OuterDurability => Math.Max(0, Support.Durability - Definition.LayerDurability);
        public int InnerDurability => Math.Min(Support.Durability, Definition.LayerDurability);
    }

    /// <summary>One fixed enemy-entry condition. No targeting or per-trigger payment occurs after deployment.</summary>
    public static class CombatSealRules
    {
        internal static string Validate(CombatSession session, CombatantSnapshot actor, CombatJindanRequest request,
            CombatJindanAbilityDefinition ability)
        {
            var definition = ability.Seal;
            if (request.EdgePath.Count != 0 || request.RemoteOrigin.HasValue || request.MetricDistanceUnits != 0 ||
                request.EarthMode.HasValue || request.TideAction.HasValue || request.SelectedTargetCell.HasValue ||
                request.EarthTargetId != null || request.TideSource.HasValue || request.Charged || request.CreateTideEye)
                return "seal_fixed_condition_required";
            if (!definition.ContainsOwner(actor.Position)) return "seal_owner_outside_domain";
            var board = session.SceneObjects.Board;
            var policy = new SpatialAttackPolicy(SpatialMovementMode.Ground | SpatialMovementMode.Fly,
                SpatialMovementMode.Ground, ability.VerticalReach);
            foreach (var cell in new[] { definition.From, definition.To })
            {
                if (!board.TryGetCell(cell, out var rules) || rules.IsEntityObstacle || rules.BlocksMovement)
                    return "seal_cell_invalid";
                var target = new SpatialPose(cell, 0, SpatialFootprintKind.Single, 1);
                var contact = board.QueryContact(actor.Pose, target, 0, ability.Range, policy);
                if (!contact.IsInRange || !contact.HasLineOfSight) return contact.Reason;
            }
            if (!board.TryGetEdge(definition.From, definition.To, out _)) return "seal_edge_missing";
            if (session.TurnScheduler.CurrentTick > int.MaxValue - definition.DurationTicks) return "combat_effect_overflow";
            return string.Empty;
        }

        internal static CombatSealInstance Prepare(CombatSession session, CombatantSnapshot actor,
            CombatJindanSeatState seat, CombatJindanAbilityDefinition ability)
        {
            var definition = ability.Seal;
            var support = new CombatSceneObject(seat.Definition.AbilityInstanceId + ":" + definition.SupportId +
                ":" + (seat.SuccessfulDeployments + 1), new CombatSceneObjectDefinition("stone_support", "stone",
                    ability.SupportDurability, 0, 0, false, false, "failed", isSupportMarker: true),
                definition.To, sourceRef: seat.Definition.AbilityInstanceId);
            return new CombatSealInstance(definition, actor.Team, session.TurnScheduler.CurrentTick, support);
        }

        public static IReadOnlyList<SpatialDirectedEdge> BlockedEdges(CombatSession session, CombatantSnapshot actor) =>
            actor.MovementMode is not (SpatialMovementMode.Ground or SpatialMovementMode.Fly) ? Array.Empty<SpatialDirectedEdge>() :
            session.Jindan?.Seats.Where(seat => seat.ActiveSeal != null && seat.ActiveSeal.OwnerTeam != actor.Team)
                .Select(seat => new SpatialDirectedEdge(seat.ActiveSeal.Definition.From, seat.ActiveSeal.Definition.To))
                .ToArray() ?? Array.Empty<SpatialDirectedEdge>();

        internal static CombatCommand Deployment(CombatJindanSeatState seat) => new CombatCommand(
            CombatCommandKind.JindanAbility, seat.Definition.ActorId, profileId: "SPACE_SEAL",
            jindan: new CombatJindanRequest(seat.Definition.SeatId));

        /// <summary>While maintaining the gate, rank only moves that keep its existing local support contract.</summary>
        internal static IReadOnlyList<CombatCommand> MaintainingActions(CombatSession session,
            IReadOnlyList<CombatCommand> actions)
        {
            var seals = session.Jindan?.Seats.Where(seat => seat.ActiveSeal != null &&
                seat.Definition.ActorId == session.CurrentActorId).Select(seat => seat.ActiveSeal).ToArray();
            if (seals == null || seals.Length == 0) return actions;
            return actions.Where(action => action.Kind != CombatCommandKind.Move ||
                seals.All(seal => seal.Definition.ContainsOwner(action.Destination.Value))).ToArray();
        }
    }
}
