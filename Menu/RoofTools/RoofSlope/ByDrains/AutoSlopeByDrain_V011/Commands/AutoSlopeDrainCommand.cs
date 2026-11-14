// File: AutoSlopeDrainCommand.cs
// Location: Commands/
// Base: ported from AutoSlopeByDrain V004 — roof pick, enable shape editing,
// reset vertices to zero, initial drain detection all happen here
// synchronously BEFORE the window is shown (safe since the window hasn't
// opened yet). Window shown modeless via window.Show().
//
// FIX (V005): neither AutoSlopeByPoint V018 nor AutoSlopeByDrain V004
// actually set the window's Owner via WindowInteropHelper, despite that
// being the project's stated standing convention — confirmed as a real gap
// during the merge audit. Fixed here per Rafi's confirmed decision
// (2026-07-21), even though neither prior tool does this.
//
// NEW (V008), per Rafi's confirmed multi-roof decision (2026-09-08):
//   PickObject -> PickObjects. The tool now accepts one or more roofs in a
//   single pick session. Each picked roof gets its own RoofData (geometry
//   reset to zero, top face, shape vertices, drain detection) exactly as
//   before, just looped. The ViewModel now takes the full List<RoofData>
//   and builds one tab per roof — see AutoSlopeDrainViewModel /
//   RoofTabViewModel.
//   A roof that fails geometry analysis (no top face) is skipped with a
//   TaskDialog-free log entry rather than aborting the whole multi-roof
//   pick — the remaining roofs still open in the window.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V011.Core.Models;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V011.Core.Services;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V011.UI.ViewModels;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V011.UI.Views;
using Revit26_Plugin_Revised.Shared.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V011.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoSlopeByDrain : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument uidoc = uiApp.ActiveUIDocument;
                Document doc = uidoc.Document;

                IList<Reference> roofRefs;
                try
                {
                    roofRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new RoofFilter(),
                        "Select one or more roofs (FootPrintRoof only — required for sketch-based opening detection). Click Finish when done.");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (roofRefs == null || roofRefs.Count == 0)
                    return Result.Cancelled;

                // Buffer diagnostic (DRAIN-DEBUG) entries here — the log panel doesn't
                // exist yet at this point, so we collect them and flush into the
                // ViewModel's log once it's constructed below. Detection results
                // (detectedDrains) are completely unaffected by this.
                var diagnosticLog = new List<LogEntry>();
                var detectionService = new DrainDetectionService();

                var roofDataList = new List<RoofData>();
                var skippedRoofNames = new List<string>();

                foreach (var roofId in roofRefs.Select(r => r.ElementId).Distinct())
                {
                    var roof = doc.GetElement(roofId) as FootPrintRoof;
                    if (roof == null)
                    {
                        // Defensive fallback only — RoofFilter already restricts PickObjects
                        // to FootPrintRoof, so this should not be reachable in normal use.
                        continue;
                    }

                    var roofData = new RoofData { Roof = roof };

                    RoofGeometryService.InitializeRoofGeometry(roof, doc);

                    try
                    {
                        AnalyzeRoofGeometry(roofData);
                    }
                    catch (System.Exception)
                    {
                        skippedRoofNames.Add($"{roof.Name} (Id {roof.Id.Value})");
                        continue;
                    }

                    var detectedDrains = detectionService.DetectDrainsFromRoof(
                        roof, roofData.TopFace, roofData.Vertices, entry => diagnosticLog.Add(entry));
                    roofData.DetectedDrains = detectedDrains;

                    roofDataList.Add(roofData);
                }

                if (roofDataList.Count == 0)
                {
                    TaskDialog.Show("AutoSlope By Drain",
                        "None of the selected roofs could be analyzed (no top face found). Nothing to open.");
                    return Result.Cancelled;
                }

                var viewModel = new AutoSlopeDrainViewModel(uidoc, uiApp, roofDataList);
                foreach (var entry in diagnosticLog)
                    viewModel.LogEntries.Add(entry);

                foreach (var skippedName in skippedRoofNames)
                    viewModel.LogEntries.Add(new LogEntry(LogLevel.Warning,
                        $"Skipped roof {skippedName} — could not find its top face."));

                var window = new AutoSlopeByDrainWindow(viewModel);

                // FIX (V005): parent to Revit's main window per standing convention —
                // never System.Windows.Application.Current.MainWindow, which is null/unreliable in
                // an add-in context.
                new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;

                window.Show(); // modeless

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                Logger.Error("AutoSlopeByDrain", ex);
                message = $"Failed to initialize plugin: {ex.Message}";
                return Result.Failed;
            }
        }

        private void AnalyzeRoofGeometry(RoofData roofData)
        {
            var roof = roofData.Roof;

            roofData.TopFace = RoofGeometryService.GetTopFace(roof);
            if (roofData.TopFace == null)
                throw new System.Exception("Could not find top face of the roof.");

            roofData.Vertices.Clear();
            var slabShapeEditor = roof.GetSlabShapeEditor();
            foreach (SlabShapeVertex vertex in slabShapeEditor.SlabShapeVertices)
            {
                roofData.Vertices.Add(vertex);
            }
        }
    }

    /// <summary>
    /// Restricts roof selection to FootPrintRoof only — confirmed by Rafi
    /// (2026-07-22) since opening detection is now Sketch-based, and only
    /// FootPrintRoof carries a dependent Sketch element. ExtrusionRoof and any
    /// other RoofBase subtype are not selectable with this tool.
    /// </summary>
    public class RoofFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is FootPrintRoof;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}

