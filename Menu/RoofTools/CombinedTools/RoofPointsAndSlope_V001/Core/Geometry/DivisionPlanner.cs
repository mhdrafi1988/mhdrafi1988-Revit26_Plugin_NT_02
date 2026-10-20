// =======================================================
// File: DivisionPlanner.cs
// Location: Core/Geometry/
// Ported unchanged from Inner Loop Divider V012 (namespace only).
//
// Rules — points are per LOOP:
//   Smooth loop (circle):  N points evenly spaced by length around the
//                          whole loop, starting at the east-most point
//                          (so the result doesn't depend on sketch order).
//   Loop with corners:     corners are kept (they are part of N); the
//                          remaining N − corners points are shared out
//                          over the sides in proportion to side length
//                          and spaced evenly along each side.
//   Spacing mode:          smooth → max(3, ⌈perimeter ÷ spacing⌉) points;
//                          cornered → each side divided so no gap is
//                          longer than the spacing.
// Planned points within the tolerance of an existing shape point are
// marked AlreadyExists and are not added again.
// =======================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry
{
    /// <summary>Count = N points around each loop; Spacing = no gap longer than a distance.</summary>
    public enum DivisionMode { Count, Spacing }

    /// <summary>One point the planner wants on a loop.</summary>
    public sealed class PlannedPoint
    {
        public P2 Position { get; init; }
        public int Segment { get; init; }
        public double Fraction { get; init; }
        public bool IsCorner { get; init; }
        public bool AlreadyExists { get; init; }
    }

    /// <summary>All planned points for one loop.</summary>
    public sealed class DivisionPlan
    {
        public static DivisionPlan Empty { get; } = new DivisionPlan { Points = Array.Empty<PlannedPoint>() };

        public IReadOnlyList<PlannedPoint> Points { get; init; } = Array.Empty<PlannedPoint>();
        public int TotalPoints => Points.Count;
        public int NewPoints => Points.Count(p => !p.AlreadyExists);
        public int ExistingPoints => Points.Count(p => p.AlreadyExists);

        /// <summary>Average gap between points along the loop (0 when there are no points).</summary>
        public double AverageSpacing { get; init; }
    }

    /// <summary>Plans the points around one opening loop (rules in the file header).</summary>
    public static class DivisionPlanner
    {
        public const int MinSmoothPointsInSpacingMode = 3;

        public static DivisionPlan Plan(Loop2D loop, DivisionMode mode, int count, double spacing,
                                        IReadOnlyList<P2> existingPoints, double tolerance)
        {
            if (loop == null || loop.Perimeter <= tolerance) return DivisionPlan.Empty;
            if (mode == DivisionMode.Count && count <= 0) return DivisionPlan.Empty;
            if (mode == DivisionMode.Spacing && spacing <= tolerance) return DivisionPlan.Empty;

            double p = loop.Perimeter;
            var positions = new List<(double S, bool Corner)>();
            var corners = loop.CornerSegmentIndices;

            if (corners.Count == 0)
            {
                int n = mode == DivisionMode.Count
                    ? count
                    : Math.Max(MinSmoothPointsInSpacingMode, (int)Math.Ceiling(p / spacing - 1e-9));
                double s0 = loop.EastMostArcLength();
                for (int k = 0; k < n; k++)
                    positions.Add((s0 + k * p / n, false));
            }
            else
            {
                double[] sides = LoopClassifier.SideLengths(loop);
                int[] interior = mode == DivisionMode.Count
                    ? ShareOut(Math.Max(0, count - corners.Count), sides)
                    : sides.Select(l => Math.Max(0, (int)Math.Ceiling(l / spacing - 1e-9) - 1)).ToArray();

                for (int k = 0; k < corners.Count; k++)
                {
                    double sc = loop.CornerArcLength(corners[k]);
                    positions.Add((sc, true));
                    for (int j = 1; j <= interior[k]; j++)
                        positions.Add((sc + sides[k] * j / (interior[k] + 1), false));
                }
            }

            var existing = existingPoints ?? Array.Empty<P2>();
            var planned = new List<PlannedPoint>();
            foreach ((double s, bool corner) in positions)
            {
                (int seg, double f) = loop.Locate(s);
                P2 pos = loop.Segments[seg].PointAt(f);
                if (planned.Any(q => q.Position.DistanceTo(pos) <= tolerance))
                    continue; // two planned points on top of each other

                planned.Add(new PlannedPoint
                {
                    Position = pos,
                    Segment = seg,
                    Fraction = f,
                    IsCorner = corner,
                    AlreadyExists = existing.Any(e => e.DistanceTo(pos) <= tolerance)
                });
            }

            return new DivisionPlan
            {
                Points = planned,
                AverageSpacing = planned.Count > 0 ? p / planned.Count : 0
            };
        }

        /// <summary>Largest-remainder share of <paramref name="extra"/> points over sides by length.</summary>
        public static int[] ShareOut(int extra, double[] sides)
        {
            var result = new int[sides.Length];
            double total = sides.Sum();
            if (extra <= 0 || total <= 0) return result;

            var remainders = new List<(int Index, double Rem)>();
            int given = 0;
            for (int i = 0; i < sides.Length; i++)
            {
                double exact = extra * sides[i] / total;
                result[i] = (int)Math.Floor(exact);
                given += result[i];
                remainders.Add((i, exact - result[i]));
            }

            foreach (var r in remainders.OrderByDescending(r => r.Rem).ThenByDescending(r => sides[r.Index]).ThenBy(r => r.Index))
            {
                if (given >= extra) break;
                result[r.Index]++;
                given++;
            }

            return result;
        }
    }
}
