using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Board grid frame: U runs along the board length, V across (V = U rotated +90°).
    /// Local coordinates are (p·U, p·V) — a pure rotation about the plan origin.
    /// </summary>
    public sealed class GridFrame
    {
        private GridFrame(Vec2 u, double lengthM, double widthM, Polygon2D outline, GridOriginMode origin, bool flushV = false)
        {
            U = u;
            V = u.PerpLeft;
            LengthM = lengthM;
            WidthM = widthM;

            IEnumerable<Vec2> local = outline.Vertices.Select(ToLocal);
            UMin = local.Min(p => p.X);
            UMax = local.Max(p => p.X);
            VMin = local.Min(p => p.Y);
            VMax = local.Max(p => p.Y);

            FirstU = origin == GridOriginMode.Centred ? CentredStart(UMin, UMax, LengthM) : UMin;
            FirstV = origin == GridOriginMode.Centred && !flushV ? CentredStart(VMin, VMax, WidthM) : VMin;
        }

        public Vec2 U { get; }
        public Vec2 V { get; }
        public double LengthM { get; }
        public double WidthM { get; }
        public double UMin { get; }
        public double UMax { get; }
        public double VMin { get; }
        public double VMax { get; }

        /// <summary>Local u of the grid line the column numbering is anchored on.</summary>
        public double FirstU { get; }

        /// <summary>Local v of the grid line the row numbering is anchored on.</summary>
        public double FirstV { get; }

        public static GridFrame Create(Polygon2D outlineCcw, Pattern2DSettings settings)
        {
            Vec2 u = settings.GridDirection switch
            {
                GridDirectionMode.ProjectX => Vec2.UnitX,
                GridDirectionMode.SelectedEdge => outlineCcw.Edge(settings.AlignEdgeIndex).Direction,
                _ => LongestEdgeDirection(outlineCcw)
            };
            return new GridFrame(u, settings.BoardLengthM, settings.BoardWidthM, outlineCcw, settings.GridOrigin);
        }

        /// <summary>
        /// Frame for one slope face: U along its floor edge, V pointing inward from that edge.
        /// Rows always start flush on the edge (v = edge line); columns follow <paramref name="origin"/>.
        /// </summary>
        public static GridFrame ForFace(Polygon2D face, Vec2 edgeDirection, Pattern2DSettings settings) =>
            new GridFrame(edgeDirection.Normalized(), settings.BoardLengthM, settings.BoardWidthM, face, settings.GridOrigin, flushV: true);

        /// <summary>Index of the longest edge; a tie goes to the first edge in CCW order.</summary>
        public static int LongestEdgeIndex(Polygon2D outlineCcw)
        {
            int best = 0;
            for (int i = 1; i < outlineCcw.Count; i++)
            {
                if (outlineCcw.Edge(i).Length > outlineCcw.Edge(best).Length + GeometryTolerance.Vertex)
                {
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Direction of the longest edge; a tie goes to the first edge in CCW order.</summary>
        public static Vec2 LongestEdgeDirection(Polygon2D outlineCcw) =>
            outlineCcw.Edge(LongestEdgeIndex(outlineCcw)).Direction;

        public Vec2 ToLocal(Vec2 world) => new Vec2(world.Dot(U), world.Dot(V));

        public Vec2 ToWorld(double u, double v) => U * u + V * v;

        /// <summary>u values of grid lines running across the board length (parallel to V).</summary>
        public IReadOnlyList<double> CrossLinePositions() => Positions(FirstU, LengthM, UMin, UMax);

        /// <summary>v values of grid lines running along the board length (parallel to U).</summary>
        public IReadOnlyList<double> AlongLinePositions() => Positions(FirstV, WidthM, VMin, VMax);

        /// <summary>Start u of every board column touching the outline's bounding box (index from 1).</summary>
        public IReadOnlyList<(int Index, double Start)> Columns() => Cells(FirstU, LengthM, UMin, UMax);

        /// <summary>Start v of every board row touching the outline's bounding box (index from 1).</summary>
        public IReadOnlyList<(int Index, double Start)> Rows() => Cells(FirstV, WidthM, VMin, VMax);

        private static double CentredStart(double min, double max, double step)
        {
            double span = max - min;
            double whole = Math.Floor(span / step + 1e-9) * step;
            double rest = span - whole;
            return rest < GeometryTolerance.Vertex ? min : min + rest / 2.0;
        }

        private static IReadOnlyList<double> Positions(double first, double step, double min, double max)
        {
            var list = new List<double>();
            int n0 = (int)Math.Ceiling((min - first) / step - 1e-9);
            for (int n = n0; ; n++)
            {
                double p = first + n * step;
                if (p > max + GeometryTolerance.Vertex)
                {
                    break;
                }

                list.Add(p);
            }

            return list;
        }

        private static IReadOnlyList<(int, double)> Cells(double first, double step, double min, double max)
        {
            var list = new List<(int, double)>();
            int n0 = (int)Math.Floor((min - first) / step + 1e-9);
            int index = 1;
            for (int n = n0; ; n++)
            {
                double start = first + n * step;
                if (start >= max - GeometryTolerance.Vertex)
                {
                    break;
                }

                list.Add((index++, start));
            }

            return list;
        }
    }
}
