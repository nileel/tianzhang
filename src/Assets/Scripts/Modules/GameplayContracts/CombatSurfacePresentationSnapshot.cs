using System;

namespace TianZhang.Gameplay.Contracts
{
    public enum CombatSurfaceDisplayKind { None, Scorched, Burning, Wet, ShallowWater, DeepWater }

    public sealed class CombatSurfaceCellSnapshot
    {
        public CombatSurfaceCellSnapshot(CombatUnitPresentationHex cell, CombatSurfaceDisplayKind kind, int fuel)
        { Cell = cell; Kind = kind; Fuel = fuel; }
        public CombatUnitPresentationHex Cell { get; }
        public CombatSurfaceDisplayKind Kind { get; }
        public int Fuel { get; }
    }

    public sealed class CombatTidePushSnapshot
    {
        public CombatTidePushSnapshot(string actorId, CombatUnitPresentationHex origin,
            CombatUnitPresentationHex destination, int direction, string reason)
        { ActorId = actorId; Origin = origin; Destination = destination; Direction = direction; Reason = reason; }
        public string ActorId { get; }
        public CombatUnitPresentationHex Origin { get; }
        public CombatUnitPresentationHex Destination { get; }
        public int Direction { get; }
        public string Reason { get; }
    }

    public sealed class CombatSurfacePresentationSnapshot
    {
        public CombatSurfacePresentationSnapshot(long revision, CombatSurfaceCellSnapshot[] cells,
            CombatUnitPresentationHex[] changes = null, CombatTidePushSnapshot[] pushes = null,
            CombatUnitPresentationHex? source = null, int sourceHeight = 0, int waterlineHeight = 0)
        {
            Revision = revision; Cells = cells; Changes = changes ?? Array.Empty<CombatUnitPresentationHex>();
            Pushes = pushes ?? Array.Empty<CombatTidePushSnapshot>(); Source = source;
            SourceHeight = sourceHeight; WaterlineHeight = waterlineHeight;
        }
        public long Revision { get; }
        public CombatSurfaceCellSnapshot[] Cells { get; }
        public CombatUnitPresentationHex[] Changes { get; }
        public CombatTidePushSnapshot[] Pushes { get; }
        public CombatUnitPresentationHex? Source { get; }
        public int SourceHeight { get; }
        public int WaterlineHeight { get; }
    }
}
