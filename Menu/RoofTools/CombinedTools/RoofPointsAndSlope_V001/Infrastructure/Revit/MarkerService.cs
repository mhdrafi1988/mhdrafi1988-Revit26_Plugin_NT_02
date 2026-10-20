// =======================================================
// File: MarkerService.cs
// Location: Infrastructure/Revit/
// Shows temporary markers in a view with Revit's TemporaryGraphicsManager
// (in-canvas controls). They are not elements: nothing is saved in the
// model or added to Undo. Each marker is a small coloured dot image (.bmp,
// the only format the API takes), written once to %TEMP%.
// Call from Revit's API context (the ExternalEvent handler).
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Infrastructure.Revit
{
    /// <summary>Adds and clears this tool's temporary markers.</summary>
    public static class MarkerService
    {
        /// <summary>More markers than this are not shown (the view would slow down).</summary>
        public const int MaxMarkers = 3000;

        private const int ImageSize = 11;

        // Controls this tool added, per document, so Clear never touches other tools' graphics.
        private static readonly Dictionary<Document, List<int>> Shown = new();

        /// <summary>Replaces this tool's markers in <paramref name="view"/> with <paramref name="markers"/> at height <paramref name="z"/>.</summary>
        /// <returns>The number of markers shown.</returns>
        public static int Show(Document doc, View view, IReadOnlyList<MarkerPoint> markers, double z)
        {
            Clear(doc);
            if (doc == null || view == null || markers == null || markers.Count == 0) return 0;

            TemporaryGraphicsManager manager = TemporaryGraphicsManager.GetTemporaryGraphicsManager(doc);
            var ids = new List<int>();
            int count = Math.Min(markers.Count, MaxMarkers);
            for (int i = 0; i < count; i++)
            {
                MarkerPoint m = markers[i];
                var data = new InCanvasControlData(ImagePath(m.Kind), new XYZ(m.Position.X, m.Position.Y, z));
                ids.Add(manager.AddControl(data, view.Id));
            }

            Shown[doc] = ids;
            return count;
        }

        /// <summary>Removes this tool's markers from <paramref name="doc"/> (no-op when there are none).</summary>
        public static void Clear(Document doc)
        {
            if (doc == null || !Shown.TryGetValue(doc, out List<int> ids)) return;
            Shown.Remove(doc);
            if (!doc.IsValidObject) return;

            TemporaryGraphicsManager manager = TemporaryGraphicsManager.GetTemporaryGraphicsManager(doc);
            foreach (int id in ids)
            {
                try { manager.RemoveControl(id); }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { /* already gone (view closed) */ }
            }
        }

        /// <summary>Colour of each kind (R, G, B).</summary>
        public static (byte R, byte G, byte B) Colour(MarkerKind kind) => kind switch
        {
            MarkerKind.OpeningPoint => (37, 99, 235),
            MarkerKind.Perpendicular => (147, 51, 234),
            MarkerKind.MidQuarter => (22, 163, 74),
            MarkerKind.GapFill => (234, 88, 12),
            MarkerKind.Ridge => (220, 38, 38),
            _ => (234, 179, 8)
        };

        private static string ImagePath(MarkerKind kind)
        {
            string folder = Path.Combine(Path.GetTempPath(), "Revit26_Plugin", "RoofPointsAndSlope");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"marker_{kind}_v1.bmp");
            if (!File.Exists(path))
                File.WriteAllBytes(path, DotBitmap(Colour(kind), ring: kind == MarkerKind.Opening));
            return path;
        }

        /// <summary>A 24-bit BMP of a filled dot (or a ring) on white, which Revit draws as the marker.</summary>
        internal static byte[] DotBitmap((byte R, byte G, byte B) c, bool ring)
        {
            const int n = ImageSize;
            int rowBytes = (n * 3 + 3) & ~3;
            int pixelBytes = rowBytes * n;
            var bmp = new byte[54 + pixelBytes];

            void Int(int at, int v) => BitConverter.GetBytes(v).CopyTo(bmp, at);
            bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
            Int(2, bmp.Length); Int(10, 54); Int(14, 40); Int(18, n); Int(22, n);
            bmp[26] = 1; bmp[28] = 24; Int(34, pixelBytes);

            double r = (n - 1) / 2.0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    double d = Math.Sqrt((x - r) * (x - r) + (y - r) * (y - r));
                    bool on = ring ? d <= r + 0.3 && d >= r - 1.6 : d <= r + 0.3;
                    bool edge = !ring && d > r - 1.2 && d <= r + 0.3;
                    int at = 54 + y * rowBytes + x * 3;
                    (byte R, byte G, byte B) px = !on ? ((byte)255, (byte)255, (byte)255)
                        : edge ? ((byte)255, (byte)255, (byte)255) : c;
                    if (on && edge) px = ((byte)(c.R / 2), (byte)(c.G / 2), (byte)(c.B / 2));
                    bmp[at] = px.B; bmp[at + 1] = px.G; bmp[at + 2] = px.R;
                }

            return bmp;
        }
    }
}
