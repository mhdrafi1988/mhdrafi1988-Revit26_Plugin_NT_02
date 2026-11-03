// =======================================================
// File: Loop2D.cs
// Location: Core/Geometry/
// New in V012. A closed chain of plan segments with the measures the
// tool needs: perimeter, signed area, centroid, sharp corners, and
// arc-length lookup. No Revit API.
// =======================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Geometry
{
    public sealed class Loop2D
    {
        /// <summary>A joint whose direction turns more than this is a corner (smaller turns = smooth, e.g. a 24-gon "circle").</summary>
        public const double CornerAngleDeg = 20.0;

        private readonly double[] _startS; // arc length at the start of each segment
        private List<P2> _samples;

        public Loop2D(IReadOnlyList<LoopSegment2D> orderedSegments)
        {
            if (orderedSegments == null || orderedSegments.Count == 0)
                throw new ArgumentException("A loop needs at least one segment.", nameof(orderedSegments));

            Segments = orderedSegments;
            _startS = new double[orderedSegments.Count];
            double s = 0;
            for (int i = 0; i < orderedSegments.Count; i++)
            {
                _startS[i] = s;
                s += orderedSegments[i].Length;
            }

            Perimeter = s;
            CornerSegmentIndices = FindCorners();
        }

        public IReadOnlyList<LoopSegment2D> Segments { get; }
        public double Perimeter { get; }

        /// <summary>Indices i where the joint at the START of segment i is a sharp corner.</summary>
        public IReadOnlyList<int> CornerSegmentIndices { get; }

        public double CornerArcLength(int segmentIndex) => _startS[segmentIndex];

        /// <summary>Polyline approximation (arcs every 5°), closed implicitly.</summary>
        public IReadOnlyList<P2> Samples => _samples ??= BuildSamples();

        public double SignedArea
        {
            get
            {
                var p = Samples;
                double a = 0;
                for (int i = 0; i < p.Count; i++)
                    a += p[i].Cross(p[(i + 1) % p.Count]);
                return a / 2;
            }
        }

        public double Area => Math.Abs(SignedArea);

        public P2 Centroid
        {
            get
            {
                var p = Samples;
                double a = 0, cx = 0, cy = 0;
                for (int i = 0; i < p.Count; i++)
                {
                    P2 u = p[i], v = p[(i + 1) % p.Count];
                    double c = u.Cross(v);
                    a += c;
                    cx += (u.X + v.X) * c;
                    cy += (u.Y + v.Y) * c;
                }

                if (Math.Abs(a) < 1e-12)
                    return new P2(p.Average(q => q.X), p.Average(q => q.Y));

                return new P2(cx / (3 * a), cy / (3 * a));
            }
        }

        public (P2 Min, P2 Max) Bounds
        {
            get
            {
                var p = Samples;
                return (new P2(p.Min(q => q.X), p.Min(q => q.Y)), new P2(p.Max(q => q.X), p.Max(q => q.Y)));
            }
        }

        /// <summary>Segment index and length fraction at arc length s (wrapped into [0, perimeter)).</summary>
        public (int Segment, double Fraction) Locate(double s)
        {
            if (Perimeter <= 0) return (0, 0);
            s %= Perimeter;
            if (s < 0) s += Perimeter;

            for (int i = Segments.Count - 1; i >= 0; i--)
            {
                if (s >= _startS[i] - 1e-12)
                {
                    double len = Segments[i].Length;
                    double f = len > 1e-12 ? (s - _startS[i]) / len : 0;
                    return (i, Math.Min(1, Math.Max(0, f)));
                }
            }

            return (0, 0);
        }

        public P2 PointAtArcLength(double s)
        {
            (int seg, double f) = Locate(s);
            return Segments[seg].PointAt(f);
        }

        /// <summary>Arc length of the east-most point (max X, then min Y) — a start that doesn't depend on sketch order.</summary>
        public double EastMostArcLength()
        {
            const int stepsPerSegment = 72;
            double bestS = 0;
            P2 best = Segments[0].Start;
            for (int i = 0; i < Segments.Count; i++)
            {
                for (int k = 0; k <= stepsPerSegment; k++)
                {
                    double f = (double)k / stepsPerSegment;
                    P2 p = Segments[i].PointAt(f);
                    if (p.X > best.X + 1e-9 || (Math.Abs(p.X - best.X) <= 1e-9 && p.Y < best.Y))
                    {
                        best = p;
                        bestS = _startS[i] + f * Segments[i].Length;
                    }
                }
            }

            return bestS;
        }

        /// <summary>Turn angle (degrees, 0–180) at the joint before segment i.</summary>
        public double TurnAngleDeg(int i)
        {
            int prev = (i - 1 + Segments.Count) % Segments.Count;
            P2 a = Segments[prev].TangentAt(1);
            P2 b = Segments[i].TangentAt(0);
            double cos = Math.Max(-1, Math.Min(1, a.Dot(b)));
            return Math.Acos(cos) * 180 / Math.PI;
        }

        private IReadOnlyList<int> FindCorners()
        {
            var corners = new List<int>();
            if (Segments.Count < 2) return corners;
            for (int i = 0; i < Segments.Count; i++)
            {
                if (TurnAngleDeg(i) > CornerAngleDeg)
                    corners.Add(i);
            }

            return corners;
        }

        private List<P2> BuildSamples()
        {
            var pts = new List<P2>();
            foreach (LoopSegment2D seg in Segments)
            {
                int n = seg is ArcSegment2D arc
                    ? Math.Max(2, (int)Math.Ceiling(Math.Abs(arc.Sweep) / (Math.PI / 36)))
                    : 1;
                for (int k = 0; k < n; k++)
                    pts.Add(seg.PointAt((double)k / n));
            }

            return pts;
        }

        /// <summary>Point-in-polygon on the sampled outline (even-odd).</summary>
        public bool Contains(P2 q)
        {
            var p = Samples;
            bool inside = false;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            {
                if ((p[i].Y > q.Y) != (p[j].Y > q.Y) &&
                    q.X < (p[j].X - p[i].X) * (q.Y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X)
                    inside = !inside;
            }

            return inside;
        }
    }

    /// <summary>Orders loose segments head-to-tail (reversing where needed) into one closed loop.</summary>
    public static class LoopChainer
    {
        /// <returns>The closed loop, or null when the segments don't form exactly one closed chain.</returns>
        public static Loop2D Chain(IEnumerable<LoopSegment2D> segments, double tolerance)
        {
            var pool = segments.Where(s => s != null && s.Length > tolerance * 0.01).ToList();
            if (pool.Count == 0) return null;

            var ordered = new List<LoopSegment2D> { pool[0] };
            pool.RemoveAt(0);

            while (pool.Count > 0)
            {
                P2 end = ordered[ordered.Count - 1].End;
                int idx = pool.FindIndex(s => s.Start.DistanceTo(end) <= tolerance);
                if (idx >= 0)
                {
                    ordered.Add(pool[idx]);
                }
                else
                {
                    idx = pool.FindIndex(s => s.End.DistanceTo(end) <= tolerance);
                    if (idx < 0) return null;
                    ordered.Add(pool[idx].Reversed());
                }

                pool.RemoveAt(idx);
            }

            if (ordered[ordered.Count - 1].End.DistanceTo(ordered[0].Start) > tolerance)
                return null;

            return new Loop2D(ordered);
        }
    }
}
