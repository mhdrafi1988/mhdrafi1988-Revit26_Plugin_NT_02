using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models;
using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Services;
using Revit26_Plugin_Revised.SheetTextReplace.V001.Infrastructure;
using Revit26_Plugin_Revised.SheetTextReplace.V001.UI.ViewModels;
using Revit26_Plugin_Revised.SheetTextReplace.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.Commands
{
    /// <summary>
    /// Opens Sheet Text Replace: find and replace text in the text notes of a sheet, in the views the
    /// user ticks, or of a single view. Preselects the active sheet when the active view is one; when
    /// the active view is a plain view that can hold text notes it asks whether to work on that view
    /// alone, and otherwise opens with nothing picked.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetTextReplaceCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            var doc = uiDoc?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show(ToolCatalog.SheetTextReplace.Title, "Open a project first.");
                return Result.Cancelled;
            }

            if (doc.IsReadOnly)
            {
                TaskDialog.Show(ToolCatalog.SheetTextReplace.Title,
                    "This model is read-only, so its text notes cannot be changed.");
                return Result.Cancelled;
            }

            var initialScope = PickInitialScope(uiDoc.ActiveView, out bool cancelled);
            if (cancelled) return Result.Cancelled;

            var session = new RevitSheetTextSession(doc, initialScope);
            if (session.Scopes.Count == 0)
            {
                session.Dispose();
                TaskDialog.Show(ToolCatalog.SheetTextReplace.Title,
                    "This model has no sheets and no views that can hold text notes.");
                return Result.Cancelled;
            }

            SheetTextReplaceWindow window;
            try
            {
                window = new SheetTextReplaceWindow(new SheetTextReplaceViewModel(session));
            }
            catch
            {
                session.Dispose();
                throw;
            }

            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.Show(); // modeless — Load / Apply / Show go through an ExternalEvent
            return Result.Succeeded;
        }

        /// <summary>
        /// What the tool opens on. The active sheet is used as it is. For a plain view that can hold
        /// text notes the user is asked whether to work on that view alone (Yes) or to pick a sheet or
        /// view in the window instead (No); Cancel closes the tool without opening it. Any other
        /// active view (a schedule, a 3D view…) opens the window with nothing picked.
        /// </summary>
        private static ScopeItem PickInitialScope(View activeView, out bool cancelled)
        {
            cancelled = false;

            if (activeView is ViewSheet sheet && !sheet.IsPlaceholder)
                return ScopeItem.ForSheet(sheet.Id.Value, sheet.SheetNumber ?? string.Empty, sheet.Name ?? string.Empty);

            if (!SheetTextReader.CanHoldTextNotes(activeView))
                return null;

            var scope = SheetTextReader.ScopeOf(activeView);
            var dialog = new TaskDialog(ToolCatalog.SheetTextReplace.Title)
            {
                MainInstruction = "Work on the active view alone?",
                MainContent =
                    $"The active view is \"{scope.Name}\" ({scope.TypeLabel}), not a sheet.\n\n" +
                    "Yes — find and replace in this view only, whether or not it is on a sheet.\n" +
                    "No — open the tool and pick a sheet (or another view) yourself.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No | TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Yes
            };

            switch (dialog.Show())
            {
                case TaskDialogResult.Yes:
                    return scope;
                case TaskDialogResult.No:
                    return null;
                default:
                    cancelled = true; // Cancel, or the dialog was closed
                    return null;
            }
        }
    }
}
