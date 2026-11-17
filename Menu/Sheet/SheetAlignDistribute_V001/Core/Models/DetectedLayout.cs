namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models
{
    /// <summary>
    /// What the sheet's own layout measures, in millimetres: the margins between the title block and
    /// the outermost views, and the gaps the views already sit at. Filled in by
    /// <see cref="Services.MarginDetector"/> and shown as the window's "Detected" labels.
    /// </summary>
    public sealed class DetectedLayout
    {
        /// <summary>True when there was a title block and at least one view to measure between.</summary>
        public bool HasValues { get; init; }

        /// <summary>Title block top edge down to the highest view (mm).</summary>
        public double MarginTopMm { get; init; }

        /// <summary>Title block bottom edge up to the lowest view (mm).</summary>
        public double MarginBottomMm { get; init; }

        /// <summary>Title block left edge across to the leftmost view (mm).</summary>
        public double MarginLeftMm { get; init; }

        /// <summary>
        /// Title block right edge across to the rightmost view (mm). On a sheet whose title strip is
        /// down the right, this is where the views stop, which is normally just inside the strip —
        /// Revit gives no way to ask a title block family where its strip begins.
        /// </summary>
        public double MarginRightMm { get; init; }

        /// <summary>Median gap between two views side by side in a row (mm), or null when no row has two.</summary>
        public double? GapHorizontalMm { get; init; }

        /// <summary>Median gap between two rows (mm), or null when there is only one row.</summary>
        public double? GapVerticalMm { get; init; }
    }
}
