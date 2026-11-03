using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Splits every segment at end points of other segments that lie on its interior,
    /// so overlapping pieces become identical and can be de-duplicated.
    /// </summary>
    public static class SegmentSplitter
    {
        public static IReadOnlyList<Segment2D> SplitAtEndpoints(IReadOnlyList<Segment2D> segments, double tolerance)
        {
            var endpoints = segments.SelectMany(s => new[] { s.Start, s.End }).ToList();
            var result = new List<Segment2D>();

            foreach (Segment2D seg in segments)
            {
                Vec2 d = seg.End - seg.Start;
                double len = seg.Length;
                if (len < tolerance)
                {
                    continue;
                }

                Vec2 dir = d / len;
                var cuts = new List<double>();
                foreach (Vec2 p in endpoints)
                {
                    double t = (p - seg.Start).Dot(dir);
                    if (t > tolerance && t < len - tolerance && seg.DistanceToLine(p) <= tolerance)
                    {
                        cuts.Add(t);
                    }
                }

                cuts.Sort();
                double prev = 0;
                foreach (double t in cuts)
                {
                    if (t - prev > tolerance)
                    {
                        result.Add(new Segment2D(seg.Start + dir * prev, seg.Start + dir * t));
                        prev = t;
                    }
                }

                result.Add(new Segment2D(seg.Start + dir * prev, seg.End));
            }

            return result;
        }
    }
}
