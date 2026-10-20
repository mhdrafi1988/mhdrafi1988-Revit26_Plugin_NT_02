// =======================================================
// File: Segments2D.cs
// Location: Core/Geometry/
// Plain 2D plan geometry (no Revit API), ported from Inner Loop Divider
// V012's LoopSegments.cs. Added here:
//   • PolylineSegment2D — one ellipse/spline edge kept as ONE segment
//     (V012 split it into many line pieces, which made every piece an
//     "edge" for the midpoint/quarter and gap-fill steps).
//   • Project(p) on every segment — closest point and its length
//     fraction, used by the perpendicular and gap-fill steps.
// Units are whatever the caller uses (the Revit layer passes feet).
// =======================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry
{
    /// <summary>2D point / vector in plan.</summary>
    public readonly struct P2
    {
        /// <summary>Creates a plan point.</summary>
        public P2(double x, double y) { X = x; Y = y; }

        /// <summary>X coordinate.</summary>
        public double X { get; }

        /// <summary>Y coordinate.</summary>
        public double Y { get; }

        public static P2 operator +(P2 a, P2 b) => new P2(a.X + b.X, a.Y + b.Y);
        public static P2 operator -(P2 a, P2 b) => new P2(a.X - b.X, a.Y - b.Y);
        public static P2 operator *(P2 a, double k) => new P2(a.X * k, a.Y * k);

        /// <summary>Vector length.</summary>
        public double Length => Math.Sqrt(X * X + Y * Y);

        /// <summary>Dot product.</summary>
        public double Dot(P2 o) => X * o.X + Y * o.Y;

        /// <summary>2D cross product (z of the 3D cross product).</summary>
        public double Cross(P2 o) => X * o.Y - Y * o.X;

        /// <summary>Distance to another point.</summary>
        public double DistanceTo(P2 o) => (this - o).Length;

        /// <summary>Unit vector, or (0,0) for a zero vector.</summary>
        public P2 Normalized()
        {
            double l = Length;
            return l < 1e-12 ? new P2(0, 0) : new P2(X / l, Y / l);
        }

        /// <summary>This vector turned 90° counter-clockwise.</summary>
        public P2 Left => new P2(-Y, X);

        /// <inheritdoc/>
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }

    /// <summary>One curve of a loop, parameterised by length fraction t ∈ [0, 1].</summary>
    public abstract class LoopSegment2D
    {
        /// <summary>Length of the segment.</summary>
        public abstract double Length { get; }

        /// <summary>Point at length fraction t (0 = start, 1 = end).</summary>
        public abstract P2 PointAt(double t);

        /// <summary>Unit tangent at length fraction t, in the segment's direction.</summary>
        public abstract P2 TangentAt(double t);

        /// <summary>The same segment, run the other way.</summary>
        public abstract LoopSegment2D Reversed();

        /// <summary>True for straight lines.</summary>
        public abstract bool IsStraight { get; }

        /// <summary>Closest point on the segment to <paramref name="p"/>: its length fraction and distance.</summary>
        public abstract (double T, double Distance) Project(P2 p);

        /// <summary>Points for a polyline approximation, start included, end excluded.</summary>
        public abstract IEnumerable<P2> SamplePoints();

        /// <summary>Start point.</summary>
        public P2 Start => PointAt(0);

        /// <summary>End point.</summary>
        public P2 End => PointAt(1);
    }

    /// <summary>Straight segment A → B.</summary>
    public sealed class LineSegment2D : LoopSegment2D
    {
        /// <summary>Creates the segment A → B.</summary>
        public LineSegment2D(P2 a, P2 b) { A = a; B = b; }

        /// <summary>Start point.</summary>
        public P2 A { get; }

        /// <summary>End point.</summary>
        public P2 B { get; }

        /// <inheritdoc/>
        public override double Length => A.DistanceTo(B);

        /// <inheritdoc/>
        public override P2 PointAt(double t) => A + (B - A) * t;

        /// <inheritdoc/>
        public override P2 TangentAt(double t) => (B - A).Normalized();

        /// <inheritdoc/>
        public override LoopSegment2D Reversed() => new LineSegment2D(B, A);

        /// <inheritdoc/>
        public override bool IsStraight => true;

        /// <inheritdoc/>
        public override (double T, double Distance) Project(P2 p)
        {
            P2 d = B - A;
            double len2 = d.Dot(d);
            double t = len2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, (p - A).Dot(d) / len2));
            return (t, PointAt(t).DistanceTo(p));
        }

        /// <inheritdoc/>
        public override IEnumerable<P2> SamplePoints() { yield return A; }
    }

    /// <summary>Circular arc: centre, radius, start angle and signed sweep (radians, + = counter-clockwise).</summary>
    public sealed class ArcSegment2D : LoopSegment2D
    {
        /// <summary>Creates the arc.</summary>
        public ArcSegment2D(P2 center, double radius, double startAngle, double sweep)
        {
            Center = center;
            Radius = radius;
            StartAngle = startAngle;
            Sweep = sweep;
        }

        /// <summary>Arc centre.</summary>
        public P2 Center { get; }

        /// <summary>Arc radius.</summary>
        public double Radius { get; }

        /// <summary>Angle of the start point (radians).</summary>
        public double StartAngle { get; }

        /// <summary>Signed sweep (radians, + = counter-clockwise).</summary>
        public double Sweep { get; }

        /// <inheritdoc/>
        public override double Length => Radius * Math.Abs(Sweep);

        /// <inheritdoc/>
        public override P2 PointAt(double t)
        {
            double a = StartAngle + Sweep * t;
            return new P2(Center.X + Radius * Math.Cos(a), Center.Y + Radius * Math.Sin(a));
        }

        /// <inheritdoc/>
        public override P2 TangentAt(double t)
        {
            double a = StartAngle + Sweep * t;
            double s = Sweep >= 0 ? 1 : -1;
            return new P2(-Math.Sin(a) * s, Math.Cos(a) * s);
        }

        /// <inheritdoc/>
        public override LoopSegment2D Reversed() => new ArcSegment2D(Center, Radius, StartAngle + Sweep, -Sweep);

        /// <inheritdoc/>
        public override bool IsStraight => false;

        /// <inheritdoc/>
        public override (double T, double Distance) Project(P2 p)
        {
            if (Math.Abs(Sweep) < 1e-12) return (0, Start.DistanceTo(p));

            // Angle of p measured from the start, in the sweep's direction.
            double a = Math.Atan2(p.Y - Center.Y, p.X - Center.X);
            double rel = Norm((a - StartAngle) * Math.Sign(Sweep));
            double t = rel / Math.Abs(Sweep);
            if (t > 1)
            {
                // Outside the sweep: the nearer end wins.
                double dStart = Start.DistanceTo(p), dEnd = End.DistanceTo(p);
                return dStart <= dEnd ? (0, dStart) : (1, dEnd);
            }

            return (t, PointAt(t).DistanceTo(p));
        }

        /// <inheritdoc/>
        public override IEnumerable<P2> SamplePoints()
        {
            int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(Sweep) / (Math.PI / 36)));
            for (int k = 0; k < n; k++)
                yield return PointAt((double)k / n);
        }

        /// <summary>Arc through start → mid → end (mid fixes the direction). Returns null for a zero radius.</summary>
        public static ArcSegment2D FromThreePoints(P2 center, double radius, P2 start, P2 mid, P2 end)
        {
            if (radius <= 1e-12) return null;
            double a0 = Math.Atan2(start.Y - center.Y, start.X - center.X);
            double am = Math.Atan2(mid.Y - center.Y, mid.X - center.X);
            double a1 = Math.Atan2(end.Y - center.Y, end.X - center.X);
            double ccwToEnd = Norm(a1 - a0);
            double ccwToMid = Norm(am - a0);
            if (ccwToEnd < 1e-9) ccwToEnd = 2 * Math.PI; // closed arc
            double sweep = ccwToMid <= ccwToEnd ? ccwToEnd : -(2 * Math.PI - ccwToEnd);
            return new ArcSegment2D(center, radius, a0, sweep);
        }

        private static double Norm(double a)
        {
            double two = 2 * Math.PI;
            a %= two;
            return a < 0 ? a + two : a;
        }
    }

    /// <summary>
    /// A curved edge that is not a circular arc (ellipse, spline …), kept as one segment
    /// through its tessellation points and parameterised by length.
    /// </summary>
    public sealed class PolylineSegment2D : LoopSegment2D
    {
        private readonly P2[] _points;
        private readonly double[] _cumulative;

        /// <summary>Creates the segment through <paramref name="points"/> (at least two).</summary>
        public PolylineSegment2D(IEnumerable<P2> points)
        {
            _points = points?.ToArray() ?? Array.Empty<P2>();
            if (_points.Length < 2)
                throw new ArgumentException("A polyline needs at least two points.", nameof(points));

            _cumulative = new double[_points.Length];
            for (int i = 1; i < _points.Length; i++)
                _cumulative[i] = _cumulative[i - 1] + _points[i].DistanceTo(_points[i - 1]);
        }

        /// <inheritdoc/>
        public override double Length => _cumulative[_cumulative.Length - 1];

        /// <inheritdoc/>
        public override P2 PointAt(double t)
        {
            (int i, double f) = Locate(t);
            return _points[i] + (_points[i + 1] - _points[i]) * f;
        }

        /// <inheritdoc/>
        public override P2 TangentAt(double t)
        {
            (int i, _) = Locate(t);
            return (_points[i + 1] - _points[i]).Normalized();
        }

        /// <inheritdoc/>
        public override LoopSegment2D Reversed() => new PolylineSegment2D(_points.Reverse());

        /// <inheritdoc/>
        public override bool IsStraight => false;

        /// <inheritdoc/>
        public override (double T, double Distance) Project(P2 p)
        {
            double bestS = 0, bestD = double.MaxValue;
            for (int i = 0; i + 1 < _points.Length; i++)
            {
                (double f, double d) = new LineSegment2D(_points[i], _points[i + 1]).Project(p);
                if (d < bestD)
                {
                    bestD = d;
                    bestS = _cumulative[i] + f * (_cumulative[i + 1] - _cumulative[i]);
                }
            }

            return (Length > 1e-12 ? bestS / Length : 0, bestD);
        }

        /// <inheritdoc/>
        public override IEnumerable<P2> SamplePoints() => _points.Take(_points.Length - 1);

        private (int Index, double Fraction) Locate(double t)
        {
            double s = Math.Max(0, Math.Min(1, t)) * Length;
            for (int i = 0; i + 1 < _points.Length; i++)
            {
                double span = _cumulative[i + 1] - _cumulative[i];
                if (s <= _cumulative[i + 1] || i + 2 == _points.Length)
                    return (i, span > 1e-12 ? Math.Max(0, Math.Min(1, (s - _cumulative[i]) / span)) : 0);
            }

            return (0, 0);
        }
    }
}
