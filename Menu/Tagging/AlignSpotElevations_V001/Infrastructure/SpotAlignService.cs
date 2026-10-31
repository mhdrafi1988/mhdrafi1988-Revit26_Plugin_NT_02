using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.Infrastructure
{
    /// <summary>
    /// Reads spot elevation tags from the selection and moves their text. Only the tag moves; the
    /// measured point and leader end stay where they are. Positions are passed to the engine in paper mm
    /// (model feet × 304.8 ÷ view scale) so tolerances and gaps match the printed drawing.
    /// </summary>
    public static class SpotAlignService
    {
        private const double MmPerFoot = 304.8;

        /// <summary>The view's scale, at least 1 (3D and drafting views can report odd values).</summary>
        public static int ScaleOf(View view)
        {
            try { return Math.Max(1, view.Scale); } catch { return 1; }
        }

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
            var result = new SpotReadResult { Scale = ScaleOf(view) };
            XYZ right = view.RightDirection, up = view.UpDirection;
            double toPaper = MmPerFoot / result.Scale;
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
                    XYZ pt = PointOf(sd) ?? p;
                    EstimateSize(doc, sd, out double widthMm, out double heightMm);
                    result.Items.Add(new SpotItem
                    {
                        WidthMm = widthMm,
                        HeightMm = heightMm,
                        Id = id.Value,
                        Label = LabelOf(sd),
                        U = p.DotProduct(right) * toPaper,
                        V = p.DotProduct(up) * toPaper,
                        PointU = pt.DotProduct(right) * toPaper,
                        PointV = pt.DotProduct(up) * toPaper
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

        /// <summary>Average character width as a share of the text height, for the size estimate.</summary>
        private const double CharWidthFactor = 0.62;

        /// <summary>
        /// Estimates the tag text size on paper from the type's text size and width factor and the length of
        /// the shown value. Revit does not expose the text extents of a spot, so this is an estimate used only
        /// for the overlap warning. Both values are 0 when the type gives no text size.
        /// </summary>
        private static void EstimateSize(Document doc, SpotDimension sd, out double widthMm, out double heightMm)
        {
            widthMm = 0; heightMm = 0;
            try
            {
                if (!(doc.GetElement(sd.GetTypeId()) is DimensionType type)) return;
                double sizeFt = type.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 0;
                if (sizeFt <= 0) return;
                double widthScale = type.get_Parameter(BuiltInParameter.TEXT_WIDTH_SCALE)?.AsDouble() ?? 1;
                if (widthScale <= 0) widthScale = 1;
                string text = null;
                try { text = sd.ValueString; } catch { }
                int chars = Math.Max(4, (text ?? string.Empty).Length);
                heightMm = sizeFt * MmPerFoot;
                widthMm = chars * CharWidthFactor * heightMm * widthScale;
            }
            catch
            {
                widthMm = 0; heightMm = 0; // unknown: the engine falls back to a small default box
            }
        }

        /// <summary>The measured point: the spot's origin, else its leader end; null when neither can be read.</summary>
        private static XYZ PointOf(SpotDimension sd)
        {
            try { XYZ o = sd.Origin; if (o != null) return o; } catch { }
            try { if (sd.HasLeader) return sd.LeaderEndPosition; } catch { }
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
        /// Deltas are paper mm in the view's axes; <paramref name="scale"/> turns them back into model feet.
        /// Returns the number moved; <paramref name="error"/> is set when it rolled back.
        /// </summary>
        public static int Apply(Document doc, View view, IEnumerable<PlannedMove> moves, int scale,
            string transactionName, out string error)
        {
            error = null;
            double toFeet = Math.Max(1, scale) / MmPerFoot;
            XYZ right = view.RightDirection, up = view.UpDirection;
            int moved = 0;
            using (var t = new Transaction(doc, transactionName))
            {
                t.Start();
                try
                {
                    foreach (PlannedMove m in moves.Where(x => x.Moves))
                    {
                        var sd = doc.GetElement(new ElementId(m.Item.Id)) as SpotDimension;
                        if (sd == null) throw new InvalidOperationException($"Element {m.Item.Id} no longer exists.");
                        XYZ shift = right.Multiply(m.DeltaU * toFeet) + up.Multiply(m.DeltaV * toFeet);
                        sd.TextPosition = sd.TextPosition + shift;
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
