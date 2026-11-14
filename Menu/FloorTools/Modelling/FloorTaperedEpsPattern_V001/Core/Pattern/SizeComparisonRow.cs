namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Result of laying the floor with one board size and the current settings.</summary>
    public sealed class SizeComparisonRow
    {
        public BoardSize Size { get; init; }
        public int Pieces { get; init; }
        public int Full { get; init; }

        /// <summary>Share of the floor area covered by full boards (0–1).</summary>
        public double FullAreaShare { get; init; }
        public int HipCuts { get; init; }
        public int StraightCuts { get; init; }
        public int Codes { get; init; }
        public int Slivers { get; init; }
        public bool IsBest { get; init; }
        public string Note { get; init; } = string.Empty;
    }
}
