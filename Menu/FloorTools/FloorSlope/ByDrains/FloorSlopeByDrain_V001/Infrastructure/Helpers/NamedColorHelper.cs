// File: NamedColorHelper.cs
// Location: Infrastructure/Helpers/
// Ported from AutoSlopeByPoint V028 — maps a named WPF color (offered in the
// Color dropdown) to the equivalent Autodesk.Revit.DB.Color for use in
// OverrideGraphicSettings. Unknown/unmapped names fall back to Black.

using System.Collections.Generic;
using RevitColor = Autodesk.Revit.DB.Color;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Infrastructure.Helpers
{
    public static class NamedColorHelper
    {
        private static readonly Dictionary<string, RevitColor> Map =
            new Dictionary<string, RevitColor>
            {
                ["Black"]   = new RevitColor(0, 0, 0),
                ["Blue"]    = new RevitColor(0, 0, 255),
                ["Green"]   = new RevitColor(0, 128, 0),
                ["Red"]     = new RevitColor(255, 0, 0),
                ["Orange"]  = new RevitColor(255, 165, 0),
                ["Cyan"]    = new RevitColor(0, 255, 255),
                ["Magenta"] = new RevitColor(255, 0, 255),
            };

        /// <summary>Ordered list of color names offered in the UI dropdown.</summary>
        public static IReadOnlyList<string> PaletteNames { get; } =
            new List<string> { "Black", "Blue", "Green", "Red", "Orange", "Cyan", "Magenta" };

        /// <summary>Returns the mapped Revit color, or Black if the name is unrecognized.</summary>
        public static RevitColor ToRevitColor(string colorName)
        {
            if (!string.IsNullOrWhiteSpace(colorName) && Map.TryGetValue(colorName, out var c))
                return c;
            return Map["Black"];
        }
    }
}
