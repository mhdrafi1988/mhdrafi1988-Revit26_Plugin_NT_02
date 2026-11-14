namespace Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Core
{
    /// <summary>One overlapping pair as shown in the results grid and written to Excel.</summary>
    public sealed class ReportRow
    {
        /// <summary>Element id value of the view the pair was found in (used by Zoom to pair).</summary>
        public long ViewId { get; set; }

        /// <summary>Sheet number the view is placed on, or the sheet's own number; empty when not on a sheet.</summary>
        public string Sheet { get; set; }

        /// <summary>View name (or sheet name for annotations placed on the sheet itself).</summary>
        public string View { get; set; }

        /// <summary>View scale as text, for example "1:100".</summary>
        public string Scale { get; set; }

        /// <summary>Annotation type of the first element.</summary>
        public string TypeA { get; set; }

        /// <summary>Text of the first element.</summary>
        public string TextA { get; set; }

        /// <summary>Element id of the first element.</summary>
        public long IdA { get; set; }

        /// <summary>Annotation type of the second element.</summary>
        public string TypeB { get; set; }

        /// <summary>Text of the second element.</summary>
        public string TextB { get; set; }

        /// <summary>Element id of the second element.</summary>
        public long IdB { get; set; }

        /// <summary>Overlap depth in millimetres on paper.</summary>
        public double OverlapMm { get; set; }

        /// <summary>Grid text for the first element: "Type · Text".</summary>
        public string LabelA => string.IsNullOrWhiteSpace(TextA) ? TypeA : TypeA + " · " + TextA;

        /// <summary>Grid text for the second element: "Type · Text".</summary>
        public string LabelB => string.IsNullOrWhiteSpace(TextB) ? TypeB : TypeB + " · " + TextB;

        /// <summary>Group header in the grid: view, scale and sheet.</summary>
        public string ViewGroup =>
            View + " · " + Scale + (string.IsNullOrWhiteSpace(Sheet) ? "" : " · on sheet " + Sheet);
    }
}
