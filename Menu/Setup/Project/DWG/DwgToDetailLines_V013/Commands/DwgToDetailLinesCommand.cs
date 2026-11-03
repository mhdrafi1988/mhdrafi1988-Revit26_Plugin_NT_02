// ==============================================
// File: DwgToDetailLinesCommand.cs
// Layer: Commands
// Namespace: Revit26_Plugin_Revised.DwgToDetailLines.V013.Commands
// ==============================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.DwgToDetailLines.V013.Infrastructure.Helpers;
using Revit26_Plugin_Revised.DwgToDetailLines.V013.UI.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.DwgToDetailLines.V013.Commands
{
    /// <summary>
    /// Entry point for the DWG to Detail Lines tool.
    /// Enforces Project + Drafting View context and launches the UI.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DwgToDetailLinesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return ExecuteInternal(commandData, ref message, elements);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (System.Exception ex)
            {
                Logger.Error("DwgToDetailLinesCommand", ex);
                message = ex.Message;
                TaskDialog.Show("DWG to Detail Lines", $"An unexpected error occurred: {ex.Message}");
                return Result.Failed;
            }
        }

        private Result ExecuteInternal(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;

            if (!RevitContextValidator.IsDraftingViewInProject(uiApp, out message))
            {
                TaskDialog.Show("DWG to Detail Lines", message);
                return Result.Cancelled;
            }

            var view = new DwgToDetailLinesWindow(uiApp);
            view.Show();

            return Result.Succeeded;
        }
    }
}


