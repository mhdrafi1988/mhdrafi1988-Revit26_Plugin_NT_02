// IslandRoofSplitter V002 — builds the plan graph (vertices + straight edges) from tessellated sources.
// CrossingMode.Split: edges are split at every crossing and T-junction (touch tolerance = heal tolerance),
// which makes the graph planar. CrossingMode.ChainsOnly: only shared endpoints connect.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Core.Services
{
    public sealed class GraphEdge
    {
        public int A { get; set; }
        public int B { get; set; }
        public long SourceId { get; init; }
        public int ArcIndex { get; init; } = -1;
        public bool Removed { get; set; }
    }

    public sealed class PlanarGraph
    {
        public List<Pt2> Vertices { get; init; } = new();
        public List<GraphEdge> Edges { get; } = new();
        public List<ArcInfo> Arcs { get; init; } = new();

        public IEnumerable<int> LiveEdgeIds() => Enumerable.Range(0, Edges.Count).Where(i => !Edges[i].Removed);
    }

    public static class PlanarGraphBuilder
    {
        /// <summary>Distance under which two computed points are considered the same vertex.</summary>
        public const double VertexMergeTolFt = 1e-6;

        public static PlanarGraph Build(List<TessellatedSource> sources, List<ArcInfo> arcs,
                                        CrossingMode crossings, double touchTolFt)
        {
            // 1) raw straight pieces
            var raw = new List<(Pt2 a, Pt2 b, long src, int arc)>();
            foreach (var s in sources)
                for (int i = 0; i + 1 < s.Points.Count; i++)
                    if (s.Points[i].DistanceTo(s.Points[i + 1]) > VertexMergeTolFt)
                        raw.Add((s.Points[i], s.Points[i + 1], s.SourceId, s.ArcIndex));

            // 2) split points per raw edge (parameter t along a→b, point)
            var splits = raw.Select(_ => new List<(double t, Pt2 p)>()).ToList();
            if (crossings == CrossingMode.Split)
                FindSplits(raw, splits, touchTolFt);

            // 3) emit sub-edges through a tolerant vertex index
            var vix = new VertexIndex(VertexMergeTolFt);
            var graph = new PlanarGraph { Vertices = vix.Points, Arcs = arcs };
            var seen = new HashSet<(int, int)>();
            for (int i = 0; i < raw.Count; i++)
            {
                var (a, b, src, arc) = raw[i];
                var chain = new List<Pt2> { a };
                chain.AddRange(splits[i].OrderBy(s => s.t).Select(s => s.p));
                chain.Add(b);
                for (int k = 0; k + 1 < chain.Count; k++)
                {
                    int u = vix.GetOrAdd(chain[k]);
                    int v = vix.GetOrAdd(chain[k + 1]);
                    if (u == v) continue;
                    var key = u < v ? (u, v) : (v, u);
                    if (!seen.Add(key)) continue; // overlapping duplicate line
                    graph.Edges.Add(new GraphEdge { A = u, B = v, SourceId = src, ArcIndex = arc });
                }
            }
            return graph;
        }

        private static void FindSplits(List<(Pt2 a, Pt2 b, long src, int arc)> raw,
                                       List<List<(double t, Pt2 p)>> splits, double tol)
        {
            int n = raw.Count;
            var order = Enumerable.Range(0, n)
                .OrderBy(i => Math.Min(raw[i].a.X, raw[i].b.X)).ToArray();

            for (int oi = 0; oi < n; oi++)
            {
                int i = order[oi];
                var (a, b, _, _) = raw[i];
                double maxX = Math.Max(a.X, b.X) + tol;
                double minY = Math.Min(a.Y, b.Y) - tol, maxY = Math.Max(a.Y, b.Y) + tol;

                for (int oj = oi + 1; oj < n; oj++)
                {
                    int j = order[oj];
                    var (c, d, _, _) = raw[j];
                    if (Math.Min(c.X, d.X) > maxX) break;
                    if (Math.Max(c.Y, d.Y) < minY || Math.Min(c.Y, d.Y) > maxY) continue;

                    if (Geometry2D.SegmentIntersection(a, b, c, d, tol, tol, out double t, out _))
                    {
                        var x = a + (b - a) * t;
                        x = SnapToEndpoint(x, tol, a, b, c, d);
                        AddSplit(splits[i], a, b, x);
                        AddSplit(splits[j], c, d, x);
                    }
                    else
                    {
                        // parallel: collinear overlap / end touching — split each at the other's endpoints
                        foreach (var p in new[] { c, d })
                            if (Geometry2D.DistancePointSegment(p, a, b) <= tol) AddSplit(splits[i], a, b, p);
                        foreach (var p in new[] { a, b })
                            if (Geometry2D.DistancePointSegment(p, c, d) <= tol) AddSplit(splits[j], c, d, p);
                    }
                }
            }
        }

        private static Pt2 SnapToEndpoint(Pt2 x, double tol, params Pt2[] ends)
        {
            Pt2 best = x;
            double bestD = tol;
            foreach (var e in ends)
            {
                double d = e.DistanceTo(x);
                if (d <= bestD) { bestD = d; best = e; }
            }
            return best;
        }

        private static void AddSplit(List<(double t, Pt2 p)> list, Pt2 a, Pt2 b, Pt2 p)
        {
            if (p.DistanceTo(a) <= VertexMergeTolFt || p.DistanceTo(b) <= VertexMergeTolFt) return;
            var ab = b - a;
            double t = Pt2.Dot(p - a, ab) / Pt2.Dot(ab, ab);
            if (t <= 0 || t >= 1) return;
            list.Add((t, p));
        }
    }
}
