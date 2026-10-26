using System;
using System.Collections.Generic;

namespace TianZhang.Gameplay.Contracts
{
    /// <summary>Committed footprint projected by combat authority; position is always the front cell.</summary>
    [Serializable]
    public readonly struct CombatUnitPresentationPose : IEquatable<CombatUnitPresentationPose>
    {
        public CombatUnitPresentationPose(CombatUnitPresentationHex position, int facing, bool isDoubleCell = false, int altitudeBand = 0)
        {
            if (facing < 0 || facing >= 6) throw new ArgumentOutOfRangeException(nameof(facing));
            Position = position;
            Facing = facing;
            IsDoubleCell = isDoubleCell;
            if (altitudeBand is < 0 or > 2 || isDoubleCell && altitudeBand != 0) throw new ArgumentOutOfRangeException(nameof(altitudeBand));
            AltitudeBand = altitudeBand;
        }

        public CombatUnitPresentationHex Position { get; }
        public int Facing { get; }
        public bool IsDoubleCell { get; }
        public int AltitudeBand { get; }
        public CombatUnitPresentationHex Rear => Step(Position, (Facing + 3) % 6);
        public IReadOnlyList<CombatUnitPresentationHex> OccupiedCells => IsDoubleCell
            ? new[] { Position, Rear } : new[] { Position };
        public bool Occupies(CombatUnitPresentationHex cell) => Position.Equals(cell) ||
            (IsDoubleCell && Rear.Equals(cell));
        public bool Equals(CombatUnitPresentationPose other) => Position.Equals(other.Position) &&
            Facing == other.Facing && IsDoubleCell == other.IsDoubleCell && AltitudeBand == other.AltitudeBand;
        public override bool Equals(object obj) => obj is CombatUnitPresentationPose other && Equals(other);
        public override int GetHashCode() => ((Position.GetHashCode() * 397 ^ Facing) * 397 ^ IsDoubleCell.GetHashCode()) * 397 ^ AltitudeBand;

        public static CombatUnitPresentationHex Step(CombatUnitPresentationHex position, int direction) => direction switch
        {
            0 => new CombatUnitPresentationHex(position.Q + 1, position.R),
            1 => new CombatUnitPresentationHex(position.Q + 1, position.R - 1),
            2 => new CombatUnitPresentationHex(position.Q, position.R - 1),
            3 => new CombatUnitPresentationHex(position.Q - 1, position.R),
            4 => new CombatUnitPresentationHex(position.Q - 1, position.R + 1),
            5 => new CombatUnitPresentationHex(position.Q, position.R + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };
    }

    /// <summary>One already-validated turn sequence followed by one forward anchor edge.</summary>
    [Serializable]
    public sealed class CombatUnitPresentationMovementStep : IEquatable<CombatUnitPresentationMovementStep>
    {
        public CombatUnitPresentationMovementStep(CombatUnitPresentationPose from, CombatUnitPresentationPose to,
            IReadOnlyList<int> turnFacings = null, bool isLightLeap = false)
        {
            if (from.IsDoubleCell != to.IsDoubleCell ||
                !CombatUnitPresentationPose.Step(from.Position, to.Facing).Equals(to.Position))
                throw new ArgumentException("Movement must advance one cell without changing footprint.");
            var turns = turnFacings == null ? new List<int>() : new List<int>(turnFacings);
            if (from.IsDoubleCell)
            {
                int facing = from.Facing;
                int delta = (to.Facing - facing + 6) % 6;
                if (turns.Count != Math.Min(delta, 6 - delta))
                    throw new ArgumentException("Movement requires a shortest sixty-degree turn sequence.");
                int turnSign = 0;
                foreach (int turn in turns)
                {
                    if (turn < 0 || turn >= 6) throw new ArgumentOutOfRangeException(nameof(turnFacings));
                    int turnDelta = (turn - facing + 6) % 6;
                    if ((turnDelta != 1 && turnDelta != 5) || (turnSign != 0 && turnDelta != turnSign))
                        throw new ArgumentException("Movement turns must be consecutive sixty-degree steps.");
                    turnSign = turnDelta;
                    facing = turn;
                }
                if (facing != to.Facing) throw new ArgumentException("Movement turns do not reach the final facing.");
            }
            From = from;
            To = to;
            TurnFacings = turns.AsReadOnly();
            IsLightLeap = isLightLeap;
        }
        public CombatUnitPresentationPose From { get; }
        public CombatUnitPresentationPose To { get; }
        public IReadOnlyList<int> TurnFacings { get; }
        public bool IsLightLeap { get; }
        public bool Equals(CombatUnitPresentationMovementStep other)
        {
            if (other == null || !From.Equals(other.From) || !To.Equals(other.To) || TurnFacings.Count != other.TurnFacings.Count || IsLightLeap != other.IsLightLeap)
                return false;
            for (int index = 0; index < TurnFacings.Count; index++)
                if (TurnFacings[index] != other.TurnFacings[index]) return false;
            return true;
        }
        public override bool Equals(object obj) => Equals(obj as CombatUnitPresentationMovementStep);
        public override int GetHashCode()
        {
            int hash = From.GetHashCode() * 397 ^ To.GetHashCode();
            hash = hash * 397 ^ IsLightLeap.GetHashCode();
            foreach (int facing in TurnFacings) hash = hash * 397 ^ facing;
            return hash;
        }
    }
}
