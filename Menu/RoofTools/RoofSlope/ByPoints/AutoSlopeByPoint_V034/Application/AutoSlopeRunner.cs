// =======================================================
// File: AutoSlopeRunner.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application
// V034: the orchestrator moved out of Infrastructure into the Application
// layer. Same steps, same order, same messages as V033's runner:
//   0. read-only checks before anything changes (D6)
//   1. prepare           reset, arc points, drain points
//   2. drains            exact drain vertex set (D1–D3)
//   3. graph             + Core ShortestPathSolver (distance, drain, next hop)
//   3b. ridges           Core RidgeFinder (when ridge lines are on)
//   4. elevations + circle markers + ridge lines, in one "Apply AutoSlope"
//      change (one undo step)
//   5. read-back + adjustment warning (D7)
//   6. roof parameters, 7. result, 8. Excel export
// What changed: the runner no longer touches the Revit API. Every Revit step
// goes through an interface in Application/Ports; RunJob plugs in the Revit
// implementations (Infrastructure/Adapters), unit tests plug in fakes.
// The handler still decides Assimilate (success) or RollBack (cancel /
// failure) — B1. Every failure is logged exactly once, here (B11).
// =======================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application
{
    /// <summary>
    /// Runs one Auto Slope job: the order of the steps, progress, cancel and the mapping of
    /// expected failures to a result. Uses no Revit API; the model work is done by the
    /// interfaces passed to the constructor.
    /// </summary>
    public sealed class AutoSlopeRunner
    {
        private readonly IRoofModel _roof;
        private readonly IAnnotationPlacer _annotations;
        private readonly IChangeScopeFactory _changes;
        private readonly IResultExporter _exporter;
        private readonly ILengthUnits _units;
        private readonly string _toolVersion;

        /// <summary>Creates a runner for one roof. All arguments are required.</summary>
        public AutoSlopeRunner(
            IRoofModel roof,
            IAnnotationPlacer annotations,
            IChangeScopeFactory changes,
            IResultExporter exporter,
            ILengthUnits units,
            string toolVersion)
        {
            _roof = roof ?? throw new ArgumentNullException(nameof(roof));
            _annotations = annotations ?? throw new ArgumentNullException(nameof(annotations));
            _changes = changes ?? throw new ArgumentNullException(nameof(changes));
            _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _toolVersion = toolVersion ?? string.Empty;
        }

        /// <summary>
        /// Runs the job. Returns a cancelled or failed result for a cancel or an expected
        /// failure (<see cref="AutoSlopeAbortException"/>); anything else propagates to the caller.
        /// </summary>
        public AutoSlopeResult Run(
            AutoSlopeRequest request,
            Action<RunProgressInfo> progress,
            Action<LogLevel, string> log,
            CancellationToken cancel)
        {
            progress ??= _ => { };
            log ??= (_, __) => { };
            DateTime start = DateTime.Now;
            try
            {
                return RunCore(request, progress, log, cancel, start);
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

        private AutoSlopeResult RunCore(
            AutoSlopeRequest request,
            Action<RunProgressInfo> progress,
            Action<LogLevel, string> log,
            CancellationToken cancel,
            DateTime start)
        {
            var clock = Stopwatch.StartNew();
            if (request == null) throw new AutoSlopeAbortException("No run settings were given.");

            // ── 0. Read-only checks (D6) ───────────────────────────────────
            progress(RunPhases.Report(RunPhase.Checking));
            string notReady = _roof.CheckReady(request);
            if (notReady != null) throw new AutoSlopeAbortException(notReady);

            RoofInfo roofInfo = _roof.Describe();
            log(LogLevel.Info, $"Run started on roof {roofInfo.IdValue} ({roofInfo.TypeName}). Slope {request.SlopePercent}%, max drainage distance {request.MaxDrainDistanceM} m.");

            // ── 1. Prepare ─────────────────────────────────────────────────
            PreparedRoofData prep = _roof.Prepare(request, progress, log, cancel);
            cancel.ThrowIfCancellationRequested();

            // ── 2. Drains ──────────────────────────────────────────────────
            progress(RunPhases.Report(RunPhase.ResolvingDrains));
            DrainResolution drains = _roof.ResolveDrains(request, log);
            if (drains.DrainVertices.Count == 0)
                throw new AutoSlopeAbortException("No roof shape point could be used as a drain.");

            // ── 3. Graph + shortest paths ──────────────────────────────────
            var graphClock = Stopwatch.StartNew();
            PathGraph graph = _roof.BuildGraph(request,
                pct => progress(RunPhases.Report(RunPhase.BuildingGraph, pct)), cancel);
            log(LogLevel.Info, $"Path graph: {graph.VertexCount} points, {graph.EdgeCount} links (max link {request.EffectiveMaxEdgeLengthM:0.##} m), built in {graphClock.Elapsed.TotalSeconds:0.0} s.");

            progress(RunPhases.Report(RunPhase.ComputingPaths));
            ShortestPathResult paths = ShortestPathSolver.Solve(graph, drains.DrainVertices, cancel);
            cancel.ThrowIfCancellationRequested();

            // ── 3b. Ridge lines: worked out here, drawn inside the change ──
            IReadOnlyList<Point3> ridgePositions = null;
            RidgeResult ridges = null;
            if (request.DrawRidgeLines)
            {
                ridgePositions = prep.Positions;
                ridges = FindRidges(ridgePositions, graph, paths, drains, request, log, cancel);
            }

            // ── 4. Apply + markers (one change, one undo step) ─────────────
            ApplyOutcome applied;
            MarkerCounts markers;
            RidgeLineOutcome ridgeLines = null;
            var drainPoints = drains.DrainVertices.Select(i => prep.Positions[i]).ToList();

            // Outer boundary corners for the corner Offset notes (32.3.0), read before the roof changes.
            MarkerStyle highStyle = request.Markers?.FirstOrDefault(m => m.Kind == MarkerGroupKind.HighestPoint);
            if (highStyle is { IsEnabled: true, ShowOffsetText: true, ShowCornerOffsetText: true })
            {
                try
                {
                    request = request with
                    {
                        BoundaryCorners = _roof.GetOuterBoundaryCorners(AutoSlopeConstants.CornerMinAngleDeg).ToList()
                    };
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Not fatal: the marker step logs that the corner notes were skipped.
                    log(LogLevel.Warning, $"Offset notes: the roof's outer boundary could not be read ({ex.Message}).");
                }
            }

            using (IChangeScope change = _changes.Begin("Apply AutoSlope"))
            {
                applied = _roof.ApplyElevations(paths, drains.DrainVertices, request,
                    pct => progress(RunPhases.Report(RunPhase.ApplyingElevations, pct)),
                    cancel);

                progress(RunPhases.Report(RunPhase.PlacingMarkers));
                markers = _annotations.PlaceMarkers(drainPoints, applied.Vertices, request, log);

                if (ridges != null)
                {
                    progress(RunPhases.Report(RunPhase.PlacingRidgeLines));
                    ridgeLines = _annotations.PlaceRidgeLines(ridgePositions, ridges, request, log);
                }

                cancel.ThrowIfCancellationRequested();
                change.Commit();
            }

            int skipped = applied.BeyondDistance + applied.NoPath;
            log(LogLevel.Success, $"Applied {request.SlopePercent}% slope to {applied.Processed} point(s).");
            if (ridgeLines?.Placed > 0)
                log(LogLevel.Success, $"Ridge lines: {ridgeLines.Placed} detail line(s) for {ridges.RidgeCount} ridge(s) between {drains.GroupCount} drain groups, line style “{ridgeLines.StyleUsed}”.");
            if (applied.BeyondDistance > 0)
                log(LogLevel.Warning, $"{applied.BeyondDistance} point(s) are further than {request.MaxDrainDistanceM} m from every drain and were left at 0. Increase the drainage distance to include them.");
            if (applied.NoPath > 0)
                log(LogLevel.Warning, $"{applied.NoPath} point(s) have no valid path to any drain and were left at 0. Check openings or the max link length.");

            // ── 5. Read back ───────────────────────────────────────────────
            progress(RunPhases.Report(RunPhase.ReadingBack));
            ReadbackOutcome readback = _roof.ReadBack(applied.Vertices, drains.DrainVertices);
            if (readback.Unmatched > 0)
                log(LogLevel.Warning, $"{readback.Unmatched} point(s) could not be read back; their calculated value is used.");
            if (readback.AdjustedCount > 0)
                log(LogLevel.Warning, $"Revit adjusted {readback.AdjustedCount} point(s) by up to {readback.MaxAdjustmentMm:0} mm. The Highest Point marker uses the calculated value; the header tile uses the model value.");

            // ── 6. Parameters ──────────────────────────────────────────────
            progress(RunPhases.Report(RunPhase.WritingParameters));
            double durationSoFar = clock.Elapsed.TotalSeconds;
            ParameterWriteOutcome param = _roof.WriteParameters(
                request,
                readback.HighestElevationMm, applied.LongestPathFt,
                applied.Processed, skipped, (int)Math.Round(durationSoFar),
                drains.DrainVertices.Count, start.ToString("dd-MM-yy HH:mm"), _toolVersion);
            log(LogLevel.Info, $"Roof parameters: {param.Written} updated, {param.Skipped} not written.");
            foreach (string failure in param.Failures)
                log(LogLevel.Warning, $"Roof parameter not written – {failure}");

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
                LongestPathM = Math.Round(_units.FtToM(applied.LongestPathFt), 2, MidpointRounding.AwayFromZero),
                StartTime = start,
                ArcsFound = prep.ArcsFound,
                ArcPointsInserted = prep.ArcPointsInserted,
                GraphEdges = graph.EdgeCount,
                ToolVersion = _toolVersion,
                ParameterStatus = param.Status,
                ParametersWritten = param.Written,
                ParametersSkipped = param.Skipped,
                DrainCirclesPlaced = markers.Drain,
                HighestCirclesPlaced = markers.Highest,
                OffsetCirclesPlaced = markers.Offset,
                RidgeLinesPlaced = ridgeLines?.Placed ?? 0,
                RidgeCount = ridgeLines?.Placed > 0 ? ridges.RidgeCount : 0,
                RidgeLineStyleUsed = ridgeLines?.StyleUsed,
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
                result.ExportedFilePath = _exporter.ExportAuto(result, log);
            }

            result.EndTime = DateTime.Now;
            result.DurationSeconds = clock.Elapsed.TotalSeconds;

            log(LogLevel.Success,
                $"Finished. Start {result.StartTime:HH:mm:ss} · End {result.EndTime:HH:mm:ss} · Total {result.DurationSeconds:0.0} s. " +
                $"{result.VerticesProcessed} processed, {result.VerticesSkipped} skipped, highest {result.HighestElevationMm:0} mm, longest path {result.LongestPathM:0.00} m.");
            return result;
        }

        /// <summary>
        /// Finds the ridges between drain groups from this run's values. Returns null (with a
        /// log line) when there is nothing to draw or the ridges could not be worked out; the slope
        /// is applied either way.
        /// </summary>
        private RidgeResult FindRidges(
            IReadOnlyList<Point3> positions,
            PathGraph graph,
            ShortestPathResult paths,
            DrainResolution drains,
            AutoSlopeRequest request,
            Action<LogLevel, string> log,
            CancellationToken cancel)
        {
            log(LogLevel.Info, $"Drain groups: {drains.PickedCount} pick(s) → {drains.GroupCount} group(s). Picks that share a shape point are one group.");
            if (drains.GroupCount < 2)
            {
                log(LogLevel.Warning, $"Ridge lines skipped: they need 2 or more drain groups ({drains.GroupCount} found).");
                return null;
            }

            int n = positions.Count;
            double maxDistFt = _units.MToFt(request.MaxDrainDistanceM);
            double slopeFactor = request.SlopePercent / 100.0;
            var groupOf = new int[n];
            var elevation = new double[n];
            var isDrain = new bool[n];
            for (int i = 0; i < n; i++)
            {
                double d = paths.Distance[i];
                int source = paths.SourceDrain[i];
                int group = -1;
                bool sloped = !double.IsInfinity(d) && d <= maxDistFt && source >= 0
                              && drains.GroupOfDrain.TryGetValue(source, out group);
                groupOf[i] = sloped ? group : -1;
                elevation[i] = sloped ? d * slopeFactor : 0;   // same value the elevation step writes
                isDrain[i] = drains.GroupOfDrain.ContainsKey(i);
            }

            try
            {
                var clock = Stopwatch.StartNew();
                RidgeResult ridges = RidgeFinder.Find(new RidgeInput
                {
                    Positions = positions,
                    Graph = graph,
                    GroupOf = groupOf,
                    Elevation = elevation,
                    IsDrain = isDrain,
                    TieTolerance = _units.MmToFt(AutoSlopeConstants.RidgeTieToleranceMm)
                }, cancel);

                if (ridges.Lines.Count == 0)
                {
                    log(LogLevel.Info, "Ridge lines: no ridge was found between the drain groups.");
                    return null;
                }
                log(LogLevel.Info, $"Ridge lines: {ridges.RidgePoints.Count} ridge point(s), {ridges.Junctions.Count} junction(s), {ridges.Lines.Count} line(s), found in {clock.Elapsed.TotalSeconds:0.0} s.");
                return ridges;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log(LogLevel.Warning, $"Ridge lines skipped: the ridges could not be worked out ({ex.Message}). The slope is applied as usual.");
                return null;
            }
        }
    }
}
