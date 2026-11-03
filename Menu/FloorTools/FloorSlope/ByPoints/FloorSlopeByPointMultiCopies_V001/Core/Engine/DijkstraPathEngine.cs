using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Engine
{
    public class DijkstraPathEngine
    {
        private readonly List<SlabShapeVertex> _verts;
        private readonly Dictionary<int, List<int>> _adj = new();
        private readonly Dictionary<(int, int), double> _edgeWeight = new();
        private readonly Face _topFace;
        private readonly double _edgeThresholdFt;
        private readonly List<Arc> _arcs;
        private readonly double _curveTolFt;
        private readonly Dictionary<int, List<VertexCurveInfo>> _curveMap;
        private const double PROJ_TOL = 0.00328084;

        /// <summary>NEW. Reports 0-100 progress through BuildGraph's outer loop. Throttled internally — safe to leave unset (null).</summary>
        private readonly Action<double> _onBuildGraphProgress;

        /// <summary>NEW. Checked right after each throttled progress report; throws OperationCanceledException if the user cancelled.</summary>
        private readonly CancellationToken _cancelToken;

        private const int ProgressThrottleMs = 150;

        /// <param name="vertices">All floor shape vertices (including any inserted curve-intersection points).</param>
        /// <param name="topFace">Floor top face.</param>
        /// <param name="edgeThresholdFt">Max candidate edge length.</param>
        /// <param name="arcs">Boundary/opening arcs (outer + inner loops). Pass null/empty to disable arc-length handling.</param>
        /// <param name="curveTolFt">Tolerance for treating a vertex as lying "on" an arc.</param>
        /// <param name="onBuildGraphProgress">NEW. Optional throttled 0-100 progress callback for BuildGraph's outer loop.</param>
        /// <param name="cancelToken">NEW. Optional cancellation token, checked after each throttled progress report.</param>
        public DijkstraPathEngine(
            List<SlabShapeVertex> vertices,
            Face topFace,
            double edgeThresholdFt,
            List<Arc> arcs = null,
            double curveTolFt = 0.0033, // ~1mm default
            Action<double> onBuildGraphProgress = null,
            CancellationToken cancelToken = default)
        {
            _verts = vertices;
            _topFace = topFace;
            _edgeThresholdFt = edgeThresholdFt;
            _arcs = arcs ?? new List<Arc>();
            _curveTolFt = curveTolFt;
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

            for (int i = 0; i < n; i++)
            {
                if (progressStopwatch != null && progressStopwatch.ElapsedMilliseconds >= ProgressThrottleMs)
                {
                    progressStopwatch.Restart();
                    double frac = n <= 1 ? 100.0 : i * 100.0 / (n - 1);
                    _onBuildGraphProgress(frac);
                    _cancelToken.ThrowIfCancellationRequested();
                }

                XYZ a = _verts[i].Position;
                for (int j = i + 1; j < n; j++)
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

                    if (TryGetSharedArc(i, j, out VertexCurveInfo ciMatch, out VertexCurveInfo cjMatch))
                    {
                        weight = CurveIntersectionHelper.ArcLengthBetween(ciMatch.Curve, ciMatch.Parameter, cjMatch.Parameter);
                    }
                    else
                    {
                        List<Arc> crossingArcs = GetCrossingArcs(a, b);

                        if (crossingArcs.Count == 0)
                        {
                            if (!IsValidEdge(a, b)) continue;
                            weight = chord;
                        }
                        else
                        {
                            // Straight chord cuts across an arc mid-span: instead of a
                            // hard reject, try building a tangent-in / arc / tangent-out
                            // route around the arc and use it if all three legs are valid.
                            double? tangentWeight = TryComputeTangentPathWeight(a, b, crossingArcs);
                            if (tangentWeight == null) continue;
                            weight = tangentWeight.Value;
                        }
                    }

                    _adj[i].Add(j);
                    _adj[j].Add(i);
                    _edgeWeight[(i, j)] = weight;
                    _edgeWeight[(j, i)] = weight;
                }
            }

            _onBuildGraphProgress?.Invoke(100.0);
            _cancelToken.ThrowIfCancellationRequested();
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
            double len = a.DistanceTo(b);
            int samples = Math.Max(10, (int)(len * 4));
            double step = 1.0 / samples;

            for (double t = step; t < 1.0; t += step)
            {
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
        /// Multi‑source reverse Dijkstra – computes shortest distance from every vertex
        /// to the nearest drain in a single pass.
        /// </summary>
        /// <param name="drains">Set of vertex indices that are drains.</param>
        /// <returns>Array of shortest path lengths (in internal feet) for each vertex.</returns>
        public double[] ComputeAllDistances(HashSet<int> drains)
        {
            int n = _verts.Count;
            var dist = new double[n];
            for (int i = 0; i < n; i++)
                dist[i] = double.PositiveInfinity;

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
                        pq.Add((nd, nb));
                    }
                }
            }

            return dist;
        }
    }
}