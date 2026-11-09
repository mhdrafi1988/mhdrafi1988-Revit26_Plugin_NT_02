// Merge Roofs V001 — checks footprints for overlap / intersection / gaps / separation and builds the merged loops.
using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models;

namespace Revit26_Plugin_Revised.MergeRoofs.V001.Core.Services
{
    /// <summary>Analyses a set of roof footprints and merges them into closed loops.</summary>
    public static class MergeAnalyzer
    {
        /// <summary>Shortest curve Revit accepts in a sketch (feet).</summary>
        public const double ShortCurve = 0.00256;
        private const double MinAreaSqFt = 1e-6;

        /// <summary>
        /// Reports overlap, intersection, gap (snapped up to <paramref name="toleranceFeet"/>) and not-joined problems,
        /// and returns the merged outer/hole loops. <see cref="MergeResult.CanCreate"/> is true only for a single outer loop.
        /// </summary>
        public static MergeResult Analyze(IReadOnlyList<RoofFootprint> roofs, double toleranceFeet)
        {
            var issues = new List<Issue>();
            var regions = roofs.Select(RoofUnion.ToRegion).ToList();

            FindPairIssues(roofs, regions, issues);

            Paths64 merged = RoofUnion.Union(regions);
            var links = new List<GapLink>();

            if (toleranceFeet > 0)
            {
                Paths64 closed = RoofUnion.Close(merged, toleranceFeet);
                Paths64 fill = Clipper.Difference(closed, merged, FillRule.NonZero);
                var keep = new Paths64();
                foreach (var f in fill)
                {
                    if (Math.Abs(Clipper.Area(f)) / (RoofUnion.Scale * RoofUnion.Scale) < MinAreaSqFt) continue;
                    var b = Clipper.GetBounds(f);
                    double extent = Math.Max(b.Width, b.Height) / RoofUnion.Scale;
                    if (extent < 2 * toleranceFeet) continue;       // corner touch, not a real gap
                    keep.Add(f);
                }
                if (keep.Count > 0)
                {
                    Paths64 withFill = Clipper.Union(merged.Concat(keep).ToList().ToPaths(), FillRule.NonZero);
                    foreach (var f in keep)
                    {
                        var pts = RoofUnion.FromPath(f);
                        issues.Add(new Issue(IssueKind.Gap, Centroid(pts), "Gap between roofs closed within the tolerance."));
                        links.AddRange(BridgeLines(pts, withFill, merged));
                    }
                    merged = withFill;
                }
            }

            var (outerPts, holePts) = RoofUnion.Split(merged);
            var outers = outerPts.Select(ToLoop).Where(l => l != null).Select(l => l!).ToList();
            var holes = holePts.Select(ToLoop).Where(l => l != null).Select(l => l!).ToList();

            if (outers.Count > 1) AddNotJoined(outerPts, issues);

            var allSources = roofs;
            outers = outers.Select(l => new Loop(SlopeMapper.Apply(l.Edges, allSources, toleranceFeet))).ToList();
            holes = holes.Select(l => new Loop(SlopeMapper.Apply(l.Edges, allSources, toleranceFeet))).ToList();

            return new MergeResult
            {
                Outer = outers,
                Holes = holes,
                Issues = issues,
                GapLinks = links,
                CanCreate = outers.Count == 1 && !issues.Any(i => i.Kind == IssueKind.NotJoined),
            };
        }

        private static void FindPairIssues(IReadOnlyList<RoofFootprint> roofs, List<Paths64> regions, List<Issue> issues)
        {
            for (int i = 0; i < roofs.Count; i++)
                for (int j = i + 1; j < roofs.Count; j++)
                {
                    var common = Clipper.Intersect(regions[i], regions[j], FillRule.NonZero);
                    if (common.Count > 0 && Math.Abs(RoofUnion.AreaFeet(common)) > MinAreaSqFt)
                        issues.Add(new Issue(IssueKind.Overlap, Centroid(RoofUnion.FromPath(common[0])),
                            $"Roofs {roofs[i].Id} and {roofs[j].Id} overlap."));
                    var x = FirstCrossing(roofs[i], roofs[j]);
                    if (x is Pt p)
                        issues.Add(new Issue(IssueKind.Intersection, p, $"Boundaries of roofs {roofs[i].Id} and {roofs[j].Id} cross."));
                }
        }

        private static IEnumerable<List<Pt>> Rings(RoofFootprint f)
        {
            yield return ArcGeometry.Ring(f.Outer);
            foreach (var h in f.Holes) yield return ArcGeometry.Ring(h);
        }

        private static Pt? FirstCrossing(RoofFootprint a, RoofFootprint b)
        {
            foreach (var ra in Rings(a))
                foreach (var rb in Rings(b))
                    for (int i = 0; i < ra.Count; i++)
                        for (int j = 0; j < rb.Count; j++)
                        {
                            var p = ProperCross(ra[i], ra[(i + 1) % ra.Count], rb[j], rb[(j + 1) % rb.Count]);
                            if (p != null) return p;
                        }
            return null;
        }

        private static Pt? ProperCross(Pt p, Pt p2, Pt q, Pt q2)
        {
            double rx = p2.X - p.X, ry = p2.Y - p.Y, sx = q2.X - q.X, sy = q2.Y - q.Y;
            double den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-12) return null;
            double t = ((q.X - p.X) * sy - (q.Y - p.Y) * sx) / den;
            double u = ((q.X - p.X) * ry - (q.Y - p.Y) * rx) / den;
            const double e = 1e-9;
            if (t <= e || t >= 1 - e || u <= e || u >= 1 - e) return null;
            return new Pt(p.X + t * rx, p.Y + t * ry);
        }

        private static Loop? ToLoop(List<Pt> pts)
        {
            var ring = new List<Pt>(pts);
            bool changed = true;
            while (changed && ring.Count > 3)
            {
                changed = false;
                for (int i = 0; i < ring.Count && ring.Count > 3; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                    if (Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)) < ShortCurve)
                    {
                        ring.RemoveAt((i + 1) % ring.Count);
                        changed = true;
                        break;
                    }
                }
            }
            if (ring.Count < 3) return null;
            var edges = new List<Edge>(ring.Count);
            for (int i = 0; i < ring.Count; i++) edges.Add(new Edge { A = ring[i], B = ring[(i + 1) % ring.Count] });
            return new Loop(edges);
        }

        private static void AddNotJoined(List<List<Pt>> outers, List<Issue> issues)
        {
            for (int i = 1; i < outers.Count; i++)
            {
                double best = double.MaxValue; Pt pa = default, pb = default;
                foreach (var a in outers[0]) foreach (var b in outers[i])
                {
                    double d = (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
                    if (d < best) { best = d; pa = a; pb = b; }
                }
                issues.Add(new Issue(IssueKind.NotJoined, new Pt((pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2),
                    $"Separate roof group is {Math.Sqrt(best):0.###} ft from the main group — not joined."));
            }
        }

        private static IEnumerable<GapLink> BridgeLines(List<Pt> fillPts, Paths64 closed, Paths64 raw)
        {
            for (int i = 0; i < fillPts.Count; i++)
            {
                var a = fillPts[i]; var b = fillPts[(i + 1) % fillPts.Count];
                var mid = new Pt((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                if (OnBoundary(mid, closed) && !OnBoundary(mid, raw)) yield return new GapLink(a, b);
            }
        }

        private static bool OnBoundary(Pt s, Paths64 paths)
        {
            const double eps = 3.0 / RoofUnion.Scale;
            foreach (var path in paths)
            {
                var pts = RoofUnion.FromPath(path);
                for (int i = 0; i < pts.Count; i++)
                    if (ArcGeometry.DistToSegment(s, pts[i], pts[(i + 1) % pts.Count]) < eps) return true;
            }
            return false;
        }

        private static Pt Centroid(List<Pt> pts) => new(pts.Average(p => p.X), pts.Average(p => p.Y));

        private static Paths64 ToPaths(this List<Path64> l) { var p = new Paths64(); p.AddRange(l); return p; }
    }
}
