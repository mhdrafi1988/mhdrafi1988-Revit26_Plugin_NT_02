using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.RoofTag.V017.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofTag.V017
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoofTagCommand : IExternalCommand
    {
        private const double PointDedupTolFt = 10.0 / 304.8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return ExecuteInternal(commandData, ref message);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (System.Exception ex)
            {
                Logger.Error("RoofTagCommand", ex);
                message = ex.Message;
                TaskDialog.Show("Roof Tag V015 (FaceRef)", $"An unexpected error occurred: {ex.Message}");
                return Result.Failed;
            }
        }

        private Result ExecuteInternal(ExternalCommandData commandData, ref string message)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument    uiDoc = uiApp.ActiveUIDocument;
            Document      doc   = uiDoc.Document;

            // ── 1. Select roof ───────────────────────────────────────────
            RoofBase roof = SelectionHelper.SelectRoof(uiDoc);
            if (roof == null)
            {
                TaskDialog.Show("Roof Tag V015 (FaceRef)", "No roof selected.");
                return Result.Cancelled;
            }

            // ── 2. Ensure SlabShapeEditor is enabled ─────────────────────
            SlabShapeEditor editor = roof.GetSlabShapeEditor();
            if (editor != null && !editor.IsEnabled)
            {
                using Transaction tx = new Transaction(doc, "Enable Slab Shape Editor");
                tx.Start();
                editor.Enable();
                tx.Commit();
            }

            // ── 3. Show settings window ──────────────────────────────────
            RoofTagWindow window = new RoofTagWindow(uiApp);
            window.Show();

            RoofTagViewModel vm = (RoofTagViewModel)window.DataContext;
            vm.ResetCounters();

            // ── 4. Collect points ────────────────────────────────────────
            List<XYZ> rawPoints;

            if (vm.UseManualMode)
            {
                IList<Reference> refs = uiDoc.Selection.PickObjects(
                    ObjectType.PointOnElement,
                    "Select points on roof");
                rawPoints = refs.Select(r => r.GlobalPoint).ToList();
            }
            else
            {
                rawPoints = RoofTagGeometryHelper.GetExactShapeVertices(roof);
            }

            if (rawPoints.Count == 0)
            {
                TaskDialog.Show("Roof Tag V015 (FaceRef)", "No valid points found.");
                return Result.Cancelled;
            }

            // ── 5. Deduplicate input points at 10 mm ─────────────────────
            List<XYZ> points = DeduplicatePoints(rawPoints, PointDedupTolFt);

            vm.AddLog(new LogEntry(LogLevel.Info,
                $"Roof id {roof.Id} — {rawPoints.Count} raw pts → {points.Count} unique"));

            // ── 5a. Rule 2 — interior loop reduction (runs before Rule 1) ────
            if (vm.InteriorLoopReductionEnabled)
            {
                double spacingFt = UnitUtils.ConvertToInternalUnits(vm.InteriorLoopSpacing, UnitTypeId.Millimeters);
                double toleranceFt = UnitUtils.ConvertToInternalUnits(vm.InteriorLoopBoundaryTolerance, UnitTypeId.Millimeters);
                List<XYZ> reduced = RoofTagLoopReductionHelper.Filter(
                    roof, points, spacingFt, toleranceFt, LoopScope.Interior,
                    out List<XYZ> skippedByLoop,
                    out List<string> loopLogs);

                foreach (string line in loopLogs)
                    vm.AddLog(new LogEntry(LogLevel.Info, line));

                foreach (XYZ sp in skippedByLoop)
                {
                    vm.AddLog(new LogEntry(LogLevel.Warning,
                        $"Skipped (interior loop reduction, spacing {vm.InteriorLoopSpacing:0} mm) — " +
                        $"({UnitUtils.ConvertFromInternalUnits(sp.X, UnitTypeId.Millimeters):0.0}, " +
                        $"{UnitUtils.ConvertFromInternalUnits(sp.Y, UnitTypeId.Millimeters):0.0}, " +
                        $"{UnitUtils.ConvertFromInternalUnits(sp.Z, UnitTypeId.Millimeters):0.0})"));
                }

                vm.AddLog(new LogEntry(LogLevel.Info,
                    $"Interior loop reduction — {points.Count} pts → {reduced.Count} after rule 2 " +
                    $"(spacing {vm.InteriorLoopSpacing:0} mm, tol {vm.InteriorLoopBoundaryTolerance:0} mm)"));

                points = reduced;
            }

            // ── 5b. Rule 3 — exterior loop reduction (runs after Rule 2, before Rule 1) ──
            if (vm.ExteriorLoopReductionEnabled)
            {
                double spacingFt = UnitUtils.ConvertToInternalUnits(vm.ExteriorLoopSpacing, UnitTypeId.Millimeters);
                double toleranceFt = UnitUtils.ConvertToInternalUnits(vm.ExteriorLoopBoundaryTolerance, UnitTypeId.Millimeters);
                List<XYZ> reduced = RoofTagLoopReductionHelper.Filter(
                    roof, points, spacingFt, toleranceFt, LoopScope.Exterior,
                    out List<XYZ> skippedByLoop,
                    out List<string> loopLogs);

                foreach (string line in loopLogs)
                    vm.AddLog(new LogEntry(LogLevel.Info, line));

                foreach (XYZ sp in skippedByLoop)
                {
                    vm.AddLog(new LogEntry(LogLevel.Warning,
                        $"Skipped (exterior loop reduction, spacing {vm.ExteriorLoopSpacing:0} mm) — " +
                        $"({UnitUtils.ConvertFromInternalUnits(sp.X, UnitTypeId.Millimeters):0.0}, " +
                        $"{UnitUtils.ConvertFromInternalUnits(sp.Y, UnitTypeId.Millimeters):0.0}, " +
                        $"{UnitUtils.ConvertFromInternalUnits(sp.Z, UnitTypeId.Millimeters):0.0})"));
                }

                vm.AddLog(new LogEntry(LogLevel.Info,
                    $"Exterior loop reduction — {points.Count} pts → {reduced.Count} after rule 3 " +
                    $"(spacing {vm.ExteriorLoopSpacing:0} mm, tol {vm.ExteriorLoopBoundaryTolerance:0} mm)"));

                points = reduced;
            }

            // ── 5c. Rule 1 — cluster filter (same elevation, within radius) ──
            if (vm.ClusterFilterEnabled)
            {
                double radiusFt = UnitUtils.ConvertToInternalUnits(vm.ClusterRadius, UnitTypeId.Millimeters);
                List<XYZ> clustered = RoofTagClusterHelper.Filter(points, radiusFt, out List<XYZ> skippedByCluster);

                foreach (XYZ sp in skippedByCluster)
                {
                    vm.AddLog(new LogEntry(LogLevel.Warning,
                        $"Skipped (clustered within {vm.ClusterRadius:0} mm, same elevation ±5 mm) — " +
                        $"({UnitUtils.ConvertFromInternalUnits(sp.X, UnitTypeId.Millimeters):0.0}, " +
                        $"{UnitUtils.ConvertFromInternalUnits(sp.Y, UnitTypeId.Millimeters):0.0}, " +
                        $"{UnitUtils.ConvertFromInternalUnits(sp.Z, UnitTypeId.Millimeters):0.0})"));
                }

                vm.AddLog(new LogEntry(LogLevel.Info,
                    $"Cluster filter — {points.Count} pts → {clustered.Count} after grouping " +
                    $"(radius {vm.ClusterRadius:0} mm, Z tol 5 mm)"));

                points = clustered;
            }

            // ── 6. Place tags ─────────────────────────────────────────────
            using (Transaction tx = new Transaction(doc, "Place Roof Tags V015 (FaceRef)"))
            {
                tx.Start();

                foreach (XYZ pt in points)
                {
                    RoofTagGeometryHelper.GetTaggingReferenceOnRoof(
                        roof, pt,
                        out Reference faceRef,
                        out XYZ projected);

                    XYZ origin = projected ?? pt;

                    LogEntry result = RoofTaggingService_FaceRef.PlaceTag(doc, faceRef, roof, origin, vm);
                    vm.AddLog(result);
                }

                tx.Commit();
            }

            return Result.Succeeded;
        }

        private static List<XYZ> DeduplicatePoints(List<XYZ> points, double tolFt)
        {
            List<XYZ> unique = new();

            foreach (XYZ candidate in points)
            {
                bool isDuplicate = unique.Any(u => u.DistanceTo(candidate) <= tolFt);
                if (!isDuplicate)
                    unique.Add(candidate);
            }

            return unique;
        }
    }
}
