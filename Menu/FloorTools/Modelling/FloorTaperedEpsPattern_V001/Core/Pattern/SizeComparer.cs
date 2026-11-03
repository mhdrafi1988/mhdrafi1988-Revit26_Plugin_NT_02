using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Lays the floor once per board size with the current settings and reports full boards, cuts,
    /// codes and slivers. Best = most full-board area, then fewest slivers, then fewest pieces.
    /// With square boards selected, non-square sizes are reported but marked.
    /// </summary>
    public static class SizeComparer
    {
        public static IReadOnlyList<SizeComparisonRow> Compare(Polygon2D outlineCcw, Pattern2DSettings settings, IReadOnlyList<BoardSize> sizes)
        {
            var rows = new List<SizeComparisonRow>();
            double area = outlineCcw.Area;
            foreach (BoardSize size in sizes)
            {
                Pattern2DSettings s = settings with
                {
                    BoardLengthM = size.LengthM,
                    BoardWidthM = size.WidthM,
                    Shape = settings.Shape == BoardShape.Square && !size.IsSquare ? BoardShape.Rectangular : settings.Shape,
                    DrawGrid = false,
                    DrawHipRidge = false,
                    DrawContours = false,
                    Labels = LabelMode.None,
                    Hatch = HatchMode.None
                };
                Pattern2DResult r = Pattern2DEngine.Generate(outlineCcw, s);
                if (!r.IsValid)
                {
                    rows.Add(new SizeComparisonRow { Size = size, Note = string.Join(" ", r.Errors) });
                    continue;
                }

                int full = r.Boards.Count(b => b.Kind == PieceKind.Full);
                rows.Add(new SizeComparisonRow
                {
                    Size = size,
                    Pieces = r.BoardCount,
                    Full = full,
                    FullAreaShare = area > 0 ? full * size.LengthM * size.WidthM / area : 0,
                    HipCuts = r.Boards.Count(b => b.Kind == PieceKind.HipLeft || b.Kind == PieceKind.HipRight),
                    StraightCuts = r.Boards.Count(b => b.Kind == PieceKind.Filler || b.Kind == PieceKind.Cut),
                    Codes = r.CodeCount,
                    Slivers = r.SliverCount,
                    Note = settings.Shape == BoardShape.Square && !size.IsSquare ? "not square — laid as rectangular" : string.Empty
                });
            }

            SizeComparisonRow? best = rows.Where(x => x.Pieces > 0)
                .OrderByDescending(x => System.Math.Round(x.FullAreaShare, 4))
                .ThenBy(x => x.Slivers)
                .ThenBy(x => x.Pieces)
                .FirstOrDefault();
            return rows.Select(x => ReferenceEquals(x, best)
                ? new SizeComparisonRow
                {
                    Size = x.Size, Pieces = x.Pieces, Full = x.Full, FullAreaShare = x.FullAreaShare, HipCuts = x.HipCuts,
                    StraightCuts = x.StraightCuts, Codes = x.Codes, Slivers = x.Slivers, IsBest = true, Note = x.Note
                }
                : x).ToList();
        }
    }
}
