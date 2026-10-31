// =======================================================
// File: NamedColors.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models
// Replaces V030's Infrastructure/Helpers/NamedColorHelper (which returned
// a Revit Color from Infrastructure and was used by Core). Pure RGB here;
// Infrastructure builds the Revit Color, the UI builds the WPF brush.
// Unknown names fall back to Black so a bad settings value never throws.
// =======================================================

using System.Collections.Generic;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models
{
    public static class NamedColors
    {
        private static readonly Dictionary<string, (byte R, byte G, byte B)> Map =
            new Dictionary<string, (byte, byte, byte)>
            {
                ["Black"]   = (0, 0, 0),
                ["Blue"]    = (0, 0, 255),
                ["Green"]   = (0, 128, 0),
                ["Red"]     = (255, 0, 0),
                ["Orange"]  = (255, 165, 0),
                ["Cyan"]    = (0, 255, 255),
                ["Magenta"] = (255, 0, 255),
            };

        /// <summary>Ordered list offered in the Colour dropdown.</summary>
        public static IReadOnlyList<string> PaletteNames { get; } =
            new List<string> { "Black", "Blue", "Green", "Red", "Orange", "Cyan", "Magenta" };

        public static bool IsKnown(string name) => !string.IsNullOrWhiteSpace(name) && Map.ContainsKey(name);

        public static (byte R, byte G, byte B) ToRgb(string name) =>
            !string.IsNullOrWhiteSpace(name) && Map.TryGetValue(name, out var c) ? c : Map["Black"];
    }
}
