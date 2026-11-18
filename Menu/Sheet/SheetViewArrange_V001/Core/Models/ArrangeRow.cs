namespace Revit26_Plugin_Revised.SheetViewArrange.V001.Core.Models
{
    /// <summary>Status of one view in the planned arrangement.</summary>
    public enum ArrangeStatus
    {
        /// <summary>Will be moved to its slot.</summary>
        Move,

        /// <summary>Already in its slot — nothing to do.</summary>
        InPlace,

        /// <summary>Its slot falls outside the usable area.</summary>
        DoesNotFit,

        /// <summary>Left where it is (pinned, owned by another user, or unticked).</summary>
        Skipped
    }

    /// <summary>One line in the Views grid: a viewport and where the plan puts it.</summary>
    public sealed class ArrangeRow
    {
        /// <summary>Text for an empty "#" / Row cell.</summary>
        public const string NoValue = "–";

        /// <summary>The viewport's element id value; identifies the row across refreshes.</summary>
        public long ViewportKey { get; init; }

        /// <summary>
        /// Position of the view in the sheet's reading order (0-based, every view counted, ticked
        /// or not). Keeps the list stable while views are ticked and unticked.
        /// </summary>
        public int ReadingKey { get; init; }

        /// <summary>Reading-order position among the arranged views (1-based), or null when skipped.</summary>
        public int? Order { get; init; }

        /// <summary>The viewport's Detail Number.</summary>
        public string DetailNumber { get; init; }

        /// <summary>View name.</summary>
        public string ViewName { get; init; }

        /// <summary>View type display name.</summary>
        public string ViewTypeName { get; init; }

        /// <summary>"W × H" footprint in mm.</summary>
        public string SizeText { get; init; }

        /// <summary>Footprint area in mm² (for sorting by size).</summary>
        public double SizeArea { get; init; }

        /// <summary>1-based row on the sheet, or null when skipped.</summary>
        public int? Row { get; init; }

        /// <summary>Planned status.</summary>
        public ArrangeStatus Status { get; init; }

        /// <summary>
        /// True when the user may tick or untick the view. False for views that are left alone
        /// anyway (owned by another user, or pinned while "Move pinned views" is off).
        /// </summary>
        public bool CanTick { get; init; } = true;

        /// <summary>True when the view takes part in the arrangement (ticked and tickable).</summary>
        public bool IsTicked { get; init; } = true;

        /// <summary>Status as shown in the grid.</summary>
        public string StatusLabel => Status switch
        {
            ArrangeStatus.Move => "Move",
            ArrangeStatus.InPlace => "In place",
            ArrangeStatus.DoesNotFit => "Doesn't fit",
            _ => "Skipped"
        };

        /// <summary>Short explanation shown next to the status.</summary>
        public string Note { get; init; }

        /// <summary>
        /// True when <see cref="Row"/> counts columns because the layout flows down columns, so the
        /// grid and the group headers say "Column" instead of "Row".
        /// </summary>
        public bool BandIsColumn { get; init; }

        /// <summary>The band number as shown in the grid and its filter ("2", or "–" when none).</summary>
        public string RowLabel => Row?.ToString() ?? NoValue;

        /// <summary>Group header text when grouping by layout row.</summary>
        public string GroupRowName => Row is int r
            ? (BandIsColumn ? $"Column {r}" : $"Row {r}")
            : (BandIsColumn ? "Not in a column" : "Not on a row");
    }
}
