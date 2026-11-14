namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Laying patterns offered for square boards. Each is a preset of row start + joint pattern
    /// (see <see cref="Pattern2DSettings.Normalized"/>).
    /// </summary>
    public enum SquarePattern
    {
        /// <summary>Rows start at the upper hip point, joints carried straight up the slope.</summary>
        StackFromHips,

        /// <summary>Rows start at the upper hip point, every second row shifted half a board.</summary>
        HalfBondFromHips,

        /// <summary>Each side starts from its centre line (D Auto), joints carried straight up.</summary>
        CentreOutStack,

        /// <summary>Every row starts at its own upper hip point: the hip piece is the same half-square triangle in every row.</summary>
        HipDiagonalPairs
    }
}
