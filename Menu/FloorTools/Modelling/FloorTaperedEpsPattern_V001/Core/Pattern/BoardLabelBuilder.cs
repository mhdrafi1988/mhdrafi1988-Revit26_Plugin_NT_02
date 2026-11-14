using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Layer D — one label per board (full and cut) at the board centroid.</summary>
    public static class BoardLabelBuilder
    {
        public static IReadOnlyList<PatternLabel> Build(IReadOnlyList<BoardPiece> boards, LabelMode mode)
        {
            if (mode == LabelMode.None)
            {
                return new List<PatternLabel>();
            }

            return boards
                .Select(b => new PatternLabel(b.Polygon.Centroid, mode == LabelMode.Code ? b.Code : b.Id, b.Id, b.EdgeIndex))
                .ToList();
        }
    }
}
