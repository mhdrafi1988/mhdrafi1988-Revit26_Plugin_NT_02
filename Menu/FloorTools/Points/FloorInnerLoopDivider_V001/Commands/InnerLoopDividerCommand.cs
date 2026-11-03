// =======================================================
// File: InnerLoopDividerCommand.cs
// Location: Commands/
// Changes vs V011:
//   FIX  No longer enables shape editing and resets every shape point to
//        offset 0 on launch. V011 did that before the window even opened,
//        wiping any slope already on the floor. Resetting is now an opt-in
//        checkbox applied inside the Apply transaction.
//   NEW  Pick is filtered to floors; a floor already selected is used
//        without a pick.
//   NOTE First analysis still runs here, in this Execute()'s API context
//        (read-only now). Re-analyze / Apply go through the ExternalEvent.
// =======================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Infrastructure.Revit;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.UI.ViewModels;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.UI.Views;
using Revit26_Plugin_Revised.Utilities;
using System;
using System.Linq;
using System.Windows.Interop;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class InnerLoopDividerCommand : IExternalCommand
    {
        /// <summary>Revit entry point; delegates to ToolGuard.RunCommand.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "Open a project first.";
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;

            try
            {
                Floor floor = uidoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .OfType<Floor>()
                    .FirstOrDefault();

                if (floor == null)
                {
                    Reference picked = uidoc.Selection.PickObject(ObjectType.Element, new FloorSelectionFilter(), "Select a floor");
                    floor = picked == null ? null : doc.GetElement(picked) as Floor;
                }

                if (floor == null)
                    return Result.Cancelled;

                // Floor port: refuse floors that can't be changed (group, read-only,
                // borrowed by another user, no shape-editing support).
                string blockReason = FloorShapeEditChecks.GetBlockReason(floor);
                if (blockReason != null)
                {
                    TaskDialog.Show("Floor Inner Loop Divider", $"Floor {floor.Name} (Id {floor.Id.Value}) can't be changed: {blockReason}.");
                    return Result.Cancelled;
                }

                FloorLoopReadResult read = FloorLoopReader.Read(floor);

                var vm = new InnerLoopDividerViewModel(commandData.Application, floor.Id, read.InnerLoops, circlesOnlyByDefault: true);
                vm.ReportNotes(read.Notes);

                var window = new InnerLoopDividerWindow { DataContext = vm };
                new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
                window.Show();

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                Logger.Error("InnerLoopDividerCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private sealed class FloorSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Floor && (elem.GroupId == null || elem.GroupId == ElementId.InvalidElementId);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
