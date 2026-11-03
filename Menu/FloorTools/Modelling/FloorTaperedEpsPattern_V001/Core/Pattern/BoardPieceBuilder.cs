using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Cuts the outline into grid boards and measures each board's thickness range.
    /// Stand-in for the V001 GridCutter until the V001 Core is copied in (see README).
    /// </summary>
    public static class BoardPieceBuilder
    {
        public static IReadOnlyList<BoardPiece> Build(GridFrame frame, ThicknessField thickness, IReadOnlyList<SlopeFace> faces) =>
            Build(frame, thickness.Distances.Outline, thickness, faces, string.Empty, null);

        /// <summary>Cuts <paramref name="region"/> (the outline or one slope face) into boards of <paramref name="frame"/>.</summary>
        public static IReadOnlyList<BoardPiece> Build(GridFrame frame, Polygon2D region, ThicknessField thickness,
            IReadOnlyList<SlopeFace> faces, string idPrefix, int? edgeIndex)
        {
            Polygon2D outline = region;
            double fullArea = frame.LengthM * frame.WidthM;
            var pieces = new List<BoardPiece>();

            foreach ((int row, double v0) in frame.Rows())
            {
                foreach ((int col, double u0) in frame.Columns())
                {
                    double u1 = u0 + frame.LengthM;
                    double v1 = v0 + frame.WidthM;
                    var cell = new[]
                    {
                        new HalfPlane(frame.U, -u0),
                        new HalfPlane(-frame.U, u1),
                        new HalfPlane(frame.V, -v0),
                        new HalfPlane(-frame.V, v1)
                    };

                    Polygon2D piece = ConvexClipper.ClipAll(outline, cell);
                    if (piece.IsEmpty || piece.Area < GeometryTolerance.MinLoopArea)
                    {
                        continue;
                    }

                    piece = piece.ToCounterClockwise();
                    pieces.Add(new BoardPiece
                    {
                        Id = idPrefix + string.Format(CultureInfo.InvariantCulture, "R{0:00}-C{1:00}", row, col),
                        EdgeIndex = edgeIndex,
                        Row = row,
                        Column = col,
                        Polygon = piece,
                        IsFull = Math.Abs(piece.Area - fullArea) < 1e-6,
                        Kind = Math.Abs(piece.Area - fullArea) < 1e-6 ? PieceKind.Full : PieceKind.Cut,
                        MinThicknessMm = piece.Vertices.Min(thickness.ThicknessAt),
                        MaxThicknessMm = MaxThickness(piece, thickness, faces)
                    });
                }
            }

            return pieces;
        }

        /// <summary>
        /// H is concave and piecewise linear (linear on each slope face), so its maximum over
        /// a convex piece is reached at a vertex of piece ∩ face for some face.
        /// </summary>
        private static double MaxThickness(Polygon2D piece, ThicknessField thickness, IReadOnlyList<SlopeFace> faces)
        {
            double max = piece.Vertices.Max(thickness.ThicknessAt);
            foreach (SlopeFace face in faces)
            {
                IEnumerable<HalfPlane> faceEdges = face.Polygon.Edges
                    .Where(e => e.Length > GeometryTolerance.Epsilon)
                    .Select(e => HalfPlane.FromPointNormal(e.Start, (e.End - e.Start).PerpLeft));
                Polygon2D part = ConvexClipper.ClipAll(piece, faceEdges);
                foreach (Vec2 p in part.Vertices)
                {
                    max = Math.Max(max, thickness.ThicknessAt(p));
                }
            }

            return max;
        }
    }
}
