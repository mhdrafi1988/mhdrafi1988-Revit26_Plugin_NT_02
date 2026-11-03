using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.UI.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.Commands
{
    /// <summary>
    /// Ribbon PushButton entry point. VA007 replaces VA006's "must be run from a
    /// supported plan view" gate with a pre-selection gate: the user must have a
    /// single Floor or Roof selected in the host model BEFORE launching the tool.
    /// No window is ever shown for an invalid/missing selection — just a warning
    /// and Result.Cancelled, per the enhancement spec's entry-flow requirement.
    /// A valid placed Revit link must also exist, same as VA006.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenLinkedDetailLineGeneratorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;
            View activeView = uiDoc.ActiveView;

            // ── Entry flow: require a single pre-selected Floor/Roof ─────────
            Element? selectedElement;
            try
            {
                selectedElement = GetSelectedFloorOrRoof(uiDoc, doc);
            }
            catch (Exception ex)
            {
                Logger.Error("OpenLinkedDetailLineGeneratorCommand", ex);
                message = $"Failed to read current selection: {ex.Message}";
                return Result.Failed;
            }

            if (selectedElement == null)
            {
                TaskDialog.Show(
                    "Boundary Line Generator",
                    "Select a single Floor or Roof before running this command.\n\n" +
                    "The tool generates lines inside that element's own boundary — " +
                    "nothing is drawn without a Floor or Roof selected first.");
                return Result.Cancelled;
            }

            // ── Validate Active View (Detail Lines are view-specific) ────────
            if (!IsSupportedPlanView(activeView))
            {
                TaskDialog.Show(
                    "Boundary Line Generator",
                    "This command can only be run from a supported plan view.");
                return Result.Cancelled;
            }

            // ── Validate Linked Revit Models ─────────────────────────────────
            bool hasValidLink;
            try
            {
                hasValidLink = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .Any(li => IsValidPlacedLink(li));
            }
            catch (Exception ex)
            {
                Logger.Error("OpenLinkedDetailLineGeneratorCommand", ex);
                message = $"Failed to validate linked models: {ex.Message}";
                return Result.Failed;
            }

            if (!hasValidLink)
            {
                TaskDialog.Show(
                    "Boundary Line Generator",
                    "No valid placed Revit link was found in the current project.");
                return Result.Cancelled;
            }

            // ── Open UI (modeless, matches suite convention) ───────────────
            try
            {
                var window = new MainWindow(uiApp, selectedElement);
                window.Show();
            }
            catch (Exception ex)
            {
                Logger.Error("OpenLinkedDetailLineGeneratorCommand", ex);
                message = $"Failed to open Boundary Line Generator: {ex.Message}";
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        /// <summary>Returns the single pre-selected Floor or Roof from the host
        /// document, or null if the selection is empty or contains no Floor/Roof.
        /// If multiple elements are selected, the first Floor/Roof found among
        /// them is used — the rest are ignored rather than treated as an error,
        /// since a user often box-selects a Floor along with nearby annotation.</summary>
        private static Element? GetSelectedFloorOrRoof(UIDocument uiDoc, Document doc)
        {
            var selectedIds = uiDoc.Selection.GetElementIds();
            if (selectedIds.Count == 0) return null;

            foreach (var id in selectedIds)
            {
                Element el = doc.GetElement(id);
                if (IsFloorOrRoof(el)) return el;
            }
            return null;
        }

        private static bool IsFloorOrRoof(Element? element)
        {
            if (element?.Category == null) return false;
            var bic = (BuiltInCategory)element.Category.Id.Value;
            return bic == BuiltInCategory.OST_Floors || bic == BuiltInCategory.OST_Roofs;
        }

        /// <summary>Floor Plan and Structural Plan supported, matching VA006.
        /// Ceiling Plans intentionally excluded pending view-range behavior validation.</summary>
        private static bool IsSupportedPlanView(View view)
        {
            if (view == null) return false;
            return view.ViewType == ViewType.FloorPlan
                || view.ViewType == ViewType.EngineeringPlan; // Structural Plan
        }

        /// <summary>A link is valid if it has a placed instance and a readable linked
        /// document. Unloaded links (GetLinkDocument() == null) are excluded — VA007's
        /// window never lists them at all (see LinkService.GetLinkedModels).</summary>
        private static bool IsValidPlacedLink(RevitLinkInstance linkInstance)
        {
            try
            {
                Document linkedDoc = linkInstance.GetLinkDocument();
                return linkedDoc != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
