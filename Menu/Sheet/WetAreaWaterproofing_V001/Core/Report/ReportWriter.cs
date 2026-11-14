using ClosedXML.Excel;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Run;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Report
{
    /// <summary>
    /// Writes a <see cref="RunReport"/> to an .xlsx workbook. Column names and order are fixed;
    /// new columns are only ever added at the end.
    /// </summary>
    public static class ReportWriter
    {
        /// <summary>Report format version, written to the Summary sheet.</summary>
        public const int FormatVersion = 1;

        /// <summary>Rooms sheet columns, in order.</summary>
        public static IReadOnlyList<string> RoomColumns { get; } = new[]
        {
            "Room ElementId", "Source", "Level", "Number", "Name", "Room area m²", "Matched keyword", "Slab ElementId",
            "Slab top mm", "Roof ElementId", "Roof area m²", "Area difference %", "Boundary result", "Status", "Reason",
            "Phase", "Workset", "Group", "Scale", "Run ID"
        };

        /// <summary>Boundary repair sheet columns, in order.</summary>
        public static IReadOnlyList<string> RepairColumns { get; } = new[]
        {
            "Room ElementId", "Number", "Name", "Method", "Method name", "Result", "Detail", "X mm", "Y mm",
            "Room area m²", "Outline area m²"
        };

        /// <summary>Issues sheet columns, in order.</summary>
        public static IReadOnlyList<string> IssueColumns { get; } = new[]
        {
            "Severity", "Step", "Room number", "Message", "Suggested fix", "Room key"
        };

        private static readonly XLColor Green = XLColor.FromHtml("#E6F4EC");
        private static readonly XLColor Yellow = XLColor.FromHtml("#FDF1E1");
        private static readonly XLColor Red = XLColor.FromHtml("#FBE8E7");

        /// <summary>WP_Report_&lt;Model&gt;_&lt;yyyyMMdd_HHmm&gt;.xlsx, with characters Windows rejects replaced by '_'.</summary>
        public static string FileName(string modelName, DateTime started)
        {
            var bad = new HashSet<char>(Path.GetInvalidFileNameChars()) { ' ', ':', '/', '\\', '*', '?', '"', '<', '>', '|' };
            string model = new string((modelName ?? string.Empty).Trim().Select(c => bad.Contains(c) ? '_' : c).ToArray());
            if (model.Length == 0) model = "Model";
            return $"WP_Report_{model}_{started.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture)}.xlsx";
        }

        /// <summary>
        /// Saves the workbook in <paramref name="folder"/> (created when missing) and returns its
        /// path. When the file is open in Excel the report is saved beside it with a _1 … _9 suffix.
        /// </summary>
        /// <exception cref="IOException">The folder cannot be written to at all.</exception>
        public static string Write(RunReport report, string folder)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("No report folder.", nameof(folder));
            Directory.CreateDirectory(folder);

            string name = FileName(report.Summary.Model, report.Summary.Started);
            string stem = Path.GetFileNameWithoutExtension(name);
            using XLWorkbook workbook = BuildWorkbook(report);

            IOException last = null;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                string path = Path.Combine(folder, attempt == 0 ? name : $"{stem}_{attempt}.xlsx");
                try
                {
                    // A file open in Excel must not be replaced: probe for the lock first.
                    if (File.Exists(path))
                        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    workbook.SaveAs(path);
                    return path;
                }
                catch (IOException ex) { last = ex; }
                catch (UnauthorizedAccessException ex) { last = new IOException(ex.Message, ex); }
            }
            throw last ?? new IOException("The report could not be saved.");
        }

        private static XLWorkbook BuildWorkbook(RunReport report)
        {
            var workbook = new XLWorkbook();
            ReportSummary s = report.Summary;

            IXLWorksheet summary = workbook.AddWorksheet("Summary");
            var lines = new List<(string Name, XLCellValue Value)>
            {
                ("Run ID", s.RunId ?? string.Empty),
                ("Date / time", s.Started.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                ("User", s.User ?? string.Empty),
                ("Model", s.Model ?? string.Empty),
                ("Plugin version", s.PluginVersion ?? string.Empty),
                ("Tool version", s.ToolVersion ?? string.Empty),
                ("Report format", FormatVersion),
                ("Run time (s)", Math.Round(s.DurationSeconds, 1)),
                ("Rooms ticked", s.RoomsTicked),
                ("Roofs created", s.RoofsCreated),
                ("Roofs replaced", s.RoofsReplaced),
                ("Rooms skipped", s.Skipped),
                ("Warnings", s.Warnings),
                ("Errors", s.Errors)
            };
            lines.AddRange(s.Settings.Select(p => (p.Key, (XLCellValue)(p.Value ?? string.Empty))));
            summary.Cell(1, 1).Value = "Item";
            summary.Cell(1, 2).Value = "Value";
            for (int i = 0; i < lines.Count; i++)
            {
                summary.Cell(i + 2, 1).Value = lines[i].Name;
                summary.Cell(i + 2, 2).Value = lines[i].Value;
            }
            FinishSheet(summary, 2);

            IXLWorksheet rooms = workbook.AddWorksheet("Rooms");
            WriteHeader(rooms, RoomColumns);
            int statusColumn = RoomColumns.ToList().IndexOf("Status") + 1;
            for (int i = 0; i < report.Rooms.Count; i++)
            {
                ReportRoom r = report.Rooms[i];
                int row = i + 2, col = 1;
                rooms.Cell(row, col++).Value = r.RoomId;
                rooms.Cell(row, col++).Value = r.Source ?? string.Empty;
                rooms.Cell(row, col++).Value = r.Level ?? string.Empty;
                rooms.Cell(row, col++).Value = r.Number ?? string.Empty;
                rooms.Cell(row, col++).Value = r.Name ?? string.Empty;
                rooms.Cell(row, col++).Value = Math.Round(r.RoomAreaM2, 3);
                rooms.Cell(row, col++).Value = r.Matched ?? string.Empty;
                SetId(rooms.Cell(row, col++), r.SlabId);
                SetNumber(rooms.Cell(row, col++), r.SlabTopMm, 1);
                SetId(rooms.Cell(row, col++), r.RoofId);
                SetNumber(rooms.Cell(row, col++), r.RoofAreaM2, 3);
                SetNumber(rooms.Cell(row, col++), r.AreaDiffPct, 2);
                rooms.Cell(row, col++).Value = r.BoundaryResult ?? string.Empty;
                rooms.Cell(row, col++).Value = r.Status ?? string.Empty;
                rooms.Cell(row, col++).Value = r.Reason ?? string.Empty;
                rooms.Cell(row, col++).Value = r.Phase ?? string.Empty;
                rooms.Cell(row, col++).Value = r.Workset ?? string.Empty;
                rooms.Cell(row, col++).Value = r.Group ?? string.Empty;
                rooms.Cell(row, col++).Value = r.Scale ?? string.Empty;
                rooms.Cell(row, col).Value = r.RoofId != 0 ? s.RunId ?? string.Empty : string.Empty;

                XLColor fill = r.Status switch
                {
                    "Created" or "Replaced" => r.HasWarning ? Yellow : Green,
                    "Skipped" or "Failed" => Red,
                    _ => null
                };
                if (fill != null) rooms.Cell(row, statusColumn).Style.Fill.BackgroundColor = fill;
            }
            FinishSheet(rooms, RoomColumns.Count);

            IXLWorksheet repairs = workbook.AddWorksheet("Boundary repair");
            WriteHeader(repairs, RepairColumns);
            for (int i = 0; i < report.Repairs.Count; i++)
            {
                ReportRepair r = report.Repairs[i];
                int row = i + 2, col = 1;
                repairs.Cell(row, col++).Value = r.RoomId;
                repairs.Cell(row, col++).Value = r.Number ?? string.Empty;
                repairs.Cell(row, col++).Value = r.Name ?? string.Empty;
                repairs.Cell(row, col++).Value = r.Method;
                repairs.Cell(row, col++).Value = r.MethodName ?? string.Empty;
                IXLCell result = repairs.Cell(row, col++);
                result.Value = r.Result ?? string.Empty;
                result.Style.Fill.BackgroundColor = r.Result == "Passed" ? Yellow : Red;
                repairs.Cell(row, col++).Value = r.Detail ?? string.Empty;
                SetNumber(repairs.Cell(row, col++), r.X, 1);
                SetNumber(repairs.Cell(row, col++), r.Y, 1);
                repairs.Cell(row, col++).Value = Math.Round(r.RoomAreaM2, 3);
                repairs.Cell(row, col).Value = Math.Round(r.OutlineAreaM2, 3);
            }
            FinishSheet(repairs, RepairColumns.Count);

            IXLWorksheet issues = workbook.AddWorksheet("Issues");
            WriteHeader(issues, IssueColumns);
            for (int i = 0; i < report.Issues.Count; i++)
            {
                Issue issue = report.Issues[i];
                int row = i + 2;
                IXLCell severity = issues.Cell(row, 1);
                severity.Value = issue.Severity ?? string.Empty;
                XLColor fill = issue.Severity == "Error" ? Red : issue.Severity == "Warning" ? Yellow : null;
                if (fill != null) severity.Style.Fill.BackgroundColor = fill;
                issues.Cell(row, 2).Value = issue.Step ?? string.Empty;
                issues.Cell(row, 3).Value = issue.Number ?? string.Empty;
                issues.Cell(row, 4).Value = issue.Message ?? string.Empty;
                issues.Cell(row, 5).Value = issue.Fix ?? string.Empty;
                issues.Cell(row, 6).Value = issue.RoomKey ?? string.Empty;
            }
            FinishSheet(issues, IssueColumns.Count);
            return workbook;
        }

        private static void WriteHeader(IXLWorksheet sheet, IReadOnlyList<string> columns)
        {
            for (int i = 0; i < columns.Count; i++) sheet.Cell(1, i + 1).Value = columns[i];
        }

        /// <summary>Bold header, filter buttons, frozen top row, sensible widths.</summary>
        private static void FinishSheet(IXLWorksheet sheet, int columnCount)
        {
            IXLRange header = sheet.Range(1, 1, 1, columnCount);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#ECEFF3");
            int lastRow = Math.Max(1, sheet.LastRowUsed()?.RowNumber() ?? 1);
            sheet.Range(1, 1, lastRow, columnCount).SetAutoFilter();
            sheet.SheetView.FreezeRows(1);
            try
            {
                sheet.Columns(1, columnCount).AdjustToContents(1, Math.Min(lastRow, 200), 8, 70);
            }
            catch (Exception)
            {
                sheet.Columns(1, columnCount).Width = 18; // no font metrics on this machine
            }
        }

        private static void SetId(IXLCell cell, long id)
        {
            if (id != 0) cell.Value = id;
        }

        private static void SetNumber(IXLCell cell, double? value, int decimals)
        {
            if (value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value))
                cell.Value = Math.Round(value.Value, decimals);
        }
    }
}
