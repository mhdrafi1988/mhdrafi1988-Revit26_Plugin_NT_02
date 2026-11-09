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
        public UIDocument UiDoc { get; set; }

        /// <summary>A straight detail line that was selected with the spots (exactly one), or null.</summary>
        public DetailLine2D PreselectedLine { get; set; }
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

        private sealed class DetailLineFilter : ISelectionFilter
        {
            private readonly View _view;
            public DetailLineFilter(View view) { _view = view; }
            public bool AllowElement(Element e) => SpotAlignService.IsStraightDetailLine(e, _view);
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        /// <summary>
        /// Lets the user click one straight detail line in the active view for <paramref name="part"/>.
        /// Returns null when the pick is cancelled (Esc) or the line cannot be used (a message says why).
        /// </summary>
        public static DetailLine2D PickDetailLine(AlignContext ctx, SpotPart part)
        {
            string what = part == SpotPart.Text ? "text" : part == SpotPart.LeaderEnd ? "leader end points" : "bend points";
            try
            {
                Reference r = ctx.UiDoc.Selection.PickObject(ObjectType.Element, new DetailLineFilter(ctx.View),
                    $"Click a straight detail line for the {what} (Esc keeps the current one)");
                DetailLine2D line = SpotAlignService.ReadDetailLine(ctx.Doc.GetElement(r), ctx.View, ctx.Read.Scale);
                if (line == null)
                    TaskDialog.Show(ctx.Title, "That detail line is too short in this view to align to. Pick another one.");
                return line;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null; // Esc: keep what was there
            }
        }

        /// <summary>
        /// Lets the user click one spot elevation in the active view and returns an endless line through its
        /// leader end (<paramref name="part"/> = LeaderEnd) or bend point (LeaderShoulder), vertical (same X)
        /// or horizontal (same Y) as <paramref name="direction"/> says. Returns null when the pick is
        /// cancelled (Esc, which throws only when <paramref name="escThrows"/> is true) or the tag has no
        /// such point (a message says why).
        /// </summary>
        public static DetailLine2D PickTagLine(AlignContext ctx, SpotPart part, AlignDirection direction, bool escThrows = false)
        {
            if (part == SpotPart.Text) throw new System.ArgumentException("Tag lines are for the leader end or bend point only.", nameof(part));
            string what = part == SpotPart.LeaderEnd ? "end points" : "bend points";
            Reference r;
            try
            {
                r = ctx.UiDoc.Selection.PickObject(ObjectType.Element, new SpotFilter(),
                    $"Click the spot elevation to line the {what} up with (Esc cancels)");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                if (escThrows) throw;
                return null; // Esc: keep what was there
            }

            SpotItem tag = SpotAlignService.ReadReference(ctx.Doc, ctx.View, ctx.Doc.GetElement(r), ctx.Read.Scale, out string why);
            if (tag != null && part == SpotPart.LeaderEnd && !tag.HasEnd)
                why = "That spot elevation has no leader, so it has no end point to line up with.";
            else if (tag != null && part == SpotPart.LeaderShoulder && !tag.HasShoulder)
                why = "That spot elevation has no leader bend point to line up with.";
            if (why != null)
            {
                TaskDialog.Show(ctx.Title, why + " Pick another one.");
                return null;
            }
            return part == SpotPart.LeaderEnd
                ? DetailLine2D.ThroughTag(tag.EndU, tag.EndV, direction, tag.Id, tag.Label, part)
                : DetailLine2D.ThroughTag(tag.ShoulderU, tag.ShoulderV, direction, tag.Id, tag.Label, part);
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
            ICollection<ElementId> selected = uiDoc.Selection.GetElementIds();
            List<ElementId> ids = selected
                .Where(id => SpotAlignService.IsSpotElevation(doc.GetElement(id))).ToList();
            List<ElementId> lineIds = selected
                .Where(id => SpotAlignService.IsStraightDetailLine(doc.GetElement(id), view)).ToList();
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
            // Exactly one straight detail line selected with the spots: offer it for Align to detail lines.
            DetailLine2D preselected = lineIds.Count == 1
                ? SpotAlignService.ReadDetailLine(doc.GetElement(lineIds[0]), view, read.Scale) : null;
            return new AlignContext { Doc = doc, View = view, Read = read, UiDoc = uiDoc, PreselectedLine = preselected };
        }

        /// <summary>Applies <paramref name="plan"/> as one Undo step and reports the result.</summary>
        public static Result Apply(AlignContext ctx, ArrangePlan plan, string whatHappened)
        {
            // Esc / Cancel with a Yes-No confirm; the move transaction rolls back on cancel.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.AlignSpotElevations, ctx.UiDoc?.Application);
            int moved = SpotAlignService.Apply(ctx.Doc, ctx.View, plan.Entries, ctx.Read.Scale, ctx.Title,
                out string error, out int endsShort);
            if (error != null)
            {
                TaskDialog.Show(ctx.Title, "Nothing was changed. " + error);
                return Result.Failed;
            }

            var lines = new List<string> { $"{moved} spot elevation(s) {whatHappened}." };
            if (endsShort > 0)
                lines.Add($"{endsShort} leader end(s) did not reach the line: a leader with a bend point only lets " +
                          "its end slide along the bend-to-end segment. Also align the bend points to move them further.");
            if (plan.MissingEndCount > 0)
                lines.Add($"{plan.MissingEndCount} spot(s) have no leader, so there was no end point to move.");
            if (plan.MissingShoulderCount > 0)
                lines.Add($"{plan.MissingShoulderCount} spot(s) have no bend point, so there was none to move.");
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
