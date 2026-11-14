// File: DijkstraPathEngine.cs
// Location: Core/Engine/
// Ported unchanged from AutoSlopeByDrain V004 (roof) — this is the more capable of
// the two prior versions (arc-aware tangent routing + user-configurable
// PathSampleCount + root-drain/path-position tracking for per-drain
// attribution). Rafi confirmed keeping the configurable Path Samples field
// over ByPoint's fixed dynamic formula. Namespace updated only.
//
// NEW (V010), per Rafi's confirmed progress-bar/Cancel decision: BuildGraph()
// is the O(n^2) step that dominates run time on a large floor (each surviving
// candidate pair calls Face.Project via IsValidEdge). It now accepts an
// optional throttled progress callback (0-100 fraction of the outer loop,
// reported at most every ~150ms via a Stopwatch so the reporting itself never
// meaningfully slows the loop down) and a CancellationToken, checked right
// after each throttled report — i.e. right after the UI has had a chance to
// pump a queued Cancel-button click (see AutoSlopeDrainViewModel/
// the shared ToolCancellation). BuildGraph never writes to the model, so a cancellation here
// costs nothing to unwind — the caller's transaction rolls back a true no-op.
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
// V013: BuildGraph uses a plan grid (PlanGrid) instead of all pairs; same edges.

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Engine
{
    public enum EdgeRouteType { Straight, SameArc, TangentRoute }

    public class DijkstraPathEngine
    {
        private readonly List<SlabShapeVertex> _verts;
        private readonly Dictionary<int, List<int>> _adj = new();
        private readonly Dictionary<(int, int), double> _edgeWeight = new();
        private readonly Dictionary<(int, int), EdgeRouteType> _edgeType = new();
        private readonly Face _topFace;
        private readonly double _edgeThresholdFt;
        private readonly List<Arc> _arcs;
        private readonly double _curveTolFt;
        private readonly Dictionary<int, List<VertexCurveInfo>> _curveMap;
        private int[] _lastPred;
        private const double PROJ_TOL = 0.00328084;

        // ByDrain exposes a user-facing "Path Samples" field (unlike ByPoint,
        // which uses a fixed dynamic sample count). Kept per Rafi's decision —
        // this control stays meaningful instead of silently ignoring it once
        // the tangent-arc engine took over edge validation.
        private readonly int _pathSampleCount;

        /// <summary>NEW (V010). Reports 0-100 progress through BuildGraph's outer loop. Throttled internally — safe to leave unset (null).</summary>
        private readonly Action<double> _onBuildGraphProgress;

        /// <summary>NEW (V010). Checked right after each throttled progress report; throws OperationCanceledException if the user cancelled.</summary>
        private readonly CancellationToken _cancelToken;

        private const int ProgressThrottleMs = 150;

        /// <param name="vertices">All floor shape vertices (including any inserted curve-intersection points).</param>
        /// <param name="topFace">Floor top face.</param>
        /// <param name="edgeThresholdFt">Max candidate edge length (Max Edge Distance / ConnectionThresholdMeters, in feet).</param>
        /// <param name="arcs">Boundary/opening arcs (outer + inner loops). Pass null/empty to disable arc-length handling.</param>
        /// <param name="curveTolFt">Tolerance for treating a vertex as lying "on" an arc.</param>
        /// <param name="pathSampleCount">
        ///     Points sampled along each candidate straight/tangent-leg edge to confirm
        ///     it lies on the floor face — comes from the UI "Path Samples" field.
        ///     Clamped to a minimum of 2.
        /// </param>
        /// <param name="onBuildGraphProgress">NEW (V010). Optional throttled 0-100 progress callback for BuildGraph's outer loop.</param>
        /// <param name="cancelToken">NEW (V010). Optional cancellation token, checked after each throttled progress report.</param>
        public DijkstraPathEngine(
            List<SlabShapeVertex> vertices,
            Face topFace,
            double edgeThresholdFt,
            List<Arc> arcs = null,
            double curveTolFt = 0.0033, // ~1mm default
            int pathSampleCount = 5,
            Action<double> onBuildGraphProgress = null,
            CancellationToken cancelToken = default)
        {
            _verts = vertices;
            _topFace = topFace;
            _edgeThresholdFt = edgeThresholdFt;
            _arcs = arcs ?? new List<Arc>();
            _curveTolFt = curveTolFt;
            _pathSampleCount = Math.Max(2, pathSampleCount);
            _onBuildGraphProgress = onBuildGraphProgress;
            _cancelToken = cancelToken;

            var positions = new List<XYZ>(_verts.Count);
            foreach (var v in _verts) positions.Add(v.Position);
            _curveMap = CurveIntersectionHelper.MapVerticesOnCurves(positions, _arcs, _curveTolFt);

            BuildGraph();
        }

        private void BuildGraph()
        {
            int n = _verts.Count;
            for (int i = 0; i < n; i++)
                _adj[i] = new List<int>();

            var progressStopwatch = _onBuildGraphProgress != null ? Stopwatch.StartNew() : null;

            // NEW (V013): plan-view grid with cells as wide as the max edge
            // distance. Any pair closer than that (3D) is also closer in plan, so
            // it sits in the same or a neighbouring cell: the edge set is exactly
            // V012's, without comparing every point with every other point.
            // Candidates come back in ascending index order, so edges are also
            // added in the same order as before.
            var grid = new PlanGrid(_verts, _edgeThresholdFt);

            for (int i = 0; i < n; i++)
            {
                // Throttled progress + cancellation checkpoint. Reporting fires at
                // most every ProgressThrottleMs — the report itself pumps the WPF
                // message loop (see AutoSlopeDrainViewModel's Progress callback),
                // which is also what lets a queued Cancel-button click actually run.
                if (progressStopwatch != null && progressStopwatch.ElapsedMilliseconds >= ProgressThrottleMs)
                {
                    progressStopwatch.Restart();
                    double frac = n <= 1 ? 100.0 : i * 100.0 / (n - 1);
                    _onBuildGraphProgress(frac);
                    _cancelToken.ThrowIfCancellationRequested();
                }

                XYZ a = _verts[i].Position;
                foreach (int j in grid.CandidatesAfter(i, a))
                {
                    XYZ b = _verts[j].Position;
                    double chord = a.DistanceTo(b);
                    if (chord < 0.033 || chord > _edgeThresholdFt) continue;

                    // If both endpoints sit on the SAME arc, always use the true arc
                    // length and bypass chord/crossing checks entirely — a straight
                    // chord between two points on the same (possibly concave) arc can
                    // cut outside the face and get wrongly rejected otherwise.
                    // NOTE: the TryGetValue chain must be inlined into this condition —
                    // caching the result in a bool and checking that bool separately
                    // breaks C#'s definite-assignment tracking for 'ci'/'cj'.
                    double weight;
                    EdgeRouteType type;

                    if (TryGetSharedArc(i, j, out VertexCurveInfo ciMatch, out VertexCurveInfo cjMatch))
                    {
                        weight = CurveIntersectionHelper.ArcLengthBetween(ciMatch.Curve, ciMatch.Parameter, cjMatch.Parameter);
                        type = EdgeRouteType.SameArc;
                    }
                    else
                    {
                        List<Arc> crossingArcs = GetCrossingArcs(a, b);

                        if (crossingArcs.Count == 0)
                        {
                            if (!IsValidEdge(a, b)) continue;
                            weight = chord;
                            type = EdgeRouteType.Straight;
                        }
                        else
                        {
                            // Straight chord cuts across an arc mid-span: instead of a
                            // hard reject, try building a tangent-in / arc / tangent-out
                            // route around the arc and use it if all three legs are valid.
                            double? tangentWeight = TryComputeTangentPathWeight(a, b, crossingArcs);
                            if (tangentWeight == null) continue;
                            weight = tangentWeight.Value;
                            type = EdgeRouteType.TangentRoute;
                        }
                    }

                    _adj[i].Add(j);
                    _adj[j].Add(i);
                    _edgeWeight[(i, j)] = weight;
                    _edgeWeight[(j, i)] = weight;
                    _edgeType[(i, j)] = type;
                    _edgeType[(j, i)] = type;
                }
            }

            _onBuildGraphProgress?.Invoke(100.0);
            _cancelToken.ThrowIfCancellationRequested();
        }

        /// <summary>NEW (V013). Buckets points by plan position for BuildGraph.</summary>
        private sealed class PlanGrid
        {
            private readonly Dictionary<(long, long), List<int>> _cells = new();
            private readonly double _cell;
            private readonly int _count;

            public PlanGrid(List<SlabShapeVertex> verts, double cellSizeFt)
            {
                _count = verts.Count;
                _cell = cellSizeFt > 1e-6 ? cellSizeFt : 0;
                if (_cell == 0) return; // degenerate threshold: fall back to all pairs

                for (int i = 0; i < verts.Count; i++)
                {
                    var key = Key(verts[i].Position);
                    if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>();
                    list.Add(i); // ascending, since i increases
                }
            }

            private (long, long) Key(XYZ p) => ((long)Math.Floor(p.X / _cell), (long)Math.Floor(p.Y / _cell));

            /// <summary>Indices j &gt; i in the 3×3 cells around p, ascending.</summary>
            public IEnumerable<int> CandidatesAfter(int i, XYZ p)
            {
                if (_cell == 0)
                {
                    for (int j = i + 1; j < _count; j++) yield return j;
                    yield break;
                }

                var (cx, cy) = Key(p);
                var found = new List<int>();
                for (long dx = -1; dx <= 1; dx++)
                    for (long dy = -1; dy <= 1; dy++)
                        if (_cells.TryGetValue((cx + dx, cy + dy), out var list))
                            foreach (int j in list)
                                if (j > i) found.Add(j);

                found.Sort();
                foreach (int j in found) yield return j;
            }
        }

        /// <summary>
        /// Returns the arcs that the straight segment a→b crosses at a point strictly
        /// interior to the segment (not at/near a or b) — i.e. arcs this chord would
        /// shortcut across instead of following.
        /// </summary>
        private List<Arc> GetCrossingArcs(XYZ a, XYZ b)
        {
            var crossing = new List<Arc>();
            if (_arcs.Count == 0) return crossing;

            Line line;
            try { line = Line.CreateBound(a, b); }
            catch { return crossing; }

            foreach (Arc arc in _arcs)
            {
                IntersectionResultArray xsects;
                SetComparisonResult result;
                try { result = line.Intersect(arc, out xsects); }
                catch { continue; }

                if (result != SetComparisonResult.Overlap || xsects == null) continue;

                foreach (IntersectionResult ir in xsects)
                {
                    XYZ p = ir.XYZPoint;
                    if (p == null) continue;
                    if (p.DistanceTo(a) > _curveTolFt && p.DistanceTo(b) > _curveTolFt)
                    {
                        crossing.Add(arc);
                        break;
                    }
                }
            }
            return crossing;
        }

        /// <summary>
        /// Builds the shortest valid tangent-in / arc / tangent-out path from a to b
        /// around each crossing arc, validating every leg against the floor face.
        /// Returns null if no valid tangent route exists (edge should be rejected).
        /// </summary>
        private double? TryComputeTangentPathWeight(XYZ a, XYZ b, List<Arc> crossingArcs)
        {
            double? best = null;

            foreach (Arc arc in crossingArcs)
            {
                var tangentsA = CurveIntersectionHelper.GetTangentCandidates(a, arc);
                var tangentsB = CurveIntersectionHelper.GetTangentCandidates(b, arc);
                if (tangentsA.Count == 0 || tangentsB.Count == 0) continue;

                foreach (var tA in tangentsA)
                {
                    XYZ ptA = arc.Evaluate(tA.Parameter, false);
                    if (!SafeIsValidEdge(a, ptA)) continue;

                    foreach (var tB in tangentsB)
                    {
                        XYZ ptB = arc.Evaluate(tB.Parameter, false);
                        if (!SafeIsValidEdge(ptB, b)) continue;

                        double legA = a.DistanceTo(ptA);
                        double legB = ptB.DistanceTo(b);
                        double arcLeg = CurveIntersectionHelper.ArcLengthBetween(arc, tA.Parameter, tB.Parameter);
                        double total = legA + arcLeg + legB;

                        if (best == null || total < best.Value)
                            best = total;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// IsValidEdge wrapper that treats a near-zero-length leg (point already at/on
        /// the tangent location, e.g. an on-arc endpoint) as trivially valid instead of
        /// throwing on Line.CreateBound.
        /// </summary>
        private bool SafeIsValidEdge(XYZ p1, XYZ p2)
        {
            if (p1.DistanceTo(p2) < 0.001) return true;
            try { return IsValidEdge(p1, p2); }
            catch { return false; }
        }

        /// <summary>
        /// Checks whether vertices i and j share any common arc among their candidate
        /// lists (a vertex can be "on" more than one arc at a shared endpoint between
        /// two adjoining arcs). Returns the matching VertexCurveInfo for each side —
        /// same Curve reference, each with its own Parameter on that curve.
        /// </summary>
        private bool TryGetSharedArc(int i, int j, out VertexCurveInfo ciMatch, out VertexCurveInfo cjMatch)
        {
            ciMatch = null;
            cjMatch = null;

            if (!_curveMap.TryGetValue(i, out var listI) || !_curveMap.TryGetValue(j, out var listJ))
                return false;

            foreach (var candI in listI)
            {
                foreach (var candJ in listJ)
                {
                    if (ReferenceEquals(candI.Curve, candJ.Curve))
                    {
                        ciMatch = candI;
                        cjMatch = candJ;
                        return true;
                    }
                }
            }
            return false;
        }

        private double Weight(int i, int j)
        {
            return _edgeWeight.TryGetValue((i, j), out double w)
                ? w
                : _verts[i].Position.DistanceTo(_verts[j].Position);
        }

        private bool IsValidEdge(XYZ a, XYZ b)
        {
            Line ln = Line.CreateBound(a, b);
            double step = 1.0 / (_pathSampleCount + 1);

            for (int i = 1; i <= _pathSampleCount; i++)
            {
                double t = step * i;
                XYZ p = ln.Evaluate(t, true);
                if (!PointOnTopFace(p)) return false;
            }
            return true;
        }

        private bool PointOnTopFace(XYZ p)
        {
            IntersectionResult proj = _topFace.Project(p);
            if (proj == null)
            {
                proj = _topFace.Project(p + XYZ.BasisZ * PROJ_TOL)
                    ?? _topFace.Project(p - XYZ.BasisZ * PROJ_TOL);
                if (proj == null) return false;
            }

            try
            {
                return _topFace.IsInside(proj.UVPoint);
            }
            catch
            {
                BoundingBoxUV bb = _topFace.GetBoundingBox();
                UV uv = proj.UVPoint;
                return uv.U >= bb.Min.U && uv.U <= bb.Max.U &&
                       uv.V >= bb.Min.V && uv.V <= bb.Max.V;
            }
        }

        /// <summary>
        /// Legacy per‑vertex Dijkstra (kept for reference, but not used in main engine)
        /// </summary>
        public double ComputeShortestPath(int start, HashSet<int> drains)
        {
            int n = _verts.Count;
            var dist = new double[n];
            var visited = new bool[n];

            for (int i = 0; i < n; i++)
                dist[i] = double.PositiveInfinity;

            dist[start] = 0;
            var pq = new SortedSet<(double, int)>(
                Comparer<(double, int)>.Create((a, b) =>
                {
                    int c = a.Item1.CompareTo(b.Item1);
                    return c != 0 ? c : a.Item2.CompareTo(b.Item2);
                }));

            pq.Add((0, start));

            while (pq.Count > 0)
            {
                var (d, v) = pq.Min;
                pq.Remove(pq.Min);
                if (visited[v]) continue;
                visited[v] = true;
                if (drains.Contains(v)) return d;

                foreach (int nb in _adj[v])
                {
                    double nd = d + Weight(v, nb);
                    if (nd < dist[nb])
                    {
                        dist[nb] = nd;
                        pq.Add((nd, nb));
                    }
                }
            }
            return double.PositiveInfinity;
        }

        /// <summary>
        /// Multi‑source reverse Dijkstra – computes shortest distance from every vertex
        /// to the nearest drain in a single pass.
        /// </summary>
        /// <param name="drains">Set of vertex indices that are drains.</param>
        /// <returns>Array of shortest path lengths (in internal feet) for each vertex.</returns>
        public double[] ComputeAllDistances(HashSet<int> drains)
        {
            int n = _verts.Count;
            var dist = new double[n];
            var pred = new int[n];
            for (int i = 0; i < n; i++)
            {
                dist[i] = double.PositiveInfinity;
                pred[i] = -1;
            }

            var pq = new SortedSet<(double, int)>(
                Comparer<(double, int)>.Create((a, b) =>
                {
                    int cmp = a.Item1.CompareTo(b.Item1);
                    return cmp != 0 ? cmp : a.Item2.CompareTo(b.Item2);
                }));

            // Initialise all drains with distance 0
            foreach (int drain in drains)
            {
                dist[drain] = 0;
                pred[drain] = -1;
                pq.Add((0, drain));
            }

            while (pq.Count > 0)
            {
                var (d, v) = pq.Min;
                pq.Remove(pq.Min);
                if (d > dist[v]) continue; // stale entry

                foreach (int nb in _adj[v])
                {
                    double nd = d + Weight(v, nb);
                    if (nd < dist[nb])
                    {
                        dist[nb] = nd;
                        pred[nb] = v;
                        pq.Add((nd, nb));
                    }
                }
            }

            _lastPred = pred;
            return dist;
        }

        /// <summary>True if this vertex index lies on any arc within curveTolFt.</summary>
        public bool IsOnArc(int vertexIndex) => _curveMap.TryGetValue(vertexIndex, out var list) && list.Count > 0;

        /// <summary>Count of vertices classified as lying on an arc.</summary>
        public int OnArcVertexCount => _curveMap.Count;

        /// <summary>
        /// Walks the predecessor chain from vertexIndex back to its drain (built by the
        /// most recent ComputeAllDistances call) and summarises how many hops of each
        /// route type (straight / same-arc / tangent-route) made up the path.
        /// Must be called after ComputeAllDistances.
        /// </summary>
        public string DescribePathEdgeTypes(int vertexIndex)
        {
            if (_lastPred == null) return "unavailable (call ComputeAllDistances first)";

            int straight = 0, sameArc = 0, tangent = 0, hops = 0;
            int cur = vertexIndex;
            int guard = 0;

            while (_lastPred[cur] != -1 && guard++ < _verts.Count + 5)
            {
                int prev = _lastPred[cur];
                if (_edgeType.TryGetValue((cur, prev), out var t))
                {
                    switch (t)
                    {
                        case EdgeRouteType.Straight: straight++; break;
                        case EdgeRouteType.SameArc: sameArc++; break;
                        case EdgeRouteType.TangentRoute: tangent++; break;
                    }
                }
                hops++;
                cur = prev;
            }

            if (hops == 0) return "is a drain / no path";

            var parts = new List<string>();
            if (sameArc > 0) parts.Add($"same-arc x{sameArc}");
            if (tangent > 0) parts.Add($"tangent-route x{tangent}");
            if (straight > 0) parts.Add($"straight x{straight}");

            return parts.Count > 0 ? string.Join(" + ", parts) : "unknown";
        }

        // ── Additions for per-drain attribution ────────────────────────────
        // ByPoint only ever has one undifferentiated drain-point set, so it never
        // needed to know WHICH source a vertex's shortest path terminates at.
        // ByDrain has multiple distinct DrainItems and needs that identity for its
        // Excel export / log ("nearest drain #3, 200x100mm") — these two methods
        // walk the same _lastPred chain DescribePathEdgeTypes already uses, just
        // for a different purpose. Neither touches the tangent/arc weight calculation.

        /// <summary>
        /// Walks the predecessor chain from vertexIndex back to the drain vertex index
        /// its shortest path terminates at. Must be called after ComputeAllDistances.
        /// Returns -1 if the vertex has no path (unreachable) or is itself a drain.
        /// </summary>
        public int GetRootDrainIndex(int vertexIndex)
        {
            if (_lastPred == null) return -1;

            int cur = vertexIndex;
            int guard = 0;
            int last = -1;

            while (_lastPred[cur] != -1 && guard++ < _verts.Count + 5)
            {
                last = cur;
                cur = _lastPred[cur];
            }

            // cur is now a drain root (pred == -1). If we never moved, vertexIndex
            // itself was already a drain (or unreachable — caller checks distance first).
            return cur != vertexIndex ? cur : -1;
        }

        /// <summary>
        /// Reconstructs the vertex-position polyline from the nearest drain out to
        /// vertexIndex (drain first, vertexIndex last), for direction-vector / export
        /// purposes. Note: this returns the FLOOR VERTEX chain only — it does not
        /// include intermediate tangent points on an arc for TangentRoute hops.
        /// Must be called after ComputeAllDistances.
        /// </summary>
        public List<XYZ> GetPathPositions(int vertexIndex)
        {
            var chain = new List<int> { vertexIndex };
            if (_lastPred == null) return new List<XYZ> { _verts[vertexIndex].Position };

            int cur = vertexIndex;
            int guard = 0;
            while (_lastPred[cur] != -1 && guard++ < _verts.Count + 5)
            {
                cur = _lastPred[cur];
                chain.Add(cur);
            }

            chain.Reverse(); // drain -> ... -> vertexIndex
            var positions = new List<XYZ>(chain.Count);
            foreach (int idx in chain)
                positions.Add(_verts[idx].Position);
            return positions;
        }
    }
}
