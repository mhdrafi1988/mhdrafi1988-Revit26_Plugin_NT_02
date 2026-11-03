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
using Revit26_Plugin_Revised.OuterCurveDivider.V006.Core.Services;
using Revit26_Plugin_Revised.OuterCurveDivider.V006.UI.ViewModels;
using Revit26_Plugin_Revised.OuterCurveDivider.V006.UI.Views;
using System;
using System.Windows.Interop;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.OuterCurveDivider.V006.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CurveDividerCommand : IExternalCommand
    {
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
                    ObjectType.Element, new RoofSelectionFilter(),
                    "Select a roof to divide its curved edges");
                if (pickedRef == null) return Result.Cancelled;

                RoofBase roof = doc.GetElement(pickedRef) as RoofBase;
                if (roof == null)
                {
                    TaskDialog.Show("Curve Point Divider", "Selected element is not a roof.");
                    return Result.Failed;
                }

                var service = new CurveDivisionService();
                var initialEdges = service.ExtractNonLinearEdges(roof, out int filteredLines);

                var vm = new CurveDividerViewModel(roof.Id, initialEdges, filteredLines);
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

    public class RoofSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is RoofBase;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
