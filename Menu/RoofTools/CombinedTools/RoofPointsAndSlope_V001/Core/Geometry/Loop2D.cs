// =======================================================
// File: Loop2D.cs
// Location: Core/Geometry/
// Ported from Inner Loop Divider V012 (Loop2D, LoopChainer,
// LoopClassifier). Only change: the outline samples come from each
// segment's SamplePoints(), so polyline (ellipse/spline) segments are
// sampled through all their points.
// =======================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry
{
    /// <summary>
    /// A closed chain of plan segments with the measures the tool needs: perimeter, signed
    /// area, centroid, sharp corners, and arc-length lookup. No Revit API.
    /// </summary>
    public sealed class Loop2D
    {
        /// <summary>A joint whose direction turns more than this is a corner (smaller turns = smooth, e.g. a 24-gon "circle").</summary>
        public const double CornerAngleDeg = 20.0;

        private readonly double[] _startS; // arc length at the start of each segment
        private List<P2> _samples;

        /// <summary>Builds the loop from segments already ordered head to tail.</summary>
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

        /// <summary>The loop's segments, head to tail.</summary>
        public IReadOnlyList<LoopSegment2D> Segments { get; }

        /// <summary>Total length.</summary>
        public double Perimeter { get; }

        /// <summary>Indices i where the joint at the START of segment i is a sharp corner.</summary>
        public IReadOnlyList<int> CornerSegmentIndices { get; }

        /// <summary>Arc length at the start of segment <paramref name="segmentIndex"/>.</summary>
        public double CornerArcLength(int segmentIndex) => _startS[segmentIndex];

        /// <summary>Polyline approximation (arcs every 5°), closed implicitly.</summary>
        public IReadOnlyList<P2> Samples => _samples ??= Segments.SelectMany(s => s.SamplePoints()).ToList();

        /// <summary>Signed area (+ = counter-clockwise).</summary>
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

        /// <summary>Area.</summary>
        public double Area => Math.Abs(SignedArea);

        /// <summary>Area centroid.</summary>
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

        /// <summary>Plan bounding box.</summary>
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

        /// <summary>Distance from <paramref name="q"/> to the nearest point of the loop.</summary>
        public double DistanceTo(P2 q) => Segments.Min(s => s.Project(q).Distance);

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

    /// <summary>Opening shape class.</summary>
    public enum LoopShape { Circular, Rectangle, Other }

    /// <summary>Shape and size of one loop.</summary>
    public sealed class LoopShapeInfo
    {
        /// <summary>Shape class.</summary>
        public LoopShape Shape { get; init; }

        /// <summary>Circular: diameter. Rectangle: shorter side. Other: bounding-box width.</summary>
        public double SizeA { get; init; }

        /// <summary>Rectangle: longer side. Other: bounding-box depth. Circular: same as SizeA.</summary>
        public double SizeB { get; init; }

        /// <summary>"Circular", "Rectangle" or "Other".</summary>
        public string Name => Shape.ToString();
    }

    /// <summary>
    /// Classifies a loop from its plan geometry:
    /// Circular — no sharp corners and every point the same distance from the centroid (±1 %);
    /// Rectangle — exactly 4 corners of ~90° and straight sides; Other — anything else.
    /// </summary>
    public static class LoopClassifier
    {
        /// <summary>Allowed radius spread for a circle (fraction of the mean radius).</summary>
        public const double RadiusTolerance = 0.01;

        /// <summary>Allowed deviation from 90° at a rectangle corner.</summary>
        public const double RightAngleToleranceDeg = 5.0;

        /// <summary>Classifies <paramref name="loop"/>.</summary>
        public static LoopShapeInfo Classify(Loop2D loop)
        {
            if (loop == null) throw new ArgumentNullException(nameof(loop));

            if (loop.CornerSegmentIndices.Count == 0)
            {
                P2 c = loop.Centroid;
                var d = loop.Samples.Select(p => p.DistanceTo(c)).ToList();
                double mean = d.Average();
                if (mean > 1e-9 && d.All(r => Math.Abs(r - mean) / mean <= RadiusTolerance))
                    return new LoopShapeInfo { Shape = LoopShape.Circular, SizeA = 2 * mean, SizeB = 2 * mean };
            }

            if (loop.CornerSegmentIndices.Count == 4 &&
                loop.Segments.All(s => s.IsStraight) &&
                loop.CornerSegmentIndices.All(i => Math.Abs(loop.TurnAngleDeg(i) - 90) <= RightAngleToleranceDeg))
            {
                double[] sides = SideLengths(loop);
                double a = Math.Min(sides[0], sides[1]);
                double b = Math.Max(sides[0], sides[1]);
                return new LoopShapeInfo { Shape = LoopShape.Rectangle, SizeA = a, SizeB = b };
            }

            (P2 min, P2 max) = loop.Bounds;
            return new LoopShapeInfo { Shape = LoopShape.Other, SizeA = max.X - min.X, SizeB = max.Y - min.Y };
        }

        /// <summary>Lengths between consecutive corners (one entry per side).</summary>
        public static double[] SideLengths(Loop2D loop)
        {
            var corners = loop.CornerSegmentIndices;
            if (corners.Count == 0) return new[] { loop.Perimeter };

            var sides = new double[corners.Count];
            for (int k = 0; k < corners.Count; k++)
            {
                double s0 = loop.CornerArcLength(corners[k]);
                double s1 = k + 1 < corners.Count ? loop.CornerArcLength(corners[k + 1]) : loop.Perimeter + loop.CornerArcLength(corners[0]);
                sides[k] = s1 - s0;
            }

            return sides;
        }
    }
}
