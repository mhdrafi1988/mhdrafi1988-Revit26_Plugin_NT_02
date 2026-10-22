// =======================================================
// File: AutoSlopeRunner.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Infrastructure.RoofOps
// Replaces V030's 514-line AutoSlopeEngine with a thin orchestrator
// (plan 1.3). Steps, in order:
//   0. read-only checks before anything changes (D6)
//   1. RoofPreparer      reset, arc points, drain points
//   2. DrainResolver     exact drain vertex set (D1–D3)
//   3. PathGraphBuilder  + Core ShortestPathSolver (distance, drain, next hop)
//   4. ElevationApplier  + CircleMarkerService, one "Apply AutoSlope" tx
//   5. ModelReadback     read-back + adjustment warning (D7)
//   6. ParameterWriter, 7. Excel export, 8. result + summary
// Returns a result instead of reporting through callbacks; the handler
// decides Assimilate (success) or RollBack (cancel / failure) — B1.
// Every failure is logged exactly once, here (B11).
// =======================================================

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Core.Services;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Infrastructure.RoofOps
{
    internal static class AutoSlopeRunner
    {
        public static string ToolVersion =>
            ToolCatalog.Find(typeof(AutoSlopeRunner))?.Version ?? AutoSlopeConstants.FallbackToolVersion;

        public static AutoSlopeResult Run(
            UIApplication app,
            AutoSlopeRequest request,
            Action<RunProgressInfo> progress,
            Action<LogLevel, string> log,
            CancellationToken cancel)
        {
            DateTime start = DateTime.Now;
            try
            {
                return RunCore(app, request, progress, log, cancel, start);
            }
            catch (OperationCanceledException)
            {
                log(LogLevel.Warning, "Run cancelled. All changes are being rolled back.");
                return AutoSlopeResult.Cancelled(start);
            }
            catch (AutoSlopeAbortException ex)
            {
                log(LogLevel.Error, ex.Message + " Nothing was changed in the model.");
                return AutoSlopeResult.Failed(ex.Message, start);
            }
        }

        private static AutoSlopeResult RunCore(
            UIApplication app,
            AutoSlopeRequest request,
            Action<RunProgressInfo> progress,
            Action<LogLevel, string> log,
            CancellationToken cancel,
            DateTime start)
        {
            var clock = Stopwatch.StartNew();
            UIDocument uidoc = app.ActiveUIDocument ?? throw new AutoSlopeAbortException("No document is open.");
            Document doc = uidoc.Document;

            // ── 0. Read-only checks (D6) ───────────────────────────────────
            progress(RunPhases.Report(RunPhase.Checking));
            if (doc.GetElement(new ElementId(request.RoofIdValue)) is not RoofBase roof)
                throw new AutoSlopeAbortException("The roof was not found. It may have been deleted.");
            if (request.PickedPoints == null || request.PickedPoints.Count == 0)
                throw new AutoSlopeAbortException("No drain points are picked. Use Re-pick drains first.");

            SlabShapeEditor editor = RoofPreparer.GetEditor(roof);
            if (!editor.IsEnabled)
                throw new AutoSlopeAbortException("Shape editing is off for this roof. Close the window and run the tool again to turn it on.");
            if (RoofPreparer.Collect(editor).Count == 0)
                throw new AutoSlopeAbortException("The roof has no shape points.");
            if (AutoSlopeGeometry.GetTopFace(roof) == null)
                throw new AutoSlopeAbortException("The roof's top face could not be found.");

            RoofInfo roofInfo = ProjectQueries.DescribeRoof(roof);
            log(LogLevel.Info, $"Run started on roof {roofInfo.IdValue} ({roofInfo.TypeName}). Slope {request.SlopePercent}%, max drainage distance {request.MaxDrainDistanceM} m.");

            // ── 1. Prepare ─────────────────────────────────────────────────
            PreparedRoof prep = RoofPreparer.Prepare(doc, roof, request, progress, log, cancel);
            cancel.ThrowIfCancellationRequested();

            // ── 2. Drains ──────────────────────────────────────────────────
            progress(RunPhases.Report(RunPhase.ResolvingDrains));
            DrainResolution drains = DrainResolver.Resolve(request.PickedPoints, prep.Positions, prep.TopFace, request, log);
            if (drains.DrainVertices.Count == 0)
                throw new AutoSlopeAbortException("No roof shape point could be used as a drain.");

            // ── 3. Graph + shortest paths ──────────────────────────────────
            double maxEdgeFt = RevitConvert.MToFt(request.EffectiveMaxEdgeLengthM);
            double curveTolFt = RevitConvert.MmToFt(AutoSlopeConstants.CurveToleranceMm);
            var graphClock = Stopwatch.StartNew();
            var builder = new PathGraphBuilder(
                prep.Positions, prep.TopFace, maxEdgeFt, prep.Arcs, curveTolFt,
                pct => progress(RunPhases.Report(RunPhase.BuildingGraph, pct, prep.Positions.Count)),
                cancel);
            PathGraph graph = builder.Build();
            log(LogLevel.Info, $"Path graph: {graph.VertexCount} points, {graph.EdgeCount} links (max link {request.EffectiveMaxEdgeLengthM:0.##} m), built in {graphClock.Elapsed.TotalSeconds:0.0} s.");

            progress(RunPhases.Report(RunPhase.ComputingPaths));
            ShortestPathResult paths = ShortestPathSolver.Solve(graph, drains.DrainVertices, cancel);
            cancel.ThrowIfCancellationRequested();

            // ── 4. Apply + markers (one transaction, one undo step) ────────
            ApplyOutcome applied;
            MarkerCounts markers;
            var drainPoints = drains.DrainVertices.Select(i => prep.Positions[i].ToPoint3()).ToList();
            using (var tx = new Transaction(doc, "Apply AutoSlope"))
            {
                tx.Start();
                applied = ElevationApplier.Apply(
                    prep.Editor, prep.Vertices, prep.Positions, paths, drains.DrainVertices, request,
                    pct => progress(RunPhases.Report(RunPhase.ApplyingElevations, pct, prep.Positions.Count)),
                    cancel);

                progress(RunPhases.Report(RunPhase.PlacingMarkers));
                markers = CircleMarkerService.PlaceMarkers(doc, uidoc.ActiveView, drainPoints, applied.Vertices, request, log);

                cancel.ThrowIfCancellationRequested();
                tx.Commit();
            }

            int skipped = applied.BeyondDistance + applied.NoPath;
            log(LogLevel.Success, $"Applied {request.SlopePercent}% slope to {applied.Processed} point(s).");
            if (applied.BeyondDistance > 0)
                log(LogLevel.Warning, $"{applied.BeyondDistance} point(s) are further than {request.MaxDrainDistanceM} m from every drain and were left at 0. Increase the drainage distance to include them.");
            if (applied.NoPath > 0)
                log(LogLevel.Warning, $"{applied.NoPath} point(s) have no valid path to any drain and were left at 0. Check openings or the max link length.");

            // ── 5. Read back ───────────────────────────────────────────────
            progress(RunPhases.Report(RunPhase.ReadingBack));
            ReadbackOutcome readback = ModelReadback.Read(roof, applied.Vertices, drains.DrainVertices);
            if (readback.Unmatched > 0)
                log(LogLevel.Warning, $"{readback.Unmatched} point(s) could not be read back; their calculated value is used.");
            if (readback.AdjustedCount > 0)
                log(LogLevel.Warning, $"Revit adjusted {readback.AdjustedCount} point(s) by up to {readback.MaxAdjustmentMm:0} mm. The Highest Point marker uses the calculated value; the header tile uses the model value.");

            // ── 6. Parameters ──────────────────────────────────────────────
            progress(RunPhases.Report(RunPhase.WritingParameters));
            string version = ToolVersion;
            double durationSoFar = clock.Elapsed.TotalSeconds;
            ParameterWriteOutcome param = AutoSlopeParameterWriter.WriteAll(
                doc, roof, request,
                readback.HighestElevationMm, applied.LongestPathFt,
                applied.Processed, skipped, (int)Math.Round(durationSoFar),
                drains.DrainVertices.Count, start.ToString("dd-MM-yy HH:mm"), version);
            log(LogLevel.Info, $"Roof parameters: {param.Written} updated, {param.Skipped} not found or read-only.");

            // ── 7. Result ──────────────────────────────────────────────────
            var result = new AutoSlopeResult
            {
                Success = true,
                VerticesProcessed = applied.Processed,
                VerticesSkipped = skipped,
                VerticesUnreached = applied.NoPath,
                PickedDrainCount = drains.PickedCount,
                FinalDrainCount = drains.DrainVertices.Count,
                NearbyDrainPointsAdded = drains.NearbyAdded,
                HighestElevationMm = readback.HighestElevationMm,
                LongestPathM = Math.Round(RevitConvert.FtToM(applied.LongestPathFt), 2, MidpointRounding.AwayFromZero),
                StartTime = start,
                ArcsFound = prep.ArcsFound,
                ArcPointsInserted = prep.ArcPointsInserted,
                GraphEdges = graph.EdgeCount,
                ToolVersion = version,
                ParameterStatus = param.Status,
                ParametersWritten = param.Written,
                ParametersSkipped = param.Skipped,
                DrainCirclesPlaced = markers.Drain,
                HighestCirclesPlaced = markers.Highest,
                OffsetCirclesPlaced = markers.Offset,
                AdjustedByRevitCount = readback.AdjustedCount,
                MaxAdjustmentMm = readback.MaxAdjustmentMm,
                Vertices = applied.Vertices,
                Roof = roofInfo,
                Request = request
            };

            // ── 8. Excel ───────────────────────────────────────────────────
            if (request.ExportToExcel)
            {
                progress(RunPhases.Report(RunPhase.Exporting));
                result.EndTime = DateTime.Now;
                result.DurationSeconds = clock.Elapsed.TotalSeconds;
                result.ExportedFilePath = ExcelExportService.ExportAuto(result, log);
            }

            result.EndTime = DateTime.Now;
            result.DurationSeconds = clock.Elapsed.TotalSeconds;

            log(LogLevel.Success,
                $"Finished. Start {result.StartTime:HH:mm:ss} · End {result.EndTime:HH:mm:ss} · Total {result.DurationSeconds:0.0} s. " +
                $"{result.VerticesProcessed} processed, {result.VerticesSkipped} skipped, highest {result.HighestElevationMm:0} mm, longest path {result.LongestPathM:0.00} m.");
            return result;
        }
    }
}
