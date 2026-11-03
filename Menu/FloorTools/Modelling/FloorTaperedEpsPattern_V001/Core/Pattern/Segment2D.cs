using System;
using System.Globalization;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Immutable straight segment in plan, metres.</summary>
    public readonly struct Segment2D
    {
        public Segment2D(Vec2 start, Vec2 end)
        {
            Start = start;
            End = end;
        }

        public Vec2 Start { get; }
        public Vec2 End { get; }

        public double Length => Start.DistanceTo(End);
        public Vec2 Midpoint => (Start + End) * 0.5;
        public Vec2 Direction => (End - Start).Normalized();
        public Segment2D Reversed => new Segment2D(End, Start);

        /// <summary>True when both end points match in either order.</summary>
        public bool SameAs(Segment2D other, double tolerance) =>
            (Start.AlmostEquals(other.Start, tolerance) && End.AlmostEquals(other.End, tolerance)) ||
            (Start.AlmostEquals(other.End, tolerance) && End.AlmostEquals(other.Start, tolerance));

        /// <summary>Distance from a point to this segment (not the infinite line).</summary>
        public double DistanceTo(Vec2 point)
        {
            Vec2 d = End - Start;
            double lenSq = d.Dot(d);
            if (lenSq < 1e-30)
            {
                return point.DistanceTo(Start);
            }

            double t = Math.Max(0.0, Math.Min(1.0, (point - Start).Dot(d) / lenSq));
            return point.DistanceTo(Start + d * t);
        }

        /// <summary>Distance from a point to the infinite line through this segment.</summary>
        public double DistanceToLine(Vec2 point)
        {
            double len = Length;
            if (len < 1e-15)
            {
                return point.DistanceTo(Start);
            }

            return Math.Abs((End - Start).Cross(point - Start)) / len;
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0} → {1} ({2:0.###} m)", Start, End, Length);
    }
}
