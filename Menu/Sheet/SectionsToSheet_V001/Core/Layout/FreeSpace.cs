using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout
{
    /// <summary>
    /// Finds the largest empty rectangle in an area that holds some obstacles — the space on an
    /// existing sheet the new sections can use. Pure math, no Revit types.
    /// </summary>
    public static class FreeSpace
    {
        private const double Eps = 1e-9;

        /// <summary>
        /// The largest axis-aligned rectangle inside <paramref name="area"/> that overlaps none of
        /// <paramref name="obstacles"/> (grow them first to keep a gap). Ties go to the higher one,
        /// then the one further left. Null when no empty rectangle is left.
        /// </summary>
        public static LayoutRect? Largest(LayoutRect area, IEnumerable<LayoutRect> obstacles)
        {
            if (!area.IsValid)
                return null;

            var blocks = (obstacles ?? Enumerable.Empty<LayoutRect>())
                .Select(o => o.Intersect(area))
                .Where(o => o.HasValue).Select(o => o.Value)
                .ToList();
            if (blocks.Count == 0)
                return area;

            // A maximal empty rectangle has each side on the area's edge or an obstacle's edge.
            var lefts = blocks.Select(b => b.MaxX).Append(area.MinX).Distinct().OrderBy(x => x).ToList();
            var rights = blocks.Select(b => b.MinX).Append(area.MaxX).Distinct().OrderBy(x => x).ToList();

            LayoutRect? best = null;
            foreach (double x1 in lefts)
            {
                foreach (double x2 in rights)
                {
                    if (x2 - x1 <= Eps)
                        continue;

                    // Obstacles across this column, as y-intervals, bottom first.
                    var spans = blocks.Where(b => b.MinX < x2 - Eps && b.MaxX > x1 + Eps)
                        .Select(b => (b.MinY, b.MaxY)).OrderBy(s => s.MinY).ToList();

                    double floor = area.MinY;
                    foreach (var (lo, hi) in spans.Append((area.MaxY, area.MaxY)))
                    {
                        if (lo - floor > Eps)
                            best = Better(best, new LayoutRect(x1, floor, x2, Math.Min(lo, area.MaxY)));
                        floor = Math.Max(floor, hi);
                        if (floor >= area.MaxY - Eps)
                            break;
                    }
                }
            }
            return best;
        }

        private static LayoutRect Better(LayoutRect? current, LayoutRect candidate)
        {
            if (current is not LayoutRect c)
                return candidate;
            double a = candidate.Area, b = c.Area;
            if (Math.Abs(a - b) > 1e-9 * Math.Max(1, b))
                return a > b ? candidate : c;
            if (Math.Abs(candidate.MaxY - c.MaxY) > Eps)
                return candidate.MaxY > c.MaxY ? candidate : c;
            return candidate.MinX < c.MinX - Eps ? candidate : c;
        }
    }
}
