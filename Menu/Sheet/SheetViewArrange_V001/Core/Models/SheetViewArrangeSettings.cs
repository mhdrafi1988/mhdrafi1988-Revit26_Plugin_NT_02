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
        /// Current settings-file format version. 2 added <see cref="GroupBy"/>; version-1 files
        /// load unchanged (the new field takes its default).
        /// </summary>
        public const int CurrentVersion = 2;

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
    }
}
