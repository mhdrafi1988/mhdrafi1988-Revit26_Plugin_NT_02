using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AutomatedSectionPlacer.V003.ViewModels;
using Revit26_Plugin_Revised.AutomatedSectionPlacer.V003.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AutomatedSectionPlacer.V003
{
    /// <summary>
    /// Ribbon entry point for the Automated Section Placer tool. Opens the
    /// single accordion window (5 collapsible stages sharing one ViewModel);
    /// the window itself stays open after operations complete, per our
    /// window-lifecycle convention — the user closes it manually via the
    /// Close / Cancel / Open Selected & Close buttons, or Esc (V213).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutomatedSectionPlacerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiDoc = commandData.Application.ActiveUIDocument;
                if (uiDoc == null)
                {
                    message = "No active document.";
                    return Result.Failed;
                }

                if (!(uiDoc.ActiveView is ViewPlan planView) || planView.ViewType != ViewType.FloorPlan && planView.ViewType != ViewType.CeilingPlan && planView.ViewType != ViewType.AreaPlan && planView.ViewType != ViewType.EngineeringPlan)
                {
                    TaskDialog.Show("Automated Section Placer",
                        "Please activate a Plan View (Floor Plan, Ceiling Plan, Area Plan, or Engineering Plan) before running this tool, then try again.");
                    return Result.Cancelled;
                }

                var viewModel = new AutomatedSectionPlacerViewModel(uiDoc, planView.Id);
                var window = new AutomatedSectionPlacerWindow
                {
                    DataContext = viewModel
                };

                viewModel.CloseRequested += window.Close;

                window.Show(); // modeless — matches our Close-only modeless dialog convention

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                Logger.Error("AutomatedSectionPlacerCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
