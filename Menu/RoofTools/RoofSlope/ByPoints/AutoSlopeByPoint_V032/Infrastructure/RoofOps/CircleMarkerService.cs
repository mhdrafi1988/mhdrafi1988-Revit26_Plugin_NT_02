// =======================================================
// File: CircleMarkerService.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Infrastructure.RoofOps
// Moved from V030 Core/Engine (Revit API). Contract unchanged (per the
// confirmed V026 spec):
//   - Called inside the runner's open "Apply AutoSlope" transaction; it
//     never opens or commits its own.
//   - Highest Point uses the CALCULATED offset (the read-back value only
//     exists after commit). The runner now warns when Revit adjusted any
//     point, so a mismatch with the header tile is explained (D7).
//   - Active view must be a plan view, else it no-ops with a warning.
//   - Colour is a per-view override; line styles are never created.
// Changes vs V030:
//   - Works on MarkerStyle snapshots (immutable) — B7.
//   - Drain circles sit on the resolved drain shape points (D1/D2).
//   - Clustering is in plan (XY) via Core.Services.PointClustering — D3.
//   - 32.3.0: optional Offset text notes at the outer boundary corners
//     (request.BoundaryCorners), using each corner's calculated elevation.
// =======================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Infrastructure.RoofOps
{
    internal sealed class MarkerCounts
    {
        public int Drain { get; set; }
        public int Highest { get; set; }
        public int Offset { get; set; }
        public int CornerLabels { get; set; }
    }

    internal static class CircleMarkerService
    {
        public static MarkerCounts PlaceMarkers(
            Document doc,
            View activeView,
            IReadOnlyList<Point3> drainPoints,
            IReadOnlyList<VertexData> rows,
            AutoSlopeRequest request,
            Action<LogLevel, string> log)
        {
            var counts = new MarkerCounts();
            MarkerStyle drainStyle = Find(request, MarkerGroupKind.Drain);
            MarkerStyle highStyle = Find(request, MarkerGroupKind.HighestPoint);
            MarkerStyle offsetStyle = Find(request, MarkerGroupKind.AllowedOffset);

            if (drainStyle?.IsEnabled != true && highStyle?.IsEnabled != true && offsetStyle?.IsEnabled != true)
                return counts;

            if (activeView is not ViewPlan plan)
            {
                log(LogLevel.Warning, "Circle markers skipped: the active view is not a plan view.");
                return counts;
            }
            if (plan.SketchPlane == null && plan.GenLevel == null)
            {
                log(LogLevel.Warning, "Circle markers skipped: the active view has no level or sketch plane.");
                return counts;
            }

            double viewElevFt = plan.GenLevel?.Elevation ?? 0;
            double clusterFt = RevitConvert.MmToFt(request.EffectiveSnapRadiusMm);
            var processed = rows.Where(r => r.WasProcessed).ToList();

            // ── Drains ─────────────────────────────────────────────────────
            if (drainStyle?.IsEnabled == true && drainPoints.Count > 0)
            {
                var centres = PointClustering.ClusterXY(drainPoints, clusterFt);
                counts.Drain = centres.Count(p => PlaceOneCircle(doc, plan, p, viewElevFt, drainStyle, log));
                log(LogLevel.Success, $"Circle markers: {counts.Drain} drain circle(s) for {drainPoints.Count} drain point(s).");
            }

            // ── Highest point (every point tied at the max, to the whole mm) ──
            var highestIndices = new HashSet<int>();
            var highestCentres = new List<Point3>();
            if (highStyle?.IsEnabled == true && processed.Count > 0)
            {
                double maxMm = processed.Max(r => Math.Round(r.ElevationCalcMm));
                var highest = processed.Where(r => Math.Round(r.ElevationCalcMm) == maxMm).ToList();
                foreach (var r in highest) highestIndices.Add(r.VertexIndex);

                var centres = PointClustering.ClusterXY(highest.Select(r => r.Position), clusterFt);
                foreach (Point3 c in centres)
                {
                    highestCentres.Add(c);
                    if (PlaceOneCircle(doc, plan, c, viewElevFt, highStyle, log)) counts.Highest++;
                    if (highStyle.ShowOffsetText) PlaceOffsetLabel(doc, plan, c, viewElevFt, maxMm, "highest-point", log);
                }
                log(LogLevel.Success, $"Circle markers: {counts.Highest} highest-point circle(s) at {maxMm:0} mm.");

                // ── Offset notes at the outer boundary corners (32.3.0, off by default) ──
                if (highStyle.ShowOffsetText && highStyle.ShowCornerOffsetText)
                    counts.CornerLabels = PlaceCornerLabels(doc, plan, viewElevFt, rows, highestCentres, clusterFt, request, log);
            }

            // ── Allowed offset (excluding points already marked highest) ──
            if (offsetStyle?.IsEnabled == true)
            {
                double thr = request.AllowedOffsetThresholdMm;
                var qualifying = processed
                    .Where(r => Math.Round(r.ElevationCalcMm) >= thr && !highestIndices.Contains(r.VertexIndex))
                    .ToList();

                if (qualifying.Count == 0)
                {
                    log(LogLevel.Info, $"Circle markers: no points at or above {thr:0} mm.");
                }
                else
                {
                    var centres = PointClustering.ClusterXY(qualifying.Select(r => r.Position), clusterFt);
                    counts.Offset = centres.Count(p => PlaceOneCircle(doc, plan, p, viewElevFt, offsetStyle, log));
                    log(LogLevel.Success, $"Circle markers: {counts.Offset} allowed-offset circle(s) for {qualifying.Count} point(s) ≥ {thr:0} mm.");
                }
            }

            return counts;
        }

        private static MarkerStyle Find(AutoSlopeRequest request, MarkerGroupKind kind) =>
            request.Markers?.FirstOrDefault(m => m.Kind == kind);

        private static bool PlaceOneCircle(Document doc, View view, Point3 centrePt, double viewElevFt, MarkerStyle style, Action<LogLevel, string> log)
        {
            try
            {
                double radiusFt = RevitConvert.MmToFt(style.RadiusMm);
                if (radiusFt <= 0)
                {
                    log(LogLevel.Warning, $"Circle markers: {style.Label} radius must be greater than 0.");
                    return false;
                }

                // Flat on the view's level so the mark reads in plan regardless of roof height.
                var centre = new XYZ(centrePt.X, centrePt.Y, viewElevFt);

                // A single full-sweep Arc is rejected by Revit: build the circle from two halves.
                Arc halfA = Arc.Create(centre, radiusFt, 0, Math.PI, XYZ.BasisX, XYZ.BasisY);
                Arc halfB = Arc.Create(centre, radiusFt, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY);

                DetailCurve dcA = doc.Create.NewDetailCurve(view, halfA);
                DetailCurve dcB = doc.Create.NewDetailCurve(view, halfB);
                ApplyStyle(doc, view, dcA, style, log);
                ApplyStyle(doc, view, dcB, style, log);
                return true;
            }
            catch (Exception ex)
            {
                log(LogLevel.Warning, $"Circle markers: a {style.Label} circle could not be placed ({ex.Message}).");
                return false;
            }
        }

        private static void ApplyStyle(Document doc, View view, DetailCurve dc, MarkerStyle style, Action<LogLevel, string> log)
        {
            if (dc == null) return;

            if (style.LineStyleIdValue > 0 && doc.GetElement(new ElementId(style.LineStyleIdValue)) is GraphicsStyle gs)
            {
                try { dc.LineStyle = gs; }
                catch (Exception ex)
                {
                    log(LogLevel.Warning, $"Circle markers: line style '{style.LineStyleName}' could not be applied ({ex.Message}).");
                }
            }

            try
            {
                OverrideGraphicSettings ogs = view.GetElementOverrides(dc.Id) ?? new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(RevitConvert.ToRevitColor(style.ColorName));
                view.SetElementOverrides(dc.Id, ogs);
            }
            catch (Exception ex)
            {
                log(LogLevel.Warning, $"Circle markers: colour '{style.ColorName}' could not be applied ({ex.Message}).");
            }
        }

        /// <summary>
        /// One Offset note per outer boundary corner, at the elevation of the shape point at that corner.
        /// Corners that already carry the highest-point note, or that have no shape point within
        /// <see cref="AutoSlopeConstants.CornerLabelMatchMm"/>, are skipped. Returns the notes placed.
        /// </summary>
        private static int PlaceCornerLabels(
            Document doc, View view, double viewElevFt,
            IReadOnlyList<VertexData> rows, IReadOnlyList<Point3> highestCentres, double clusterFt,
            AutoSlopeRequest request, Action<LogLevel, string> log)
        {
            IReadOnlyList<Point3> corners = request.BoundaryCorners ?? new List<Point3>();
            if (corners.Count == 0)
            {
                log(LogLevel.Warning, "Offset notes: the roof's outer boundary corners could not be read; corner notes skipped.");
                return 0;
            }

            double matchFt = RevitConvert.MmToFt(AutoSlopeConstants.CornerLabelMatchMm);
            double sameSpotFt = Math.Max(clusterFt, matchFt);
            int placed = 0, unmatched = 0, shared = 0;

            foreach (Point3 corner in corners)
            {
                if (highestCentres.Any(h => h.DistanceXY(corner) <= sameSpotFt))
                {
                    shared++; // already has the highest-point note
                    continue;
                }

                VertexData nearest = null;
                double best = double.MaxValue;
                foreach (VertexData r in rows)
                {
                    double d = r.Position.DistanceXY(corner);
                    if (d < best) { best = d; nearest = r; }
                }
                if (nearest == null || best > matchFt)
                {
                    unmatched++;
                    continue;
                }

                if (PlaceOffsetLabel(doc, view, corner, viewElevFt, Math.Round(nearest.ElevationCalcMm), "corner", log))
                    placed++;
            }

            string extra = (shared > 0 ? $", {shared} already marked as highest" : string.Empty)
                         + (unmatched > 0 ? $", {unmatched} without a shape point at the corner" : string.Empty);
            log(LogLevel.Success, $"Offset notes: {placed} of {corners.Count} outer boundary corner(s){extra}.");
            if (unmatched > 0)
                log(LogLevel.Warning, $"Offset notes: {unmatched} corner(s) had no shape point within {AutoSlopeConstants.CornerLabelMatchMm:0} mm and got no note.");
            return placed;
        }

        private static bool PlaceOffsetLabel(Document doc, View view, Point3 centrePt, double viewElevFt, double offsetMm, string what, Action<LogLevel, string> log)
        {
            try
            {
                ElementId typeId = GetOrCreateRedTextNoteType(doc, log);
                if (typeId == null || typeId == ElementId.InvalidElementId)
                {
                    log(LogLevel.Warning, $"Circle markers: no text note type available; {what} label skipped.");
                    return false;
                }

                double gapFt = RevitConvert.MmToFt(AutoSlopeConstants.HighestPointLabelOffsetMm);
                var point = new XYZ(centrePt.X, centrePt.Y, viewElevFt);
                var textPos = new XYZ(point.X + gapFt, point.Y, point.Z);

                TextNote note = TextNote.Create(doc, view.Id, textPos, $"Offset: {offsetMm:0} mm", typeId);
                Leader leader = note.AddLeader(TextNoteLeaderTypes.TNLT_STRAIGHT_L);
                leader.End = point;
                return true;
            }
            catch (Exception ex)
            {
                log(LogLevel.Warning, $"Circle markers: a {what} label could not be placed ({ex.Message}).");
                return false;
            }
        }

        /// <summary>Reuses one red TextNoteType across runs instead of duplicating every time.</summary>
        private static ElementId GetOrCreateRedTextNoteType(Document doc, Action<LogLevel, string> log)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault(t => t.Name == AutoSlopeConstants.RedHighestPointTextTypeName);
            if (existing != null) return existing.Id;

            ElementId baseTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            if (baseTypeId == null || baseTypeId == ElementId.InvalidElementId) return null;
            if (doc.GetElement(baseTypeId) is not TextNoteType baseType) return null;
            if (baseType.Duplicate(AutoSlopeConstants.RedHighestPointTextTypeName) is not TextNoteType redType) return null;

            Parameter colorParam = redType.LookupParameter("Color");
            if (colorParam != null && !colorParam.IsReadOnly)
                colorParam.Set(255); // packed 0x00BBGGRR → pure red
            else
                log(LogLevel.Warning, "Circle markers: the text type has no settable Color; the label uses the default colour.");

            return redType.Id;
        }
    }
}
