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
                    XYZ pt = PointOf(sd);
                    XYZ end = LeaderEndOf(sd);
                    XYZ shoulder = end != null ? ShoulderOf(sd) : null;
                    EstimateSize(doc, sd, out double widthMm, out double heightMm);
                    var item = new SpotItem
                    {
                        WidthMm = widthMm,
                        HeightMm = heightMm,
                        Id = id.Value,
                        Label = LabelOf(sd),
                        U = p.DotProduct(right) * toPaper,
                        V = p.DotProduct(up) * toPaper,
                        TextU = p.DotProduct(right) * toPaper,
                        TextV = p.DotProduct(up) * toPaper,
                        HasPoint = pt != null,
                        PointU = pt != null ? pt.DotProduct(right) * toPaper : 0,
                        PointV = pt != null ? pt.DotProduct(up) * toPaper : 0,
                        HasLeader = end != null,
                        EndU = end != null ? end.DotProduct(right) * toPaper : 0,
                        EndV = end != null ? end.DotProduct(up) * toPaper : 0,
                        HasShoulder = shoulder != null,
                        ShoulderU = shoulder != null ? shoulder.DotProduct(right) * toPaper : 0,
                        ShoulderV = shoulder != null ? shoulder.DotProduct(up) * toPaper : 0
                    };
                    result.Items.Add(item);
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

        /// <summary>
        /// The measured point (the spot's origin), or null when it cannot be read. The leader end is not a
        /// substitute: it is the other end of the leader, at the text.
        /// </summary>
        private static XYZ PointOf(SpotDimension sd)
        {
            try { return sd.Origin; } catch { return null; }
        }

        /// <summary>The leader end (where the leader meets the text), or null when the spot has no leader.</summary>
        private static XYZ LeaderEndOf(SpotDimension sd)
        {
            try { return sd.HasLeader ? sd.LeaderEndPosition : null; } catch { return null; }
        }

        /// <summary>The leader shoulder, or null when the leader has none.</summary>
        private static XYZ ShoulderOf(SpotDimension sd)
        {
            try { return sd.LeaderHasShoulder ? sd.LeaderShoulderPosition : null; } catch { return null; }
        }

        private static string LabelOf(SpotDimension sd)
        {
            string text = null;
            try { text = sd.ValueString; } catch { }
            return string.IsNullOrWhiteSpace(text) ? $"Spot {sd.Id.Value}" : $"{text}  ({sd.Id.Value})";
        }

        /// <summary>
        /// Moves the tags in one transaction (one Undo step). If any tag cannot be moved, nothing changes.
        /// Each move shifts the point named by its item's <see cref="SpotItem.Anchor"/>; deltas are paper mm in
        /// the view's axes and <paramref name="scale"/> turns them back into model feet. After the moves every
        /// tag is read back, and tags whose point landed more than <see cref="LandToleranceMm"/> from the plan
        /// are listed in <see cref="ApplyResult.NotExact"/> (they are still moved).
        /// </summary>
        public static ApplyResult Apply(Document doc, View view, IEnumerable<PlannedMove> moves, int scale,
            string transactionName)
        {
            var result = new ApplyResult();
            double toFeet = Math.Max(1, scale) / MmPerFoot;
            XYZ right = view.RightDirection, up = view.UpDirection;
            var targets = new List<(SpotDimension Spot, PlannedMove Move, XYZ Target)>();
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
                        XYZ target = AnchorOf(sd, m.Item.Anchor) + shift;
                        MoveAnchor(doc, sd, m.Item.Anchor, shift, target);
                        targets.Add((sd, m, target));
                    }

                    doc.Regenerate();
                    foreach (var (spot, move, target) in targets)
                    {
                        XYZ now = AnchorOf(spot, move.Item.Anchor);
                        double offMm = (now - target).GetLength() / toFeet;
                        if (offMm > LandToleranceMm)
                            result.NotExact.Add($"{move.Item.Label}: {AnchorName(move.Item.Anchor)} is {offMm:0.#} mm from the planned position");
                    }
                    if (t.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit did not accept the change.");
                    result.Moved = targets.Count;
                }
                catch (Exception ex)
                {
                    if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
                    result.Moved = 0;
                    result.NotExact.Clear();
                    result.Error = ex.Message;
                }
            }
            return result;
        }

        /// <summary>A tag whose point lands farther than this from the plan is reported, paper mm.</summary>
        public const double LandToleranceMm = 0.25;

        /// <summary>Plain name of an anchor point for messages.</summary>
        public static string AnchorName(AnchorPoint anchor) => anchor switch
        {
            AnchorPoint.LeaderEnd => "leader end",
            AnchorPoint.Shoulder => "shoulder",
            _ => "text"
        };

        private static XYZ AnchorOf(SpotDimension sd, AnchorPoint anchor) => anchor switch
        {
            AnchorPoint.LeaderEnd => sd.LeaderEndPosition,
            AnchorPoint.Shoulder => sd.LeaderShoulderPosition,
            _ => sd.TextPosition
        };

        /// <summary>
        /// Moves one spot's anchor to <paramref name="target"/>. The measured point never moves.
        /// <list type="bullet">
        /// <item>Text: shifts the text drag point.</item>
        /// <item>Shoulder: shifts the shoulder; Revit moves the leader end and text with it.</item>
        /// <item>Leader end without a shoulder: sets the leader end; Revit moves the text with it.</item>
        /// <item>Leader end with a shoulder: Revit keeps the end on the shoulder line, so the shoulder is first
        /// shifted across that line (keeping its direction, e.g. horizontal), then the end slides along it to the
        /// target.</item>
        /// </list>
        /// </summary>
        private static void MoveAnchor(Document doc, SpotDimension sd, AnchorPoint anchor, XYZ shift, XYZ target)
        {
            switch (anchor)
            {
                case AnchorPoint.Shoulder:
                    sd.LeaderShoulderPosition = sd.LeaderShoulderPosition + shift;
                    break;

                case AnchorPoint.LeaderEnd:
                    if (sd.LeaderHasShoulder)
                    {
                        XYZ s = sd.LeaderShoulderPosition;
                        XYZ along = sd.LeaderEndPosition - s;
                        XYZ across = shift;
                        if (along.GetLength() > 1e-9)
                        {
                            XYZ dir = along.Normalize();
                            across = shift - dir.Multiply(shift.DotProduct(dir));
                        }
                        if (across.GetLength() > 1e-9)
                        {
                            sd.LeaderShoulderPosition = s + across;
                            doc.Regenerate();
                        }
                    }
                    sd.LeaderEndPosition = target;
                    break;

                default:
                    sd.TextPosition = sd.TextPosition + shift;
                    break;
            }
        }
    }

    /// <summary>Outcome of <see cref="SpotAlignService.Apply"/>.</summary>
    public sealed class ApplyResult
    {
        /// <summary>Number of tags moved (0 when rolled back).</summary>
        public int Moved { get; set; }

        /// <summary>Tags moved but not exactly to the planned position, with how far off.</summary>
        public List<string> NotExact { get; } = new List<string>();

        /// <summary>Why nothing changed, or null on success.</summary>
        public string Error { get; set; }
    }
}
