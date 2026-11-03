namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Line count and total length of one layer (summary grid row).</summary>
    public sealed class LayerStats
    {
        public LayerStats(PatternLayer layer, int lineCount, double totalLengthM)
        {
            Layer = layer;
            LineCount = lineCount;
            TotalLengthM = totalLengthM;
        }

        public PatternLayer Layer { get; }
        public int LineCount { get; }
        public double TotalLengthM { get; }
    }
}
