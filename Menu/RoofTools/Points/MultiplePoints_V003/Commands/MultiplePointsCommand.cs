// =======================================================
// File: MultiplePointsCommand.cs
// Location: Commands/
// Picks a roof, extracts its edges synchronously (still inside this
// Execute() call's own valid Revit API context), then shows the window
// modeless per this suite's standing convention — Apply goes through
// MultiplePointsEventManager/Handler (see Infrastructure/ExternalEvents).
// =======================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.MultiplePoints.V003.Core.Services;
using Revit26_Plugin_Revised.MultiplePoints.V003.UI.ViewModels;
using Revit26_Plugin_Revised.MultiplePoints.V003.UI.Views;
using System;
using System.Windows.Interop;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.MultiplePoints.V003.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MultiplePointsCommand : IExternalCommand
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
                    ObjectType.Element, new MultiplePointsRoofSelectionFilter(),
                    "Select a roof to add edge points");
                if (pickedRef == null) return Result.Cancelled;

                RoofBase roof = doc.GetElement(pickedRef) as RoofBase;
                if (roof == null)
                {
                    TaskDialog.Show("Multiple Points", "Selected element is not a roof.");
                    return Result.Failed;
                }

                var service = new EdgePointService();
                var initialEdges = service.ExtractEdges(roof);

                var vm = new MultiplePointsViewModel(roof.Id, initialEdges);
                var window = new MultiplePointsWindow { DataContext = vm };
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
                Logger.Error("MultiplePointsCommand", ex);
                TaskDialog.Show("Multiple Points — Exception", ex.Message);
                return Result.Failed;
            }
        }
    }

    public class MultiplePointsRoofSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is RoofBase;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
