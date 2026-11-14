// File: FloorSlopeProcessorService.cs
// Location: Core/Services/
// Base: ported from AutoSlopeByDrain V004 (roof) (DijkstraPathEngine-based path
// solving, straight-line primary / tangent-arc fallback).
//
// NOTE (unchanged from V004): ByPoint's tangent-arc math has a known,
// unresolved bug (arc-adjacent vertices can get an incorrect elevation) —
// ported here as-is, not fixed, since this is out of scope for the merge.
//
// NEW (V005), per Rafi's confirmed decisions on 2026-07-21:
//   - Curve-intersection point insertion (opt-in, off by default) — ported
//     from AutoSlopeByPoint's engine, wired in via InsertCurveIntersectionPoints
//     on the payload. Runs BEFORE the connectivity graph is built, with the
//     full re-fetch-fresh-handles cycle after the insertion transaction
//     commits (doc.Regenerate() invalidates the SlabShapeEditor + topFace).
//   - Elevation read-back verification (opt-in, OFF by default) — re-reads
//     each processed vertex's elevation from the model after Commit() and
//     populates DrainVertexData.ElevationFromModel_mm / ElevationDiff_mm.
//   - ArcsCalculated and UnreachableVertexCount surfaced for the result/UI
//     metrics (the unreachable-count LOG line already existed in V004; it's
//     now also captured as a number instead of just logged text).

// V013 changes:
//   - drain points re-matched after curve-point insertion with
//     FindShapeVerticesOnLoop (every point on the opening edge)
//   - over-distance points keep their path length and a SkipReason, and
//     drain points are flagged IsDrain, so "skipped" no longer counts the
//     drain points themselves
//   - read-back verification uses a plan-position lookup (was every point
//     against every point) and the positions captured before commit, not
//     the pre-commit SlabShapeVertex handles
//   - offset-circle counter reset per run

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Engine;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Services
{
    public class FloorSlopeProcessorService
    {
        private List<DrainVertexData> _lastExportData;
        private int _lastRunDuration;
        private int _lastArcsCalculated;
        private int _lastUnreachableCount;
        private int _lastOverThresholdCount;
        private int _lastDrainCirclesPlaced;
        private int _lastHighestCirclesPlaced;
        private int _lastOffsetCirclesPlaced;

        public int LastRunDuration => _lastRunDuration;

        /// <summary>Boundary/opening arcs found during this run. 0 if curve-intersection insertion was off or none were found.</summary>
        public int LastArcsCalculated => _lastArcsCalculated;

        /// <summary>Vertices that had no path to any selected drain at all (disconnected graph), not just "too far".</summary>
        public int LastUnreachableCount => _lastUnreachableCount;

        /// <summary>NEW (V005): vertices where a path existed but exceeded Max Path Distance.</summary>
        public int LastOverThresholdCount => _lastOverThresholdCount;

        /// <summary>Circles placed on selected drain openings this run. 0 if the marker group was disabled/absent or the view wasn't a plan view.</summary>
        public int LastDrainCirclesPlaced => _lastDrainCirclesPlaced;

        /// <summary>Circles placed on vertices tied at max elevation this run. 0 if the marker group was disabled/absent or the view wasn't a plan view.</summary>
        public int LastHighestCirclesPlaced => _lastHighestCirclesPlaced;

        /// <summary>Circles placed on processed vertices meeting the Allowed Offset threshold this run. 0 if the marker group was disabled/absent or the view wasn't a plan view.</summary>
        public int LastOffsetCirclesPlaced => _lastOffsetCirclesPlaced;

        /// <summary>
        /// Process slopes for the floor.
        /// </summary>
        /// <param name="connectionThresholdMeters">Max Edge Distance — gates graph edge creation, not final path length.</param>
        /// <param name="pathSampleCount">Interior points sampled per candidate edge to verify it lies on the floor face. Recommended range: 5-20.</param>
        /// <param name="insertCurveIntersectionPoints">NEW (V005). Opt-in: insert real vertices at boundary/opening arc crossings before pathfinding.</param>
        /// <param name="verifyElevationsAfterCommit">NEW (V005). Opt-in, off by default: re-read each processed vertex's elevation from the model after Commit() to detect silent Revit adjustments.</param>
        /// <param name="thresholdMeters">
        ///     NEW (V005). Max Path Distance — max TOTAL (multi-hop) path length from a
        ///     vertex to its nearest selected drain. Checked INSIDE the transaction,
        ///     before any elevation is written, so an over-threshold vertex is truly
        ///     skipped (no elevation change at all) rather than merely mislabeled
        ///     after the fact. Pass 0 or less to disable (no path-length cutoff beyond
        ///     what Max Edge Distance already implies).
        /// </param>
        public (int modifiedCount, double maxOffset, double longestPath) ProcessFloorSlopes(
            FloorData floorData,
            List<DrainItem> selectedDrains,
            double slopePercentage,
            Action<string> logAction,
            double connectionThresholdMeters = 30.0,
            int pathSampleCount = 10,
            bool insertCurveIntersectionPoints = false,
            bool verifyElevationsAfterCommit = false,
            double thresholdMeters = 0,
            View activeView = null,
            CircleMarkerGroup drainMarkerGroup = null,
            CircleMarkerGroup highestPointMarkerGroup = null,
            CircleMarkerGroup allowedOffsetMarkerGroup = null,
            double allowedOffsetThresholdMm = 0,
            Action<RunProgressInfo> onProgress = null,
            CancellationToken cancelToken = default)
        {
            var doc = floorData.Floor.Document;
            int modifiedCount = 0;
            double maxOffset = 0;
            double longestPath = 0;
            DateTime startTime = DateTime.Now;
            _lastArcsCalculated = 0;
            _lastUnreachableCount = 0;
            _lastOverThresholdCount = 0;
            _lastDrainCirclesPlaced = 0;
            _lastHighestCirclesPlaced = 0;
            _lastOffsetCirclesPlaced = 0;
            _overThresholdDistances = new Dictionary<SlabShapeVertex, double>();

            var topFace = floorData.TopFace;
            var vertexList = floorData.Vertices;

            // ── NEW (V005): opt-in curve-intersection point insertion ─────────
            // Runs in its own transaction BEFORE the main slope transaction, since
            // it changes floor geometry (adds shape editor points) and requires a
            // regenerate + fresh-handle re-fetch, per this suite's standing rule
            // that doc.Regenerate() invalidates SlabShapeEditor handles.
            double curveTolFt = UnitUtils.ConvertToInternalUnits(2.0, UnitTypeId.Millimeters);
            List<Arc> boundaryArcs = FloorBoundaryHelper.GetBoundaryArcs(topFace);

            if (insertCurveIntersectionPoints)
            {
                onProgress?.Invoke(new RunProgressInfo("Inserting curve intersection points"));
                if (boundaryArcs.Count == 0)
                {
                    logAction("Curve intersection check enabled, but no arc edges found on this floor.");
                }
                else
                {
                    var positions = new List<XYZ>(vertexList.Count);
                    foreach (var v in vertexList) positions.Add(v.Position);

                    List<Line> boundaryLines = FloorBoundaryHelper.GetBoundaryLines(topFace);

                    int insertedCount;
                    var editor = floorData.Floor.GetSlabShapeEditor();
                    using (Transaction tx = new Transaction(doc, "Insert Curve Intersection Points"))
                    {
                        tx.Start();
                        insertedCount = CurveIntersectionHelper.InsertIntersectionPoints(
                            editor, boundaryLines, positions, boundaryArcs, curveTolFt, logAction);
                        tx.Commit();
                    }

                    if (insertedCount > 0)
                    {
                        // tx.Commit() above already regenerated the document, which
                        // invalidates the SlabShapeEditor handle acquired before it —
                        // re-fetch fresh handles before touching geometry again.
                        editor = floorData.Floor.GetSlabShapeEditor();
                        if (editor == null || !editor.IsValidObject)
                        {
                            logAction("ERROR: Slab shape editor became invalid after inserting curve points. Aborting.");
                            throw new InvalidOperationException("Slab shape editor became invalid after inserting curve points.");
                        }

                        vertexList = new List<SlabShapeVertex>();
                        foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                            vertexList.Add(v);
                        floorData.Vertices = vertexList;

                        // The drain vertices matched by AutoSlopeDrainEngine are handles
                        // into the PRE-insert vertex list; the regenerate above invalidated
                        // them, so the refreshed list holds different SlabShapeVertex
                        // objects. Re-match each selected drain's loop points against the
                        // fresh list (same X/Y tolerance as the engine) or the drain
                        // set below would be stale/empty and every vertex unreachable.
                        var rematchService = new DrainDetectionService();
                        foreach (var drain in selectedDrains)
                        {
                            var loopPoints = new List<XYZ>();
                            if (drain.LoopCurves != null)
                                foreach (var curve in drain.LoopCurves)
                                    loopPoints.Add(curve.GetEndPoint(0));

                            drain.DrainVertices = rematchService.FindShapeVerticesOnLoop(drain, vertexList);
                        }
                        logAction($"Re-matched drain vertices after curve-point insertion: {selectedDrains.Sum(d => d.DrainVertices.Count)} vertex/vertices across {selectedDrains.Count} drain(s).");

                        topFace = GetTopFaceFresh(floorData.Floor);
                        if (topFace == null)
                        {
                            logAction("ERROR: Top face not found after inserting curve points. Aborting.");
                            throw new InvalidOperationException("Top face not found after inserting curve points.");
                        }
                        floorData.TopFace = topFace;
                        boundaryArcs = FloorBoundaryHelper.GetBoundaryArcs(topFace);
                    }
                }
            }

            _lastArcsCalculated = boundaryArcs.Count;

            using (var transaction = new Transaction(doc, "Auto Floor Sloper - Apply Slopes"))
            {
                transaction.Start();

                try
                {
                    cancelToken.ThrowIfCancellationRequested();

                    logAction($"Building connectivity graph (max edge distance: {connectionThresholdMeters:F1} m, samples per edge: {pathSampleCount})...");
                    logAction($"Found {boundaryArcs.Count} boundary/opening arc(s) for tangent-route fallback.");
                    onProgress?.Invoke(new RunProgressInfo("Building path graph"));

                    double connectionThresholdFeet = connectionThresholdMeters / 0.3048;
                    var pathEngine = new DijkstraPathEngine(
                        vertexList, topFace, connectionThresholdFeet, boundaryArcs,
                        curveTolFt, pathSampleCount,
                        onBuildGraphProgress: pct => onProgress?.Invoke(new RunProgressInfo("Building path graph", pct)),
                        cancelToken: cancelToken);

                    cancelToken.ThrowIfCancellationRequested();

                    // Step 1: Get all drain vertices from SELECTED drains
                    var selectedDrainVertices = new HashSet<SlabShapeVertex>();
                    foreach (var drain in selectedDrains)
                    {
                        foreach (var vertex in drain.DrainVertices)
                        {
                            selectedDrainVertices.Add(vertex);
                        }
                        logAction($"Drain {drain.SizeCategory} has {drain.DrainVertices.Count} vertices within 5mm tolerance");
                    }

                    // Captured BEFORE zeroing, for the post-commit verification pass —
                    // ElevationOffsetMm is relative to this baseline, not absolute Z=0,
                    // matching how AutoSlopeByPoint derives drainBaselineZFt.
                    _lastDrainBaselineZFt = selectedDrainVertices.Count > 0
                        ? selectedDrainVertices.Average(v => v.Position.Z)
                        : 0;

                    logAction($"Found {selectedDrainVertices.Count} vertices on selected drain loops - setting to ZERO elevation");
                    SetDrainLoopVerticesToZero(floorData.Floor, selectedDrainVertices, logAction);
                    modifiedCount += selectedDrainVertices.Count;

                    // Step 2: map vertex <-> index, and vertex -> owning DrainItem
                    var drainVertexIndices = new HashSet<int>();
                    var vertexToDrain = new Dictionary<SlabShapeVertex, DrainItem>();

                    for (int i = 0; i < vertexList.Count; i++)
                        if (selectedDrainVertices.Contains(vertexList[i]))
                            drainVertexIndices.Add(i);

                    foreach (var drain in selectedDrains)
                        foreach (var v in drain.DrainVertices)
                            vertexToDrain[v] = drain;

                    logAction($"Computing multi-source shortest paths to {drainVertexIndices.Count} drain vertices for {vertexList.Count} floor vertices...");
                    onProgress?.Invoke(new RunProgressInfo("Computing shortest paths"));

                    double[] distances = pathEngine.ComputeAllDistances(drainVertexIndices);

                    int onArc = 0;
                    for (int i = 0; i < vertexList.Count; i++)
                        if (pathEngine.IsOnArc(i)) onArc++;
                    if (onArc > 0)
                        logAction($"{onArc} vertex(es) sit on a boundary/opening arc within tolerance.");

                    var pathResults = BuildPathResultsFromEngine(vertexList, distances, drainVertexIndices, vertexToDrain, pathEngine, logAction);

                    // ── NEW (V005): Max Path Distance (ThresholdMeters) ────────────
                    // Applied HERE, before any elevation is written — removes
                    // over-threshold entries from pathResults entirely, the same way
                    // unreachable vertices never entered pathResults in the first
                    // place (see BuildPathResultsFromEngine). This guarantees a
                    // vertex that fails this check gets NO elevation change and is
                    // correctly reported as skipped in the export — not written to
                    // the model and then mislabeled afterward.
                    int overThresholdCount = 0;
                    if (thresholdMeters > 0)
                    {
                        var overThreshold = pathResults
                            .Where(kvp => kvp.Value.drain != null && kvp.Value.totalDistance * 0.3048 > thresholdMeters)
                            .Select(kvp => kvp.Key)
                            .ToList();

                        foreach (var vertex in overThreshold)
                        {
                            _overThresholdDistances[vertex] = pathResults[vertex].totalDistance;
                            pathResults.Remove(vertex);
                        }

                        overThresholdCount = overThreshold.Count;
                        if (overThresholdCount > 0)
                        {
                            logAction($"WARNING: {overThresholdCount} vertex(es) exceeded Max Path Distance ({thresholdMeters:F1} m total path) and were skipped (no elevation change applied).");
                        }
                    }
                    _lastOverThresholdCount = overThresholdCount;

                    logAction("Applying elevations based on path distances...");
                    onProgress?.Invoke(new RunProgressInfo("Applying elevations"));
                    int slopeModifiedCount = ApplyElevationsWithDrainHierarchy(
                        floorData.Floor, pathResults, selectedDrainVertices, slopePercentage,
                        logAction, out maxOffset, out longestPath);
                    modifiedCount += slopeModifiedCount;

                    // Collect export data (elevation read-back happens AFTER commit, below)
                    _lastExportData = CollectVertexExportData(pathResults, selectedDrainVertices, vertexList, selectedDrains, slopePercentage);
                    _lastRunDuration = (int)(DateTime.Now - startTime).TotalSeconds;

                    // ── Circle Markers — ported from AutoSlopeByPoint ──────────────
                    // Runs inside this same transaction (one commit, one undo step
                    // for the whole Run), same reasoning as ByPoint's CircleMarkerService.
                    if ((drainMarkerGroup != null && drainMarkerGroup.IsEnabled) ||
                        (highestPointMarkerGroup != null && highestPointMarkerGroup.IsEnabled) ||
                        (allowedOffsetMarkerGroup != null && allowedOffsetMarkerGroup.IsEnabled))
                    {
                        onProgress?.Invoke(new RunProgressInfo("Placing circle markers"));
                        var drainCenterPoints = selectedDrains.Select(d => d.CenterPoint).Where(p => p != null).ToList();
                        var markerCounts = CircleMarkerService.PlaceMarkers(
                            doc,
                            activeView,
                            drainCenterPoints,
                            _lastExportData,
                            drainMarkerGroup,
                            highestPointMarkerGroup,
                            allowedOffsetMarkerGroup,
                            allowedOffsetThresholdMm,
                            clusterToleranceMm: 0,
                            log: logAction);

                        _lastDrainCirclesPlaced = markerCounts.DrainCirclesPlaced;
                        _lastHighestCirclesPlaced = markerCounts.HighestCirclesPlaced;
                        _lastOffsetCirclesPlaced = markerCounts.OffsetCirclesPlaced;
                    }

                    transaction.Commit();

                    logAction($"SUCCESS: Set {selectedDrainVertices.Count} drain vertices to zero + modified {slopeModifiedCount} slope vertices");
                    logAction($"Maximum offset: {maxOffset:F1} mm");
                    logAction($"Longest drainage path: {longestPath:F2} meters");
                }
                catch (Exception ex)
                {
                    transaction.RollBack();
                    logAction($"ERROR: Transaction rolled back - {ex.Message}");
                    throw;
                }
            }

            // ── NEW (V005): elevation read-back verification ───────────────────
            // Runs OUTSIDE the main transaction (it already committed above) — this
            // is a read-only re-fetch, not a modification, so no transaction is
            // needed. Off by default; only runs when the toggle is on.
            if (verifyElevationsAfterCommit && _lastExportData != null && _lastExportData.Count > 0)
            {
                VerifyElevationsAfterCommit(floorData.Floor, vertexList, _lastDrainBaselineZFt, _lastExportData, logAction);
            }

            return (modifiedCount, maxOffset, longestPath);
        }

        // NEW (V013): over-distance points keep their path length for the export / results panel.
        private Dictionary<SlabShapeVertex, double> _overThresholdDistances = new Dictionary<SlabShapeVertex, double>();

        public List<DrainVertexData> GetLastExportData()
        {
            return _lastExportData;
        }

        // Captured during the main transaction so the post-commit verification
        // pass (below) can compute each vertex's offset relative to the drain
        // baseline, the same way the elevations were originally calculated.
        private double _lastDrainBaselineZFt;

        /// <summary>
        /// NEW (V005), ported from AutoSlopeByPoint's post-commit re-read logic
        /// (adapted here for ByDrain's DrainVertexData / HashSet-of-vertices shape).
        /// Revit does not guarantee SlabShapeEditor.SlabShapeVertices index order is
        /// stable across a re-fetch after Commit(), so each pre-commit vertex is
        /// re-matched to its refreshed counterpart by X/Y position (not by index)
        /// before reading back its Z. The offset is computed relative to the drain
        /// baseline Z (drain vertices were set to a ZERO offset, not absolute Z=0),
        /// exactly as the engine calculated ElevationOffsetMm in the first place.
        /// Read-only — does not open a transaction.
        /// </summary>
        private void VerifyElevationsAfterCommit(
            Floor floor,
            List<SlabShapeVertex> preCommitVertices,
            double drainBaselineZFt,
            List<DrainVertexData> exportData,
            Action<string> logAction)
        {
            logAction("Verifying elevations against model after commit...");

            var editor = floor.GetSlabShapeEditor();
            if (editor == null || !editor.IsValidObject)
            {
                logAction("WARNING: Could not verify elevations — slab shape editor unavailable after commit.");
                return;
            }

            // V013: plan-position lookup instead of comparing every point with
            // every other point. Cells are 2 × the match tolerance; a point is
            // looked up in its own cell and the 8 around it.
            const double matchTolFt = 0.001;
            const double cell = matchTolFt * 2;
            var lookup = new Dictionary<(long, long), List<XYZ>>();
            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
            {
                XYZ p = v?.Position;
                if (p == null) continue;
                var key = ((long)Math.Floor(p.X / cell), (long)Math.Floor(p.Y / cell));
                if (!lookup.TryGetValue(key, out var list)) lookup[key] = list = new List<XYZ>();
                list.Add(p);
            }

            int adjustedCount = 0;
            int unmatchedCount = 0;

            foreach (var data in exportData)
            {
                if (!data.WasProcessed || data.Position == null) continue;

                // The position was captured before commit (the SlabShapeVertex
                // handles from before the commit are not used after it).
                XYZ p = data.Position;
                long kx = (long)Math.Floor(p.X / cell), ky = (long)Math.Floor(p.Y / cell);
                XYZ best = null;
                double bestD = matchTolFt;
                for (long dx = -1; dx <= 1; dx++)
                    for (long dy = -1; dy <= 1; dy++)
                        if (lookup.TryGetValue((kx + dx, ky + dy), out var list))
                            foreach (XYZ q in list)
                            {
                                double d = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
                                if (d < bestD) { bestD = d; best = q; }
                            }

                if (best != null)
                {
                    double elevFromModelFt = best.Z - drainBaselineZFt;
                    data.ElevationFromModel_mm = UnitUtils.ConvertFromInternalUnits(elevFromModelFt, UnitTypeId.Millimeters);
                    data.HasModelValue = true;

                    if (Math.Round(data.ElevationDiff_mm, 0) != 0)
                        adjustedCount++;
                }
                else
                {
                    // Could not match — fall back to the calculated value so ElevDiff_mm
                    // reads as 0 (no false positive) instead of a meaningless large diff.
                    data.ElevationFromModel_mm = data.ElevationOffsetMm;
                    unmatchedCount++;
                }
            }

            if (unmatchedCount > 0)
                logAction($"WARNING: Could not re-match {unmatchedCount} vertex(es) after commit — verification skipped for those.");

            if (adjustedCount > 0)
                logAction($"WARNING: {adjustedCount} vertex(es) were adjusted by Revit during commit — check ElevDiff_mm column in the export.");
            else
                logAction("Verification complete — no elevation adjustments detected.");
        }

        private List<DrainVertexData> CollectVertexExportData(
            Dictionary<SlabShapeVertex, (DrainItem drain, double totalDistance, List<XYZ> path)> pathResults,
            HashSet<SlabShapeVertex> drainVertices,
            List<SlabShapeVertex> allVertices,
            List<DrainItem> selectedDrains,
            double slopePercentage)
        {
            // NEW (V005) fix: iterate ALL floor vertices, not just pathResults.Keys.
            // In V004, any vertex absent from pathResults (unreachable, or now also
            // over-Max-Path-Distance) silently produced NO export row at all. Per
            // Rafi's confirmed decision, skipped vertices must always appear in the
            // Vertex Data sheet with WasProcessed=NO — matching the same principle
            // already applied to ByPoint's export (see ExcelExportHelper).
            var vertexDataList = new List<DrainVertexData>();

            for (int i = 0; i < allVertices.Count; i++)
            {
                var vertex = allVertices[i];
                if (vertex?.Position == null) continue;

                bool inPathResults = pathResults.TryGetValue(vertex, out var result);
                var drain = inPathResults ? result.drain : null;
                var totalDistance = inPathResults ? result.totalDistance : 0;
                var path = inPathResults ? result.path : null;

                bool isDrain = drainVertices.Contains(vertex);
                bool wasProcessed = inPathResults && !isDrain && drain != null;
                double overDistFt = 0;
                bool overDistance = !inPathResults && _overThresholdDistances.TryGetValue(vertex, out overDistFt);

                double elevationMm = wasProcessed ? slopePercentage / 100.0 * totalDistance * 304.8 : 0;

                int drainIndex = -1;
                string drainSize = "";
                string drainShape = "";

                if (drain != null)
                {
                    drainIndex = selectedDrains.IndexOf(drain);
                    drainSize = drain.SizeCategory;
                    drainShape = drain.ShapeType;
                }

                XYZ direction = XYZ.Zero;
                if (wasProcessed && path != null && path.Count >= 2)
                {
                    direction = (path[path.Count - 1] - path[0]).Normalize();
                }

                vertexDataList.Add(new DrainVertexData
                {
                    VertexIndex = i,
                    Position = vertex.Position,
                    // For a skipped/unreachable vertex that never entered pathResults,
                    // totalDistance is 0 (unknown) rather than a misleading computed
                    // value — the export's WasProcessed=NO column is the signal to
                    // read, not PathLengthMeters, for these rows.
                    PathLengthMeters = inPathResults ? totalDistance * 0.3048 : overDistance ? overDistFt * 0.3048 : 0,
                    ElevationOffsetMm = elevationMm,
                    NearestDrainId = drainIndex + 1,
                    DrainSize = drainSize,
                    DrainShape = drainShape,
                    DirectionVector = direction,
                    WasProcessed = wasProcessed,
                    IsDrain = isDrain,
                    SkipReason = wasProcessed || isDrain ? null : overDistance ? "Beyond distance" : "No path"
                });
            }

            return vertexDataList;
        }

        private void SetDrainLoopVerticesToZero(Floor floor, HashSet<SlabShapeVertex> drainVertices, Action<string> logAction)
        {
            var slabShapeEditor = floor.GetSlabShapeEditor();
            int setToZeroCount = 0;

            foreach (var vertex in drainVertices)
            {
                if (vertex == null) continue;
                try
                {
                    slabShapeEditor.ModifySubElement(vertex, 0.0);
                    setToZeroCount++;
                }
                catch (Exception ex)
                {
                    logAction($"WARNING: Could not set drain vertex to zero: {ex.Message}");
                }
            }

            logAction($"Set {setToZeroCount} drain loop vertices to ZERO elevation");
        }

        private Dictionary<SlabShapeVertex, (DrainItem drain, double totalDistance, List<XYZ> path)> BuildPathResultsFromEngine(
            List<SlabShapeVertex> vertexList,
            double[] distances,
            HashSet<int> drainVertexIndices,
            Dictionary<SlabShapeVertex, DrainItem> vertexToDrain,
            DijkstraPathEngine pathEngine,
            Action<string> logAction)
        {
            var results = new Dictionary<SlabShapeVertex, (DrainItem, double, List<XYZ>)>();
            int processedCount = 0;
            int unreachable = 0;

            for (int i = 0; i < vertexList.Count; i++)
            {
                var vertex = vertexList[i];
                if (vertex?.Position == null) continue;

                if (drainVertexIndices.Contains(i))
                {
                    results[vertex] = (null, 0, null);
                    continue;
                }

                double dist = distances[i];
                if (double.IsInfinity(dist))
                {
                    unreachable++;
                    continue; // no path found within threshold — vertex is simply skipped
                }

                int rootIdx = pathEngine.GetRootDrainIndex(i);
                DrainItem drain = null;
                if (rootIdx >= 0 && rootIdx < vertexList.Count)
                    vertexToDrain.TryGetValue(vertexList[rootIdx], out drain);

                if (drain == null) continue;

                var path = pathEngine.GetPathPositions(i);
                results[vertex] = (drain, dist, path);

                processedCount++;
                if (processedCount % 50 == 0)
                    logAction($"Computed paths for {processedCount}/{vertexList.Count} vertices...");
            }

            _lastUnreachableCount = unreachable;

            if (unreachable > 0)
                logAction($"WARNING: {unreachable} vertex(es) had no valid path to any selected drain — likely disconnected from the graph. Check Max Edge Distance if this seems too high.");

            logAction($"✓ Path computation completed for {processedCount} vertices");
            return results;
        }

        private int ApplyElevationsWithDrainHierarchy(Floor floor,
            Dictionary<SlabShapeVertex, (DrainItem drain, double totalDistance, List<XYZ> path)> pathResults,
            HashSet<SlabShapeVertex> drainVertices,
            double slopePercentage,
            Action<string> logAction,
            out double maxOffset,
            out double longestPath)
        {
            int modifiedCount = 0;
            maxOffset = 0;
            longestPath = 0;

            var slabShapeEditor = floor.GetSlabShapeEditor();

            foreach (var kvp in pathResults)
            {
                var vertex = kvp.Key;
                var totalDistance = kvp.Value.totalDistance;
                var drain = kvp.Value.drain;

                if (vertex == null) continue;

                if (drainVertices.Contains(vertex))
                    continue;

                if (drain == null)
                    continue;

                double pathLengthMeters = totalDistance * 0.3048;
                if (pathLengthMeters > longestPath)
                    longestPath = pathLengthMeters;

                double slopeRatio = slopePercentage / 100.0;
                double elevationChange = slopeRatio * totalDistance * 304.8;
                double newElevationFeet = elevationChange / 304.8;
                slabShapeEditor.ModifySubElement(vertex, newElevationFeet);
                modifiedCount++;

                if (elevationChange > maxOffset)
                    maxOffset = elevationChange;
            }

            logAction($"Applied slopes to {modifiedCount} non-drain vertices");
            return modifiedCount;
        }

        private static Face GetTopFaceFresh(Floor floor)
        {
            GeometryElement geomElem = floor.get_Geometry(new Options());
            Face topFace = null;
            double maxZ = double.MinValue;

            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Solid solid)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face == null) continue;
                        BoundingBoxUV bb = face.GetBoundingBox();
                        if (bb == null) continue;

                        UV midpointUV = new UV((bb.Min.U + bb.Max.U) / 2, (bb.Min.V + bb.Max.V) / 2);
                        XYZ midpoint = face.Evaluate(midpointUV);

                        if (midpoint != null && midpoint.Z > maxZ)
                        {
                            maxZ = midpoint.Z;
                            topFace = face;
                        }
                    }
                }
            }
            return topFace;
        }
    }
}
