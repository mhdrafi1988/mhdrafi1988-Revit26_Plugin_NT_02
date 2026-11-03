using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.RoofRidgeLines.V069.Services;
using Revit26_Plugin_Revised.RoofRidgeLines.V069.ViewModels;
using Revit26_Plugin_Revised.RoofRidgeLines.V069.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofRidgeLines.V069.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoofRidgeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiDoc = commandData.Application.ActiveUIDocument;
                if (uiDoc == null)
                {
                    message = "No active document found.";
                    return Result.Failed;
                }

                // 1. Select Roof ONLY – no drain picker
                RoofBase selectedRoof = null;
                try
                {
                    var roofRef = uiDoc.Selection.PickObject(
                        ObjectType.Element,
                        new RoofSelectionFilter(),
                        "Select a roof element");
                    selectedRoof = uiDoc.Document.GetElement(roofRef) as RoofBase;
                    if (selectedRoof == null)
                    {
                        message = "Selected element is not a valid roof.";
                        return Result.Failed;
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                // 2. Launch UI – pass the selected roof.
                // The ViewModel will auto-load openings upon construction.
                // The window is modeless, so every later Revit API call (transactions,
                // PickObject) is routed through the dispatcher's ExternalEvent, which
                // must be created here while still in API context.
                var dispatcher = new RevitApiDispatcher();
                var viewModel = new RoofRidgeViewModel(uiDoc, dispatcher, selectedRoof);
                var view = new RoofRidgeView(viewModel);
                viewModel.SetOwnerWindow(view);
                view.Closed += (_, __) => dispatcher.Dispose();
                view.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("RoofRidgeCommand", ex);
                // For XAML/BAML load failures, ex.Message itself is always the same
                // generic "Provide value on '...MarkupExtension' threw an exception"
                // wrapper — WPF's loader adds it automatically whenever a markup
                // extension's ProvideValue() throws, and it never names the actual
                // resource/property/value at fault. It carries no diagnostic value,
                // so it's dropped here in favor of the real InnerException chain.
                var sb = new System.Text.StringBuilder("Roof Ridge Command failed:");
                var inner = ex.InnerException ?? ex;
                int depth = 0;
                while (inner != null && depth < 6)
                {
                    sb.Append($"\n  [{inner.GetType().Name}] {inner.Message}");
                    inner = inner.InnerException;
                    depth++;
                }
                message = sb.ToString();
                return Result.Failed;
            }
        }

        private class RoofSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is RoofBase;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
