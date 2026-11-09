// Merge Roofs V001 — entry point: gets the roofs, opens the modal window. All errors go through ToolGuard.
using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.MergeRoofs.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.MergeRoofs.V001.UI.ViewModels;
using Revit26_Plugin_Revised.MergeRoofs.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.MergeRoofs.V001.Commands
{
    /// <summary>Creates one footprint roof from several selected roofs.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MergeRoofsCommand : IExternalCommand
    {
        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, Run);

        private static Result Run(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument
                               ?? throw new InvalidOperationException("Open a project view first.");
            var roofs = SelectionHelper.GetRoofs(uidoc);
            var window = new MergeRoofsWindow(new MergeRoofsViewModel(uidoc, roofs));
            new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };
            window.ShowDialog();
            return Result.Succeeded;
        }
    }
}
