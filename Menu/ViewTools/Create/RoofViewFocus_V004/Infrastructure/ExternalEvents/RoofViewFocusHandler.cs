using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.RoofViewFocus.V004.Core.Models;
using Revit26_Plugin_Revised.RoofViewFocus.V004.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofViewFocus.V004.Infrastructure.ExternalEvents
{
    /// <summary>
    /// Runs on Revit's API thread. Focuses the target plan view on the union of the
    /// selected roofs' bounding boxes inside ONE Transaction (rollback on any failure).
    ///
    /// Steps: 1 reset Scope Box → 2 collect roof bounds → 3 set crop box (+ margin)
    ///        → 4 enable Annotation Crop (+ margin).
    /// </summary>
    public sealed class RoofViewFocusHandler : IExternalEventHandler
    {
        /// <summary>Set by <see cref="RoofViewFocusEventManager.Raise"/> just before Event.Raise().</summary>
        public static FocusRequest? PendingRequest { get; set; }

        public string GetName() => "RoofViewFocusHandler";

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            FocusRequest? request = PendingRequest;
            PendingRequest = null;
            if (request == null) return;

            var result = new FocusResult();
            var sw = Stopwatch.StartNew();
            Log(request, LogLevel.Info, "Handler entered");

            try
            {
                Document doc = app.ActiveUIDocument?.Document
                    ?? throw new InvalidOperationException("No active document.");
                RunFocus(doc, request, result);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Failed = Math.Max(request.RoofUniqueIds.Count - result.Skipped, 0);
                result.Included = 0;

                Log(request, LogLevel.Error, $"Operation failed and was rolled back: {ex.Message}");
                TaskDialog.Show(RoofViewFocusDefaults.Title,
                    $"The operation failed and was rolled back.\n\n{ex.Message}");
            }
            finally
            {
                sw.Stop();
                result.Duration = sw.Elapsed;
                Log(request, LogLevel.Info, "Handler exited");
                Complete(request, result);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Main operation — one Transaction
        // ─────────────────────────────────────────────────────────────────────
        private static void RunFocus(Document doc, FocusRequest req, FocusResult result)
        {
            if (doc.GetElement(req.ViewUniqueId) is not ViewPlan view || view.IsTemplate)
                throw new InvalidOperationException(
                    "The target plan view is no longer available in the active document.");

            double viewTotalMm = req.ViewMarginMm + req.DefaultOffsetMm;
            double annTotalMm = req.AnnotationMarginMm + req.DefaultOffsetMm;

            Log(req, LogLevel.Info, $"Target view: '{view.Name}' (Id {view.Id.Value}, {view.ViewType})");
            Log(req, LogLevel.Info,
                $"Params: viewMargin={req.ViewMarginMm:0.##} mm, annotationMargin={req.AnnotationMarginMm:0.##} mm, " +
                $"defaultOffset={req.DefaultOffsetMm:0.##} mm → viewTotal={viewTotalMm:0.##} mm, annotationTotal={annTotalMm:0.##} mm");
            Log(req, LogLevel.Info, $"Roofs requested: {req.RoofUniqueIds.Count}");

            using (var tx = new Transaction(doc, "Roof View Focus"))
            {
                tx.Start();
                try
                {
                    // 1 — Scope Box → None
                    ResetScopeBox(doc, view, req);

                    // 2 — Union bounds of all roofs, in view-local coordinates
                    Bounds2D union = CollectRoofBounds(doc, view, req, result);
                    result.BoxWidthMm = CropBoundsCalculator.FeetToMm(union.Width);
                    result.BoxHeightMm = CropBoundsCalculator.FeetToMm(union.Height);
                    Log(req, LogLevel.Info,
                        $"Union bounding box: {result.BoxWidthMm:N0} × {result.BoxHeightMm:N0} mm ({result.Included} roof(s))");

                    // 3 — Crop box + margin
                    Bounds2D expanded = CropBoundsCalculator.Expand(union, CropBoundsCalculator.MmToFeet(viewTotalMm));
                    ApplyCropBox(view, expanded, viewTotalMm, req);
                    result.ViewMarginTotalMm = viewTotalMm;

                    // 4 — Annotation crop + margin
                    ApplyAnnotationCrop(view, annTotalMm, req);
                    result.AnnotationMarginTotalMm = annTotalMm;

                    TransactionStatus status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                        throw new InvalidOperationException($"Transaction did not commit (status: {status}).");

                    result.Success = true;
                    Log(req, LogLevel.Success, "Transaction committed");
                }
                catch
                {
                    if (tx.HasStarted())
                    {
                        try { tx.RollBack(); Log(req, LogLevel.Warning, "Transaction rolled back"); }
                        catch (Exception rbEx) { Log(req, LogLevel.Error, $"Rollback failed: {rbEx.Message}"); }
                    }
                    throw;
                }
            }
        }

        // ── Step 1 ───────────────────────────────────────────────────────────
        private static void ResetScopeBox(Document doc, View view, FocusRequest req)
        {
            Log(req, LogLevel.Info, "Step 1/4 — Reset Scope Box");

            Parameter? p = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            if (p == null)
            {
                Log(req, LogLevel.Info, "View has no Scope Box parameter — nothing to reset");
                return;
            }

            ElementId current = p.AsElementId();
            if (current == null || current.Value == ElementId.InvalidElementId.Value)
            {
                Log(req, LogLevel.Info, "Scope Box is already None");
                return;
            }

            string name = doc.GetElement(current)?.Name ?? $"Id {current.Value}";
            if (p.IsReadOnly)
            {
                Log(req, LogLevel.Warning, $"Scope Box '{name}' could not be cleared (parameter is read-only, e.g. view template)");
                return;
            }

            if (p.Set(ElementId.InvalidElementId))
                Log(req, LogLevel.Info, $"Scope Box '{name}' → None");
            else
                Log(req, LogLevel.Warning, $"Scope Box '{name}' could not be cleared");
        }

        // ── Step 2 ───────────────────────────────────────────────────────────
        private static Bounds2D CollectRoofBounds(Document doc, ViewPlan view, FocusRequest req, FocusResult result)
        {
            Log(req, LogLevel.Info, "Step 2/4 — Collect roof bounds");

            // Project bbox corners into the view's crop-box coordinate system so the
            // result is correct even if the view is not aligned with project X/Y.
            BoundingBoxXYZ cropBox = view.CropBox;
            Transform toViewSpace = cropBox.Transform.Inverse;

            var bounds = new List<Bounds2D>();
            foreach (string uid in req.RoofUniqueIds)
            {
                if (doc.GetElement(uid) is not RoofBase roof)
                {
                    result.Skipped++;
                    Log(req, LogLevel.Warning, $"Roof {uid} not found or not a roof — skipped");
                    continue;
                }

                long id = roof.Id.Value;
                string source = "view";
                BoundingBoxXYZ? bb = roof.get_BoundingBox(view);
                if (bb == null)
                {
                    bb = roof.get_BoundingBox(null);
                    source = "model";
                    if (bb != null)
                        Log(req, LogLevel.Warning, $"Roof {id}: no bounding box in this view — model bounding box used");
                }

                if (bb == null)
                {
                    result.Skipped++;
                    Log(req, LogLevel.Warning, $"Roof {id}: no bounding box — skipped");
                    continue;
                }

                Bounds2D b = ProjectToViewSpace(bb, toViewSpace);
                bounds.Add(b);
                Log(req, LogLevel.Info,
                    $"Roof {id}: bbox ({source}) {CropBoundsCalculator.FeetToMm(b.Width):N0} × {CropBoundsCalculator.FeetToMm(b.Height):N0} mm");
            }

            result.Included = bounds.Count;
            Log(req, LogLevel.Info, $"Collected {result.Included} of {req.RoofUniqueIds.Count} roof(s); {result.Skipped} skipped");

            if (bounds.Count == 0)
                throw new InvalidOperationException("None of the selected roofs has a usable bounding box.");

            return CropBoundsCalculator.Union(bounds);
        }

        private static Bounds2D ProjectToViewSpace(BoundingBoxXYZ bb, Transform toViewSpace)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            double[] xs = { bb.Min.X, bb.Max.X };
            double[] ys = { bb.Min.Y, bb.Max.Y };
            double[] zs = { bb.Min.Z, bb.Max.Z };

            foreach (double x in xs)
            foreach (double y in ys)
            foreach (double z in zs)
            {
                XYZ p = toViewSpace.OfPoint(bb.Transform.OfPoint(new XYZ(x, y, z)));
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            return new Bounds2D(minX, minY, maxX, maxY);
        }

        // ── Step 3 ───────────────────────────────────────────────────────────
        private static void ApplyCropBox(ViewPlan view, Bounds2D expanded, double totalMarginMm, FocusRequest req)
        {
            Log(req, LogLevel.Info, $"Step 3/4 — Crop box (margin {totalMarginMm:0.##} mm)");

            using (ViewCropRegionShapeManager mgr = view.GetCropRegionShapeManager())
            {
                if (mgr.ShapeSet)
                {
                    mgr.RemoveCropRegionShape();
                    Log(req, LogLevel.Warning, "Non-rectangular crop shape removed (crop reset to rectangle)");
                }
            }

            view.CropBoxActive = true;
            Log(req, LogLevel.Info, "Crop View: ON");
            view.CropBoxVisible = true;
            Log(req, LogLevel.Info, "Crop Region Visible: ON");

            // Keep the existing Z range and transform; replace only X/Y extents.
            BoundingBoxXYZ cropBox = view.CropBox;
            cropBox.Min = new XYZ(expanded.MinX, expanded.MinY, cropBox.Min.Z);
            cropBox.Max = new XYZ(expanded.MaxX, expanded.MaxY, cropBox.Max.Z);
            view.CropBox = cropBox;

            Log(req, LogLevel.Info,
                $"Crop box set: {CropBoundsCalculator.FeetToMm(expanded.Width):N0} × {CropBoundsCalculator.FeetToMm(expanded.Height):N0} mm");
        }

        // ── Step 4 ───────────────────────────────────────────────────────────
        private static void ApplyAnnotationCrop(ViewPlan view, double totalMarginMm, FocusRequest req)
        {
            Log(req, LogLevel.Info, $"Step 4/4 — Annotation crop (offset {totalMarginMm:0.##} mm)");

            Parameter? active = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
            using (ViewCropRegionShapeManager mgr = view.GetCropRegionShapeManager())
            {
                if (!mgr.CanHaveAnnotationCrop || active == null || active.IsReadOnly)
                {
                    Log(req, LogLevel.Warning, "Annotation Crop is not available for this view — skipped");
                    return;
                }

                if (!active.Set(1))
                {
                    Log(req, LogLevel.Warning, "Annotation Crop could not be enabled — skipped");
                    return;
                }
                Log(req, LogLevel.Info, "Annotation Crop: ON");

                // API offsets are in VIEW (paper) units, not model units.
                double feet = CropBoundsCalculator.ClampAnnotationOffset(
                    CropBoundsCalculator.MmToFeet(totalMarginMm), out bool clamped);
                if (clamped)
                    Log(req, LogLevel.Warning,
                        $"Annotation offset raised to Revit minimum ({CropBoundsCalculator.FeetToMm(feet):0.###} mm)");

                mgr.LeftAnnotationCropOffset = feet;
                mgr.RightAnnotationCropOffset = feet;
                mgr.TopAnnotationCropOffset = feet;
                mgr.BottomAnnotationCropOffset = feet;
                Log(req, LogLevel.Info, $"Annotation crop offsets set: {CropBoundsCalculator.FeetToMm(feet):0.###} mm (paper units) on all sides");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private static void Log(FocusRequest req, LogLevel level, string message)
        {
            try { req.OnLog?.Invoke(level, message); }
            catch { /* a UI logging failure must never break the Revit operation */ }
        }

        private static void Complete(FocusRequest req, FocusResult result)
        {
            try { req.OnCompleted?.Invoke(result); }
            catch { /* see Log() */ }
        }
    }
}
