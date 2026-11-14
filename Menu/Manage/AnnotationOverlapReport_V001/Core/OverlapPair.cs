namespace Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Core
{
    /// <summary>Two annotations whose boxes overlap in one view.</summary>
    public sealed class OverlapPair
    {
        /// <summary>The annotation with the smaller element id.</summary>
        public PaperBox A { get; set; }

        /// <summary>The annotation with the larger element id.</summary>
        public PaperBox B { get; set; }

        /// <summary>
        /// How far one box sits inside the other: the smaller of the horizontal and vertical overlap depths,
        /// in millimetres on paper, rounded to 2 decimals.
        /// </summary>
        public double OverlapMm { get; set; }
    }
}
