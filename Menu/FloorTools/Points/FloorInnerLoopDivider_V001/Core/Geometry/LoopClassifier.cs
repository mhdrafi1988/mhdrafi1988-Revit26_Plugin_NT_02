// =======================================================
// File: LoopClassifier.cs
// Location: Core/Geometry/
// New in V012 — replaces FloorGeometryService.ClassifyLoopShape (V011),
// which needed ≥ 3 arcs for a circle (Revit usually returns two half
// arcs), measured arc sweep with AngleTo (wrong above 180°), and saw a
// rectangle with a split side as "Other".
// Works on the plan geometry only:
//   Circular  — no sharp corners and every point the same distance from
//               the centroid (±1 %). Also catches polygons with many
//               short sides (turns under 20° are not corners).
//   Rectangle — exactly 4 sharp corners, ~90° each, straight sides
//               (collinear pieces of one side are fine).
//   Other     — anything else.
// =======================================================

using System;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Geometry
{
    public enum LoopShape { Circular, Rectangle, Other }

    public sealed class LoopShapeInfo
    {
        public LoopShape Shape { get; init; }

        /// <summary>Circular: diameter. Rectangle: shorter side. Other: bounding-box width.</summary>
        public double SizeA { get; init; }

        /// <summary>Rectangle: longer side. Other: bounding-box depth. Circular: same as SizeA.</summary>
        public double SizeB { get; init; }

        /// <summary>V011-compatible name: "Circular", "Rectangle" or "Other".</summary>
        public string Name => Shape.ToString();
    }

    public static class LoopClassifier
    {
        public const double RadiusTolerance = 0.01;
        public const double RightAngleToleranceDeg = 5.0;

        public static LoopShapeInfo Classify(Loop2D loop)
        {
            if (loop == null) throw new ArgumentNullException(nameof(loop));

            if (loop.CornerSegmentIndices.Count == 0)
            {
                P2 c = loop.Centroid;
                var d = loop.Samples.Select(p => p.DistanceTo(c)).ToList();
                double mean = d.Average();
                if (mean > 1e-9 && d.All(r => Math.Abs(r - mean) / mean <= RadiusTolerance))
                    return new LoopShapeInfo { Shape = LoopShape.Circular, SizeA = 2 * mean, SizeB = 2 * mean };
            }

            if (loop.CornerSegmentIndices.Count == 4 &&
                loop.Segments.All(s => s.IsStraight) &&
                loop.CornerSegmentIndices.All(i => Math.Abs(loop.TurnAngleDeg(i) - 90) <= RightAngleToleranceDeg))
            {
                double[] sides = SideLengths(loop);
                double a = Math.Min(sides[0], sides[1]);
                double b = Math.Max(sides[0], sides[1]);
                return new LoopShapeInfo { Shape = LoopShape.Rectangle, SizeA = a, SizeB = b };
            }

            (P2 min, P2 max) = loop.Bounds;
            return new LoopShapeInfo { Shape = LoopShape.Other, SizeA = max.X - min.X, SizeB = max.Y - min.Y };
        }

        /// <summary>Lengths between consecutive corners (one entry per side).</summary>
        public static double[] SideLengths(Loop2D loop)
        {
            var corners = loop.CornerSegmentIndices;
            if (corners.Count == 0) return new[] { loop.Perimeter };
            var sides = new double[corners.Count];
            for (int k = 0; k < corners.Count; k++)
            {
                double s0 = loop.CornerArcLength(corners[k]);
                double s1 = k + 1 < corners.Count ? loop.CornerArcLength(corners[k + 1]) : loop.Perimeter + loop.CornerArcLength(corners[0]);
                sides[k] = s1 - s0;
            }

            return sides;
        }
    }
}
