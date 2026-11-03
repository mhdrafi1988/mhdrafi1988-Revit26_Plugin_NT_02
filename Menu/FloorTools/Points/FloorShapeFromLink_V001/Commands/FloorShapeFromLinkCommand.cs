using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorShapeFromLink.V001.Core.Models;
using Revit26_Plugin_Revised.FloorShapeFromLink.V001.Core.Services;
using Revit26_Plugin_Revised.FloorShapeFromLink.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.FloorShapeFromLink.V001.UI.ViewModels;
using Revit26_Plugin_Revised.FloorShapeFromLink.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorShapeFromLink.V001.Commands
{
    /// <summary>
    /// Ribbon entry point. Needs a plan view, one or more floors selected beforehand and at least
    /// one loaded Revit link; otherwise it warns and exits. The window then lets the user choose links,
    /// review the new elevations and apply them.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FloorShapeFromLinkCommand : IExternalCommand
    {
        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private static Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string title = ToolCatalog.FloorShapeFromLink.Name;
            if (FloorShapeFromLinkWindow.TryActivateOpenWindow())
                return Result.Succeeded;

            UIApplication app = commandData.Application;
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(title, "Open a project first.");
                return Result.Cancelled;
            }
            Document doc = uidoc.Document;

            if (!HostFloorService.IsPlanView(uidoc.ActiveView))
            {
                TaskDialog.Show(title, "This tool works in a plan view only. Open a floor, structural or ceiling plan and run it again.");
                return Result.Cancelled;
            }

            List<Floor> floors = HostFloorService.FilterFloors(doc, uidoc.Selection.GetElementIds(), out int ignored);
            if (floors.Count == 0)
            {
                TaskDialog.Show(title, "Select one or more floors first, then run the tool.\n\n" +
                    "Only floors are supported; extrusion floors and other elements are ignored.");
                return Result.Cancelled;
            }

            List<LinkItem> links = FloorShapeFromLinkHandler.ReadLinks(doc);
            if (links.Count == 0)
            {
                TaskDialog.Show(title, "No loaded Revit link was found in this project. Load a linked model and run the tool again.");
                return Result.Cancelled;
            }

            ForgeTypeId unit = doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId();
            var vm = new FloorShapeFromLinkViewModel(floors.Select(r => r.Id).ToList(), unit);
            if (links.Count == 1) links[0].IsSelected = true;
            vm.SetLinks(links);
            vm.FloorSummary = $"{floors.Count} floor(s) selected" + (ignored > 0 ? $" ({ignored} other element(s) ignored)" : "");
            if (ignored > 0)
                vm.Log(Shared.Models.LogLevel.Warning, $"{ignored} selected element(s) ignored: not floors.");

            var window = new FloorShapeFromLinkWindow(vm);
            new WindowInteropHelper(window).Owner = app.MainWindowHandle;
            window.Show();
            return Result.Succeeded;
        }
    }
}
