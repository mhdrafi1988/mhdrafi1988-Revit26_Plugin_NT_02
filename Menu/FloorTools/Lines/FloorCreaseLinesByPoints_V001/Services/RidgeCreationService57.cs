using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.Models;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.Services
{
    public class RidgeCreationService
    {
        // ── Results ───────────────────────────────────────────────────────────────
        public string FirstResetStatus { get; private set; } = "N/A";
        public string LastResetStatus { get; private set; } = "N/A";
        public int TotalCalculated { get; private set; }
        public int TotalAdded { get; private set; }
        public int TotalFailed { get; private set; }
        public int TotalSkipped { get; private set; }
        public int DetailLinesAdded { get; private set; }

        // ── Callbacks ─────────────────────────────────────────────────────────────
        public Action OnFirstResetDone { get; set; }
        public Action OnPointsDone { get; set; }
        public Action OnLastResetDone { get; set; }

        /// <summary>
        /// Raised when points are removed by deduplication (before TX-2) or when
        /// a slab-geometry warning is caught by the TX-2 failure preprocessor.
        /// The string is human-readable and suitable for the pipeline log.
        /// </summary>
        public Action<string> OnSlabWarning { get; set; }

        // ── Dedup tolerance ───────────────────────────────────────────────────────
        // Points closer than this (Revit feet) are considered duplicates before TX-2.
        // 50 mm — prevents degenerate mesh triangles on complex floors (168+ boundary vertices).
        private const double DedupToleranceFt = 50.0 / 304.8;

        // ─────────────────────────────────────────────────────────────────────────

        public void CreateAll(
            Document doc,
            View activeView,
            Floor floor,
            VoronoiRidgeResult result,
            bool addDetailLines = true,
            bool addShapePoints = true)
        {
            TotalCalculated = 0;
            TotalAdded = 0;
            TotalFailed = 0;
            TotalSkipped = 0;
            DetailLinesAdded = 0;

            FirstResetStatus = "N/A";
            OnFirstResetDone?.Invoke();

            // ── TX-0: Detail lines ─────────────────────────────────────────────────
            // Independent transaction — runs (or is skipped) entirely on its own.
            // Its outcome has no bearing on whether the shape-point transactions
            // below run, and vice versa.
            if (addDetailLines)
            {
                ExecuteTx1_DetailLines(doc, activeView, result);
                DetailLinesAdded = result.CreatedDetailLineIds.Count;
            }
            else
            {
                OnSlabWarning?.Invoke("[TX-0] Detail lines skipped (unchecked).");
            }

            // ── Shape points (TX-1 → TX-2 → TX-3) ───────────────────────────────────
            // This whole chain is its own independent unit: if it's unchecked, or if
            // any transaction inside it fails, the detail-line transaction above is
            // already committed and is unaffected either way.
            if (!addShapePoints)
            {
                // Raw (non-deduplicated) counts are enough here — we're not about to
                // add any points, so paying the O(n²) dedup cost would be wasted work.
                TotalCalculated = result.ShapePoints.Count + result.RidgeEdgeMidPoints.Count;
                TotalSkipped = TotalCalculated;
                LastResetStatus = "Skipped — shape points unchecked";
                OnSlabWarning?.Invoke("[TX-1/2/3] Shape points skipped (unchecked).");
                OnPointsDone?.Invoke();
                OnLastResetDone?.Invoke();
                return;
            }

            // ── Pre-filter: deduplicate ShapePoints and RidgeEdgeMidPoints ────────
            //
            // On complex floors (many boundary vertices, tightly-clustered drains)
            // the Voronoi + clipping pipeline can produce shape points that are very
            // close together. Adding all of them causes the SlabShapeEditor mesh
            // triangulator to generate degenerate zero-area faces, which triggers
            // Revit's "Slab Shape Edit failed" warning at TX-2 commit time even
            // though every individual AddPoint call succeeded.
            // Deduplicating to 50 mm removes excess points while keeping the
            // geometrically meaningful ones (first occurrence of each cluster wins).
            // Only worth doing once we know shape points will actually be added.

            var dedupedShapePoints = DeduplicatePoints(result.ShapePoints, DedupToleranceFt);
            var dedupedMidPoints = DeduplicatePoints(result.RidgeEdgeMidPoints, DedupToleranceFt);

            int shapeDropped = result.ShapePoints.Count - dedupedShapePoints.Count;
            int midDropped = result.RidgeEdgeMidPoints.Count - dedupedMidPoints.Count;

            TotalCalculated = dedupedShapePoints.Count + dedupedMidPoints.Count;

            if (shapeDropped > 0 || midDropped > 0)
                OnSlabWarning?.Invoke(
                    $"[DEDUP] Removed {shapeDropped} ShapePoint(s) and {midDropped} MidPoint(s) " +
                    $"within {DedupToleranceFt * 304.8:F0} mm tolerance before TX-2.");

            RunShapePointChain(doc, floor, result, dedupedShapePoints, dedupedMidPoints);
        }

        // ── Shape-point chain: TX-1 (enable) → TX-2 (add) → TX-3 (zero) ───────────
        //
        // Extracted so the three possible outcomes — completed, editor unavailable,
        // or an add-time transaction failure — each set exactly one counter
        // (TotalAdded / TotalFailed / TotalSkipped) in one place, rather than being
        // set ad hoc across multiple branches in CreateAll.
        private void RunShapePointChain(
            Document doc,
            Floor floor,
            VoronoiRidgeResult result,
            List<XYZ> dedupedShapePoints,
            List<XYZ> dedupedMidPoints)
        {
            // TX-1: Enable shape editing.
            bool editingEnabled = EnableShapeEditing(doc, floor);

            if (!editingEnabled)
            {
                // Editor unavailable is a systemic skip, not a per-point add failure —
                // it belongs in TotalSkipped, not TotalFailed, so the log text and the
                // Skipped ⊘ card agree with each other.
                TotalSkipped = TotalCalculated;
                LastResetStatus = "Skipped — editor unavailable";
                OnSlabWarning?.Invoke(
                    "[TX-1] Shape points skipped — SlabShapeEditor unavailable on this floor.");
                OnPointsDone?.Invoke();
                OnLastResetDone?.Invoke();
                System.Diagnostics.Debug.WriteLine(
                    "[RidgeCreationService] Shape-point chain stopped — SlabShapeEditor " +
                    "unavailable. This does not affect the detail-line transaction.");
                return;
            }

            // Capture existing vertices BEFORE adding new points (for TX-3 zeroing).
            var existingVertices = GetCurrentVertexPositions(floor);

            // TX-2: AddSplitLine + AddPoint (deduplicated lists, failure preprocessor).
            ExecuteTx2_AddPoints(doc, floor, result, dedupedShapePoints, dedupedMidPoints);
            OnPointsDone?.Invoke();

            // TX-3: Zero ONLY the newly added vertices (set offset = 0).
            ExecuteTx3_ZeroNewVertices(doc, floor, existingVertices);
            OnLastResetDone?.Invoke();
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

        // ── TX-1: Enable shape editing ────────────────────────────────────────────

        private static bool EnableShapeEditing(Document doc, Floor floor)
        {
            try
            {
                using var tx = new Transaction(doc, "TX-1 | Enable Floor Shape Editing");
                tx.Start();
                var ed = floor.GetSlabShapeEditor();
                if (ed == null) { tx.RollBack(); return false; }
                if (!ed.IsEnabled)
                {
                    try { ed.Enable(); }
                    catch (OperationCanceledException)
                    {
                        throw; // user cancel: never swallowed as a failure
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[RidgeCreationService] TX-1 Enable warning: {ex.Message}");
                    }
                }
                tx.Commit();
                return true;
            }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[RidgeCreationService] TX-1 failed: {ex.Message}");
                return false;
            }
        }

        // ── Capture existing vertex positions (2D) ────────────────────────────────

        private static List<XYZ> GetCurrentVertexPositions(Floor floor)
        {
            var ed = floor.GetSlabShapeEditor();
            if (ed == null || !ed.IsEnabled) return new List<XYZ>();

            return ed.SlabShapeVertices
                .Cast<SlabShapeVertex>()
                .Select(v => v.Position)
                .Where(p => p != null)
                .Select(p => new XYZ(p.X, p.Y, 0))
                .ToList();
        }

        // ── TX-2: AddSplitLine + AddPoint ─────────────────────────────────────────
        //
        // Accepts deduplicated point lists so the mesh triangulator never receives
        // two points closer than DedupToleranceFt.
        //
        // A SlabShapePreprocessor is attached to the transaction so that any
        // slab-geometry warnings Revit raises at commit time are:
        //   • dismissed silently  (no blocking dialog shown to the user)
        //   • forwarded via OnSlabWarning → ViewModel → pipeline log (copyable)
        //
        // Top-face check runs before each AddPoint — points not on the floor's top
        // face are snapped inward up to 3 times before being skipped.

        private void ExecuteTx2_AddPoints(
            Document doc,
            Floor floor,
            VoronoiRidgeResult result,
            List<XYZ> shapePoints,
            List<XYZ> midPoints)
        {
            // Local counters — only copied into the public TotalAdded/TotalFailed
            // properties once tx.Commit() has actually succeeded below. If the
            // transaction throws or rolls back, none of these points are really
            // in the model, so the public counters must not claim otherwise.
            int added = 0;
            int failed = 0;

            try
            {
                using var tx = new Transaction(doc, "TX-2 | Add Voronoi Shape Points");

                // ── Failure handling ──────────────────────────────────────────────
                var preprocessor = new SlabShapePreprocessor();
                var failOpts = tx.GetFailureHandlingOptions();
                failOpts.SetFailuresPreprocessor(preprocessor);
                failOpts.SetClearAfterRollback(true);
                tx.SetFailureHandlingOptions(failOpts);

                tx.Start();

                var ed = floor.GetSlabShapeEditor();
                if (ed == null || !ed.IsEnabled)
                {
                    failed = TotalCalculated;
                    tx.Commit();
                    TotalAdded = added;
                    TotalFailed = failed;
                    return;
                }

                // Cache the top face once for the whole transaction.
                Face topFace = GetFloorTopFace(floor);

                result.CreatedShapePointIds.Clear();

                // ── ShapePoints (Voronoi vertices + inner-loop intersections) ─────
                foreach (var pt in shapePoints)
                {
                    ToolCancellation.CheckCurrent();
                    if (TryProcessPoint(floor, ref ed, pt, topFace, result, "ShapePoint"))
                        added++;
                    else
                        failed++;
                }

                // ── Mid-points: one per clipped ridge edge ────────────────────────
                foreach (var pt in midPoints)
                {
                    ToolCancellation.CheckCurrent();
                    if (TryProcessPoint(floor, ref ed, pt, topFace, result, "MidPoint"))
                        added++;
                    else
                        failed++;
                }

                tx.Commit();

                // Only now — after a confirmed commit — do these counts become real.
                TotalAdded = added;
                TotalFailed = failed;

                // Forward any slab warnings collected during commit to the pipeline log.
                foreach (var warning in preprocessor.CapturedWarnings)
                    OnSlabWarning?.Invoke(warning);
            }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception ex)
            {
                // Transaction did not commit — using Statement rollback semantics,
                // nothing from this attempt is actually in the model, regardless of
                // how far the loops above got. Report all calculated points as failed.
                TotalAdded = 0;
                TotalFailed = TotalCalculated;
                System.Diagnostics.Debug.WriteLine(
                    $"[RidgeCreationService] TX-2 outer error: {ex.Message}");
            }
        }

        // ── One point, one shared code path ────────────────────────────────────
        //
        // Both the ShapePoints loop and the MidPoints loop apply the exact same
        // four steps (top-face check → crease → re-fetch editor → add-with-nudge).
        // They used to be two independently maintained ~40-line copies that only
        // differed in a debug label — a real risk, since a fix applied to one loop
        // and not the other would silently reintroduce a mismatch (as already
        // happened once with the skip/fail counters). Now there is exactly one
        // place this logic lives.
        //
        // `ed` is passed by ref because Step 3 re-fetches the SlabShapeEditor after
        // cutting a crease line, and the caller's loop needs to keep using the
        // refreshed reference for the next point too.
        private static bool TryProcessPoint(
            Floor floor,
            ref SlabShapeEditor ed,
            XYZ pt,
            Face topFace,
            VoronoiRidgeResult result,
            string debugLabel)
        {
            try
            {
                // Step 1: top-face check with up to 3 snap attempts
                XYZ checkedPt = EnsureOnTopFace(pt, topFace, result.BoundaryPolygon);
                if (checkedPt == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[RidgeCreationService] {debugLabel} skipped — not on top face " +
                        $"after 3 snap attempts: X={pt.X * 304.8:F1} Y={pt.Y * 304.8:F1} mm");
                    return false;
                }

                // Step 2: cut a crease through the interior XY
                TryAddSplitLineThroughPoint(ed, checkedPt);

                // Step 3: re-fetch so crease vertices are visible
                ed = floor.GetSlabShapeEditor();
                if (ed == null || !ed.IsEnabled) return false;

                // Step 4: add the point; nudge toward centroid if outside slab face
                double slabZ = GetSlabBaseZ(ed);
                return TryAddPointWithNudge(ed, checkedPt, slabZ, result.FloorCentroid,
                                             result.BoundaryPolygon);
            }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[RidgeCreationService] TX-2 {debugLabel} skipped: {ex.Message}");
                return false;
            }
        }

        // ── TX-3: Zero only new vertices (offset = 0) ─────────────────────────────

        private void ExecuteTx3_ZeroNewVertices(
            Document doc,
            Floor floor,
            List<XYZ> existingVertices)
        {
            try
            {
                using var tx = new Transaction(doc, "TX-3 | Zero New Shape Point Offsets");
                tx.Start();

                var ed = floor.GetSlabShapeEditor();
                if (ed == null || !ed.IsEnabled)
                {
                    LastResetStatus = "Skipped — editor unavailable";
                    tx.Commit();
                    return;
                }

                var allVertices = ed.SlabShapeVertices
                    .Cast<SlabShapeVertex>()
                    .ToList();

                int zeroed = 0;
                const double snapTol = 1.0 / 304.8; // 1 mm

                foreach (var v in allVertices)
                {
                    ToolCancellation.CheckCurrent();
                    XYZ pos = v.Position;
                    if (pos == null) continue;

                    bool existed = existingVertices.Any(ex =>
                        Math.Abs(ex.X - pos.X) < snapTol &&
                        Math.Abs(ex.Y - pos.Y) < snapTol);

                    if (!existed)
                    {
                        try
                        {
                            ed.ModifySubElement(v, 0.0);
                            zeroed++;
                        }
                        catch (OperationCanceledException)
                        {
                            throw; // user cancel: never swallowed as a failure
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[RidgeCreationService] TX-3 ModifySubElement failed: {ex.Message}");
                        }
                    }
                }

                LastResetStatus = $"Success ({zeroed} new vertices zeroed)";
                tx.Commit();
            }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception ex)
            {
                LastResetStatus = $"Failed — {ex.Message}";
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
        //
        // Returns the input point unchanged if it projects onto the top face.
        // If not, snaps to the nearest boundary edge and retries up to 3 times
        // (each attempt steps 5 mm further inward). Returns null if all fail.

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
            if (topFace == null) return true; // no face available — skip check
            try { return topFace.Project(pt) != null; }
            catch { return false; }
        }

        private static Face GetFloorTopFace(Floor floor)
        {
            try
            {
                var opts = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
                GeometryElement geom = floor.get_Geometry(opts);
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
                            if (normal.Z <= 0.1) continue; // skip downward / vertical faces

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
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[RidgeCreationService] GetFloorTopFace failed: {ex.Message}");
                return null;
            }
        }

        // ── AddSplitLine helper (unchanged) ───────────────────────────────────────

        private static void TryAddSplitLineThroughPoint(SlabShapeEditor ed, XYZ pt)
        {
            var vertices = ed.SlabShapeVertices.Cast<SlabShapeVertex>().ToList();
            if (vertices.Count < 2) return;

            SlabShapeVertex leftV = null;
            SlabShapeVertex rightV = null;
            double bestL = double.MaxValue;
            double bestR = double.MaxValue;

            foreach (var v in vertices)
            {
                XYZ p = v.Position;
                if (p == null) continue;

                double dx = p.X - pt.X;
                double dy = p.Y - pt.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < 1e-6) continue;

                // Only vertices within ±45° horizontal cone (|dy| < |dx|)
                if (Math.Abs(dy) >= Math.Abs(dx)) continue;

                if (dx < 0 && dist < bestL) { bestL = dist; leftV = v; }
                if (dx > 0 && dist < bestR) { bestR = dist; rightV = v; }
            }

            if (leftV == null || rightV == null) return;

            try { ed.AddSplitLine(leftV, rightV); }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[RidgeCreationService] AddSplitLine skipped: {ex.Message}");
            }
        }

        // ── TX-0: Detail lines ────────────────────────────────────────────────────

        private void ExecuteTx1_DetailLines(Document doc, View activeView, VoronoiRidgeResult result)
        {
            ElementId lineStyleId = GetFirstLineStyleId(doc);

            using var tx = new Transaction(doc, "TX-0 | Create Voronoi Ridge Detail Lines");
            tx.Start();

            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Color(255, 0, 0));
            ogs.SetProjectionLineWeight(3);

            result.CreatedDetailLineIds.Clear();

            foreach (var edge in result.ClippedEdges)
            {
                ToolCancellation.CheckCurrent();
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
                catch (OperationCanceledException)
                {
                    throw; // user cancel: never swallowed as a failure
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[RidgeCreationService] TX-0 line skipped: {ex.Message}");
                }
            }

            tx.Commit();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        // Phase-1: centroid nudge (convex / near-edge failures)
        // Phase-2: nearest-edge inset (concave / amoeba floors)

        private static bool TryAddPointWithNudge(
            SlabShapeEditor ed,
            XYZ pt,
            double slabZ,
            XYZ centroid,
            List<XYZ> boundary)
        {
            // ── Phase 1: centroid nudge ───────────────────────────────────────────
            const int p1Steps = 10;
            const double p1Fraction = 0.05;

            for (int step = 0; step <= p1Steps; step++)
            {
                double t = step * p1Fraction;
                XYZ candidate = new XYZ(
                    pt.X + (centroid.X - pt.X) * t,
                    pt.Y + (centroid.Y - pt.Y) * t,
                    slabZ);
                try
                {
                    ed.AddPoint(candidate);
                    if (step > 0)
                        System.Diagnostics.Debug.WriteLine(
                            $"[RidgeCreationService] Phase-1 accepted after {step} nudge(s) " +
                            $"({t * 100:F0}% toward centroid)");
                    return true;
                }
                catch { /* keep nudging */ }
            }

            // ── Phase 2: nearest-edge inset ───────────────────────────────────────
            if (boundary == null || boundary.Count < 3)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[RidgeCreationService] Phase-2 skipped — no boundary polygon.");
                return false;
            }

            XYZ insetOrigin = GetNearestEdgeInsetPoint(pt, boundary, insetMm: 5.0);
            if (insetOrigin == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[RidgeCreationService] Phase-2 skipped — could not compute inset point.");
                return false;
            }

            const int p2Steps = 10;
            const double p2StepMm = 5.0; // walk further inward 5 mm per step

            for (int step = 0; step < p2Steps; step++)
            {
                double extraInset = step * p2StepMm / 304.8; // mm → ft
                XYZ dir = GetNearestEdgeInwardNormal(pt, boundary);
                XYZ candidate = new XYZ(
                    insetOrigin.X + dir.X * extraInset,
                    insetOrigin.Y + dir.Y * extraInset,
                    slabZ);
                try
                {
                    ed.AddPoint(candidate);
                    System.Diagnostics.Debug.WriteLine(
                        $"[RidgeCreationService] Phase-2 accepted at step {step} " +
                        $"(inset {5 + step * 5} mm from nearest edge)");
                    return true;
                }
                catch { /* keep stepping */ }
            }

            System.Diagnostics.Debug.WriteLine(
                "[RidgeCreationService] Both phases failed — point skipped.");
            return false;
        }

        // Returns the midpoint of the nearest boundary edge offset inward by insetMm.
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
                    // Edge tangent → inward normal (rotate 90°; sign from polygon winding)
                    double ex = b.X - a.X, ey = b.Y - a.Y;
                    double len = Math.Sqrt(ex * ex + ey * ey);
                    if (len < 1e-9) continue;
                    // For CCW winding the left normal (-ey, ex) points inward
                    double nx = -ey / len, ny = ex / len;
                    // Validate direction: nudged midpoint should be inside polygon
                    XYZ test = new XYZ(mid.X + nx * insetFt, mid.Y + ny * insetFt, 0);
                    if (!FloorGeometry2D.PointInPolygon(test, boundary))
                    { nx = -nx; ny = -ny; } // flip for CW winding
                    bestPoint = new XYZ(mid.X + nx * insetFt, mid.Y + ny * insetFt, 0);
                }
            }
            return bestPoint;
        }

        // Returns the inward unit normal of the nearest boundary edge (used for Phase-2 stepping).
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
                    if (!FloorGeometry2D.PointInPolygon(test, boundary)) { nx = -nx; ny = -ny; }
                    bestNx = nx; bestNy = ny;
                }
            }
            return new XYZ(bestNx, bestNy, 0);
        }

        private static double GetSlabBaseZ(SlabShapeEditor ed)
        {
            var vertices = ed.SlabShapeVertices.Cast<SlabShapeVertex>().ToList();
            if (vertices.Count == 0) return 0.0;

            // The floor may carry genuine slope from a prior AutoSlopeByPoint run.
            // Taking the plain minimum Z would pick up one of those low drain points
            // instead of the actual flat reference plane. The flat plane is still
            // represented by the majority of vertices (every untouched corner/edge
            // point), so the most frequent Z cluster within 1 mm tolerance is used.
            const double zTol = 1.0 / 304.8; // 1 mm
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
    }
}