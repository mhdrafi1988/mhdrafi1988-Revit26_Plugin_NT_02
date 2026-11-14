// =======================================================
// File: NamedColorHelper.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001
// New in V026.
// Purpose: Maps a named WPF color (System.Windows.Media.Colors,
//          used for the Color dropdown in the UI) to the
//          equivalent Autodesk.Revit.DB.Color for use in
//          OverrideGraphicSettings.
// Notes:
//   - Kept intentionally small — only the palette actually offered
//     in the Color dropdown (see AppConstants.ColorPalette) needs a
//     mapping. Unknown/unmapped names fall back to Black rather
//     than throwing, so a bad settings.json value degrades gracefully.
// =======================================================

using System.Collections.Generic;
using RevitColor = Autodesk.Revit.DB.Color;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Infrastructure.Helpers
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
