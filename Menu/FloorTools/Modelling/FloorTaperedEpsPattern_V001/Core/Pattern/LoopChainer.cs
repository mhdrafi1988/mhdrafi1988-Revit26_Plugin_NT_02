using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Orders sketch segments head-to-tail into one closed vertex loop.
    /// Segments may arrive in any order and direction.
    /// </summary>
    public static class LoopChainer
    {
        /// <returns>The loop vertices, or null when the segments do not form one closed loop.</returns>
        public static IReadOnlyList<Vec2>? Chain(IReadOnlyList<Segment2D> segments, double tolerance)
        {
            if (segments.Count < 3)
            {
                return null;
            }

            var remaining = new List<Segment2D>(segments);
            var vertices = new List<Vec2> { remaining[0].Start };
            Vec2 current = remaining[0].End;
            Vec2 first = remaining[0].Start;
            remaining.RemoveAt(0);

            while (remaining.Count > 0)
            {
                int found = -1;
                bool reversed = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    if (remaining[i].Start.AlmostEquals(current, tolerance))
                    {
                        found = i;
                        break;
                    }

                    if (remaining[i].End.AlmostEquals(current, tolerance))
                    {
                        found = i;
                        reversed = true;
                        break;
                    }
                }

                if (found < 0)
                {
                    return null;
                }

                vertices.Add(current);
                current = reversed ? remaining[found].Start : remaining[found].End;
                remaining.RemoveAt(found);
            }

            return current.AlmostEquals(first, tolerance) ? vertices : null;
        }
    }
}
