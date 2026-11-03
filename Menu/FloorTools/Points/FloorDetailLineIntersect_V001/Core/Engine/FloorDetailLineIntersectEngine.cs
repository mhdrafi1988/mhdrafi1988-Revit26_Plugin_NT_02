// =======================================================
// File: FloorDetailLineIntersectEngine.cs
// Location: Core/Engine/
// Extracted from FloorDetailLineIntersectViewModel (V008), which held all
// of this geometry/placement logic directly — a real violation of this
// suite's vertical-slice convention (Core/Engine logic living inside
// UI/ViewModels). Logic is unchanged from V008; only where it lives (and
// how counters/log entries are threaded — local values returned in
// FloorDetailLineIntersectResult instead of direct ViewModel property
// increments) has changed.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.Core.Engine
{
    public static class FloorDetailLineIntersectEngine
    {
        /// <summary>2 mm dedup tolerance in Revit internal feet.</summary>
        private const double DedupToleranceFt   = 2.0 / 1000.0 / 0.3048;
        /// <summary>Zero-length tolerance for segment culling.</summary>
        private const double ZeroLengthTolerance = 1e-6;
        /// <summary>Ray-casting distance (must be >> model extents).</summary>
        private const double RayCastDistance     = 100_000.0;
        /// <summary>
        /// Increasing inward-nudge offsets (mm) tried when Revit rejects a point
        /// (commonly because it sits exactly on the outer boundary edge).
        /// </summary>
        private static readonly double[] NudgeOffsetsMm = { 1, 3, 6, 12, 25, 50 };

        public static FloorDetailLineIntersectResult Execute(
            Document doc, Floor floor, List<DetailLine> detailLines, Action<LogEntry> log,
            bool includeLineToLineIntersections = true)
        {
            int errorCount = 0;
            var userLog = log ?? (_ => { });
            log = entry =>
            {
                if (entry.Level == LogLevel.Error) errorCount++;
                userLog(entry);
            };
            int intersectionsFound = 0, pointsPlaced = 0, skippedCount = 0, lineToLineFound = 0;

            // 1. Extract boundary — separate outer and inner loop segments
            var outerSegments = new List<Line>();
            var innerSegments = new List<Line>();
            ExtractBoundarySegments2D(floor, outerSegments, innerSegments,
                out int outerCount, out int innerLoops, out int innerEdgeCount);

            log(new LogEntry(LogLevel.Info, $"Floor id {floor.Id.Value}: {outerCount} outer edges, " +
                                  $"{innerLoops} inner loops ({innerEdgeCount} edges)."));

            if (outerSegments.Count == 0)
            {
                const string msg = "No outer boundary segments found. Aborting.";
                log(new LogEntry(LogLevel.Error, msg));
                return new FloorDetailLineIntersectResult { Success = false, ErrorMessage = msg };
            }

            // All boundary segments combined (for intersection scanning)
            var allSegments = outerSegments.Concat(innerSegments).ToList();

            // Approximate interior direction reference for inward-nudge retries
            XYZ centroid = ComputeCentroid(outerSegments);

            // 2. Base Z — computed before opening the transaction (read-only)
            double baseZ = GetBaseZ(doc, floor);
            log(new LogEntry(LogLevel.Info, $"Base Z = {baseZ * 0.3048:F3} m"));

            // 3. Open one transaction for all point placements
            using var tx = new Transaction(doc, "FloorDetailLineIntersect — Place Shape Points");
            try
            {
                tx.Start();

                SlabShapeEditor sse = floor.GetSlabShapeEditor();
                if (!sse.IsEnabled)
                    sse.Enable();

                var placedXYs = new List<XYZ>(); // global dedup list
                int lineIndex = 0;

                // Tessellate every detail line once — reused for both the
                // boundary/endpoint pass and the line×line pass below.
                var tessellated = new List<(int Index, DetailLine Dl, List<Line> Segments)>();
                foreach (var dl in detailLines)
                {
                    lineIndex++;
                    var segs = TessellateDetailLine2D(dl, log);
                    tessellated.Add((lineIndex, dl, segs));
                }

                foreach (var t in tessellated)
                {
                    try
                    {
                        ProcessDetailLine(t.Index, t.Dl, t.Segments, outerSegments, innerSegments,
                                          allSegments, baseZ, sse, placedXYs, centroid, doc, log,
                                          ref intersectionsFound, ref pointsPlaced, ref skippedCount);
                    }
                    catch (Exception ex)
                    {
                        log(new LogEntry(LogLevel.Error,
                            $"Line {t.Index} (id {t.Dl.Id.Value}) — exception: {ex.Message}"));
                    }
                }

                if (includeLineToLineIntersections)
                {
                    try
                    {
                        ProcessLineToLineIntersections(tessellated, baseZ, sse, placedXYs, centroid,
                            doc, log, ref lineToLineFound, ref pointsPlaced, ref skippedCount);
                    }
                    catch (Exception ex)
                    {
                        log(new LogEntry(LogLevel.Error,
                            $"Line×Line intersection pass — exception: {ex.Message}"));
                    }
                }

                tx.Commit();

                log(new LogEntry(LogLevel.Info,
                    $"Done — {pointsPlaced} placed, {skippedCount} skipped, {errorCount} errors."));
            }
            catch (Exception ex)
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();

                string msg = $"Transaction aborted: {ex.Message}";
                log(new LogEntry(LogLevel.Error, msg));
                return new FloorDetailLineIntersectResult
                {
                    Success = false,
                    ErrorMessage = msg,
                    IntersectionsFound = intersectionsFound,
                    LineToLineIntersectionsFound = lineToLineFound,
                    PointsPlaced = pointsPlaced,
                    SkippedCount = skippedCount
                };
            }

            return new FloorDetailLineIntersectResult
            {
                Success = true,
                IntersectionsFound = intersectionsFound,
                LineToLineIntersectionsFound = lineToLineFound,
                PointsPlaced = pointsPlaced,
                SkippedCount = skippedCount
            };
        }

        // ── Process single DetailLine ────────────────────────────────────────
        private static void ProcessDetailLine(
            int              lineIndex,
            DetailLine       dl,
            List<Line>       dlSegments,
            List<Line>       outerSegments,
            List<Line>       innerSegments,
            List<Line>       allSegments,
            double           baseZ,
            SlabShapeEditor  sse,
            List<XYZ>        placedXYs,
            XYZ              centroid,
            Document         doc,
            Action<LogEntry> log,
            ref int intersectionsFound,
            ref int pointsPlaced,
            ref int skippedCount)
        {
            if (dlSegments.Count == 0)
            {
                log(new LogEntry(LogLevel.Warning,
                    $"Line {lineIndex} (id {dl.Id.Value}) — no geometry or zero length."));
                skippedCount++;
                return;
            }

            var candidateHits = new List<XYZ>();

            foreach (var dlSeg in dlSegments)
            {
                // A) Bounded intersection: where this segment crosses any boundary edge
                var hits = FindIntersectionsBounded(dlSeg, allSegments);
                candidateHits.AddRange(hits);

                // B) Endpoints that lie strictly inside the floor (outer - inner holes)
                var p0 = Flatten(dlSeg.GetEndPoint(0));
                var p1 = Flatten(dlSeg.GetEndPoint(1));

                if (IsPointInsideFloor(p0, outerSegments, innerSegments))
                    candidateHits.Add(p0);
                if (IsPointInsideFloor(p1, outerSegments, innerSegments))
                    candidateHits.Add(p1);
            }

            var uniqueHits = DeduplicatePoints(candidateHits);

            if (uniqueHits.Count == 0)
            {
                log(new LogEntry(LogLevel.Warning,
                    $"Line {lineIndex} (id {dl.Id.Value}) — no intersections or interior points."));
                skippedCount++;
                return;
            }

            foreach (var hit in uniqueHits)
            {
                intersectionsFound++;

                if (IsDuplicate(hit, placedXYs))
                {
                    log(new LogEntry(LogLevel.Warning,
                        $"Line {lineIndex} → ({ToM(hit.X):F3}, {ToM(hit.Y):F3}) m — global dedup, skipped."));
                    skippedCount++;
                    continue;
                }

                var pt3D = new XYZ(hit.X, hit.Y, baseZ);

                if (TryAddPointWithNudge(sse, doc, pt3D, baseZ, centroid,
                        out double nudgeMm, out XYZ actualXY, out string failReason))
                {
                    placedXYs.Add(actualXY); // dedup tracks the REAL placed position, not the pre-nudge hit
                    pointsPlaced++;

                    string suffix = nudgeMm > 0 ? $" (nudged {nudgeMm:F0} mm inward)" : "";
                    log(new LogEntry(LogLevel.Success,
                        $"Line {lineIndex} → ({ToM(hit.X):F3}, {ToM(hit.Y):F3}) m — placed{suffix}."));
                }
                else
                {
                    skippedCount++;
                    log(new LogEntry(LogLevel.Warning,
                        $"Line {lineIndex} → ({ToM(hit.X):F3}, {ToM(hit.Y):F3}) m — skipped: {failReason}"));
                }
            }
        }

        // ── Process Line×Line intersections (new in V012) ────────────────────
        /// <summary>
        /// Places shape points wherever two DIFFERENT selected detail lines
        /// cross each other, independent of the floor boundary. Runs after the
        /// per-line boundary/endpoint pass, sharing the same global dedup list
        /// so a line×line hit that coincides with an already-placed boundary
        /// point is skipped rather than double-placed.
        /// </summary>
        private static void ProcessLineToLineIntersections(
            List<(int Index, DetailLine Dl, List<Line> Segments)> tessellated,
            double           baseZ,
            SlabShapeEditor  sse,
            List<XYZ>        placedXYs,
            XYZ              centroid,
            Document         doc,
            Action<LogEntry> log,
            ref int lineToLineFound,
            ref int pointsPlaced,
            ref int skippedCount)
        {
            var candidateHits = new List<(XYZ Pt, int A, int B)>();

            for (int i = 0; i < tessellated.Count; i++)
            {
                for (int j = i + 1; j < tessellated.Count; j++)
                {
                    foreach (var segA in tessellated[i].Segments)
                        foreach (var segB in tessellated[j].Segments)
                            if (TryIntersectSegments(segA, segB, out XYZ hit))
                                candidateHits.Add((hit, tessellated[i].Index, tessellated[j].Index));
                }
            }

            if (candidateHits.Count == 0) return;

            var uniqueHits = new List<(XYZ Pt, int A, int B)>();
            foreach (var c in candidateHits)
                if (!IsDuplicate(c.Pt, uniqueHits.Select(u => u.Pt).ToList()))
                    uniqueHits.Add(c);

            foreach (var hit in uniqueHits)
            {
                lineToLineFound++;

                if (IsDuplicate(hit.Pt, placedXYs))
                {
                    log(new LogEntry(LogLevel.Warning,
                        $"Line×Line {hit.A}∩{hit.B} → ({ToM(hit.Pt.X):F3}, {ToM(hit.Pt.Y):F3}) m — global dedup, skipped."));
                    skippedCount++;
                    continue;
                }

                var pt3D = new XYZ(hit.Pt.X, hit.Pt.Y, baseZ);

                if (TryAddPointWithNudge(sse, doc, pt3D, baseZ, centroid,
                        out double nudgeMm, out XYZ actualXY, out string failReason))
                {
                    placedXYs.Add(actualXY);
                    pointsPlaced++;

                    string suffix = nudgeMm > 0 ? $" (nudged {nudgeMm:F0} mm inward)" : "";
                    log(new LogEntry(LogLevel.Success,
                        $"Line×Line {hit.A}∩{hit.B} → ({ToM(hit.Pt.X):F3}, {ToM(hit.Pt.Y):F3}) m — placed{suffix}."));
                }
                else
                {
                    skippedCount++;
                    log(new LogEntry(LogLevel.Warning,
                        $"Line×Line {hit.A}∩{hit.B} → ({ToM(hit.Pt.X):F3}, {ToM(hit.Pt.Y):F3}) m — skipped: {failReason}"));
                }
            }
        }

        /// <summary>Bounded segment-vs-segment intersection (both t and u clamped to [0,1]).</summary>
        private static bool TryIntersectSegments(Line a, Line b, out XYZ hit)
        {
            hit = XYZ.Zero;

            XYZ p = a.GetEndPoint(0);
            XYZ r = a.GetEndPoint(1) - p;
            XYZ q = b.GetEndPoint(0);
            XYZ s = b.GetEndPoint(1) - q;

            double rxs  = Cross2D(r, s);
            if (Math.Abs(rxs) < 1e-10) return false; // parallel / collinear

            XYZ   qmp   = q - p;
            double qpxr = Cross2D(qmp, r);
            double qpxs = Cross2D(qmp, s);

            double t = qpxs / rxs;
            double u = qpxr / rxs;

            const double eps = 1e-9;
            if (t < -eps || t > 1.0 + eps || u < -eps || u > 1.0 + eps) return false;

            XYZ pt = p + t * r;
            hit = new XYZ(pt.X, pt.Y, 0);
            return true;
        }

        // ── Tessellate DetailLine → 2D line segments ─────────────────────────
        private static List<Line> TessellateDetailLine2D(DetailLine dl, Action<LogEntry> log)
        {
            var result = new List<Line>();
            Curve c = dl.GeometryCurve;
            if (c == null) return result;

            try
            {
                if (c is Line)
                {
                    var s = Flatten(c.GetEndPoint(0));
                    var e = Flatten(c.GetEndPoint(1));
                    if (s.DistanceTo(e) > ZeroLengthTolerance)
                        result.Add(Line.CreateBound(s, e));
                    return result;
                }

                // Arc, circle, spline, ellipse, etc.
                var pts = c.Tessellate();
                if (pts == null || pts.Count < 2)
                {
                    log(new LogEntry(LogLevel.Warning,
                        $"DetailLine id {dl.Id.Value} — tessellation: insufficient points."));
                    return result;
                }

                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var p0 = Flatten(pts[i]);
                    var p1 = Flatten(pts[i + 1]);
                    if (p0.DistanceTo(p1) > ZeroLengthTolerance)
                        result.Add(Line.CreateBound(p0, p1));
                }

                if (result.Count > 0)
                    log(new LogEntry(LogLevel.Info,
                        $"DetailLine id {dl.Id.Value} — arc/curve → {result.Count} segments."));
            }
            catch (Exception ex)
            {
                log(new LogEntry(LogLevel.Warning,
                    $"DetailLine id {dl.Id.Value} — tessellation failed: {ex.Message}"));
            }

            return result;
        }

        // ── Bounded intersection: query segment vs boundary segments ─────────
        /// <summary>
        /// Returns points where the query segment [p,p+r] crosses any boundary
        /// segment. Both t (query) and u (boundary) are clamped to [0,1].
        /// This prevents phantom hits from extended line math.
        /// </summary>
        private static List<XYZ> FindIntersectionsBounded(Line querySegment, List<Line> boundarySegs)
        {
            var results = new List<XYZ>();

            XYZ p = querySegment.GetEndPoint(0);
            XYZ r = querySegment.GetEndPoint(1) - p;

            foreach (var seg in boundarySegs)
            {
                XYZ q = seg.GetEndPoint(0);
                XYZ s = seg.GetEndPoint(1) - q;

                double rxs  = Cross2D(r, s);
                XYZ   qmp   = q - p;
                double qpxr = Cross2D(qmp, r);
                double qpxs = Cross2D(qmp, s);

                if (Math.Abs(rxs) < 1e-10) continue; // parallel / collinear

                double t = qpxs / rxs; // parameter along query segment
                double u = qpxr / rxs; // parameter along boundary segment

                // Both must be within their segments (with small epsilon tolerance)
                const double eps = 1e-6;
                if (t >= -eps && t <= 1.0 + eps &&
                    u >= -eps && u <= 1.0 + eps)
                {
                    XYZ hit = p + t * r;
                    results.Add(new XYZ(hit.X, hit.Y, 0));
                }
            }

            return results;
        }

        // ── Point-in-floor test (outer polygon minus inner holes) ─────────────
        /// <summary>
        /// A point is inside the floor if:
        ///   - Ray crosses outer boundary an ODD number of times, AND
        ///   - Ray crosses every inner loop an EVEN number of times
        ///     (even = outside that hole → still on floor surface).
        /// </summary>
        private static bool IsPointInsideFloor(
            XYZ        point,
            List<Line> outerSegments,
            List<Line> innerSegments)
        {
            // Must be inside outer polygon
            if (!IsPointInsidePolygon(point, outerSegments)) return false;

            // Must NOT be inside any inner hole
            // We treat all inner segments together: even crossings = outside all holes
            // (works when inner loops don't overlap each other)
            if (innerSegments.Count > 0 &&
                IsPointInsidePolygon(point, innerSegments))
                return false;

            return true;
        }

        private static bool IsPointInsidePolygon(XYZ point, List<Line> segments)
        {
            int  crossings = 0;
            XYZ  rayEnd    = new XYZ(point.X + RayCastDistance, point.Y, 0);

            foreach (var seg in segments)
            {
                if (DoSegmentsIntersect2D(point, rayEnd,
                        seg.GetEndPoint(0), seg.GetEndPoint(1), out _))
                    crossings++;
            }

            return (crossings % 2) == 1;
        }

        private static bool DoSegmentsIntersect2D(
            XYZ p1, XYZ p2, XYZ p3, XYZ p4, out XYZ intersection)
        {
            intersection = XYZ.Zero;
            double x1 = p1.X, y1 = p1.Y, x2 = p2.X, y2 = p2.Y;
            double x3 = p3.X, y3 = p3.Y, x4 = p4.X, y4 = p4.Y;

            double denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(denom) < 1e-10) return false;

            double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;
            double u = ((x1 - x3) * (y1 - y2) - (y1 - y3) * (x1 - x2)) / denom;

            if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
            {
                intersection = new XYZ(x1 + t * (x2 - x1), y1 + t * (y2 - y1), 0);
                return true;
            }

            return false;
        }

        // ── Boundary extraction ───────────────────────────────────────────────
        /// <summary>
        /// Populates separate outer and inner segment lists from the floor's sketch loops
        /// (Shared.Services.FloorSketchProfile; a Floor has no GetProfiles()). The sketch does
        /// not promise the outer loop comes first, so loops are sorted by plan area, largest
        /// first: the largest is the outer boundary, the rest are inner holes.
        /// </summary>
        private static void ExtractBoundarySegments2D(
            Floor floor,
            List<Line> outerSegments,
            List<Line> innerSegments,
            out int    outerEdgeCount,
            out int    innerLoopCount,
            out int    innerEdgeCount)
        {
            outerEdgeCount = 0;
            innerLoopCount = 0;
            innerEdgeCount = 0;

            List<List<Curve>> profiles = FloorSketchProfile.GetLoops(floor)
                .OrderByDescending(PlanArea)
                .ToList();
            bool isFirst = true;

            foreach (List<Curve> loop in profiles)
            {
                var targetList = isFirst ? outerSegments : innerSegments;
                int edgesAdded = 0;

                foreach (Curve c in loop)
                {
                    if (c == null) continue;

                    if (c is Line)
                    {
                        var s = Flatten(c.GetEndPoint(0));
                        var e = Flatten(c.GetEndPoint(1));
                        if (TryAddSegment(targetList, s, e)) edgesAdded++;
                    }
                    else
                    {
                        // Bound or unbound non-linear (arc, ellipse, spline)
                        IList<XYZ> pts;
                        try   { pts = c.Tessellate(); }
                        catch { continue; }

                        if (pts == null || pts.Count < 2) continue;

                        for (int i = 0; i < pts.Count - 1; i++)
                        {
                            if (TryAddSegment(targetList,
                                    Flatten(pts[i]), Flatten(pts[i + 1])))
                                edgesAdded++;
                        }
                    }
                }

                if (isFirst) { outerEdgeCount = edgesAdded; isFirst = false; }
                else         { innerLoopCount++; innerEdgeCount += edgesAdded; }
            }
        }

        /// <summary>Absolute plan (XY) area of a closed loop of curves, from their tessellation.</summary>
        private static double PlanArea(List<Curve> loop)
        {
            var pts = new List<XYZ>();
            foreach (Curve c in loop)
            {
                IList<XYZ> t;
                try { t = c.Tessellate(); } catch { continue; }
                if (t != null) pts.AddRange(t);
            }
            double a = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                XYZ p = pts[i], q = pts[(i + 1) % pts.Count];
                a += p.X * q.Y - q.X * p.Y;
            }
            return Math.Abs(a) / 2;
        }

        private static bool TryAddSegment(List<Line> list, XYZ s, XYZ e)
        {
            if (s.DistanceTo(e) <= ZeroLengthTolerance) return false;
            list.Add(Line.CreateBound(s, e));
            return true;
        }

        // ── Base Z ────────────────────────────────────────────────────────────
        /// <summary>
        /// Returns the lowest existing SlabShapeVertex Z, or the floor level
        /// elevation if no vertices exist yet. Called before tx.Start() — read only.
        /// </summary>
        private static double GetBaseZ(Document doc, Floor floor)
        {
            SlabShapeEditor sse      = floor.GetSlabShapeEditor();
            var             vertices = sse.SlabShapeVertices;

            if (vertices == null || vertices.Size == 0)
            {
                Level lvl = doc.GetElement(floor.LevelId) as Level;
                return lvl?.Elevation ?? 0.0;
            }

            double minZ = double.MaxValue;
            foreach (SlabShapeVertex v in vertices)
                if (v.Position.Z < minZ) minZ = v.Position.Z;

            return minZ;
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static XYZ     Flatten(XYZ p)         => new XYZ(p.X, p.Y, 0);
        private static double  Cross2D(XYZ a, XYZ b)  => a.X * b.Y - a.Y * b.X;
        private static double  ToM(double feet)        => feet * 0.3048;

        /// <summary>
        /// Rough interior reference point (average of outer boundary vertices).
        /// Used only to pick an inward nudge direction — doesn't need to be the
        /// exact polygon centroid.
        /// </summary>
        private static XYZ ComputeCentroid(List<Line> outerSegments)
        {
            double sx = 0, sy = 0;
            int    n  = 0;
            foreach (var seg in outerSegments)
            {
                var p = seg.GetEndPoint(0);
                sx += p.X; sy += p.Y; n++;
            }
            return n > 0 ? new XYZ(sx / n, sy / n, 0) : XYZ.Zero;
        }

        /// <summary>
        /// Attempts sse.AddPoint at the exact location; if Revit rejects it
        /// (typically a point sitting exactly on the outer boundary edge),
        /// retries at increasing offsets nudged toward the floor interior.
        /// Each attempt is isolated — a failure here never aborts sibling points.
        /// </summary>
        private static bool TryAddPointWithNudge(
            SlabShapeEditor sse, Document doc, XYZ pt, double baseZ, XYZ centroid,
            out double appliedNudgeMm, out XYZ actualXY, out string failureReason)
        {
            appliedNudgeMm = 0;
            actualXY        = new XYZ(pt.X, pt.Y, 0);

            if (TryAdd(sse, doc, pt.X, pt.Y, baseZ, out failureReason))
                return true;

            XYZ dir = new XYZ(centroid.X - pt.X, centroid.Y - pt.Y, 0);
            double len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
            if (len < 1e-9)
                return false; // point is at the centroid itself — nudging can't help

            double dx = dir.X / len, dy = dir.Y / len;

            foreach (double mm in NudgeOffsetsMm)
            {
                double offsetFt = mm / 304.8;
                double nx = pt.X + dx * offsetFt;
                double ny = pt.Y + dy * offsetFt;

                if (TryAdd(sse, doc, nx, ny, baseZ, out failureReason))
                {
                    appliedNudgeMm = mm;
                    actualXY        = new XYZ(nx, ny, 0);
                    return true;
                }
            }

            return false;
        }

        private static bool TryAdd(SlabShapeEditor sse, Document doc, double x, double y, double z, out string failureReason)
        {
            failureReason = string.Empty;
            SlabShapeVertex vertex;

            try
            {
                vertex = sse.AddPoint(new XYZ(x, y, z));
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return false;
            }

            // Regenerate immediately so an invalid/degenerate shape edit is caught
            // and isolated to THIS point right now — instead of surfacing later at
            // tx.Commit(), which would force a full rollback of every point already
            // placed by earlier lines in the same run.
            try
            {
                doc.Regenerate();
                return true;
            }
            catch (Exception ex)
            {
                failureReason = $"rejected on regenerate: {ex.Message}";
                // FIX: SlabShapeEditor has no RemovePoint() in this project's Revit API
                // version (this call was already present, unbuilt, in the original V008
                // source) — DeletePoint() is the correct method, confirmed by
                // VertexReducer's usage of the same API in this same batch.
                try { sse.DeletePoint(vertex); }
                catch { /* best-effort cleanup only — point may already be gone */ }
                return false;
            }
        }

        private static List<XYZ> DeduplicatePoints(List<XYZ> points)
        {
            var unique = new List<XYZ>();
            foreach (var pt in points)
                if (!IsDuplicate(pt, unique)) unique.Add(pt);
            return unique;
        }

        private static bool IsDuplicate(XYZ candidate, List<XYZ> existing)
            => existing.Any(p =>
                   Math.Sqrt(Math.Pow(candidate.X - p.X, 2) +
                             Math.Pow(candidate.Y - p.Y, 2)) < DedupToleranceFt);
    }
}
