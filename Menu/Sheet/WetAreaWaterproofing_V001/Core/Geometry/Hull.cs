using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry
{
    /// <summary>Convex hull, the last-resort approximate outline.</summary>
    public static class Hull
    {
        /// <summary>Counter-clockwise convex hull of <paramref name="points"/> (monotone chain); empty for fewer than 3 distinct points.</summary>
        public static IReadOnlyList<Pt> Convex(IEnumerable<Pt> points)
        {
            var pts = (points ?? Enumerable.Empty<Pt>()).Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (pts.Count < 3) return new List<Pt>();

            var hull = new List<Pt>();
            foreach (Pt p in pts)
            {
                while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            int lower = hull.Count + 1;
            for (int i = pts.Count - 2; i >= 0; i--)
            {
                Pt p = pts[i];
                while (hull.Count >= lower && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull.Count >= 3 ? hull : new List<Pt>();
        }

        private static double Cross(Pt o, Pt a, Pt b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
    }
}
