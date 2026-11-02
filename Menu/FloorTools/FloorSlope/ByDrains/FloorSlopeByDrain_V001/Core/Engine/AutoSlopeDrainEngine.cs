// =======================================================
// File: AutoSlopeDrainEngine.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Engine
// One floor's run. Called by DrainRunJob inside that floor's own
// TransactionGroup: the job assimilates the group only when this returns
// Success, so a failed or cancelled floor rolls back completely (reset,
// inserted arc points, slope, markers and parameters).
// Changes vs V012:
//   - Step 1 resets every shape point to 0 (moved here from the Command, so
//     picking floors no longer changes the model, and Run again always starts
//     from a flat floor with a single top face).
//   - Drain points: every shape point on the opening edge
//     (DrainDetectionService.FindShapeVerticesOnLoop), not only the points
//     next to the tessellated loop points.
//   - A selected drain that matches no shape point is named in a warning;
//     a floor whose selected drains match no point at all fails (V012
//     reported success with nothing changed and still wrote parameters).
//   - Version from ToolCatalog (V012 wrote a hard-coded "009").
//   - Skipped count excludes the drain points themselves.
//   - Result carries decimal seconds, start/end DateTimes and the vertex
//     rows for the results side panel.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Parameters;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Services;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Engine
{
    public static class AutoSlopeDrainEngine
    {
        /// <summary>Used when ToolCatalog has no entry for this namespace yet.</summary>
        public const string FallbackToolVersion = "1.0.0";

        public static string ToolVersion =>
            ToolCatalog.Find(typeof(AutoSlopeDrainEngine))?.Version ?? FallbackToolVersion;

        public static AutoSlopeDrainResult Execute(UIApplication app, AutoSlopeDrainPayload payload)
        {
            var log = payload.Log ?? (_ => { });
            var doc = app.ActiveUIDocument.Document;
            var startTime = DateTime.Now;
            string version = ToolVersion;
            log(new LogEntry(LogLevel.Info, $"Run started at {startTime:HH:mm:ss}."));

            try
            {
                payload.CancelToken.ThrowIfCancellationRequested();

                if (!(doc.GetElement(payload.FloorId) is Floor floor))
                    return Fail("The floor could not be found (it may have been deleted).", log, startTime, version);

                // ── 1. Reset every shape point to 0 ─────────────────────────
                payload.Progress?.Invoke(new RunProgressInfo("Resetting shape points to 0"));
                int resetCount = FloorGeometryService.ResetShapePoints(floor, doc);
                log(new LogEntry(LogLevel.Info, $"{resetCount} shape point(s) reset to 0."));
                payload.CancelToken.ThrowIfCancellationRequested();

                // ── 2. Fresh geometry after the reset's regenerate ───────────
                payload.Progress?.Invoke(new RunProgressInfo("Reading floor geometry"));
                var floorData = new FloorData { Floor = floor, TopFace = FloorGeometryService.GetTopFace(floor) };
                if (floorData.TopFace == null)
                    return Fail("The floor's top face could not be found.", log, startTime, version);

                var selectedDrains = payload.SelectedDrains ?? new List<DrainItem>();
                if (selectedDrains.Count == 0)
                    return Fail("No drains are selected on this floor.", log, startTime, version);

                // ── 2b. Fixture drains: a shape point at each drain family ───
                var fixtureDrains = selectedDrains.Where(d => d.IsFixture).ToList();
                if (fixtureDrains.Count > 0)
                {
                    payload.Progress?.Invoke(new RunProgressInfo("Adding shape points at drain families"));
                    FloorGeometryService.AddFixtureDrainPoints(floor, doc, floorData.TopFace, fixtureDrains, log);
                    floorData.TopFace = FloorGeometryService.GetTopFace(floor);
                    if (floorData.TopFace == null)
                        return Fail("The floor's top face could not be found after adding the drain points.", log, startTime, version);
                    payload.CancelToken.ThrowIfCancellationRequested();
                }

                SlabShapeEditor editor = floor.GetSlabShapeEditor();
                foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                    floorData.Vertices.Add(v);

                // ── 3. Drain points: every shape point on each opening edge ──
                payload.Progress?.Invoke(new RunProgressInfo("Matching drain points"));
                var detection = new DrainDetectionService();
                var unmatched = new List<string>();
                for (int i = 0; i < selectedDrains.Count; i++)
                {
                    DrainItem drain = selectedDrains[i];
                    drain.DrainVertices = drain.IsFixture
                        ? detection.FindShapeVerticesAtFixture(drain, floorData.Vertices)
                        : detection.FindShapeVerticesOnLoop(drain, floorData.Vertices);
                    if (drain.DrainVertices.Count == 0)
                        unmatched.Add($"#{i + 1} {DrainLabel(drain)}");
                }

                int drainPointCount = selectedDrains.SelectMany(d => d.DrainVertices).Distinct().Count();
                int usedDrains = selectedDrains.Count(d => d.DrainVertices.Count > 0);

                if (unmatched.Count > 0)
                    log(new LogEntry(LogLevel.Warning,
                        $"{unmatched.Count} selected drain(s) have no shape point on their edge and are ignored: {string.Join(", ", unmatched)}. " +
                        $"Check that the opening or drain family is on this floor ({DrainDetectionService.VertexMatchToleranceMm:0} mm tolerance for openings)."));

                if (drainPointCount == 0)
                    return Fail("None of the selected drains has a shape point on its edge, so there is nothing to slope towards. The floor was not changed.",
                        log, startTime, version);

                log(new LogEntry(LogLevel.Info,
                    $"{usedDrains} drain(s) → {drainPointCount} shape point(s) on their opening edges."));
                log(new LogEntry(LogLevel.Info, $"Applying {payload.SlopePercent}% slope..."));

                // ── 4. Paths, elevations, markers ───────────────────────────
                var slopeService = new FloorSlopeProcessorService();
                View activeView = app.ActiveUIDocument?.ActiveView;
                var results = slopeService.ProcessFloorSlopes(
                    floorData,
                    selectedDrains,
                    payload.SlopePercent,
                    msg => log(new LogEntry(LogLevel.Info, msg)),
                    payload.ConnectionThresholdMeters,
                    payload.PathSampleCount,
                    payload.InsertCurveIntersectionPoints,
                    payload.VerifyElevationsAfterCommit,
                    payload.ThresholdMeters,
                    activeView,
                    payload.DrainMarkerGroup,
                    payload.HighestPointMarkerGroup,
                    payload.AllowedOffsetMarkerGroup,
                    payload.AllowedOffsetThresholdMm,
                    payload.Progress,
                    payload.CancelToken);

                payload.CancelToken.ThrowIfCancellationRequested();

                var vertexData = slopeService.GetLastExportData() ?? new List<DrainVertexData>();
                int processedCount = vertexData.Count(v => v.WasProcessed);
                int skippedCount = vertexData.Count(v => !v.WasProcessed && !v.IsDrain);

                var endTime = DateTime.Now;
                int durationSec = (int)Math.Round((endTime - startTime).TotalSeconds);

                var metrics = new DrainExportMetrics
                {
                    ProcessedVertices = processedCount,
                    SkippedVertices = skippedCount,
                    DrainCount = usedDrains,
                    HighestElevationMm = results.maxOffset,
                    LongestPathM = results.longestPath,
                    SlopePercent = payload.SlopePercent,
                    RunDurationSec = durationSec,
                    RunDate = startTime.ToString("dd-MM-yy HH:mm"),
                    StartTime = startTime.ToString("HH:mm:ss"),
                    EndTime = endTime.ToString("HH:mm:ss"),
                    FloorId = floor.Id.Value.ToString(),
                    FloorName = floor.Name,
                    TotalDetectedCount = payload.TotalDetectedCount,
                    SelectedCount = selectedDrains.Count,
                    ArcsCalculated = slopeService.LastArcsCalculated,
                    Version = version,
                    UnreachableVertexCount = slopeService.LastUnreachableCount,
                    OverThresholdVertexCount = slopeService.LastOverThresholdCount
                };

                // ── 5. Floor parameters ──────────────────────────────────────
                payload.Progress?.Invoke(new RunProgressInfo("Writing floor parameters"));
                new AutoSlopeDrainParameterWriter().WriteAll(doc, floor, metrics, payload.SlopePercent,
                    payload.ConnectionThresholdMeters, version, msg => log(new LogEntry(LogLevel.Info, msg)));

                // ── 6. Excel ────────────────────────────────────────────────
                string exportedPath = null;
                if (payload.ExportConfig != null && payload.ExportConfig.ExportToExcel)
                {
                    payload.Progress?.Invoke(new RunProgressInfo("Exporting Excel workbook"));
                    System.IO.Directory.CreateDirectory(payload.ExportConfig.ExportPath);
                    exportedPath = ExcelExportService.ExportWorkbook(payload, vertexData, floor, payload.SlopePercent, metrics, log);
                    if (!string.IsNullOrEmpty(exportedPath))
                        log(new LogEntry(LogLevel.Success, $"Excel saved: {exportedPath}"));
                }

                endTime = DateTime.Now;
                double seconds = (endTime - startTime).TotalSeconds;

                log(new LogEntry(LogLevel.Success,
                    $"Done in {seconds:0.0} s: {processedCount} point(s) sloped, {skippedCount} left at 0 · " +
                    $"highest {results.maxOffset:0} mm · longest path {results.longestPath:0.00} m."));
                if (slopeService.LastDrainCirclesPlaced + slopeService.LastHighestCirclesPlaced + slopeService.LastOffsetCirclesPlaced > 0)
                    log(new LogEntry(LogLevel.Info,
                        $"Circle markers: {slopeService.LastDrainCirclesPlaced} drain, {slopeService.LastHighestCirclesPlaced} highest, {slopeService.LastOffsetCirclesPlaced} allowed offset."));

                return new AutoSlopeDrainResult
                {
                    Success = true,
                    VerticesModified = results.modifiedCount,
                    VerticesProcessed = processedCount,
                    VerticesSkipped = skippedCount,
                    DrainCount = usedDrains,
                    DrainPointCount = drainPointCount,
                    SlopePercent = payload.SlopePercent,
                    HighestElevation_mm = results.maxOffset,
                    LongestPath_m = results.longestPath,
                    RunDuration_sec = durationSec,
                    DurationSeconds = seconds,
                    StartedAt = startTime,
                    EndedAt = endTime,
                    RunDate = metrics.RunDate,
                    StartTime = metrics.StartTime,
                    EndTime = endTime.ToString("HH:mm:ss"),
                    Status = AppConstants.Status_OK,
                    Version = version,
                    TotalDetectedCount = payload.TotalDetectedCount,
                    SelectedCount = selectedDrains.Count,
                    ArcsCalculated = slopeService.LastArcsCalculated,
                    UnreachableVertexCount = slopeService.LastUnreachableCount,
                    OverThresholdVertexCount = slopeService.LastOverThresholdCount,
                    ExportedFilePath = exportedPath,
                    DrainCirclesPlaced = slopeService.LastDrainCirclesPlaced,
                    HighestCirclesPlaced = slopeService.LastHighestCirclesPlaced,
                    OffsetCirclesPlaced = slopeService.LastOffsetCirclesPlaced,
                    Vertices = vertexData
                };
            }
            catch (OperationCanceledException)
            {
                log(new LogEntry(LogLevel.Warning, "Cancelled."));
                return Fail("Cancelled by user.", log, startTime, version, wasCancelled: true, alreadyLogged: true);
            }
            catch (Exception ex)
            {
                return Fail($"Unexpected error: {ex.Message}", log, startTime, version);
            }
        }

        /// <summary>"⌀100" for a circle, the family name for a drain family, "600 x 600 mm" otherwise.</summary>
        public static string DrainLabel(DrainItem d) =>
            d == null ? "" : d.IsFixture ? (string.IsNullOrEmpty(d.FixtureName) ? "drain family" : d.FixtureName) : d.ShapeGroup == "Circle" ? $"⌀{d.Width:0}" : d.SizeCategory;

        private static AutoSlopeDrainResult Fail(string message, Action<LogEntry> log, DateTime startTime, string version,
            bool wasCancelled = false, bool alreadyLogged = false)
        {
            if (!alreadyLogged)
                log(new LogEntry(LogLevel.Error, message));

            var endTime = DateTime.Now;
            return new AutoSlopeDrainResult
            {
                Success = false,
                WasCancelled = wasCancelled,
                ErrorMessage = message,
                Status = AppConstants.Status_Failed,
                Version = version,
                StartedAt = startTime,
                EndedAt = endTime,
                StartTime = startTime.ToString("HH:mm:ss"),
                EndTime = endTime.ToString("HH:mm:ss"),
                DurationSeconds = (endTime - startTime).TotalSeconds,
                RunDuration_sec = (int)Math.Round((endTime - startTime).TotalSeconds)
            };
        }
    }
}
