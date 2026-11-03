// =======================================================
// File: FloorSlopeByDrainCommand.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Commands
// Ribbon string: "Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Commands.FloorSlopeByDrainCommand"
// Floor port of Auto Slope By Drain (Multi-Roof) V013:
//   - Floors already selected (only floors) are used without asking;
//     otherwise the user picks host-model floors that are not in a group.
//   - Each floor is checked before the window opens (FloorShapeEditChecks):
//     shape-editing support, group, read-only, borrowed by another user.
//     Floors that fail are listed in the log and left out.
//   - A floor whose type has no Variable layer triggers a Continue / Skip
//     dialog (shape editing would tilt the whole slab).
//   - Picking floors does not change the model: shape editing is enabled and
//     reset at the start of each floor's run, inside its group.
//   - Body runs through ToolGuard.RunCommand; one window at a time.
// =======================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Services;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.UI.ViewModels;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FloorSlopeByDrainCommand : IExternalCommand
    {
        private const string Title = "Floor Slope By Drain";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private static Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (FloorSlopeByDrainWindow.TryActivateOpenWindow())
                return Result.Succeeded;

            UIApplication uiApp = commandData.Application;
            UIDocument uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(Title, "Open a project first.");
                return Result.Cancelled;
            }
            Document doc = uidoc.Document;

            // ── 1. Floors: use the selection when it is only Floors ──
            List<Floor> floors = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Floor>()
                .ToList();
            bool selectionIsFloorsOnly = floors.Count > 0 && floors.Count == uidoc.Selection.GetElementIds().Count;

            if (!selectionIsFloorsOnly)
            {
                // Esc throws OperationCanceledException; ToolGuard returns Cancelled.
                IList<Reference> floorRefs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new FloorFilter(),
                    "Select one or more floors to slope, then click Finish");

                floors = floorRefs
                    .Select(r => r.ElementId)
                    .Distinct()
                    .Select(id => doc.GetElement(id))
                    .OfType<Floor>()
                    .ToList();
            }

            if (floors.Count == 0)
                return Result.Cancelled;

            // ── 2. Check each floor before anything opens ─────────────────
            var diagnostics = new List<LogEntry>();
            var usable = new List<Floor>();
            foreach (Floor floor in floors)
            {
                string reason = FloorShapeEditChecks.GetBlockReason(floor);
                if (reason != null)
                {
                    diagnostics.Add(new LogEntry(LogLevel.Warning,
                        $"Skipped floor {floor.Name} (Id {floor.Id.Value}): {reason}."));
                    continue;
                }

                if (!FloorShapeEditChecks.HasVariableLayer(floor))
                {
                    if (FloorShapeEditChecks.AskNoVariableLayer(floor, Title) == NoVariableLayerChoice.Skip)
                    {
                        diagnostics.Add(new LogEntry(LogLevel.Info,
                            $"Skipped floor {floor.Name} (Id {floor.Id.Value}): its type has no Variable layer and you chose Skip."));
                        continue;
                    }
                    diagnostics.Add(new LogEntry(LogLevel.Warning,
                        $"Floor {floor.Name} (Id {floor.Id.Value}): its type has no Variable layer, so the whole slab will follow the slope."));
                }

                usable.Add(floor);
            }

            if (usable.Count == 0)
            {
                string why = diagnostics.Count > 0
                    ? string.Join("\n", diagnostics.Select(d => "• " + d.Message))
                    : "No floor could be used.";
                TaskDialog.Show(Title, "None of the selected floors can be sloped:\n\n" + why);
                return Result.Cancelled;
            }
            floors = usable;

            // ── 3. Read each floor (read-only: nothing is changed here) ─────
            var detectionService = new DrainDetectionService();
            var floorDataList = new List<FloorData>();

            foreach (Floor floor in floors)
            {
                var floorData = new FloorData { Floor = floor };
                floorData.TopFace = FloorGeometryService.GetTopFace(floor);
                if (floorData.TopFace == null)
                {
                    diagnostics.Add(new LogEntry(LogLevel.Warning,
                        $"Skipped floor {floor.Name} (Id {floor.Id.Value}): its top face could not be found."));
                    continue;
                }

                // Shape-edit points, when shape editing is already on. Used only
                // to weight the progress bar; the run reads them again fresh.
                SlabShapeEditor editor = floor.GetSlabShapeEditor();
                if (editor != null && editor.IsEnabled)
                    foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                        floorData.Vertices.Add(v);

                floorData.DetectedDrains = detectionService.DetectDrainsFromFloor(
                    floor, floorData.TopFace, null, entry => diagnostics.Add(entry));

                floorDataList.Add(floorData);
            }

            if (floorDataList.Count == 0)
            {
                TaskDialog.Show(Title, "None of the selected floors could be read (no top face found). Nothing to open.");
                return Result.Cancelled;
            }

            // ── 4. Window (modeless, owned by Revit) ───────────────────────
            var viewModel = new AutoSlopeDrainViewModel(uidoc, uiApp, floorDataList, diagnostics);
            var window = new FloorSlopeByDrainWindow(viewModel, uiApp);
            window.Show();
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Only host-model floors outside groups can be picked: shape points of a
    /// grouped floor cannot be edited on their own.
    /// </summary>
    public class FloorFilter : ISelectionFilter
    {
        /// <summary>True for a floor that is not in a group.</summary>
        public bool AllowElement(Element elem) =>
            elem is Floor && (elem.GroupId == null || elem.GroupId == ElementId.InvalidElementId);

        /// <summary>Sub-element references are not picked.</summary>
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
