// ==============================================
// File: DwgToLinesCommand.cs
// Layer: Commands
// Namespace: Revit26_Plugin_Revised.DwgToLines.V007.Commands
// ==============================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.DwgToLines.V007.Infrastructure.Helpers;
using Revit26_Plugin_Revised.DwgToLines.V007.UI.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.DwgToLines.V007.Commands
{
    /// <summary>
    /// Entry point for the DWG to Lines tool.
    /// Enforces Family Editor context and launches the UI.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DwgToLinesCommand : IExternalCommand
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
                Logger.Error("DwgToLinesCommand", ex);
                message = ex.Message;
                TaskDialog.Show("DWG to Lines", $"An unexpected error occurred: {ex.Message}");
                return Result.Failed;
            }
        }

        private Result ExecuteInternal(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;

            if (!RevitContextValidator.IsFamilyEditor(uiApp, out message))
            {
                TaskDialog.Show("DWG to Lines", message);
                return Result.Cancelled;
            }

            var view = new DwgToLinesWindow(uiApp);
            view.Show();

            return Result.Succeeded;
        }
    }
}
