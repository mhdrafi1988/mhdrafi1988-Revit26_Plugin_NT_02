// =======================================================
// File: ExcelExportService.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.Helpers
// Changes vs V030:
//   - Works only from AutoSlopeResult (no Revit objects), so the manual
//     "Export results…" writes the SAME full workbook as the auto-export
//     (V030 wrote a placeholder "Vertex Data" sheet).
//   - Detailed-export dead path removed (IncludeVertexDetails was always false).
//   - Workbook columns: DrainIndex is the drain the path actually reaches
//     (D4); a Status column says why a point was skipped (D5).
// LICENCE NOTE: EPPlus 5+ is Polyform Noncommercial. Check the licence
// before commercial use, or swap to ClosedXML (MIT). Not legal advice.
// =======================================================

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using DrawingColor = System.Drawing.Color;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.Helpers
{
    public static class ExcelExportService
    {
        private static bool? _available;

        static ExcelExportService()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        private static bool IsAvailable(Action<LogLevel, string> log)
        {
            if (_available.HasValue) return _available.Value;

            string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string dll = Path.Combine(folder, "EPPlus.dll");
            _available = File.Exists(dll);
            if (!_available.Value)
                log?.Invoke(LogLevel.Warning, $"Excel export skipped: EPPlus.dll was not found next to the add-in ({dll}).");
            return _available.Value;
        }

        /// <summary>Auto-export after a run, into the request's export folder. Returns the path or null.</summary>
        public static string ExportAuto(AutoSlopeResult result, Action<LogLevel, string> log)
        {
            if (result?.Request == null || !IsAvailable(log)) return null;

            try
            {
                string folder = result.Request.ExportFolder;
                if (string.IsNullOrWhiteSpace(folder))
                {
                    log(LogLevel.Warning, "Excel export skipped: no export folder is set.");
                    return null;
                }
                Directory.CreateDirectory(folder);

                string slope = result.Request.SlopePercent.ToString("0.00", CultureInfo.InvariantCulture);
                string name = $"{result.Roof?.IdValue ?? 0}_{slope}_{result.StartTime:dd-MM-yy}.xlsx";
                string path = UniquePath(Path.Combine(folder, name));

                Write(path, result);
                log(LogLevel.Success, $"Excel saved: {path}");
                return path;
            }
            catch (Exception ex)
            {
                log(LogLevel.Error, $"Excel export failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Manual export to a chosen path. Returns an error message, or null on success.</summary>
        public static string ExportTo(string path, AutoSlopeResult result, Action<LogLevel, string> log)
        {
            if (result == null) return "Run AutoSlope first.";
            if (!IsAvailable(log)) return "EPPlus.dll was not found next to the add-in.";
            try
            {
                Write(path, result);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static string DefaultFileName(AutoSlopeResult result) =>
            $"AutoSlope_Results_{result?.Roof?.IdValue ?? 0}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

        // ── Workbook ─────────────────────────────────────────────────────────

        private static void Write(string path, AutoSlopeResult r)
        {
            using var package = new ExcelPackage();
            FillVertexSheet(package.Workbook.Worksheets.Add("Vertex Data"), r);
            FillSummarySheet(package.Workbook.Worksheets.Add("Run Summary"), r);
            package.SaveAs(new FileInfo(path));
        }

        private static void FillVertexSheet(ExcelWorksheet ws, AutoSlopeResult r)
        {
            ws.Cells["A1"].Value = "AUTOSLOPE — VERTEX DATA";
            ws.Cells["A1:M1"].Merge = true;
            ws.Cells["A1"].Style.Font.Bold = true;
            ws.Cells["A1"].Style.Font.Size = 14;
            ws.Cells["A1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells["A1"].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightBlue);

            string[] headers =
            {
                "VertexIndex", "DrainIndex", "Status", "IsDrain",
                "RoofElementId", "RoofTypeName", "BaseLevel", "LevelOffset_mm",
                "PathLength_m", "SlopePercent",
                "ElevCalc_mm", "ElevModel_mm", "ElevDiff_mm"
            };
            for (int i = 0; i < headers.Length; i++)
            {
                var c = ws.Cells[3, i + 1];
                c.Value = headers[i];
                c.Style.Font.Bold = true;
                c.Style.Fill.PatternType = ExcelFillStyle.Solid;
                c.Style.Fill.BackgroundColor.SetColor(DrawingColor.LightGray);
            }
            ws.Cells[3, 11].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightGreen);
            ws.Cells[3, 12].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightSkyBlue);
            ws.Cells[3, 13].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightYellow);

            var rows = r.Vertices
                .Where(v => v.WasProcessed).OrderByDescending(v => v.PathLengthMeters)
                .Concat(r.Vertices.Where(v => !v.WasProcessed))
                .ToList();

            RoofInfo roof = r.Roof ?? new RoofInfo(0, "Unknown", "Unknown", 0);
            int row = 4;
            foreach (var v in rows)
            {
                ws.Cells[row, 1].Value = v.VertexIndex;
                ws.Cells[row, 2].Value = v.WasProcessed && v.DrainIndex >= 0 ? (object)v.DrainIndex : "—";
                ws.Cells[row, 3].Value = v.Outcome switch
                {
                    VertexOutcome.Processed => "Sloped",
                    VertexOutcome.BeyondDistance => "Beyond distance",
                    _ => "No path"
                };
                ws.Cells[row, 4].Value = v.IsDrain ? "YES" : "";
                ws.Cells[row, 5].Value = roof.IdValue;
                ws.Cells[row, 6].Value = roof.TypeName;
                ws.Cells[row, 7].Value = roof.BaseLevelName;
                ws.Cells[row, 8].Value = Math.Round(roof.BaseOffsetMm, 0);
                ws.Cells[row, 9].Value = v.Outcome == VertexOutcome.NoPath ? (object)"—" : Math.Round(v.PathLengthMeters, 2);
                ws.Cells[row, 10].Value = r.Request?.SlopePercent ?? 0;

                if (v.WasProcessed)
                {
                    ws.Cells[row, 11].Value = Math.Round(v.ElevationCalcMm, 0);
                    ws.Cells[row, 12].Value = v.HasModelValue ? (object)Math.Round(v.ElevationModelMm, 0) : "—";
                    double diff = v.ElevationDiffMm;
                    ws.Cells[row, 13].Value = diff;
                    ws.Cells[row, 13].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    ws.Cells[row, 13].Style.Fill.BackgroundColor.SetColor(diff == 0 ? DrawingColor.LightGreen : DrawingColor.Orange);
                }
                else
                {
                    ws.Cells[row, 11].Value = "—";
                    ws.Cells[row, 12].Value = "—";
                    ws.Cells[row, 13].Value = "—";
                    ws.Cells[row, 1, row, 13].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    ws.Cells[row, 1, row, 13].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightYellow);
                }
                row++;
            }

            row += 2;
            int processed = rows.Count(v => v.WasProcessed);
            int adjusted = rows.Count(v => v.WasProcessed && v.ElevationDiffMm != 0);
            ws.Cells[row, 1].Value = "SUMMARY";
            ws.Cells[row, 1].Style.Font.Bold = true;
            row++;
            Pair(ws, ref row, "Total points:", rows.Count);
            Pair(ws, ref row, "Sloped:", processed);
            Pair(ws, ref row, "Beyond distance:", rows.Count(v => v.Outcome == VertexOutcome.BeyondDistance));
            Pair(ws, ref row, "No path:", rows.Count(v => v.Outcome == VertexOutcome.NoPath));
            Pair(ws, ref row, "Adjusted by Revit:", adjusted);
            if (processed > 0)
            {
                Pair(ws, ref row, "Longest path (m):", Math.Round(rows.Where(v => v.WasProcessed).Max(v => v.PathLengthMeters), 2));
                Pair(ws, ref row, "Shortest path (m):", Math.Round(rows.Where(v => v.WasProcessed).Min(v => v.PathLengthMeters), 2));
            }

            if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns();
        }

        private static void FillSummarySheet(ExcelWorksheet ws, AutoSlopeResult r)
        {
            ws.Cells[1, 1].Value = "AUTOSLOPE — RUN SUMMARY";
            ws.Cells[1, 1, 1, 2].Merge = true;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.Font.Size = 14;
            ws.Cells[1, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[1, 1].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightBlue);
            ws.Cells[2, 1].Value = "Exported:";
            ws.Cells[2, 1].Style.Font.Bold = true;
            ws.Cells[2, 2].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            int row = 4;
            ws.Cells[row, 1].Value = "Parameter";
            ws.Cells[row, 2].Value = "Value";
            ws.Cells[row, 1, row, 2].Style.Font.Bold = true;
            ws.Cells[row, 1, row, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[row, 1, row, 2].Style.Fill.BackgroundColor.SetColor(DrawingColor.LightGray);
            row++;

            AutoSlopeRequest q = r.Request;
            Info(ws, ref row, "Project", q?.ProjectTitle ?? "");
            Info(ws, ref row, "Roof", $"{r.Roof?.IdValue} · {r.Roof?.TypeName} · {r.Roof?.BaseLevelName}");
            Info(ws, ref row, "Run date", r.RunDate);
            Info(ws, ref row, "Start time", r.StartTime.ToString("HH:mm:ss"));
            Info(ws, ref row, "End time", r.EndTime.ToString("HH:mm:ss"));
            Info(ws, ref row, "Duration (s)", r.DurationSeconds.ToString("0.0"));
            Info(ws, ref row, "Tool version", r.ToolVersion ?? "");
            Info(ws, ref row, "Parameter status", StatusText(r.ParameterStatus));
            row++;
            Info(ws, ref row, "Slope (%)", $"{q?.SlopePercent}");
            Info(ws, ref row, "Max drainage distance (m)", $"{q?.MaxDrainDistanceM}");
            Info(ws, ref row, "Max link length (m)", q == null ? "" : (q.MaxEdgeLengthM > 0 ? $"{q.MaxEdgeLengthM}" : $"same as drainage distance ({q.MaxDrainDistanceM})"));
            Info(ws, ref row, "Drain mode", q?.DrainMode == DrainMode.AddPointAtPick ? "Add a shape point at each pick" : "Snap to nearest shape point");
            Info(ws, ref row, "Nearby points radius (mm)", q?.IncludeNearbyPoints == true ? $"{q.SnapRadiusMm}" : "Off");
            Info(ws, ref row, "Curve handling", q?.InsertCurveIntersectionPoints == true ? $"On ({r.ArcsFound} arcs, {r.ArcPointsInserted} points inserted)" : "Off");
            row++;
            Info(ws, ref row, "Points sloped", r.VerticesProcessed.ToString());
            Info(ws, ref row, "Points skipped", r.VerticesSkipped.ToString());
            Info(ws, ref row, "  of which no path", r.VerticesUnreached.ToString());
            Info(ws, ref row, "Drains picked", r.PickedDrainCount.ToString());
            Info(ws, ref row, "Drain points used", r.FinalDrainCount.ToString());
            row++;
            Info(ws, ref row, "Highest elevation (mm)", r.HighestElevationMm.ToString("0"));
            Info(ws, ref row, "Longest path (m)", r.LongestPathM.ToString("0.00"));
            Info(ws, ref row, "Points adjusted by Revit", $"{r.AdjustedByRevitCount} (max {r.MaxAdjustmentMm:0} mm)");
            Info(ws, ref row, "Circles placed", $"{r.DrainCirclesPlaced} drain · {r.HighestCirclesPlaced} highest · {r.OffsetCirclesPlaced} offset");
            row++;
            Info(ws, ref row, "Export folder", q?.ExportFolder ?? "");
            // V033: new rows go at the end so the existing rows keep their place.
            Info(ws, ref row, "Ridge lines", q?.DrawRidgeLines == true
                ? $"{r.RidgeLinesPlaced} placed · {r.RidgeCount} ridge(s) · {r.RidgeLineStyleUsed ?? "-"}"
                : "Off");

            if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns();
        }

        private static string StatusText(int status) => status switch
        {
            AutoSlopeConstants.Status_OK => "OK",
            AutoSlopeConstants.Status_Partial => "Partial (some parameters missing)",
            AutoSlopeConstants.Status_Failed => "Failed (no parameters written)",
            _ => "Unknown"
        };

        private static void Info(ExcelWorksheet ws, ref int row, string label, string value)
        {
            ws.Cells[row, 1].Value = label;
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 2].Value = value;
            row++;
        }

        private static void Pair(ExcelWorksheet ws, ref int row, string label, object value)
        {
            ws.Cells[row, 1].Value = label;
            ws.Cells[row, 2].Value = value;
            row++;
        }

        private static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;

            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            string stem = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            var m = Regex.Match(stem, @"^(.*?)_(\d{2})$");
            string baseStem = m.Success ? m.Groups[1].Value : stem;

            for (int i = 1; i <= 99; i++)
            {
                string candidate = Path.Combine(dir, $"{baseStem}_{i:D2}{ext}");
                if (!File.Exists(candidate)) return candidate;
            }
            return Path.Combine(dir, $"{baseStem}_{DateTime.Now:HHmmss}{ext}");
        }
    }
}
