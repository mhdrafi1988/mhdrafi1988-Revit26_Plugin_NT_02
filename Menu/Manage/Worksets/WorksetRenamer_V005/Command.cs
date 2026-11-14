using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.WorksetRenamer.V005.ViewModels;
using Revit26_Plugin_Revised.WorksetRenamer.V005.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.WorksetRenamer.V005
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
                    TaskDialog.Show("Workset Renamer",
                        "This tool requires a workshared model.\n" +
                        "Enable worksharing before running Workset Renamer.");
                    return Result.Cancelled;
                }

                var viewModel = new WorksetRenamerViewModel(doc);
                var view      = new WorksetRenamerView(viewModel);
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
