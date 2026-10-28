using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Services;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Infrastructure;
using Revit26_Plugin_Revised.SectionsToSheet.V001.UI.ViewModels;
using Revit26_Plugin_Revised.SectionsToSheet.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Commands
{
    /// <summary>
    /// Opens Sections To Sheet for the active view: lists the section markers shown in it (or the
    /// selected ones), takes picked sections off the sheet they are on, and lays them out on a new
    /// sheet in a grid or in rows.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SectionsToSheetCommand : IExternalCommand
    {
        /// <summary>Folder under %AppData%\Revit26_Plugin_Revised holding this tool's settings.</summary>
        public const string SettingsFolder = "SectionsToSheet";

        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null)
            {
                TaskDialog.Show(ToolCatalog.SectionsToSheet.Title, "Open a project first.");
                return Result.Cancelled;
            }

            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show(ToolCatalog.SectionsToSheet.Title, "This tool works in a project, not in the Family Editor.");
                return Result.Cancelled;
            }

            if (doc.IsReadOnly)
            {
                TaskDialog.Show(ToolCatalog.SectionsToSheet.Title, "The active document is read-only.");
                return Result.Cancelled;
            }

            View source = uidoc.ActiveView;
            string viewProblem = SectionCollector.CheckView(source);
            if (viewProblem != null)
            {
                TaskDialog.Show(ToolCatalog.SectionsToSheet.Title, viewProblem);
                return Result.Cancelled;
            }

            var settings = SettingsService<SectionsToSheetSettings>.Load(SettingsFolder).Normalize();
            var session = new RevitSectionsSession(uidoc, source, settings);
            SectionsToSheetWindow window;
            try
            {
                window = new SectionsToSheetWindow(new SectionsToSheetViewModel(session, settings));
            }
            catch
            {
                session.Dispose();
                throw;
            }

            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.Show(); // modeless — Refresh / Pick / Create go through an ExternalEvent
            return Result.Succeeded;
        }
    }
}
