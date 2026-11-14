using Clipper2Lib;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry
{
    /// <summary>
    /// Turns a room's boundary pieces into closed outlines. Repairs are tried in a fixed order and
    /// every attempt is recorded, so the grid, the log and the Excel report can say what was done.
    /// </summary>
    public static class LoopRepair
    {
        /// <summary>Ends closer than this are the same point, mm.</summary>
        public const double JoinTolMm = 1.0;

        /// <summary>Pieces shorter than this are dropped (Revit rejects curves under about 0.8 mm), mm.</summary>
        public const double MinSegMm = 0.8;

        private const double ClipperScale = 1000; // mm → 1/1000 mm integers

        /// <summary>
        /// Builds one closed outline from <paramref name="segs"/>. Order: as given → clean (2) →
        /// merge collinear (3) → snap small gaps (4) → bridge larger gaps (5); an outline that closes
        /// but crosses itself goes through overlap removal (6).
        /// </summary>
        public static LoopResult Repair(IReadOnlyList<Seg> segs, WetAreaSettings s)
        {
            s ??= new WetAreaSettings();
            double snap = s.SnapMm > 0 ? s.SnapMm : WetAreaSettings.DefaultSnapMm;
            double bridge = Math.Max(s.BridgeMm, snap);
            var steps = new List<RepairStep>();
            var work = (segs ?? Array.Empty<Seg>()).ToList();

            if (work.Count < 3)
            {
                steps.Add(new RepairStep(2, "Clean", false, "fewer than 3 boundary pieces", null));
                return Fail(steps);
            }

            Chained chain = Chain(work, JoinTolMm, JoinTolMm);
            if (chain.Closed) return Finish(chain, steps);

            var cleaned = Clean(work, out int removed);
            chain = Chain(cleaned, JoinTolMm, JoinTolMm);
            steps.Add(new RepairStep(2, "Clean", chain.Closed,
                removed > 0 ? $"removed {removed} short or duplicate pieces" : "nothing to remove", null));
            if (chain.Closed) return Finish(chain, steps);

            var merged = MergeCollinear(cleaned);
            chain = Chain(merged, JoinTolMm, JoinTolMm);
            steps.Add(new RepairStep(3, "Merge collinear", chain.Closed,
                $"merged {cleaned.Count - merged.Count} collinear pieces", null));
            if (chain.Closed) return Finish(chain, steps);

            chain = Chain(merged, snap, snap);
            steps.Add(new RepairStep(4, "Snap", chain.Closed, chain.Closed
                ? $"gap {Mm(chain.Gap)} mm snapped"
                : $"gap {Mm(chain.Gap)} mm is larger than {Mm(snap)} mm", chain.GapAt));
            if (chain.Closed) return Finish(chain, steps);

            chain = Chain(merged, snap, bridge);
            steps.Add(new RepairStep(5, "Bridge", chain.Closed, chain.Closed
                ? $"gap {Mm(chain.Gap)} mm bridged"
                : $"gap {Mm(chain.Gap)} mm is larger than {Mm(bridge)} mm", chain.GapAt));
            return chain.Closed ? Finish(chain, steps) : Fail(steps);
        }

        /// <summary>
        /// Builds the outline for one room from its boundary loops. The largest loop is the outer
        /// one. A failed outer loop falls back to the room's 3D bottom face (8), then, when allowed,
        /// to a convex hull (10). A hole that cannot be closed is left out (9). The result is never
        /// rejected for its area: a difference above the tolerance is reported as a warning.
        /// </summary>
        /// <param name="loops">Boundary loops, mm, host coordinates.</param>
        /// <param name="roomAreaM2">The room's own area; 0 when unknown.</param>
        /// <param name="s">Tolerances.</param>
        /// <param name="bottomFace3D">Reads the outer outline from the room's solid; may be null, may throw.</param>
        public static RoomOutline Evaluate(
            IReadOnlyList<IReadOnlyList<Seg>> loops, double roomAreaM2, WetAreaSettings s, Func<IReadOnlyList<Pt>> bottomFace3D)
        {
            s ??= new WetAreaSettings();
            var steps = new List<RepairStep>();
            var noLoops = new List<IReadOnlyList<Pt>>();
            if (loops == null || loops.Count == 0 || loops.All(l => l == null || l.Count == 0))
                return new RoomOutline(new List<Pt>(), noLoops, 0, roomAreaM2, 0, RoomStatus.NotEnclosed, steps);

            var results = loops.Select(l => Repair(l, s)).ToList();
            int outerIndex = 0;
            double best = -1;
            for (int i = 0; i < loops.Count; i++)
            {
                double box = BoxArea(loops[i]);
                if (box > best) { best = box; outerIndex = i; }
            }

            steps.AddRange(results[outerIndex].Steps);
            IReadOnlyList<Pt> outer = results[outerIndex].Ok ? results[outerIndex].Points : null;
            bool approximate = false;

            if (outer == null && bottomFace3D != null)
            {
                try
                {
                    var face = Normalize(bottomFace3D());
                    bool usable = IsValid(face);
                    steps.Add(new RepairStep(8, "Room 3D shape", usable,
                        usable ? "outline read from the room's bottom face" : "the room's bottom face gave no usable outline", null));
                    if (usable) outer = face;
                }
                catch (Exception ex)
                {
                    steps.Add(new RepairStep(8, "Room 3D shape", false, ex.Message, null));
                }
            }

            if (outer == null && s.AllowApproximate)
            {
                var hull = Hull.Convex(loops[outerIndex].SelectMany(g => new[] { g.A, g.B }));
                bool usable = IsValid(hull);
                steps.Add(new RepairStep(10, "Approximate", usable,
                    usable ? "convex hull of the boundary used; not the real outline" : "no hull could be built", null));
                if (usable) { outer = hull; approximate = true; }
            }

            if (outer == null)
                return new RoomOutline(new List<Pt>(), noLoops, 0, roomAreaM2, 0, RoomStatus.Failed, steps);

            var inner = new List<IReadOnlyList<Pt>>();
            for (int i = 0; i < loops.Count; i++)
            {
                if (i == outerIndex || loops[i] == null || loops[i].Count == 0) continue;
                LoopResult hole = results[i];
                steps.AddRange(hole.Steps);
                if (hole.Ok && !approximate && Polyline2D.Contains(outer, Polyline2D.Centroid(hole.Points)))
                {
                    inner.Add(hole.Points.Reverse().ToList()); // holes run clockwise
                    continue;
                }
                steps.Add(new RepairStep(9, "Skip hole", true,
                    hole.Ok ? "hole left out of the outline" : "hole could not be closed and was left out; the roof covers it",
                    loops[i][0].A));
            }

            double area = Polyline2D.AreaM2(outer) - inner.Sum(Polyline2D.AreaM2);
            double diff = roomAreaM2 > 1e-9 ? (area - roomAreaM2) / roomAreaM2 * 100 : 0;
            RoomStatus status = approximate ? RoomStatus.Approximate
                : Math.Abs(diff) > s.AreaTolerancePct ? RoomStatus.AreaWarning
                : steps.Count > 0 ? RoomStatus.Repaired
                : RoomStatus.Ready;
            return new RoomOutline(outer, inner, area, roomAreaM2, diff, status, steps);
        }

        /// <summary>
        /// Tidies a closed outline: drops repeated points, points on a straight run and zero-width
        /// spikes, and makes it counter-clockwise. Returns an empty list for null input.
        /// </summary>
        public static IReadOnlyList<Pt> Normalize(IReadOnlyList<Pt> points)
        {
            var pts = (points ?? Array.Empty<Pt>()).ToList();
            bool changed = true;
            while (changed && pts.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < pts.Count && pts.Count >= 3; i++)
                {
                    Pt a = pts[(i - 1 + pts.Count) % pts.Count], b = pts[i], c = pts[(i + 1) % pts.Count];
                    if (b.DistanceTo(c) < 0.01 || a.DistanceTo(c) < 0.01 || DistanceToLine(a, c, b) < 0.05)
                    {
                        pts.RemoveAt(i);
                        i--;
                        changed = true;
                    }
                }
            }
            if (pts.Count < 3) return new List<Pt>();
            if (Polyline2D.SignedAreaMm2(pts) < 0) pts.Reverse();
            return pts;
        }

        private static bool IsValid(IReadOnlyList<Pt> pts)
            => pts != null && pts.Count >= 3 && !Polyline2D.SelfIntersects(pts) && Polyline2D.AreaM2(pts) > 1e-6;

        private static LoopResult Fail(List<RepairStep> steps) => new(new List<Pt>(), false, steps, false);

        /// <summary>Closed chain → tidy outline; one that crosses itself goes through overlap removal (6).</summary>
        private static LoopResult Finish(Chained chain, List<RepairStep> steps)
        {
            var pts = Normalize(chain.Points);
            if (IsValid(pts)) return new LoopResult(pts, true, steps, false);

            var fixedPts = RemoveOverlaps(chain.Points);
            bool ok = IsValid(fixedPts);
            steps.Add(new RepairStep(6, "Remove overlaps", ok,
                ok ? "outline crossed itself; the largest clean part is kept" : "outline crosses itself and could not be untangled", null));
            return ok ? new LoopResult(fixedPts, true, steps, false) : Fail(steps);
        }

        private static string Mm(double value) => Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);

        private static double DistanceToLine(Pt a, Pt b, Pt p)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            return len < 1e-9 ? a.DistanceTo(p) : Math.Abs(dx * (p.Y - a.Y) - dy * (p.X - a.X)) / len;
        }

        private static double BoxArea(IReadOnlyList<Seg> segs)
        {
            if (segs == null || segs.Count == 0) return -1;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (Seg g in segs)
                foreach (Pt p in new[] { g.A, g.B })
                {
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                }
            return (maxX - minX) * (maxY - minY);
        }

        // ── Method 2 ────────────────────────────────────────────────────────────────────────────

        private static List<Seg> Clean(List<Seg> segs, out int removed)
        {
            var kept = new List<Seg>();
            foreach (Seg g in segs)
            {
                if (g.Length < MinSegMm) continue;
                bool duplicate = kept.Any(k =>
                    (k.A.DistanceTo(g.A) < JoinTolMm && k.B.DistanceTo(g.B) < JoinTolMm) ||
                    (k.A.DistanceTo(g.B) < JoinTolMm && k.B.DistanceTo(g.A) < JoinTolMm));
                if (!duplicate) kept.Add(g);
            }
            removed = segs.Count - kept.Count;
            return kept;
        }

        // ── Method 3 ────────────────────────────────────────────────────────────────────────────

        private sealed class LineGroup
        {
            public double Dx, Dy, Offset;
            public readonly List<(double From, double To, Seg Source)> Runs = new();
        }

        /// <summary>Joins pieces that lie on one straight line and overlap or touch.</summary>
        private static List<Seg> MergeCollinear(List<Seg> segs)
        {
            var groups = new List<LineGroup>();
            foreach (Seg g in segs)
            {
                double len = g.Length;
                if (len < 1e-9) continue;
                double dx = (g.B.X - g.A.X) / len, dy = (g.B.Y - g.A.Y) / len;
                if (dx < -1e-12 || (Math.Abs(dx) <= 1e-12 && dy < 0)) { dx = -dx; dy = -dy; }
                double offset = g.A.X * dy - g.A.Y * dx;

                LineGroup group = groups.FirstOrDefault(q =>
                    Math.Abs(q.Dx * dy - q.Dy * dx) < 1e-4 && Math.Abs(q.Offset - offset) < 0.5
                    && Math.Abs(g.B.X * q.Dy - g.B.Y * q.Dx - q.Offset) < 0.5);
                if (group == null)
                {
                    group = new LineGroup { Dx = dx, Dy = dy, Offset = offset };
                    groups.Add(group);
                }
                double t1 = g.A.X * group.Dx + g.A.Y * group.Dy, t2 = g.B.X * group.Dx + g.B.Y * group.Dy;
                group.Runs.Add((Math.Min(t1, t2), Math.Max(t1, t2), g));
            }

            var merged = new List<Seg>();
            foreach (LineGroup group in groups)
            {
                var runs = group.Runs.OrderBy(r => r.From).ToList();
                int i = 0;
                while (i < runs.Count)
                {
                    double from = runs[i].From, to = runs[i].To;
                    int start = i;
                    while (i + 1 < runs.Count && runs[i + 1].From <= to + JoinTolMm)
                    {
                        i++;
                        to = Math.Max(to, runs[i].To);
                    }
                    if (i == start) merged.Add(runs[start].Source); // untouched piece keeps its exact ends
                    else merged.Add(new Seg(OnLine(group, from), OnLine(group, to)));
                    i++;
                }
            }
            return merged;
        }

        private static Pt OnLine(LineGroup g, double t)
            => new(g.Dx * t + g.Dy * g.Offset, g.Dy * t - g.Dx * g.Offset);

        // ── Chaining, methods 4 and 5 ───────────────────────────────────────────────────────────

        private sealed class Chained
        {
            public bool Closed;
            public List<Pt> Points = new();
            public double Gap;   // largest gap closed, or the gap that stopped the chain
            public Pt? GapAt;
        }

        /// <summary>
        /// Orders the pieces end to end (reversing where needed). Gaps up to <paramref name="snapTol"/>
        /// are closed by moving the ends together; gaps up to <paramref name="bridgeTol"/> are closed
        /// with a straight line; a larger gap stops the chain.
        /// </summary>
        private static Chained Chain(List<Seg> segs, double snapTol, double bridgeTol)
        {
            var result = new Chained();
            if (segs.Count < 3) return result;

            var used = new bool[segs.Count];
            var ordered = new List<Seg> { segs[0] };
            used[0] = true;
            for (int n = 1; n < segs.Count; n++)
            {
                Pt end = ordered[^1].B;
                int bestIndex = -1;
                bool reverse = false;
                double bestDistance = double.MaxValue;
                for (int i = 0; i < segs.Count; i++)
                {
                    if (used[i]) continue;
                    double da = end.DistanceTo(segs[i].A), db = end.DistanceTo(segs[i].B);
                    if (da < bestDistance) { bestDistance = da; bestIndex = i; reverse = false; }
                    if (db < bestDistance) { bestDistance = db; bestIndex = i; reverse = true; }
                }
                if (bestDistance > bridgeTol)
                {
                    result.Gap = bestDistance;
                    result.GapAt = end;
                    return result;
                }
                used[bestIndex] = true;
                ordered.Add(reverse ? segs[bestIndex].Reversed() : segs[bestIndex]);
            }

            double closing = ordered[^1].B.DistanceTo(ordered[0].A);
            if (closing > bridgeTol)
            {
                result.Gap = closing;
                result.GapAt = ordered[^1].B;
                return result;
            }

            for (int i = 0; i < ordered.Count; i++)
            {
                Seg current = ordered[i], next = ordered[(i + 1) % ordered.Count];
                double gap = current.B.DistanceTo(next.A);
                if (gap > result.Gap) { result.Gap = gap; result.GapAt = current.B.MidTo(next.A); }

                if (gap <= 1e-6) result.Points.Add(current.B);
                else if (gap <= snapTol) result.Points.Add(SnapPoint(current, next, snapTol));
                else { result.Points.Add(current.B); result.Points.Add(next.A); }
            }
            result.Closed = true;
            return result;
        }

        /// <summary>Where two pieces should meet: their lines' crossing when it is near both ends, else halfway.</summary>
        private static Pt SnapPoint(Seg a, Seg b, double tol)
        {
            double d1x = a.B.X - a.A.X, d1y = a.B.Y - a.A.Y, d2x = b.B.X - b.A.X, d2y = b.B.Y - b.A.Y;
            double denom = d1x * d2y - d1y * d2x;
            double scale = Math.Sqrt(d1x * d1x + d1y * d1y) * Math.Sqrt(d2x * d2x + d2y * d2y);
            if (scale > 1e-9 && Math.Abs(denom) / scale > 1e-3)
            {
                double t = ((b.A.X - a.A.X) * d2y - (b.A.Y - a.A.Y) * d2x) / denom;
                var x = new Pt(a.A.X + t * d1x, a.A.Y + t * d1y);
                if (x.DistanceTo(a.B) <= tol && x.DistanceTo(b.A) <= tol) return x;
            }
            return a.B.MidTo(b.A);
        }

        // ── Method 6 ────────────────────────────────────────────────────────────────────────────

        /// <summary>Untangles an outline that crosses itself and keeps its largest clean part.</summary>
        private static IReadOnlyList<Pt> RemoveOverlaps(IReadOnlyList<Pt> pts)
        {
            if (pts == null || pts.Count < 3) return new List<Pt>();
            var path = new Path64(pts.Count);
            foreach (Pt p in pts)
                path.Add(new Point64((long)Math.Round(p.X * ClipperScale), (long)Math.Round(p.Y * ClipperScale)));

            Paths64 union = Clipper.Union(new Paths64 { path }, FillRule.NonZero);
            IReadOnlyList<Pt> bestPart = new List<Pt>();
            double bestArea = 0;
            foreach (Path64 part in union)
            {
                var candidate = Normalize(part.Select(q => new Pt(q.X / ClipperScale, q.Y / ClipperScale)).ToList());
                double area = Polyline2D.AreaM2(candidate);
                if (area > bestArea) { bestArea = area; bestPart = candidate; }
            }
            return bestPart;
        }
    }
}
