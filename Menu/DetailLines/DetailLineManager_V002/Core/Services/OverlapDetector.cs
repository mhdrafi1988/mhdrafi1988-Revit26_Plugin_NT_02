using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.DetailLineManager.V002.Core.Services
{
    /// <summary>
    /// Finds straight detail lines of the same style in the same view that lie on top of each other
    /// (collinear within a tolerance and sharing more than the tolerance of length) without being
    /// exact duplicates. Reports overlap groups and lines fully covered by a longer one.
    /// </summary>
    public static class OverlapDetector
    {
        // Above this many lines of one style in one view, pairs are only compared within the same
        // rounded direction, which can miss lines whose directions fall either side of a rounding step.
        private const int FullCompareLimit = 3000;

        /// <summary>One straight line. Built in the Revit API context.</summary>
        public sealed class Segment
        {
            /// <summary>Index of the line in the caller's list.</summary>
            public int Index { get; init; }

            /// <summary>Element id value (tie-breaker).</summary>
            public long IdValue { get; init; }

            /// <summary>Owner view id value.</summary>
            public long ViewKey { get; init; }

            /// <summary>Line style subcategory id value.</summary>
            public long StyleKey { get; init; }

            /// <summary>Exact-duplicate cluster (0 for none); lines of one cluster are not compared.</summary>
            public int DuplicateCluster { get; init; }

            /// <summary>Start point.</summary>
            public XYZ Start { get; init; }

            /// <summary>End point.</summary>
            public XYZ End { get; init; }
        }

        /// <summary>Result of <see cref="Find"/>.</summary>
        public sealed class Result
        {
            /// <summary>Index → overlap group number (1-based); lines that overlap nothing are left out.</summary>
            public Dictionary<int, int> Groups { get; } = new();

            /// <summary>Index → index of the longest line that fully covers it.</summary>
            public Dictionary<int, int> CoveredBy { get; } = new();

            /// <summary>Views where the direction shortcut was used (see class remarks).</summary>
            public int ApproximatedBuckets { get; set; }
        }

        /// <summary>Compares the segments within each (view, style) set.</summary>
        public static Result Find(IEnumerable<Segment> segments, double toleranceFeet)
        {
            var result = new Result();
            var list = segments.Where(s => s != null && s.Start.DistanceTo(s.End) > toleranceFeet).ToList();
            var parent = list.ToDictionary(s => s.Index, s => s.Index);
            var length = list.ToDictionary(s => s.Index, s => s.Start.DistanceTo(s.End));

            int FindRoot(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }

            foreach (var bucket in list.GroupBy(s => (s.ViewKey, s.StyleKey)))
            {
                var items = bucket.ToList();
                IEnumerable<List<Segment>> groups = new[] { items };
                if (items.Count > FullCompareLimit)
                {
                    groups = items.GroupBy(DirectionKey).Select(g => g.ToList());
                    result.ApproximatedBuckets++;
                }

                foreach (var g in groups)
                {
                    for (int i = 0; i < g.Count; i++)
                    {
                        for (int j = i + 1; j < g.Count; j++)
                        {
                            var a = g[i];
                            var b = g[j];
                            if (a.DuplicateCluster > 0 && a.DuplicateCluster == b.DuplicateCluster) continue;
                            if (!Compare(a, b, toleranceFeet, out bool aCoveredByB, out bool bCoveredByA)) continue;

                            int ra = FindRoot(a.Index), rb = FindRoot(b.Index);
                            if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);

                            if (aCoveredByB) SetCoverer(result, a.Index, b.Index, length);
                            if (bCoveredByA) SetCoverer(result, b.Index, a.Index, length);
                        }
                    }
                }
            }

            int number = 0;
            var numbers = new Dictionary<int, int>();
            foreach (var group in list.GroupBy(s => FindRoot(s.Index)).Where(g => g.Count() > 1).OrderBy(g => g.Key))
            {
                numbers[group.Key] = ++number;
                foreach (var s in group) result.Groups[s.Index] = number;
            }

            // A line counted as covered must be in a group.
            foreach (var key in result.CoveredBy.Keys.Where(k => !result.Groups.ContainsKey(k)).ToList())
                result.CoveredBy.Remove(key);
            return result;
        }

        /// <summary>
        /// True when <paramref name="a"/> and <paramref name="b"/> are collinear within
        /// <paramref name="tol"/> and share more than <paramref name="tol"/> of length.
        /// </summary>
        public static bool Compare(Segment a, Segment b, double tol, out bool aCoveredByB, out bool bCoveredByA)
        {
            aCoveredByB = bCoveredByA = false;
            if (DistanceToLine(b.Start, a) > tol || DistanceToLine(b.End, a) > tol
                || DistanceToLine(a.Start, b) > tol || DistanceToLine(a.End, b) > tol)
                return false;

            XYZ axis = a.End - a.Start;
            double lenA = axis.GetLength();
            if (lenA < 1e-9) return false;
            axis /= lenA;

            double t0 = (b.Start - a.Start).DotProduct(axis);
            double t1 = (b.End - a.Start).DotProduct(axis);
            if (t0 > t1) (t0, t1) = (t1, t0);
            double lenB = t1 - t0;

            double shared = Math.Min(lenA, t1) - Math.Max(0, t0);
            if (shared <= tol) return false;

            // Strictly longer coverer, so two lines can never cover each other.
            aCoveredByB = t0 <= tol && t1 >= lenA - tol && lenB > lenA + 1e-9;
            bCoveredByA = t0 >= -tol && t1 <= lenA + tol && lenA > lenB + 1e-9;
            return true;
        }

        private static void SetCoverer(Result result, int covered, int coverer, Dictionary<int, double> length)
        {
            if (!result.CoveredBy.TryGetValue(covered, out int current) || length[coverer] > length[current])
                result.CoveredBy[covered] = coverer;
        }

        private static double DistanceToLine(XYZ p, Segment s)
        {
            XYZ d = s.End - s.Start;
            double len = d.GetLength();
            if (len < 1e-9) return p.DistanceTo(s.Start);
            return (p - s.Start).CrossProduct(d / len).GetLength();
        }

        // Direction rounded to about 1°, sign-normalised so opposite directions match.
        private static (long, long, long) DirectionKey(Segment s)
        {
            XYZ d = (s.End - s.Start).Normalize();
            if (d.X < -1e-9 || (Math.Abs(d.X) <= 1e-9 && (d.Y < -1e-9 || (Math.Abs(d.Y) <= 1e-9 && d.Z < 0)))) d = d.Negate();
            return ((long)Math.Round(d.X * 60), (long)Math.Round(d.Y * 60), (long)Math.Round(d.Z * 60));
        }
    }
}
