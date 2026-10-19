// IslandRoofSplitter V002 — removes edges that can't bound a region before face tracing:
//  * dangling chains (open ends, i.e. gaps > 1 mm)  → returned as OpenChains so they're reported as Skipped
//  * bridges (connector lines between two loops)    → removed and counted (logged, not an island)
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin_Revised.IslandRoofSplitter.V002.Core.Services
{
    public sealed class OpenChain
    {
        public List<int> EdgeIds { get; } = new();
        public List<Pt2> OpenEnds { get; } = new();
        public HashSet<long> SourceIds { get; } = new();
        /// <summary>Distance from one of this chain's open ends to the nearest other open end (ft).</summary>
        public double GapFt { get; set; } = double.PositiveInfinity;
        public Pt2 GapAt { get; set; }
    }

    public static class GraphCleaner
    {
        public static List<OpenChain> RemoveDangling(PlanarGraph g)
        {
            var adj = BuildAdjacency(g);
            var degree = adj.Select(l => l.Count).ToArray();
            var openEnds = Enumerable.Range(0, degree.Length).Where(v => degree[v] == 1).ToHashSet();

            var removed = new List<int>();
            var queue = new Queue<int>(openEnds);
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                if (degree[v] != 1) continue;
                foreach (int e in adj[v])
                {
                    if (g.Edges[e].Removed) continue;
                    g.Edges[e].Removed = true;
                    removed.Add(e);
                    degree[v]--;
                    int w = g.Edges[e].A == v ? g.Edges[e].B : g.Edges[e].A;
                    degree[w]--;
                    if (degree[w] == 1) queue.Enqueue(w);
                    break;
                }
            }

            // group removed edges into chains
            var ds = new DisjointSet(g.Vertices.Count);
            foreach (int e in removed) ds.Union(g.Edges[e].A, g.Edges[e].B);
            var chains = new Dictionary<int, OpenChain>();
            foreach (int e in removed)
            {
                int root = ds.Find(g.Edges[e].A);
                if (!chains.TryGetValue(root, out var ch)) chains[root] = ch = new OpenChain();
                ch.EdgeIds.Add(e);
                ch.SourceIds.Add(g.Edges[e].SourceId);
            }
            foreach (int v in openEnds)
                if (chains.TryGetValue(ds.Find(v), out var ch)) ch.OpenEnds.Add(g.Vertices[v]);

            // gap = nearest other open end
            var allEnds = openEnds.Select(v => g.Vertices[v]).ToList();
            foreach (var ch in chains.Values)
                foreach (var p in ch.OpenEnds)
                    foreach (var q in allEnds)
                    {
                        if (q.Equals(p)) continue;
                        double d = p.DistanceTo(q);
                        if (d < ch.GapFt) { ch.GapFt = d; ch.GapAt = new Pt2((p.X + q.X) / 2, (p.Y + q.Y) / 2); }
                    }
            return chains.Values.ToList();
        }

        /// <summary>Removes bridge edges (Tarjan, iterative). Returns how many were removed.</summary>
        public static int RemoveBridges(PlanarGraph g)
        {
            var adj = BuildAdjacency(g);
            int n = g.Vertices.Count;
            var disc = new int[n];
            var low = new int[n];
            Array.Fill(disc, -1);
            int time = 0;
            var bridges = new List<int>();

            for (int root = 0; root < n; root++)
            {
                if (disc[root] != -1 || adj[root].Count == 0) continue;
                var stack = new Stack<(int v, int parentEdge, int next)>();
                disc[root] = low[root] = time++;
                stack.Push((root, -1, 0));
                while (stack.Count > 0)
                {
                    var (v, pe, next) = stack.Pop();
                    if (next < adj[v].Count)
                    {
                        stack.Push((v, pe, next + 1));
                        int e = adj[v][next];
                        if (e == pe) continue;
                        int w = g.Edges[e].A == v ? g.Edges[e].B : g.Edges[e].A;
                        if (disc[w] == -1)
                        {
                            disc[w] = low[w] = time++;
                            stack.Push((w, e, 0));
                        }
                        else low[v] = Math.Min(low[v], disc[w]);
                    }
                    else if (pe >= 0)
                    {
                        int parent = g.Edges[pe].A == v ? g.Edges[pe].B : g.Edges[pe].A;
                        low[parent] = Math.Min(low[parent], low[v]);
                        if (low[v] > disc[parent]) bridges.Add(pe);
                    }
                }
            }
            foreach (int e in bridges) g.Edges[e].Removed = true;
            return bridges.Count;
        }

        internal static List<int>[] BuildAdjacency(PlanarGraph g)
        {
            var adj = new List<int>[g.Vertices.Count];
            for (int i = 0; i < adj.Length; i++) adj[i] = new List<int>();
            foreach (int e in g.LiveEdgeIds())
            {
                adj[g.Edges[e].A].Add(e);
                adj[g.Edges[e].B].Add(e);
            }
            return adj;
        }
    }
}
