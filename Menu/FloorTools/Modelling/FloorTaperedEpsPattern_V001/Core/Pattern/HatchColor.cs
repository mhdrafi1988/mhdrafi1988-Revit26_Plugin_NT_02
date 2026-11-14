using System.Globalization;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>An RGB colour used for a hatch slot (shared by the preview, the legend and created fill types).</summary>
    public readonly struct HatchColor
    {
        public HatchColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public string Hex => string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", R, G, B);
    }
}
