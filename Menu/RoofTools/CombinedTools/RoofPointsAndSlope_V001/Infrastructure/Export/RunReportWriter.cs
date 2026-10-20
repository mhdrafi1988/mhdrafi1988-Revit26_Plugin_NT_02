// =======================================================
// File: RunReportWriter.cs
// Location: Infrastructure/Export/
// Writes the run report as .xlsx (ClosedXML): sheet "Summary" and sheet
// "Points" (Roof, Step, X (m), Y (m), Height (mm)). Column names and order
// are part of the format: append new columns at the end only.
// =======================================================

using ClosedXML.Excel;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models;
using System;
using System.IO;
using System.Linq;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Infrastructure.Export
{
    /// <summary>Saves a <see cref="RunReport"/> to an Excel file.</summary>
    public static class RunReportWriter
    {
        /// <summary>
        /// Writes <paramref name="report"/> into <paramref name="folder"/> and returns the file path.
        /// Throws IOException / UnauthorizedAccessException when the file can't be written.
        /// </summary>
        public static string Write(RunReport report, string folder)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            Directory.CreateDirectory(folder);
            string safeRoof = new string(report.Roof.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            string path = Path.Combine(folder, $"RoofPointsAndSlope_{safeRoof}_{report.Date:yyyyMMdd_HHmmss}.xlsx");

            using var book = new XLWorkbook();

            IXLWorksheet summary = book.Worksheets.Add("Summary");
            summary.Cell(1, 1).Value = "Roof Points & Slope — run report";
            summary.Cell(1, 1).Style.Font.Bold = true;
            int row = 3;
            void Line(string label, string value)
            {
                summary.Cell(row, 1).Value = label;
                summary.Cell(row, 2).Value = value;
                row++;
            }

            Line("Report format", RunReport.FormatVersion.ToString());
            Line("Roof", report.Roof);
            Line("Date", report.Date.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (var (label, value) in report.Summary) Line(label, value);
            summary.Column(1).Style.Font.Bold = true;
            summary.Columns(1, 2).AdjustToContents();

            IXLWorksheet points = book.Worksheets.Add("Points");
            string[] headers = { "Roof", "Step", "X (m)", "Y (m)", "Height (mm)" };
            for (int c = 0; c < headers.Length; c++) points.Cell(1, c + 1).Value = headers[c];
            points.Row(1).Style.Font.Bold = true;
            int r = 2;
            foreach (ReportPoint p in report.Points)
            {
                points.Cell(r, 1).Value = report.Roof;
                points.Cell(r, 2).Value = p.Step;
                points.Cell(r, 3).Value = Math.Round(p.XMeters, 4);
                points.Cell(r, 4).Value = Math.Round(p.YMeters, 4);
                points.Cell(r, 5).Value = Math.Round(p.HeightMm, 1);
                r++;
            }

            points.SheetView.FreezeRows(1);
            if (r > 2) points.Range(1, 1, r - 1, headers.Length).SetAutoFilter();
            points.Columns(1, headers.Length).AdjustToContents();

            book.SaveAs(path);
            return path;
        }
    }
}
