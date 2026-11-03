using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.SheetAutoRearrange.V027.UI.ViewModels;
using Revit26_Plugin_Revised.SheetAutoRearrange.V027.UI.Views;
using System;
using System.Windows.Interop;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V027.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetAutoRearrangeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiDoc = commandData.Application.ActiveUIDocument;

                var viewModel = new SheetAutoRearrangeViewModel(uiDoc);
                var window = new SheetAutoRearrangeWindow(viewModel);

                new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;

                window.Show(); // modeless — never ShowDialog()
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("SheetAutoRearrangeCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
