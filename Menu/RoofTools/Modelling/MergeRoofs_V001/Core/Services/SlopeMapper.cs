// Merge Roofs V001 — gives each merged edge the slope of the source edge it came from.
using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models;

namespace Revit26_Plugin_Revised.MergeRoofs.V001.Core.Services
{
    /// <summary>Copies slope settings from source footprint edges onto merged edges.</summary>
    public static class SlopeMapper
    {
        /// <summary>
        /// For each merged edge finds the source edge whose line passes through the merged edge's midpoint
        /// (within max(tolerance, 0.001 ft)) and copies its slope; edges with no match stay flat.
        /// </summary>
        public static IReadOnlyList<Edge> Apply(IReadOnlyList<Edge> merged, IReadOnlyList<RoofFootprint> sources, double toleranceFeet)
        {
            double tol = Math.Max(toleranceFeet, 0.001);
            var candidates = new List<(Edge Edge, List<Pt> Pts)>();
            foreach (var s in sources)
            {
                foreach (var loop in Loops(s))
                    foreach (var e in loop.Edges)
                    {
                        if (!e.DefinesSlope) continue;
                        var pts = ArcGeometry.Flatten(e);
                        pts.Add(e.B);
                        candidates.Add((e, pts));
                    }
            }

            var result = new List<Edge>(merged.Count);
            foreach (var m in merged)
            {
                var mid = new Pt((m.A.X + m.B.X) / 2, (m.A.Y + m.B.Y) / 2);
                Edge? best = null;
                double bestD = tol;
                foreach (var (edge, pts) in candidates)
                    for (int i = 0; i + 1 < pts.Count; i++)
                    {
                        double d = ArcGeometry.DistToSegment(mid, pts[i], pts[i + 1]);
                        if (d <= bestD) { bestD = d; best = edge; }
                    }
                result.Add(best is null ? m : new Edge { A = m.A, B = m.B, ArcMid = m.ArcMid, DefinesSlope = true, SlopeAngle = best.SlopeAngle });
            }
            return result;
        }

        private static IEnumerable<Loop> Loops(RoofFootprint f)
        {
            yield return f.Outer;
            foreach (var h in f.Holes) yield return h;
        }
    }
}
