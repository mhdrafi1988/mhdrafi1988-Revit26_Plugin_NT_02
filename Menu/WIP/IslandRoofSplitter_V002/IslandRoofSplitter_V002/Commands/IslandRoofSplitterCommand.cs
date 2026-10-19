// IslandRoofSplitter V002 — entry point. Picks one sloped footprint roof, then the detail lines drawn over it in
// the active view, snapshots everything Revit-side into a SplitterContext, and opens the modeless window.
//
// Decisions (per Anas's confirmed decisions, 2026-10-14):
//  * New tool family (not a RoofFromDetailLines version): face projection, nesting and vertex-Z push are new.
//  * Nested regions: Ring / Full — user choice in the UI.     * Crossings: Split / Endpoint chains — user choice.
//  * Roof type / level / base offset: pickers, defaulting to the source roof.
//  * Chord tolerance: 5 mm default, 1–50 mm.                  * Sloped interior: boundary only / ridge & valley points.
//  * Mark: "<prefix>Island_NN", prefix defaults to "R<sourceRoofId>_" (clear it for plain Island_NN).
//  * Flat mode arcs: keep true arcs / tessellate — user choice.
//  * Preview (Run) is pure Core and auto-runs on open; nothing is created until Commit.
//
// V002 (per Anas, 2026-10-15): the window also offers the output element — Roof only / Roof with a floor
// fallback / Floor only — so a document with no roof family but a floor family still opens.
//
// Update (2026-10-15, per Anas): level and roof type are confirmed before the window opens.
//  * No levels or no roof types in the document → TaskDialog + Result.Cancelled. The tool never creates a level.
//  * Level or type not detected on the source roof → modal LevelTypePromptWindow; Cancel aborts the command,
//    Continue seeds the window's pickers. The window also keeps its own banner + Commit guard (belt and braces).
//
// Error handling goes through Shared ToolGuard (cancel → Cancelled, anything else → logged + dialog + Failed),
// so this file holds no try/catch of its own (refactor, 2026-10-14).
#nullable enable
using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin.IslandRoofSplitter.V002.Infrastructure.Helpers;
using Revit26_Plugin.IslandRoofSplitter.V002.UI.ViewModels;
using Revit26_Plugin.IslandRoofSplitter.V002.UI.Views;
using Revit26_Plugin.Shared.Services;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class IslandRoofSplitterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, Run);

        private static Result Run(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument
                               ?? throw new InvalidOperationException("Open a project view first.");

            var roof = SelectionHelper.PickRoof(uidoc);
            var lines = SelectionHelper.PickDetailCurves(uidoc);
            var context = ContextFactory.Build(uidoc, roof, lines);

            // Nothing to pick from → stop with a clear message rather than opening a window that cannot commit.
            // V002: roof types alone are no longer required — a document with only floor types can still run
            // the Floor output, so the window opens and its banner says to switch the output.
            if (!context.HasLevels || (!context.HasRoofTypes && !context.HasFloorTypes))
            {
                TaskDialog.Show("Island Roof Splitter",
                    !context.HasLevels
                        ? "No levels in this document — create a level in Revit, then run the tool again."
                        : "No roof types and no floor types are loaded — load a roof or floor family, then run the tool again.");
                return Result.Cancelled;
            }

            long? presetLevelId = null;
            long? presetRoofTypeId = null;

            // Level or type not detected on the source roof → ask before the window opens.
            if (context.HasRoofTypes && (!context.LevelDetected || !context.RoofTypeDetected))
            {
                var prompt = new LevelTypePromptWindow(context);
                new WindowInteropHelper(prompt) { Owner = uiapp.MainWindowHandle };
                if (prompt.ShowDialog() != true) return Result.Cancelled;
                presetLevelId = prompt.ChosenLevelId;
                presetRoofTypeId = prompt.ChosenRoofTypeId;
            }

            var window = new IslandRoofSplitterWindow(
                new IslandRoofSplitterViewModel(context, presetLevelId, presetRoofTypeId));
            new WindowInteropHelper(window) { Owner = uiapp.MainWindowHandle };
            window.Show();
            return Result.Succeeded;
        }
    }
}
