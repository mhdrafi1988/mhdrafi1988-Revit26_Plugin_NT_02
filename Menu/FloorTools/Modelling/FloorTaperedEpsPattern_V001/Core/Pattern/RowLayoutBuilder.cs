using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Contour-row layout. On every slope face, rows are the bands between thickness contours
    /// (row n spans n−1 … n board widths from the edge; the last row at the ridge takes what is left).
    /// Where each row starts and where its leftover cut goes is set by <see cref="Pattern2DSettings.RowStart"/>:
    /// <list type="bullet">
    /// <item>A — from both hips, one filler in the middle; hip pieces mirror across every hip.</item>
    /// <item>B — from both hips, leftover split equally into the two hip ends (no middle cut).</item>
    /// <item>C — from the left hip, leftover at the right-hand hip (no middle cut).</item>
    /// <item>D — from the exact middle: D1 joint on the centre line, D2 board centred, or Auto per face.</item>
    /// <item>E — from the upper hip point (+ optional hip strip): clean hip triangles, most full boards.</item>
    /// </list>
    /// <see cref="Pattern2DSettings.JointPattern"/> then keeps rows as set out, or carries the first row's
    /// joints up the slope (aligned / staggered). <see cref="Pattern2DSettings.BestFitOffset"/> instead
    /// searches one joint grid per side with the fewest slivers and most full boards.
    /// Board edges that are not on the face boundary become grid lines, joined along each line.
    /// </summary>
    public static class RowLayoutBuilder
    {
        private const double Eps = 1e-9;

        /// <summary>Best fit tries this many offsets across one board length.</summary>
        public const int BestFitSteps = 50;

        private sealed class FaceLayout
        {
            public List<BoardPiece> Pieces { get; } = new List<BoardPiece>();
            public List<Segment2D> Edges { get; } = new List<Segment2D>();
            public int Slivers { get; set; }
            public int FullCount => Pieces.Count(p => p.Kind == PieceKind.Full);
        }

        /// <summary>One row of a face, in the face's local frame (u along the edge, v up from the edge).</summary>
        private sealed class RowGeom
        {
            public int Row { get; init; }
            public double V0 { get; init; }
            public double V1 { get; init; }
            public bool Partial { get; init; }
            public Polygon2D Strip { get; init; } = Polygon2D.Empty;
            public double A0 { get; init; }
            public double B0 { get; init; }
            public double Ai { get; init; }
            public double Bi { get; init; }
        }

        /// <summary>A face in its local frame, with its rows.</summary>
        private sealed class FaceGeom
        {
            public SlopeFace Face { get; init; } = null!;
            public GridFrame Frame { get; init; } = null!;
            public double VBase { get; init; }
            public List<RowGeom> Rows { get; } = new List<RowGeom>();
            public Vec2 ToWorld(Vec2 q) => Frame.ToWorld(q.X, q.Y + VBase);
        }

        public static RowLayoutResult Build(ThicknessField thickness, IReadOnlyList<SlopeFace> faces,
            Pattern2DSettings settings, double minLengthM)
        {
            Polygon2D outline = thickness.Distances.Outline;
            var boards = new List<BoardPiece>();
            var lines = new List<PatternSegment>();
            var notes = new List<string>();
            int slivers = 0;

            foreach (SlopeFace face in faces)
            {
                FaceGeom geom = Describe(face, outline, settings);
                FaceLayout layout;

                if (settings.BestFitOffset)
                {
                    // Candidates: regular joint grids at BestFitSteps offsets, and the plain set-out
                    // (row start + joint pattern). Best fit is therefore never worse than the plain layout.
                    (double offset, int gridFull, int gridSlivers) = BestOffset(geom, settings);
                    (int planFull, int planSlivers) = ScoreFace(geom, settings, Planned(geom, settings, settings.MiddleVariant));
                    bool usePlan = planSlivers < gridSlivers || (planSlivers == gridSlivers && planFull >= gridFull);
                    layout = usePlan
                        ? LayFace(geom, thickness, settings, Planned(geom, settings, settings.MiddleVariant))
                        : LayFace(geom, thickness, settings, row => GridJoints(geom, row, settings, offset));
                    notes.Add(usePlan
                        ? string.Format(CultureInfo.InvariantCulture,
                            "Edge {0}: best fit kept the row-start set-out → {1} full boards, {2} slivers (best grid offset {3:0.000} m: {4} full, {5} slivers)",
                            face.EdgeIndex + 1, layout.FullCount, layout.Slivers, offset, gridFull, gridSlivers)
                        : string.Format(CultureInfo.InvariantCulture,
                            "Edge {0}: best fit offset {1:0.000} m → {2} full boards, {3} slivers",
                            face.EdgeIndex + 1, offset, layout.FullCount, layout.Slivers));
                }
                else if (settings.RowStart == RowStartMode.FromMiddle && settings.MiddleVariant == MiddleStartVariant.Auto)
                {
                    FaceLayout d1 = LayFace(geom, thickness, settings, Planned(geom, settings, MiddleStartVariant.JointOnCentre));
                    FaceLayout d2 = LayFace(geom, thickness, settings, Planned(geom, settings, MiddleStartVariant.BoardCentred));
                    bool pickD2 = d2.Slivers < d1.Slivers || (d2.Slivers == d1.Slivers && d2.FullCount > d1.FullCount);
                    layout = pickD2 ? d2 : d1;
                    notes.Add(string.Format(CultureInfo.InvariantCulture,
                        "Edge {0}: Auto chose {1} (D1: {2} full, {3} slivers · D2: {4} full, {5} slivers)",
                        face.EdgeIndex + 1, pickD2 ? "D2 board centred" : "D1 joint on centre line",
                        d1.FullCount, d1.Slivers, d2.FullCount, d2.Slivers));
                }
                else
                {
                    layout = LayFace(geom, thickness, settings, Planned(geom, settings, settings.MiddleVariant));
                }

                boards.AddRange(layout.Pieces);
                slivers += layout.Slivers;
                foreach (Segment2D s in CollinearUnion.Union(layout.Edges, GeometryTolerance.Merge))
                {
                    if (s.Length >= minLengthM)
                    {
                        lines.Add(new PatternSegment(PatternLayer.Grid, s, edgeIndex: face.EdgeIndex));
                    }
                }
            }

            return new RowLayoutResult
            {
                Boards = RowPieceCoder.Assign(boards),
                GridLines = lines,
                SliverCount = slivers,
                Notes = notes
            };
        }

        // ── Geometry ────────────────────────────────────────────────

        private static FaceGeom Describe(SlopeFace face, Polygon2D outline, Pattern2DSettings settings)
        {
            GridFrame frame = GridFrame.ForFace(face.Polygon, outline.Edge(face.EdgeIndex).Direction, settings);
            double vBase = frame.VMin; // the floor edge line
            var geom = new FaceGeom { Face = face, Frame = frame, VBase = vBase };
            Polygon2D local = new Polygon2D(face.Polygon.Vertices.Select(p =>
            {
                Vec2 q = frame.ToLocal(p);
                return new Vec2(q.X, q.Y - vBase);
            }));
            double w = settings.BoardWidthM;
            double maxV = local.Vertices.Max(p => p.Y);

            for (int row = 1; (row - 1) * w < maxV - GeometryTolerance.Vertex; row++)
            {
                double v0 = (row - 1) * w;
                double v1 = Math.Min(row * w, maxV);
                Polygon2D strip = ConvexClipper.ClipAll(local, new[]
                {
                    new HalfPlane(new Vec2(0, 1), -v0),
                    new HalfPlane(new Vec2(0, -1), v1)
                });
                if (strip.IsEmpty || strip.Area < GeometryTolerance.MinLoopArea * 1e-2)
                {
                    continue;
                }

                if (ConvexClipper.ClipLine(local, new Vec2(0, v0), Vec2.UnitX) is not Segment2D outer)
                {
                    continue;
                }

                double a0 = Math.Min(outer.Start.X, outer.End.X);
                double b0 = Math.Max(outer.Start.X, outer.End.X);
                double ai, bi;
                if (ConvexClipper.ClipLine(local, new Vec2(0, v1), Vec2.UnitX) is Segment2D upper)
                {
                    ai = Math.Min(upper.Start.X, upper.End.X);
                    bi = Math.Max(upper.Start.X, upper.End.X);
                }
                else
                {
                    ai = bi = (a0 + b0) / 2; // apex
                }

                geom.Rows.Add(new RowGeom
                {
                    Row = row,
                    V0 = v0,
                    V1 = v1,
                    Partial = v1 - v0 < w - GeometryTolerance.Vertex,
                    Strip = strip,
                    A0 = a0,
                    B0 = b0,
                    Ai = ai,
                    Bi = bi
                });
            }

            return geom;
        }

        // ── Joint set-out ──────────────────────────────────────────

        /// <summary>Row start option + joint pattern (as set out / aligned / staggered).</summary>
        private static Func<RowGeom, List<double>> Planned(FaceGeom geom, Pattern2DSettings settings, MiddleStartVariant variant)
        {
            double l = settings.BoardLengthM;
            List<double>? baseInner = null;
            int baseRow = 0;
            return r =>
            {
                if (settings.JointPattern == JointPattern.AsSetOut || baseInner == null)
                {
                    List<double> joints = RowJoints.For(settings.RowStart, variant, r.A0, r.B0, l, settings.MinPieceM,
                        r.Ai, r.Bi, settings.HipStripWidthM);
                    if (settings.JointPattern != JointPattern.AsSetOut && !r.Partial && joints.Count > 2)
                    {
                        baseInner = joints.GetRange(1, joints.Count - 2);
                        baseRow = r.Row;
                    }

                    return joints;
                }

                double shift = RowJoints.StaggerShift(settings.JointPattern, r.Row - baseRow, l);
                return RowJoints.Continue(baseInner, shift, r.A0, r.B0, l);
            };
        }

        /// <summary>Best fit: one regular joint grid per side (offset from the first row's left hip), staggered if chosen.</summary>
        private static List<double> GridJoints(FaceGeom geom, RowGeom r, Pattern2DSettings settings, double offset)
        {
            double l = settings.BoardLengthM;
            RowGeom first = geom.Rows[0];
            List<double> baseInner = RowJoints.Grid(first.A0, first.B0, offset, l);
            JointPattern pattern = settings.JointPattern == JointPattern.AsSetOut ? JointPattern.Aligned : settings.JointPattern;
            double shift = RowJoints.StaggerShift(pattern, r.Row - first.Row, l);
            return RowJoints.Continue(baseInner, shift, r.A0, r.B0, l);
        }

        /// <summary>Tries <see cref="BestFitSteps"/> offsets; fewest slivers, then most full boards, then smallest offset.</summary>
        private static (double Offset, int Full, int Slivers) BestOffset(FaceGeom geom, Pattern2DSettings settings)
        {
            double l = settings.BoardLengthM;
            (double Offset, int Full, int Slivers) best = (0, -1, int.MaxValue);
            if (geom.Rows.Count == 0)
            {
                return (0, 0, 0);
            }

            for (int k = 0; k < BestFitSteps; k++)
            {
                double offset = k * l / BestFitSteps;
                int full = 0, sl = 0;
                foreach (RowGeom r in geom.Rows)
                {
                    (int f, int s) = Score(r, GridJoints(geom, r, settings, offset), settings);
                    full += f;
                    sl += s;
                }

                if (sl < best.Slivers || (sl == best.Slivers && full > best.Full))
                {
                    best = (offset, full, sl);
                }
            }

            return best;
        }

        private static (int Full, int Slivers) ScoreFace(FaceGeom geom, Pattern2DSettings settings, Func<RowGeom, List<double>> jointsFor)
        {
            int full = 0, slivers = 0;
            foreach (RowGeom r in geom.Rows)
            {
                (int f, int s) = Score(r, jointsFor(r), settings);
                full += f;
                slivers += s;
            }

            return (full, slivers);
        }

        /// <summary>Fast 1-D count of full boards and slivers for one row's joints (no clipping).</summary>
        private static (int Full, int Slivers) Score(RowGeom r, List<double> joints, Pattern2DSettings settings)
        {
            double l = settings.BoardLengthM;
            double innerLeft = Math.Max(r.A0, r.Ai), innerRight = Math.Min(r.B0, r.Bi);
            double outerLeft = Math.Min(r.A0, r.Ai), outerRight = Math.Max(r.B0, r.Bi);
            bool canAvoid = r.B0 - r.A0 >= settings.MinPieceM;
            int full = 0, slivers = 0;
            for (int i = 0; i < joints.Count - 1; i++)
            {
                double u0 = joints[i], u1 = joints[i + 1];
                bool isFull = !r.Partial && Math.Abs(u1 - u0 - l) < 1e-6 && u0 >= innerLeft - 1e-9 && u1 <= innerRight + 1e-9;
                if (isFull)
                {
                    full++;
                    continue;
                }

                double lo = i == 0 ? outerLeft : u0;
                double hi = i == joints.Count - 2 ? outerRight : u1;
                if (canAvoid && hi - lo < settings.MinPieceM - GeometryTolerance.Vertex)
                {
                    slivers++;
                }
            }

            return (full, slivers);
        }

        // ── Pieces ──────────────────────────────────────────────────

        private static FaceLayout LayFace(FaceGeom geom, ThicknessField thickness, Pattern2DSettings settings,
            Func<RowGeom, List<double>> jointsFor)
        {
            double l = settings.BoardLengthM;
            double w = settings.BoardWidthM;
            SlopeFace face = geom.Face;
            var result = new FaceLayout();

            foreach (RowGeom r in geom.Rows)
            {
                List<double> joints = jointsFor(r);
                double mid = (r.A0 + r.B0) / 2;
                bool rowCanAvoidSlivers = r.B0 - r.A0 >= settings.MinPieceM;

                int cellCount = joints.Count - 1;
                for (int i = 0; i < cellCount; i++)
                {
                    var cuts = new List<HalfPlane>();
                    if (i > 0)
                    {
                        cuts.Add(new HalfPlane(Vec2.UnitX, -joints[i]));
                    }

                    if (i < cellCount - 1)
                    {
                        cuts.Add(new HalfPlane(-Vec2.UnitX, joints[i + 1]));
                    }

                    Polygon2D pieceLocal = ConvexClipper.ClipAll(r.Strip, cuts);
                    if (pieceLocal.IsEmpty || pieceLocal.Area < GeometryTolerance.MinLoopArea * 1e-2)
                    {
                        continue;
                    }

                    PieceKind kind = Classify(pieceLocal, r.Partial, l * w, cellCount, mid);
                    double minX = pieceLocal.Vertices.Min(p => p.X);
                    double maxX = pieceLocal.Vertices.Max(p => p.X);
                    if (kind != PieceKind.Full && rowCanAvoidSlivers && maxX - minX < settings.MinPieceM - GeometryTolerance.Vertex)
                    {
                        result.Slivers++;
                    }

                    double xRef = kind == PieceKind.HipLeft ? r.A0 : kind == PieceKind.HipRight ? r.B0 : minX;
                    Polygon2D world = new Polygon2D(pieceLocal.Vertices.Select(geom.ToWorld)).ToCounterClockwise();
                    result.Pieces.Add(new BoardPiece
                    {
                        Id = string.Format(CultureInfo.InvariantCulture, "E{0}-R{1:00}-P{2:00}", face.EdgeIndex + 1, r.Row, i + 1),
                        Row = r.Row,
                        Column = i + 1,
                        Polygon = world,
                        IsFull = kind == PieceKind.Full,
                        Kind = kind,
                        EdgeIndex = face.EdgeIndex,
                        ShapeKey = ShapeKey(pieceLocal, xRef, r.V0, mirror: kind == PieceKind.HipRight),
                        SpanAlongRowM = maxX - minX,
                        MinThicknessMm = world.Vertices.Min(thickness.ThicknessAt),
                        MaxThicknessMm = world.Vertices.Max(thickness.ThicknessAt)
                    });

                    foreach (Segment2D e in world.Edges)
                    {
                        if (e.Length >= GeometryTolerance.Merge && !face.Polygon.IsOnBoundaryLine(e, GeometryTolerance.Merge))
                        {
                            result.Edges.Add(e);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Ridge row first; then full boards; a row with a single piece is a plain cut; any other piece
        /// that is not a rectangle has been cut by a hip (left or right half of the row); a rectangular
        /// part-board is a straight cut inside the row.
        /// </summary>
        private static PieceKind Classify(Polygon2D piece, bool partialRow, double fullArea, int cellCount, double rowMid)
        {
            if (partialRow)
            {
                return PieceKind.RidgeRow;
            }

            if (Math.Abs(piece.Area - fullArea) < 1e-6)
            {
                return PieceKind.Full;
            }

            if (cellCount == 1)
            {
                return PieceKind.Cut;
            }

            (Vec2 min, Vec2 max) = piece.BoundingBox();
            bool rectangle = Math.Abs((max.X - min.X) * (max.Y - min.Y) - piece.Area) < 1e-6;
            if (rectangle)
            {
                return PieceKind.Filler;
            }

            return piece.Centroid.X < rowMid ? PieceKind.HipLeft : PieceKind.HipRight;
        }

        /// <summary>Vertices relative to the row (mm), mirrored for right-hand hip pieces, sorted.</summary>
        private static string ShapeKey(Polygon2D local, double xRef, double v0, bool mirror) =>
            string.Join(";", local.Vertices
                .Select(p => (X: (long)Math.Round((mirror ? xRef - p.X : p.X - xRef) * 1000), Y: (long)Math.Round((p.Y - v0) * 1000)))
                .Distinct()
                .OrderBy(p => p.X).ThenBy(p => p.Y)
                .Select(p => p.X.ToString(CultureInfo.InvariantCulture) + "," + p.Y.ToString(CultureInfo.InvariantCulture)));
    }
}
