using System;
using System.Globalization;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Immutable 2D vector / point in plan, metres.</summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }

        public static Vec2 Zero => new Vec2(0, 0);
        public static Vec2 UnitX => new Vec2(1, 0);

        public double Length => Math.Sqrt(X * X + Y * Y);

        /// <summary>Vector rotated +90° (to the left of the direction of travel).</summary>
        public Vec2 PerpLeft => new Vec2(-Y, X);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, double s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(double s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, double s) => new Vec2(a.X / s, a.Y / s);

        public double Dot(Vec2 other) => X * other.X + Y * other.Y;

        /// <summary>Z component of the 3D cross product (this × other).</summary>
        public double Cross(Vec2 other) => X * other.Y - Y * other.X;

        public Vec2 Normalized()
        {
            double len = Length;
            return len < 1e-15 ? Zero : new Vec2(X / len, Y / len);
        }

        public double DistanceTo(Vec2 other) => (this - other).Length;

        public bool AlmostEquals(Vec2 other, double tolerance) => DistanceTo(other) <= tolerance;

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###})", X, Y);
    }
}
