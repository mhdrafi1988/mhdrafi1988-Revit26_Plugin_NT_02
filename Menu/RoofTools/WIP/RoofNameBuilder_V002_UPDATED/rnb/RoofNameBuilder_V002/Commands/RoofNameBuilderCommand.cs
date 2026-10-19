using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.RoofNameBuilder.V002.UI.ViewModels;
using Revit26_Plugin_Revised.RoofNameBuilder.V002.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofNameBuilder.V002.Commands
{
    /// <summary>
    /// Roof Name Builder V002 — thin entry point. The body runs through Shared ToolGuard.RunCommand
    /// (cancellation → Cancelled; anything else → error log + TaskDialog + Failed), replacing V001's
    /// hand-written try/catch.
    ///
    /// V002 (2026-10-14) — new version folder, not an in-place fix, because names that already carry
    /// codes are now parsed back into base + ticked codes on load (a behaviour change worth being able
    /// to fall back from, per Claude/TOOL_VERSIONING.md). Also moves the tool onto the real Shared/
    /// API (ToolViewModelBase, SettingsService&lt;T&gt;, LogEntry ctor, ToolGuard) — V001 was written
    /// against guessed names and does not compile against the repo.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class RoofNameBuilderCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(typeof(RoofNameBuilderCommand), commandData, ref message, elements, ExecuteCore);

        private static Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument
                ?? throw new InvalidOperationException("Open a project before running Roof Name Builder.");
            var doc = uiDoc.Document;

            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show("Roof Name Builder", "Roof Name Builder works in project files, not families.");
                return Result.Cancelled;
            }

            var currentSelection = uiDoc.Selection.GetElementIds();

            var viewModel = new RoofNameBuilderViewModel(doc, currentSelection);
            var window = new RoofNameBuilderWindow(viewModel);

            new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };

            window.Show(); // modeless, per CLAUDE.md convention

            return Result.Succeeded;
        }
    }
}
