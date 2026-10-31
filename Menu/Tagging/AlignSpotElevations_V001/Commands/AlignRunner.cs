using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Infrastructure;
using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.Commands
{
    /// <summary>Everything a command needs once the selection has been read.</summary>
    internal sealed class AlignContext
    {
        public Document Doc { get; set; }
        public View View { get; set; }
        public SpotReadResult Read { get; set; }
        public string Title => ToolCatalog.AlignSpotElevations.Title;
    }

    /// <summary>Shared steps of the window and quick-align commands: checks, selection, reading and applying.</summary>
    internal static class AlignRunner
    {
        private sealed class SpotFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => SpotAlignService.IsSpotElevation(e);
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        /// <summary>
        /// Checks the document and view, takes the selected spot elevations (or asks for them) and reads them.
        /// Returns null after telling the user why when the command cannot go on.
        /// </summary>
        public static AlignContext Prepare(ExternalCommandData commandData)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc?.Document;
            string title = ToolCatalog.AlignSpotElevations.Title;
            if (doc == null || doc.IsFamilyDocument) { TaskDialog.Show(title, "Open a project first."); return null; }
            if (doc.IsReadOnly) { TaskDialog.Show(title, "This model is read-only, so tags cannot be moved."); return null; }

            View view = doc.ActiveView;
            string viewProblem = SpotAlignService.CheckView(view);
            if (viewProblem != null) { TaskDialog.Show(title, viewProblem); return null; }

            // Use the current selection if it holds spot elevations; otherwise ask for them.
            // Cancelling the pick throws OperationCanceledException, which ToolGuard turns into Cancelled.
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
                return null;
            }
            return new AlignContext { Doc = doc, View = view, Read = read };
        }

        /// <summary>Applies <paramref name="plan"/> as one Undo step and reports the result.</summary>
        public static Result Apply(AlignContext ctx, ArrangePlan plan, string whatHappened)
        {
            int moved = SpotAlignService.Apply(ctx.Doc, ctx.View, plan.Entries, ctx.Read.Scale, ctx.Title, out string error);
            if (error != null)
            {
                TaskDialog.Show(ctx.Title, "Nothing was changed. " + error);
                return Result.Failed;
            }

            var lines = new List<string> { $"{moved} spot elevation tag(s) {whatHappened}." };
            if (plan.OverlapCount > 0)
                lines.Add($"{plan.OverlapCount} tag(s) may overlap (estimated from the text size). " +
                          "Undo, or open Align Spot Elevations and choose even spacing or a fixed gap.");
            if (ctx.Read.Skipped.Count > 0)
                lines.Add($"Skipped:\n{string.Join("\n", ctx.Read.Skipped)}");
            TaskDialog.Show(ctx.Title, string.Join("\n\n", lines));
            return Result.Succeeded;
        }
    }
}
