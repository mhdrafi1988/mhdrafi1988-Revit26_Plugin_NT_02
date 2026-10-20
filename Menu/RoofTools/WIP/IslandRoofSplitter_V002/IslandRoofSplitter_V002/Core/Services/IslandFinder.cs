// IslandRoofSplitter V002 — traces every minimal closed region (face) of the cleaned plan graph and resolves
// nesting between disconnected loops.
//   NestingMode.Ring : an island that fully contains another component gets that component's outline as an opening.
//   NestingMode.Full : no openings; the outer island overlaps the inner one.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin_Revised.IslandRoofSplitter.V002.Core.Services
{
    public sealed class TracedLoop
    {
        public List<LoopVertex> Vertices { get; } = new();
        public List<int> VertexIds { get; } = new();
        public double SignedArea { get; set; }
        public List<Pt2> Points => Vertices.Select(v => v.P).ToList();
    }

    public sealed class RawIsland
    {
        public TracedLoop Outer { get; init; } = new();
        public List<TracedLoop> Holes { get; } = new();
        public HashSet<long> SourceIds { get; } = new();
        /// <summary>Note for the grid, e.g. "Ring with 1 opening".</summary>
        public string Note { get; set; } = "";
    }

    public static class IslandFinder
    {
        public static List<RawIsland> Find(PlanarGraph g, NestingMode nesting)
        {
            var faces = TraceFaces(g, out var faceComponent);

            // per component: bounded faces + the outer boundary
            var islands = new List<(RawIsland island, int comp)>();
            var outerOf = new Dictionary<int, TracedLoop>();
            foreach (var grp in Enumerable.Range(0, faces.Count).GroupBy(i => faceComponent[i]))
            {
                var list = grp.ToList();
                int outerIdx;
                if (list.Count == 2)
                {
                    // simple cycle: the larger signed area is the region (also covers bow-ties with ~0 area)
                    outerIdx = faces[list[0]].SignedArea <= faces[list[1]].SignedArea ? list[0] : list[1];
                    int inner = outerIdx == list[0] ? list[1] : list[0];
                    islands.Add((MakeIsland(g, faces[inner]), grp.Key));
                }
                else
                {
                    outerIdx = list.OrderBy(i => faces[i].SignedArea).First();
                    foreach (int fi in list)
                        if (fi != outerIdx && faces[fi].SignedArea > 0)
                            islands.Add((MakeIsland(g, faces[fi]), grp.Key));
                }
                outerOf[grp.Key] = faces[outerIdx];
            }

            if (nesting == NestingMode.Ring)
            {
                foreach (var (comp, outline) in outerOf)
                {
                    if (outline.Vertices.Count == 0) continue;
                    var probe = outline.Vertices[0].P;
                    double innerArea = Math.Abs(outline.SignedArea);
                    RawIsland? parent = null;
                    double parentArea = double.MaxValue;
                    foreach (var (isl, c) in islands)
                    {
                        if (c == comp) continue;
                        double a = Math.Abs(isl.Outer.SignedArea);
                        if (a <= innerArea || a >= parentArea) continue;
                        if (Geometry2D.PointInPolygon(probe, isl.Outer.Points)) { parent = isl; parentArea = a; }
                    }
                    if (parent != null)
                    {
                        parent.Holes.Add(outline);
                        foreach (var v in outline.Vertices) parent.SourceIds.Add(v.SourceId);
                    }
                }
                foreach (var (isl, _) in islands)
                    if (isl.Holes.Count > 0)
                        isl.Note = isl.Holes.Count == 1 ? "Ring with 1 opening" : $"Ring with {isl.Holes.Count} openings";
            }
            else
            {
                // Full: flag overlap so the grid explains it
                foreach (var (isl, comp) in islands)
                foreach (var (other, oc) in islands)
                {
                    if (oc == comp || other == isl) continue;
                    if (Math.Abs(other.Outer.SignedArea) < Math.Abs(isl.Outer.SignedArea) &&
                        Geometry2D.PointInPolygon(other.Outer.Vertices[0].P, isl.Outer.Points))
                    { isl.Note = "Full outer; overlaps a nested island"; break; }
                }
            }
            return islands.Select(x => x.island).ToList();
        }

        private static RawIsland MakeIsland(PlanarGraph g, TracedLoop loop)
        {
            var isl = new RawIsland { Outer = loop };
            foreach (var v in loop.Vertices) isl.SourceIds.Add(v.SourceId);
            return isl;
        }

        /// <summary>Half-edge face traversal. Bounded faces come out counter-clockwise (positive area).</summary>
        private static List<TracedLoop> TraceFaces(PlanarGraph g, out List<int> faceComponent)
        {
            int nv = g.Vertices.Count;
            var outgoing = new List<int>[nv];
            for (int i = 0; i < nv; i++) outgoing[i] = new List<int>();
            int Origin(int h) => (h & 1) == 0 ? g.Edges[h >> 1].A : g.Edges[h >> 1].B;
            int Dest(int h) => (h & 1) == 0 ? g.Edges[h >> 1].B : g.Edges[h >> 1].A;

            var live = g.LiveEdgeIds().ToList();
            foreach (int e in live)
            {
                outgoing[g.Edges[e].A].Add(2 * e);
                outgoing[g.Edges[e].B].Add(2 * e + 1);
            }
            var pos = new Dictionary<int, int>();
            for (int v = 0; v < nv; v++)
            {
                var o = g.Vertices[v];
                outgoing[v].Sort((h1, h2) =>
                {
                    var p1 = g.Vertices[Dest(h1)] - o;
                    var p2 = g.Vertices[Dest(h2)] - o;
                    return Math.Atan2(p1.Y, p1.X).CompareTo(Math.Atan2(p2.Y, p2.X));
                });
                for (int k = 0; k < outgoing[v].Count; k++) pos[outgoing[v][k]] = k;
            }

            var ds = new DisjointSet(nv);
            foreach (int e in live) ds.Union(g.Edges[e].A, g.Edges[e].B);

            var visited = new HashSet<int>();
            var faces = new List<TracedLoop>();
            faceComponent = new List<int>();
            foreach (int e in live)
            foreach (int start in new[] { 2 * e, 2 * e + 1 })
            {
                if (visited.Contains(start)) continue;
                var loop = new TracedLoop();
                int h = start, guard = 0;
                do
                {
                    visited.Add(h);
                    var edge = g.Edges[h >> 1];
                    int o = Origin(h);
                    loop.Vertices.Add(new LoopVertex(g.Vertices[o], edge.ArcIndex, edge.SourceId));
                    loop.VertexIds.Add(o);
                    int v = Dest(h);
                    int twin = h ^ 1;
                    int deg = outgoing[v].Count;
                    h = outgoing[v][(pos[twin] - 1 + deg) % deg];
                    if (++guard > 4 * g.Edges.Count + 8) break; // safety
                } while (h != start);

                loop.SignedArea = Geometry2D.SignedArea(loop.Points);
                faces.Add(loop);
                faceComponent.Add(ds.Find(loop.VertexIds[0]));
            }
            return faces;
        }
    }
}
