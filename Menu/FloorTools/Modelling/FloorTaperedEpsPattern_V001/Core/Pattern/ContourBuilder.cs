using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Layer C — thickness contours at h = tmin + n·Δ (n ≥ 1). The loop at h is the outline
    /// clipped by { Li ≥ (h − tmin)/k } for all i. The tmin level is the outline itself and
    /// is not drawn; loops below <see cref="GeometryTolerance.MinLoopArea"/> are skipped.
    /// </summary>
    public static class ContourBuilder
    {
        public const int MaxLoops = 2000;

        public static IReadOnlyList<ContourLoop> Build(ThicknessField thickness, double intervalMm, double maxThicknessMm)
        {
            var loops = new List<ContourLoop>();
            if (intervalMm <= 0 || thickness.K <= 0)
            {
                return loops;
            }

            for (int n = 1; n <= MaxLoops; n++)
            {
                double h = thickness.MinThicknessMm + n * intervalMm;
                if (h > maxThicknessMm + 1e-6)
                {
                    break;
                }

                double inset = thickness.DistanceForThickness(h);
                Polygon2D loop = ConvexClipper.ClipAll(thickness.Distances.Outline, thickness.Distances.InsetHalfPlanes(inset));
                if (loop.IsEmpty || loop.Area < GeometryTolerance.MinLoopArea)
                {
                    break; // level sets only shrink from here on
                }

                loops.Add(new ContourLoop(h, loop.ToCounterClockwise()));
            }

            return loops;
        }
    }
}
