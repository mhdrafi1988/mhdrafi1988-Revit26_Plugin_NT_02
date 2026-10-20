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
        public const int SchemaVersion = 2;

        /// <summary>Column names, in file order.</summary>
        public static readonly IReadOnlyList<string> Columns = new[]
        {
            "Tracker_ID", "Category", "Family", "Type", "Type_Mark", "Zone", "Level", "Grid",
            "Count", "Length_m", "Area_m2", "Volume_m3",
            "Revit_ElementId", "Revit_UniqueId", "Model", "Export_Date", "Schema_Version", "Model_Code",
            // Schema 2 (Tracker ID 1.1.0): status read back into Revit.
            "Status", "Progress_Pct", "Status_Date"
        };

        /// <summary>
        /// The values of one row in <see cref="Columns"/> order: text as string, numbers as double or long,
        /// empty quantities as null. Shared by the CSV and Excel exports so both always match.
        /// </summary>
        public static object[] Values(ExportRow r, string modelName, string modelCode, DateTime exportTime) => new object[]
        {
            r.TrackerId, r.Category, r.Family, r.Type, r.TypeMark, r.Zone, r.Level, r.Grid,
            1L, Round(r.LengthM), Round(r.AreaM2), Round(r.VolumeM3),
            r.ElementId, r.UniqueId, modelName ?? string.Empty,
            exportTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), (long)SchemaVersion, modelCode ?? string.Empty,
            r.Status, r.ProgressPct, r.StatusDate
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

        /// <summary>
        /// Writes the health check findings to <paramref name="path"/> (UTF-8 CSV, one row per finding) and
        /// returns the full path. Throws <see cref="IOException"/> when the file is open elsewhere.
        /// </summary>
        public static string WriteReport(string path, IEnumerable<HealthIssue> issues, Func<HealthIssueKind, string> kindLabel)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a file for the report.", nameof(path));
            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            var sb = new StringBuilder();
            sb.AppendLine("Problem,Element_ID,Tracker_ID,Category,Type,Detail");
            foreach (var i in issues ?? Enumerable.Empty<HealthIssue>())
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Quote(kindLabel?.Invoke(i.Kind) ?? i.Kind.ToString()),
                    i.ElementId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    Quote(i.TrackerId), Quote(i.Category), Quote(i.Type), Quote(i.Detail)
                }));
            }
            return WriteAtomically(path, sb.ToString());
        }

        /// <summary>The CSV text for <paramref name="rows"/>, header included.</summary>
        public static string Build(IEnumerable<ExportRow> rows, string modelName, string modelCode, DateTime exportTime)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Columns));

            foreach (var r in rows ?? Enumerable.Empty<ExportRow>())
                sb.AppendLine(string.Join(",", Values(r, modelName, modelCode, exportTime).Select(Format)));
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

        private static double? Round(double? value) => value.HasValue ? Math.Round(value.Value, 3) : null;

        private static string Format(object value) => value switch
        {
            null => string.Empty,
            double d => d.ToString("0.###", CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            _ => Quote(value.ToString())
        };

        private static string Quote(string value)
        {
            value ??= string.Empty;
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }
    }
}
