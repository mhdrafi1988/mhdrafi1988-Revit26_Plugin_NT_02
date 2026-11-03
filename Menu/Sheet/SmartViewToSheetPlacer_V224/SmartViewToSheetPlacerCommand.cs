using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.SmartViewToSheetPlacer.V224.ViewModels;
using Revit26_Plugin_Revised.SmartViewToSheetPlacer.V224.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.SmartViewToSheetPlacer.V224
{
    /// <summary>
    /// Ribbon entry point for the Smart View To Sheet Placer tool. Opens the
    /// single accordion window (5 collapsible stages sharing one ViewModel);
    /// the window itself stays open after operations complete, per our
    /// window-lifecycle convention — the user closes it manually via the
    /// Close / Cancel / Open Selected & Close buttons, or Esc (V213).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SmartViewToSheetPlacerCommand : IExternalCommand
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

                var viewModel = new SmartViewToSheetPlacerViewModel(uiDoc);
                var window = new SmartViewToSheetPlacerWindow
                {
                    DataContext = viewModel
                };

                viewModel.CloseRequested += window.Close;

                window.Show(); // modeless — matches our Close-only modeless dialog convention

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                Logger.Error("SmartViewToSheetPlacerCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
