using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.FloorTag.V001.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.Utilities;

namespace Revit26_Plugin_Revised.FloorTag.V001
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FloorTagCommand : IExternalCommand
    {
        private const double PointDedupTolFt = 10.0 / 304.8;

        /// <summary>Revit entry point; delegates to ToolGuard.RunCommand.</summary>
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
            catch (System.OperationCanceledException)
            {
                // Confirmed Esc / Cancel: the run group was rolled back as the exception unwound.
                return Result.Cancelled;
            }
            catch (System.Exception ex)
            {
                Logger.Error("FloorTagCommand", ex);
                message = ex.Message;
                TaskDialog.Show("Floor Tag", $"An unexpected error occurred: {ex.Message}");
                return Result.Failed;
            }
        }

        private Result ExecuteInternal(ExternalCommandData commandData, ref string message)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument    uiDoc = uiApp.ActiveUIDocument;
            Document      doc   = uiDoc.Document;

            // ── 1. Select floor ───────────────────────────────────────────
            Floor floor = SelectionHelper.SelectFloor(uiDoc);
            if (floor == null)
            {
                TaskDialog.Show("Floor Tag", "No floor selected.");
                return Result.Cancelled;
            }

            // Floor port: shape editing is turned on below, so refuse floors that can't be
            // changed (group, read-only, borrowed by another user, no shape-editing support).
            string blockReason = FloorShapeEditChecks.GetBlockReason(floor);
            if (blockReason != null)
            {
                TaskDialog.Show("Floor Tag", $"Floor {floor.Name} (Id {floor.Id.Value}) can't be changed: {blockReason}.");
                return Result.Cancelled;
            }
            // One group for the whole run, so a cancel also undoes enabling shape editing.
            using TransactionGroup runGroup = new TransactionGroup(doc, "Floor Tag");
            runGroup.Start();


            // ── 2. Ensure SlabShapeEditor is enabled ─────────────────────
            SlabShapeEditor editor = floor.GetSlabShapeEditor();
            if (editor != null && !editor.IsEnabled)
            {
                using Transaction tx = new Transaction(doc, "Enable Slab Shape Editor");
                tx.Start();
                editor.Enable();
                tx.Commit();
            }

            // ── 3. Show settings window ──────────────────────────────────
            FloorTagWindow window = new FloorTagWindow(uiApp);
            window.Show();

            FloorTagViewModel vm = (FloorTagViewModel)window.DataContext;
            vm.ResetCounters();

            // Esc / Cancel with a Yes-No confirm while the points are filtered and the tags placed.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.FloorTag, uiApp);

            // ── 4. Collect points ────────────────────────────────────────
            List<XYZ> rawPoints;

            if (vm.UseManualMode)
            {
                IList<Reference> refs = uiDoc.Selection.PickObjects(
                    ObjectType.PointOnElement,
                    "Select points on floor");
                rawPoints = refs.Select(r => r.GlobalPoint).ToList();
            }
            else
            {
                rawPoints = FloorTagGeometryHelper.GetExactShapeVertices(floor);
            }

            if (rawPoints.Count == 0)
            {
                TaskDialog.Show("Floor Tag", "No valid points found.");
                return Result.Cancelled;
            }

            // ── 5. Deduplicate input points at 10 mm ─────────────────────
            List<XYZ> points = DeduplicatePoints(rawPoints, PointDedupTolFt);

            vm.AddLog(new LogEntry(LogLevel.Info,
                $"Floor id {floor.Id} — {rawPoints.Count} raw pts → {points.Count} unique"));

            // ── 5a. Rule 2 — interior loop reduction (runs before Rule 1) ────
            if (vm.InteriorLoopReductionEnabled)
            {
                double spacingFt = UnitUtils.ConvertToInternalUnits(vm.InteriorLoopSpacing, UnitTypeId.Millimeters);
                double toleranceFt = UnitUtils.ConvertToInternalUnits(vm.InteriorLoopBoundaryTolerance, UnitTypeId.Millimeters);
                List<XYZ> reduced = FloorTagLoopReductionHelper.Filter(
                    floor, points, spacingFt, toleranceFt, LoopScope.Interior,
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
                List<XYZ> reduced = FloorTagLoopReductionHelper.Filter(
                    floor, points, spacingFt, toleranceFt, LoopScope.Exterior,
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
                List<XYZ> clustered = FloorTagClusterHelper.Filter(points, radiusFt, out List<XYZ> skippedByCluster);

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
            using (Transaction tx = new Transaction(doc, "Place Floor Tags (FaceRef)"))
            {
                tx.Start();

                foreach (XYZ pt in points)
                {
                    ToolCancellation.CheckCurrent();
                    FloorTagGeometryHelper.GetTaggingReferenceOnFloor(
                        floor, pt,
                        out Reference faceRef,
                        out XYZ projected);

                    XYZ origin = projected ?? pt;

                    LogEntry result = FloorTaggingService_FaceRef.PlaceTag(doc, faceRef, floor, origin, vm);
                    vm.AddLog(result);
                }

                ToolCancellation.CheckCurrent(); // last chance to cancel before the commit
                tx.Commit();
            }

            runGroup.Assimilate();
            return Result.Succeeded;
        }

        private static List<XYZ> DeduplicatePoints(List<XYZ> points, double tolFt)
        {
            List<XYZ> unique = new();

            foreach (XYZ candidate in points)
            {
                ToolCancellation.CheckCurrent();
                bool isDuplicate = unique.Any(u => u.DistanceTo(candidate) <= tolFt);
                if (!isDuplicate)
                    unique.Add(candidate);
            }

            return unique;
        }
    }
}
