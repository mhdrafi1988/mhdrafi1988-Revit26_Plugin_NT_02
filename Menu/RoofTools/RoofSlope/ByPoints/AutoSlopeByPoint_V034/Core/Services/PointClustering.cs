// =======================================================
// File: PointClustering.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services
// Moved out of V030's CircleMarkerService (pure logic, now testable).
// Change: clusters by XY distance (V032 D3) — markers are drawn in plan,
// so two points at different heights but the same plan spot are one mark.
// =======================================================

using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services
{
    public static class PointClustering
    {
        /// <summary>
        /// Merges points within <paramref name="toleranceFt"/> of each other
        /// (transitively, union-find) into one point at the centre of the
        /// group's XY bounding box. A point with nothing nearby is returned as is.
        /// </summary>
        public static List<Point3> ClusterXY(IEnumerable<Point3> points, double toleranceFt)
        {
            var valid = (points ?? Enumerable.Empty<Point3>()).ToList();
            if (valid.Count <= 1 || toleranceFt <= 0)
                return valid;

            int n = valid.Count;
            int[] parent = Enumerable.Range(0, n).ToArray();

            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }

            var grid = new SpatialGrid2D(valid, toleranceFt);
            for (int i = 0; i < n; i++)
            {
                foreach (int j in grid.QueryXY(valid[i], toleranceFt))
                {
                    if (j <= i) continue;
                    int ri = Find(i), rj = Find(j);
                    if (ri != rj) parent[ri] = rj;
                }
            }

            var groups = new Dictionary<int, List<Point3>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!groups.TryGetValue(root, out var list))
                    groups[root] = list = new List<Point3>();
                list.Add(valid[i]);
            }

            var centres = new List<Point3>(groups.Count);
            foreach (var g in groups.Values)
            {
                double minX = g.Min(p => p.X), maxX = g.Max(p => p.X);
                double minY = g.Min(p => p.Y), maxY = g.Max(p => p.Y);
                centres.Add(new Point3((minX + maxX) / 2.0, (minY + maxY) / 2.0, g.Average(p => p.Z)));
            }
            return centres;
        }
    }
}
