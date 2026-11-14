// Point generation + slope-by-drain solve for ONE roof. No Revit references.
//
//  1. Voronoi cells of the selected drains (half-plane clipping) -> ridge lines,
//     and which drains are neighbours (the Delaunay pairs).
//  2. Points: inner loops, curve points, perpendicular feet, ridge points / saddles.
//  3. Height of any point = slope x distance to the nearest drain edge (or valley line in saddle mode).
//     1.1.0: distance is measured to the drain's outline, not to a circle round its centre.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.RoofSlopeBuilder.V001.Core
{
    public enum PointKind { InnerLoop, Curve, Perpendicular, Ridge, SaddlePeak, SaddleTip }

    /// <summary>One shape point the solver wants on the roof, with its height above the drains.</summary>
    public class SolvedPoint
    {
        public P2 P;
        public PointKind Kind;
        public double Height;
    }

    /// <summary>One opening of the roof that may be a drain.</summary>
    public class DrainInfo
    {
        public int LoopIndex;        // index into RoofInput.Inner
        public P2 Center;
        public double Radius;        // farthest loop point from the centre
        public double Area;
        public bool Selected;
        /// <summary>The opening's outline (tessellated). Heights are measured from this edge, not from a circle.</summary>
        public List<P2> Outline = new List<P2>();

        /// <summary>
        /// Plan distance from <paramref name="p"/> to the drain's edge; 0 on or inside it.
        /// Falls back to the circle round the centre when there is no outline.
        /// </summary>
        public double DistanceTo(P2 p)
        {
            if (Outline == null || Outline.Count < 3)
                return Math.Max(0, p.DistanceTo(Center) - Radius);
            return Geo2D.Inside(Outline, p) ? 0 : Geo2D.DistToPolygon(Outline, p);
        }
    }

    /// <summary>One curved sketch edge, evaluated by normalised parameter.</summary>
    public class CurveInfo
    {
        public int LoopIndex;        // -1 = outer loop
        public double Length;
        public Func<double, P2> Eval; // normalised parameter 0..1
    }

    /// <summary>One straight sketch edge.</summary>
    public class EdgeInfo
    {
        public int LoopIndex;        // -1 = outer loop
        public P2 A, B;
    }

    /// <summary>Plan geometry of one roof, read from its sketch (lengths in feet).</summary>
    public class RoofInput
    {
        public List<P2> Outer = new List<P2>();                     // tessellated
        public List<List<P2>> Inner = new List<List<P2>>();         // tessellated
        public List<List<P2>> InnerCorners = new List<List<P2>>();  // curve end points only
        public List<EdgeInfo> StraightEdges = new List<EdgeInfo>();
        public List<CurveInfo> Curves = new List<CurveInfo>();
        public List<DrainInfo> Drains = new List<DrainInfo>();
        public List<P2> ExistingVertices = new List<P2>();

        /// <summary>True when <paramref name="p"/> is on the roof: inside the outline and outside every opening, within <paramref name="tol"/>.</summary>
        public bool Contains(P2 p, double tol)
        {
            if (!Geo2D.Inside(Outer, p) && Geo2D.DistToPolygon(Outer, p) > tol) return false;
            foreach (List<P2> hole in Inner)
                if (Geo2D.Inside(hole, p) && Geo2D.DistToPolygon(hole, p) > tol) return false;
            return true;
        }
    }

    /// <summary>Points, split lines, warnings and the height rule for one roof.</summary>
    public class SolveResult
    {
        public bool Skipped;
        public List<SolvedPoint> Points = new List<SolvedPoint>();
        public List<P2[]> SplitLines = new List<P2[]>();
        public List<string> Warnings = new List<string>();
        public HeightField Field;
        public double MaxHeight;
        public int DrainCount;
        public int RidgePointCount;
    }

    /// <summary>Height above the drains at any plan position.</summary>
    public class HeightField
    {
        public double Slope;
        public double RoundTo;
        public List<DrainInfo> Drains = new List<DrainInfo>();
        public List<P2[]> ValleySegments = new List<P2[]>();     // saddle mode
        public List<SolvedPoint> Overrides = new List<SolvedPoint>(); // saddle peaks
        public double OverrideTol;

        /// <summary>Distance to the nearest drain's edge, and that drain's index in <see cref="Drains"/>.</summary>
        public double DistanceToDrain(P2 p, out int nearest)
        {
            double best = double.MaxValue; nearest = -1;
            for (int i = 0; i < Drains.Count; i++)
            {
                double d = Drains[i].DistanceTo(p);
                if (d < best) { best = d; nearest = i; }
            }
            return best;
        }

        /// <summary>Height (feet, rounded) at plan position <paramref name="p"/>.</summary>
        public double At(P2 p)
        {
            foreach (SolvedPoint o in Overrides)
                if (o.P.DistanceTo(p) <= OverrideTol) return Round(o.Height);

            int n;
            double d = DistanceToDrain(p, out n);
            foreach (P2[] seg in ValleySegments)
                d = Math.Min(d, Geo2D.DistToSegment(p, seg[0], seg[1]));
            return Round(Slope * d);
        }

        private double Round(double h)
        {
            return RoundTo > 1e-9 ? Math.Round(h / RoundTo) * RoundTo : h;
        }
    }

    /// <summary>Default choice of which openings are drains.</summary>
    public static class DrainDetector
    {
        /// <summary>Default selection: openings not larger than the max size; optionally only the smallest size class.</summary>
        public static void ApplyDefaultSelection(IList<DrainInfo> drains, RoofSlopeSettings s)
        {
            double smallest = drains.Count == 0 ? 0 : drains.Min(d => d.Area);
            foreach (DrainInfo d in drains)
            {
                bool ok = d.Area <= s.DrainMaxArea;
                if (ok && s.LowestSizeOnly) ok = d.Area <= smallest * 1.10 + 1e-9;
                d.Selected = ok;
            }
        }
    }

    /// <summary>Works out the points and heights for one roof. Pure geometry: no Revit calls.</summary>
    public static class SlopeSolver
    {
        private class RidgeSeg { public P2 A, B; public int I, J; }

        /// <summary>Solves one roof. Returns <see cref="SolveResult.Skipped"/> when no drain is selected.</summary>
        public static SolveResult Solve(RoofInput roof, RoofSlopeSettings s)
        {
            SolveResult res = new SolveResult();
            List<DrainInfo> drains = roof.Drains.Where(d => d.Selected).ToList();
            res.DrainCount = drains.Count;
            if (drains.Count == 0)
            {
                res.Skipped = true;
                res.Warnings.Add("No drain selected - roof skipped.");
                return res;
            }

            double tol = Math.Max(s.MergeTolerance, 1e-4);
            HashSet<int> drainLoops = new HashSet<int>(drains.Select(d => d.LoopIndex));

            HeightField field = new HeightField();
            field.Slope = s.Slope; field.RoundTo = s.RoundHeightTo; field.Drains = drains; field.OverrideTol = tol * 0.5;
            res.Field = field;

            List<SolvedPoint> pts = res.Points;
            // points that already exist on the roof block new ones nearby
            List<P2> occupied = new List<P2>(roof.ExistingVertices);

            Func<P2, PointKind, bool, bool> add = delegate(P2 p, PointKind kind, bool mustBeInside)
            {
                if (mustBeInside && !roof.Contains(p, 1e-6)) return false;
                foreach (P2 q in occupied) if (q.DistanceTo(p) < tol) return false;
                occupied.Add(p);
                pts.Add(new SolvedPoint { P = p, Kind = kind });
                return true;
            };

            // ---------- 1. ridges between drains (Voronoi) ----------
            List<RidgeSeg> ridges = drains.Count > 1 ? BuildRidges(roof, drains) : new List<RidgeSeg>();

            if (s.AddDrainRidges && drains.Count > 1 && s.RidgeMode == RidgeMode.Saddle)
            {
                HashSet<long> done = new HashSet<long>();
                foreach (RidgeSeg r in ridges)
                {
                    long key = (long)r.I * 100000 + r.J;
                    if (!done.Add(key)) continue;

                    P2 a = drains[r.I].Center, b = drains[r.J].Center;
                    double len = a.DistanceTo(b);
                    if (len > s.MaxDrainSpacing) continue;

                    double w = len * s.SaddleWidthRatio;
                    P2 mid = (a + b) * 0.5;
                    P2 v = (b - a).Normalized().Perp();
                    if (!roof.Contains(mid, 1e-6)) continue;

                    field.ValleySegments.Add(new[] { a, b });

                    double peakH = s.SaddleSlopeFactor * s.Slope * w / 2;
                    if (add(mid, PointKind.SaddlePeak, false))
                        field.Overrides.Add(new SolvedPoint { P = mid, Height = peakH });

                    foreach (double sign in new[] { 1.0, -1.0 })
                    {
                        P2 tip = mid + v * (sign * w / 2);
                        if (!roof.Contains(tip, 1e-6)) continue;
                        add(tip, PointKind.SaddleTip, false);
                        if (s.AddSplitLines) res.SplitLines.Add(new[] { mid, tip });
                    }

                    // fall along the saddle edge (drain -> side tip): the line water actually follows
                    double valley = s.Slope * w / Math.Sqrt(len * len + w * w);
                    if (valley > 0 && 1 / valley > s.MinFallN)
                        res.Warnings.Add(string.Format(
                            "Saddle between drains {0} and {1}: fall along the saddle edge is only 1:{2:0} (minimum 1:{3:0}). Widen the saddle or steepen the slope.",
                            r.I + 1, r.J + 1, 1 / valley, s.MinFallN));
                }
            }

            // ---------- 2. perpendicular points ----------
            if (s.AddPerpendicular)
            {
                for (int i = 0; i < drains.Count; i++)
                {
                    P2 c = drains[i].Center;

                    if (s.PerpToRidge && s.AddDrainRidges && s.RidgeMode == RidgeMode.Watershed)
                        foreach (RidgeSeg r in ridges)
                        {
                            if (r.I != i && r.J != i) continue;
                            P2 foot; double t;
                            Geo2D.DistToSegment(c, r.A, r.B, out foot, out t);
                            if (t >= 0 && t <= 1) add(foot, PointKind.Ridge, false);
                        }

                    if (s.PerpToBoundary)
                        foreach (EdgeInfo e in roof.StraightEdges)
                        {
                            if (drainLoops.Contains(e.LoopIndex)) continue;
                            P2 foot; double t;
                            Geo2D.DistToSegment(c, e.A, e.B, out foot, out t);
                            if (t < 0.02 || t > 0.98) continue;       // foot must lie inside the edge
                            int nearest;
                            field.DistanceToDrain(foot, out nearest);
                            if (nearest != i) continue;                // stay inside this drain's catchment
                            add(foot, PointKind.Perpendicular, false);
                        }
                }
            }

            // ---------- 3. watershed ridge points ----------
            if (s.AddDrainRidges && s.RidgeMode == RidgeMode.Watershed)
            {
                double spacing = Math.Max(s.RidgeSpacing, tol);
                foreach (RidgeSeg r in ridges)
                {
                    double len = r.A.DistanceTo(r.B);
                    int n = Math.Max(1, (int)Math.Ceiling(len / spacing));
                    P2 prev = r.A;
                    for (int k = 0; k <= n; k++)
                    {
                        P2 p = r.A + (r.B - r.A) * (k / (double)n);
                        add(p, PointKind.Ridge, false);
                        if (k > 0 && s.AddSplitLines) res.SplitLines.Add(new[] { prev, p });
                        prev = p;
                    }
                }
            }

            // ---------- 4. inner loops ----------
            if (s.IncludeInnerLoops)
                for (int i = 0; i < roof.Inner.Count; i++)
                {
                    if (drainLoops.Contains(i)) continue;              // drain outlines stay level, Revit already has them
                    if (Geo2D.Area(roof.Inner[i]) < s.InnerMinArea) continue;
                    foreach (P2 p in roof.InnerCorners[i]) add(p, PointKind.InnerLoop, false);
                }

            // ---------- 5. points on curves ----------
            if (s.AddCurvePoints)
                foreach (CurveInfo c in roof.Curves)
                {
                    if (drainLoops.Contains(c.LoopIndex)) continue;
                    int n = c.Length >= s.LargeCurveLength ? s.CurvePointCount : (s.SmallCurveMidpoint ? 1 : 0);
                    if (n > 0 && c.Length / (n + 1) < s.MinGap)
                    {
                        if (s.GapMode == GapMode.ReduceCount)
                            n = Math.Max(0, (int)Math.Floor(c.Length / s.MinGap) - 1);
                        else if (s.GapMode == GapMode.SkipCurve)
                        {
                            n = 0;
                            res.Warnings.Add("A curve was skipped: too short for the minimum gap.");
                        }
                    }
                    for (int k = 1; k <= n; k++) add(c.Eval(k / (double)(n + 1)), PointKind.Curve, false);
                }

            // ---------- 6. heights ----------
            foreach (SolvedPoint p in pts)
            {
                p.Height = field.At(p.P);
                res.MaxHeight = Math.Max(res.MaxHeight, p.Height);
            }
            foreach (P2 p in roof.ExistingVertices)
                res.MaxHeight = Math.Max(res.MaxHeight, field.At(p));

            res.RidgePointCount = pts.Count(p => p.Kind == PointKind.Ridge || p.Kind == PointKind.SaddlePeak);

            if (s.Slope > 0 && 1 / s.Slope > s.MinFallN)
                res.Warnings.Add(string.Format("Slope is 1:{0:0}, flatter than the minimum fall 1:{1:0}.", 1 / s.Slope, s.MinFallN));
            if (res.MaxHeight > s.MaxHeightWarn)
                res.Warnings.Add(string.Format("Highest point is {0:0} mm, above the {1:0} mm limit.",
                    res.MaxHeight * 304.8, s.MaxHeightWarn * 304.8));
            return res;
        }

        /// <summary>
        /// Voronoi cell of every drain by clipping a large box with the bisector of each other drain.
        /// Cell edges lying on a bisector are the ridges; they are then cut to the roof outline and openings.
        /// </summary>
        private static List<RidgeSeg> BuildRidges(RoofInput roof, List<DrainInfo> drains)
        {
            double minX = roof.Outer.Min(p => p.X), maxX = roof.Outer.Max(p => p.X);
            double minY = roof.Outer.Min(p => p.Y), maxY = roof.Outer.Max(p => p.Y);
            double m = (maxX - minX) + (maxY - minY) + 1;

            List<RidgeSeg> raw = new List<RidgeSeg>();
            for (int i = 0; i < drains.Count; i++)
            {
                List<P2> cell = new List<P2> {
                    new P2(minX - m, minY - m), new P2(maxX + m, minY - m),
                    new P2(maxX + m, maxY + m), new P2(minX - m, maxY + m) };
                List<int> tag = new List<int> { -1, -1, -1, -1 };   // tag[k] = drain behind edge k -> k+1

                for (int j = 0; j < drains.Count && cell.Count > 2; j++)
                {
                    if (j == i) continue;
                    P2 ci = drains[i].Center, cj = drains[j].Center;
                    if (ci.DistanceTo(cj) < 1e-9) continue;
                    P2 mid = (ci + cj) * 0.5, nrm = cj - ci;

                    List<P2> outP = new List<P2>(); List<int> outT = new List<int>();
                    for (int k = 0; k < cell.Count; k++)
                    {
                        P2 a = cell[k], b = cell[(k + 1) % cell.Count];
                        double da = (a - mid).Dot(nrm), db = (b - mid).Dot(nrm);
                        bool ina = da <= 0, inb = db <= 0;
                        if (ina)
                        {
                            outP.Add(a); outT.Add(tag[k]);
                            if (!inb) { outP.Add(a + (b - a) * (da / (da - db))); outT.Add(j); }
                        }
                        else if (inb)
                        {
                            outP.Add(a + (b - a) * (da / (da - db))); outT.Add(tag[k]);
                        }
                    }
                    cell = outP; tag = outT;
                }

                for (int k = 0; k < cell.Count; k++)
                    if (tag[k] > i)   // each shared edge once
                        raw.Add(new RidgeSeg { A = cell[k], B = cell[(k + 1) % cell.Count], I = i, J = tag[k] });
            }

            // cut to the roof: keep the parts inside the outline and outside openings
            List<RidgeSeg> result = new List<RidgeSeg>();
            List<List<P2>> loops = new List<List<P2>> { roof.Outer };
            loops.AddRange(roof.Inner);

            foreach (RidgeSeg r in raw)
            {
                double len = r.A.DistanceTo(r.B);
                if (len < 1e-6) continue;
                List<double> cuts = new List<double> { 0, 1 };
                foreach (List<P2> loop in loops)
                    for (int k = 0; k < loop.Count; k++)
                    {
                        double t = Geo2D.SegmentCrossParam(r.A, r.B, loop[k], loop[(k + 1) % loop.Count]);
                        if (!double.IsNaN(t)) cuts.Add(t);
                    }
                cuts.Sort();
                for (int k = 0; k + 1 < cuts.Count; k++)
                {
                    double t0 = cuts[k], t1 = cuts[k + 1];
                    if ((t1 - t0) * len < 1e-4) continue;
                    P2 midp = r.A + (r.B - r.A) * ((t0 + t1) / 2);
                    if (!roof.Contains(midp, 0)) continue;
                    result.Add(new RidgeSeg { A = r.A + (r.B - r.A) * t0, B = r.A + (r.B - r.A) * t1, I = r.I, J = r.J });
                }
            }
            return result;
        }
    }
}
