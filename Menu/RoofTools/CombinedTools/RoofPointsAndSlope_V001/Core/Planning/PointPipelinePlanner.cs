// =======================================================
// File: PointPipelinePlanner.cs
// Location: Core/Planning/
// Plans the new shape points of steps 1–4, in plan, without any Revit
// call — the window re-plans on every change and the run plans again
// from freshly read shape points, so the preview and the run agree.
//
// Each step sees the points of the steps before it, so nothing is added
// twice and step 4 only fills gaps that are still there:
//   1 Opening points   — Inner Loop Divider V012's DivisionPlanner.
//   2 Perpendicular    — Inner Loops + Perpendicular V007: the points on
//                        an opening's edge are grouped (XY proximity);
//                        from each group's centre the nearest point of
//                        every boundary edge is taken, the nearest one per
//                        45° direction (relative to the opening's first
//                        straight side) is kept.
//   3 Midpoint/quarter — Multiple Points V003: ½, and ¼ + ¾ of the edge
//                        length, on the edges in scope.
//   4 Gap fill         — new: along each edge in scope, wherever two
//                        neighbouring points (existing, planned or the
//                        edge's ends) are further apart than the interval,
//                        evenly spaced points are added so no gap is
//                        longer than the interval.
// A planned point within the tolerance of a known point is counted as
// "already there" and not added. Steps 2–4 also skip a point closer than
// the minimum spacing to any known point ("too close"), so Revit isn't
// given thin triangles; step 1 is exempt. With "clear existing points
// first" the roof's current points are ignored and only the sketch
// corners (which Revit keeps after a reset) count as existing.
// =======================================================

using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Planning
{
    /// <summary>The four point steps, in run order.</summary>
    public enum PointStep { OpeningPoints, Perpendicular, MidQuarter, GapFill }

    /// <summary>One new shape point.</summary>
    public sealed class NewPoint
    {
        /// <summary>Plan position (feet).</summary>
        public P2 Position { get; init; }

        /// <summary>Unit direction into the roof, used to nudge a point Revit rejects on an edge. (0,0) when unknown.</summary>
        public P2 Inward { get; init; }

        /// <summary>Opening number for steps 1–2, 0 otherwise.</summary>
        public int OpeningIndex { get; init; }
    }

    /// <summary>The planned points of one step.</summary>
    public sealed class StepPlan
    {
        /// <summary>The step.</summary>
        public PointStep Step { get; init; }

        /// <summary>Points the step will add.</summary>
        public List<NewPoint> Points { get; } = new List<NewPoint>();

        /// <summary>Planned positions that are already shape points (or planned by an earlier step).</summary>
        public int AlreadyThere { get; set; }

        /// <summary>Planned positions skipped for being closer than the minimum spacing to another point.</summary>
        public int TooClose { get; set; }
    }

    /// <summary>The planned points of all enabled point steps.</summary>
    public sealed class PipelinePlan
    {
        /// <summary>One entry per point step, in run order (disabled steps have no points).</summary>
        public List<StepPlan> Steps { get; } = new List<StepPlan>();

        /// <summary>The plan of <paramref name="step"/>.</summary>
        public StepPlan this[PointStep step] => Steps.First(s => s.Step == step);

        /// <summary>New points over all steps.</summary>
        public int TotalNew => Steps.Sum(s => s.Points.Count);
    }

    /// <summary>Plans steps 1–4 (see the file header).</summary>
    public static class PointPipelinePlanner
    {
        /// <summary>5 mm in feet — points closer than this are the same point.</summary>
        public const double Tolerance = 5.0 / 304.8;

        private const double FeetPerMeter = 1 / 0.3048;

        /// <summary>Plans every enabled point step for <paramref name="outline"/>.</summary>
        public static PipelinePlan Plan(RoofOutline outline, PipelineSettings settings)
        {
            if (outline == null) throw new ArgumentNullException(nameof(outline));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var plan = new PipelinePlan();
            var known = new KnownPoints(StartingPoints(outline, settings), Math.Max(0, settings.MinPointSpacingMm) / 304.8);

            var inner = new StepPlan { Step = PointStep.OpeningPoints };
            var perp = new StepPlan { Step = PointStep.Perpendicular };
            var mid = new StepPlan { Step = PointStep.MidQuarter };
            var gap = new StepPlan { Step = PointStep.GapFill };
            plan.Steps.AddRange(new[] { inner, perp, mid, gap });

            if (settings.InnerPointsEnabled)
                PlanOpeningPoints(outline, settings, known, inner);
            if (settings.PerpendicularEnabled)
                PlanPerpendicular(outline, settings, known, perp);
            if (settings.MidQuarterEnabled)
                PlanMidQuarter(outline, settings, known, mid);
            if (settings.GapFillEnabled)
                PlanGapFill(outline, settings, known, gap);

            return plan;
        }

        /// <summary>
        /// The points the roof has when step 1 starts: its current shape points, or — when they are
        /// cleared first — the sketch corners (every edge's start), which Revit keeps after a reset.
        /// </summary>
        public static List<P2> StartingPoints(RoofOutline outline, PipelineSettings settings) =>
            settings.ClearExistingFirst
                ? outline.Edges.Select(e => e.Segment.Start).ToList()
                : outline.ExistingVertices.ToList();

        // ── Step 1 ────────────────────────────────────────────────────────────
        private static void PlanOpeningPoints(RoofOutline outline, PipelineSettings s, KnownPoints known, StepPlan step)
        {
            double spacing = Math.Max(0.05, s.InnerSpacingMeters) * FeetPerMeter;
            foreach (OpeningModel o in outline.Openings.Where(o => o.AddInnerPoints && o.Loop != null))
            {
                DivisionPlan dp = DivisionPlanner.Plan(o.Loop, s.InnerMode, Math.Max(0, o.PointCount), spacing,
                                                       Array.Empty<P2>(), Tolerance);
                foreach (PlannedPoint p in dp.Points)
                {
                    P2 inward = InwardOnOpening(o.Loop, o.Loop.Segments[p.Segment], p.Fraction);
                    known.TryAdd(step, new NewPoint { Position = p.Position, Inward = inward, OpeningIndex = o.Index }, enforceSpacing: false);
                }
            }
        }

        // ── Step 2 ────────────────────────────────────────────────────────────
        private static void PlanPerpendicular(RoofOutline outline, PipelineSettings s, KnownPoints known, StepPlan step)
        {
            double groupDist = Math.Max(1, s.GroupProximityMm) / 304.8;

            foreach (OpeningModel o in outline.Openings.Where(o => o.AddPerpendicular && o.Loop != null))
            {
                Loop2D boundary = BoundaryAround(outline, o.Loop);
                if (boundary == null) continue;

                // Pool: every known point on this opening's edge, plus its joints.
                var pool = known.All.Where(p => o.Loop.DistanceTo(p) <= Tolerance).ToList();
                pool.AddRange(o.Loop.Segments.Select(seg => seg.Start));
                if (pool.Count == 0) continue;

                var groups = new List<List<P2>>();
                foreach (P2 p in pool)
                {
                    List<P2> match = groups.FirstOrDefault(g => Centre(g).DistanceTo(p) <= groupDist);
                    if (match != null) match.Add(p);
                    else groups.Add(new List<P2> { p });
                }

                LoopSegment2D firstLine = o.Loop.Segments.FirstOrDefault(seg => seg.IsStraight);
                P2 dir = firstLine != null ? firstLine.End - firstLine.Start : new P2(1, 0);
                double orientation = Math.Atan2(dir.Y, dir.X);

                var boundaryEdges = outline.Edges.Where(e => e.IsBoundary && ReferenceEquals(e.Loop, boundary)).ToList();
                foreach (List<P2> g in groups)
                {
                    P2 c = Centre(g);
                    var nearestPerSector = new Dictionary<int, (P2 Point, double Distance)>();
                    foreach (RoofEdge edge in boundaryEdges)
                    {
                        (double t, double d) = edge.Segment.Project(c);
                        P2 q = edge.Segment.PointAt(t);
                        P2 toHit = q - c;
                        if (toHit.Length < 1e-9) continue;

                        double angle = NormalizeAngle(Math.Atan2(toHit.Y, toHit.X) - orientation);
                        int sector = (int)Math.Round(angle / (Math.PI / 4)) % 8;
                        if (!nearestPerSector.TryGetValue(sector, out var best) || d < best.Distance)
                            nearestPerSector[sector] = (q, d);
                    }

                    foreach (var hit in nearestPerSector.Values.OrderBy(h => h.Distance))
                        known.TryAdd(step, new NewPoint { Position = hit.Point, Inward = (c - hit.Point).Normalized(), OpeningIndex = o.Index });
                }
            }
        }

        // ── Step 3 ────────────────────────────────────────────────────────────
        private static void PlanMidQuarter(RoofOutline outline, PipelineSettings s, KnownPoints known, StepPlan step)
        {
            var fractions = new List<double>();
            if (s.AddMidpoint) fractions.Add(0.5);
            if (s.AddQuarterPoints) { fractions.Add(0.25); fractions.Add(0.75); }
            if (fractions.Count == 0) return;

            foreach (RoofEdge edge in outline.Edges.Where(s.MidQuarterScope.Includes))
            {
                foreach (double f in fractions)
                    known.TryAdd(step, EdgePoint(edge, f));
            }
        }

        // ── Step 4 ────────────────────────────────────────────────────────────
        private static void PlanGapFill(RoofOutline outline, PipelineSettings s, KnownPoints known, StepPlan step)
        {
            double interval = Math.Max(0.05, s.GapIntervalMeters) * FeetPerMeter;

            foreach (RoofEdge edge in outline.Edges.Where(s.GapScope.Includes))
            {
                double length = edge.Segment.Length;
                if (length <= interval + Tolerance) continue;

                // Positions (by length) of every known point on this edge, plus both ends.
                var positions = new List<double> { 0, length };
                foreach (P2 p in known.All)
                {
                    (double t, double d) = edge.Segment.Project(p);
                    if (d <= Tolerance) positions.Add(t * length);
                }

                positions.Sort();
                for (int i = 0; i + 1 < positions.Count; i++)
                {
                    double a = positions[i], b = positions[i + 1];
                    double g = b - a;
                    if (g <= interval + Tolerance) continue;

                    int extra = (int)Math.Ceiling(g / interval - 1e-9) - 1;
                    for (int k = 1; k <= extra; k++)
                        known.TryAdd(step, EdgePoint(edge, (a + g * k / (extra + 1)) / length));
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static NewPoint EdgePoint(RoofEdge edge, double fraction)
        {
            P2 inward = edge.IsBoundary
                ? InwardOnBoundary(edge.Loop, edge.Segment, fraction)
                : InwardOnOpening(edge.Loop, edge.Segment, fraction);
            return new NewPoint { Position = edge.Segment.PointAt(fraction), Inward = inward, OpeningIndex = edge.OpeningIndex };
        }

        /// <summary>Unit normal at t pointing into the roof, for a boundary edge (into the loop).</summary>
        private static P2 InwardOnBoundary(Loop2D loop, LoopSegment2D seg, double t)
        {
            P2 n = seg.TangentAt(t).Left;
            return loop.Contains(seg.PointAt(t) + n * (10 * Tolerance)) ? n : n * -1;
        }

        /// <summary>Unit normal at t pointing into the roof, for an opening edge (out of the opening).</summary>
        private static P2 InwardOnOpening(Loop2D loop, LoopSegment2D seg, double t)
        {
            P2 n = seg.TangentAt(t).Left;
            return loop.Contains(seg.PointAt(t) + n * (10 * Tolerance)) ? n * -1 : n;
        }

        /// <summary>The smallest boundary loop around <paramref name="opening"/>.</summary>
        private static Loop2D BoundaryAround(RoofOutline outline, Loop2D opening) =>
            outline.Boundaries
                .Where(b => b.Contains(opening.Centroid) || b.Contains(opening.Samples[0]))
                .OrderBy(b => b.Area)
                .FirstOrDefault();

        private static P2 Centre(List<P2> pts) => new P2(pts.Average(p => p.X), pts.Average(p => p.Y));

        private static double NormalizeAngle(double a)
        {
            double two = 2 * Math.PI;
            a %= two;
            return a < 0 ? a + two : a;
        }

        /// <summary>Existing shape points plus every point planned so far.</summary>
        private sealed class KnownPoints
        {
            private readonly List<P2> _all;
            private readonly double _minSpacing;

            public KnownPoints(IEnumerable<P2> existing, double minSpacingFeet)
            {
                _all = (existing ?? Enumerable.Empty<P2>()).ToList();
                _minSpacing = minSpacingFeet;
            }

            public IReadOnlyList<P2> All => _all;

            /// <summary>
            /// Adds the point to the step, unless a known point is already there, or (when
            /// <paramref name="enforceSpacing"/>) one is closer than the minimum spacing.
            /// </summary>
            public void TryAdd(StepPlan step, NewPoint p, bool enforceSpacing = true)
            {
                double nearest = _all.Count == 0 ? double.MaxValue : _all.Min(q => q.DistanceTo(p.Position));
                if (nearest <= Tolerance)
                {
                    step.AlreadyThere++;
                    return;
                }

                if (enforceSpacing && nearest < _minSpacing)
                {
                    step.TooClose++;
                    return;
                }

                _all.Add(p.Position);
                step.Points.Add(p);
            }
        }
    }
}
