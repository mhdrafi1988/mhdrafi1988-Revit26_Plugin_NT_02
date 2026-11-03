// File: DrainDetectionService.cs
// Location: Core/Services/
//
// UPDATED (V005), per Rafi's confirmed decisions on 2026-07-22 (second round):
//   1. Size filter (5-2000mm) REMOVED entirely — every opening loop that
//      survives dedup and shape classification becomes a DrainItem
//      regardless of its bounding-box dimensions.
//   2. Shape-edit vertex matching (X,Y -> Z) is DEFERRED — no longer run
//      during detection. DetectDrainsFromFloor now populates every DrainItem
//      with an EMPTY DrainVertices list; the grid can still show Shape/Size
//      immediately since those come from the loop's own geometry, not from
//      vertex matching. FindShapeVerticesAtLoopPoints is now PUBLIC so
//      AutoSlopeDrainEngine can call it later, at Run time, only for the
//      DrainItems the user actually selected — see that file for where the
//      matching now happens.
//
// Original rewrite (V005, first round): detection comes from the floor's
// Sketch profile (Floor only), not raw solid/face geometry — ported
// from Revit26_Plugin_Revised.RoofTools.LineAndPoints.RoofRidgeLines.V67's
// InnerLoopService + OpeningAnalyzerService, adapted to still produce
// DrainItem so every downstream consumer (FloorSlopeProcessorService, the
// ViewModel grid, Excel export, parameter writer) is untouched.
//
// Current flow:
//   1. Read the floor's dependent Sketch. Compute each profile loop's area
//      (Shoelace); largest = outer boundary (discarded), rest = opening
//      candidates.
//   2. Tessellate each candidate loop to a flattened (Z=0) XYZ point list.
//   3. DEDUPLICATE THE LOOPS THEMSELVES by centroid proximity — happens
//      BEFORE classification, not as a cleanup pass at the end.
//   4. Classify each surviving unique loop: Circle (loop's real Arc/Ellipse
//      curves share a common center/radius — checked on the ORIGINAL curves,
//      not tessellated point count; see ClassifyShape/TryArcBasedCircle for
//      why point-count-based detection was unreliable for small circles),
//      Rectangle (4 tessellated vertices, adjacent edges perpendicular), else
//      Other/Polygon. Centroid (used only by the tessellated-point fallback
//      circle test) = bounding-box center.
//   5. Build a DrainItem with EMPTY DrainVertices — vertex matching deferred
//      to Run time (see AutoSlopeDrainEngine).
//
// REMOVED: the old raw-geometry path (GetUpwardFaces / DrainsFromFaceLoops /
// ProjectToFace / TryArcClusterCircle / TryBestFitCircle) — no longer used,
// since detection is Sketch-first only now. Floor-without-a-sketch
// or non-Floor cases are excluded at the Command's FloorFilter level
// (floor picker only allows Floor), so there is no "no sketch found"
// fallback path to support here.
//
// UNCHANGED: GenerateSizeCategories / FilterDrainsBySize / FindVerticesForDrain
// (operate on DrainItem post-detection, no dependency on how detection works).

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Engine;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Services
{
    public class DrainDetectionService
    {
        private const double LOOP_DEDUP_TOL_FT = 0.01; // ~3mm, matches the old RemoveDuplicateDrains tolerance

        /// <summary>5mm tolerance used by FindShapeVerticesAtLoopPoints (now called at Run time, not during detection).</summary>
        public const double VertexMatchToleranceMm = 5.0;

        // DRAIN-DEBUG counters — reset per DetectDrainsFromFloor call, used only for the
        // optional diagnostic summary. Do not affect detection results.
        private int _dbgLoopsFound, _dbgLoopsAfterDedup, _dbgRejectedDegenerate,
                    _dbgRejectedException, _dbgAccepted;

        /// <param name="allShapeVertices">
        /// UNUSED as of this update (V005, second round) — kept as a parameter only
        /// for call-site compatibility with existing Command/Engine code that still
        /// passes it in. Vertex matching against this list now happens later, at
        /// Run time, via FindShapeVerticesAtLoopPoints, not during detection.
        /// </param>
        /// <param name="log">
        /// Optional diagnostic sink (DRAIN-DEBUG). Purely additive — passing null (the
        /// default) reproduces the exact prior behavior with zero extra logging.
        /// </param>
        public List<DrainItem> DetectDrainsFromFloor(Floor floor, Face ignoredTopFace, List<SlabShapeVertex> allShapeVertices = null, Action<LogEntry> log = null)
        {
            var drains = new List<DrainItem>();

            _dbgLoopsFound = _dbgLoopsAfterDedup = _dbgRejectedDegenerate =
                _dbgRejectedException = _dbgAccepted = 0;

            try
            {
                if (!(floor is Floor))
                {
                    log?.Invoke(new LogEntry(LogLevel.Error,
                        "DRAIN-DEBUG: floor is not a Floor — Sketch-based opening detection requires a sketch-based floor. " +
                        "(This should not be reachable — the floor picker restricts selection to Floor.)"));
                    return drains;
                }

                var loopCandidates = ExtractOpeningLoopsFromSketch(floor, log);
                _dbgLoopsFound = loopCandidates.Count;

                var uniqueLoops = DeduplicateLoops(loopCandidates);
                _dbgLoopsAfterDedup = uniqueLoops.Count;

                foreach (var (originalLoop, loopPts) in uniqueLoops)
                {
                    var drain = BuildDrainFromLoop(originalLoop, loopPts, log);
                    if (drain != null)
                    {
                        _dbgAccepted++;
                        drains.Add(drain);
                    }
                }

                drains = drains.OrderBy(d => d.Width * d.Height).ToList();

                // Floor V001: drain families (Plumbing Fixtures) on the floor.
                drains.AddRange(DetectFixtureDrains(floor, ignoredTopFace, log));

                log?.Invoke(new LogEntry(LogLevel.Info,
                    $"DRAIN-DEBUG summary: {_dbgLoopsFound} opening loop(s) found in sketch, " +
                    $"{_dbgLoopsFound - _dbgLoopsAfterDedup} duplicate(s) removed, " +
                    $"{_dbgLoopsAfterDedup} unique loop(s) examined -> {_dbgAccepted} accepted, " +
                    $"{_dbgRejectedDegenerate} rejected (fewer than 3 boundary points), " +
                    $"{_dbgRejectedException} rejected (exception during processing). " +
                    "Shape-edit vertex matching is now deferred to Run time for selected drains only."));

                return drains;
            }
            catch (Exception ex)
            {
                throw new Exception($"Drain detection failed: {ex.Message}");
            }
        }

        // ── Step 1-2: Sketch profile → opening loop candidates ──────────────────

        /// <summary>
        /// Reads the floor's dependent Sketch, computes each profile loop's area,
        /// discards the largest (outer boundary), and returns the rest as
        /// (original CurveArray, tessellated Z=0 points) pairs. The original
        /// CurveArray is kept alongside the tessellated points specifically so
        /// shape classification can inspect real Arc/Ellipse curve types — see
        /// ClassifyShape's circle test, which tessellated-point-count alone
        /// cannot reliably detect for small-radius circles (Revit's adaptive
        /// tessellation of a small arc can produce well under 12 points).
        /// </summary>
        private List<(CurveArray originalLoop, List<XYZ> pts)> ExtractOpeningLoopsFromSketch(Floor floor, Action<LogEntry> log)
        {
            var doc = floor.Document;

            // A floor names its own sketch; fall back to the dependent-element
            // search the roof tool used if that id is missing.
            var dependentIds = new List<ElementId>();
            if (floor.SketchId != null && floor.SketchId != ElementId.InvalidElementId)
                dependentIds.Add(floor.SketchId);
            else
                dependentIds.AddRange(floor.GetDependentElements(new ElementClassFilter(typeof(Sketch))));

            foreach (ElementId id in dependentIds)
            {
                if (!(doc.GetElement(id) is Sketch sketch)) continue;

                var loops = new List<(CurveArray loop, double area)>();
                foreach (CurveArray loop in sketch.Profile)
                {
                    double area = SketchGeometryHelper.ApproximateLoopArea(loop);
                    loops.Add((loop, area));
                }

                if (loops.Count <= 1)
                {
                    log?.Invoke(new LogEntry(LogLevel.Info, "DRAIN-DEBUG: sketch has only one loop (outer boundary) — no openings."));
                    return new List<(CurveArray, List<XYZ>)>();
                }

                double maxArea = loops.Max(l => l.area);
                return loops
                    .Where(l => l.area < maxArea)
                    .Select(l => (l.loop, pts: SketchGeometryHelper.TessellateLoop(l.loop)))
                    .Where(l => l.pts.Count >= 3)
                    .ToList();
            }

            log?.Invoke(new LogEntry(LogLevel.Warning, "DRAIN-DEBUG: no Sketch element found on this floor — no openings detected."));
            return new List<(CurveArray, List<XYZ>)>();
        }

        // ── Step 3: dedup at the loop level, BEFORE classification/vertex-matching ──

        /// <summary>
        /// Removes duplicate opening loops by bounding-box-center proximity, before any
        /// classification or shape-edit-vertex matching runs — confirmed by Rafi to
        /// happen here, not as an end-of-pipeline cleanup, so no duplicate work is
        /// done downstream. Operates on (original CurveArray, tessellated points)
        /// pairs so the original loop survives dedup for later shape classification.
        /// </summary>
        private List<(CurveArray originalLoop, List<XYZ> pts)> DeduplicateLoops(List<(CurveArray originalLoop, List<XYZ> pts)> loops)
        {
            var unique = new List<(CurveArray, List<XYZ>)>();
            var centers = new List<XYZ>();

            foreach (var (originalLoop, pts) in loops)
            {
                XYZ center = BoundingBoxCenter(pts);
                bool isDuplicate = centers.Any(c => c.DistanceTo(center) < LOOP_DEDUP_TOL_FT);
                if (!isDuplicate)
                {
                    unique.Add((originalLoop, pts));
                    centers.Add(center);
                }
            }

            return unique;
        }

        // ── Step 4: classify a single loop's shape ──────────────────────────────

        /// <summary>
        /// Classifies a loop's shape (Rectangle/Circle/Square/Polygon).
        /// No size filtering — every loop that reaches here becomes a DrainItem,
        /// per Rafi's confirmed removal of the 5-2000mm bounding-box filter.
        /// </summary>
        private (string shape, double widthMm, double heightMm) Classify(CurveArray originalLoop, List<XYZ> pts)
        {
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            double widthMm = (maxX - minX) * 304.8;
            double heightMm = (maxY - minY) * 304.8;

            string shape = ClassifyShape(originalLoop, pts, widthMm, heightMm);
            return (shape, widthMm, heightMm);
        }

        /// <summary>
        /// Rectangle: exactly 4 tessellated vertices, consecutive edges perpendicular.
        ///   (Further split into "Square" here, matching this tool's existing
        ///   shape-name convention, when width and height are within 5mm.)
        /// Circle: tried FIRST via TryArcBasedCircle, which inspects the loop's
        ///   REAL (pre-tessellation) Arc/Ellipse curves directly — see that
        ///   method's comment for why tessellated-point-count alone is unreliable
        ///   for small-radius circles. Falls back to a tessellated-point stddev
        ///   heuristic only if the loop has no usable curve data (shouldn't
        ///   normally happen for a Sketch profile, but kept as a safety net).
        /// Other: neither test passes ("Polygon", matching this tool's existing naming).
        /// </summary>
        private string ClassifyShape(CurveArray originalLoop, List<XYZ> pts, double widthMm, double heightMm)
        {
            // Circle test runs FIRST, on the loop's real (pre-tessellation) curves —
            // fixes a real bug where small-radius circles tessellate to well under
            // 12 points and never reached the old stddev-based circle test at all.
            if (TryArcBasedCircle(originalLoop))
                return "Circle";

            if (pts.Count == 4)
            {
                bool isRect = true;
                for (int i = 0; i < 4; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % 4];
                    var c = pts[(i + 2) % 4];
                    XYZ e1 = new XYZ(b.X - a.X, b.Y - a.Y, 0);
                    XYZ e2 = new XYZ(c.X - b.X, c.Y - b.Y, 0);
                    double dot = e1.X * e2.X + e1.Y * e2.Y;
                    if (Math.Abs(dot) > 1e-6) { isRect = false; break; }
                }

                if (isRect)
                    return Math.Abs(widthMm - heightMm) < 5 ? "Square" : "Rectangle";

                return "Polygon";
            }

            // Fallback stddev-based circle test — only reachable if the real-curve
            // test above was inconclusive (e.g. mixed arc/line loop that isn't a
            // clean circle by curve type, but still tessellates round). Kept as a
            // safety net; TryArcBasedCircle should catch genuine circles before
            // execution reaches here.
            if (pts.Count >= 12)
            {
                double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
                double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                var centroid = new XYZ((minX + maxX) / 2.0, (minY + maxY) / 2.0, 0);

                double avgDist = pts.Average(p => SketchGeometryHelper.Dist2D(p, centroid));
                if (avgDist > 1e-9)
                {
                    double stdDev = Math.Sqrt(pts.Average(p =>
                        Math.Pow(SketchGeometryHelper.Dist2D(p, centroid) - avgDist, 2)));
                    if (stdDev / avgDist < 0.05)
                        return "Circle";
                }
            }

            return "Polygon";
        }

        /// <summary>
        /// Real-curve circle test: a loop is a circle if every curve in it is an
        /// Arc (or Ellipse with equal radii — a true circle, not an oval) sharing a
        /// common center and radius within a small tolerance. This works
        /// regardless of tessellation density, unlike the stddev-on-tessellated-
        /// points approach, which fails for small-radius circles that Revit
        /// tessellates to very few points (well under the old 12-point gate).
        /// Handles both the single-Arc/single-Ellipse case (a circle drawn as one
        /// closed curve) and the multi-Arc case (a circle drawn as 2+ arc segments).
        /// </summary>
        private bool TryArcBasedCircle(CurveArray originalLoop)
        {
            if (originalLoop == null) return false;

            const double centerTolFt = 5.0 / 304.8; // 5mm, matches this service's other tolerances
            const double radiusTolFt = 5.0 / 304.8;

            XYZ refCenter = null;
            double refRadius = -1;
            int curveCount = 0;

            foreach (Curve c in originalLoop)
            {
                curveCount++;

                XYZ center;
                double radius;

                if (c is Arc arc)
                {
                    center = arc.Center;
                    radius = arc.Radius;
                }
                else if (c is Ellipse ell)
                {
                    // Only a true circle (equal radii) counts — an oval opening
                    // should not be classified as "Circle".
                    if (Math.Abs(ell.RadiusX - ell.RadiusY) > radiusTolFt) return false;
                    center = ell.Center;
                    radius = ell.RadiusX;
                }
                else
                {
                    // Any Line (or other non-circular curve type) in the loop means
                    // this is not a pure circle.
                    return false;
                }

                if (refCenter == null)
                {
                    refCenter = center;
                    refRadius = radius;
                }
                else if (center.DistanceTo(refCenter) > centerTolFt || Math.Abs(radius - refRadius) > radiusTolFt)
                {
                    return false; // curves don't share a common center/radius
                }
            }

            // A closed loop made entirely of Arc/Ellipse curves sharing one
            // center/radius is a circle, whether drawn as 1 closed curve or split
            // across multiple arc segments.
            return curveCount > 0;
        }

        // ── Step 5: build DrainItem (DrainVertices deferred to Run time) ────────

        /// <summary>
        /// For a single opening loop: classifies its shape, then builds a DrainItem
        /// with an EMPTY DrainVertices list. Shape-edit vertex matching no longer
        /// happens here — it's deferred to Run time (AutoSlopeDrainEngine), and
        /// only performed for the DrainItems the user actually selects. The grid
        /// can still show Shape/Size immediately since those come from the loop's
        /// own geometry, independent of vertex matching.
        /// </summary>
        private DrainItem BuildDrainFromLoop(CurveArray originalLoop, List<XYZ> loopPts, Action<LogEntry> log)
        {
            try
            {
                if (loopPts.Count < 3)
                {
                    _dbgRejectedDegenerate++;
                    log?.Invoke(new LogEntry(LogLevel.Warning,
                        $"DRAIN-DEBUG: loop rejected — only {loopPts.Count} boundary point(s) found (degenerate loop)."));
                    return null;
                }

                var (shape, widthMm, heightMm) = Classify(originalLoop, loopPts);

                // Build the loop's curves (straight segments between consecutive
                // tessellated points) for DrainItem's LoopCurves/ContainsVertex use —
                // the Sketch profile's original curve types aren't preserved through
                // tessellation, so these are always Lines. This only affects the
                // downstream ContainsVertex 5mm-tolerance check and Dijkstra's
                // arc-length special-casing (which simply won't apply to these
                // edges, falling back to straight-chord distance — acceptable, since
                // the floor's actual boundary/opening ARCS still come from
                // FloorBoundaryHelper.GetBoundaryArcs reading live face geometry, not
                // from this tessellated loop).
                var curves = BuildStraightLoopCurves(loopPts);

                // DrainVertices intentionally empty here — populated later, at Run
                // time, only for selected DrainItems. See AutoSlopeDrainEngine.
                return new DrainItem(new List<SlabShapeVertex>(), curves, widthMm, heightMm, shape);
            }
            catch (Exception ex)
            {
                _dbgRejectedException++;
                log?.Invoke(new LogEntry(LogLevel.Error,
                    $"DRAIN-DEBUG: loop rejected — exception during processing: {ex.Message}"));
                return null;
            }
        }

        /// <summary>
        /// For each X,Y point in a loop, finds the real SlabShapeVertex at that
        /// X,Y (5mm tolerance) and takes its actual Z — this is what lets the slope
        /// engine call ModifySubElement on genuine shape-edit vertices instead of
        /// the flattened Z=0 sketch points. Points with no match are skipped
        /// individually rather than invalidating the whole loop.
        ///
        /// PUBLIC (V005, second round): no longer called during detection. Called
        /// by AutoSlopeDrainEngine at Run time, only for the DrainItems the user
        /// selected, against a freshly re-read SlabShapeVertex list — see that
        /// file for the call site.
        /// </summary>
        public List<SlabShapeVertex> FindShapeVerticesAtLoopPoints(List<XYZ> loopPts, List<SlabShapeVertex> allVertices, Action<LogEntry> log = null)
        {
            var matched = new List<SlabShapeVertex>();
            double toleranceFeet = VertexMatchToleranceMm / 304.8;
            int unmatchedCount = 0;

            if (allVertices == null || allVertices.Count == 0)
                return matched;

            foreach (var pt in loopPts)
            {
                SlabShapeVertex best = null;
                double bestDist = toleranceFeet;

                foreach (var vertex in allVertices)
                {
                    if (vertex?.Position == null) continue;

                    // X,Y-only distance — Z is exactly what we're solving for here,
                    // so it must not be part of the match test.
                    double dx = vertex.Position.X - pt.X;
                    double dy = vertex.Position.Y - pt.Y;
                    double xyDist = Math.Sqrt(dx * dx + dy * dy);

                    if (xyDist < bestDist)
                    {
                        bestDist = xyDist;
                        best = vertex;
                    }
                }

                if (best != null)
                    matched.Add(best);
                else
                    unmatchedCount++;
            }

            if (unmatchedCount > 0)
                log?.Invoke(new LogEntry(LogLevel.Warning,
                    $"{unmatchedCount} loop point(s) had no matching shape-edit vertex within {VertexMatchToleranceMm}mm (skipped individually)."));

            return matched;
        }

        /// <summary>
        /// NEW (V013). Every shape-edit point that lies on the drain opening's
        /// edge: within VertexMatchToleranceMm (plan distance) of any segment of
        /// the loop. V012 matched only the points nearest each tessellated loop
        /// point, so shape points along a circle's edge between two tessellation
        /// points were missed and ended up sloped as ordinary floor points.
        /// Superset of FindShapeVerticesAtLoopPoints: a point next to a loop
        /// point is also next to that segment's end.
        /// </summary>
        public List<SlabShapeVertex> FindShapeVerticesOnLoop(DrainItem drain, List<SlabShapeVertex> allVertices)
        {
            var matched = new List<SlabShapeVertex>();
            if (drain?.LoopCurves == null || drain.LoopCurves.Count == 0 || allVertices == null) return matched;

            double tol = VertexMatchToleranceMm / 304.8;
            var segments = new List<(double ax, double ay, double bx, double by)>(drain.LoopCurves.Count);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

            foreach (Curve c in drain.LoopCurves)
            {
                XYZ a = c.GetEndPoint(0), b = c.GetEndPoint(1);
                segments.Add((a.X, a.Y, b.X, b.Y));
                minX = Math.Min(minX, Math.Min(a.X, b.X)); maxX = Math.Max(maxX, Math.Max(a.X, b.X));
                minY = Math.Min(minY, Math.Min(a.Y, b.Y)); maxY = Math.Max(maxY, Math.Max(a.Y, b.Y));
            }

            foreach (SlabShapeVertex v in allVertices)
            {
                XYZ p = v?.Position;
                if (p == null) continue;
                if (p.X < minX - tol || p.X > maxX + tol || p.Y < minY - tol || p.Y > maxY + tol) continue;

                foreach (var s in segments)
                {
                    if (DistanceToSegment(p.X, p.Y, s.ax, s.ay, s.bx, s.by) <= tol)
                    {
                        matched.Add(v);
                        break;
                    }
                }
            }
            return matched;
        }

        // ── Floor V001: drain families ──────────────────────────────────────────

        /// <summary>Plan distance (mm) within which a shape point counts as a fixture drain's point.</summary>
        public const double FixtureMatchToleranceMm = 10.0;

        /// <summary>Largest height (mm) a fixture may sit above or below the floor's top and still count as on it.</summary>
        public const double FixtureHeightToleranceMm = 300.0;

        private static readonly string[] DrainNameHints = { "drain", "gully", "gulley", "outlet", "trap", "sump" };

        /// <summary>
        /// Finds Plumbing Fixture family instances on <paramref name="floor"/>: hosted on it,
        /// or standing over its top face (within <see cref="FixtureHeightToleranceMm"/>).
        /// Each becomes a "Fixture" <see cref="DrainItem"/>. Only fixtures whose family or
        /// type name looks like a drain (drain, gully, outlet, trap, sump) start ticked, so
        /// WCs and basins on the same floor are listed but not used unless chosen.
        /// Never throws: a fixture that cannot be read is logged and skipped.
        /// </summary>
        /// <param name="floor">The floor to search.</param>
        /// <param name="topFace">The floor's top face (used to test that a fixture stands on it).</param>
        /// <param name="log">Optional log sink.</param>
        public List<DrainItem> DetectFixtureDrains(Floor floor, Face topFace, Action<LogEntry> log = null)
        {
            var result = new List<DrainItem>();
            if (floor == null || topFace == null) return result;

            Document doc = floor.Document;
            double xyTolFt = FixtureMatchToleranceMm / 304.8;
            double zTolFt = FixtureHeightToleranceMm / 304.8;
            int checkedCount = 0, failed = 0;

            IEnumerable<FamilyInstance> fixtures;
            try
            {
                fixtures = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>()
                    .ToList();
            }
            catch (Exception ex)
            {
                log?.Invoke(new LogEntry(LogLevel.Warning, $"Drain families could not be read ({ex.Message}); only openings are listed."));
                return result;
            }

            foreach (FamilyInstance fi in fixtures)
            {
                try
                {
                    if (!(fi.Location is LocationPoint lp) || lp.Point == null) continue;
                    XYZ p = lp.Point;
                    checkedCount++;

                    bool hostedOnFloor = fi.Host != null && fi.Host.Id == floor.Id;
                    if (!hostedOnFloor)
                    {
                        IntersectionResult hit = topFace.Project(p);
                        if (hit?.XYZPoint == null) continue;
                        XYZ q = hit.XYZPoint;
                        double dxy = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
                        if (dxy > xyTolFt || Math.Abs(q.Z - p.Z) > zTolFt) continue;
                    }

                    BoundingBoxXYZ bb = fi.get_BoundingBox(null);
                    double widthMm = bb != null ? Math.Max(50.0, (bb.Max.X - bb.Min.X) * 304.8) : 100.0;
                    double heightMm = bb != null ? Math.Max(50.0, (bb.Max.Y - bb.Min.Y) * 304.8) : 100.0;

                    string familyName = fi.Symbol?.FamilyName ?? string.Empty;
                    string typeName = fi.Symbol?.Name ?? string.Empty;
                    string label = string.IsNullOrEmpty(typeName) ? familyName : $"{familyName} : {typeName}";
                    bool looksLikeDrain = DrainNameHints.Any(h =>
                        label.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0);

                    var drain = new DrainItem(new XYZ(p.X, p.Y, p.Z), Math.Round(widthMm), Math.Round(heightMm),
                        DrainItem.FixtureShapeType, fi.Id)
                    {
                        FixtureName = label,
                        IsSelected = looksLikeDrain
                    };
                    result.Add(drain);
                }
                catch (Exception ex)
                {
                    failed++;
                    log?.Invoke(new LogEntry(LogLevel.Warning, $"Plumbing fixture {fi?.Id.Value} could not be read and is not listed ({ex.Message})."));
                }
            }

            int ticked = result.Count(d => d.IsSelected);
            log?.Invoke(new LogEntry(LogLevel.Info,
                $"Drain families: {result.Count} plumbing fixture(s) on floor {floor.Name} (Id {floor.Id.Value}), " +
                $"{ticked} ticked because the name looks like a drain" +
                (failed > 0 ? $", {failed} unreadable." : ".")));
            return result.OrderBy(d => d.FixtureName).ToList();
        }

        /// <summary>
        /// Shape points of a fixture drain: every point within
        /// <see cref="FixtureMatchToleranceMm"/> (plan distance) of the fixture's location.
        /// The run adds a point there first (see FloorGeometryService.AddFixtureDrainPoints).
        /// </summary>
        /// <param name="drain">A drain with <see cref="DrainItem.IsFixture"/> true.</param>
        /// <param name="allVertices">The floor's current shape points.</param>
        public List<SlabShapeVertex> FindShapeVerticesAtFixture(DrainItem drain, List<SlabShapeVertex> allVertices)
        {
            var matched = new List<SlabShapeVertex>();
            if (drain?.CenterPoint == null || allVertices == null) return matched;

            double tol = FixtureMatchToleranceMm / 304.8;
            XYZ c = drain.CenterPoint;
            foreach (SlabShapeVertex v in allVertices)
            {
                XYZ p = v?.Position;
                if (p == null) continue;
                double dx = p.X - c.X, dy = p.Y - c.Y;
                if (dx * dx + dy * dy <= tol * tol)
                    matched.Add(v);
            }
            return matched;
        }

        private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            double len2 = dx * dx + dy * dy;
            double t = len2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / len2));
            double cx = ax + t * dx - px, cy = ay + t * dy - py;
            return Math.Sqrt(cx * cx + cy * cy);
        }

        /// <summary>Builds straight-line curves connecting consecutive tessellated loop points (closed loop).</summary>
        private List<Curve> BuildStraightLoopCurves(List<XYZ> loopPts)
        {
            var curves = new List<Curve>();
            for (int i = 0; i < loopPts.Count; i++)
            {
                XYZ a = loopPts[i];
                XYZ b = loopPts[(i + 1) % loopPts.Count];
                if (a.DistanceTo(b) < 0.001) continue; // skip degenerate zero-length segments
                curves.Add(Line.CreateBound(a, b));
            }
            return curves;
        }

        private static XYZ BoundingBoxCenter(List<XYZ> pts)
        {
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            return new XYZ((minX + maxX) / 2.0, (minY + maxY) / 2.0, 0);
        }

        // ---- UI helpers (unchanged — operate on DrainItem, independent of detection method) ----
        public List<string> GenerateSizeCategories(List<DrainItem> drains)
        {
            var categories = new List<string> { "All" };
            categories.AddRange(drains.Select(d => d.SizeCategory).Distinct().OrderBy(s => s));
            categories.Add("Less than 100x100");
            categories.Add("100x100 - 200x200");
            categories.Add("200x200 - 300x300");
            categories.Add("Greater than 300x300");
            return categories;
        }

        public List<DrainItem> FilterDrainsBySize(List<DrainItem> drains, string filter)
        {
            if (filter == "All") return drains;
            if (filter == "Less than 100x100") return drains.Where(d => d.Width < 100 && d.Height < 100).ToList();
            if (filter == "100x100 - 200x200") return drains.Where(d => d.Width >= 100 && d.Width <= 200 && d.Height >= 100 && d.Height <= 200).ToList();
            if (filter == "200x200 - 300x300") return drains.Where(d => d.Width > 200 && d.Width <= 300 && d.Height > 200 && d.Height <= 300).ToList();
            if (filter == "Greater than 300x300") return drains.Where(d => d.Width > 300 && d.Height > 300).ToList();
            return drains.Where(d => d.SizeCategory == filter).ToList();
        }

        public List<SlabShapeVertex> FindVerticesForDrain(DrainItem drain, List<SlabShapeVertex> allVertices, Face topFace)
        {
            return drain.DrainVertices ?? new List<SlabShapeVertex>();
        }
    }
}
