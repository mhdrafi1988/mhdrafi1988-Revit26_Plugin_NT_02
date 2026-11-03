// =======================================================
// File: FloorPointsAndSlopeCommand.cs
// Location: Commands/
// Ribbon string: "Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Commands.FloorPointsAndSlopeCommand"
// Floor port of Roof Points & Slope V001.
// Uses the selected floors, or asks to pick one or more (host model, not in
// a group); checks each can be changed (FloorShapeEditChecks + checkout; a
// floor that can't is skipped with the reason), asks Continue / Skip for a
// floor whose type has no Variable layer, reads them (read-only — nothing
// changes until Run) and opens the window.
// Body runs through ToolGuard.RunCommand.
// =======================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Core.Engine;
using Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Core.Models;
using Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.FloorPointsAndSlope.V001.UI.ViewModels;
using Revit26_Plugin_Revised.FloorPointsAndSlope.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Commands
{
    /// <summary>Opens the Floor Points &amp; Slope window for one or more floors.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FloorPointsAndSlopeCommand : IExternalCommand
    {
        private const string Title = "Floor Points & Slope";

        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private static Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (FloorPointsAndSlopeWindow.TryActivateOpenWindow())
                return Result.Succeeded;

            UIApplication uiApp = commandData.Application;
            UIDocument uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(Title, "Open a project first.");
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show(Title, "This tool works in a project, not in the family editor.");
                return Result.Cancelled;
            }

            // ── Floors: the selected floors, or pick one or more ─────────────
            List<Floor> picked = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Floor>()
                .ToList();
            if (picked.Count == 0)
            {
                // Esc throws OperationCanceledException; ToolGuard returns Cancelled.
                IList<Reference> refs = uidoc.Selection.PickObjects(ObjectType.Element, new FloorFilter(),
                    "Select one or more floors (their sketch openings can be used as drains), then Finish");
                picked = refs.Select(r => doc.GetElement(r)).OfType<Floor>().ToList();
            }

            if (picked.Count == 0)
                return Result.Cancelled;

            // ── Check and read each floor (no model change) ───────────────────
            var diagnostics = new List<LogEntry>();
            var skipped = new List<string>();
            var floors = new List<FloorEntry>();
            foreach (Floor floor in picked.OrderBy(r => r.Name).ThenBy(r => r.Id.Value))
            {
                string label = $"{floor.Name}  ·  Id {floor.Id.Value}";
                if (FloorPointsAndSlopeEngine.ValidateFloor(uiApp, doc, floor.Id, out string error) == null)
                {
                    skipped.Add($"{label}: {error}");
                    continue;
                }

                // Without a Variable layer, shape editing tilts the whole slab.
                if (!FloorShapeEditChecks.HasVariableLayer(floor) &&
                    FloorShapeEditChecks.AskNoVariableLayer(floor, Title) == NoVariableLayerChoice.Skip)
                {
                    skipped.Add($"{label}: its type has no Variable layer (skipped at your choice).");
                    continue;
                }

                string prefix = picked.Count > 1 ? label + ": " : string.Empty;
                var floorLog = new List<LogEntry>();
                FloorOutline outline = FloorPointsAndSlopeHandler.ReadOutline(floor, floorLog.Add);
                if (outline.Boundaries.Count == 0)
                {
                    skipped.Add($"{label}: the sketch has no closed boundary loop.");
                    continue;
                }

                RidgeInput ridgeInput = FloorPointsAndSlopeHandler.ReadRidgeInput(floor, floorLog.Add);
                diagnostics.AddRange(floorLog.Select(e => new LogEntry(e.Level, prefix + e.Message)));
                floors.Add(new FloorEntry { Id = floor.Id, Label = label, Outline = outline, RidgeInput = ridgeInput });
            }

            if (floors.Count == 0)
            {
                TaskDialog.Show(Title, "No floor can be processed:\n\n" + string.Join("\n", skipped));
                return Result.Cancelled;
            }

            FloorPointsAndSlopeEventManager.Init();
            var vm = new FloorPointsAndSlopeViewModel(doc, floors);
            foreach (string s in skipped)
                vm.Log(LogLevel.Warning, "Skipped " + s);
            foreach (LogEntry entry in diagnostics)
                vm.Log(entry.Level, entry.Message);

            var window = new FloorPointsAndSlopeWindow(vm, uiApp.MainWindowHandle);
            window.Show();
            return Result.Succeeded;
        }

        /// <summary>Only host-model floors outside groups; openings are read from the floor's sketch.</summary>
        private sealed class FloorFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) =>
                elem is Floor && (elem.GroupId == null || elem.GroupId == ElementId.InvalidElementId);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
