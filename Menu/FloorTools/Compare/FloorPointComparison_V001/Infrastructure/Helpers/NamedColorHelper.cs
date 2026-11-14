using System.Collections.Generic;
using RevitColor = Autodesk.Revit.DB.Color;

namespace Revit26_Plugin_Revised.FloorPointComparison.V001.Infrastructure.Helpers
{
    /// <summary>Maps a named WPF color (offered in the UI dropdowns) to the equivalent Revit color.</summary>
    public static class NamedColorHelper
    {
        private static readonly Dictionary<string, RevitColor> Map =
            new Dictionary<string, RevitColor>
            {
                ["Black"] = new RevitColor(0, 0, 0),
                ["Blue"] = new RevitColor(0, 0, 255),
                ["Green"] = new RevitColor(0, 128, 0),
                ["Red"] = new RevitColor(255, 0, 0),
                ["Orange"] = new RevitColor(255, 165, 0),
                ["Cyan"] = new RevitColor(0, 255, 255),
                ["Magenta"] = new RevitColor(255, 0, 255),
            };

        public static IReadOnlyList<string> PaletteNames { get; } =
            new List<string> { "Black", "Blue", "Green", "Red", "Orange", "Cyan", "Magenta" };

        public static RevitColor ToRevitColor(string colorName)
        {
            if (!string.IsNullOrWhiteSpace(colorName) && Map.TryGetValue(colorName, out var c))
                return c;
            return Map["Black"];
        }
    }
}
