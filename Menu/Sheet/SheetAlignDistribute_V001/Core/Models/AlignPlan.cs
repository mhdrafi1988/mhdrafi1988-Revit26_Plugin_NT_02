using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models
{
    /// <summary>What the plan does with one element on the sheet, and everything the grid shows for it.</summary>
    public sealed class AlignMove
    {
        /// <summary>Text for a cell with no value.</summary>
        public const string NoValue = "–";

        /// <summary>The element this is about.</summary>
        public SheetElementSnapshot Element { get; init; }

        /// <summary>The element id's value; identifies the row across refreshes.</summary>
        public long Key => Element?.Key ?? 0;

        /// <summary>How far it moves on X, in feet. Zero whenever it is not moved.</summary>
        public double DeltaX { get; init; }

        /// <summary>How far it moves on Y, in feet. Zero whenever it is not moved.</summary>
        public double DeltaY { get; init; }

        /// <summary>Where it ends up. Equal to its current footprint whenever it is not moved.</summary>
        public LayoutRect Target { get; init; }

        /// <summary>1-based row on the sheet, or null when it takes no part.</summary>
        public int? Row { get; init; }

        /// <summary>1-based column on the sheet, or null when it takes no part.</summary>
        public int? Column { get; init; }

        /// <summary>What the plan does with it.</summary>
        public AlignStatus Status { get; init; }

        /// <summary>Short explanation shown next to the status, or null.</summary>
        public string Note { get; init; }

        /// <summary>True when the user may tick or untick it.</summary>
        public bool CanTick { get; init; } = true;

        /// <summary>True when it takes part in this run.</summary>
        public bool IsTicked { get; init; } = true;

        /// <summary>Its own clearance, or null when it uses the sheet gap.</summary>
        public ViewPadding Pad { get; init; }

        /// <summary>True when Apply has to move this element.</summary>
        public bool NeedsMove => Status == AlignStatus.Move && (DeltaX != 0 || DeltaY != 0);

        /// <summary>How far it travels, in mm, to one decimal.</summary>
        public double DistanceMm => Units.ForDisplay(Units.FeetToMm(
            System.Math.Sqrt(DeltaX * DeltaX + DeltaY * DeltaY)));

        /// <summary>X movement in mm, to one decimal.</summary>
        public double DeltaXMm => Units.ForDisplay(Units.FeetToMm(DeltaX));

        /// <summary>Y movement in mm, to one decimal, positive up as on the sheet.</summary>
        public double DeltaYMm => Units.ForDisplay(Units.FeetToMm(DeltaY));

        /// <summary>"W × H" footprint in whole mm.</summary>
        public string SizeText => Element == null ? NoValue
            : $"{Units.FeetToMm(Element.Footprint.Width):F0} × {Units.FeetToMm(Element.Footprint.Height):F0}";

        /// <summary>The row number as shown in the grid.</summary>
        public string RowLabel => Row?.ToString() ?? NoValue;

        /// <summary>The column number as shown in the grid.</summary>
        public string ColumnLabel => Column?.ToString() ?? NoValue;

        /// <summary>X movement as shown in the grid ("+3", "−3", "0", or "–").</summary>
        public string DeltaXLabel => Status == AlignStatus.Move ? Signed(DeltaXMm) : NoValue;

        /// <summary>Y movement as shown in the grid.</summary>
        public string DeltaYLabel => Status == AlignStatus.Move ? Signed(DeltaYMm) : NoValue;

        /// <summary>How far it travels, as shown in the grid.</summary>
        public string DistanceLabel => Status == AlignStatus.Move || Status == AlignStatus.OverLimit
            ? DistanceMm.ToString("0.0") : NoValue;

        /// <summary>Its own clearance as shown in the grid: one number when all four sides match, else "T/B/L/R".</summary>
        public string PadLabel
        {
            get
            {
                if (Pad == null || Pad.IsEmpty) return NoValue;
                var sides = new[] { Pad.TopMm, Pad.BottomMm, Pad.LeftMm, Pad.RightMm };
                if (sides.All(s => s.HasValue) && sides.Distinct().Count() == 1)
                    return sides[0].Value.ToString("0.#");
                return string.Join("/", sides.Select(s => s.HasValue ? s.Value.ToString("0.#") : NoValue));
            }
        }

        /// <summary>Status as shown in the grid.</summary>
        public string StatusLabel => Status switch
        {
            AlignStatus.Move => "Move",
            AlignStatus.InPlace => "In place",
            AlignStatus.OverLimit => "Over the limit",
            AlignStatus.NoPartner => "No row or column partner",
            AlignStatus.Blocked => "Would leave the margins",
            _ => "Skipped"
        };

        private static string Signed(double mm) =>
            mm > 0 ? "+" + mm.ToString("0.#") : mm < 0 ? "−" + (-mm).ToString("0.#") : "0";
    }

    /// <summary>One run of <see cref="Services.AlignPlanner"/>: what would happen to the sheet.</summary>
    public sealed class AlignPlan
    {
        /// <summary>The sheet this was planned from.</summary>
        public SheetSnapshot Snapshot { get; init; }

        /// <summary>The area views may occupy, or null when it could not be worked out.</summary>
        public LayoutRect? Usable { get; init; }

        /// <summary>What the sheet's own margins and gaps measure, for the "Detected" labels.</summary>
        public DetectedLayout Detected { get; init; }

        /// <summary>One entry per element on the sheet, in reading order (row, then column).</summary>
        public List<AlignMove> Moves { get; init; } = new();

        /// <summary>Rows read off the sheet.</summary>
        public int RowCount { get; init; }

        /// <summary>Columns read off the sheet.</summary>
        public int ColumnCount { get; init; }

        /// <summary>Things worth saying in the log that are not about one view.</summary>
        public List<string> Notes { get; init; } = new();

        /// <summary>Why nothing can be applied, or null when it can.</summary>
        public string Blocker { get; init; }

        /// <summary>Elements that will be moved.</summary>
        public int MoveCount => Moves.Count(m => m.NeedsMove);

        /// <summary>Elements already within the snap tolerance of their line.</summary>
        public int InPlaceCount => Moves.Count(m => m.Status == AlignStatus.InPlace);

        /// <summary>Elements whose move is longer than the limit.</summary>
        public int OverLimitCount => Moves.Count(m => m.Status == AlignStatus.OverLimit);

        /// <summary>Elements left out of this run.</summary>
        public int SkippedCount => Moves.Count(m => m.Status == AlignStatus.Skipped);

        /// <summary>Elements with nothing to align to.</summary>
        public int NoPartnerCount => Moves.Count(m => m.Status == AlignStatus.NoPartner);

        /// <summary>The longest move the plan makes, in mm.</summary>
        public double LargestMoveMm => Moves.Where(m => m.NeedsMove)
            .Select(m => m.DistanceMm).DefaultIfEmpty(0).Max();

        /// <summary>True when Apply has something to do and nothing blocks it.</summary>
        public bool CanApply => Blocker == null && MoveCount > 0;

        /// <summary>
        /// Identifies this plan, so Apply can check that the sheet still matches what the user saw
        /// before it moves anything.
        /// </summary>
        public string Signature { get; init; }
    }
}
