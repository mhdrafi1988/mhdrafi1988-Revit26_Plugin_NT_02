using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Services
{
    /// <summary>
    /// One row of the run manifest.
    /// </summary>
    public class ExportManifestRow
    {
        /// <summary>CAD type name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Revit element id of the CAD instance.</summary>
        public long ElementId { get; set; }

        /// <summary>"Link" or "Import".</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>Plan / Section / Drafting / No Views.</summary>
        public string ViewType { get; set; } = string.Empty;

        /// <summary>Host view name.</summary>
        public string HostView { get; set; } = string.Empty;

        /// <summary>Sheet label, or "Not on Sheet".</summary>
        public string Sheet { get; set; } = string.Empty;

        /// <summary>Layers the instance brings into the model; empty when it could not be read.</summary>
        public string Layers { get; set; } = string.Empty;

        /// <summary>Link's source file, or a statement for an import.</summary>
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>Full path written (or that would have been written).</summary>
        public string Destination { get; set; } = string.Empty;

        /// <summary>"copied source file" / "re-exported from view".</summary>
        public string Route { get; set; } = string.Empty;

        /// <summary>Exported / Skipped / Failed.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Skip or failure reason; empty on success.</summary>
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Writes one CSV per export run at the output root. NEW in V004.
    /// <para>
    /// V003 wrote a four-line sidecar .txt beside every DWG, which is fine for one file and useless
    /// for thirty — there is no way to see at a glance which rows failed and why. The manifest is
    /// the run as a table: one row per DWG, with the source path, the destination, the route that
    /// produced it, the status and the reason. It is what you attach for a consultant, or open to
    /// chase the three rows that didn't come out. The sidecars still work and are still optional.
    /// </para>
    /// <para>No Revit API dependency — plain .NET, as <c>Core/Services</c> is meant to be.</para>
    /// </summary>
    public static class ExportManifestService
    {
        /// <summary>Column order, and the header row.</summary>
        private static readonly string[] Header =
        {
            "Name", "Element ID", "Source", "View Type", "Host View", "Sheet", "Layers",
            "Source Path", "Destination", "Route", "Status", "Reason"
        };

        /// <summary>
        /// Writes the manifest and returns its full path, or null when <paramref name="rows"/> is
        /// empty. Throws nothing the caller has to handle beyond IO — the caller logs a failure as a
        /// Warning, because a missing manifest must never make a successful export look failed.
        /// </summary>
        /// <param name="outputRoot">The run's output folder.</param>
        /// <param name="rows">One row per attempted export, in run order.</param>
        /// <param name="runHeader">
        /// One line describing the run's options (from <c>DwgExportOptionsFactory.Describe</c>),
        /// written as a comment above the table so the file explains itself months later.
        /// </param>
        /// <param name="timestamp">Run time, used in the file name.</param>
        public static string Write(string outputRoot, IList<ExportManifestRow> rows, string runHeader, DateTime timestamp)
        {
            if (string.IsNullOrWhiteSpace(outputRoot)) return null;
            if (rows == null || rows.Count == 0) return null;

            Directory.CreateDirectory(outputRoot);

            string fileName = $"_export-manifest_{timestamp:yyyy-MM-dd_HHmm}.csv";
            string path = Path.Combine(outputRoot, fileName);

            // A second run in the same minute gets its own file rather than overwriting the first.
            int suffix = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(outputRoot,
                    $"_export-manifest_{timestamp:yyyy-MM-dd_HHmm}_{suffix}.csv");
                suffix++;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"# Export DWG to Folder — run {timestamp:yyyy-MM-dd HH:mm}");
            if (!string.IsNullOrWhiteSpace(runHeader))
                sb.AppendLine($"# {runHeader}");
            sb.AppendLine($"# {rows.Count} row(s)");

            sb.AppendLine(string.Join(",", Array.ConvertAll(Header, Quote)));

            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Quote(row.Name),
                    Quote(row.ElementId.ToString()),
                    Quote(row.Source),
                    Quote(row.ViewType),
                    Quote(row.HostView),
                    Quote(row.Sheet),
                    Quote(row.Layers),
                    Quote(row.SourcePath),
                    Quote(row.Destination),
                    Quote(row.Route),
                    Quote(row.Status),
                    Quote(row.Reason)
                }));
            }

            // UTF-8 with BOM: Excel reads a BOM-less UTF-8 CSV as ANSI and mangles any non-ASCII
            // sheet name or path.
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return path;
        }

        /// <summary>
        /// CSV-quotes one field. Always quoted, so a path with a comma, a quote or a line break in
        /// it can't shift the columns.
        /// </summary>
        private static string Quote(string value)
            => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }
}
