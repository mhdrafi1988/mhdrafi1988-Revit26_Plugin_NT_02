using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using System.Windows.Interop;
using Revit26_Plugin_Revised.DtlLineDim.V010.UI.ViewModels;
using Revit26_Plugin_Revised.DtlLineDim.V010.UI.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.DtlLineDim.V010.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DtlLineDimCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                var doc = uiApp.ActiveUIDocument?.Document;

                if (doc == null)
                {
                    TaskDialog.Show("Error", "No active document.");
                    return Result.Failed;
                }

                if (doc.IsReadOnly)
                {
                    TaskDialog.Show("Error", "Document is read-only.");
                    return Result.Failed;
                }

                var vm = new DtlLineDimViewModel(uiApp);
                var window = new DtlLineDimWindow
                {
                    DataContext = vm
                };

                new WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;
                window.Show();

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                Logger.Error("DtlLineDimCommand", ex);
                message = ex.ToString();
                return Result.Failed;
            }
        }
    }
}
