using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Infrastructure;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.UI.ViewModels;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.Commands
{
    /// <summary>
    /// Arranges the tags of the selected spot elevations: lines them up in a column or row (average, most
    /// common, median, min / max or a reference tag), spaces them evenly or at a fixed gap, or places each
    /// one at the same offset from its measured point. Remembers the last options.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private sealed class SpotFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => SpotAlignService.IsSpotElevation(e);
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc?.Document;
            string title = ToolCatalog.AlignSpotElevations.Title;
            if (doc == null || doc.IsFamilyDocument) { TaskDialog.Show(title, "Open a project first."); return Result.Cancelled; }
            if (doc.IsReadOnly) { TaskDialog.Show(title, "This model is read-only, so tags cannot be moved."); return Result.Cancelled; }

            View view = doc.ActiveView;
            string viewProblem = SpotAlignService.CheckView(view);
            if (viewProblem != null) { TaskDialog.Show(title, viewProblem); return Result.Cancelled; }

            // Use the current selection if it holds spot elevations; otherwise ask for them.
            List<ElementId> ids = uiDoc.Selection.GetElementIds()
                .Where(id => SpotAlignService.IsSpotElevation(doc.GetElement(id))).ToList();
            if (ids.Count < 2)
            {
                ids = uiDoc.Selection.PickObjects(ObjectType.Element, new SpotFilter(),
                    "Select the spot elevations to align, then click Finish").Select(r => r.ElementId).ToList();
            }

            SpotReadResult read = SpotAlignService.Read(doc, view, ids);
            if (read.Items.Count < 2)
            {
                string why = read.Skipped.Count > 0 ? "\n\n" + string.Join("\n", read.Skipped) : string.Empty;
                TaskDialog.Show(title, "Select at least two spot elevations that can be moved." + why);
                return Result.Cancelled;
            }

            var vm = new AlignViewModel(read.Items, read.Skipped, read.Scale, AlignSettingsStore.Load());
            var window = new AlignWindow(vm);
            new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };
            if (window.ShowDialog() != true) return Result.Cancelled;

            AlignSettingsStore.Save(vm.Options);
            int moved = SpotAlignService.Apply(doc, view, vm.Plan.Entries, read.Scale, title, out string error);
            if (error != null)
            {
                TaskDialog.Show(title, "Nothing was changed. " + error);
                return Result.Failed;
            }
            TaskDialog.Show(title, $"{moved} spot elevation tag(s) aligned.");
            return Result.Succeeded;
        }
    }
}
