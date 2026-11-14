using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ParaManager.V005.Services;
using Revit26_Plugin_Revised.ParaManager.V005.ViewModels;
using Revit26_Plugin_Revised.ParaManager.V005.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ParaManager.V005
{
    /// <summary>
    /// Ribbon entry point for ParaManager — registered under the Manage panel
    /// in <see cref="Revit26_Plugin_Revised.Menu.Ribbon.ManageRibbon"/> (not duplicated
    /// here; this class only launches the modeless tool window).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ParaManagerCommand : IExternalCommand
    {
        // Static so the window survives command re-invocation and doesn't get GC'd
        // while modeless — standard pattern already used by this suite's other tools.
        private static ParaManagerWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (_window != null && _window.IsVisible)
                {
                    _window.Activate();
                    return Result.Succeeded;
                }

                var handler = new ParaManagerExternalEventHandler();
                var externalEvent = ExternalEvent.Create(handler);

                var viewModel = new ParaManagerViewModel(handler, externalEvent);
                _window = new ParaManagerWindow(viewModel)
                {
                    Owner = null // modeless — does not block Revit's main window
                };
                _window.Closed += (_, _) => _window = null;
                _window.Show();

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                Logger.Error("ParaManagerCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
