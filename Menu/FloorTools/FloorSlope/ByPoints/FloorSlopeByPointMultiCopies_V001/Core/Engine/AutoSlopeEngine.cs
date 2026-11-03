// =======================================================
// File: AutoSlopeEngine.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001
// Changes vs V028:
//   - Execute(app, payload) replaced by ApplySlope(app, payload):
//     no internal Transaction.Start/Commit — the caller
//     (MultiSlopeVariantEngine) already has a Transaction open per floor
//     copy, and Revit does not allow starting a Transaction while one is
//     already open on the document. Every point where the original relied
//     on tx.Commit() to trigger regeneration before re-reading geometry
//     now calls doc.Regenerate() explicitly instead, while remaining
//     inside the caller's open transaction — the SlabShapeEditor handle
//     is still re-fetched afterward exactly as before, since Regenerate()
//     invalidates it the same way a commit did.
//   - ApplySlope returns a GeometryApplyOutcome (geometry-phase results
//     only) instead of the final AutoSlopeResult.
// Standardized (2026-09): AutoSlopeParameterWriter.WriteAll was restored
// to owning its own Transaction (matching every other AutoSlope tool),
// which means it can no longer be called from inside ApplySlope — that
// method runs entirely within the caller's still-open geometry
// Transaction. Parameter writing and Excel export now happen in the new
// FinalizeAndExport(...), which the caller invokes AFTER its geometry
// Transaction has committed, so WriteAll's own Transaction opens cleanly
// with nothing else active on the document.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Models;
using Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Parameters;
using Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Engine
{
    public static class AutoSlopeEngine
    {
        /// <summary>
        /// Runs the geometry phase only — resets/edits SlabShapeVertex elevations,
        /// optionally inserts curve-intersection points, and places circle markers.
        /// Assumes the caller already has a Transaction active on the document —
        /// this method never opens or commits one itself. Does NOT write any
        /// AutoSlope_* parameters or run Excel export; call FinalizeAndExport(...)
        /// with the returned outcome after the caller's Transaction has committed.
        /// </summary>
        public static GeometryApplyOutcome ApplySlope(UIApplication app, AutoSlopePayload data)
        {
            Document doc = app.ActiveUIDocument.Document;
            DateTime runStartTime = DateTime.Now;

            data.CancelToken.ThrowIfCancellationRequested();

            // ── Guard: floor ─────────────────────────────────────────────────
            Floor floor = doc.GetElement(data.FloorId) as Floor;
            if (floor == null)
                return Failure(data, "Floor element not found. Aborting.");

            // ── Guard: slab shape editor ────────────────────────────────────
            SlabShapeEditor editor = floor.GetSlabShapeEditor();
            if (editor == null || !editor.IsValidObject)
                return Failure(data, "Floor slab shape editor is not available. Aborting.");

            // ── Reset vertices ───────────────────────────────────────────────
            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                editor.ModifySubElement(v, 0);

            // Regenerate now (in place of the original "Reset Floor Vertices" tx
            // commit) so vertex positions below reflect the reset.
            doc.Regenerate();

            // ── Collect vertices ─────────────────────────────────────────────
            var vertices = new List<SlabShapeVertex>();
            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                vertices.Add(v);

            double slopeFactor = data.SlopePercent / 100.0;
            double thresholdFt = UnitUtils.ConvertToInternalUnits(data.ThresholdMeters, UnitTypeId.Meters);

            // ── Guard: top face ──────────────────────────────────────────────
            Face topFace = AutoSlopeGeometry.GetTopFace(floor);
            if (topFace == null)
                return Failure(data, "Top face not found. Aborting.");

            // ── Opt-in: insert real vertices at line/arc intersection points ──
            double curveTolFt = UnitUtils.ConvertToInternalUnits(2.0, UnitTypeId.Millimeters); // ~1-2mm tolerance
            List<Arc> boundaryArcs = null;
            if (data.InsertCurveIntersectionPoints)
            {
                boundaryArcs = AutoSlopeGeometry.GetBoundaryArcs(topFace);

                if (boundaryArcs.Count == 0)
                {
                    data.Log?.Invoke(new LogEntry(LogLevel.Info,
                        "Curve intersection check enabled, but no arc edges found on this floor."));
                }
                else
                {
                    var positions = new List<XYZ>(vertices.Count);
                    foreach (var v in vertices) positions.Add(v.Position);

                    List<Line> boundaryLines = AutoSlopeGeometry.GetBoundaryLines(topFace);

                    int insertedCount = CurveIntersectionHelper.InsertIntersectionPoints(
                        editor, boundaryLines, positions, boundaryArcs, curveTolFt,
                        msg => data.Log?.Invoke(new LogEntry(LogLevel.Info, msg)));

                    if (insertedCount > 0)
                    {
                        // Regenerate in place of the original commit — the
                        // SlabShapeEditor handle is invalidated by Regenerate()
                        // the same way it was by a transaction commit, so it
                        // must be re-fetched before touching it again.
                        doc.Regenerate();

                        editor = floor.GetSlabShapeEditor();
                        if (editor == null || !editor.IsValidObject)
                            return Failure(data, "Slab shape editor became invalid after inserting curve points. Aborting.");

                        // Re-collect vertices (now includes the newly inserted points)
                        // and re-acquire topFace + arcs, since geometry changed underneath.
                        vertices = new List<SlabShapeVertex>();
                        foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                            vertices.Add(v);

                        topFace = AutoSlopeGeometry.GetTopFace(floor);
                        if (topFace == null)
                            return Failure(data, "Top face not found after inserting curve points. Aborting.");
                        boundaryArcs = AutoSlopeGeometry.GetBoundaryArcs(topFace);
                    }
                }
            }

            // ── Build final drain points ─────────────────────────────────────
            List<XYZ> finalDrainPoints = data.DrainPoints ?? new List<XYZ>();

            if (data.EnableDrainTolerance && data.DrainToleranceMm > 0)
            {
                data.Log?.Invoke(new LogEntry(LogLevel.Info,
                    $"🔍 Checking for nearby floor shape points within {data.DrainToleranceMm}mm of selected points..."));

                finalDrainPoints = DrainDetectionHelper.DetectDrainsWithinRadius(
                    floor, finalDrainPoints, data.DrainToleranceMm, data.Log);

                finalDrainPoints = DrainDetectionHelper.RemoveDuplicates(
                    finalDrainPoints, data.DrainToleranceMm);
            }

            if (finalDrainPoints == null || finalDrainPoints.Count == 0)
                return Failure(data, "No drain points are available. Aborting.");

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
                return Failure(data, "No floor vertices matched the selected drain points. Aborting.");

            // ── OPTIMIZATION: Single multi-source Dijkstra ───────────────────
            data.Progress?.Invoke(new RunProgressInfo("Computing shortest paths"));
            double[] distances = dijkstra.ComputeAllDistances(drainIndices);

            // ── Main slope loop ──────────────────────────────────────────────
            int processed = 0, skipped = 0;
            double maxPathFt = 0;
            var vertexDataList = new List<VertexData>();

            double drainBaselineZFt = drainIndices.Count > 0
                ? drainIndices.Average(idx => vertices[idx].Position.Z)
                : 0;

            for (int i = 0; i < vertices.Count; i++)
            {
                double pathFt = distances[i];

                if (double.IsInfinity(pathFt) || pathFt > thresholdFt)
                {
                    skipped++;

                    if (data.ExportConfig?.IncludeVertexDetails == true)
                    {
                        vertexDataList.Add(new VertexData
                        {
                            VertexIndex       = i,
                            Position          = vertices[i].Position,
                            PathLengthMeters  = double.IsInfinity(pathFt) ? 0
                                : UnitUtils.ConvertFromInternalUnits(pathFt, UnitTypeId.Meters),
                            ElevationOffsetMm      = 0,
                            ElevationFromModel_mm  = 0,
                            NearestDrainIndex      = -1,
                            DirectionVector        = XYZ.Zero,
                            WasProcessed           = false
                        });
                    }
                    continue;
                }

                double elevFt = pathFt * slopeFactor;
                editor.ModifySubElement(vertices[i], elevFt);

                processed++;
                if (pathFt > maxPathFt) maxPathFt = pathFt;

                int nearestDrainIndex = FindNearestDrainIndex(vertices[i].Position, finalDrainPoints);
                XYZ directionVector   = nearestDrainIndex >= 0
                    ? CalculateDirectionVector(vertices[i].Position, finalDrainPoints[nearestDrainIndex])
                    : XYZ.Zero;

                vertexDataList.Add(new VertexData
                {
                    VertexIndex       = i,
                    Position          = vertices[i].Position,
                    PathLengthMeters  = UnitUtils.ConvertFromInternalUnits(pathFt, UnitTypeId.Meters),
                    ElevationOffsetMm = UnitUtils.ConvertFromInternalUnits(elevFt, UnitTypeId.Millimeters),
                    ElevationFromModel_mm = 0,
                    NearestDrainIndex = nearestDrainIndex,
                    DirectionVector   = directionVector,
                    WasProcessed      = true
                });
            }

            data.Progress?.Invoke(new RunProgressInfo("Applying elevations"));

            // ── Circle Markers ────────────────────────────────────────────────
            View activeView = app.ActiveUIDocument?.ActiveView;
            data.Progress?.Invoke(new RunProgressInfo("Placing circle markers"));
            CircleMarkerService.PlacementCounts markerCounts = CircleMarkerService.PlaceMarkers(
                doc,
                activeView,
                finalDrainPoints,
                vertexDataList,
                data.DrainMarkerGroup,
                data.HighestPointMarkerGroup,
                data.AllowedOffsetMarkerGroup,
                data.AllowedOffsetThresholdMm,
                data.EnableDrainTolerance ? data.DrainToleranceMm : 0,
                data.Log);

            // Regenerate in place of the original "Apply AutoSlope" tx commit,
            // so the re-read below reflects the vertex edits above.
            doc.Regenerate();

            // ── Re-read vertices from Revit after regenerate ──────────────────
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

            data.Log?.Invoke(new LogEntry(LogLevel.Success,
                $"Slope {data.SlopePercent}% applied — {processed} vertices processed, {skipped} skipped, " +
                $"highest {highest_mm}mm, longest path {longest_m:0.00}m."));

            return new GeometryApplyOutcome
            {
                Success           = true,
                Processed         = processed,
                Skipped           = skipped,
                Highest_mm        = highest_mm,
                MaxPathFt         = maxPathFt,
                Longest_m         = longest_m,
                DurationSec       = durationSec,
                RunDate           = runDate,
                FinalDrainPoints  = finalDrainPoints,
                VertexDataList    = vertexDataList,
                MarkerCounts      = markerCounts,
                BoundaryArcsCount = boundaryArcs?.Count ?? 0
            };
        }

        /// <summary>
        /// Writes AutoSlope_* parameters (via AutoSlopeParameterWriter's own
        /// Transaction) and runs Excel export. Call this only after the caller's
        /// geometry Transaction — the one ApplySlope ran inside of — has already
        /// committed, so no other Transaction is open on the document.
        /// </summary>
        public static AutoSlopeResult FinalizeAndExport(
            Document doc, Floor floor, AutoSlopePayload data, GeometryApplyOutcome geo)
        {
            string toolVersion = Revit26_Plugin_Revised.Shared.Services.ToolCatalog.FloorSlopeByPointMultiCopies.Version;

            int statusCode = AutoSlopeParameterWriter.WriteAll(
                doc, floor, data,
                geo.Highest_mm, geo.MaxPathFt,
                geo.Processed, geo.Skipped, geo.DurationSec,
                geo.FinalDrainPoints.Count,
                geo.RunDate,
                toolVersion);

            string compactPath = null;
            if (data.ExportConfig?.ExportToExcel == true)
            {
                compactPath = ExcelExportService.ExportCompactVertexData(
                    data, geo.VertexDataList, floor, data.SlopePercent,
                    toolVersion, statusCode);

                if (!string.IsNullOrEmpty(compactPath))
                {
                    data.Log?.Invoke(new LogEntry(LogLevel.Success,
                        $"✅ Compact Excel exported to: {compactPath}"));
                    data.Log?.Invoke(new LogEntry(LogLevel.Info,
                        $"  • Contains {geo.Processed} processed vertices"));
                }

                if (data.ExportConfig.IncludeVertexDetails)
                {
                    string detailedPath = ExcelExportService.ExportDetailedVertexData(
                        data, geo.VertexDataList, floor, geo.FinalDrainPoints, data.SlopePercent);

                    if (!string.IsNullOrEmpty(detailedPath))
                    {
                        data.Log?.Invoke(new LogEntry(LogLevel.Success,
                            $"✅ Detailed Excel exported to: {detailedPath}"));
                    }
                }
            }

            return new AutoSlopeResult
            {
                Success           = true,
                VerticesProcessed = geo.Processed,
                VerticesSkipped   = geo.Skipped,
                PickedDrainCount  = data.PickedDrainPoints?.Count ?? 0,
                FinalDrainCount   = geo.FinalDrainPoints.Count,
                HighestElevation_mm = geo.Highest_mm,
                LongestPath_m     = geo.Longest_m,
                RunDuration_sec   = geo.DurationSec,
                RunDate           = geo.RunDate,
                CurvesCalculated  = geo.BoundaryArcsCount,
                Version           = toolVersion,
                Status            = statusCode,
                ExportedFilePath  = compactPath,
                DrainCirclesPlaced   = geo.MarkerCounts?.DrainCirclesPlaced ?? 0,
                HighestCirclesPlaced = geo.MarkerCounts?.HighestCirclesPlaced ?? 0,
                OffsetCirclesPlaced  = geo.MarkerCounts?.OffsetCirclesPlaced ?? 0
            };
        }

        private static GeometryApplyOutcome Failure(AutoSlopePayload data, string reason)
        {
            data.Log?.Invoke(new LogEntry(LogLevel.Error, reason));
            return new GeometryApplyOutcome
            {
                Success      = false,
                ErrorMessage = reason
            };
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

        private static XYZ CalculateDirectionVector(XYZ fromPoint, XYZ toPoint)
        {
            if (fromPoint.DistanceTo(toPoint) < 0.001) return XYZ.Zero;
            return (toPoint - fromPoint).Normalize();
        }
    }

    /// <summary>
    /// Geometry-phase output of AutoSlopeEngine.ApplySlope — everything
    /// FinalizeAndExport needs to write parameters and run Excel export,
    /// without re-touching Revit geometry itself.
    /// </summary>
    public class GeometryApplyOutcome
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }

        public int Processed { get; set; }
        public int Skipped { get; set; }
        public int Highest_mm { get; set; }
        public double MaxPathFt { get; set; }
        public double Longest_m { get; set; }
        public int DurationSec { get; set; }
        public string RunDate { get; set; }
        public int BoundaryArcsCount { get; set; }

        public List<XYZ> FinalDrainPoints { get; set; }
        public List<VertexData> VertexDataList { get; set; }
        public CircleMarkerService.PlacementCounts MarkerCounts { get; set; }
    }
}
