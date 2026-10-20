using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RoofRidgeLines.V070.Models;

namespace Revit26_Plugin_Revised.RoofRidgeLines.V070.Services
{
    public class RidgeCreationService
    {
        // ── Results ───────────────────────────────────────────────────────────────
        public int TotalCalculated { get; private set; }
        public int TotalAdded { get; private set; }
        public int TotalFailed { get; private set; }

        /// <summary>Ridge points skipped because a shape point was already there (it is used as the ridge point).</summary>
        public int AlreadyThere { get; private set; }

        // ── Callbacks ─────────────────────────────────────────────────────────────
        public Action OnPointsDone { get; set; }

        /// <summary>
        /// Raised whenever a slab-geometry warning is caught by the TX-02
        /// failure preprocessor, or when points are removed by deduplication.
        /// The string is human-readable and suitable for the pipeline log.
        /// </summary>
        public Action<string> OnSlabWarning { get; set; }

        // ── Dedup tolerance ───────────────────────────────────────────────────────
        // Points closer than this (Revit feet) are considered duplicates before TX-02.
        // 50 mm — matches the default proximity-distance in the ViewModel.
        // Prevents degenerate mesh triangles on complex roofs (168+ boundary vertices).
        private const double DedupToleranceFt = 50.0 / 304.8;

        // ─────────────────────────────────────────────────────────────────────────

        public void CreateAll(
            Document doc,
            View activeView,
            RoofBase roof,
            VoronoiRidgeResult result,
            bool addDetailLines,
            bool addShapePoints,
            ElementId selectedLineStyleId)
        {
            // ── Pre-filter: deduplicate ShapePoints and RidgeEdgeMidPoints ────────
            //
            // On complex roofs (many boundary vertices, tightly-clustered drains)
            // the Voronoi + clipping pipeline can produce shape points that are very
            // close together. Adding all of them causes the SlabShapeEditor mesh
            // triangulator to generate degenerate zero-area faces, which triggers
            // Revit's "Slab Shape Edit failed" warning at TX-02 commit time even
            // though every individual AddPoint call succeeded.
            // Deduplicating to 50 mm removes excess points while keeping the
            // geometrically meaningful ones (first occurrence of each cluster wins).

            var dedupedShapePoints = DeduplicatePoints(result.ShapePoints, DedupToleranceFt);
            var dedupedMidPoints = DeduplicatePoints(result.RidgeEdgeMidPoints, DedupToleranceFt);

            int shapeDropped = result.ShapePoints.Count - dedupedShapePoints.Count;
            int midDropped = result.RidgeEdgeMidPoints.Count - dedupedMidPoints.Count;

            TotalCalculated = 0;
            TotalAdded = 0;
            TotalFailed = 0;
            AlreadyThere = 0;

            if (shapeDropped > 0 || midDropped > 0)
                OnSlabWarning?.Invoke(
                    $"[DEDUP] Removed {shapeDropped} ShapePoint(s) and {midDropped} MidPoint(s) " +
                    $"within {DedupToleranceFt * 304.8:F0} mm tolerance before TX-02.");

            // TX-01: Detail lines — runs first, no dependency on shape editor.
            if (addDetailLines)
            {
                ExecuteTx1_DetailLines(doc, activeView, result, selectedLineStyleId);
            }
            else
            {
                OnSlabWarning?.Invoke("[STEP 7] Detail lines skipped (unchecked).");
            }

            if (!addShapePoints)
            {
                OnSlabWarning?.Invoke("[STEP 7] Shape points skipped (unchecked).");
                OnPointsDone?.Invoke();
                return;
            }

            TotalCalculated = dedupedShapePoints.Count + dedupedMidPoints.Count;

            // TX-03: Enable shape editing only. Does not touch existing vertices.
            bool editingEnabled = EnableShapeEditing(doc, roof);

            if (!editingEnabled)
            {
                TotalFailed = TotalCalculated;
                OnPointsDone?.Invoke();
                System.Diagnostics.Debug.WriteLine(
                    "[RidgeCreationService] Model update skipped — SlabShapeEditor " +
                    "unavailable. Detail lines were created in TX-01 regardless.");
                return;
            }

            // TX-02: AddSplitLine + AddPoint using the deduplicated point lists.
            // Inner-loop intersection points are already inside result.ShapePoints
            // (added by InnerLoopIntersectionService), so no separate loop is needed.
            ExecuteTx2_AddPoints(doc, roof, result, dedupedShapePoints, dedupedMidPoints);
            OnPointsDone?.Invoke();
        }

        // ── Deduplication ─────────────────────────────────────────────────────────
        //
        // Greedy pass — keeps the first occurrence of each spatial cluster.
        // ShapePoints is processed before RidgeEdgeMidPoints in CreateAll so Voronoi
        // vertices always win over mid-points when they are near-coincident.

        private static List<XYZ> DeduplicatePoints(List<XYZ> points, double toleranceFt)
        {
            var kept = new List<XYZ>(points.Count);
            foreach (var pt in points)
            {
                bool isDup = kept.Any(k =>
                    Math.Abs(k.X - pt.X) < toleranceFt &&
                    Math.Abs(k.Y - pt.Y) < toleranceFt &&
                    Math.Sqrt(Math.Pow(k.X - pt.X, 2) + Math.Pow(k.Y - pt.Y, 2)) < toleranceFt);

                if (!isDup) kept.Add(pt);
            }
            return kept;
        }

        // ── TX-03: Enable shape editing ───────────────────────────────────────────

        private bool EnableShapeEditing(Document doc, RoofBase roof)
        {
            try
            {
                using var tx = new Transaction(doc, "TX-03 | Enable Roof Shape Editing");
                tx.Start();

                var ed = roof.GetSlabShapeEditor();
                if (ed == null) { tx.RollBack(); return false; }

                if (!ed.IsEnabled)
                {
                    try { ed.Enable(); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[RidgeCreationService] TX-03 Enable warning: {ex.Message}");
                    }
                }

                tx.Commit();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[RidgeCreationService] TX-03 failed: {ex.Message}");
                return false;
            }
        }

        // ── TX-02: AddPoint, then split lines along the ridges ────────────────────
        //
        // Accepts deduplicated point lists so the mesh triangulator never receives
        // two points closer than DedupToleranceFt.
        //
        // Fixed in 70.1.1 (each of these moved the roof or bent it in the wrong place,
        // so the slope worked out from these points was wrong):
        //   • Height: each point is placed ON the current top surface at its XY
        //     (was: the most common height of the existing points, which dented or
        //     lifted a roof that was already sloped).
        //   • Rejected points: nudged at most 60 mm toward the roof centre, and the
        //     edge inset is used only for a point that sits on the boundary (was: up
        //     to 50 % of the way to the roof centre — metres off the ridge — or
        //     snapped to an inset of whichever edge was nearest).
        //   • Split lines: added along each ridge segment, through the points on it,
        //     after all points exist (was: before each point, between its nearest
        //     left and right vertices — lines that neither passed through the point
        //     nor followed the ridge, creasing the roof in random places).
        //   • Failures are reported through OnSlabWarning instead of only Debug.
        //
        // A SlabShapePreprocessor is attached to the transaction so that any
        // slab-geometry warnings Revit raises at commit time are:
        //   • dismissed silently  (no blocking dialog shown to the user)
        //   • forwarded via OnSlabWarning → ViewModel → pipeline log (copyable)

        private void ExecuteTx2_AddPoints(
            Document doc,
            RoofBase roof,
            VoronoiRidgeResult result,
            List<XYZ> shapePoints,
            List<XYZ> midPoints)
        {
            try
            {
                using var tx = new Transaction(doc, "TX-02 | Add Points");

                // ── Failure handling ──────────────────────────────────────────────
                var preprocessor = new SlabShapePreprocessor();
                var failOpts = tx.GetFailureHandlingOptions();
                failOpts.SetFailuresPreprocessor(preprocessor);
                failOpts.SetClearAfterRollback(true);
                tx.SetFailureHandlingOptions(failOpts);

                tx.Start();

                var ed = roof.GetSlabShapeEditor();
                if (ed == null || !ed.IsEnabled)
                {
                    TotalFailed = TotalCalculated;
                    tx.Commit();
                    return;
                }

                Face topFace = GetRoofTopFace(roof);
                var surface = new TopSurface(roof, GetSlabBaseZ(ed));
                result.CreatedShapePointIds.Clear();

                // Voronoi vertices + inner-loop intersections first, then edge midpoints.
                foreach (var pt in shapePoints.Concat(midPoints))
                {
                    try
                    {
                        XYZ checkedPt = EnsureOnTopFace(pt, topFace, result.BoundaryPolygon);
                        if (checkedPt == null)
                        {
                            TotalFailed++;
                            OnSlabWarning?.Invoke(
                                $"[TX-02] Point skipped — not on the roof's top face: X={pt.X * 304.8:F0} Y={pt.Y * 304.8:F0} mm");
                            continue;
                        }

                        // A shape point already there (e.g. from an earlier tool) is the ridge point.
                        if (ed.SlabShapeVertices.Cast<SlabShapeVertex>().Any(v =>
                                Math.Sqrt(Math.Pow(v.Position.X - checkedPt.X, 2) + Math.Pow(v.Position.Y - checkedPt.Y, 2)) <= OnRidgeToleranceFt))
                        {
                            AlreadyThere++;
                            continue;
                        }

                        if (TryAddPointWithNudge(ed, checkedPt, surface, result.RoofCentroid, result.BoundaryPolygon))
                            TotalAdded++;
                        else
                        {
                            TotalFailed++;
                            OnSlabWarning?.Invoke(
                                $"[TX-02] Revit rejected the point at X={pt.X * 304.8:F0} Y={pt.Y * 304.8:F0} mm, even nudged up to 60 mm.");
                        }
                    }
                    catch (Exception ex)
                    {
                        TotalFailed++;
                        OnSlabWarning?.Invoke($"[TX-02] Point skipped: {ex.Message}");
                    }
                }

                int splits = AddRidgeSplitLines(roof, result.ClippedEdges, out int splitFailures);
                OnSlabWarning?.Invoke($"[TX-02] {splits} split line(s) added along the ridges" +
                                      (splitFailures > 0 ? $", {splitFailures} refused by Revit." : "."));

                tx.Commit();

                // Forward any slab warnings collected during commit to the pipeline log
                foreach (var warning in preprocessor.CapturedWarnings)
                    OnSlabWarning?.Invoke(warning);
            }
            catch (Exception ex)
            {
                TotalFailed = TotalCalculated - TotalAdded;
                OnSlabWarning?.Invoke($"[TX-02] Stopped: {ex.Message}");
            }
        }

        // ── IFailuresPreprocessor — slab shape warning interceptor ────────────────
        //
        // Revit raises a Warning (not an Error) when the mesh triangulation produces
        // degenerate faces after AddPoint commits. This preprocessor:
        //   • calls DeleteWarning() on every Warning severity failure → no dialog
        //   • stores a human-readable description in CapturedWarnings
        //   • returns ProceedWithRollBack only if an actual Error is present
        //     (Errors cannot be silently dismissed)

        private sealed class SlabShapePreprocessor : IFailuresPreprocessor
        {
            public List<string> CapturedWarnings { get; } = new List<string>();

            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                var failures = accessor.GetFailureMessages().ToList();
                if (failures.Count == 0)
                    return FailureProcessingResult.Continue;

                bool hasErrors = false;

                foreach (var f in failures)
                {
                    string desc =
                        $"[SLAB WARNING] {f.GetDescriptionText()} " +
                        $"(severity: {f.GetSeverity()}" +
                        (f.GetFailingElementIds().Any()
                            ? $", elements: {string.Join(", ", f.GetFailingElementIds().Select(id => id.Value))}"
                            : "") +
                        ")";

                    CapturedWarnings.Add(desc);
                    System.Diagnostics.Debug.WriteLine($"[SlabShapePreprocessor] {desc}");

                    if (f.GetSeverity() == FailureSeverity.Warning)
                        accessor.DeleteWarning(f);
                    else
                        hasErrors = true;
                }

                return hasErrors
                    ? FailureProcessingResult.ProceedWithRollBack
                    : FailureProcessingResult.Continue;
            }
        }

        // ── Top-face check ────────────────────────────────────────────────────────

        private static XYZ EnsureOnTopFace(XYZ pt, Face topFace, List<XYZ> boundary)
        {
            if (IsOnTopFace(pt, topFace)) return pt;
            if (topFace == null || boundary == null || boundary.Count < 3) return null;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                double insetMm = attempt * 5.0;
                XYZ snapped = GetNearestEdgeInsetPoint(pt, boundary, insetMm);
                if (snapped != null && IsOnTopFace(snapped, topFace))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[RidgeCreationService] Top-face snap accepted at attempt {attempt} " +
                        $"({insetMm} mm inset): X={snapped.X * 304.8:F1} Y={snapped.Y * 304.8:F1} mm");
                    return snapped;
                }
            }

            return null;
        }

        private static bool IsOnTopFace(XYZ pt, Face topFace)
        {
            if (topFace == null) return true;
            try { return topFace.Project(pt) != null; }
            catch { return false; }
        }

        private static Face GetRoofTopFace(RoofBase roof)
        {
            try
            {
                var opts = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
                GeometryElement geom = roof.get_Geometry(opts);
                if (geom == null) return null;

                Face best = null;
                double bestZ = double.MinValue;

                foreach (GeometryObject obj in geom)
                {
                    Solid solid = obj as Solid;
                    if (solid == null || solid.Faces.Size == 0) continue;

                    foreach (Face face in solid.Faces)
                    {
                        try
                        {
                            XYZ normal = face.ComputeNormal(new UV(0.5, 0.5));
                            if (normal.Z <= 0.1) continue;

                            BoundingBoxUV bb = face.GetBoundingBox();
                            UV mid = new UV((bb.Min.U + bb.Max.U) / 2.0,
                                                       (bb.Min.V + bb.Max.V) / 2.0);
                            XYZ centroid = face.Evaluate(mid);
                            if (centroid.Z > bestZ) { bestZ = centroid.Z; best = face; }
                        }
                        catch { /* skip degenerate face */ }
                    }
                }

                return best;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[RidgeCreationService] GetRoofTopFace failed: {ex.Message}");
                return null;
            }
        }

        // ── Split lines along the ridges ──────────────────────────────────────────
        //
        // For each clipped ridge edge, the shape points lying on it (within 5 mm,
        // its two ends included) are joined in order by split lines, so the roof
        // creases exactly along the ridge. Vertices are re-read for every line:
        // AddSplitLine can invalidate SlabShapeVertex objects read before it.

        private const double OnRidgeToleranceFt = 5.0 / 304.8;

        private static int AddRidgeSplitLines(RoofBase roof, List<(XYZ Start, XYZ End)> edges, out int failures)
        {
            failures = 0;
            int added = 0;
            foreach (var edge in edges)
            {
                XYZ a = new XYZ(edge.Start.X, edge.Start.Y, 0), b = new XYZ(edge.End.X, edge.End.Y, 0);
                XYZ d = b - a;
                double len2 = d.DotProduct(d);
                if (len2 < 1e-12) continue;

                var ed = roof.GetSlabShapeEditor();
                if (ed == null || !ed.IsEnabled) return added;

                // Positions (by fraction along the edge) of the vertices on it.
                var onEdge = new List<(double T, XYZ Position)>();
                foreach (SlabShapeVertex v in ed.SlabShapeVertices)
                {
                    XYZ p = new XYZ(v.Position.X, v.Position.Y, 0);
                    double t = (p - a).DotProduct(d) / len2;
                    if (t < -1e-6 || t > 1 + 1e-6) continue;
                    if ((a + d * t).DistanceTo(p) <= OnRidgeToleranceFt)
                        onEdge.Add((t, v.Position));
                }

                // A ridge end Revit only accepted after a nudge sits up to 60 mm off the line.
                double endReach = 65.0 / 304.8;
                if (!onEdge.Any(o => o.T * Math.Sqrt(len2) <= OnRidgeToleranceFt))
                    AddNearest(ed, edge.Start, endReach, 0, onEdge);
                if (!onEdge.Any(o => (1 - o.T) * Math.Sqrt(len2) <= OnRidgeToleranceFt))
                    AddNearest(ed, edge.End, endReach, 1, onEdge);

                var ordered = onEdge.OrderBy(o => o.T).ToList();
                for (int i = 0; i + 1 < ordered.Count; i++)
                {
                    if (ordered[i + 1].T - ordered[i].T < 1e-6) continue;
                    var fresh = roof.GetSlabShapeEditor();
                    SlabShapeVertex v1 = FindVertex(fresh, ordered[i].Position);
                    SlabShapeVertex v2 = FindVertex(fresh, ordered[i + 1].Position);
                    if (v1 == null || v2 == null) { failures++; continue; }
                    try { fresh.AddSplitLine(v1, v2); added++; }
                    catch { failures++; }
                }
            }

            return added;
        }

        private static void AddNearest(SlabShapeEditor ed, XYZ target, double reach, double t, List<(double T, XYZ Position)> into)
        {
            SlabShapeVertex best = null;
            double bestD = reach;
            foreach (SlabShapeVertex v in ed.SlabShapeVertices)
            {
                double dd = Math.Sqrt(Math.Pow(v.Position.X - target.X, 2) + Math.Pow(v.Position.Y - target.Y, 2));
                if (dd <= bestD) { bestD = dd; best = v; }
            }
            if (best != null) into.Add((t, best.Position));
        }

        private static SlabShapeVertex FindVertex(SlabShapeEditor ed, XYZ position)
        {
            foreach (SlabShapeVertex v in ed.SlabShapeVertices)
                if (Math.Abs(v.Position.X - position.X) < 1e-6 && Math.Abs(v.Position.Y - position.Y) < 1e-6)
                    return v;
            return null;
        }

        // ── TryAddPointWithNudge ──────────────────────────────────────────────────
        //
        // The point goes on the current surface at its XY. Rejected: moved toward the
        // roof centre by at most 60 mm (3, 8, 15, 30, 60). A point on the boundary
        // (within 50 mm of it) may instead be inset 5–50 mm from that edge. Never
        // further, so a ridge point stays on its ridge.

        private static readonly double[] NudgeMm = { 0, 3, 8, 15, 30, 60 };

        private static bool TryAddPointWithNudge(
            SlabShapeEditor ed,
            XYZ pt,
            TopSurface surface,
            XYZ centroid,
            List<XYZ> boundary)
        {
            XYZ toCentre = new XYZ(centroid.X - pt.X, centroid.Y - pt.Y, 0);
            XYZ dir = toCentre.GetLength() > 1e-9 ? toCentre.Normalize() : XYZ.Zero;

            foreach (double mm in NudgeMm)
            {
                if (mm > 0 && dir.IsZeroLength()) break;
                if (TryAdd(ed, surface, pt.X + dir.X * mm / 304.8, pt.Y + dir.Y * mm / 304.8))
                    return true;
            }

            if (boundary == null || boundary.Count < 3 || DistanceToBoundary(pt, boundary) > 50.0 / 304.8)
                return false;

            XYZ insetOrigin = GetNearestEdgeInsetPoint(pt, boundary, insetMm: 5.0);
            if (insetOrigin == null) return false;
            XYZ inward = GetNearestEdgeInwardNormal(pt, boundary);
            for (int step = 0; step < 10; step++)
            {
                double extra = step * 5.0 / 304.8;
                if (TryAdd(ed, surface, insetOrigin.X + inward.X * extra, insetOrigin.Y + inward.Y * extra))
                    return true;
            }

            return false;
        }

        private static bool TryAdd(SlabShapeEditor ed, TopSurface surface, double x, double y)
        {
            try
            {
                ed.AddPoint(new XYZ(x, y, surface.ZAt(x, y)));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double DistanceToBoundary(XYZ pt, List<XYZ> boundary)
        {
            double best = double.MaxValue;
            for (int i = 0; i < boundary.Count; i++)
            {
                XYZ a = boundary[i], b = boundary[(i + 1) % boundary.Count];
                double ex = b.X - a.X, ey = b.Y - a.Y;
                double len2 = ex * ex + ey * ey;
                double t = len2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((pt.X - a.X) * ex + (pt.Y - a.Y) * ey) / len2));
                double dx = a.X + ex * t - pt.X, dy = a.Y + ey * t - pt.Y;
                best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
            }
            return best;
        }

        /// <summary>Height of the roof's current top surface at a plan point (vertical projection).</summary>
        private sealed class TopSurface
        {
            private readonly List<Face> _faces;
            private readonly double _fallbackZ;

            public TopSurface(RoofBase roof, double fallbackZ)
            {
                _fallbackZ = fallbackZ;
                try
                {
                    _faces = HostObjectUtils.GetTopFaces(roof)
                        .Select(r => roof.GetGeometryObjectFromReference(r) as Face)
                        .Where(f => f != null)
                        .ToList();
                }
                catch
                {
                    _faces = new List<Face>();
                }
            }

            public double ZAt(double x, double y)
            {
                foreach (Face f in _faces)
                {
                    if (f is PlanarFace pf && Math.Abs(pf.FaceNormal.Z) > 1e-6)
                    {
                        XYZ n = pf.FaceNormal, o = pf.Origin;
                        double z = o.Z - ((x - o.X) * n.X + (y - o.Y) * n.Y) / n.Z;
                        IntersectionResult hit = pf.Project(new XYZ(x, y, z));
                        if (hit != null && hit.Distance <= 1e-3) return z;
                    }
                }

                // Non-planar faces: nearest face point under the plan point.
                double bestDist = double.MaxValue, bestZ = _fallbackZ;
                foreach (Face f in _faces)
                {
                    IntersectionResult hit = f.Project(new XYZ(x, y, _fallbackZ));
                    if (hit == null) continue;
                    double dxy = Math.Sqrt(Math.Pow(hit.XYZPoint.X - x, 2) + Math.Pow(hit.XYZPoint.Y - y, 2));
                    if (dxy < bestDist) { bestDist = dxy; bestZ = hit.XYZPoint.Z; }
                }
                return bestDist <= 0.05 ? bestZ : _fallbackZ;
            }
        }

        // ── TX-01: Detail Lines Placement ─────────────────────────────────────────

        private void ExecuteTx1_DetailLines(Document doc, View activeView, VoronoiRidgeResult result, ElementId selectedLineStyleId)
        {
            ElementId lineStyleId = (selectedLineStyleId != null && selectedLineStyleId != ElementId.InvalidElementId)
                ? selectedLineStyleId
                : GetFirstLineStyleId(doc);

            using var tx = new Transaction(doc, "TX-01 | Detail Lines Placement");
            tx.Start();

            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Color(255, 0, 0));
            ogs.SetProjectionLineWeight(3);

            result.CreatedDetailLineIds.Clear();

            foreach (var edge in result.ClippedEdges)
            {
                try
                {
                    if (edge.Start.DistanceTo(edge.End) < 1.0 / 304.8) continue;
                    Line line = Line.CreateBound(edge.Start, edge.End);
                    DetailLine dl = doc.Create.NewDetailCurve(activeView, line) as DetailLine;
                    if (dl == null) continue;
                    if (lineStyleId != ElementId.InvalidElementId)
                        dl.LineStyle = doc.GetElement(lineStyleId) as GraphicsStyle;
                    activeView.SetElementOverrides(dl.Id, ogs);
                    result.CreatedDetailLineIds.Add(dl.Id);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[RidgeCreationService] TX-01 line skipped: {ex.Message}");
                }
            }

            tx.Commit();
        }

        // ── Geometry helpers ──────────────────────────────────────────────────────

        private static XYZ GetNearestEdgeInsetPoint(XYZ pt, List<XYZ> boundary, double insetMm)
        {
            double insetFt = insetMm / 304.8;
            int n = boundary.Count;
            double bestDist = double.MaxValue;
            XYZ bestPoint = null;

            for (int i = 0; i < n; i++)
            {
                XYZ a = boundary[i];
                XYZ b = boundary[(i + 1) % n];
                XYZ mid = new XYZ((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, 0);
                double dist = Math.Sqrt(Math.Pow(pt.X - mid.X, 2) + Math.Pow(pt.Y - mid.Y, 2));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    double ex = b.X - a.X, ey = b.Y - a.Y;
                    double len = Math.Sqrt(ex * ex + ey * ey);
                    if (len < 1e-9) continue;
                    double nx = -ey / len, ny = ex / len;
                    XYZ test = new XYZ(mid.X + nx * insetFt, mid.Y + ny * insetFt, 0);
                    if (!RoofGeometry2D.PointInPolygon(test, boundary)) { nx = -nx; ny = -ny; }
                    bestPoint = new XYZ(mid.X + nx * insetFt, mid.Y + ny * insetFt, 0);
                }
            }
            return bestPoint;
        }

        private static XYZ GetNearestEdgeInwardNormal(XYZ pt, List<XYZ> boundary)
        {
            int n = boundary.Count;
            double bestDist = double.MaxValue;
            double bestNx = 0, bestNy = 0;

            for (int i = 0; i < n; i++)
            {
                XYZ a = boundary[i];
                XYZ b = boundary[(i + 1) % n];
                XYZ mid = new XYZ((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, 0);
                double dist = Math.Sqrt(Math.Pow(pt.X - mid.X, 2) + Math.Pow(pt.Y - mid.Y, 2));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    double ex = b.X - a.X, ey = b.Y - a.Y;
                    double len = Math.Sqrt(ex * ex + ey * ey);
                    if (len < 1e-9) continue;
                    double nx = -ey / len, ny = ex / len;
                    XYZ test = new XYZ(mid.X + nx / 304.8, mid.Y + ny / 304.8, 0);
                    if (!RoofGeometry2D.PointInPolygon(test, boundary)) { nx = -nx; ny = -ny; }
                    bestNx = nx; bestNy = ny;
                }
            }
            return new XYZ(bestNx, bestNy, 0);
        }

        private static double GetSlabBaseZ(SlabShapeEditor ed)
        {
            var vertices = ed.SlabShapeVertices.Cast<SlabShapeVertex>().ToList();
            if (vertices.Count == 0) return 0.0;

            const double zTol = 1.0 / 304.8;
            var zs = vertices.Select(v => v.Position.Z).OrderBy(z => z).ToList();

            double bestZ = zs[0];
            int bestCount = 0;
            int i = 0;
            while (i < zs.Count)
            {
                int j = i;
                while (j < zs.Count && zs[j] - zs[i] <= zTol) j++;
                int count = j - i;
                if (count > bestCount) { bestCount = count; bestZ = zs[i + (j - i) / 2]; }
                i = j;
            }
            return bestZ;
        }

        private static ElementId GetFirstLineStyleId(Document doc)
        {
            foreach (GraphicsStyle gs in new FilteredElementCollector(doc)
                         .OfClass(typeof(GraphicsStyle)))
            {
                if (gs.GraphicsStyleCategory?.Parent != null &&
                    gs.GraphicsStyleType == GraphicsStyleType.Projection)
                    return gs.Id;
            }
            return ElementId.InvalidElementId;
        }

        // ── Line style enumeration (for the UI's "Detail line style" ComboBox) ────
        //
        // Same filter as GetFirstLineStyleId — only projection-type GraphicsStyles
        // that belong to a subcategory (Parent != null), which excludes internal
        // styles not meant to be user-selectable.

        public static List<(string Name, ElementId Id)> GetAvailableLineStyles(Document doc)
        {
            var styles = new List<(string, ElementId)>();
            foreach (GraphicsStyle gs in new FilteredElementCollector(doc)
                         .OfClass(typeof(GraphicsStyle)))
            {
                if (gs.GraphicsStyleCategory?.Parent != null &&
                    gs.GraphicsStyleType == GraphicsStyleType.Projection)
                    styles.Add((gs.Name, gs.Id));
            }
            return styles.OrderBy(s => s.Item1).ToList();
        }

        // ── Line style lookup by name (used at Run time — no id map is kept) ──────
        public static ElementId GetLineStyleIdByName(Document doc, string name)
        {
            if (string.IsNullOrEmpty(name)) return ElementId.InvalidElementId;

            foreach (GraphicsStyle gs in new FilteredElementCollector(doc)
                         .OfClass(typeof(GraphicsStyle)))
            {
                if (gs.GraphicsStyleCategory?.Parent != null &&
                    gs.GraphicsStyleType == GraphicsStyleType.Projection &&
                    string.Equals(gs.Name, name, StringComparison.OrdinalIgnoreCase))
                    return gs.Id;
            }
            return ElementId.InvalidElementId;
        }
    }
}