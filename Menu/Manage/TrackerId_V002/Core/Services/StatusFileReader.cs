using ClosedXML.Excel;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Revit26_Plugin_Revised.TrackerId.V002.Core.Services
{
    /// <summary>
    /// Reads the team's status table (Excel .xlsx/.xlsm or CSV). Columns are found by their header,
    /// without regard to case, spaces or underscores, so "Tracker_ID", "Tracker ID" and "TrackerID" all
    /// work. Only Tracker_ID and Status are required; Progress and Date are optional. When a Tracker_ID
    /// has several rows (a history log), the row with the latest date wins; rows without a date fall
    /// back to "the later row wins".
    /// </summary>
    public static class StatusFileReader
    {
        private static readonly string[] IdHeaders = { "trackerid", "id" };
        private static readonly string[] StatusHeaders = { "status", "materialstatus", "progressstatus", "currentstatus" };
        private static readonly string[] ProgressHeaders = { "progress", "progress%", "progresspct", "progresspercent", "percentcomplete", "complete%", "%" };
        private static readonly string[] DateHeaders = { "date", "statusdate", "updated", "updatedon", "lastupdated", "dateupdated" };

        /// <summary>
        /// Reads <paramref name="path"/>. Throws <see cref="InvalidDataException"/> when it has no
        /// Tracker_ID or Status column, and <see cref="IOException"/> when it cannot be opened (for
        /// example while Excel has it locked).
        /// </summary>
        public static StatusFileResult Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Choose a status file that exists.", path);

            string ext = Path.GetExtension(path).ToLowerInvariant();
            IEnumerable<StatusCells> rows = ext switch
            {
                ".xlsx" or ".xlsm" => ExcelRows(path),
                ".csv" or ".txt" => CsvRows(path),
                _ => throw new InvalidDataException("The status file must be Excel (.xlsx) or CSV.")
            };
            return FromRows(rows.ToList());
        }

        /// <summary>One row of the file as text cells; Progress may already be known from Excel's own number.</summary>
        public sealed class StatusCells
        {
            /// <summary>Row number in the file (1-based); 0 means "its position in the list".</summary>
            public int SourceRow { get; set; }

            /// <summary>Cell texts, left to right.</summary>
            public List<string> Cells { get; } = new();

            /// <summary>Excel-only: the numeric value per column (null when not a number).</summary>
            public List<double?> Numbers { get; } = new();

            /// <summary>Excel-only: the date value per column (null when not a date).</summary>
            public List<DateTime?> Dates { get; } = new();

            /// <summary>Excel-only: true per column when the cell is formatted as a percentage.</summary>
            public List<bool> IsPercent { get; } = new();
        }

        /// <summary>Builds the result from rows already split into cells (the first row with a Tracker_ID header is the header).</summary>
        public static StatusFileResult FromRows(IReadOnlyList<StatusCells> rows)
        {
            var result = new StatusFileResult();
            int headerIndex = -1, idCol = -1, statusCol = -1, progressCol = -1, dateCol = -1;

            for (int i = 0; i < Math.Min(rows.Count, 10) && headerIndex < 0; i++)
            {
                var headers = rows[i].Cells.Select(Normalise).ToList();
                idCol = Find(headers, IdHeaders);
                if (idCol < 0) continue;
                headerIndex = i;
                statusCol = Find(headers, StatusHeaders);
                progressCol = Find(headers, ProgressHeaders);
                dateCol = Find(headers, DateHeaders);
            }

            if (headerIndex < 0) throw new InvalidDataException("No Tracker_ID column found in the first 10 rows of the status file.");
            if (statusCol < 0) throw new InvalidDataException("No Status column found in the status file.");
            if (progressCol < 0) result.Warnings.Add("No Progress column; Tracker_Progress is left as it is.");
            if (dateCol < 0) result.Warnings.Add("No Date column; the later row wins when an ID is listed twice.");

            for (int i = headerIndex + 1; i < rows.Count; i++)
            {
                var row = rows[i];
                int rowNumber = row.SourceRow > 0 ? row.SourceRow : i + 1;
                if (row.Cells.All(string.IsNullOrWhiteSpace)) continue;
                result.RowCount++;

                string id = Cell(row, idCol);
                if (id.Length == 0) continue;
                result.RowsWithId++;

                var record = new StatusRecord
                {
                    TrackerId = id,
                    Status = Cell(row, statusCol),
                    Progress = progressCol < 0 ? null : Progress(row, progressCol, rowNumber, result),
                    Date = dateCol < 0 ? null : DateOf(row, dateCol, rowNumber, result),
                    Row = rowNumber
                };

                if (!result.Records.TryGetValue(id, out var existing) || Newer(record, existing))
                    result.Records[id] = record;
            }
            return result;
        }

        // Both dated: the later date wins (a tie goes to the later row). Otherwise the later row wins.
        private static bool Newer(StatusRecord candidate, StatusRecord existing) =>
            candidate.Date.HasValue && existing.Date.HasValue ? candidate.Date.Value >= existing.Date.Value : true;

        private static string Normalise(string header) =>
            new string((header ?? string.Empty).Trim().ToLowerInvariant().Where(c => c != ' ' && c != '_' && c != '-' && c != '.').ToArray());

        // Earlier names in the list win over later ones ("trackerid" before "id").
        private static int Find(List<string> headers, string[] names)
        {
            foreach (string name in names)
            {
                int i = headers.IndexOf(name);
                if (i >= 0) return i;
            }
            return -1;
        }

        private static string Cell(StatusCells row, int col) =>
            col >= 0 && col < row.Cells.Count ? (row.Cells[col] ?? string.Empty).Trim() : string.Empty;

        private static double? Progress(StatusCells row, int col, int rowNumber, StatusFileResult result)
        {
            double? value = null;
            if (col < row.Numbers.Count && row.Numbers[col].HasValue)
            {
                value = row.Numbers[col].Value;
                if (col < row.IsPercent.Count && row.IsPercent[col]) value *= 100;
            }
            else
            {
                string text = Cell(row, col);
                if (text.Length == 0) return null;
                string number = text.TrimEnd('%').Trim();
                if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ||
                    double.TryParse(number, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
                    value = v;
                else
                {
                    result.Warnings.Add($"Row {rowNumber}: progress '{text}' is not a number; left empty.");
                    return null;
                }
            }

            if (value < 0 || value > 100)
            {
                result.Warnings.Add($"Row {rowNumber}: progress {value} is outside 0–100; clamped.");
                value = Math.Clamp(value.Value, 0, 100);
            }
            return Math.Round(value.Value, 2);
        }

        private static DateTime? DateOf(StatusCells row, int col, int rowNumber, StatusFileResult result)
        {
            if (col < row.Dates.Count && row.Dates[col].HasValue) return row.Dates[col].Value;

            string text = Cell(row, col);
            if (text.Length == 0) return null;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d) ||
                DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out d))
                return d;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double oa) && oa > 20000 && oa < 80000)
                return DateTime.FromOADate(oa);

            result.Warnings.Add($"Row {rowNumber}: date '{text}' is not a date; left empty.");
            return null;
        }

        // ── Excel ─────────────────────────────────────────────────────────────

        private static IEnumerable<StatusCells> ExcelRows(string path)
        {
            // Opened read-only and shared, so a file open in Excel can still be read.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name.Equals("Status", StringComparison.OrdinalIgnoreCase))
                        ?? workbook.Worksheets.First();

            var range = sheet.RangeUsed();
            if (range == null) return new List<StatusCells>();

            int lastCol = range.LastColumn().ColumnNumber();
            var rows = new List<StatusCells>();
            foreach (var xlRow in range.Rows())
            {
                var cells = new StatusCells { SourceRow = xlRow.RowNumber() };
                for (int c = 1; c <= lastCol; c++)
                {
                    var cell = xlRow.WorksheetRow().Cell(c);
                    cells.Cells.Add(cell.GetFormattedString());
                    bool isDate = cell.DataType == XLDataType.DateTime;
                    bool isNumber = cell.DataType == XLDataType.Number;
                    cells.Dates.Add(isDate ? cell.GetDateTime() : null);
                    cells.Numbers.Add(isNumber ? cell.GetDouble() : null);
                    string format = cell.Style.NumberFormat.Format ?? string.Empty;
                    int formatId = cell.Style.NumberFormat.NumberFormatId;
                    cells.IsPercent.Add(isNumber && (format.Contains('%') || formatId == 9 || formatId == 10));
                }
                rows.Add(cells);
            }
            return rows;
        }

        // ── CSV ───────────────────────────────────────────────────────────────

        private static IEnumerable<StatusCells> CsvRows(string path)
        {
            string text;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                text = reader.ReadToEnd();

            string firstLine = text.Split('\n')[0];
            char delimiter = new[] { ',', ';', '\t' }.OrderByDescending(d => firstLine.Count(c => c == d)).First();

            var rows = new List<StatusCells>();
            var current = new StatusCells();
            var field = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else field.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == delimiter) { current.Cells.Add(field.ToString()); field.Clear(); }
                else if (c == '\n')
                {
                    current.Cells.Add(field.ToString().TrimEnd('\r'));
                    field.Clear();
                    rows.Add(current);
                    current = new StatusCells();
                }
                else field.Append(c);
            }
            if (field.Length > 0 || current.Cells.Count > 0)
            {
                current.Cells.Add(field.ToString().TrimEnd('\r'));
                rows.Add(current);
            }
            return rows;
        }
    }
}
