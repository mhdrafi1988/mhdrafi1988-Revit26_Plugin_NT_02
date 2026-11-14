using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Core;

namespace Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Infrastructure
{
    /// <summary>
    /// Writes the overlap list to an .xlsx file: sheet "Overlaps" (one row per pair) and sheet "Run info".
    /// Revit-free. Column names and order are fixed; new columns may only be appended.
    /// </summary>
    public static class ExcelReportWriter
    {
        /// <summary>Version of the workbook layout, written to the "Run info" sheet.</summary>
        public const int FormatVersion = 1;

        /// <summary>Header of the "Overlaps" sheet, in order.</summary>
        public static readonly string[] Columns =
        {
            "Sheet", "View", "Scale", "Type A", "Text A", "ID A", "Type B", "Text B", "ID B", "Overlap (mm)"
        };

        /// <summary>
        /// Writes <paramref name="rows"/> and <paramref name="info"/> to <paramref name="path"/>, replacing the file.
        /// Throws <see cref="System.IO.IOException"/> when the file is open in Excel or the folder is not writable.
        /// </summary>
        public static void Write(string path, IReadOnlyList<ReportRow> rows, ReportRunInfo info)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("No file path was given.", nameof(path));
            rows ??= new ReportRow[0];
            info ??= new ReportRunInfo();

            using var workbook = new XLWorkbook();

            IXLWorksheet sheet = workbook.Worksheets.Add("Overlaps");
            for (int c = 0; c < Columns.Length; c++)
                sheet.Cell(1, c + 1).SetValue(Columns[c]);
            sheet.Row(1).Style.Font.Bold = true;
            sheet.SheetView.FreezeRows(1);

            for (int i = 0; i < rows.Count; i++)
            {
                ReportRow row = rows[i];
                if (row == null)
                    continue;
                int r = i + 2;
                SetText(sheet.Cell(r, 1), row.Sheet);
                SetText(sheet.Cell(r, 2), row.View);
                SetText(sheet.Cell(r, 3), row.Scale);
                SetText(sheet.Cell(r, 4), row.TypeA);
                SetText(sheet.Cell(r, 5), row.TextA);
                sheet.Cell(r, 6).SetValue(row.IdA);
                SetText(sheet.Cell(r, 7), row.TypeB);
                SetText(sheet.Cell(r, 8), row.TextB);
                sheet.Cell(r, 9).SetValue(row.IdB);
                sheet.Cell(r, 10).SetValue(row.OverlapMm);
            }
            sheet.Column(10).Style.NumberFormat.Format = "0.00";
            sheet.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 200));

            IXLWorksheet run = workbook.Worksheets.Add("Run info");
            int line = 1;
            void Add(string name, string value)
            {
                run.Cell(line, 1).SetValue(name);
                SetText(run.Cell(line, 2), value);
                line++;
            }
            Add("Tool", "Annotation Overlap Report");
            Add("Tool version", info.ToolVersion);
            Add("Format version", FormatVersion.ToString(CultureInfo.InvariantCulture));
            Add("Run at", info.RunAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            Add("Views scanned", info.ViewsScanned.ToString(CultureInfo.InvariantCulture));
            Add("Annotations scanned", info.AnnotationsScanned.ToString(CultureInfo.InvariantCulture));
            Add("Annotations not measured", info.NotMeasured.ToString(CultureInfo.InvariantCulture));
            Add("Overlapping pairs", rows.Count.ToString(CultureInfo.InvariantCulture));
            foreach (string skipped in info.SkippedViews ?? new List<string>())
                Add("Skipped view", skipped);
            run.Column(1).Style.Font.Bold = true;
            run.Columns().AdjustToContents();

            workbook.SaveAs(path);
        }

        /// <summary>Single-line text for a cell: null becomes empty, line breaks and repeated spaces become one space.</summary>
        public static string Clean(string text) =>
            string.IsNullOrEmpty(text) ? "" : Regex.Replace(text, @"\s+", " ").Trim();

        /// <summary>Writes text as a plain string, so a value that starts with "=" never becomes a formula.</summary>
        private static void SetText(IXLCell cell, string text)
        {
            cell.Style.NumberFormat.Format = "@";
            cell.SetValue(Clean(text));
        }
    }
}
