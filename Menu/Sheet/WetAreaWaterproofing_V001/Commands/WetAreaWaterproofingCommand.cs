using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Infrastructure;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.UI.ViewModels;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.UI.Views;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Commands
{
    /// <summary>
    /// Opens Wet Area Waterproofing: lists the rooms of the host model and its links, marks the wet
    /// ones, and creates a waterproofing roof on top of the slab under each ticked room.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WetAreaWaterproofingCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            string title = ToolCatalog.WetAreaWaterproofing.Title;
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

            var settings = SettingsService<WetAreaSettings>.Load(ViewModel_WetAreaWaterproofing.SettingsFolder).Normalize();
            RevitWetAreaSession session;
            using (ToolCancellation.BeginFor(ToolCatalog.WetAreaWaterproofing, commandData.Application, "Reading rooms…"))
                session = new RevitWetAreaSession(uidoc, settings);

            WetAreaWaterproofingWindow window;
            try
            {
                window = new WetAreaWaterproofingWindow(new ViewModel_WetAreaWaterproofing(session, settings));
            }
            catch
            {
                session.Dispose();
                throw;
            }

            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.Show(); // modeless — Refresh / Run / Remove go through an ExternalEvent
            return Result.Succeeded;
        }
    }
}
