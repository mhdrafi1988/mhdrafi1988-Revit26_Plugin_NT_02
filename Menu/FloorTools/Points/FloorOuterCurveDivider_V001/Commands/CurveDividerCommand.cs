// =======================================================
// File: CurveDividerCommand.cs
// Location: Commands/
// Changes vs V002:
//   FIX  Window shown modeless (.Show()) instead of modal (.ShowDialog()).
//        The V002 ViewModel called CurveDivisionService directly from a
//        RelayCommand, which only worked because ShowDialog() kept this
//        Execute() call's own valid Revit API context alive for the whole
//        window lifetime. Modeless windows cannot call the Revit API
//        directly from a button click — edge extraction and Apply now go
//        through CurveDividerEventManager/Handler instead (see
//        Infrastructure/ExternalEvents).
//   FIX  Window parented via WindowInteropHelper to Revit's main window,
//        per this suite's standing convention (previously not set).
//   NOTE The first edge-extraction pass still runs synchronously here,
//        inside this Execute() call's own valid API context — avoids an
//        unnecessary ExternalEvent round-trip just to populate the window
//        on first open.
// =======================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Core.Services;
using Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.UI.ViewModels;
using Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.UI.Views;
using System;
using System.Windows.Interop;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CurveDividerCommand : IExternalCommand
    {
        /// <summary>Revit entry point; delegates to ToolGuard.RunCommand.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument    uidoc = uiapp.ActiveUIDocument;
            Document      doc   = uidoc.Document;

            try
            {
                Reference pickedRef = uidoc.Selection.PickObject(
                    ObjectType.Element, new FloorSelectionFilter(),
                    "Select a floor to divide its curved edges");
                if (pickedRef == null) return Result.Cancelled;

                Floor floor = doc.GetElement(pickedRef) as Floor;
                if (floor == null)
                {
                    TaskDialog.Show("Curve Point Divider", "Selected element is not a floor.");
                    return Result.Failed;
                }

                // Floor port: refuse floors that can't be changed (group, read-only,
                // borrowed by another user, no shape-editing support).
                string blockReason = FloorShapeEditChecks.GetBlockReason(floor);
                if (blockReason != null)
                {
                    TaskDialog.Show("Floor Outer Curve Divider", $"Floor {floor.Name} (Id {floor.Id.Value}) can't be changed: {blockReason}.");
                    return Result.Cancelled;
                }

                var service = new CurveDivisionService();
                var initialEdges = service.ExtractNonLinearEdges(floor, out int filteredLines);

                var vm = new CurveDividerViewModel(floor.Id, initialEdges, filteredLines);
                var window = new CurveDividerWindow { DataContext = vm };
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
                Logger.Error("CurveDividerCommand", ex);
                TaskDialog.Show("Curve Point Divider — Exception", ex.Message);
                return Result.Failed;
            }
        }
    }

    public class FloorSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Floor && (elem.GroupId == null || elem.GroupId == ElementId.InvalidElementId);
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
