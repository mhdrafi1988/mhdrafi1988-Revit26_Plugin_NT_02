using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.RoofTag.V018.Helpers
{
    /// <summary>Which set of sketch loops a Filter() call should operate on.</summary>
    public enum LoopScope
    {
        /// <summary>All loops except the largest-area one (openings/holes).</summary>
        Interior,

        /// <summary>Only the largest-area loop (the roof's outer boundary).</summary>
        Exterior
    }

    /// <summary>
    /// Shared core for rule 2 (interior loop reduction) and rule 3 (exterior loop
    /// reduction). Both rules use the identical mechanism — match points to a loop's
    /// boundary within a tolerance, keep the top 4 highest points on that loop only
    /// if each consecutive pair (by height) is spaced far enough apart, otherwise
    /// collapse to the single highest point — but run independently with their own
    /// spacing/tolerance values and their own loop scope (interior vs exterior).
    ///
    /// Loop source: FootPrintRoof.GetProfiles(). The loop with the largest
    /// plan-projected area is treated as the outer boundary; every other loop is
    /// interior. Requires a FootPrintRoof — other roof types have no GetProfiles()
    /// and are passed through unfiltered.
    /// </summary>
    public static class RoofTagLoopReductionHelper
    {
        public static List<XYZ> Filter(
            RoofBase roof,
            List<XYZ> points,
            double spacingFt,
            double toleranceFt,
            LoopScope scope,
            out List<XYZ> skipped,
            out List<string> loopLogs)
        {
            skipped = new List<XYZ>();
            loopLogs = new List<string>();
            string scopeLabel = scope == LoopScope.Interior ? "Interior" : "Exterior";

            if (roof is not FootPrintRoof fpRoof)
            {
                loopLogs.Add($"{scopeLabel} loop reduction skipped — roof is not a FootPrintRoof (no sketch profile available).");
                return new List<XYZ>(points);
            }

            ModelCurveArrArray profile;
            try
            {
                profile = fpRoof.GetProfiles();
            }
            catch
            {
                profile = null;
            }

            if (profile == null || profile.IsEmpty)
            {
                loopLogs.Add($"{scopeLabel} loop reduction skipped — roof sketch profile unavailable.");
                return new List<XYZ>(points);
            }

            // ── Build a plan polyline + area for every sketch loop ──────────
            var loops = new List<(List<XYZ> Polyline, double Area)>();
            foreach (ModelCurveArray loopArr in profile)
            {
                List<XYZ> poly = BuildPolyline(loopArr);
                if (poly.Count < 2) continue;
                loops.Add((poly, ShoelaceArea(poly)));
            }

            if (loops.Count == 0)
            {
                loopLogs.Add($"{scopeLabel} loop reduction skipped — no sketch loops found.");
                return new List<XYZ>(points);
            }

            // Largest-area loop = outer boundary; the rest are interior loops.
            int outerIndex = loops
                .Select((l, i) => (l.Area, i))
                .OrderByDescending(t => t.Area)
                .First().i;

            List<List<XYZ>> targetLoops;
            if (scope == LoopScope.Exterior)
            {
                targetLoops = new List<List<XYZ>> { loops[outerIndex].Polyline };
            }
            else
            {
                targetLoops = loops
                    .Where((_, i) => i != outerIndex)
                    .Select(l => l.Polyline)
                    .ToList();

                if (targetLoops.Count == 0)
                {
                    loopLogs.Add("Interior loop reduction skipped — no interior loops found on this roof.");
                    return new List<XYZ>(points);
                }
            }

            // status: 0 = untouched (out of scope / unmatched), 1 = keep, 2 = skip
            int[] status = new int[points.Count];
            bool[] claimed = new bool[points.Count];

            int loopNumber = 0;
            foreach (List<XYZ> loopPoly in targetLoops)
            {
                loopNumber++;

                var matchedIdx = new List<int>();
                for (int i = 0; i < points.Count; i++)
                {
                    if (claimed[i]) continue;
                    if (DistanceToPolylineXY(points[i], loopPoly) <= toleranceFt)
                        matchedIdx.Add(i);
                }

                if (matchedIdx.Count == 0)
                {
                    loopLogs.Add($"{scopeLabel} loop {loopNumber} — no matching points found.");
                    continue;
                }

                foreach (int i in matchedIdx) claimed[i] = true;

                // Sort matched points by height, descending. OrderByDescending is
                // stable, so ties keep original input order (tie-break rule).
                List<int> sortedIdx = matchedIdx
                    .OrderByDescending(i => points[i].Z)
                    .ToList();

                int topCount = Math.Min(4, sortedIdx.Count);
                List<int> topIdx = sortedIdx.Take(topCount).ToList();
                List<int> beyondTop = sortedIdx.Skip(topCount).ToList();

                bool allSpaced = true;
                for (int i = 0; i < topIdx.Count - 1; i++)
                {
                    double horizDist = HorizontalDistance(points[topIdx[i]], points[topIdx[i + 1]]);
                    if (horizDist < spacingFt)
                    {
                        allSpaced = false;
                        break;
                    }
                }

                List<int> survivors;
                List<int> dropped = new List<int>(beyondTop);

                if (allSpaced)
                {
                    survivors = topIdx;
                }
                else
                {
                    survivors = new List<int> { topIdx[0] };
                    dropped.AddRange(topIdx.Skip(1));
                }

                foreach (int i in survivors) status[i] = 1;
                foreach (int i in dropped) status[i] = 2;

                double spacingMm = UnitUtils.ConvertFromInternalUnits(spacingFt, UnitTypeId.Millimeters);
                loopLogs.Add(
                    $"{scopeLabel} loop {loopNumber} — {matchedIdx.Count} pt(s) on loop, " +
                    $"top {topIdx.Count} checked, kept {survivors.Count} " +
                    $"({(allSpaced ? "spacing OK" : $"below {spacingMm:0} mm spacing → highest only")})");
            }

            var result = new List<XYZ>();
            for (int i = 0; i < points.Count; i++)
            {
                if (status[i] == 2)
                    skipped.Add(points[i]);
                else
                    result.Add(points[i]);
            }

            return result;
        }

        private static List<XYZ> BuildPolyline(ModelCurveArray loopArr)
        {
            var poly = new List<XYZ>();
            foreach (ModelCurve mc in loopArr)
            {
                Curve c = mc?.GeometryCurve;
                if (c == null) continue;

                IList<XYZ> tess;
                try { tess = c.Tessellate(); }
                catch { continue; }

                foreach (XYZ p in tess)
                {
                    if (poly.Count == 0 || poly[^1].DistanceTo(p) > 1e-6)
                        poly.Add(p);
                }
            }
            return poly;
        }

        private static double ShoelaceArea(List<XYZ> poly)
        {
            double sum = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                XYZ a = poly[i];
                XYZ b = poly[(i + 1) % poly.Count];
                sum += (a.X * b.Y) - (b.X * a.Y);
            }
            return Math.Abs(sum) * 0.5;
        }

        private static double DistanceToPolylineXY(XYZ pt, List<XYZ> poly)
        {
            double min = double.MaxValue;
            for (int i = 0; i < poly.Count - 1; i++)
            {
                double d = DistancePointToSegmentXY(pt, poly[i], poly[i + 1]);
                if (d < min) min = d;
            }
            return min;
        }

        private static double DistancePointToSegmentXY(XYZ p, XYZ a, XYZ b)
        {
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double apx = p.X - a.X, apy = p.Y - a.Y;
            double lenSq = abx * abx + aby * aby;
            double t = lenSq < 1e-12 ? 0 : (apx * abx + apy * aby) / lenSq;
            t = Math.Max(0, Math.Min(1, t));
            double cx = a.X + t * abx, cy = a.Y + t * aby;
            double dx = p.X - cx, dy = p.Y - cy;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double HorizontalDistance(XYZ a, XYZ b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
