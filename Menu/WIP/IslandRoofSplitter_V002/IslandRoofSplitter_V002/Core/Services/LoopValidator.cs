// IslandRoofSplitter V002 — cleans a traced loop and rejects ones Revit would refuse, BEFORE touching Revit.
// Rejections become Status = Skipped (not Failed), per spec.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Core.Services
{
    public static class LoopValidator
    {
        /// <summary>Smallest region worth a roof: 10 mm × 10 mm.</summary>
        public static readonly double MinAreaSqFt = LengthUnits.MmToFt(10) * LengthUnits.MmToFt(10);

        /// <param name="shortCurveTolFt">Revit's Application.ShortCurveTolerance (~0.8 mm).</param>
        /// <returns>Cleaned vertices, or null with <paramref name="reason"/> set.</returns>
        public static List<LoopVertex>? Clean(TracedLoop loop, double shortCurveTolFt, out string reason)
        {
            reason = "";
            var dup = loop.VertexIds.GroupBy(v => v).FirstOrDefault(g => g.Count() > 1);
            if (dup != null)
            {
                var p = loop.Vertices[loop.VertexIds.IndexOf(dup.Key)].P;
                reason = $"Degenerate: loop touches itself at {Fmt(p)}";
                return null;
            }

            var v = new List<LoopVertex>(loop.Vertices);
            // a slightly larger merge distance than Revit's limit keeps every sketch curve safely above it
            double mergeTol = shortCurveTolFt * 1.05;
            bool changed = true;
            while (changed && v.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < v.Count && v.Count >= 3; i++)
                {
                    int j = (i + 1) % v.Count;
                    if (v[i].P.DistanceTo(v[j].P) < mergeTol)
                    {
                        RemoveAt(v, j);
                        changed = true;
                        break;
                    }
                }
                if (changed) continue;
                for (int j = 0; j < v.Count && v.Count >= 3; j++)
                {
                    int i = (j - 1 + v.Count) % v.Count, k = (j + 1) % v.Count;
                    if (v[i].ArcIndex >= 0 || v[j].ArcIndex >= 0) continue;
                    var d1 = v[j].P - v[i].P;
                    var d2 = v[k].P - v[j].P;
                    double cross = Math.Abs(Pt2.Cross(d1, d2)) / (d1.Length * d2.Length);
                    if (cross < 1e-9 && Pt2.Dot(d1, d2) > 0)
                    {
                        RemoveAt(v, j);
                        changed = true;
                        break;
                    }
                }
            }

            if (v.Count < 3) { reason = "Degenerate: fewer than 3 vertices after cleanup"; return null; }

            var pts = v.Select(x => x.P).ToList();
            if (Geometry2D.IsSelfIntersecting(pts, shortCurveTolFt * 0.5, out var at))
            {
                reason = $"Self-intersecting at {Fmt(at)}";
                return null;
            }

            double area = Math.Abs(Geometry2D.SignedArea(pts));
            if (area < MinAreaSqFt) { reason = $"Degenerate: area {area * 92903.04:0.#} mm² is below 10 mm × 10 mm"; return null; }
            return v;
        }

        private static void RemoveAt(List<LoopVertex> v, int j)
        {
            int i = (j - 1 + v.Count) % v.Count;
            var prev = v[i];
            if (prev.ArcIndex != v[j].ArcIndex) prev.ArcIndex = -1; // merged edge spans two curves → straight
            v[i] = prev;
            v.RemoveAt(j);
        }

        internal static string Fmt(Pt2 p) => $"({LengthUnits.FtToMm(p.X):0} mm, {LengthUnits.FtToMm(p.Y):0} mm)";
    }

    /// <summary>Turns a cleaned vertex loop into the edges that will be sketched.</summary>
    public static class ArcReassembler
    {
        public static List<LoopEdge> Build(List<LoopVertex> v, IReadOnlyList<ArcInfo> arcs, bool keepTrueArcs,
                                           double shortCurveTolFt)
        {
            int n = v.Count;
            var edges = new List<LoopEdge>();
            if (!keepTrueArcs || v.All(x => x.ArcIndex < 0))
            {
                for (int i = 0; i < n; i++) edges.Add(new LoopEdge(v[i].P, v[(i + 1) % n].P, null));
                return edges;
            }

            // start at a vertex where the leaving edge's curve differs from the arriving one
            int start = 0;
            for (int i = 0; i < n; i++)
                if (v[i].ArcIndex != v[(i - 1 + n) % n].ArcIndex) { start = i; break; }

            int k = 0;
            while (k < n)
            {
                int i = (start + k) % n;
                int arc = v[i].ArcIndex;
                int run = 1;
                while (arc >= 0 && k + run < n && v[(start + k + run) % n].ArcIndex == arc) run++;

                if (arc < 0)
                {
                    edges.Add(new LoopEdge(v[i].P, v[(i + 1) % n].P, null));
                    k++;
                    continue;
                }

                // a whole loop on one arc (full circle) → two halves so start != end
                if (run == n && n >= 2)
                {
                    int half = n / 2;
                    AddArcRun(edges, v, i, half, arcs[arc], shortCurveTolFt);
                    AddArcRun(edges, v, (i + half) % n, n - half, arcs[arc], shortCurveTolFt);
                }
                else AddArcRun(edges, v, i, run, arcs[arc], shortCurveTolFt);
                k += run;
            }
            return edges;
        }

        private static void AddArcRun(List<LoopEdge> edges, List<LoopVertex> v, int from, int run, ArcInfo arc,
                                      double shortTol)
        {
            int n = v.Count;
            var s = v[from].P;
            var e = v[(from + run) % n].P;
            Pt2 mid = run >= 2
                ? arc.Project(v[(from + run / 2) % n].P)
                : arc.Project(new Pt2((s.X + e.X) / 2, (s.Y + e.Y) / 2));

            // Too short / too flat for a stable arc → straight chord(s)
            if (s.DistanceTo(e) < shortTol * 4 ||
                Geometry2D.DistancePointSegment(mid, s, e) < shortTol * 0.25)
            {
                for (int q = 0; q < run; q++)
                    edges.Add(new LoopEdge(v[(from + q) % n].P, v[(from + q + 1) % n].P, null));
                return;
            }
            edges.Add(new LoopEdge(s, e, mid));
        }
    }
}
