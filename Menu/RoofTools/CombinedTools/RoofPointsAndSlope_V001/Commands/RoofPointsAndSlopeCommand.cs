// =======================================================
// File: RoofPointsAndSlopeCommand.cs
// Location: Commands/
// Ribbon string: "Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Commands.RoofPointsAndSlopeCommand"
// Picks one footprint roof (a selected one is used without asking),
// checks it can be changed, reads it (read-only — nothing changes until
// Run) and opens the window. Body runs through ToolGuard.RunCommand.
// =======================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Engine;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.UI.ViewModels;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Commands
{
    /// <summary>Opens the Roof Points &amp; Slope window for one footprint roof.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoofPointsAndSlopeCommand : IExternalCommand
    {
        private const string Title = "Roof Points & Slope";

        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private static Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (RoofPointsAndSlopeWindow.TryActivateOpenWindow())
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

            // ── Roof: the selected footprint roof, or pick one ───────────────
            FootPrintRoof roof = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<FootPrintRoof>()
                .FirstOrDefault();
            if (roof == null)
            {
                // Esc throws OperationCanceledException; ToolGuard returns Cancelled.
                Reference picked = uidoc.Selection.PickObject(ObjectType.Element, new FootPrintRoofFilter(),
                    "Select a footprint roof (sketch-based, so its openings can be read)");
                roof = doc.GetElement(picked) as FootPrintRoof;
            }

            if (roof == null)
                return Result.Cancelled;

            // ── Checks: group, worksharing ───────────────────────────────────
            if (RoofPointsAndSlopeEngine.ValidateRoof(uiApp, doc, roof.Id, out string error) == null)
            {
                TaskDialog.Show(Title, error);
                return Result.Cancelled;
            }

            // ── Read (no model change) and open ──────────────────────────────
            var diagnostics = new List<LogEntry>();
            RoofOutline outline = RoofPointsAndSlopeHandler.ReadOutline(roof, diagnostics.Add);
            if (outline.Boundaries.Count == 0)
            {
                TaskDialog.Show(Title, "The roof's sketch has no closed boundary loop, so it can't be processed.");
                return Result.Cancelled;
            }

            RidgeInput ridgeInput = RoofPointsAndSlopeHandler.ReadRidgeInput(roof, diagnostics.Add);

            RoofPointsAndSlopeEventManager.Init();
            var vm = new RoofPointsAndSlopeViewModel(doc, roof.Id, $"{roof.Name}  ·  Id {roof.Id.Value}", outline, ridgeInput);
            foreach (LogEntry entry in diagnostics)
                vm.Log(entry.Level, entry.Message);

            var window = new RoofPointsAndSlopeWindow(vm, uiApp.MainWindowHandle);
            window.Show();
            return Result.Succeeded;
        }

        /// <summary>Only footprint roofs: openings are read from the roof's sketch.</summary>
        private sealed class FootPrintRoofFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is FootPrintRoof;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
