using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.UI.ViewModels;
using Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.UI.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FloorEdgeVertexReducerCommand : IExternalCommand
    {
        private static MainWindow _window;

        /// <summary>Revit entry point; delegates to ToolGuard.RunCommand.</summary>

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
                Logger.Error("FloorEdgeVertexReducerCommand", ex);
                message = ex.Message;
                TaskDialog.Show("Floor Edge Vertex Reducer", $"An unexpected error occurred: {ex.Message}");
                return Result.Failed;
            }
        }

        private Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (_window != null && _window.IsLoaded)
            {
                _window.Activate();
                return Result.Succeeded;
            }

            var viewModel = new MainViewModel();
            var handler = new FloorEdgeVertexReducerEventHandler(viewModel);
            var externalEvent = ExternalEvent.Create(handler);
            viewModel.Initialize(handler, externalEvent);

            _window = new MainWindow(viewModel);
            _window.Closed += (s, e) => _window = null;

            // Owner must be set before Show() — this is what keeps the tool window
            // above Revit's main window after PickObject() returns focus to Revit.
            new WindowInteropHelper(_window).Owner = commandData.Application.MainWindowHandle;

            _window.Show();

            return Result.Succeeded;
        }
    }
}
