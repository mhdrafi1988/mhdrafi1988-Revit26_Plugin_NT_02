// =======================================================
// File: RidgeEngine.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001
// New in Ridge V001.
// Purpose:
//   Finds the floor's ridge points from the drains alone (no manual
//   ridge picking) and works out how high each ridge point must be so
//   that water leaves it towards EVERY drain around it at the given
//   slope — not just towards the nearest one.
//
// How it works (path-distance Voronoi):
//   1. Drain vertices are clustered into GROUPS: two drains whose XY
//      distance is within DrainGroupRadius belong to one group (one
//      sump with several outlets is one drain, not a ridge pair).
//   2. For every group, a multi-source Dijkstra over the floor-shape
//      graph gives the path distance from each vertex to that group.
//   3. Each vertex belongs to the BASIN of its nearest group — this is
//      the Voronoi cell of that group measured along the floor, so it
//      follows openings and curved edges, and the boundary between two
//      basins lands wherever the distances tie, not at the geometric
//      middle.
//   4. A vertex is a RIDGE POINT when one of its graph edges crosses
//      into another basin and this vertex is the endpoint nearer the
//      tie line (so the ridge is one vertex wide; ties mark both).
//      The groups on either side of those edges are the ridge point's
//      "surrounding" drains — only the Voronoi neighbours, never a
//      drain on the far side of the floor.
//   5. Ridge lift = slopeFactor × MAX path distance to any surrounding
//      group. Water then falls from the ridge to each surrounding
//      drain at ≥ the given slope (exactly the given slope towards the
//      farthest one, steeper towards the others). This is the same as
//      "start at the nearest drain; if any surrounding drain would get
//      less slope, move to the next nearest; and so on".
//
//   The lifts are applied by DijkstraPathEngine.ComputeLowerBoundedElevations,
//   which also re-flows every vertex behind a ridge to the far drain
//   ("move watershed"), so no vertex is ever left without a way down.
// =======================================================

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Core.Engine
{
    public static class RidgeEngine
    {
        /// <summary>Per-vertex ridge information produced by <see cref="Analyze"/>.</summary>
        public class RidgeInfo
        {
            public int VertexIndex { get; set; }

            /// <summary>Groups whose basins touch this ridge point (own basin first).</summary>
            public List<int> SurroundingGroups { get; } = new List<int>();

            /// <summary>Path distance (ft) from this vertex to each surrounding group.</summary>
            public Dictionary<int, double> PathToGroupFt { get; } = new Dictionary<int, double>();

            /// <summary>Group that sets the lift (the farthest surrounding group).</summary>
            public int GoverningGroup { get; set; } = -1;

            /// <summary>Path distance (ft) to the governing group.</summary>
            public double GoverningPathFt { get; set; }

            /// <summary>Path distance (ft) to the nearest group (what V028 would have used).</summary>
            public double NearestPathFt { get; set; }

            /// <summary>Minimum elevation (ft) this ridge point must be raised to.</summary>
            public double LiftFt { get; set; }
        }

        public class RidgeAnalysis
        {
            /// <summary>Drain vertex index → group index.</summary>
            public Dictionary<int, int> DrainToGroup { get; } = new Dictionary<int, int>();

            /// <summary>Group index → drain vertex indices in that group.</summary>
            public List<List<int>> Groups { get; } = new List<List<int>>();

            /// <summary>[group][vertex] path distance in feet (+∞ = unreachable).</summary>
            public List<double[]> GroupDistances { get; } = new List<double[]>();

            /// <summary>Nearest group per vertex (-1 = unreachable from every group).</summary>
            public int[] Basin { get; set; }

            /// <summary>Path distance (ft) to the nearest group per vertex.</summary>
            public double[] NearestDistance { get; set; }

            /// <summary>Ridge points keyed by vertex index.</summary>
            public Dictionary<int, RidgeInfo> RidgePoints { get; } = new Dictionary<int, RidgeInfo>();

            /// <summary>Per-vertex minimum elevation (ft) — 0 for non-ridge vertices.</summary>
            public double[] MinElevationFt { get; set; }

            public int GroupCount => Groups.Count;
        }

        /// <summary>
        /// Runs steps 1–5 from the file header. Never throws for "no ridge possible"
        /// (one drain group, or no cross-basin edge): the returned analysis simply
        /// has an empty <see cref="RidgeAnalysis.RidgePoints"/> and zero lifts, and
        /// the caller degrades to plain V028 behaviour.
        /// </summary>
        /// <param name="graph">Graph already built by DijkstraPathEngine for this floor.</param>
        /// <param name="drainIndices">Vertex indices matched to drain points.</param>
        /// <param name="groupRadiusFt">XY radius (ft) within which drains are one group. ≤ 0 = every drain is its own group.</param>
        /// <param name="slopeFactor">Slope as a ratio (percent / 100).</param>
        public static RidgeAnalysis Analyze(
            DijkstraPathEngine graph,
            IReadOnlyList<SlabShapeVertex> vertices,
            HashSet<int> drainIndices,
            double groupRadiusFt,
            double slopeFactor)
        {
            var result = new RidgeAnalysis();
            int n = graph.VertexCount;

            // ── 1. Group drains by XY proximity (union-find) ────────────────
            var drains = drainIndices.OrderBy(i => i).ToList();
            var parent = new Dictionary<int, int>();
            foreach (int d in drains) parent[d] = d;

            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }

            if (groupRadiusFt > 0)
            {
                for (int a = 0; a < drains.Count; a++)
                {
                    XYZ pa = vertices[drains[a]].Position;
                    for (int b = a + 1; b < drains.Count; b++)
                    {
                        XYZ pb = vertices[drains[b]].Position;
                        double dx = pa.X - pb.X, dy = pa.Y - pb.Y;
                        if (Math.Sqrt(dx * dx + dy * dy) <= groupRadiusFt)
                        {
                            int ra = Find(drains[a]), rb = Find(drains[b]);
                            if (ra != rb) parent[rb] = ra;
                        }
                    }
                }
            }

            var rootToGroup = new Dictionary<int, int>();
            foreach (int d in drains)
            {
                int root = Find(d);
                if (!rootToGroup.TryGetValue(root, out int g))
                {
                    g = result.Groups.Count;
                    rootToGroup[root] = g;
                    result.Groups.Add(new List<int>());
                }
                result.Groups[g].Add(d);
                result.DrainToGroup[d] = g;
            }

            // ── 2. Per-group path distances ─────────────────────────────────
            foreach (var group in result.Groups)
                result.GroupDistances.Add(graph.ComputeAllDistances(new HashSet<int>(group)));

            // ── 3. Basin (nearest group) per vertex ─────────────────────────
            result.Basin = new int[n];
            result.NearestDistance = new double[n];
            for (int i = 0; i < n; i++)
            {
                int best = -1;
                double bestD = double.PositiveInfinity;
                for (int g = 0; g < result.GroupCount; g++)
                {
                    double d = result.GroupDistances[g][i];
                    if (d < bestD) { bestD = d; best = g; }
                }
                result.Basin[i] = best;
                result.NearestDistance[i] = bestD;
            }

            result.MinElevationFt = new double[n];
            if (result.GroupCount < 2) return result;   // one group → no ridge possible

            // ── 4. Ridge points = nearer endpoint of every cross-basin edge ──
            const double tieEps = 0.001; // ft (~0.3 mm)
            for (int i = 0; i < n; i++)
            {
                int gi = result.Basin[i];
                if (gi < 0 || drainIndices.Contains(i)) continue;

                RidgeInfo info = null;
                foreach (int j in graph.Neighbors(i))
                {
                    int gj = result.Basin[j];
                    if (gj < 0 || gj == gi) continue;

                    // How far each endpoint sits from the tie line between the
                    // two basins: 0 = exactly on the watershed.
                    double marginI = result.GroupDistances[gj][i] - result.GroupDistances[gi][i];
                    double marginJ = result.GroupDistances[gi][j] - result.GroupDistances[gj][j];
                    if (marginI > marginJ + tieEps) continue;   // j is the ridge end of this edge

                    if (info == null)
                    {
                        info = new RidgeInfo
                        {
                            VertexIndex = i,
                            NearestPathFt = result.GroupDistances[gi][i]
                        };
                        info.SurroundingGroups.Add(gi);
                        info.PathToGroupFt[gi] = result.GroupDistances[gi][i];
                    }
                    if (!info.PathToGroupFt.ContainsKey(gj))
                    {
                        info.SurroundingGroups.Add(gj);
                        info.PathToGroupFt[gj] = result.GroupDistances[gj][i];
                    }
                }

                if (info == null) continue;

                // ── 5. Lift = slope × farthest surrounding group ────────────
                foreach (var kv in info.PathToGroupFt)
                {
                    if (double.IsInfinity(kv.Value)) continue;
                    if (kv.Value > info.GoverningPathFt)
                    {
                        info.GoverningPathFt = kv.Value;
                        info.GoverningGroup = kv.Key;
                    }
                }
                info.LiftFt = info.GoverningPathFt * slopeFactor;
                result.MinElevationFt[i] = info.LiftFt;
                result.RidgePoints[i] = info;
            }

            return result;
        }

        /// <summary>
        /// Post-check used for logging: counts processed non-drain vertices that
        /// have NO neighbour they can descend to at ≥ the given slope. With the
        /// lower-bounded Dijkstra this should always be 0; a non-zero value means
        /// the graph changed under us and is worth a warning in the log.
        /// </summary>
        public static int CountVerticesWithoutDescent(
            DijkstraPathEngine graph,
            double[] elevFt,
            HashSet<int> drainIndices,
            double slopeFactor)
        {
            const double eps = 1e-6;
            int bad = 0;
            for (int i = 0; i < elevFt.Length; i++)
            {
                if (drainIndices.Contains(i) || double.IsInfinity(elevFt[i])) continue;

                bool ok = false;
                foreach (int j in graph.Neighbors(i))
                {
                    if (double.IsInfinity(elevFt[j])) continue;
                    double need = graph.EdgeWeight(i, j) * slopeFactor;
                    if (elevFt[i] - elevFt[j] >= need - eps) { ok = true; break; }
                }
                if (!ok) bad++;
            }
            return bad;
        }
    }
}
