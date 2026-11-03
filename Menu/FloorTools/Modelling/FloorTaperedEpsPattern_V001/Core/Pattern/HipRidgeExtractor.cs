using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Layer B — hips and ridges: slope-face edges that are not on the perimeter,
    /// de-duplicated (each is shared by two faces) and merged where collinear.
    /// </summary>
    public static class HipRidgeExtractor
    {
        public static IReadOnlyList<Segment2D> Extract(Polygon2D outlineCcw, IReadOnlyList<SlopeFace> faces, double minLengthM)
        {
            double tol = GeometryTolerance.Merge;

            List<Segment2D> interior = faces
                .SelectMany(f => f.Polygon.Edges)
                .Where(e => e.Length >= tol && !outlineCcw.IsOnBoundaryLine(e, tol))
                .ToList();

            IReadOnlyList<Segment2D> split = SegmentSplitter.SplitAtEndpoints(interior, tol);
            IReadOnlyList<Segment2D> unique = SegmentDeduplicator.Distinct(split, tol);
            IReadOnlyList<Segment2D> merged = SegmentMerger.MergeCollinear(unique, tol);

            return merged.Where(s => s.Length >= minLengthM).ToList();
        }
    }
}
