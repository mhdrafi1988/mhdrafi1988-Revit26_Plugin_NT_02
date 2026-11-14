using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.WorksetRenamer.FX05.ViewModels;
using Revit26_Plugin_Revised.WorksetRenamer.FX05.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.WorksetRenamer.FX05
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;

                // Guard: worksharing must be enabled
                if (!doc.IsWorkshared)
                {
                    TaskDialog.Show("Workset Renamer — From Excel",
                        "This tool requires a workshared model.\n" +
                        "Enable worksharing before running Workset Renamer FX03.");
                    return Result.Cancelled;
                }

                var viewModel = new WorksetRenamerFxViewModel(doc);
                var view = new WorksetRenamerFxView(viewModel);
                new WindowInteropHelper(view).Owner = commandData.Application.MainWindowHandle;
                view.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("Command", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
