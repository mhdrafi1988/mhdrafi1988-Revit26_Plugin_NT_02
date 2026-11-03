// =======================================================
// File: InnerLoopDividerCommand.cs
// Location: Commands/
// Changes vs V011:
//   FIX  No longer enables shape editing and resets every shape point to
//        offset 0 on launch. V011 did that before the window even opened,
//        wiping any slope already on the roof. Resetting is now an opt-in
//        checkbox applied inside the Apply transaction.
//   NEW  Pick is filtered to roofs; a roof already selected is used
//        without a pick.
//   NOTE First analysis still runs here, in this Execute()'s API context
//        (read-only now). Re-analyze / Apply go through the ExternalEvent.
// =======================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.InnerLoopDivider.V012.Infrastructure.Revit;
using Revit26_Plugin_Revised.InnerLoopDivider.V012.UI.ViewModels;
using Revit26_Plugin_Revised.InnerLoopDivider.V012.UI.Views;
using Revit26_Plugin_Revised.Utilities;
using System;
using System.Linq;
using System.Windows.Interop;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.InnerLoopDivider.V012.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class InnerLoopDividerCommand : IExternalCommand
    {
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
                RoofBase roof = uidoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .OfType<RoofBase>()
                    .FirstOrDefault();

                if (roof == null)
                {
                    Reference picked = uidoc.Selection.PickObject(ObjectType.Element, new RoofSelectionFilter(), "Select a roof");
                    roof = picked == null ? null : doc.GetElement(picked) as RoofBase;
                }

                if (roof == null)
                    return Result.Cancelled;

                RoofLoopReadResult read = RoofLoopReader.Read(roof);

                var vm = new InnerLoopDividerViewModel(commandData.Application, roof.Id, read.InnerLoops, circlesOnlyByDefault: true);
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

        private sealed class RoofSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is RoofBase;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
