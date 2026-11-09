// =======================================================
// File: PerpendicularPointService.cs
// Location: Core/Services/
// Ported unchanged from V003 (namespace updated only).
// GenerateCandidates is called from InnerLoopsAndPerpendicularEngine
// (read-only — no transaction). ApplyPerpendicularPoints is called
// inside the TransactionGroup opened by
// InnerLoopsAndPerpendicularHandler for Apply — this service's own
// inner Transaction nests inside it (standard nested Transaction/
// TransactionGroup pattern).
// =======================================================

using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.InnerLoopsAndPerpendicular.V007.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.InnerLoopsAndPerpendicular.V007.Core.Services
{
    /// <summary>
    /// For a selected opening (any shape — Circular/Rectangle/Other), pools its
    /// SlabShapeEditor vertices + loop geometric corner points, clusters them into
    /// groups by XY proximity and matching Z, then for each group casts perpendicular
    /// feet onto the roof's OUTER boundary in up to 8 directions (relative to the
    /// opening's own local orientation), keeping the nearest curve per direction.
    /// No TaskDialogs are raised — results are returned to the caller for log display.
    /// </summary>
    public class PerpendicularPointService
    {
        /// <summary>Max Z difference (internal units, ~5mm) for two pool points to belong to the same group.</summary>
        private static readonly double GroupZToleranceInternal = UnitUtils.ConvertToInternalUnits(5.0, UnitTypeId.Millimeters);

        /// <summary>
        /// Generates one candidate per occupied direction sector (min 4 if available, max 8) per
        /// XY/Z-clustered point group on the opening.
        /// </summary>
        /// <param name="roof">The roof element (used to read existing SlabShapeVertices for the point pool).</param>
        /// <param name="openingLoop">The selected opening's boundary loop (any shape).</param>
        /// <param name="outerLoop">The roof's outer boundary loop.</param>
        /// <param name="edgeToleranceMm">Distance (mm) used to decide whether an existing SlabShapeVertex sits on this opening's edge.</param>
        /// <param name="groupProximityMm">XY distance (mm) used to cluster pool points into groups.</param>
        public List<PerpendicularPointModel> GenerateCandidates(
            RoofBase roof, RoofLoopModel openingLoop, RoofLoopModel outerLoop, double edgeToleranceMm, double groupProximityMm)
        {
            var results = new List<PerpendicularPointModel>();

            if (roof == null || openingLoop?.Geometry == null || outerLoop?.Geometry == null)
                return results;

            var outerCurves = outerLoop.Geometry.ToList();
            double edgeToleranceInternal = UnitUtils.ConvertToInternalUnits(edgeToleranceMm, UnitTypeId.Millimeters);
            double groupProximityInternal = UnitUtils.ConvertToInternalUnits(groupProximityMm, UnitTypeId.Millimeters);

            // ── 1. Build the point pool: existing SlabShapeVertices on this opening's edge + the opening's own geometric corner points ──
            var pool = new List<XYZ>();

            var editor = roof.GetSlabShapeEditor();
            if (editor != null && editor.IsEnabled)
            {
                foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                {
                    bool onThisOpening = openingLoop.Geometry.Any(c => c.Project(v.Position)?.Distance <= edgeToleranceInternal);
                    if (onThisOpening)
                        pool.Add(v.Position);
                }
            }

            foreach (Curve c in openingLoop.Geometry)
            {
                if (c.IsBound)
                    pool.Add(c.GetEndPoint(0));
            }

            if (pool.Count == 0)
                return results;

            // ── 2. Cluster pool points by XY proximity + matching Z (greedy single-pass) ──
            var groups = new List<List<XYZ>>();
            foreach (var pt in pool)
            {
                var match = groups.FirstOrDefault(g =>
                    HorizontalDistance(GroupCentroid(g), pt) <= groupProximityInternal &&
                    Math.Abs(g.Min(p => p.Z) - pt.Z) <= GroupZToleranceInternal);

                if (match != null) match.Add(pt);
                else groups.Add(new List<XYZ> { pt });
            }

            // ── 3. Determine the opening's local orientation (angle of its first straight edge) ──
            var firstLine = openingLoop.Geometry.OfType<Line>().FirstOrDefault();
            XYZ dir = firstLine != null ? (firstLine.GetEndPoint(1) - firstLine.GetEndPoint(0)) : XYZ.BasisX;
            double orientationAngle = Math.Atan2(dir.Y, dir.X);

            // ── 4. Per group: cast perpendicular feet onto the outer boundary, bucketed into 8 directions relative to orientation ──
            for (int g = 0; g < groups.Count; g++)
            {
                ToolCancellation.CheckCurrent();
                var group = groups[g];
                XYZ centroid = GroupCentroid(group);
                double elevationZ = group.Min(p => p.Z);

                // sector index (0-7) -> nearest (curve, point, distance) in that direction from the centroid
                var sectorHits = new Dictionary<int, (Curve Curve, XYZ Point, double Distance)>();

                foreach (Curve outerCurve in outerCurves)
                {
                    IntersectionResult ir = outerCurve.Project(centroid);
                    if (ir == null) continue;

                    XYZ toHit = new XYZ(ir.XYZPoint.X - centroid.X, ir.XYZPoint.Y - centroid.Y, 0);
                    if (toHit.GetLength() < 1e-9) continue;

                    double angle = Math.Atan2(toHit.Y, toHit.X) - orientationAngle;
                    int sector = ((int)Math.Round(NormalizeAngle(angle) / (Math.PI / 4))) % 8;

                    if (!sectorHits.TryGetValue(sector, out var existing) || ir.Distance < existing.Distance)
                        sectorHits[sector] = (outerCurve, ir.XYZPoint, ir.Distance);
                }

                // up to 8 directions, ordered nearest-first
                var ordered = sectorHits.OrderBy(kv => kv.Value.Distance).ToList();

                foreach (var kv in ordered)
                {
                    var hit = kv.Value;
                    var model = new PerpendicularPointModel
                    {
                        ShapeIndex = openingLoop.Index,
                        Side = string.Empty,
                        CandidateCount = ordered.Count,
                        BorderPoint = hit.Point,
                        DistanceMm = UnitUtils.ConvertFromInternalUnits(hit.Distance, UnitTypeId.Millimeters),
                        ElevationInternal = elevationZ,
                        Direction = ComputeInwardDirection(hit.Point, centroid)
                    };
                    results.Add(model);
                }

                if (ordered.Count < 4)
                    results.Add(new PerpendicularPointModel
                    {
                        ShapeIndex = openingLoop.Index,
                        Side = string.Empty,
                        CandidateCount = ordered.Count
                        // BorderPoint left null -> IsValid == false -> logged as "no candidate" by caller
                    });
            }

            return results;
        }

        private static double NormalizeAngle(double a)
        {
            while (a < 0) a += 2 * Math.PI;
            while (a >= 2 * Math.PI) a -= 2 * Math.PI;
            return a;
        }

        private static XYZ GroupCentroid(List<XYZ> group)
            => new XYZ(group.Average(p => p.X), group.Average(p => p.Y), group.Average(p => p.Z));

        private double HorizontalDistance(XYZ a, XYZ b)
            => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

        /// <summary>
        /// Unit XY vector from <paramref name="from"/> (border point) toward
        /// <paramref name="to"/> (rectangular loop side midpoint, which sits inside the
        /// outer boundary). Returns null if the two points coincide.
        /// </summary>
        private XYZ ComputeInwardDirection(XYZ from, XYZ to)
        {
            XYZ delta = new XYZ(to.X - from.X, to.Y - from.Y, 0);
            return delta.GetLength() < 1e-9 ? null : delta.Normalize();
        }

        /// <summary>
        /// Inward nudge distances (mm) tried in order when a point lands exactly on, or
        /// too close to, the face boundary and AddPoint rejects it. 0 = original snap.
        /// </summary>
        private static readonly double[] NudgeStepsMm = { 0, 3, 8, 15, 30, 60 };

        /// <summary>
        /// Applies the selected, valid perpendicular points to the roof via the slab shape editor.
        /// Border points are nudged inward in increasing steps if Revit rejects the
        /// zero-offset position. <paramref name="details"/> receives one line per point
        /// describing the outcome (success offset, or final rejection reason).
        /// </summary>
        /// <returns>Number of points successfully added, or -1 on transaction failure.</returns>
        public int ApplyPerpendicularPoints(Document doc, RoofBase roof, IEnumerable<PerpendicularPointModel> points, out List<string> details)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (roof == null) throw new ArgumentNullException(nameof(roof));
            if (points == null) throw new ArgumentNullException(nameof(points));

            details = new List<string>();
            var validPoints = points.Where(p => p != null && p.IsSelected && p.IsValid).ToList();
            if (!validPoints.Any()) return 0;

            try
            {
                using (Transaction tx = new Transaction(doc, "Add Perpendicular Points"))
                {
                    tx.Start();

                    var editor = roof.GetSlabShapeEditor();
                    if (!editor.IsEnabled)
                        editor.Enable();

                    int pointsAdded = 0;

                    foreach (var p in validPoints)
                    {
                        ToolCancellation.CheckCurrent();
                        string lastError = null;
                        bool placed = false;

                        foreach (double nudgeMm in NudgeStepsMm)
                        {
                            XYZ xy = p.BorderPoint;
                            if (nudgeMm > 0 && p.Direction != null)
                            {
                                double nudgeInternal = UnitUtils.ConvertToInternalUnits(nudgeMm, UnitTypeId.Millimeters);
                                xy = new XYZ(p.BorderPoint.X + p.Direction.X * nudgeInternal,
                                             p.BorderPoint.Y + p.Direction.Y * nudgeInternal,
                                             p.BorderPoint.Z);
                            }
                            else if (nudgeMm > 0)
                            {
                                break; // no direction available — further nudges are pointless
                            }

                            XYZ point = new XYZ(xy.X, xy.Y, p.ElevationInternal);

                            try
                            {
                                editor.AddPoint(point);
                                pointsAdded++;
                                placed = true;
                                details.Add(nudgeMm == 0
                                    ? $"Shape #{p.ShapeIndex}: placed at border (0mm offset)."
                                    : $"Shape #{p.ShapeIndex}: placed after {nudgeMm:0}mm inward nudge.");
                                break;
                            }
                            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                            {
                                lastError = ex.Message;
                            }
                        }

                        if (!placed)
                            details.Add($"Shape #{p.ShapeIndex}: rejected at every offset up to {NudgeStepsMm.Last():0}mm — {lastError ?? "unknown reason"}.");
                    }

                    tx.Commit();
                    return pointsAdded;
                }
            }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
