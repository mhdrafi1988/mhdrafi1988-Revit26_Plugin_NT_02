using Revit26_Plugin_Revised.TrackerId.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Services
{
    /// <summary>
    /// Writes the Power BI table as CSV. The columns and their order are fixed (<see cref="Columns"/>):
    /// Power BI refreshes break when a column moves or is renamed, so new columns may only be added at
    /// the end, with <see cref="SchemaVersion"/> raised.
    /// </summary>
    public static class CsvExportService
    {
        /// <summary>Version written in the Schema_Version column.</summary>
        public const int SchemaVersion = 1;

        /// <summary>Column names, in file order.</summary>
        public static readonly IReadOnlyList<string> Columns = new[]
        {
            "Tracker_ID", "Category", "Family", "Type", "Type_Mark", "Zone", "Level", "Grid",
            "Count", "Length_m", "Area_m2", "Volume_m3",
            "Revit_ElementId", "Revit_UniqueId", "Model", "Export_Date", "Schema_Version", "Model_Code"
        };

        /// <summary>
        /// Writes <paramref name="rows"/> to <paramref name="path"/> (UTF-8 with BOM, so Excel opens it
        /// correctly), replacing the file in one step so Power BI never reads a half-written file.
        /// When <paramref name="datedCopy"/> is set, also writes "name_yyyy-MM-dd.csv" next to it.
        /// <paramref name="modelCode"/> fills the Model_Code column. Returns the paths written.
        /// Throws <see cref="IOException"/> when the file is open elsewhere.
        /// </summary>
        public static IReadOnlyList<string> Write(string path, IEnumerable<ExportRow> rows, string modelName,
            string modelCode, DateTime exportTime, bool datedCopy)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a file to export to.", nameof(path));

            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            string text = Build(rows, modelName, modelCode, exportTime);
            var written = new List<string> { WriteAtomically(path, text) };

            if (datedCopy)
            {
                string dated = Path.Combine(folder ?? string.Empty,
                    $"{Path.GetFileNameWithoutExtension(path)}_{exportTime:yyyy-MM-dd}{Path.GetExtension(path)}");
                written.Add(WriteAtomically(dated, text));
            }
            return written;
        }

        /// <summary>The CSV text for <paramref name="rows"/>, header included.</summary>
        public static string Build(IEnumerable<ExportRow> rows, string modelName, string modelCode, DateTime exportTime)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Columns));

            string date = exportTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            foreach (var r in rows ?? Enumerable.Empty<ExportRow>())
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Quote(r.TrackerId), Quote(r.Category), Quote(r.Family), Quote(r.Type), Quote(r.TypeMark),
                    Quote(r.Zone), Quote(r.Level), Quote(r.Grid),
                    "1", Number(r.LengthM), Number(r.AreaM2), Number(r.VolumeM3),
                    r.ElementId.ToString(CultureInfo.InvariantCulture), Quote(r.UniqueId), Quote(modelName),
                    date, SchemaVersion.ToString(CultureInfo.InvariantCulture), Quote(modelCode)
                }));
            }
            return sb.ToString();
        }

        private static string WriteAtomically(string path, string text)
        {
            string full = Path.GetFullPath(path);
            string temp = full + ".tmp";
            try
            {
                File.WriteAllText(temp, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                File.Move(temp, full, overwrite: true);
                return full;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
                throw new IOException($"Could not write '{full}'. Close it in Excel or Power BI and try again. ({ex.Message})", ex);
            }
        }

        private static string Number(double? value) =>
            value.HasValue ? Math.Round(value.Value, 3).ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;

        private static string Quote(string value)
        {
            value ??= string.Empty;
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }
    }
}
