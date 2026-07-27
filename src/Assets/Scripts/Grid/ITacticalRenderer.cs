using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TianZhang.Core;
using TianZhang.Core.SpatialRules;
using UnityEngine;

namespace TianZhang.Tactical
{
    public readonly struct EnvironmentEdgePresentation
    {
        public EnvironmentEdgePresentation(
            HexCoord from,
            HexCoord to,
            bool movementAllowed,
            string movementReason,
            bool interactionAllowed,
            string interactionReason)
        {
            From = from;
            To = to;
            MovementAllowed = movementAllowed;
            MovementReason = movementReason ?? SpatialQueryReasons.Ok;
            InteractionAllowed = interactionAllowed;
            InteractionReason = interactionReason ?? SpatialQueryReasons.Ok;
        }

        public HexCoord From { get; }
        public HexCoord To { get; }
        public bool MovementAllowed { get; }
        public string MovementReason { get; }
        public bool InteractionAllowed { get; }
        public string InteractionReason { get; }
    }

    public sealed class EnvironmentPresentationSnapshot
    {
        private readonly ReadOnlyCollection<string> surfacePrototypeRefs;
        private readonly ReadOnlyCollection<EnvironmentPhenomenonChannel> phenomenonChannels;
        private readonly ReadOnlyCollection<EnvironmentEdgePresentation> directedEdges;

        private EnvironmentPresentationSnapshot(
            string profileId,
            IEnumerable<string> surfacePrototypeRefs,
            IEnumerable<EnvironmentPhenomenonChannel> phenomenonChannels,
            IEnumerable<EnvironmentEdgePresentation> directedEdges,
            string failureReason)
        {
            ProfileId = profileId;
            this.surfacePrototypeRefs = new ReadOnlyCollection<string>(
                new List<string>(surfacePrototypeRefs ?? Array.Empty<string>()));
            this.phenomenonChannels = new ReadOnlyCollection<EnvironmentPhenomenonChannel>(
                new List<EnvironmentPhenomenonChannel>(phenomenonChannels ?? Array.Empty<EnvironmentPhenomenonChannel>()));
            this.directedEdges = new ReadOnlyCollection<EnvironmentEdgePresentation>(
                new List<EnvironmentEdgePresentation>(directedEdges ?? Array.Empty<EnvironmentEdgePresentation>()));
            FailureReason = failureReason ?? SpatialQueryReasons.Ok;
        }

        public string ProfileId { get; }
        public IReadOnlyList<string> SurfacePrototypeRefs => surfacePrototypeRefs;
        public IReadOnlyList<EnvironmentPhenomenonChannel> PhenomenonChannels => phenomenonChannels;
        public IReadOnlyList<EnvironmentEdgePresentation> DirectedEdges => directedEdges;
        public string FailureReason { get; }
        public bool IsConfigured => string.IsNullOrEmpty(FailureReason);

        public static EnvironmentPresentationSnapshot Create(TacticalGridModel model)
        {
            if (model == null)
                return Failure(SpatialQuerySnapshotReasons.GridNotConfigured);
            if (model.EnvironmentRules == null)
                return Failure(SpatialQuerySnapshotReasons.EnvironmentProfileNotConfigured);
            if (!SpatialQueryBoardFactory.TryCreate(model, out var spatialSnapshot, out var reason))
                return Failure(reason);

            var environment = spatialSnapshot.Environment;
            var channels = new List<EnvironmentPhenomenonChannel>();
            foreach (EnvironmentPhenomenonChannel channel in Enum.GetValues(typeof(EnvironmentPhenomenonChannel)))
                channels.Add(channel);

            var edges = new List<EnvironmentEdgePresentation>();
            foreach (var edge in environment.DirectedEdges)
            {
                var from = new SpatialHexCoord(edge.fromQ, edge.fromR);
                var to = new SpatialHexCoord(edge.toQ, edge.toR);
                var movement = spatialSnapshot.Board.InspectEdge(from, to, SpatialQueryKind.Movement);
                var interaction = spatialSnapshot.Board.InspectEdge(from, to, SpatialQueryKind.Attack);
                edges.Add(new EnvironmentEdgePresentation(
                    new HexCoord(edge.fromQ, edge.fromR),
                    new HexCoord(edge.toQ, edge.toR),
                    movement.IsLegal,
                    movement.Reason,
                    interaction.IsLegal,
                    interaction.Reason));
            }

            return new EnvironmentPresentationSnapshot(
                environment.ProfileId,
                environment.SurfacePrototypeRefs,
                channels,
                edges,
                SpatialQueryReasons.Ok);
        }

        private static EnvironmentPresentationSnapshot Failure(string reason)
        {
            return new EnvironmentPresentationSnapshot(
                null,
                Array.Empty<string>(),
                Array.Empty<EnvironmentPhenomenonChannel>(),
                Array.Empty<EnvironmentEdgePresentation>(),
                reason);
        }
    }

    public interface ITacticalRenderer
    {
        TacticalGridModel Model { get; }
        EnvironmentPresentationSnapshot EnvironmentPresentation { get; }

        void RenderGrid(TacticalGridModel model);
        EnvironmentPresentationSnapshot PresentEnvironment(TacticalGridModel model);
        HexCoord ScreenToHex(Vector3 screenPosition);
        Vector3 HexToWorld(HexCoord coord);
        void HighlightMoveRange(IEnumerable<HexCoord> tiles);
        void HighlightAttackRange(IEnumerable<HexCoord> tiles);
        void ClearOverlay();
        GameObject PlaceUnitMarker(HexCoord coord, Color color, string label);
    }
}
