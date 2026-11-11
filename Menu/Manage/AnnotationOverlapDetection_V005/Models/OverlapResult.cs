namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V005.Models
{
    /// <summary>
    /// One overlapping pair, as displayed in the results DataGrid.
    /// </summary>
    public class OverlapResult
    {
        public long ElementId1 { get; set; }
        public long ElementId2 { get; set; }
        public string AnnotationType { get; set; }

        public double X { get; set; }   // mm, 4 decimals in UI
        public double Y { get; set; }   // mm, 4 decimals in UI

        public string GridAligned { get; set; } // "Yes" / "No"

        public double VerticalDistance { get; set; }   // mm, 2 decimals
        public double HorizontalDistance { get; set; } // mm, 2 decimals

        /// <summary>
        /// Result of the last Resolve run for this pair (V005), e.g. "Skipped: 412893 pinned",
        /// or empty before the first run. Appended last so the column order of V004 is unchanged.
        /// </summary>
        public string Status { get; set; } = string.Empty;
    }
}
