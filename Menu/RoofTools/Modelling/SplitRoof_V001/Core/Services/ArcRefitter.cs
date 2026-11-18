// Split Roof V001 — turns runs of flattened segments back into the arcs they came from.
using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.SplitRoof.V001.Core.Models;

namespace Revit26_Plugin_Revised.SplitRoof.V001.Core.Services
{
    /// <summary>Re-fits arcs onto a loop that was built from flattened geometry.</summary>
    public static class ArcRefitter
    {
        /// <summary>
        /// Replaces each run of at least two consecutive segments that lie on one source arc (within the tolerance)
        /// with a single arc edge. Returns <paramref name="flattened"/> itself when nothing matches.
        /// </summary>
        public static Loop Refit(Loop flattened, IReadOnlyList<RoofFootprint> sources, double toleranceFeet)
        {
            double tol = Math.Max(toleranceFeet, 0.001);
            var arcs = CollectArcs(sources);
            if (arcs.Count == 0) return flattened;

            var edges = flattened.Edges;
            int n = edges.Count;
            var owner = new int[n];                       // index into arcs, or -1
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                owner[i] = -1;
                var mid = new Pt((edges[i].A.X + edges[i].B.X) / 2, (edges[i].A.Y + edges[i].B.Y) / 2);
                for (int k = 0; k < arcs.Count && owner[i] < 0; k++)
                    if (Near(arcs[k].Poly, edges[i].A, tol) && Near(arcs[k].Poly, edges[i].B, tol) && Near(arcs[k].Poly, mid, tol))
                    { owner[i] = k; any = true; }
            }
            if (!any) return flattened;

            int start = Array.FindIndex(owner, o => o < 0);
            if (start < 0) return flattened;               // full circle: leave as segments

            var result = new List<Edge>();
            int idx = 0;
            while (idx < n)
            {
                int i = (start + idx) % n;
                if (owner[i] < 0) { result.Add(edges[i]); idx++; continue; }
                int k = owner[i];
                var run = new List<Edge>();
                while (idx < n && owner[(start + idx) % n] == k) { run.Add(edges[(start + idx) % n]); idx++; }
                if (run.Count < 2) { result.AddRange(run); continue; }
                var midVertex = run[run.Count / 2].A;
                var (c, r) = arcs[k].Circle;
                double dx = midVertex.X - c.X, dy = midVertex.Y - c.Y, len = Math.Sqrt(dx * dx + dy * dy);
                var arcMid = new Pt(c.X + dx / len * r, c.Y + dy / len * r);
                var first = run[0]; var last = run[run.Count - 1];
                result.Add(new Edge { A = first.A, B = last.B, ArcMid = arcMid, DefinesSlope = first.DefinesSlope, SlopeAngle = first.SlopeAngle });
            }
            return new Loop(result);
        }

        private static List<(List<Pt> Poly, (Pt, double) Circle)> CollectArcs(IReadOnlyList<RoofFootprint> sources)
        {
            var list = new List<(List<Pt>, (Pt, double))>();
            foreach (var s in sources)
            {
                var loops = new List<Loop> { s.Outer };
                loops.AddRange(s.Holes);
                foreach (var loop in loops)
                    foreach (var e in loop.Edges)
                    {
                        if (e.ArcMid is not Pt m) continue;
                        var circ = ArcGeometry.Circle(e.A, m, e.B);
                        if (circ is null) continue;
                        var poly = ArcGeometry.Flatten(e);
                        poly.Add(e.B);
                        list.Add((poly, circ.Value));
                    }
            }
            return list;
        }

        private static bool Near(List<Pt> poly, Pt p, double tol)
        {
            for (int i = 0; i + 1 < poly.Count; i++)
                if (ArcGeometry.DistToSegment(p, poly[i], poly[i + 1]) <= tol) return true;
            return false;
        }
    }
}
