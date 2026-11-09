// =======================================================
// File: AutoSlopeEngine.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPointRidge.V003
// Changes vs V028:
//   - RIDGE RULE (see RidgeEngine.cs): drains are clustered into
//     groups, every vertex is assigned to the basin of its nearest
//     group by path distance (a Voronoi cell along the roof), and the
//     vertices on the basin boundaries are ridge points. Each ridge
//     point is lifted to slope × (path to the FARTHEST surrounding
//     group) so water leaves it towards every drain around it at
//     ≥ the given slope.
//   - Final elevations come from a lower-bounded Dijkstra
//     (DijkstraPathEngine.ComputeLowerBoundedElevations): the ridge
//     lifts are minimums, and everything behind a lifted ridge
//     re-flows to the far drain at exactly the given slope ("move
//     watershed"). Every processed vertex is guaranteed a neighbour
//     it descends to at ≥ the given slope — no ponds.
//   - RidgeDetectionEnabled = false reproduces V028 exactly.
//   - Ridge circle-marker group + ridge columns in the Excel export.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AutoSlopeByPointRidge.V003.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPointRidge.V003.Core.Parameters;
using Revit26_Plugin_Revised.AutoSlopeByPointRidge.V003.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.AutoSlopeByPointRidge.V003.Core.Engine
{
    public static class AutoSlopeEngine
    {
        public static void Execute(UIApplication app, AutoSlopePayload data)
        {
            try
            {
                ExecuteCore(app, data);
            }
            catch (OperationCanceledException)
            {
                data.Log?.Invoke(new LogEntry(LogLevel.Warning, "Run cancelled by user."));
                data.OnCompleted?.Invoke(new AutoSlopeResult
                {
                    Success = false,
                    WasCancelled = true,
                    ErrorMessage = "Cancelled by user.",
                    PickedDrainCount = 0,
                    FinalDrainCount = 0
                });
                throw; // the handler rolls the group back; ToolGuard treats it as a user cancel
            }
        }

        private static void ExecuteCore(UIApplication app, AutoSlopePayload data)
        {
            Document doc = app.ActiveUIDocument.Document;
            DateTime runStartTime = DateTime.Now;
            data.Log?.Invoke(new LogEntry(LogLevel.Info, $"Run started at {runStartTime:HH:mm:ss}"));

            data.CancelToken.ThrowIfCancellationRequested();

            // ── Guard: roof ─────────────────────────────────────────────────
            RoofBase roof = doc.GetElement(data.RoofId) as RoofBase;
            if (roof == null)
            {
                FireFailure(data, "Roof element not found. Aborting.");
                return;
            }

            // ── Guard: slab shape editor ────────────────────────────────────
            SlabShapeEditor editor = roof.GetSlabShapeEditor();
            if (editor == null || !editor.IsValidObject)
            {
                data.Log?.Invoke(new LogEntry(LogLevel.Error,
                    "Roof slab shape editor is not available. Aborting."));
                FireFailure(data, "Roof slab shape editor is not available.");
                return;
            }

            data.Progress?.Invoke(new RunProgressInfo("Resetting roof vertices"));

            // ── Reset vertices ───────────────────────────────────────────────
            using (Transaction tx = new Transaction(doc, "Reset Roof Vertices"))
            {
                tx.Start();
                foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                    editor.ModifySubElement(v, 0);
                tx.Commit();
            }

            // ── Collect vertices ─────────────────────────────────────────────
            var vertices = new List<SlabShapeVertex>();
            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                vertices.Add(v);

            double slopeFactor = data.SlopePercent / 100.0;
            double thresholdFt = UnitUtils.ConvertToInternalUnits(data.ThresholdMeters, UnitTypeId.Meters);

            // ── Guard: top face ──────────────────────────────────────────────
            Face topFace = AutoSlopeGeometry.GetTopFace(roof);
            if (topFace == null)
            {
                data.Log?.Invoke(new LogEntry(LogLevel.Error, "Top face not found. Aborting."));
                FireFailure(data, "Top face not found.");
                return;
            }

            // ── Opt-in: insert real vertices at line/arc intersection points ──
            // (entry/exit points where a straight roof-shape edge partially
            // overlaps a boundary or opening arc). Only runs if the user has
            // enabled "Insert intersection points on curves" in the UI.
            // curveTolFt is declared here (not inside the block below) so the
            // SAME tolerance is later passed into DijkstraPathEngine — previously
            // that call used the constructor's default (~1mm) instead of this
            // ~2mm value, so a drain vertex matched via a looser drain-tolerance
            // could fail the arc "on-curve" check and miss the same-arc bypass.
            double curveTolFt = UnitUtils.ConvertToInternalUnits(2.0, UnitTypeId.Millimeters); // ~1-2mm tolerance
            List<Arc> boundaryArcs = null;
            if (data.InsertCurveIntersectionPoints)
            {
                boundaryArcs = AutoSlopeGeometry.GetBoundaryArcs(topFace);

                if (boundaryArcs.Count == 0)
                {
                    data.Log?.Invoke(new LogEntry(LogLevel.Info,
                        "Curve intersection check enabled, but no arc edges found on this roof."));
                }
                else
                {
                    var positions = new List<XYZ>(vertices.Count);
                    foreach (var v in vertices) positions.Add(v.Position);

                    // Only the roof's real physical edges are tested against the arcs —
                    // not arbitrary vertex pairs — so points are inserted only where
                    // actually required.
                    List<Line> boundaryLines = AutoSlopeGeometry.GetBoundaryLines(topFace);

                    int insertedCount;
                    using (Transaction tx = new Transaction(doc, "Insert Curve Intersection Points"))
                    {
                        tx.Start();
                        insertedCount = CurveIntersectionHelper.InsertIntersectionPoints(
                            editor, boundaryLines, positions, boundaryArcs, curveTolFt,
                            msg => data.Log?.Invoke(new LogEntry(LogLevel.Info, msg)));
                        tx.Commit();
                    }

                    if (insertedCount > 0)
                    {
                        // NOTE: no explicit doc.Regenerate() here — tx.Commit() above already
                        // regenerated the document. Calling Regenerate() again here runs with
                        // no transaction open and throws "Modification of the document is
                        // forbidden ... no open transaction."

                        // The commit above already regenerated the document, which invalidates
                        // the SlabShapeEditor handle acquired before it — re-fetch a fresh one
                        // before touching it again, per this suite's own rule on this.
                        editor = roof.GetSlabShapeEditor();
                        if (editor == null || !editor.IsValidObject)
                        {
                            data.Log?.Invoke(new LogEntry(LogLevel.Error,
                                "Slab shape editor became invalid after inserting curve points. Aborting."));
                            FireFailure(data, "Slab shape editor became invalid after inserting curve points.");
                            return;
                        }

                        // Re-collect vertices (now includes the newly inserted points)
                        // and re-acquire topFace + arcs, since geometry changed underneath.
                        vertices = new List<SlabShapeVertex>();
                        foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                            vertices.Add(v);

                        topFace = AutoSlopeGeometry.GetTopFace(roof);
                        if (topFace == null)
                        {
                            data.Log?.Invoke(new LogEntry(LogLevel.Error,
                                "Top face not found after inserting curve points. Aborting."));
                            FireFailure(data, "Top face not found after inserting curve points.");
                            return;
                        }
                        boundaryArcs = AutoSlopeGeometry.GetBoundaryArcs(topFace);
                    }
                }
            }

            // ── Build final drain points ─────────────────────────────────────
            List<XYZ> finalDrainPoints = data.DrainPoints ?? new List<XYZ>();

            if (data.EnableDrainTolerance && data.DrainToleranceMm > 0)
            {
                data.Log?.Invoke(new LogEntry(LogLevel.Info,
                    $"🔍 Checking for nearby roof shape points within {data.DrainToleranceMm}mm of selected points..."));

                finalDrainPoints = DrainDetectionHelper.DetectDrainsWithinRadius(
                    roof, finalDrainPoints, data.DrainToleranceMm, data.Log);

                finalDrainPoints = DrainDetectionHelper.RemoveDuplicates(
                    finalDrainPoints, data.DrainToleranceMm);
            }

            if (finalDrainPoints == null || finalDrainPoints.Count == 0)
            {
                data.Log?.Invoke(new LogEntry(LogLevel.Error,
                    "No drain points are available. Aborting."));
                FireFailure(data, "No drain points are available.");
                return;
            }

            data.CancelToken.ThrowIfCancellationRequested();

            // ── Build Dijkstra graph ─────────────────────────────────────────
            data.Progress?.Invoke(new RunProgressInfo("Building path graph"));
            var dijkstra = new DijkstraPathEngine(
                vertices, topFace, thresholdFt, boundaryArcs, curveTolFt,
                onBuildGraphProgress: pct => data.Progress?.Invoke(new RunProgressInfo("Building path graph", pct)),
                cancelToken: data.CancelToken);

            data.CancelToken.ThrowIfCancellationRequested();

            double drainMatchToleranceFt = data.EnableDrainTolerance && data.DrainToleranceMm > 0
                ? UnitUtils.ConvertToInternalUnits(data.DrainToleranceMm, UnitTypeId.Millimeters)
                : 0.001;

            var drainIndices = new HashSet<int>();
            for (int i = 0; i < vertices.Count; i++)
            {
                foreach (XYZ drainPoint in finalDrainPoints)
                {
                    if (drainPoint == null) continue;
                    if (vertices[i].Position.DistanceTo(drainPoint) <= drainMatchToleranceFt)
                    {
                        drainIndices.Add(i);
                        break;
                    }
                }
            }

            if (drainIndices.Count == 0)
            {
                data.Log?.Invoke(new LogEntry(LogLevel.Error,
                    "No roof vertices matched the selected drain points. Aborting."));
                FireFailure(data, "No roof vertices matched the selected drain points.");
                return;
            }

            // ── Ridge analysis (V001) ────────────────────────────────────────
            // Groups the drains, computes per-group path distances, finds the
            // Voronoi-boundary (ridge) vertices and their lifts. When ridge
            // detection is off, the analysis still supplies the plain nearest
            // distances (identical to V028's single multi-source Dijkstra).
            double groupRadiusFt = data.RidgeDetectionEnabled && data.DrainGroupRadiusMm > 0
                ? UnitUtils.ConvertToInternalUnits(data.DrainGroupRadiusMm, UnitTypeId.Millimeters)
                : 0;

            data.Progress?.Invoke(new RunProgressInfo("Analyzing ridges"));
            RidgeEngine.RidgeAnalysis ridge = RidgeEngine.Analyze(
                dijkstra, vertices, drainIndices, groupRadiusFt, slopeFactor);

            data.CancelToken.ThrowIfCancellationRequested();

            double[] distances = ridge.NearestDistance;          // plain V028 distances (ft)
            double[] minElevFt = data.RidgeDetectionEnabled ? ridge.MinElevationFt : null;

            data.Log?.Invoke(new LogEntry(LogLevel.Info,
                $"Drain groups: {ridge.GroupCount} (from {drainIndices.Count} drain vertex/vertices, " +
                $"group radius {data.DrainGroupRadiusMm} mm)"));
            for (int g = 0; g < ridge.GroupCount; g++)
            {
                XYZ c = GroupCentroid(ridge.Groups[g], vertices);
                data.Log?.Invoke(new LogEntry(LogLevel.Info,
                    $"  • Group {g}: {ridge.Groups[g].Count} drain vertex/vertices at ({c.X:F2}, {c.Y:F2})"));
            }

            if (!data.RidgeDetectionEnabled)
            {
                data.Log?.Invoke(new LogEntry(LogLevel.Info,
                    "Ridge detection is OFF — running plain nearest-drain slope (V028 behaviour)."));
            }
            else if (ridge.GroupCount < 2)
            {
                data.Log?.Invoke(new LogEntry(LogLevel.Warning,
                    "Ridge detection: only one drain group — no ridge is possible. " +
                    "Pick drains further apart or reduce the group radius."));
            }
            else if (ridge.RidgePoints.Count == 0)
            {
                data.Log?.Invoke(new LogEntry(LogLevel.Warning,
                    "Ridge detection: drain basins never touch on this roof (no cross-basin edge) — no ridge points."));
            }
            else
            {
                data.Log?.Invoke(new LogEntry(LogLevel.Success,
                    $"Ridge detection: {ridge.RidgePoints.Count} ridge point(s) found on the basin boundaries."));
                foreach (var kv in ridge.RidgePoints.OrderBy(k => k.Key))
                {
                    RidgeEngine.RidgeInfo ri = kv.Value;
                    double nearM  = UnitUtils.ConvertFromInternalUnits(ri.NearestPathFt, UnitTypeId.Meters);
                    double farM   = UnitUtils.ConvertFromInternalUnits(ri.GoverningPathFt, UnitTypeId.Meters);
                    double liftMm = UnitUtils.ConvertFromInternalUnits(ri.LiftFt, UnitTypeId.Millimeters);
                    data.Log?.Invoke(new LogEntry(LogLevel.Info,
                        $"  • RIDGE vertex {ri.VertexIndex}: groups [{string.Join(",", ri.SurroundingGroups)}] " +
                        $"nearest {nearM:F2} m → governs group {ri.GoverningGroup} at {farM:F2} m → lift {liftMm:0} mm"));
                }
            }

            // ── Final elevations: lower-bounded multi-source Dijkstra ────────
            // Elevation units (ft of rise). Drains = 0; each edge costs
            // w × slope; each ridge vertex is clamped up to its lift; anything
            // that can only get down through a lifted ridge is lifted with it or
            // re-routed to the far drain — whichever is lower.
            double[] elevations = dijkstra.ComputeLowerBoundedElevations(
                ridge.DrainToGroup, minElevFt, slopeFactor, out int[] flowsToGroup);

            int noDescent = RidgeEngine.CountVerticesWithoutDescent(dijkstra, elevations, drainIndices, slopeFactor);
            if (noDescent > 0)
                data.Log?.Invoke(new LogEntry(LogLevel.Warning,
                    $"Slope check: {noDescent} vertex/vertices have no neighbour to descend to at {data.SlopePercent}% — please review."));
            else
                data.Log?.Invoke(new LogEntry(LogLevel.Success,
                    $"Slope check: every processed vertex descends to a neighbour at ≥ {data.SlopePercent}%."));

            // ── Main slope loop ──────────────────────────────────────────────
            int processed = 0, skipped = 0, ridgeCount = 0, watershedMoved = 0;
            double maxPathFt = 0;
            var vertexDataList = new List<VertexData>();

            double drainBaselineZFt = drainIndices.Count > 0
                ? drainIndices.Average(idx => vertices[idx].Position.Z)
                : 0;

            CircleMarkerService.PlacementCounts markerCounts = null;

            data.Progress?.Invoke(new RunProgressInfo("Applying elevations"));

            using (Transaction tx = new Transaction(doc, "Apply AutoSlope (Ridge)"))
            {
                tx.Start();

                for (int i = 0; i < vertices.Count; i++)
                {
                    double nearestFt = distances[i];
                    double elevFt    = elevations[i];

                    // Skip rule is the same as V028: unreachable, or nearest path
                    // beyond the threshold. The threshold is judged on the plain
                    // nearest distance so a ridge lift never pushes a vertex over it.
                    if (double.IsInfinity(nearestFt) || double.IsInfinity(elevFt) || nearestFt > thresholdFt)
                    {
                        skipped++;

                        if (data.ExportConfig?.IncludeVertexDetails == true)
                        {
                            vertexDataList.Add(new VertexData
                            {
                                VertexIndex       = i,
                                Position          = vertices[i].Position,
                                PathLengthMeters  = double.IsInfinity(nearestFt) ? 0
                                    : UnitUtils.ConvertFromInternalUnits(nearestFt, UnitTypeId.Meters),
                                NearestPathMeters = double.IsInfinity(nearestFt) ? 0
                                    : UnitUtils.ConvertFromInternalUnits(nearestFt, UnitTypeId.Meters),
                                ElevationOffsetMm      = 0,
                                ElevationFromModel_mm  = 0,
                                NearestDrainIndex      = -1,
                                DirectionVector        = XYZ.Zero,
                                WasProcessed           = false,
                                BasinGroup             = ridge.Basin[i]
                            });
                        }
                        continue;
                    }

                    editor.ModifySubElement(vertices[i], elevFt);

                    processed++;
                    double effectivePathFt = slopeFactor > 0 ? elevFt / slopeFactor : nearestFt;
                    if (effectivePathFt > maxPathFt) maxPathFt = effectivePathFt;

                    RidgeEngine.RidgeInfo rinfo = null;
                    bool isRidge = data.RidgeDetectionEnabled && ridge.RidgePoints.TryGetValue(i, out rinfo);
                    if (isRidge) ridgeCount++;

                    int basin = ridge.Basin[i];
                    int flows = flowsToGroup[i];
                    if (data.RidgeDetectionEnabled && !isRidge && basin >= 0 && flows >= 0 && flows != basin)
                        watershedMoved++;

                    double v028ElevFt = nearestFt * slopeFactor;
                    double liftFt     = Math.Max(0, elevFt - v028ElevFt);

                    int nearestDrainIndex = FindNearestDrainIndex(vertices[i].Position, finalDrainPoints);
                    XYZ directionVector   = nearestDrainIndex >= 0
                        ? CalculateDirectionVector(vertices[i].Position, finalDrainPoints[nearestDrainIndex])
                        : XYZ.Zero;

                    vertexDataList.Add(new VertexData
                    {
                        VertexIndex       = i,
                        Position          = vertices[i].Position,
                        PathLengthMeters  = UnitUtils.ConvertFromInternalUnits(effectivePathFt, UnitTypeId.Meters),
                        NearestPathMeters = UnitUtils.ConvertFromInternalUnits(nearestFt, UnitTypeId.Meters),
                        ElevationOffsetMm = UnitUtils.ConvertFromInternalUnits(elevFt, UnitTypeId.Millimeters),
                        ElevationFromModel_mm = 0,
                        NearestDrainIndex = nearestDrainIndex,
                        DirectionVector   = directionVector,
                        WasProcessed      = true,
                        IsRidgePoint      = isRidge,
                        BasinGroup        = basin,
                        FlowsToGroup      = flows,
                        RidgeLiftMm       = UnitUtils.ConvertFromInternalUnits(liftFt, UnitTypeId.Millimeters),
                        SurroundingGroups = rinfo != null ? string.Join(",", rinfo.SurroundingGroups) : string.Empty
                    });
                }

                // ── Circle Markers (V026) ────────────────────────────────────
                // Runs inside this same transaction (one commit, one undo step
                // for the whole Run) — see CircleMarkerService header comment
                // for why Highest Point uses ElevationOffsetMm here.
                View activeView = app.ActiveUIDocument?.ActiveView;
                data.Progress?.Invoke(new RunProgressInfo("Placing circle markers"));
                markerCounts = CircleMarkerService.PlaceMarkers(
                    doc,
                    activeView,
                    finalDrainPoints,
                    vertexDataList,
                    data.DrainMarkerGroup,
                    data.HighestPointMarkerGroup,
                    data.AllowedOffsetMarkerGroup,
                    data.RidgeMarkerGroup,
                    data.AllowedOffsetThresholdMm,
                    data.EnableDrainTolerance ? data.DrainToleranceMm : 0,
                    data.Log);

                tx.Commit();
            }

            // ── Re-read vertices from Revit after commit ─────────────────────
            double maxElevFt = 0;
            var refreshedVertices = new List<SlabShapeVertex>();
            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                refreshedVertices.Add(v);

            var refreshedZByIndex = new Dictionary<int, double>();
            for (int i = 0; i < refreshedVertices.Count; i++)
            {
                for (int j = 0; j < vertices.Count; j++)
                {
                    double xyDist = Math.Sqrt(
                        Math.Pow(refreshedVertices[i].Position.X - vertices[j].Position.X, 2) +
                        Math.Pow(refreshedVertices[i].Position.Y - vertices[j].Position.Y, 2));

                    if (xyDist < 0.001)
                    {
                        refreshedZByIndex[j] = refreshedVertices[i].Position.Z;
                        break;
                    }
                }
            }

            foreach (var vd in vertexDataList)
            {
                if (!vd.WasProcessed) continue;

                if (refreshedZByIndex.TryGetValue(vd.VertexIndex, out double refreshedZFt))
                {
                    double elevFromModelFt = refreshedZFt - drainBaselineZFt;
                    vd.ElevationFromModel_mm = UnitUtils.ConvertFromInternalUnits(
                        elevFromModelFt, UnitTypeId.Millimeters);

                    if (elevFromModelFt > maxElevFt) maxElevFt = elevFromModelFt;
                }
                else
                {
                    vd.ElevationFromModel_mm = vd.ElevationOffsetMm;
                    data.Log?.Invoke(new LogEntry(LogLevel.Warning,
                        $"WARN: Could not match refreshed vertex for index {vd.VertexIndex}, using calculated value."));
                }
            }

            DateTime runEndTime = DateTime.Now;

            int    highest_mm  = (int)Math.Round(
                UnitUtils.ConvertFromInternalUnits(maxElevFt, UnitTypeId.Millimeters),
                MidpointRounding.AwayFromZero);
            double longest_m   = Math.Round(
                UnitUtils.ConvertFromInternalUnits(maxPathFt, UnitTypeId.Meters),
                2, MidpointRounding.AwayFromZero);
            int    durationSec = (int)(runEndTime - runStartTime).TotalSeconds;
            string runDate     = DateTime.Now.ToString("dd-MM-yy HH:mm");

            const string toolVersion = "R.01.00";

            int statusCode = AutoSlopeParameterWriter.WriteAll(
                doc, roof, data,
                highest_mm, maxPathFt,
                processed, skipped, durationSec,
                finalDrainPoints.Count,
                runDate,
                toolVersion);

            string compactPath = null;
            if (data.ExportConfig?.ExportToExcel == true)
            {
                compactPath = ExcelExportService.ExportCompactVertexData(
                    data, vertexDataList, roof, data.SlopePercent,
                    toolVersion, statusCode,
                    durationSec, runDate,
                    runStartTime.ToString("HH:mm:ss"), runEndTime.ToString("HH:mm:ss"));

                if (!string.IsNullOrEmpty(compactPath))
                {
                    data.Log?.Invoke(new LogEntry(LogLevel.Success,
                        $"✅ Compact Excel exported to: {compactPath}"));
                    data.Log?.Invoke(new LogEntry(LogLevel.Info,
                        "  • Sorted by PathLength_Meters (longest first)"));
                    data.Log?.Invoke(new LogEntry(LogLevel.Info,
                        $"  • Contains {processed} processed vertices"));

                    var longestVertex = vertexDataList
                        .Where(v => v.WasProcessed)
                        .OrderByDescending(v => v.PathLengthMeters)
                        .FirstOrDefault();

                    if (longestVertex != null)
                        data.Log?.Invoke(new LogEntry(LogLevel.Info,
                            $"  • Longest path: {longestVertex.PathLengthMeters:F2} m to drain {longestVertex.NearestDrainIndex}"));
                }

                if (data.ExportConfig.IncludeVertexDetails)
                {
                    string detailedPath = ExcelExportService.ExportDetailedVertexData(
                        data, vertexDataList, roof, finalDrainPoints, data.SlopePercent);

                    if (!string.IsNullOrEmpty(detailedPath))
                    {
                        data.Log?.Invoke(new LogEntry(LogLevel.Success,
                            $"✅ Detailed Excel exported to: {detailedPath}"));
                        data.Log?.Invoke(new LogEntry(LogLevel.Info,
                            $"  • {vertexDataList.Count} total vertices ({processed} processed, {skipped} skipped)"));
                        data.Log?.Invoke(new LogEntry(LogLevel.Info,
                            "  • Sheets: Summary, Drain Points, Vertices, Statistics"));
                    }
                }
            }

            // ── Summary ──────────────────────────────────────────────────────
            data.Log?.Invoke(new LogEntry(LogLevel.Info,    "===== AutoSlope Summary ====="));
            data.Log?.Invoke(new LogEntry(LogLevel.Success, $"Applied Slope Percentage : {data.SlopePercent}%"));
            data.Log?.Invoke(new LogEntry(LogLevel.Success, $"Vertices Processed       : {processed}"));
            data.Log?.Invoke(new LogEntry(LogLevel.Warning, $"Vertices Skipped         : {skipped}"));
            data.Log?.Invoke(new LogEntry(LogLevel.Info,    $"Highest Elevation        : {highest_mm:0} mm  ← from model re-read"));
            data.Log?.Invoke(new LogEntry(LogLevel.Info,    $"Longest Path             : {longest_m:0.00} m"));
            data.Log?.Invoke(new LogEntry(LogLevel.Info,    $"Picked Drain Count       : {data.PickedDrainPoints?.Count ?? 0}"));
            data.Log?.Invoke(new LogEntry(LogLevel.Info,    $"Final Drain Count        : {finalDrainPoints.Count}"));
            data.Log?.Invoke(new LogEntry(LogLevel.Info,    $"Run Duration             : {durationSec} sec"));
            data.Log?.Invoke(new LogEntry(LogLevel.Info,    $"Run Date                 : {runDate}"));
            data.Log?.Invoke(new LogEntry(LogLevel.Info,
                $"Run finished at {runEndTime:HH:mm:ss} | Started {runStartTime:HH:mm:ss} | Duration: {durationSec}s"));
            if (data.EnableDrainTolerance)
                data.Log?.Invoke(new LogEntry(LogLevel.Info,
                    $"Drain Tolerance          : {data.DrainToleranceMm} mm (enabled)"));
            data.Log?.Invoke(new LogEntry(LogLevel.Info,    $"Drain Groups             : {ridge.GroupCount}"));
            data.Log?.Invoke(new LogEntry(LogLevel.Info,    $"Ridge Points             : {ridgeCount}" +
                (data.RidgeDetectionEnabled ? "" : "  (ridge detection off)")));
            data.Log?.Invoke(new LogEntry(LogLevel.Info,    $"Watershed Moved          : {watershedMoved} vertex/vertices re-routed to a farther drain"));
            data.Log?.Invoke(new LogEntry(LogLevel.Info,
                $"Circles Placed            : {markerCounts?.DrainCirclesPlaced ?? 0} drain, " +
                $"{markerCounts?.HighestCirclesPlaced ?? 0} highest, " +
                $"{markerCounts?.OffsetCirclesPlaced ?? 0} allowed-offset, " +
                $"{markerCounts?.RidgeCirclesPlaced ?? 0} ridge"));
            data.Log?.Invoke(new LogEntry(LogLevel.Success, "===== AutoSlope Finished Successfully ====="));

            data.OnCompleted?.Invoke(new AutoSlopeResult
            {
                Success           = true,
                VerticesProcessed = processed,
                VerticesSkipped   = skipped,
                PickedDrainCount  = data.PickedDrainPoints?.Count ?? 0,
                FinalDrainCount   = finalDrainPoints.Count,
                HighestElevation_mm = highest_mm,
                LongestPath_m     = longest_m,
                RunDuration_sec   = durationSec,
                RunDate           = runDate,
                RunStartTime      = runStartTime.ToString("HH:mm:ss"),
                RunEndTime        = runEndTime.ToString("HH:mm:ss"),
                CurvesCalculated  = boundaryArcs?.Count ?? 0,
                Version           = toolVersion,
                Status            = statusCode,
                ExportedFilePath  = compactPath,
                DrainCirclesPlaced   = markerCounts?.DrainCirclesPlaced ?? 0,
                HighestCirclesPlaced = markerCounts?.HighestCirclesPlaced ?? 0,
                OffsetCirclesPlaced  = markerCounts?.OffsetCirclesPlaced ?? 0,
                DrainGroupCount      = ridge.GroupCount,
                RidgePointCount      = ridgeCount,
                WatershedMovedCount  = watershedMoved,
                RidgeCirclesPlaced   = markerCounts?.RidgeCirclesPlaced ?? 0
            });
        }

        private static void FireFailure(AutoSlopePayload data, string reason)
        {
            data.Log?.Invoke(new LogEntry(LogLevel.Error, reason));
            data.OnCompleted?.Invoke(new AutoSlopeResult
            {
                Success      = false,
                ErrorMessage = reason,
                PickedDrainCount = 0,
                FinalDrainCount  = 0
            });
        }

        private static int FindNearestDrainIndex(XYZ vertexPos, List<XYZ> drainPoints)
        {
            if (drainPoints == null || drainPoints.Count == 0) return -1;
            int nearestIndex = 0;
            double minDistance = double.MaxValue;
            for (int i = 0; i < drainPoints.Count; i++)
            {
                if (drainPoints[i] == null) continue;
                double d = vertexPos.DistanceTo(drainPoints[i]);
                if (d < minDistance) { minDistance = d; nearestIndex = i; }
            }
            return nearestIndex;
        }

        private static XYZ GroupCentroid(List<int> drainVertexIndices, List<SlabShapeVertex> vertices)
        {
            if (drainVertexIndices == null || drainVertexIndices.Count == 0) return XYZ.Zero;
            double x = 0, y = 0, z = 0;
            foreach (int idx in drainVertexIndices)
            {
                XYZ p = vertices[idx].Position;
                x += p.X; y += p.Y; z += p.Z;
            }
            int n = drainVertexIndices.Count;
            return new XYZ(x / n, y / n, z / n);
        }

        private static XYZ CalculateDirectionVector(XYZ fromPoint, XYZ toPoint)
        {
            if (fromPoint.DistanceTo(toPoint) < 0.001) return XYZ.Zero;
            return (toPoint - fromPoint).Normalize();
        }
    }
}
