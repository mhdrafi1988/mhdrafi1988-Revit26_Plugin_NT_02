using ClosedXML.Excel;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V002.Core.Services
{
    /// <summary>A row to list in the status template.</summary>
    public sealed class TemplateRow
    {
        /// <summary>Tracker_ID.</summary>
        public string TrackerId { get; set; } = string.Empty;

        /// <summary>Category name (information for the site team).</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Type name (information for the site team).</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Zone (information for the site team).</summary>
        public string Zone { get; set; } = string.Empty;

        /// <summary>Level (information for the site team).</summary>
        public string Level { get; set; } = string.Empty;
    }

    /// <summary>What writing the status template did.</summary>
    public sealed class TemplateResult
    {
        /// <summary>True when a new file was created; false when an existing one was extended.</summary>
        public bool Created { get; set; }

        /// <summary>Tracker_IDs added.</summary>
        public int Added { get; set; }

        /// <summary>Tracker_IDs that were already in the file (left untouched).</summary>
        public int AlreadyListed { get; set; }

        /// <summary>Full path written.</summary>
        public string Path { get; set; } = string.Empty;
    }

    /// <summary>
    /// Excel output: the Power BI table as .xlsx (same columns as the CSV), and the status template
    /// the team fills in on SharePoint. The template is never rewritten: an existing file only gets
    /// new Tracker_IDs appended, so nobody's status entries are lost.
    /// </summary>
    public static class ExcelExportService
    {
        /// <summary>Template sheet name.</summary>
        public const string StatusSheet = "Status";

        private static readonly string[] TemplateColumns = { "Tracker_ID", "Status", "Progress %", "Date", "Updated by", "Category", "Type", "Zone", "Level" };

        // Data validation is set on this many rows when the template is created.
        private const int ValidatedRows = 20000;

        /// <summary>
        /// Writes the Power BI table to <paramref name="path"/> (.xlsx, sheet "Elements"), replacing the
        /// file in one step; with <paramref name="datedCopy"/> also "name_yyyy-MM-dd.xlsx". Returns the
        /// paths written. Throws <see cref="IOException"/> when the file is open elsewhere.
        /// </summary>
        public static IReadOnlyList<string> WriteElements(string path, IEnumerable<ExportRow> rows, string modelName,
            string modelCode, DateTime exportTime, bool datedCopy)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a file to export to.", nameof(path));

            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("Elements");
            for (int c = 0; c < CsvExportService.Columns.Count; c++)
                sheet.Cell(1, c + 1).Value = CsvExportService.Columns[c];

            int r = 2;
            foreach (var row in rows ?? Enumerable.Empty<ExportRow>())
            {
                object[] values = CsvExportService.Values(row, modelName, modelCode, exportTime);
                for (int c = 0; c < values.Length; c++)
                    sheet.Cell(r, c + 1).Value = ToCell(values[c]);
                r++;
            }

            StyleHeader(sheet, CsvExportService.Columns.Count);
            sheet.Columns(1, CsvExportService.Columns.Count).AdjustToContents(1, Math.Min(r, 500));

            var written = new List<string> { SaveAtomically(workbook, path) };
            if (datedCopy)
            {
                string folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? string.Empty;
                written.Add(SaveAtomically(workbook, System.IO.Path.Combine(folder,
                    $"{System.IO.Path.GetFileNameWithoutExtension(path)}_{exportTime:yyyy-MM-dd}{System.IO.Path.GetExtension(path)}")));
            }
            return written;
        }

        /// <summary>
        /// Creates the status template at <paramref name="path"/>, or, when it already exists, appends the
        /// Tracker_IDs it does not list yet. Existing rows and columns are never changed. A new file gets a
        /// Status drop-down from <paramref name="statuses"/>, 0–100 validation on Progress and date
        /// validation on Date. Throws <see cref="IOException"/> when the file is open elsewhere.
        /// </summary>
        public static TemplateResult WriteStatusTemplate(string path, IEnumerable<TemplateRow> rows, IReadOnlyList<string> statuses)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a file for the status template.", nameof(path));
            var result = new TemplateResult { Path = System.IO.Path.GetFullPath(path) };
            var wanted = (rows ?? Enumerable.Empty<TemplateRow>())
                .Where(t => !string.IsNullOrWhiteSpace(t.TrackerId))
                .GroupBy(t => t.TrackerId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            XLWorkbook workbook = null;
            MemoryStream buffer = null;
            try
            {
                IXLWorksheet sheet;
                if (File.Exists(result.Path))
                {
                    // Read into memory: ClosedXML keeps using the stream it opened, and the file itself
                    // must stay free so it can be replaced on save.
                    buffer = new MemoryStream();
                    using (var stream = new FileStream(result.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        stream.CopyTo(buffer);
                    buffer.Position = 0;
                    workbook = new XLWorkbook(buffer);
                    sheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name.Equals(StatusSheet, StringComparison.OrdinalIgnoreCase))
                            ?? workbook.Worksheets.First();
                }
                else
                {
                    result.Created = true;
                    workbook = new XLWorkbook();
                    sheet = CreateTemplateSheet(workbook, statuses);
                }

                // Find the template's columns by header, so a sheet the team rearranged still works.
                var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var headerRow = sheet.Row(1);
                int lastCol = Math.Max(sheet.LastColumnUsed()?.ColumnNumber() ?? 0, TemplateColumns.Length);
                for (int c = 1; c <= lastCol; c++)
                {
                    string h = headerRow.Cell(c).GetString().Trim();
                    if (h.Length > 0 && !headers.ContainsKey(h)) headers[h] = c;
                }
                if (!headers.TryGetValue("Tracker_ID", out int idCol))
                    throw new InvalidDataException($"'{System.IO.Path.GetFileName(result.Path)}' has no Tracker_ID column in row 1, so it is not a Tracker status file. Choose another file name.");

                int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
                var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int r = 2; r <= lastRow; r++)
                {
                    string id = sheet.Cell(r, idCol).GetString().Trim();
                    if (id.Length > 0) listed.Add(id);
                }

                int next = lastRow + 1;
                foreach (var t in wanted)
                {
                    if (listed.Contains(t.TrackerId))
                    {
                        result.AlreadyListed++;
                        continue;
                    }
                    sheet.Cell(next, idCol).Value = t.TrackerId;
                    SetIfColumn(sheet, headers, next, "Category", t.Category);
                    SetIfColumn(sheet, headers, next, "Type", t.Type);
                    SetIfColumn(sheet, headers, next, "Zone", t.Zone);
                    SetIfColumn(sheet, headers, next, "Level", t.Level);
                    next++;
                    result.Added++;
                }

                if (result.Created || result.Added > 0) SaveAtomically(workbook, result.Path);
                return result;
            }
            finally
            {
                workbook?.Dispose();
                buffer?.Dispose();
            }
        }

        private static IXLWorksheet CreateTemplateSheet(XLWorkbook workbook, IReadOnlyList<string> statuses)
        {
            var sheet = workbook.AddWorksheet(StatusSheet);
            for (int c = 0; c < TemplateColumns.Length; c++)
                sheet.Cell(1, c + 1).Value = TemplateColumns[c];
            StyleHeader(sheet, TemplateColumns.Length);

            // The drop-down reads its choices from a hidden sheet, so the list can be edited later.
            var lists = workbook.AddWorksheet("Lists");
            var choices = (statuses ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (choices.Count == 0) choices = StatusColours.Defaults().Select(d => d.Status).ToList();
            lists.Cell(1, 1).Value = "Status";
            for (int i = 0; i < choices.Count; i++) lists.Cell(i + 2, 1).Value = choices[i];
            lists.Hide();

            var statusValidation = sheet.Range(2, 2, ValidatedRows, 2).CreateDataValidation();
            statusValidation.List(lists.Range(2, 1, choices.Count + 1, 1), true);
            statusValidation.IgnoreBlanks = true;

            var progressValidation = sheet.Range(2, 3, ValidatedRows, 3).CreateDataValidation();
            progressValidation.Decimal.Between(0, 100);
            progressValidation.IgnoreBlanks = true;
            progressValidation.ErrorMessage = "Progress is a number from 0 to 100.";

            var dateValidation = sheet.Range(2, 4, ValidatedRows, 4).CreateDataValidation();
            dateValidation.Date.GreaterThan(new DateTime(2000, 1, 1));
            dateValidation.IgnoreBlanks = true;
            sheet.Range(2, 4, ValidatedRows, 4).Style.NumberFormat.Format = "yyyy-mm-dd";

            sheet.Column(1).Width = 34;
            sheet.Column(2).Width = 16;
            sheet.Columns(3, 5).Width = 14;
            sheet.Columns(6, 9).Width = 18;
            return sheet;
        }

        private static void SetIfColumn(IXLWorksheet sheet, Dictionary<string, int> headers, int row, string header, string value)
        {
            if (headers.TryGetValue(header, out int col) && !string.IsNullOrEmpty(value))
                sheet.Cell(row, col).Value = value;
        }

        private static void StyleHeader(IXLWorksheet sheet, int columns)
        {
            var header = sheet.Range(1, 1, 1, columns);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A5F");
            header.Style.Font.FontColor = XLColor.White;
            sheet.SheetView.FreezeRows(1);
            sheet.Range(1, 1, 1, columns).SetAutoFilter();
        }

        private static XLCellValue ToCell(object value) => value switch
        {
            null => Blank.Value,
            double d => d,
            long l => l,
            _ => value.ToString()
        };

        private static string SaveAtomically(XLWorkbook workbook, string path)
        {
            string full = System.IO.Path.GetFullPath(path);
            string folder = System.IO.Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string temp = full + ".tmp.xlsx";
            try
            {
                workbook.SaveAs(temp);
                File.Move(temp, full, overwrite: true);
                return full;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
                throw new IOException($"Could not write '{full}'. Close it in Excel and try again. ({ex.Message})", ex);
            }
        }
    }
}
