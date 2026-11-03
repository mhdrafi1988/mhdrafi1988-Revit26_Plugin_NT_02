using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Layer A — continuous board grid lines: each infinite grid line is clipped to the
    /// convex outline and becomes ONE segment. Lines lying on the outline are dropped.
    /// </summary>
    public static class GridLineBuilder
    {
        public static IReadOnlyList<Segment2D> Build(Polygon2D outlineCcw, GridFrame frame, double minLengthM)
        {
            var result = new List<Segment2D>();

            foreach (double u in frame.CrossLinePositions())
            {
                Add(result, outlineCcw, frame.ToWorld(u, 0), frame.V, minLengthM);
            }

            foreach (double v in frame.AlongLinePositions())
            {
                Add(result, outlineCcw, frame.ToWorld(0, v), frame.U, minLengthM);
            }

            return result;
        }

        private static void Add(List<Segment2D> result, Polygon2D outline, Vec2 origin, Vec2 direction, double minLengthM)
        {
            Segment2D? clipped = ConvexClipper.ClipLine(outline, origin, direction);
            if (clipped is not Segment2D seg || seg.Length < minLengthM)
            {
                return;
            }

            if (outline.IsOnBoundaryLine(seg, GeometryTolerance.Merge))
            {
                return;
            }

            result.Add(seg);
        }
    }
}
