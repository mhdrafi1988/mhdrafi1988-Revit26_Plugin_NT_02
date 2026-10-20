using Revit26_Plugin_Revised.TrackerId.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Services
{
    /// <summary>The status → colour table used to colour views and fill the template's drop-down.</summary>
    public static class StatusColours
    {
        /// <summary>Colour of elements that have no status.</summary>
        public const string NoStatusColor = "#D9D9D9";

        // Used in turn for statuses the table does not list yet.
        private static readonly string[] AutoPalette =
        {
            "#8E44AD", "#16A085", "#D35400", "#2C3E50", "#C0392B", "#27AE60", "#7F8C8D", "#F39C12", "#1ABC9C", "#E84393"
        };

        /// <summary>The table the tool starts with.</summary>
        public static List<StatusColour> Defaults() => new()
        {
            new() { Status = "Not started", Color = "#9E9E9E" },
            new() { Status = "Ordered", Color = "#2D6CDF" },
            new() { Status = "Delivered", Color = "#E0A030" },
            new() { Status = "Installed", Color = "#2E7D4F" },
            new() { Status = "Issue", Color = "#B23A35" }
        };

        /// <summary>
        /// Cleans <paramref name="table"/> (drops blanks and repeats, compared without regard to case;
        /// fixes bad colours) and appends every status in <paramref name="statusesInUse"/> it does not
        /// list yet, with an automatic colour not already taken where possible.
        /// </summary>
        public static List<StatusColour> Complete(IEnumerable<StatusColour> table, IEnumerable<string> statusesInUse)
        {
            var result = new List<StatusColour>();
            foreach (var entry in table ?? Enumerable.Empty<StatusColour>())
            {
                string status = entry?.Status?.Trim();
                if (string.IsNullOrEmpty(status) || result.Any(r => Same(r.Status, status))) continue;
                result.Add(new StatusColour { Status = status, Color = TryParse(entry.Color, out _, out _, out _) ? Normalise(entry.Color) : NextAuto(result) });
            }

            foreach (string raw in statusesInUse ?? Enumerable.Empty<string>())
            {
                string status = raw?.Trim();
                if (string.IsNullOrEmpty(status) || result.Any(r => Same(r.Status, status))) continue;
                result.Add(new StatusColour { Status = status, Color = NextAuto(result) });
            }
            return result;
        }

        /// <summary>Reads "#RRGGBB" (the # is optional). False for anything else.</summary>
        public static bool TryParse(string hex, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            string h = hex?.Trim().TrimStart('#');
            if (h == null || h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
                return false;
            r = (byte)(v >> 16);
            g = (byte)((v >> 8) & 0xFF);
            b = (byte)(v & 0xFF);
            return true;
        }

        private static string Normalise(string hex) => "#" + hex.Trim().TrimStart('#').ToUpperInvariant();

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string NextAuto(List<StatusColour> taken)
        {
            foreach (string c in AutoPalette)
                if (taken.All(t => !Same(t.Color, c))) return c;
            return AutoPalette[taken.Count % AutoPalette.Length];
        }
    }
}
