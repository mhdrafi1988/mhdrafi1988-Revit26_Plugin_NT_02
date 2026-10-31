using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.SheetTextReplace.V001.Infrastructure;
using Revit26_Plugin_Revised.SheetTextReplace.V001.UI.ViewModels;
using Revit26_Plugin_Revised.SheetTextReplace.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.Commands
{
    /// <summary>
    /// Opens Sheet Text Replace: find and replace text in the text notes on a sheet, in the views the
    /// user ticks. Preselects the active sheet when there is one.
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

            var activeSheet = uiDoc.ActiveView is ViewSheet sheet && !sheet.IsPlaceholder ? sheet : null;
            var session = new RevitSheetTextSession(doc, activeSheet);
            if (session.Sheets.Count == 0)
            {
                session.Dispose();
                TaskDialog.Show(ToolCatalog.SheetTextReplace.Title, "This model has no sheets.");
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
    }
}
