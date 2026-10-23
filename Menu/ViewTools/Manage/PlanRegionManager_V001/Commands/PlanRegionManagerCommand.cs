using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.PlanRegionManager.V001.Core.Services;
using Revit26_Plugin_Revised.PlanRegionManager.V001.UI.ViewModels;
using Revit26_Plugin_Revised.PlanRegionManager.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.PlanRegionManager.V001.Commands
{
    /// <summary>
    /// Opens the Plan Region Manager for the active plan view: lists its visible and hidden plan regions
    /// and lets the user hide, unhide, select or delete them.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlanRegionManagerCommand : IExternalCommand
    {
        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
            {
                TaskDialog.Show(ToolCatalog.PlanRegionManager.Title, "Open a project first.");
                return Result.Cancelled;
            }

            var view = uidoc.ActiveView;
            if (!PlanRegionService.CanHoldPlanRegions(view))
            {
                TaskDialog.Show(ToolCatalog.PlanRegionManager.Title,
                    "Open a floor plan, ceiling plan or structural plan first.\n\n" +
                    "Plan regions can only be placed in those views (not in templates, sections, 3D views or schedules).");
                return Result.Cancelled;
            }

            var viewModel = new PlanRegionManagerViewModel(commandData, view);
            var window = new PlanRegionManagerWindow(viewModel);
            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.Show();
            return Result.Succeeded;
        }
    }
}
