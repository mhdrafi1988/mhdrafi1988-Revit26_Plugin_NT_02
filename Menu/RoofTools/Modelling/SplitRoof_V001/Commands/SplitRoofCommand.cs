// Split Roof V001 — entry point: gets the roof and the cut lines, opens the modal window. All errors go through ToolGuard.
using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.SplitRoof.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.SplitRoof.V001.UI.ViewModels;
using Revit26_Plugin_Revised.SplitRoof.V001.UI.Views;

namespace Revit26_Plugin_Revised.SplitRoof.V001.Commands
{
    /// <summary>Splits one footprint roof into several along straight lines.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SplitRoofCommand : IExternalCommand
    {
        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, Run);

        private static Result Run(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument
                               ?? throw new InvalidOperationException("Open a project view first.");
            var roof = SelectionHelper.GetRoof(uidoc);
            var lines = SelectionHelper.GetCutLines(uidoc);
            var window = new SplitRoofWindow(new SplitRoofViewModel(uidoc, roof, lines));
            new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };
            window.ShowDialog();
            return Result.Succeeded;
        }
    }
}
