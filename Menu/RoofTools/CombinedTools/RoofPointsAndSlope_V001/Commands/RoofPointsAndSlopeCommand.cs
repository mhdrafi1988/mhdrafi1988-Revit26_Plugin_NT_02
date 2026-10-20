// =======================================================
// File: RoofPointsAndSlopeCommand.cs
// Location: Commands/
// Ribbon string: "Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Commands.RoofPointsAndSlopeCommand"
// Uses the selected footprint roofs, or asks to pick one or more; checks
// each can be changed (a roof that can't is skipped with the reason),
// reads them (read-only — nothing changes until Run) and opens the window.
// Body runs through ToolGuard.RunCommand.
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
    /// <summary>Opens the Roof Points &amp; Slope window for one or more footprint roofs.</summary>
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

            // ── Roofs: the selected footprint roofs, or pick one or more ─────
            List<FootPrintRoof> picked = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<FootPrintRoof>()
                .ToList();
            if (picked.Count == 0)
            {
                // Esc throws OperationCanceledException; ToolGuard returns Cancelled.
                IList<Reference> refs = uidoc.Selection.PickObjects(ObjectType.Element, new FootPrintRoofFilter(),
                    "Select one or more footprint roofs (sketch-based, so their openings can be read), then Finish");
                picked = refs.Select(r => doc.GetElement(r)).OfType<FootPrintRoof>().ToList();
            }

            if (picked.Count == 0)
                return Result.Cancelled;

            // ── Check and read each roof (no model change) ───────────────────
            var diagnostics = new List<LogEntry>();
            var skipped = new List<string>();
            var roofs = new List<RoofEntry>();
            foreach (FootPrintRoof roof in picked.OrderBy(r => r.Name).ThenBy(r => r.Id.Value))
            {
                string label = $"{roof.Name}  ·  Id {roof.Id.Value}";
                if (RoofPointsAndSlopeEngine.ValidateRoof(uiApp, doc, roof.Id, out string error) == null)
                {
                    skipped.Add($"{label}: {error}");
                    continue;
                }

                string prefix = picked.Count > 1 ? label + ": " : string.Empty;
                var roofLog = new List<LogEntry>();
                RoofOutline outline = RoofPointsAndSlopeHandler.ReadOutline(roof, roofLog.Add);
                if (outline.Boundaries.Count == 0)
                {
                    skipped.Add($"{label}: the sketch has no closed boundary loop.");
                    continue;
                }

                RidgeInput ridgeInput = RoofPointsAndSlopeHandler.ReadRidgeInput(roof, roofLog.Add);
                diagnostics.AddRange(roofLog.Select(e => new LogEntry(e.Level, prefix + e.Message)));
                roofs.Add(new RoofEntry { Id = roof.Id, Label = label, Outline = outline, RidgeInput = ridgeInput });
            }

            if (roofs.Count == 0)
            {
                TaskDialog.Show(Title, "No roof can be processed:\n\n" + string.Join("\n", skipped));
                return Result.Cancelled;
            }

            RoofPointsAndSlopeEventManager.Init();
            var vm = new RoofPointsAndSlopeViewModel(doc, roofs);
            foreach (string s in skipped)
                vm.Log(LogLevel.Warning, "Skipped " + s);
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
