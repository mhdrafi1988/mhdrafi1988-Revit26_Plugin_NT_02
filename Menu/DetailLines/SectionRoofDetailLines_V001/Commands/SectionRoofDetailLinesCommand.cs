using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Models;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Services;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.UI.ViewModels;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Commands
{
    /// <summary>
    /// Opens Section Roof Detail Lines for the active sheet: lists the sections on it and
    /// everything they show, from the host model and from every loaded link, then draws the cut
    /// profile of the ticked roofs and a line on the walls beside them, in the sections
    /// themselves.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SectionRoofDetailLinesCommand : IExternalCommand
    {
        /// <summary>Folder under %AppData%\Revit26_Plugin_Revised holding this tool's settings.</summary>
        public const string SettingsFolder = "SectionRoofDetailLines";

        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc?.Document;
            string title = ToolCatalog.SectionRoofDetailLines.Title;

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
                TaskDialog.Show(title, "The active project is read-only, so no detail line can be created in it.");
                return Result.Cancelled;
            }

            View activeView = uiDoc.ActiveView;
            string viewProblem = SheetSectionCollector.CheckView(activeView);
            if (viewProblem != null)
            {
                TaskDialog.Show(title, viewProblem);
                return Result.Cancelled;
            }

            // Read the model here, while the command still has Revit's API context, so the window
            // opens with the sheet's sections and their contents already in it.
            ModelSnapshot snapshot = new ModelReader().Read(doc, activeView);
            if (snapshot.Sections.Count == 0)
            {
                TaskDialog.Show(title,
                    "No section view was found to draw in.\n\n"
                    + "Put the sections on this sheet first, or open one of the section views.");
                return Result.Cancelled;
            }

            var settings = SettingsService<SectionRoofLinesSettings>.Load(SettingsFolder).Normalize();
            var window = new SectionRoofDetailLinesWindow(
                new ViewModel_SectionRoofDetailLines(uiApp, snapshot, settings));

            new WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;
            window.Show(); // modeless — Refresh and Create go through an ExternalEvent
            return Result.Succeeded;
        }
    }
}
