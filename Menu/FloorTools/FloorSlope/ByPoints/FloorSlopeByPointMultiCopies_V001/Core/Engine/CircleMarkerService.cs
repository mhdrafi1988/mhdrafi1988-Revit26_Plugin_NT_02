// =======================================================
// File: CircleMarkerService.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001
// New in V026.
// Purpose: Places DetailCurve circles (2 half-arcs forming a closed
//          circle, planar to the active view's sketch plane) marking:
//            1. Drain group   — one circle per final drain point.
//            2. Highest group — one circle per vertex tied at max
//                                ElevationOffsetMm (calculated value,
//                                known DURING the transaction — see
//                                note below on why not
//                                ElevationFromModel_mm).
//            3. Offset group  — one circle per processed vertex with
//                                ElevationOffsetMm >= threshold,
//                                excluding vertices already circled
//                                as Highest Point (no double-circle).
//
// Contract:
//   - Must be called from inside an already-open Transaction
//     (AutoSlopeEngine's "Apply AutoSlope" tx) — this service does
//     NOT open/commit its own transaction, per confirmed spec
//     (single commit, single undo step for the whole Run).
//   - Because of that same-transaction requirement, the Highest Point
//     group compares ElevationOffsetMm (calculated: pathLength ×
//     slope) rather than ElevationFromModel_mm (which AutoSlopeEngine
//     only re-reads from Revit AFTER tx.Commit() — not yet available
//     while this service runs). Confirmed decision, not an oversight.
//   - Caller guarantees activeView is a ViewPlan; this service
//     re-checks defensively and no-ops (returns zero counts) if not,
//     so it can never partially place circles into an invalid view.
//   - Color is applied as a per-view DetailCurve graphic override
//     (OverrideGraphicSettings.SetProjectionLineColor), independent
//     of whatever color the chosen Line Style itself carries.
//   - Line Style must already exist in the project (OST_Lines
//     subcategory) — this service never creates new line styles.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Models;
using Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Engine
{
    public static class CircleMarkerService
    {
        public class PlacementCounts
        {
            public int DrainCirclesPlaced { get; set; }
            public int HighestCirclesPlaced { get; set; }
            public int OffsetCirclesPlaced { get; set; }
        }

        /// <summary>
        /// Places circles for all three marker groups. Must be called inside an
        /// already-open Transaction. Returns per-group placed counts (zero for any
        /// group that was disabled, had no qualifying points, or was skipped due
        /// to an invalid active view).
        /// </summary>
        public static PlacementCounts PlaceMarkers(
            Document doc,
            View activeView,
            List<XYZ> finalDrainPoints,
            List<VertexData> vertexDataList,
            CircleMarkerGroup drainGroup,
            CircleMarkerGroup highestGroup,
            CircleMarkerGroup offsetGroup,
            double allowedOffsetThresholdMm,
            double clusterToleranceMm,
            Action<LogEntry> log)
        {
            var counts = new PlacementCounts();

            // Points within this distance of each other (within the same group)
            // are merged into a single circle centered on their bounding box,
            // instead of each getting its own overlapping circle. A standalone
            // point (nothing else within tolerance) still gets its own circle.
            double clusterToleranceFt = clusterToleranceMm > 0
                ? UnitUtils.ConvertToInternalUnits(clusterToleranceMm, UnitTypeId.Millimeters)
                : 0;

            // ── Guard: active view must be a plan view ─────────────────────
            if (!(activeView is ViewPlan))
            {
                log?.Invoke(new LogEntry(LogLevel.Warning,
                    "Circle Markers: active view is not a plan view — skipping circle placement."));
                return counts;
            }

            // ── Guard: sketch plane needed for DetailCurve.Create ──────────
            if (activeView.SketchPlane == null && activeView.GenLevel == null)
            {
                log?.Invoke(new LogEntry(LogLevel.Warning,
                    "Circle Markers: active view has no usable level/sketch plane — skipping circle placement."));
                return counts;
            }

            double viewElevFt = GetViewElevationFt(activeView);

            // ── Group 1: Drains ─────────────────────────────────────────────
            if (drainGroup != null && drainGroup.IsEnabled)
            {
                if (finalDrainPoints == null || finalDrainPoints.Count == 0)
                {
                    log?.Invoke(new LogEntry(LogLevel.Info,
                        "Circle Markers: Drain group enabled but there are no final drain points to circle."));
                }
                else
                {
                    var clusteredDrainPoints = ClusterPoints(finalDrainPoints, clusterToleranceFt);
                    int placed = 0;
                    foreach (XYZ pt in clusteredDrainPoints)
                    {
                        if (pt == null) continue;
                        if (PlaceOneCircle(doc, activeView, pt, viewElevFt, drainGroup, log))
                            placed++;
                    }
                    counts.DrainCirclesPlaced = placed;
                    log?.Invoke(new LogEntry(LogLevel.Success,
                        $"Circle Markers: placed {placed} drain circle(s) for {finalDrainPoints.Count} point(s)."));
                }
            }

            // ── Group 2: Highest Point ───────────────────────────────────────
            // All vertices tied at max ElevationOffsetMm (not just the first).
            // Uses the calculated offset (pathLength × slope), not the post-commit
            // model re-read value — ElevationFromModel_mm is only known AFTER
            // tx.Commit(), but circle placement runs inside the same transaction
            // as the slope vertex writes (confirmed: one commit, one undo step).
            var highestVertices = new List<VertexData>();
            if (highestGroup != null && highestGroup.IsEnabled)
            {
                var processed = vertexDataList?.Where(v => v.WasProcessed).ToList() ?? new List<VertexData>();
                if (processed.Count == 0)
                {
                    log?.Invoke(new LogEntry(LogLevel.Info,
                        "Circle Markers: Highest Point group enabled but there are no processed vertices."));
                }
                else
                {
                    double maxElev = processed.Max(v => v.ElevationOffsetMm);
                    // Small epsilon guards against floating point rounding when
                    // comparing values that were rounded to whole mm in VertexData.
                    const double eps = 0.001;
                    highestVertices = processed
                        .Where(v => Math.Abs(v.ElevationOffsetMm - maxElev) <= eps)
                        .ToList();

                    var clusteredHighestPoints = ClusterPoints(
                        highestVertices.Select(v => v.Position).ToList(), clusterToleranceFt);
                    int placed = 0;
                    foreach (var pt in clusteredHighestPoints)
                    {
                        if (PlaceOneCircle(doc, activeView, pt, viewElevFt, highestGroup, log))
                            placed++;

                        if (highestGroup.ShowOffsetText)
                            PlaceHighestPointLabel(doc, activeView, pt, viewElevFt, maxElev, log);
                    }
                    counts.HighestCirclesPlaced = placed;
                    log?.Invoke(new LogEntry(LogLevel.Success,
                        $"Circle Markers: placed {placed} highest-point circle(s) for {highestVertices.Count} vertex/vertices at {maxElev:0} mm."));
                }
            }

            // ── Group 3: Allowed Offset ──────────────────────────────────────
            // Excludes vertices already circled as Highest Point — compared by
            // VertexIndex (stable identity from the engine's vertex collection).
            if (offsetGroup != null && offsetGroup.IsEnabled)
            {
                var highestIndices = new HashSet<int>(highestVertices.Select(v => v.VertexIndex));

                var qualifying = (vertexDataList ?? new List<VertexData>())
                    .Where(v => v.WasProcessed
                             && v.ElevationOffsetMm >= allowedOffsetThresholdMm
                             && !highestIndices.Contains(v.VertexIndex))
                    .ToList();

                if (qualifying.Count == 0)
                {
                    log?.Invoke(new LogEntry(LogLevel.Info,
                        $"Circle Markers: Allowed Offset group enabled but no vertices met the {allowedOffsetThresholdMm:0} mm threshold."));
                }
                else
                {
                    var clusteredOffsetPoints = ClusterPoints(
                        qualifying.Select(v => v.Position).ToList(), clusterToleranceFt);
                    int placed = 0;
                    foreach (var pt in clusteredOffsetPoints)
                    {
                        if (PlaceOneCircle(doc, activeView, pt, viewElevFt, offsetGroup, log))
                            placed++;
                    }
                    counts.OffsetCirclesPlaced = placed;
                    log?.Invoke(new LogEntry(LogLevel.Success,
                        $"Circle Markers: placed {placed} allowed-offset circle(s) for {qualifying.Count} vertex/vertices (>= {allowedOffsetThresholdMm:0} mm)."));
                }
            }

            return counts;
        }

        // ── Internal helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Merges points that lie within toleranceFt of another point in the
        /// same group (transitively — chained via union-find) into a single
        /// point at the center of that group's bounding box, so a cluster of
        /// nearby points gets one circle instead of several overlapping ones.
        /// A point with nothing else nearby is returned unchanged (standalone).
        /// </summary>
        private static List<XYZ> ClusterPoints(List<XYZ> points, double toleranceFt)
        {
            var valid = (points ?? new List<XYZ>()).Where(p => p != null).ToList();
            if (valid.Count <= 1 || toleranceFt <= 0)
                return valid;

            int n = valid.Count;
            int[] parent = Enumerable.Range(0, n).ToArray();
            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }
            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[ra] = rb;
            }

            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (valid[i].DistanceTo(valid[j]) <= toleranceFt)
                        Union(i, j);

            var groups = new Dictionary<int, List<XYZ>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!groups.TryGetValue(root, out var list))
                    groups[root] = list = new List<XYZ>();
                list.Add(valid[i]);
            }

            var centers = new List<XYZ>();
            foreach (var group in groups.Values)
            {
                double minX = group.Min(p => p.X), maxX = group.Max(p => p.X);
                double minY = group.Min(p => p.Y), maxY = group.Max(p => p.Y);
                double avgZ = group.Average(p => p.Z);
                centers.Add(new XYZ((minX + maxX) / 2.0, (minY + maxY) / 2.0, avgZ));
            }
            return centers;
        }

        /// <summary>
        /// Places a single circular DetailCurve centered at the XY of centerPt,
        /// flattened onto the view's working elevation. Applies the group's
        /// Line Style and per-view color override. Returns false (and logs a
        /// warning) if creation failed for this point, without throwing —
        /// one bad point should not abort the whole group.
        /// </summary>
        private static bool PlaceOneCircle(
            Document doc,
            View activeView,
            XYZ centerPt,
            double viewElevFt,
            CircleMarkerGroup style,
            Action<LogEntry> log)
        {
            try
            {
                double radiusFt = UnitUtils.ConvertToInternalUnits(style.RadiusMm, UnitTypeId.Millimeters);
                if (radiusFt <= 0)
                {
                    log?.Invoke(new LogEntry(LogLevel.Warning,
                        $"Circle Markers: skipped a {style.GroupLabel} circle — radius must be greater than 0."));
                    return false;
                }

                // Flatten the circle onto the plan view's working elevation so it
                // reads correctly regardless of the source point's true Z (floor
                // vertices carry slope elevation; the marker should sit on the
                // view's reference plane, not float at floor height).
                XYZ center = new XYZ(centerPt.X, centerPt.Y, viewElevFt);

                // Revit rejects a single Arc.Create with a full 2π sweep (start/end
                // coincide) — build the closed circle from two half-arcs instead,
                // which is the standard workaround for a closed circular DetailCurve.
                Arc halfA = Arc.Create(center, radiusFt, 0, Math.PI, XYZ.BasisX, XYZ.BasisY);
                Arc halfB = Arc.Create(center, radiusFt, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY);

                DetailCurve dcA = doc.Create.NewDetailCurve(activeView, halfA);
                DetailCurve dcB = doc.Create.NewDetailCurve(activeView, halfB);

                ApplyStyle(doc, activeView, dcA, style, log);
                ApplyStyle(doc, activeView, dcB, style, log);

                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke(new LogEntry(LogLevel.Warning,
                    $"Circle Markers: failed to place a {style.GroupLabel} circle — {ex.Message}"));
                return false;
            }
        }

        private const string RedHighestPointTextTypeName = "AutoSlope Highest Point (Red)";
        private const double HighestPointLabelOffsetMm = 250;

        /// <summary>
        /// Places a red TextNote reading "Offset: {value} mm" with a straight
        /// leader pointing back at the Highest Point vertex — showing the
        /// calculated offset (pathLength × slope), the same figure logged as the
        /// group's max elevation. The text sits a fixed 250 mm away from the
        /// point regardless of the circle's radius. Best-effort: a failure here
        /// is logged as a warning but does not remove the already-placed circle.
        /// </summary>
        private static void PlaceHighestPointLabel(
            Document doc,
            View activeView,
            XYZ centerPt,
            double viewElevFt,
            double offsetMm,
            Action<LogEntry> log)
        {
            try
            {
                ElementId textTypeId = GetOrCreateRedTextNoteType(doc, log);
                if (textTypeId == null || textTypeId == ElementId.InvalidElementId)
                {
                    log?.Invoke(new LogEntry(LogLevel.Warning,
                        "Circle Markers: no text note type available — skipped highest-point label."));
                    return;
                }

                double gapFt = UnitUtils.ConvertToInternalUnits(HighestPointLabelOffsetMm, UnitTypeId.Millimeters);
                XYZ point = new XYZ(centerPt.X, centerPt.Y, viewElevFt);
                XYZ textPos = new XYZ(point.X + gapFt, point.Y, point.Z);

                TextNote note = TextNote.Create(doc, activeView.Id, textPos, $"Offset: {offsetMm:0} mm", textTypeId);

                // Point is to the left of the text box, so the leader attaches
                // on the text's left side and its arrow end goes back to the point.
                Leader leader = note.AddLeader(TextNoteLeaderTypes.TNLT_STRAIGHT_L);
                leader.End = point;
            }
            catch (Exception ex)
            {
                log?.Invoke(new LogEntry(LogLevel.Warning,
                    $"Circle Markers: failed to place highest-point label — {ex.Message}"));
            }
        }

        /// <summary>
        /// Returns the Id of a red-text TextNoteType, creating it once (by
        /// duplicating the project's default text note type and setting its
        /// Color parameter) and reusing it on every subsequent run so repeated
        /// AutoSlope runs don't pile up duplicate types.
        /// </summary>
        private static ElementId GetOrCreateRedTextNoteType(Document doc, Action<LogEntry> log)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault(t => t.Name == RedHighestPointTextTypeName);
            if (existing != null) return existing.Id;

            ElementId baseTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            if (baseTypeId == null || baseTypeId == ElementId.InvalidElementId) return null;
            if (!(doc.GetElement(baseTypeId) is TextNoteType baseType)) return null;

            if (!(baseType.Duplicate(RedHighestPointTextTypeName) is TextNoteType redType)) return null;

            Parameter colorParam = redType.LookupParameter("Color");
            if (colorParam != null && !colorParam.IsReadOnly)
            {
                // Revit stores color parameters as a packed 0x00BBGGRR integer.
                const int red = 255, green = 0, blue = 0;
                colorParam.Set(red | (green << 8) | (blue << 16));
            }
            else
            {
                log?.Invoke(new LogEntry(LogLevel.Warning,
                    "Circle Markers: could not find a settable Color parameter on the text note type — label will use the default type color."));
            }

            return redType.Id;
        }

        /// <summary>
        /// Assigns the chosen Line Style (existing OST_Lines subcategory) as the
        /// element's line-style, and applies the chosen named color as a per-view
        /// graphic override. Both are best-effort — a failure here is logged as a
        /// warning but does not remove the already-placed geometry.
        /// </summary>
        private static void ApplyStyle(
            Document doc,
            View activeView,
            DetailCurve dc,
            CircleMarkerGroup style,
            Action<LogEntry> log)
        {
            if (dc == null) return;

            // Line Style — set via the element's LineStyle property using the
            // GraphicsStyle looked up from style.LineStyleId.
            if (style.LineStyleId != null && style.LineStyleId != ElementId.InvalidElementId)
            {
                if (doc.GetElement(style.LineStyleId) is GraphicsStyle gs)
                {
                    try { dc.LineStyle = gs; }
                    catch (Exception ex)
                    {
                        log?.Invoke(new LogEntry(LogLevel.Warning,
                            $"Circle Markers: could not apply line style '{style.LineStyleName}' — {ex.Message}"));
                    }
                }
            }

            // Color — per-view projection line color override, independent of
            // the line style's own defined color.
            Color revitColor = NamedColorHelper.ToRevitColor(style.ColorName);
            if (revitColor != null)
            {
                try
                {
                    OverrideGraphicSettings ogs = activeView.GetElementOverrides(dc.Id) ?? new OverrideGraphicSettings();
                    ogs.SetProjectionLineColor(revitColor);
                    activeView.SetElementOverrides(dc.Id, ogs);
                }
                catch (Exception ex)
                {
                    log?.Invoke(new LogEntry(LogLevel.Warning,
                        $"Circle Markers: could not apply color '{style.ColorName}' — {ex.Message}"));
                }
            }
        }

        /// <summary>
        /// Working elevation (internal feet) to flatten markers onto: the active
        /// plan view's associated level elevation, or the view's GenLevel if the
        /// simpler property isn't available.
        /// </summary>
        private static double GetViewElevationFt(View activeView)
        {
            if (activeView is ViewPlan vp && vp.GenLevel != null)
                return vp.GenLevel.Elevation;
            return 0;
        }
    }
}
