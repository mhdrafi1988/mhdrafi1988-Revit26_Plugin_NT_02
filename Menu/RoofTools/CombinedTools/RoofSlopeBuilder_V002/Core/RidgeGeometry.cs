// =======================================================
// File: RidgeGeometry.cs
// Namespace: Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Core
// New in V002. Turns the solver's ridge segments into drawable ridge lines
// (segments joined end to end) and ridge points (sampled along each line).
// Pure geometry: no Revit references.
// =======================================================
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Core
{
    /// <summary>Joins ridge segments into polylines and samples points along them.</summary>
    public static class RidgeGeometry
    {
        /// <summary>
        /// Joins segments whose ends meet (within <paramref name="tol"/>) into polylines. A chain stops at a
        /// junction where three or more segments meet, so every junction is a polyline end. Collinear
        /// inner points are dropped.
        /// </summary>
        public static List<List<P2>> Join(IEnumerable<P2[]> segments, double tol)
        {
            List<P2[]> segs = segments.Where(s => s[0].DistanceTo(s[1]) > tol).ToList();
            // snap ends to shared nodes
            List<P2> nodes = new List<P2>();
            Func<P2, int> node = p =>
            {
                for (int i = 0; i < nodes.Count; i++) if (nodes[i].DistanceTo(p) <= tol) return i;
                nodes.Add(p); return nodes.Count - 1;
            };
            List<int[]> edges = new List<int[]>();
            HashSet<long> seen = new HashSet<long>();
            foreach (P2[] s in segs)
            {
                int a = node(s[0]), b = node(s[1]);
                if (a == b) continue;
                long key = (long)Math.Min(a, b) * 1000003 + Math.Max(a, b);
                if (seen.Add(key)) edges.Add(new[] { a, b });
            }

            Dictionary<int, List<int>> byNode = new Dictionary<int, List<int>>();
            for (int e = 0; e < edges.Count; e++)
                foreach (int n in edges[e])
                {
                    if (!byNode.TryGetValue(n, out List<int> l)) byNode[n] = l = new List<int>();
                    l.Add(e);
                }

            bool[] used = new bool[edges.Count];
            List<List<P2>> chains = new List<List<P2>>();
            // start at ends and junctions first, then whatever is left (closed loops)
            IEnumerable<int> starts = byNode.Keys.Where(n => byNode[n].Count != 2).Concat(byNode.Keys);
            foreach (int start in starts)
                foreach (int e0 in byNode[start])
                {
                    if (used[e0]) continue;
                    List<int> chain = new List<int> { start };
                    int cur = start, e = e0;
                    while (true)
                    {
                        used[e] = true;
                        int next = edges[e][0] == cur ? edges[e][1] : edges[e][0];
                        chain.Add(next);
                        cur = next;
                        if (byNode[cur].Count != 2) break;
                        int nextEdge = byNode[cur].FirstOrDefault(x => !used[x]);
                        if (byNode[cur].All(x => used[x])) break;
                        e = nextEdge;
                    }
                    chains.Add(Simplify(chain.Select(i => nodes[i]).ToList(), tol));
                }
            return chains;
        }

        /// <summary>Drops inner points that lie on the straight line between their neighbours.</summary>
        private static List<P2> Simplify(List<P2> pts, double tol)
        {
            if (pts.Count < 3) return pts;
            List<P2> outP = new List<P2> { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
                if (Geo2D.DistToSegment(pts[i], outP[outP.Count - 1], pts[i + 1]) > tol * 0.1) outP.Add(pts[i]);
            outP.Add(pts[pts.Count - 1]);
            return outP;
        }

        /// <summary>
        /// Points along every polyline: both ends, every inner vertex, and evenly spaced points no farther
        /// apart than <paramref name="spacing"/>. Points closer than <paramref name="tol"/> are merged.
        /// </summary>
        public static List<P2> Sample(IEnumerable<List<P2>> lines, double spacing, double tol)
        {
            List<P2> pts = new List<P2>();
            Action<P2> add = p => { if (!pts.Any(q => q.DistanceTo(p) < tol)) pts.Add(p); };
            spacing = Math.Max(spacing, tol);
            foreach (List<P2> line in lines)
            {
                if (line.Count == 0) continue;
                add(line[0]);
                for (int i = 1; i < line.Count; i++)
                {
                    P2 a = line[i - 1], b = line[i];
                    int n = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b) / spacing));
                    for (int k = 1; k <= n; k++) add(a + (b - a) * (k / (double)n));
                }
            }
            return pts;
        }
    }
}
