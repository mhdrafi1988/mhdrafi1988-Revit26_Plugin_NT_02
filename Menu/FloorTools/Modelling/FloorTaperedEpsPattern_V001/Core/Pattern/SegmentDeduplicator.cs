using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Removes segments whose end points match an earlier segment (either direction).</summary>
    public static class SegmentDeduplicator
    {
        public static IReadOnlyList<Segment2D> Distinct(IEnumerable<Segment2D> segments, double tolerance)
        {
            var result = new List<Segment2D>();
            foreach (Segment2D seg in segments)
            {
                bool seen = false;
                foreach (Segment2D kept in result)
                {
                    if (kept.SameAs(seg, tolerance))
                    {
                        seen = true;
                        break;
                    }
                }

                if (!seen)
                {
                    result.Add(seg);
                }
            }

            return result;
        }
    }
}
