using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofTag.V018.Helpers
{
    /// <summary>
    /// Rule 1: suppress tags on points that share elevation (fixed 5 mm tolerance)
    /// and lie within a user-configurable horizontal radius of another point in
    /// the same group — e.g. several shape-editor vertices clustered around one
    /// drain. Groups are transitive (union-find), so a chain of nearby points
    /// collapses into a single group even if the two ends exceed the radius.
    /// Only the first point per group (by original input order) is kept.
    /// </summary>
    public static class RoofTagClusterHelper
    {
        private const double ZTolFt = 5.0 / 304.8; // fixed 5 mm, not exposed in UI

        /// <summary>
        /// Filters points down to one representative per cluster.
        /// </summary>
        /// <param name="points">Input points, in original order.</param>
        /// <param name="radiusFt">Horizontal cluster radius, in feet.</param>
        /// <param name="skipped">Points dropped because another point in their group was kept.</param>
        public static List<XYZ> Filter(List<XYZ> points, double radiusFt, out List<XYZ> skipped)
        {
            skipped = new List<XYZ>();
            int n = points.Count;
            if (n <= 1)
                return new List<XYZ>(points);

            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }

            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[rb] = ra;
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double dz = Math.Abs(points[i].Z - points[j].Z);
                    if (dz > ZTolFt) continue;

                    double dx = points[i].X - points[j].X;
                    double dy = points[i].Y - points[j].Y;
                    double horizDist = Math.Sqrt(dx * dx + dy * dy);

                    if (horizDist <= radiusFt)
                        Union(i, j);
                }
            }

            var groupRepresentative = new Dictionary<int, int>();
            var kept = new List<XYZ>();

            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!groupRepresentative.ContainsKey(root))
                {
                    groupRepresentative[root] = i;
                    kept.Add(points[i]);
                }
                else
                {
                    skipped.Add(points[i]);
                }
            }

            return kept;
        }
    }
}
