using Revit26_Plugin_Revised.SheetViewArrange.V001.Core.Layout;

namespace Revit26_Plugin_Revised.SheetViewArrange.V001.Core.Models
{
    /// <summary>
    /// Persisted to %AppData%\Revit26_Plugin_Revised\SheetViewArrange\settings.json.
    /// Margins are measured inward from the title block's bounding box; gaps are minimums
    /// (rows and columns are spread wider to fill the area).
    /// </summary>
    public sealed class SheetViewArrangeSettings
    {
        /// <summary>
        /// Current settings-file format version. 2 added <see cref="GroupBy"/>; 3 added
        /// <see cref="Horizontal"/>, <see cref="Vertical"/> and <see cref="RowAlign"/>; 4 added
        /// <see cref="Flow"/> and <see cref="ColumnAlign"/>. Older files load unchanged — every new
        /// field defaults to the behaviour that version had.
        /// </summary>
        public const int CurrentVersion = 4;

        /// <summary>Format version of the file this was read from.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>Top margin inside the title block (mm).</summary>
        public double MarginTopMm { get; set; } = 30;

        /// <summary>Bottom margin inside the title block (mm).</summary>
        public double MarginBottomMm { get; set; } = 150;

        /// <summary>Left margin inside the title block (mm).</summary>
        public double MarginLeftMm { get; set; } = 30;

        /// <summary>Right margin inside the title block (mm) — typically clears the title strip.</summary>
        public double MarginRightMm { get; set; } = 200;

        /// <summary>Minimum gap between views in a row (mm).</summary>
        public double MinHorizontalGapMm { get; set; } = 20;

        /// <summary>Minimum gap between rows (mm).</summary>
        public double MinVerticalGapMm { get; set; } = 20;

        /// <summary>How a short last row is placed.</summary>
        public LastRowMode LastRowMode { get; set; } = LastRowMode.PackLeft;

        /// <summary>Move pinned viewports too (they are re-pinned afterwards).</summary>
        public bool MovePinned { get; set; } = true;

        /// <summary>How the Views grid was grouped last time (display only; added in version 2).</summary>
        public ArrangeGroupBy GroupBy { get; set; } = ArrangeGroupBy.None;

        /// <summary>
        /// Where each row sits across the usable width (added in version 3). Defaults to
        /// <see cref="HorizontalLayoutMode.Auto"/>, the behaviour before the setting existed.
        /// </summary>
        public HorizontalLayoutMode Horizontal { get; set; } = HorizontalLayoutMode.Auto;

        /// <summary>
        /// Where the block of rows sits down the usable height (added in version 3). Defaults to
        /// <see cref="VerticalLayoutMode.Auto"/>, the behaviour before the setting existed.
        /// </summary>
        public VerticalLayoutMode Vertical { get; set; } = VerticalLayoutMode.Auto;

        /// <summary>
        /// How views of differing height sit inside their row (added in version 3). Defaults to
        /// <see cref="RowAlignMode.Bottom"/>, so view titles line up as they always have.
        /// </summary>
        public RowAlignMode RowAlign { get; set; } = RowAlignMode.Bottom;

        /// <summary>
        /// Which way the reading order flows before it wraps (added in version 4). Defaults to
        /// <see cref="FlowMode.Rows"/>, the behaviour before the setting existed.
        /// </summary>
        public FlowMode Flow { get; set; } = FlowMode.Rows;

        /// <summary>
        /// How views of differing width sit inside their column, in column flow (added in version 4).
        /// </summary>
        public ColumnAlignMode ColumnAlign { get; set; } = ColumnAlignMode.Left;
    }
}
