// =======================================================
// File: ShortestPathSolver.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services
// Replaces DijkstraPathEngine.ComputeAllDistances (V030). Pure .NET, so it
// is unit-testable. Changes:
//   - Weighted adjacency lists instead of a tuple-keyed weight dictionary,
//     and .NET's PriorityQueue instead of SortedSet (V030 P3).
//   - Also records, per vertex, WHICH drain its shortest path reaches and
//     the next vertex along that path (V032 D4) — free during the search.
// =======================================================

using System;
using System.Collections.Generic;
using System.Threading;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services
{
    /// <summary>Undirected weighted graph over roof shape points.</summary>
    public sealed class PathGraph
    {
        private readonly List<(int To, double Weight)>[] _adjacency;

        public PathGraph(int vertexCount)
        {
            VertexCount = vertexCount;
            _adjacency = new List<(int, double)>[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                _adjacency[i] = new List<(int, double)>();
        }

        public int VertexCount { get; }
        public int EdgeCount { get; private set; }

        public void AddEdge(int a, int b, double weight)
        {
            _adjacency[a].Add((b, weight));
            _adjacency[b].Add((a, weight));
            EdgeCount++;
        }

        public IReadOnlyList<(int To, double Weight)> Neighbours(int vertex) => _adjacency[vertex];
    }

    public sealed class ShortestPathResult
    {
        /// <summary>Path length to the nearest drain (graph units), +∞ when unreachable.</summary>
        public double[] Distance { get; }

        /// <summary>Vertex index of the drain the path ends at, or -1.</summary>
        public int[] SourceDrain { get; }

        /// <summary>Next vertex towards the drain, or -1 (drains and unreachable points).</summary>
        public int[] NextHop { get; }

        public ShortestPathResult(double[] distance, int[] sourceDrain, int[] nextHop)
        {
            Distance = distance;
            SourceDrain = sourceDrain;
            NextHop = nextHop;
        }
    }

    public static class ShortestPathSolver
    {
        /// <summary>Multi-source Dijkstra from every drain at once.</summary>
        public static ShortestPathResult Solve(PathGraph graph, IEnumerable<int> drains, CancellationToken cancel)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            int n = graph.VertexCount;
            var dist = new double[n];
            var source = new int[n];
            var next = new int[n];
            for (int i = 0; i < n; i++)
            {
                dist[i] = double.PositiveInfinity;
                source[i] = -1;
                next[i] = -1;
            }

            var queue = new PriorityQueue<int, double>();
            foreach (int d in drains)
            {
                if (d < 0 || d >= n) continue;
                dist[d] = 0;
                source[d] = d;
                queue.Enqueue(d, 0);
            }

            int pops = 0;
            while (queue.TryDequeue(out int v, out double dv))
            {
                if ((++pops & 0xFFF) == 0) cancel.ThrowIfCancellationRequested();
                if (dv > dist[v]) continue; // stale entry

                foreach (var (to, w) in graph.Neighbours(v))
                {
                    double nd = dv + w;
                    if (nd < dist[to])
                    {
                        dist[to] = nd;
                        source[to] = source[v];
                        next[to] = v;
                        queue.Enqueue(to, nd);
                    }
                }
            }

            return new ShortestPathResult(dist, source, next);
        }
    }
}
