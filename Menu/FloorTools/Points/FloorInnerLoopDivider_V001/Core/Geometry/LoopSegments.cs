// =======================================================
// File: LoopSegments.cs
// Location: Core/Geometry/
// New in V012. Plain 2D plan geometry (no Revit API) so loop
// classification and point placement can be unit-tested. Units are
// whatever the caller uses (the Revit layer passes internal feet).
// =======================================================

using System;

namespace Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Geometry
{
    /// <summary>2D point / vector in plan.</summary>
    public readonly struct P2
    {
        public P2(double x, double y) { X = x; Y = y; }

        public double X { get; }
        public double Y { get; }

        public static P2 operator +(P2 a, P2 b) => new P2(a.X + b.X, a.Y + b.Y);
        public static P2 operator -(P2 a, P2 b) => new P2(a.X - b.X, a.Y - b.Y);
        public static P2 operator *(P2 a, double k) => new P2(a.X * k, a.Y * k);

        public double Length => Math.Sqrt(X * X + Y * Y);
        public double Dot(P2 o) => X * o.X + Y * o.Y;
        public double Cross(P2 o) => X * o.Y - Y * o.X;
        public double DistanceTo(P2 o) => (this - o).Length;

        public P2 Normalized()
        {
            double l = Length;
            return l < 1e-12 ? new P2(0, 0) : new P2(X / l, Y / l);
        }

        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }

    /// <summary>One curve of a closed loop, parameterised by length fraction t ∈ [0, 1].</summary>
    public abstract class LoopSegment2D
    {
        public abstract double Length { get; }

        /// <summary>Point at length fraction t (0 = start, 1 = end).</summary>
        public abstract P2 PointAt(double t);

        /// <summary>Unit tangent at length fraction t, in the segment's direction.</summary>
        public abstract P2 TangentAt(double t);

        public abstract LoopSegment2D Reversed();

        public abstract bool IsStraight { get; }

        public P2 Start => PointAt(0);
        public P2 End => PointAt(1);
    }

    public sealed class LineSegment2D : LoopSegment2D
    {
        public LineSegment2D(P2 a, P2 b) { A = a; B = b; }

        public P2 A { get; }
        public P2 B { get; }

        public override double Length => A.DistanceTo(B);
        public override P2 PointAt(double t) => A + (B - A) * t;
        public override P2 TangentAt(double t) => (B - A).Normalized();
        public override LoopSegment2D Reversed() => new LineSegment2D(B, A);
        public override bool IsStraight => true;
    }

    /// <summary>Circular arc: centre, radius, start angle and signed sweep (radians, + = counter-clockwise).</summary>
    public sealed class ArcSegment2D : LoopSegment2D
    {
        public ArcSegment2D(P2 center, double radius, double startAngle, double sweep)
        {
            Center = center;
            Radius = radius;
            StartAngle = startAngle;
            Sweep = sweep;
        }

        public P2 Center { get; }
        public double Radius { get; }
        public double StartAngle { get; }
        public double Sweep { get; }

        public override double Length => Radius * Math.Abs(Sweep);

        public override P2 PointAt(double t)
        {
            double a = StartAngle + Sweep * t;
            return new P2(Center.X + Radius * Math.Cos(a), Center.Y + Radius * Math.Sin(a));
        }

        public override P2 TangentAt(double t)
        {
            double a = StartAngle + Sweep * t;
            double s = Sweep >= 0 ? 1 : -1;
            return new P2(-Math.Sin(a) * s, Math.Cos(a) * s);
        }

        public override LoopSegment2D Reversed() => new ArcSegment2D(Center, Radius, StartAngle + Sweep, -Sweep);
        public override bool IsStraight => false;

        /// <summary>Arc through start → mid → end (mid fixes the direction). Returns null for collinear points.</summary>
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
}
