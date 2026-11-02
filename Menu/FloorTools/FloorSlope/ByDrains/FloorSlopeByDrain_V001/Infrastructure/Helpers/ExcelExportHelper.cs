// File: ExcelExportHelper.cs
// Location: Infrastructure/Helpers/
// Base: adapted from AutoSlopeByPoint's ExcelExportHelper.ExportCompactVertexData
// (the REAL, corrected 2-sheet format — "Vertex Data" + "Run Summary" — not
// the mistaken "4-sheet" description originally given to Rafi and retracted;
// see conversation history 2026-07-21).
//
// CHANGES vs ByPoint's original:
//   FIXED   the IncludeVertexDetails bug: skipped vertices are now ALWAYS
//           included in the Vertex Data sheet (this tool's
//           FloorSlopeProcessorService.CollectVertexExportData already
//           guarantees every floor vertex gets a row, processed or not —
//           see that file's V005 fix notes). There is exactly ONE export
//           call; no duplicate "detailed" file with a stale sheet-list log
//           message.
//   ADAPTED from AutoSlopeByPoint's VertexData/AutoSlopeResult to this
//           tool's DrainVertexData/DrainExportMetrics.
//   ADDED   NEW (V005) Run Summary rows: Total Detected, Selected Count,
//           Arcs Calculated, Unreachable Vertices, Over-Threshold Vertices,
//           Max Edge Distance, Max Path Distance (both threshold fields
//           shown distinctly — see AutoSlopeDrainPayload for why they are
//           NOT the same value).
//   ADDED   DrainId column (which selected-drain grid row a vertex routes
//           to) since ByDrain's drains come from the grid, not raw points.

using Autodesk.Revit.DB;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DrawingColor = System.Drawing.Color;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Infrastructure.Helpers
{
    public static class ExcelExportHelper
    {
        static ExcelExportHelper()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        /// <summary>
        /// Produces the single Excel workbook for a run:
        ///   Sheet 1 "Vertex Data" — every floor vertex (processed + skipped),
        ///                           with VertexIndex, DrainId, WasProcessed,
        ///                           path length, elevation columns, and (when
        ///                           verification was enabled) the model
        ///                           read-back + diff columns.
        ///   Sheet 2 "Run Summary"  — parameter/value run summary table.
        /// </summary>
        public static string ExportWorkbook(
            AutoSlopeDrainPayload payload,
            List<DrainVertexData> vertexData,
            Floor floor,
            double slopePercent,
            DrainExportMetrics metrics,
            string version = Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Engine.AutoSlopeDrainEngine.FallbackToolVersion)
        {
            if (payload?.ExportConfig == null || !payload.ExportConfig.ExportToExcel || vertexData == null)
                return null;

            try
            {
                string floorId = floor.Id.Value.ToString();
                string slopeStr = slopePercent.ToString("0.00", CultureInfo.InvariantCulture);
                string dateStr = DateTime.Now.ToString("dd-MM-yy");
                string fileName = $"{payload.ExportConfig.FileNamePrefix}{floorId}_{slopeStr}_{dateStr}.xlsx";
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

                bool showVerificationColumns = payload.VerifyElevationsAfterCommit;

                using (var package = new ExcelPackage())
                {
                    // ════════════════════════════════════════════════════════
                    // SHEET 1 — Vertex Data
                    // ════════════════════════════════════════════════════════
                    var vertSheet = package.Workbook.Worksheets.Add("Vertex Data");

                    vertSheet.Cells["A1"].Value = "AUTOSLOPE BY DRAIN — VERTEX DATA";
                    int lastCol = showVerificationColumns ? 14 : 12;
                    vertSheet.Cells[1, 1, 1, lastCol].Merge = true;
                    vertSheet.Cells["A1"].Style.Font.Bold = true;
                    vertSheet.Cells["A1"].Style.Font.Size = 14;
                    vertSheet.Cells["A1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    vertSheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightBlue);

                    // Column headers
                    var headers = new List<string>
                    {
                        "VertexIndex", "DrainId", "DrainSize", "DrainShape", "WasProcessed",
                        "FloorElementId", "FloorTypeName", "BaseLevel", "LevelOffset_mm",
                        "PathLength_m", "SlopePercent", "ElevCalc_mm"
                    };
                    if (showVerificationColumns)
                    {
                        headers.Add("ElevModel_mm");
                        headers.Add("ElevDiff_mm");
                    }

                    for (int i = 0; i < headers.Count; i++)
                    {
                        var hCell = vertSheet.Cells[3, i + 1];
                        hCell.Value = headers[i];
                        hCell.Style.Font.Bold = true;
                        hCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        hCell.Style.Fill.BackgroundColor.SetColor(DrawingColor.LightGray);
                    }

                    // Highlight the elevation headers
                    vertSheet.Cells[3, 12].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightGreen);
                    if (showVerificationColumns)
                    {
                        vertSheet.Cells[3, 13].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightSkyBlue);
                        vertSheet.Cells[3, 14].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightYellow);
                    }

                    // Sort: processed first (longest path → shortest), then skipped
                    var sorted = vertexData
                        .Where(v => v.WasProcessed)
                        .OrderByDescending(v => v.PathLengthMeters)
                        .Concat(vertexData.Where(v => !v.WasProcessed))
                        .ToList();

                    int row = 4;
                    foreach (var v in sorted)
                    {
                        int col = 1;
                        vertSheet.Cells[row, col++].Value = v.VertexIndex;
                        vertSheet.Cells[row, col++].Value = v.WasProcessed ? (object)v.NearestDrainId : "—";
                        vertSheet.Cells[row, col++].Value = v.WasProcessed ? v.DrainSize : "—";
                        vertSheet.Cells[row, col++].Value = v.WasProcessed ? v.DrainShape : "—";
                        vertSheet.Cells[row, col++].Value = v.WasProcessed ? "YES" : "NO";
                        vertSheet.Cells[row, col++].Value = floor.Id.Value;
                        vertSheet.Cells[row, col++].Value = floorType;
                        vertSheet.Cells[row, col++].Value = baseLevelName;
                        vertSheet.Cells[row, col++].Value = Math.Round(baseOffset, 0);
                        vertSheet.Cells[row, col++].Value = v.WasProcessed ? (object)Math.Round(v.PathLengthMeters, 2) : "—";
                        vertSheet.Cells[row, col++].Value = slopePercent;
                        vertSheet.Cells[row, col++].Value = v.WasProcessed ? (object)Math.Round(v.ElevationOffsetMm, 0) : "—";

                        if (showVerificationColumns)
                        {
                            vertSheet.Cells[row, col++].Value = v.WasProcessed ? (object)Math.Round(v.ElevationFromModel_mm, 0) : "—";

                            if (v.WasProcessed)
                            {
                                double diff = Math.Round(v.ElevationDiff_mm, 0);
                                vertSheet.Cells[row, col].Value = diff;
                                vertSheet.Cells[row, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
                                vertSheet.Cells[row, col].Style.Fill.BackgroundColor.SetColor(
                                    diff == 0 ? DrawingColor.LightGreen : DrawingColor.Orange);
                            }
                            else
                            {
                                vertSheet.Cells[row, col].Value = "—";
                            }
                            col++;
                        }

                        if (!v.WasProcessed)
                        {
                            // Highlight entire skipped row in light yellow
                            vertSheet.Cells[row, 1, row, lastCol].Style.Fill.PatternType = ExcelFillStyle.Solid;
                            vertSheet.Cells[row, 1, row, lastCol].Style.Fill.BackgroundColor
                                .SetColor(DrawingColor.LightYellow);
                        }

                        row++;
                    }

                    // Summary block below the data
                    row += 2;
                    int processed = sorted.Count(v => v.WasProcessed);
                    int skipped = sorted.Count(v => !v.WasProcessed);
                    int adjusted = showVerificationColumns
                        ? sorted.Count(v => v.WasProcessed && Math.Round(v.ElevationDiff_mm, 0) != 0)
                        : 0;

                    vertSheet.Cells[row, 1].Value = "SUMMARY";
                    vertSheet.Cells[row, 1].Style.Font.Bold = true;
                    vertSheet.Cells[row, 1].Style.Font.Size = 12;
                    row++;
                    vertSheet.Cells[row, 1].Value = "Total vertices:"; vertSheet.Cells[row, 2].Value = sorted.Count; row++;
                    vertSheet.Cells[row, 1].Value = "Processed:"; vertSheet.Cells[row, 2].Value = processed; row++;
                    vertSheet.Cells[row, 1].Value = "Skipped:"; vertSheet.Cells[row, 2].Value = skipped; row++;

                    if (showVerificationColumns)
                    {
                        vertSheet.Cells[row, 1].Value = "Adjusted by Revit:"; vertSheet.Cells[row, 2].Value = adjusted;
                        vertSheet.Cells[row, 3].Value = adjusted > 0 ? "⚠ check ElevDiff_mm" : "✓ none"; row++;
                    }

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
                    FillRunSummarySheet(
                        summarySheet, metrics, slopePercent,
                        payload.ConnectionThresholdMeters, payload.ThresholdMeters,
                        payload.InsertCurveIntersectionPoints, payload.VerifyElevationsAfterCommit,
                        version, payload.ExportConfig.ExportPath);

                    package.SaveAs(new FileInfo(filePath));
                }

                return filePath;
            }
            catch (Exception ex)
            {
                payload?.Log?.Invoke(new Revit26_Plugin_Revised.Shared.Models.LogEntry(
                    Revit26_Plugin_Revised.Shared.Models.LogLevel.Error, $"Excel Export Error: {ex.Message}"));
                return null;
            }
        }

        /// <summary>
        /// NEW (V008). One workbook, one sheet, one row per floor in a
        /// multi-floor run — written in addition to each floor's own per-floor
        /// workbook (produced by ExportWorkbook above, unchanged).
        /// </summary>
        public static string ExportCombinedSummary(
            List<AutoSlopeDrainFloorResult> floorResults,
            string exportFolderPath,
            string projectTitle)
        {
            if (floorResults == null || floorResults.Count == 0 || string.IsNullOrWhiteSpace(exportFolderPath))
                return null;

            Directory.CreateDirectory(exportFolderPath);

            string dateStr = DateTime.Now.ToString("dd-MM-yy_HHmmss");
            string fileName = $"FloorSlopeByDrain_Summary_{dateStr}.xlsx";
            string filePath = GetUniqueFilePath(Path.Combine(exportFolderPath, fileName));

            using (var package = new ExcelPackage())
            {
                var sheet = package.Workbook.Worksheets.Add("Multi-Floor Summary");

                sheet.Cells["A1"].Value = "AUTOSLOPE BY DRAIN — MULTI-FLOOR SUMMARY";
                sheet.Cells[1, 1, 1, 10].Merge = true;
                sheet.Cells["A1"].Style.Font.Bold = true;
                sheet.Cells["A1"].Style.Font.Size = 14;
                sheet.Cells["A1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                sheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightBlue);

                sheet.Cells[2, 1].Value = "Project:";
                sheet.Cells[2, 1].Style.Font.Bold = true;
                sheet.Cells[2, 2].Value = projectTitle ?? "Unknown Project";
                sheet.Cells[3, 1].Value = "Export Date:";
                sheet.Cells[3, 1].Style.Font.Bold = true;
                sheet.Cells[3, 2].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                var headers = new[]
                {
                    "Floor Name", "Floor Id", "Status", "Drains Selected",
                    "Longest Path (m)", "Highest Elevation (mm)", "Start Time", "End Time", "Run Duration (sec)", "Exported File"
                };

                int headerRow = 5;
                for (int i = 0; i < headers.Length; i++)
                {
                    var hCell = sheet.Cells[headerRow, i + 1];
                    hCell.Value = headers[i];
                    hCell.Style.Font.Bold = true;
                    hCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hCell.Style.Fill.BackgroundColor.SetColor(DrawingColor.LightGray);
                }

                int row = headerRow + 1;
                foreach (var rr in floorResults)
                {
                    var r = rr.Result;
                    int col = 1;
                    sheet.Cells[row, col++].Value = rr.FloorName ?? "Unknown";
                    sheet.Cells[row, col++].Value = rr.FloorElementId;
                    sheet.Cells[row, col++].Value = r?.Success == true ? "Success" : "Failed";
                    sheet.Cells[row, col++].Value = r?.SelectedCount ?? 0;
                    sheet.Cells[row, col++].Value = r?.Success == true ? (object)Math.Round(r.LongestPath_m, 2) : "—";
                    sheet.Cells[row, col++].Value = r?.Success == true ? (object)Math.Round(r.HighestElevation_mm, 0) : "—";
                    sheet.Cells[row, col++].Value = r?.StartTime ?? "—";
                    sheet.Cells[row, col++].Value = r?.EndTime ?? "—";
                    sheet.Cells[row, col++].Value = r?.RunDuration_sec ?? 0;
                    sheet.Cells[row, col++].Value = r?.ExportedFilePath ?? "—";

                    if (r?.Success != true)
                    {
                        sheet.Cells[row, 1, row, headers.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        sheet.Cells[row, 1, row, headers.Length].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightYellow);
                    }

                    row++;
                }

                sheet.Cells[sheet.Dimension.Address].AutoFitColumns();

                package.SaveAs(new FileInfo(filePath));
            }

            return filePath;
        }

        private static void FillRunSummarySheet(
            ExcelWorksheet sheet,
            DrainExportMetrics metrics,
            double slopePercent,
            double connectionThresholdMeters,
            double thresholdMeters,
            bool curveIntersectionEnabled,
            bool verificationEnabled,
            string version,
            string exportFolderPath)
        {
            sheet.Cells[1, 1].Value = "AUTOSLOPE BY DRAIN — RUN SUMMARY";
            sheet.Cells[1, 1, 1, 2].Merge = true;
            sheet.Cells[1, 1].Style.Font.Bold = true;
            sheet.Cells[1, 1].Style.Font.Size = 14;
            sheet.Cells[1, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[1, 1].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightBlue);

            sheet.Cells[2, 1].Value = "Export Date:";
            sheet.Cells[2, 1].Style.Font.Bold = true;
            sheet.Cells[2, 2].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            int row = 4;
            sheet.Cells[row, 1].Value = "Parameter";
            sheet.Cells[row, 2].Value = "Value";
            sheet.Cells[row, 1, row, 2].Style.Font.Bold = true;
            sheet.Cells[row, 1, row, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[row, 1, row, 2].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightGray);
            row++;

            AddInfoRow(sheet, ref row, "Run Date", metrics.RunDate ?? DateTime.Now.ToString("dd-MM-yy HH:mm"));
            AddInfoRow(sheet, ref row, "Version", version ?? "N/A");
            AddInfoRow(sheet, ref row, "Slope Percentage", $"{slopePercent}%");
            AddInfoRow(sheet, ref row, "Max Edge Distance (m)", connectionThresholdMeters.ToString("0.0"));
            AddInfoRow(sheet, ref row, "Max Path Distance (m)", thresholdMeters.ToString("0.0"));
            AddInfoRow(sheet, ref row, "Curve Intersection Insertion", curveIntersectionEnabled ? "Enabled" : "Disabled");
            AddInfoRow(sheet, ref row, "Elevation Verification", verificationEnabled ? "Enabled" : "Disabled");
            row++; // blank separator

            AddInfoRow(sheet, ref row, "Total Drains Detected", metrics.TotalDetectedCount.ToString());
            AddInfoRow(sheet, ref row, "Drains Selected", metrics.SelectedCount.ToString());
            AddInfoRow(sheet, ref row, "Arcs Calculated", metrics.ArcsCalculated.ToString());
            row++; // blank separator

            AddInfoRow(sheet, ref row, "Vertices Processed", metrics.ProcessedVertices.ToString());
            AddInfoRow(sheet, ref row, "Vertices Skipped", metrics.SkippedVertices.ToString());
            AddInfoRow(sheet, ref row, "  of which Unreachable", metrics.UnreachableVertexCount.ToString());
            AddInfoRow(sheet, ref row, "  of which Over Max Path Distance", metrics.OverThresholdVertexCount.ToString());
            row++; // blank separator

            AddInfoRow(sheet, ref row, "Highest Elevation (mm)", $"{metrics.HighestElevationMm:0}");
            AddInfoRow(sheet, ref row, "Longest Path (m)", $"{metrics.LongestPathM:0.00}");
            AddInfoRow(sheet, ref row, "Start Time", metrics.StartTime ?? "N/A");
            AddInfoRow(sheet, ref row, "End Time", metrics.EndTime ?? "N/A");
            AddInfoRow(sheet, ref row, "Run Duration (sec)", metrics.RunDurationSec.ToString());
            row++; // blank separator
            AddInfoRow(sheet, ref row, "Export Folder", exportFolderPath);

            sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        }

        private static void AddInfoRow(ExcelWorksheet sheet, ref int row, string label, string value)
        {
            sheet.Cells[row, 1].Value = label;
            sheet.Cells[row, 1].Style.Font.Bold = true;
            sheet.Cells[row, 2].Value = value;
            row++;
        }

        // Iterative unique-file-path — no unbounded recursion.
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
