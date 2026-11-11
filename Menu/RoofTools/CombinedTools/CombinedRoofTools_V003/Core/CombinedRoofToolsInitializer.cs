// =======================================================
// File: CombinedRoofToolsInitializer.cs
// Location: Core/
// Builds all 5 tool ViewModels for one picked roof, replicating each
// tool's own Command "pre-window setup" (flatten shape editor / initial
// geometry analysis / drain detection) so every tab is populated the
// instant the combined window opens — exactly like each tool's own
// standalone Command already does before showing its window. Must only
// ever run inside a valid Revit API context (an IExternalCommand.Execute
// or an IExternalEventHandler.Execute), same rule as every other tool in
// this suite.
//
// Each tool's failure is isolated: if a tool can't handle the picked
// roof (wrong roof type, unexpected geometry), that tool's ViewModel is
// left null and an "unavailable reason" message is set instead — the
// other tools still work normally.
// =======================================================

using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AutoSlopeByDrain.V007.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByDrain.V007.Core.Services;
using Revit26_Plugin_Revised.AutoSlopeByDrain.V007.UI.ViewModels;
using Revit26_Plugin_Revised.CreaserAdv.V010.Services;
using Revit26_Plugin_Revised.CreaserAdv.V010.ViewModels;
using Revit26_Plugin_Revised.InnerLoopDivider.V009.UI.ViewModels;
using Revit26_Plugin_Revised.InnerLoopsAndPerpendicular.V005.UI.ViewModels;
using Revit26_Plugin_Revised.OuterCurveDivider.V004.Core.Services;
using Revit26_Plugin_Revised.OuterCurveDivider.V004.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using ILDGeometry = Revit26_Plugin_Revised.InnerLoopDivider.V009.Core.Services.RoofGeometryService;
using ILPGeometry = Revit26_Plugin_Revised.InnerLoopsAndPerpendicular.V005.Core.Services.RoofGeometryService;

namespace Revit26_Plugin_Revised.CombinedRoofTools.V003.Core
{
    public static class CombinedRoofToolsInitializer
    {
        public static CombinedRoofToolsInitResult BuildAll(UIApplication uiApp, RoofBase roof)
        {
            UIDocument uidoc = uiApp.ActiveUIDocument;
            Document doc = uidoc.Document;

            var result = new CombinedRoofToolsInitResult
            {
                RoofLabel = $"{roof.Name}  ·  Id {roof.Id.Value}  ·  {roof.GetType().Name}"
            };

            // ── Enable shape editing & flatten ───────────────────────────────
            // Shared prerequisite for Inner Loop Divider and Inner Loops And
            // Perpendicular (both analyze SlabShapeVertex-driven loops).
            // Idempotent — safe even if already flattened.
            try
            {
                using (Transaction tx = new Transaction(doc, "Enable Shape Editing"))
                {
                    tx.Start();
                    var editor = roof.GetSlabShapeEditor();
                    if (!editor.IsEnabled)
                        editor.Enable();
                    foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                    {
                        ToolCancellation.CheckCurrent();
                        editor.ModifySubElement(v, 0.0);
                    }
                    tx.Commit();
                }
            }
            catch (OperationCanceledException)
            {
                throw; // user cancel: the transaction above is rolled back, nothing was flattened
            }
            catch (Exception ex)
            {
                string reason = $"Could not enable shape editing on this roof: {ex.Message}";
                result.InnerLoopDividerUnavailableReason = reason;
                result.InnerLoopsAndPerpendicularUnavailableReason = reason;
            }

            // ── Inner Loop Divider ───────────────────────────────────────────
            if (result.InnerLoopDividerUnavailableReason == null)
            {
                try
                {
                    var initialLoops = new ILDGeometry()
                        .ExtractCircularLoops(roof)
                        .Where(l => l.LoopType == "Inner")
                        .ToList();

                    result.InnerLoopDividerVm = new InnerLoopDividerViewModel(uiApp, roof.Id, initialLoops);
                }
                catch (Exception ex)
                {
                    result.InnerLoopDividerUnavailableReason = $"Inner Loop Divider could not analyze this roof: {ex.Message}";
                }
            }

            // ── Inner Loops And Perpendicular ────────────────────────────────
            if (result.InnerLoopsAndPerpendicularUnavailableReason == null)
            {
                try
                {
                    var allLoops = new ILPGeometry().ExtractCircularLoops(roof);
                    var outerLoop = allLoops.FirstOrDefault(l => l.LoopType == "Outer");
                    var innerLoops = allLoops.Where(l => l.LoopType == "Inner").ToList();

                    result.InnerLoopsAndPerpendicularVm =
                        new InnerLoopsAndPerpendicularViewModel(uiApp, roof.Id, innerLoops, outerLoop);
                }
                catch (Exception ex)
                {
                    result.InnerLoopsAndPerpendicularUnavailableReason = $"Inner Loops And Perpendicular could not analyze this roof: {ex.Message}";
                }
            }

            // ── Outer Curve Divider ──────────────────────────────────────────
            try
            {
                var edgeService = new CurveDivisionService();
                var initialEdges = edgeService.ExtractNonLinearEdges(roof, out int filteredLines);
                result.OuterCurveDividerVm = new CurveDividerViewModel(roof.Id, initialEdges, filteredLines);
            }
            catch (Exception ex)
            {
                result.OuterCurveDividerUnavailableReason = $"Outer Curve Divider could not analyze this roof: {ex.Message}";
            }

            // ── Auto Slope By Drain ──────────────────────────────────────────
            // Requires a FootPrintRoof — opening detection is Sketch-based, and
            // only FootPrintRoof carries a dependent Sketch element.
            if (roof is FootPrintRoof footprintRoof)
            {
                try
                {
                    var roofData = new RoofData { Roof = footprintRoof };

                    RoofGeometryService.InitializeRoofGeometry(footprintRoof, doc);

                    roofData.TopFace = RoofGeometryService.GetTopFace(footprintRoof);
                    if (roofData.TopFace == null)
                        throw new Exception("Could not find top face of the roof.");

                    roofData.Vertices.Clear();
                    var slabShapeEditor = footprintRoof.GetSlabShapeEditor();
                    foreach (SlabShapeVertex vertex in slabShapeEditor.SlabShapeVertices)
                        roofData.Vertices.Add(vertex);

                    var diagnosticLog = new List<LogEntry>();
                    var detectionService = new DrainDetectionService();
                    roofData.DetectedDrains = detectionService.DetectDrainsFromRoof(
                        footprintRoof, roofData.TopFace, roofData.Vertices, entry => diagnosticLog.Add(entry));

                    var vm = new AutoSlopeDrainViewModel(uidoc, uiApp, roofData);
                    foreach (var entry in diagnosticLog)
                        vm.LogEntries.Add(entry);

                    result.AutoSlopeByDrainVm = vm;
                }
                catch (Exception ex)
                {
                    result.AutoSlopeByDrainUnavailableReason = $"Auto Slope By Drain could not analyze this roof: {ex.Message}";
                }
            }
            else
            {
                result.AutoSlopeByDrainUnavailableReason =
                    "Auto Slope By Drain requires a sketch-based roof (FootPrintRoof) for opening detection. " +
                    $"The selected roof is a {roof.GetType().Name}, which this tool doesn't support.";
            }

            // ── Creaser Adv ───────────────────────────────────────────────────
            try
            {
                var log = new LoggingService("CreaserAdv");
                result.CreaserAdvVm = new CreaserAdvViewModel(uiApp, roof, log);
            }
            catch (Exception ex)
            {
                result.CreaserAdvUnavailableReason = $"Creaser Adv could not initialize for this roof: {ex.Message}";
            }

            return result;
        }
    }
}
