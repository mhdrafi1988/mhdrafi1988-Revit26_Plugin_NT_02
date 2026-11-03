using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Default slot colours: light tints so detail lines stay readable on top. Slot n uses colour n mod 12.
    /// These are the colours of fill types the tool creates; picked existing types keep their own colours.
    /// </summary>
    public static class HatchPalette
    {
        public static IReadOnlyList<HatchColor> Colors { get; } = new[]
        {
            new HatchColor(207, 232, 207), // soft green
            new HatchColor(246, 205, 160), // light orange
            new HatchColor(176, 208, 236), // light blue
            new HatchColor(214, 196, 236), // lavender
            new HatchColor(244, 228, 150), // pale yellow
            new HatchColor(240, 184, 190), // rose
            new HatchColor(170, 222, 214), // aqua
            new HatchColor(220, 210, 190), // sand
            new HatchColor(190, 226, 160), // lime
            new HatchColor(200, 200, 220), // grey blue
            new HatchColor(250, 200, 225), // pink
            new HatchColor(205, 225, 245)  // ice
        };

        public static HatchColor ForSlot(int slot) => Colors[((slot % Colors.Count) + Colors.Count) % Colors.Count];
    }
}
