using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.DetailLineManager.V002.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.DetailLineManager.V002.Core.Services
{
    /// <summary>
    /// Finds duplicate / overlapping detail lines: same owner view, same curve kind and the same
    /// geometry (length and sample points, in either direction) within a tolerance.
    /// </summary>
    public static class DuplicateDetector
    {
        /// <summary>Geometry fingerprint of one curve. Built in the Revit API context.</summary>
        public sealed class Candidate
        {
            /// <summary>Index of the line in the caller's list.</summary>
            public int Index { get; init; }

            /// <summary>Owner view id value.</summary>
            public long ViewKey { get; init; }

            /// <summary>Curve kind.</summary>
            public CurveKind Kind { get; init; }

            /// <summary>True for bound curves (arcs, lines); false for full circles and ellipses.</summary>
            public bool IsBound { get; init; }

            /// <summary>Curve length in internal units (feet).</summary>
            public double Length { get; init; }

            /// <summary>Sample points along the curve, start to end.</summary>
            public XYZ[] Samples { get; init; }
        }

        private static readonly double[] SampleParams = { 0.0, 0.25, 0.5, 0.75, 1.0 };

        /// <summary>Builds the fingerprint of <paramref name="curve"/>, or null when it cannot be read.</summary>
        public static Candidate Describe(int index, ElementId viewId, CurveKind kind, Curve curve)
        {
            if (curve == null) return null;
            try
            {
                XYZ[] samples;
                if (curve.IsBound)
                {
                    samples = SampleParams.Select(p => curve.Evaluate(p, true)).ToArray();
                }
                else
                {
                    // Full circle / ellipse: the start point is arbitrary, so compare the centre only.
                    XYZ center = curve switch
                    {
                        Arc a => a.Center,
                        Ellipse e => e.Center,
                        _ => Centroid(curve.Tessellate())
                    };
                    samples = new[] { center };
                }

                return new Candidate
                {
                    Index = index,
                    ViewKey = viewId?.Value ?? 0,
                    Kind = kind,
                    IsBound = curve.IsBound,
                    Length = curve.Length,
                    Samples = samples
                };
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Groups matching candidates. Returns index → cluster number (1-based); lines without a
        /// duplicate are left out.
        /// </summary>
        public static Dictionary<int, int> FindClusters(IEnumerable<Candidate> candidates, double toleranceFeet)
        {
            var list = candidates.Where(c => c != null).ToList();
            var parent = list.ToDictionary(c => c.Index, c => c.Index);

            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }

            foreach (var bucket in list.GroupBy(c => (c.ViewKey, c.Kind, c.IsBound)))
            {
                var sorted = bucket.OrderBy(c => c.Length).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    for (int j = i + 1; j < sorted.Count && sorted[j].Length - sorted[i].Length <= toleranceFeet; j++)
                    {
                        if (!Matches(sorted[i], sorted[j], toleranceFeet)) continue;
                        int a = Find(sorted[i].Index), b = Find(sorted[j].Index);
                        if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
                    }
                }
            }

            var result = new Dictionary<int, int>();
            var clusterNumbers = new Dictionary<int, int>();
            foreach (var group in list.GroupBy(c => Find(c.Index)).Where(g => g.Count() > 1).OrderBy(g => g.Key))
            {
                int number = clusterNumbers.Count + 1;
                clusterNumbers[group.Key] = number;
                foreach (var c in group) result[c.Index] = number;
            }
            return result;
        }

        /// <summary>
        /// The line of a cluster that survives a delete under <paramref name="rule"/>.
        /// </summary>
        public static DetailLineRow Keeper(IEnumerable<DetailLineRow> cluster, DuplicateKeepRule rule) => rule switch
        {
            DuplicateKeepRule.NewestId => cluster.OrderByDescending(r => r.IdValue).FirstOrDefault(),
            DuplicateKeepRule.HeaviestWeight => cluster.OrderByDescending(r => r.EffectiveWeight).ThenBy(r => r.IdValue).FirstOrDefault(),
            _ => cluster.OrderBy(r => r.IdValue).FirstOrDefault()
        };

        private static bool Matches(Candidate a, Candidate b, double tol)
        {
            if (Math.Abs(a.Length - b.Length) > tol || a.Samples.Length != b.Samples.Length) return false;

            bool forward = true, reverse = true;
            int n = a.Samples.Length;
            for (int i = 0; i < n && (forward || reverse); i++)
            {
                if (forward && a.Samples[i].DistanceTo(b.Samples[i]) > tol) forward = false;
                if (reverse && a.Samples[i].DistanceTo(b.Samples[n - 1 - i]) > tol) reverse = false;
            }
            return forward || reverse;
        }

        private static XYZ Centroid(IList<XYZ> points)
        {
            if (points == null || points.Count == 0) return XYZ.Zero;
            double x = 0, y = 0, z = 0;
            foreach (var p in points) { x += p.X; y += p.Y; z += p.Z; }
            return new XYZ(x / points.Count, y / points.Count, z / points.Count);
        }
    }
}
