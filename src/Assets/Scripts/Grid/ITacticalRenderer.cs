using System.Collections.Generic;
using TianZhang.Core;
using UnityEngine;

namespace TianZhang.Tactical
{
    public interface ITacticalRenderer
    {
        TacticalGridModel Model { get; }

        void RenderGrid(TacticalGridModel model);
        HexCoord ScreenToHex(Vector3 screenPosition);
        Vector3 HexToWorld(HexCoord coord);
        void HighlightMoveRange(IEnumerable<HexCoord> tiles);
        void HighlightAttackRange(IEnumerable<HexCoord> tiles);
        void ClearOverlay();
        GameObject PlaceUnitMarker(HexCoord coord, Color color, string label);
    }
}
