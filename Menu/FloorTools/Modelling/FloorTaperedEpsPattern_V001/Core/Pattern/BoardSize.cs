using System.Globalization;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>A board size to compare: length along the row × width up the slope (m).</summary>
    public readonly struct BoardSize
    {
        public BoardSize(double lengthM, double widthM)
        {
            LengthM = lengthM;
            WidthM = widthM;
        }

        public double LengthM { get; }
        public double WidthM { get; }
        public bool IsSquare => System.Math.Abs(LengthM - WidthM) < 1e-9;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0:0.00} × {1:0.00} m", LengthM, WidthM);
    }
}
