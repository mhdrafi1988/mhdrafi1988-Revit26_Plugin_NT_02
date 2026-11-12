using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Models;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Infrastructure;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.UI.ViewModels;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.Commands
{
    /// <summary>
    /// Opens Sheet Views Combine: lists the views on the active sheet (Sheet 1) and a second
    /// sheet in one grid, and moves the ticked ones onto one target sheet with the packing layout.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetViewsCombineCommand : IExternalCommand
    {
        /// <summary>Folder under %AppData%\Revit26_Plugin_Revised holding this tool's settings.</summary>
        public const string SettingsFolder = "SheetViewsCombine";

        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            string title = ToolCatalog.SheetViewsCombine.Title;
            if (doc == null)
            {
                TaskDialog.Show(title, "Open a project first.");
                return Result.Cancelled;
            }
            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show(title, "This tool works in a project, not in the Family Editor.");
                return Result.Cancelled;
            }
            if (doc.IsReadOnly)
            {
                TaskDialog.Show(title, "The active document is read-only.");
                return Result.Cancelled;
            }

            var settings = SettingsService<SheetViewsCombineSettings>.Load(SettingsFolder).Normalize();
            var session = new RevitCombineSession(uidoc);
            if (session.InitialCatalog.Sheets.Count < 2)
            {
                session.Dispose();
                TaskDialog.Show(title, "The project needs at least two sheets to combine.");
                return Result.Cancelled;
            }

            SheetViewsCombineWindow window;
            try
            {
                window = new SheetViewsCombineWindow(new SheetViewsCombineViewModel(session, settings));
            }
            catch
            {
                session.Dispose();
                throw;
            }

            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.Show(); // modeless — every Revit request goes through an ExternalEvent
            return Result.Succeeded;
        }
    }
}
