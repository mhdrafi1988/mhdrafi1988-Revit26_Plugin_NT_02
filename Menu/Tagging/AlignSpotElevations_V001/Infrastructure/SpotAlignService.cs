using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.Infrastructure
{
    /// <summary>
    /// Reads spot elevation tags from the selection and moves their text. Only the tag moves; the
    /// measured point and leader end stay where they are.
    /// </summary>
    public static class SpotAlignService
    {
        /// <summary>Why the active view cannot hold spot elevations, or null when it can.</summary>
        public static string CheckView(View view)
        {
            if (view == null) return "Open a view first.";
            if (view is ViewSheet || view is ViewSchedule || view.ViewType == ViewType.DrawingSheet)
                return "Spot elevations are in model views. Open a plan, section, elevation or 3D view.";
            return null;
        }

        /// <summary>True when <paramref name="e"/> is a spot elevation.</summary>
        public static bool IsSpotElevation(Element e)
            => e is SpotDimension && e.Category != null
               && e.Category.Id.Value == (long)BuiltInCategory.OST_SpotElevations;

        /// <summary>
        /// Reads the tag positions of <paramref name="ids"/> in <paramref name="view"/>. Anything that is not a
        /// spot elevation of this view, is pinned, grouped, or owned by another user is skipped with a reason.
        /// </summary>
        public static SpotReadResult Read(Document doc, View view, IEnumerable<ElementId> ids)
        {
            var result = new SpotReadResult();
            XYZ right = view.RightDirection, up = view.UpDirection;
            foreach (ElementId id in ids)
            {
                Element e = doc.GetElement(id);
                if (e == null) continue;
                if (!IsSpotElevation(e)) continue; // other selected elements are ignored, not an error

                string reason = Blocker(doc, view, e);
                if (reason != null) { result.Skipped.Add($"Element {id.Value}: {reason}"); continue; }

                try
                {
                    var sd = (SpotDimension)e;
                    XYZ p = sd.TextPosition;
                    result.Items.Add(new SpotItem
                    {
                        Id = id.Value,
                        Label = LabelOf(sd),
                        U = p.DotProduct(right),
                        V = p.DotProduct(up)
                    });
                }
                catch (Exception ex)
                {
                    result.Skipped.Add($"Element {id.Value}: cannot read its position ({ex.Message})");
                }
            }
            return result;
        }

        private static string Blocker(Document doc, View view, Element e)
        {
            if (e.OwnerViewId != view.Id) return "belongs to another view";
            if (e.GroupId != null && e.GroupId != ElementId.InvalidElementId) return "is in a group";
            if (e.Pinned) return "is pinned";
            if (doc.IsWorkshared)
            {
                try
                {
                    if (WorksharingUtils.GetCheckoutStatus(doc, e.Id) == CheckoutStatus.OwnedByOtherUser)
                        return "is owned by another user";
                }
                catch { /* status unavailable — let the move try */ }
            }
            return null;
        }

        private static string LabelOf(SpotDimension sd)
        {
            string text = null;
            try { text = sd.ValueString; } catch { }
            return string.IsNullOrWhiteSpace(text) ? $"Spot {sd.Id.Value}" : $"{text}  ({sd.Id.Value})";
        }

        /// <summary>
        /// Moves the tags in one transaction (one Undo step). If any tag cannot be moved, nothing changes.
        /// Returns the number moved; <paramref name="error"/> is set when it rolled back.
        /// </summary>
        public static int Apply(Document doc, View view, IReadOnlyList<PlannedMove> moves,
            AlignDirection direction, string transactionName, out string error)
        {
            error = null;
            XYZ axis = direction == AlignDirection.Vertical ? view.RightDirection : view.UpDirection;
            int moved = 0;
            using (var t = new Transaction(doc, transactionName))
            {
                t.Start();
                try
                {
                    foreach (PlannedMove m in moves)
                    {
                        var sd = doc.GetElement(new ElementId(m.Item.Id)) as SpotDimension;
                        if (sd == null) throw new InvalidOperationException($"Element {m.Item.Id} no longer exists.");
                        sd.TextPosition = sd.TextPosition + axis.Multiply(m.Delta);
                        moved++;
                    }
                    t.Commit();
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    error = ex.Message;
                    return 0;
                }
            }
            return moved;
        }
    }
}
