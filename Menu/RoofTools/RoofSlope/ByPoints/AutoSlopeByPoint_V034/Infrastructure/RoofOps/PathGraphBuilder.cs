// =======================================================
// File: PathGraphBuilder.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.RoofOps
// Replaces DijkstraPathEngine's constructor/BuildGraph (V030). The edge
// rules are unchanged (same-arc bypass, tangent route around crossing
// arcs, face-sampling validity). What changed:
//   - Candidate pairs come from a spatial grid sized to the max edge
//     length, instead of testing all n²/2 pairs (V030 P1b). With the
//     default (edge length = drainage distance) the result is identical
//     to V030; a shorter edge length is where the speed-up comes from.
//   - Edge length is its own setting, no longer the drainage distance (B3).
//   - Output is a pure PathGraph solved by Core.Services.ShortestPathSolver.
// Not changed (deliberately): the face-sampling validity test (P1c). It
// is the slowest part, but replacing it changes which links are accepted,
// so it needs before/after comparison on real roofs first.
// =======================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.RoofOps
{
    internal sealed class PathGraphBuilder
    {
        private const int ProgressThrottleMs = 150;

        private readonly IReadOnlyList<XYZ> _positions;
        private readonly Face _topFace;
        private readonly double _maxEdgeFt;
        private readonly List<Arc> _arcs;
        private readonly double _curveTolFt;
        private readonly double _checkSpacingFt;
        private readonly double _projectionTolFt;
        private readonly bool _exactSegmentTest;
        private readonly List<BoundarySegment> _boundary;
        private readonly Dictionary<int, List<VertexCurveInfo>> _curveMap;
        private readonly Action<double> _onProgress;
        private readonly CancellationToken _cancel;

        public PathGraphBuilder(
            IReadOnlyList<XYZ> positions,
            Face topFace,
            double maxEdgeFt,
            List<Arc> arcs,
            double curveTolFt,
            double checkSpacingFt,
            double projectionTolFt,
            bool exactSegmentTest,
            Action<double> onProgress,
            CancellationToken cancel)
        {
            _positions = positions;
            _topFace = topFace;
            _maxEdgeFt = maxEdgeFt;
            _arcs = arcs ?? new List<Arc>();
            _curveTolFt = curveTolFt;
            _checkSpacingFt = checkSpacingFt;
            _projectionTolFt = projectionTolFt;
            _exactSegmentTest = exactSegmentTest;
            _boundary = exactSegmentTest ? ReadBoundary(topFace) : null;
            _onProgress = onProgress;
            _cancel = cancel;
            _curveMap = CurveIntersectionHelper.MapVerticesOnCurves(_positions, _arcs, _curveTolFt);
        }

        public PathGraph Build()
        {
            int n = _positions.Count;
            var graph = new PathGraph(n);
            var points = _positions.Select(p => p.ToPoint3()).ToList();
            var grid = new SpatialGrid2D(points, _maxEdgeFt);
            var clock = Stopwatch.StartNew();

            for (int i = 0; i < n; i++)
            {
                if (clock.ElapsedMilliseconds >= ProgressThrottleMs)
                {
                    clock.Restart();
                    _onProgress?.Invoke(n <= 1 ? 100.0 : i * 100.0 / (n - 1));
                    _cancel.ThrowIfCancellationRequested();
                }

                XYZ a = _positions[i];
                foreach (int j in grid.QueryXY(points[i], _maxEdgeFt))
                {
                    if (j <= i) continue;

                    XYZ b = _positions[j];
                    double chord = a.DistanceTo(b);
                    if (chord < AutoSlopeConstants.MinEdgeLengthFt || chord > _maxEdgeFt) continue;

                    double weight;
                    if (TryGetSharedArc(i, j, out VertexCurveInfo ci, out VertexCurveInfo cj))
                    {
                        // Both ends on the same arc: follow the arc, skip the chord tests.
                        weight = CurveIntersectionHelper.ArcLengthBetween(ci.Curve, ci.Parameter, cj.Parameter);
                    }
                    else
                    {
                        List<Arc> crossing = GetCrossingArcs(a, b);
                        if (crossing.Count == 0)
                        {
                            if (!IsValidEdge(a, b)) continue;
                            weight = chord;
                        }
                        else
                        {
                            double? around = TryComputeTangentPathWeight(a, b, crossing);
                            if (around == null) continue;
                            weight = around.Value;
                        }
                    }

                    graph.AddEdge(i, j, weight);
                }
            }

            _onProgress?.Invoke(100.0);
            _cancel.ThrowIfCancellationRequested();
            return graph;
        }

        // ── Edge rules (unchanged from V030 DijkstraPathEngine) ─────────────

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

                        double total = a.DistanceTo(ptA)
                                     + CurveIntersectionHelper.ArcLengthBetween(arc, tA.Parameter, tB.Parameter)
                                     + ptB.DistanceTo(b);

                        if (best == null || total < best.Value)
                            best = total;
                    }
                }
            }
            return best;
        }

        private bool SafeIsValidEdge(XYZ p1, XYZ p2)
        {
            if (p1.DistanceTo(p2) < 0.001) return true;
            try { return IsValidEdge(p1, p2); }
            catch { return false; }
        }

        private bool TryGetSharedArc(int i, int j, out VertexCurveInfo ciMatch, out VertexCurveInfo cjMatch)
        {
            ciMatch = null;
            cjMatch = null;
            if (!_curveMap.TryGetValue(i, out var listI) || !_curveMap.TryGetValue(j, out var listJ))
                return false;

            foreach (var candI in listI)
                foreach (var candJ in listJ)
                    if (ReferenceEquals(candI.Curve, candJ.Curve))
                    {
                        ciMatch = candI;
                        cjMatch = candJ;
                        return true;
                    }
            return false;
        }

        private bool IsValidEdge(XYZ a, XYZ b)
        {
            if (_exactSegmentTest && _boundary != null && !ExactSegmentOnFace(a, b)) return false;

            Line ln = Line.CreateBound(a, b);
            double len = a.DistanceTo(b);
            int samples = Math.Max(AutoSlopeConstants.MinPathChecksPerLink, (int)(len / _checkSpacingFt));
            double step = 1.0 / samples;

            for (double t = step; t < 1.0; t += step)
                if (!PointOnTopFace(ln.Evaluate(t, true))) return false;
            return true;
        }

        // ── V034: exact segment test (opt-in) ───────────────────────────────

        /// <summary>One straight piece of the top face's outline (outer edge or opening), in plan (feet).</summary>
        private readonly struct BoundarySegment
        {
            public readonly double X1, Y1, X2, Y2, MinX, MaxX, MinY, MaxY;
            public BoundarySegment(double x1, double y1, double x2, double y2)
            {
                X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
                MinX = Math.Min(x1, x2); MaxX = Math.Max(x1, x2);
                MinY = Math.Min(y1, y2); MaxY = Math.Max(y1, y2);
            }
        }

        /// <summary>Outer boundary and opening edges of the top face as plan segments (curves are tessellated). Null if unreadable.</summary>
        private static List<BoundarySegment> ReadBoundary(Face face)
        {
            try
            {
                var list = new List<BoundarySegment>();
                foreach (CurveLoop loop in face.GetEdgesAsCurveLoops())
                    foreach (Curve c in loop)
                    {
                        IList<XYZ> pts = c.Tessellate();
                        for (int k = 1; k < pts.Count; k++)
                            list.Add(new BoundarySegment(pts[k - 1].X, pts[k - 1].Y, pts[k].X, pts[k].Y));
                    }
                return list.Count > 0 ? list : null;
            }
            catch
            {
                return null; // fall back to the sampled test only
            }
        }

        /// <summary>
        /// Exact test, whatever the gap width: finds every place the link crosses the roof outline or an
        /// opening edge, then checks the midpoint of each piece between crossings. The in/out state cannot
        /// change inside a piece, so one point per piece is enough.
        /// </summary>
        private bool ExactSegmentOnFace(XYZ a, XYZ b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
            double minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
            const double endEps = 1e-9;

            var ts = new List<double> { 0.0, 1.0 };
            foreach (BoundarySegment s in _boundary)
            {
                if (s.MaxX < minX || s.MinX > maxX || s.MaxY < minY || s.MinY > maxY) continue;

                double ex = s.X2 - s.X1, ey = s.Y2 - s.Y1;
                double denom = dx * ey - dy * ex;
                if (Math.Abs(denom) < 1e-12) continue; // parallel: covered by the piece midpoints

                double t = ((s.X1 - a.X) * ey - (s.Y1 - a.Y) * ex) / denom;
                double u = ((s.X1 - a.X) * dy - (s.Y1 - a.Y) * dx) / denom;
                if (u < 0.0 || u > 1.0 || t <= endEps || t >= 1.0 - endEps) continue;
                ts.Add(t);
            }

            ts.Sort();
            for (int k = 1; k < ts.Count; k++)
            {
                if (ts[k] - ts[k - 1] < 1e-7) continue; // two crossings at the same spot
                double mid = 0.5 * (ts[k - 1] + ts[k]);
                if (!PointOnTopFace(new XYZ(a.X + (b.X - a.X) * mid, a.Y + (b.Y - a.Y) * mid, a.Z + (b.Z - a.Z) * mid)))
                    return false;
            }
            return true;
        }

        private bool PointOnTopFace(XYZ p)
        {
            double tol = _projectionTolFt;
            IntersectionResult proj = _topFace.Project(p)
                ?? _topFace.Project(p + XYZ.BasisZ * tol)
                ?? _topFace.Project(p - XYZ.BasisZ * tol);
            if (proj == null) return false;

            try
            {
                return _topFace.IsInside(proj.UVPoint);
            }
            catch
            {
                BoundingBoxUV bb = _topFace.GetBoundingBox();
                UV uv = proj.UVPoint;
                return uv.U >= bb.Min.U && uv.U <= bb.Max.U && uv.V >= bb.Min.V && uv.V <= bb.Max.V;
            }
        }
    }
}
