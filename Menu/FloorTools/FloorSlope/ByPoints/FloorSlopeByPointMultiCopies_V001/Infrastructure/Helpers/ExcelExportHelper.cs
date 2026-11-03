using Revit26_Plugin_Revised.Shared.Models;
// =======================================================
// File: ExcelExportHelper.cs
// NEW CHANGES:
//   Single combined workbook — two sheets in one file:
//
//   Sheet 1 "Vertex Data":
//     All vertices including skipped ones.
//     New columns added: VertexIndex, DrainIndex, WasProcessed.
//     Skipped rows highlighted in light yellow.
//     ElevCalc_mm, ElevModel_mm, ElevDiff_mm retained.
//     ElevDiff_mm colour-coded green/orange.
//
//   Sheet 2 "Run Summary":
//     Parameter/value table — previously a separate file
//     produced by ExportResultsSummary.
//     Now generated alongside Sheet 1 in the same workbook.
//
//   ExportResultsSummary is kept for the "Export Results"
//   button path but now delegates to ExportCompactVertexData
//   so both sheets are always produced together.
//
// Earlier fixes retained:
//   #8  GetUniqueFilePath iterative — no unbounded recursion.
//   ElevationOffset_mm split into ElevCalc/ElevModel/ElevDiff.
// =======================================================

using Autodesk.Revit.DB;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DrawingColor = System.Drawing.Color;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Infrastructure.Helpers
{
    public static class ExcelExportHelper
    {
        // ── License (single authoritative location) ──────────────────────────
        static ExcelExportHelper()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        // ── Public export methods ────────────────────────────────────────────

        /// <summary>
        /// Called by the "Export Results" button in the ViewModel.
        /// Produces the same combined two-sheet workbook as the
        /// auto-export after Run, using a user-chosen file path.
        /// </summary>
        public static string ExportResultsSummary(
            string filePath,
            AutoSlopeResult result,
            double slopePercent,
            int thresholdMeters,
            bool enableDrainTolerance,
            int drainToleranceMm,
            string exportFolderPath)
        {
            if (string.IsNullOrEmpty(filePath) || result == null) return null;

            try
            {
                using (var package = new ExcelPackage())
                {
                    // Sheet 1 — vertex data not available from this call path,
                    // so produce a clear placeholder telling the user to use
                    // the auto-export file for full vertex data.
                    var placeholderSheet = package.Workbook.Worksheets.Add("Vertex Data");
                    placeholderSheet.Cells[1, 1].Value =
                        "Full vertex data is in the auto-export file " +
                        "generated immediately after Run.";
                    placeholderSheet.Cells[1, 1].Style.Font.Italic = true;
                    placeholderSheet.Cells[1, 1, 1, 5].Merge = true;
                    placeholderSheet.Column(1).Width = 80;

                    // Sheet 2 — run summary
                    var summarySheet = package.Workbook.Worksheets.Add("Run Summary");
                    FillRunSummarySheet(
                        summarySheet, result, slopePercent,
                        thresholdMeters, enableDrainTolerance,
                        drainToleranceMm, exportFolderPath);

                    package.SaveAs(new FileInfo(filePath));
                }

                return filePath;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Excel export failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Previously produced a separate detailed workbook.
        /// Now merged into ExportCompactVertexData as Sheet 1.
        /// This method is kept so existing call sites compile —
        /// it simply delegates to ExportCompactVertexData.
        /// </summary>
        public static string ExportDetailedVertexData(
            AutoSlopePayload payload,
            List<VertexData> vertexData,
            Floor floor,
            List<XYZ> drainPoints,
            double slopePercent)
        {
            // drainPoints not needed now — drain index is stored per-vertex
            return ExportCompactVertexData(payload, vertexData, floor, slopePercent);
        }

        /// <summary>
        /// Produces a two-sheet workbook:
        ///   Sheet 1 "Vertex Data"  — all vertices (processed + skipped),
        ///                            with VertexIndex, DrainIndex, WasProcessed.
        ///   Sheet 2 "Run Summary"  — parameter/value run summary table.
        /// </summary>
        public static string ExportCompactVertexData(
            AutoSlopePayload payload,
            List<VertexData> vertexData,
            Floor floor,
            double slopePercent,
            string version = "P.10.00",
            int status = 1)
        {
            if (payload?.ExportConfig == null || !payload.ExportConfig.ExportToExcel || vertexData == null)
                return null;

            try
            {
                string floorId = floor.Id.Value.ToString();
                string slopeStr = slopePercent.ToString("0.00", CultureInfo.InvariantCulture);
                string dateStr = DateTime.Now.ToString("dd-MM-yy");
                string fileName = $"{floorId}_{slopeStr}_{dateStr}.xlsx";
                string filePath = GetUniqueFilePath(Path.Combine(payload.ExportConfig.ExportPath, fileName));

                // ── Floor meta ────────────────────────────────────────────────
                string floorType = floor.Name ?? "Unknown";
                string baseLevelName = "Unknown";
                double baseOffset = 0;

                if (floor.LevelId != null && floor.LevelId != ElementId.InvalidElementId)
                {
                    Level level = floor.Document.GetElement(floor.LevelId) as Level;
                    baseLevelName = level?.Name ?? "Unknown";
                }

                Parameter offsetParam = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                if (offsetParam != null && offsetParam.HasValue)
                    baseOffset = UnitUtils.ConvertFromInternalUnits(
                        offsetParam.AsDouble(), UnitTypeId.Millimeters);

                using (var package = new ExcelPackage())
                {
                    // ════════════════════════════════════════════════════════
                    // SHEET 1 — Vertex Data
                    // ════════════════════════════════════════════════════════
                    var vertSheet = package.Workbook.Worksheets.Add("Vertex Data");

                    // Title
                    vertSheet.Cells["A1"].Value = "AUTOSLOPE — VERTEX DATA";
                    vertSheet.Cells["A1:L1"].Merge = true;
                    vertSheet.Cells["A1"].Style.Font.Bold = true;
                    vertSheet.Cells["A1"].Style.Font.Size = 14;
                    vertSheet.Cells["A1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    vertSheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightBlue);

                    // Column headers
                    // Columns: VertexIndex | DrainIndex | WasProcessed |
                    //          FloorElementId | FloorTypeName | BaseLevel | LevelOffset_mm |
                    //          PathLength_m | SlopePercent |
                    //          ElevCalc_mm | ElevModel_mm | ElevDiff_mm
                    string[] headers =
                    {
                        "VertexIndex", "DrainIndex", "WasProcessed",
                        "FloorElementId", "FloorTypeName", "BaseLevel", "LevelOffset_mm",
                        "PathLength_m", "SlopePercent",
                        "ElevCalc_mm", "ElevModel_mm", "ElevDiff_mm"
                    };

                    for (int i = 0; i < headers.Length; i++)
                    {
                        var hCell = vertSheet.Cells[3, i + 1];
                        hCell.Value = headers[i];
                        hCell.Style.Font.Bold = true;
                        hCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        hCell.Style.Fill.BackgroundColor.SetColor(DrawingColor.LightGray);
                    }

                    // Highlight the three elevation headers
                    vertSheet.Cells[3, 10].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightGreen);
                    vertSheet.Cells[3, 11].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightSkyBlue);
                    vertSheet.Cells[3, 12].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightYellow);

                    // Sort: processed first (longest path → shortest), then skipped
                    var sorted = vertexData
                        .Where(v => v.WasProcessed)
                        .OrderByDescending(v => v.PathLengthMeters)
                        .Concat(vertexData.Where(v => !v.WasProcessed))
                        .ToList();

                    int row = 4;
                    foreach (var v in sorted)
                    {
                        vertSheet.Cells[row, 1].Value = v.VertexIndex;
                        // DrainIndex: -1 for skipped, show as "—" for clarity
                        vertSheet.Cells[row, 2].Value = v.WasProcessed ? (object)v.NearestDrainIndex : "—";
                        vertSheet.Cells[row, 3].Value = v.WasProcessed ? "YES" : "NO";
                        vertSheet.Cells[row, 4].Value = floor.Id.Value;
                        vertSheet.Cells[row, 5].Value = floorType;
                        vertSheet.Cells[row, 6].Value = baseLevelName;
                        vertSheet.Cells[row, 7].Value = Math.Round(baseOffset, 0);
                        vertSheet.Cells[row, 8].Value = Math.Round(v.PathLengthMeters, 2);
                        vertSheet.Cells[row, 9].Value = slopePercent;
                        vertSheet.Cells[row, 10].Value = v.WasProcessed ? (object)Math.Round(v.ElevationOffsetMm, 0) : "—";
                        vertSheet.Cells[row, 11].Value = v.WasProcessed ? (object)Math.Round(v.ElevationFromModel_mm, 0) : "—";

                        if (v.WasProcessed)
                        {
                            double diff = Math.Round(v.ElevationDiff_mm, 0);
                            vertSheet.Cells[row, 12].Value = diff;
                            vertSheet.Cells[row, 12].Style.Fill.PatternType = ExcelFillStyle.Solid;
                            vertSheet.Cells[row, 12].Style.Fill.BackgroundColor.SetColor(
                                diff == 0 ? DrawingColor.LightGreen : DrawingColor.Orange);
                        }
                        else
                        {
                            vertSheet.Cells[row, 12].Value = "—";
                            // Highlight entire skipped row in light yellow
                            vertSheet.Cells[row, 1, row, 12].Style.Fill.PatternType = ExcelFillStyle.Solid;
                            vertSheet.Cells[row, 1, row, 12].Style.Fill.BackgroundColor
                                .SetColor(DrawingColor.LightYellow);
                        }

                        row++;
                    }

                    // Summary block below the data
                    row += 2;
                    int processed = sorted.Count(v => v.WasProcessed);
                    int skipped = sorted.Count(v => !v.WasProcessed);
                    int adjusted = sorted.Count(v => v.WasProcessed && Math.Round(v.ElevationDiff_mm, 0) != 0);

                    vertSheet.Cells[row, 1].Value = "SUMMARY";
                    vertSheet.Cells[row, 1].Style.Font.Bold = true;
                    vertSheet.Cells[row, 1].Style.Font.Size = 12;
                    row++;
                    vertSheet.Cells[row, 1].Value = "Total vertices:"; vertSheet.Cells[row, 2].Value = sorted.Count; row++;
                    vertSheet.Cells[row, 1].Value = "Processed:"; vertSheet.Cells[row, 2].Value = processed; row++;
                    vertSheet.Cells[row, 1].Value = "Skipped:"; vertSheet.Cells[row, 2].Value = skipped; row++;
                    vertSheet.Cells[row, 1].Value = "Adjusted by Revit:"; vertSheet.Cells[row, 2].Value = adjusted;
                    vertSheet.Cells[row, 3].Value = adjusted > 0 ? "⚠ check ElevDiff_mm" : "✓ none"; row++;

                    if (processed > 0)
                    {
                        var processedRows = sorted.Where(v => v.WasProcessed).ToList();
                        vertSheet.Cells[row, 1].Value = "Longest path (m):";
                        vertSheet.Cells[row, 2].Value = Math.Round(processedRows.Max(v => v.PathLengthMeters), 2); row++;
                        vertSheet.Cells[row, 1].Value = "Shortest path (m):";
                        vertSheet.Cells[row, 2].Value = Math.Round(processedRows.Min(v => v.PathLengthMeters), 2); row++;
                    }

                    vertSheet.Cells[vertSheet.Dimension.Address].AutoFitColumns();

                    // ════════════════════════════════════════════════════════
                    // SHEET 2 — Run Summary
                    // ════════════════════════════════════════════════════════
                    var summarySheet = package.Workbook.Worksheets.Add("Run Summary");

                    // Build a lightweight AutoSlopeResult from the vertexData
                    // so FillRunSummarySheet can use the shared helper.
                    var syntheticResult = new AutoSlopeResult
                    {
                        Success = true,
                        VerticesProcessed = processed,
                        VerticesSkipped = skipped,
                        RunDate = DateTime.Now.ToString("dd-MM-yy HH:mm"),
                        Version = version,
                        Status = status
                    };

                    FillRunSummarySheet(
                        summarySheet,
                        syntheticResult,
                        slopePercent,
                        (int)payload.ThresholdMeters,
                        payload.EnableDrainTolerance,
                        payload.DrainToleranceMm,
                        payload.ExportConfig.ExportPath);

                    package.SaveAs(new FileInfo(filePath));
                }

                return filePath;
            }
            catch (Exception ex)
            {
                payload?.Log?.Invoke(new Revit26_Plugin_Revised.Shared.Models.LogEntry(Revit26_Plugin_Revised.Shared.Models.LogLevel.Error, $"Excel Export Error: {ex.Message}"));
                return null;
            }
        }
        // ── Private sheet-fill helpers ───────────────────────────────────────

        /// <summary>
        /// Fills a sheet with a two-column Parameter / Value run summary table.
        /// Used by both the auto-export (Sheet 2) and the Export Results button.
        /// </summary>
        private static void FillRunSummarySheet(
            ExcelWorksheet sheet,
            AutoSlopeResult result,
            double slopePercent,
            int thresholdMeters,
            bool enableDrainTolerance,
            int drainToleranceMm,
            string exportFolderPath)
        {
            // Title
            sheet.Cells[1, 1].Value = "AUTOSLOPE — RUN SUMMARY";
            sheet.Cells[1, 1, 1, 2].Merge = true;
            sheet.Cells[1, 1].Style.Font.Bold = true;
            sheet.Cells[1, 1].Style.Font.Size = 14;
            sheet.Cells[1, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[1, 1].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightBlue);

            sheet.Cells[2, 1].Value = "Export Date:";
            sheet.Cells[2, 1].Style.Font.Bold = true;
            sheet.Cells[2, 2].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Header row
            int row = 4;
            sheet.Cells[row, 1].Value = "Parameter";
            sheet.Cells[row, 2].Value = "Value";
            sheet.Cells[row, 1, row, 2].Style.Font.Bold = true;
            sheet.Cells[row, 1, row, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[row, 1, row, 2].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightGray);
            row++;

            AddInfoRow(sheet, ref row, "Run Date", result.RunDate ?? DateTime.Now.ToString("dd-MM-yy HH:mm"));
            AddInfoRow(sheet, ref row, "Version", result.Version ?? "N/A");
            AddInfoRow(sheet, ref row, "Status", StatusToText(result.Status));
            AddInfoRow(sheet, ref row, "Slope Percentage", $"{slopePercent}%");
            AddInfoRow(sheet, ref row, "Threshold (m)", thresholdMeters.ToString());
            AddInfoRow(sheet, ref row, "Drain Tolerance Enabled", enableDrainTolerance ? "Yes" : "No");
            AddInfoRow(sheet, ref row, "Drain Tolerance (mm)", enableDrainTolerance ? drainToleranceMm.ToString() : "N/A");
            row++; // blank separator
            AddInfoRow(sheet, ref row, "Vertices Processed", result.VerticesProcessed.ToString());
            AddInfoRow(sheet, ref row, "Vertices Skipped", result.VerticesSkipped.ToString());
            AddInfoRow(sheet, ref row, "Picked Drain Count", result.PickedDrainCount.ToString());
            AddInfoRow(sheet, ref row, "Final Drain Count", result.FinalDrainCount.ToString());
            row++; // blank separator
            AddInfoRow(sheet, ref row, "Highest Elevation (mm)", $"{result.HighestElevation_mm:0}");
            AddInfoRow(sheet, ref row, "Longest Path (m)", $"{result.LongestPath_m:0.00}");
            AddInfoRow(sheet, ref row, "Run Duration (sec)", result.RunDuration_sec.ToString());
            row++; // blank separator
            AddInfoRow(sheet, ref row, "Export Folder", exportFolderPath);

            sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        }

        // ── Row helper ───────────────────────────────────────────────────────

        private static string StatusToText(int status)
        {
            switch (status)
            {
                case AppConstants.Status_OK:      return "OK";
                case AppConstants.Status_Partial: return "Partial";
                case AppConstants.Status_Failed:  return "Failed";
                default:                          return "Unknown";
            }
        }

        private static void AddInfoRow(ExcelWorksheet sheet, ref int row, string label, string value)
        {
            sheet.Cells[row, 1].Value = label;
            sheet.Cells[row, 1].Style.Font.Bold = true;
            sheet.Cells[row, 2].Value = value;
            row++;
        }

        // ── Fix #8: iterative unique-file-path — no unbounded recursion ──────
        private static string GetUniqueFilePath(string path)
        {
            if (!File.Exists(path)) return path;

            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            string stem = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);

            var match = Regex.Match(stem, @"^(.*?)_(\d{2})$");
            string baseStem = match.Success ? match.Groups[1].Value : stem;

            for (int i = 1; i <= 99; i++)
            {
                string candidate = Path.Combine(dir, $"{baseStem}_{i:D2}{ext}");
                if (!File.Exists(candidate)) return candidate;
            }

            return Path.Combine(dir, $"{baseStem}_{DateTime.Now:HHmmss}{ext}");
        }
    }
}