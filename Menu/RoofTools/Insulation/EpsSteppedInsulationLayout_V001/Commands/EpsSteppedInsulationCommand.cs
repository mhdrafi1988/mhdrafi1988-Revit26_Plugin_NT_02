using System;
using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Events;
using Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Revit;
using Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.ViewModels;
using Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Commands
{
    /// <summary>
    /// EPS Stepped Insulation Layout — fills a footprint roof area with stepped EPS panels.
    /// Entry checks, then opens the modeless window. All model changes happen through
    /// <see cref="RunInsulationHandler"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class EpsSteppedInsulationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                Show("Open a project and a plan view, then run the tool again.");
                return Result.Cancelled;
            }

            try
            {
                Document doc = uidoc.Document;
                View view = doc.ActiveView;

                if (!(view is ViewPlan viewPlan))
                {
                    Show("Switch to a plan view, then run the tool again.");
                    return Result.Cancelled;
                }

                // Pick or use preselected roof
                FootPrintRoof roof = PickRoof(uidoc, out string pickMsg);
                if (roof == null)
                {
                    Show(pickMsg);
                    return Result.Cancelled;
                }

                // Validate shape editing
                SlabShapeEditor editor = roof.GetSlabShapeEditor();
                if (editor == null || !editor.IsValidObject)
                {
                    Show("The selected roof does not have shape editing enabled. Enable shape editing first.");
                    return Result.Cancelled;
                }

                // Read context in API context
                InsulationRoofContext context = RoofContextReader.Build(doc, viewPlan.Id, roof.Id);
                if (!context.IsValid)
                {
                    Show(context.Error ?? "Could not read the roof data.");
                    return Result.Cancelled;
                }

                if (context.Drains == null || context.Drains.Count == 0)
                {
                    Show("No drain points found. The roof must have shape editing points with varying elevations.");
                    return Result.Cancelled;
                }

                IReadOnlyList<RoofTypeOption> roofTypes = RoofTypeReader.GetAll(doc);

                // Create external events
                var runHandler = new RunInsulationHandler();
                var refreshHandler = new RefreshRoofHandler();
                ExternalEvent runEvent = ExternalEvent.Create(runHandler);
                ExternalEvent refreshEvent = ExternalEvent.Create(refreshHandler);

                var viewModel = new ViewModel_EpsSteppedInsulation(
                    context, roofTypes, runHandler, runEvent, refreshHandler, refreshEvent);

                var window = new EpsSteppedInsulationWindow(viewModel);
                new WindowInteropHelper(window) { Owner = uiapp.MainWindowHandle };
                window.Closed += (_, _) =>
                {
                    viewModel.Detach();
                    runEvent.Dispose();
                    refreshEvent.Dispose();
                };
                window.Show();

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Show("The tool could not start:\n" + ex.Message);
                return Result.Failed;
            }
        }

        private static FootPrintRoof PickRoof(UIDocument uidoc, out string message)
        {
            Document doc = uidoc.Document;
            message = string.Empty;

            ICollection<ElementId> pre = uidoc.Selection.GetElementIds();
            if (pre.Count == 1)
            {
                foreach (ElementId id in pre)
                {
                    if (doc.GetElement(id) is FootPrintRoof preRoof)
                        return preRoof;
                }
            }

            try
            {
                Reference picked = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new RoofSelectionFilter(),
                    "EPS Stepped Insulation: select a footprint roof (Esc to cancel)");

                if (doc.GetElement(picked) is FootPrintRoof roof) return roof;

                message = "The selected element is not a footprint roof.";
                return null;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                message = "No roof was selected.";
                return null;
            }
        }

        private static void Show(string text) =>
            TaskDialog.Show(EpsSteppedToolInfo.Title, text);
    }

    /// <summary>Footprint roofs only.</summary>
    internal sealed class RoofSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is FootPrintRoof;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
