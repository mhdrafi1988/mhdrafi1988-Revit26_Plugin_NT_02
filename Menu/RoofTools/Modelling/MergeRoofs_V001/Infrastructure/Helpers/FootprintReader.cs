// Merge Roofs V001 — reads a footprint roof's profile loops (with per-edge slope) into the Revit-free model.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models;
using Edge = Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models.Edge;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Services;

namespace Revit26_Plugin_Revised.MergeRoofs.V001.Infrastructure.Helpers
{
    /// <summary>Reads roof footprints from Revit.</summary>
    public static class FootprintReader
    {
        /// <summary>
        /// Reads the roof's loops: the loop with the largest area is the outer boundary, the others are holes.
        /// Throws <see cref="InvalidOperationException"/> for curves other than lines and arcs.
        /// </summary>
        public static RoofFootprint Read(FootPrintRoof roof)
        {
            var loops = new List<Loop>();
            foreach (ModelCurveArray profile in roof.GetProfiles())
            {
                var edges = new List<Edge>();
                foreach (ModelCurve mc in profile)
                {
                    Curve c = mc.GeometryCurve;
                    XYZ p0 = c.GetEndPoint(0), p1 = c.GetEndPoint(1);
                    Pt? mid = null;
                    if (c is Arc arc) { XYZ m = arc.Evaluate(0.5, true); mid = new Pt(m.X, m.Y); }
                    else if (c is not Line)
                        throw new InvalidOperationException($"Roof {roof.Id} has a {c.GetType().Name} edge; only lines and arcs are supported.");
                    bool slope = roof.get_DefinesSlope(mc);
                    edges.Add(new Edge
                    {
                        A = new Pt(p0.X, p0.Y), B = new Pt(p1.X, p1.Y), ArcMid = mid,
                        DefinesSlope = slope, SlopeAngle = slope ? roof.get_SlopeAngle(mc) : 0,
                    });
                }
                loops.Add(new Loop(Chain(edges)));
            }
            if (loops.Count == 0) throw new InvalidOperationException($"Roof {roof.Id} has no footprint.");
            Loop outer = loops.OrderByDescending(l => Math.Abs(Area(l))).First();
            return new RoofFootprint(roof.Id.ToString(), outer, loops.Where(l => !ReferenceEquals(l, outer)).ToList());
        }

        /// <summary>Orients consecutive edges so each starts where the previous one ended.</summary>
        private static List<Edge> Chain(List<Edge> edges)
        {
            if (edges.Count < 2) return edges;
            var result = new List<Edge>(edges.Count);
            Edge first = edges[0];
            if (Dist(first.B, edges[1].A) > 1e-6 && Dist(first.B, edges[1].B) > 1e-6) first = Flip(first);
            result.Add(first);
            for (int i = 1; i < edges.Count; i++)
            {
                Edge e = edges[i];
                result.Add(Dist(result[i - 1].B, e.A) <= Dist(result[i - 1].B, e.B) ? e : Flip(e));
            }
            return result;
        }

        private static Edge Flip(Edge e) => new() { A = e.B, B = e.A, ArcMid = e.ArcMid, DefinesSlope = e.DefinesSlope, SlopeAngle = e.SlopeAngle };
        private static double Dist(Pt a, Pt b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static double Area(Loop l)
        {
            var r = ArcGeometry.Ring(l);
            double s = 0;
            for (int i = 0; i < r.Count; i++) { var p = r[i]; var q = r[(i + 1) % r.Count]; s += p.X * q.Y - q.X * p.Y; }
            return s / 2;
        }
    }
}
