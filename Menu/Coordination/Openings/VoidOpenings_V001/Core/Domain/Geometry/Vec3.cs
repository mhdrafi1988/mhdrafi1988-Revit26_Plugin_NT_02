using System;
using System.Globalization;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry
{
    /// <summary>
    /// Point or direction in 3D. The domain layer works in millimetres, in the host model's coordinates;
    /// the Revit extractors convert units and apply link transforms before anything reaches this type.
    /// </summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        /// <summary>X component.</summary>
        public double X { get; }

        /// <summary>Y component.</summary>
        public double Y { get; }

        /// <summary>Z component.</summary>
        public double Z { get; }

        /// <summary>Creates a vector from its components.</summary>
        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>The zero vector.</summary>
        public static Vec3 Zero => new Vec3(0, 0, 0);

        /// <summary>Unit vector along X.</summary>
        public static Vec3 UnitX => new Vec3(1, 0, 0);

        /// <summary>Unit vector along Y.</summary>
        public static Vec3 UnitY => new Vec3(0, 1, 0);

        /// <summary>Unit vector along Z.</summary>
        public static Vec3 UnitZ => new Vec3(0, 0, 1);

        /// <summary>Length of the vector.</summary>
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        /// <summary>Component-wise sum.</summary>
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        /// <summary>Component-wise difference.</summary>
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        /// <summary>Negation.</summary>
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);

        /// <summary>Scale by a number.</summary>
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        /// <summary>Scale by a number.</summary>
        public static Vec3 operator *(double s, Vec3 a) => a * s;

        /// <summary>Dot product.</summary>
        public double Dot(Vec3 other) => X * other.X + Y * other.Y + Z * other.Z;

        /// <summary>Cross product (right-handed).</summary>
        public Vec3 Cross(Vec3 other) => new Vec3(
            Y * other.Z - Z * other.Y,
            Z * other.X - X * other.Z,
            X * other.Y - Y * other.X);

        /// <summary>True when the vector is too short to give a direction.</summary>
        public bool IsNearZero(double tolerance = 1e-9) => Length <= tolerance;

        /// <summary>The same direction with length 1, or <see cref="Zero"/> when the vector has no direction.</summary>
        public Vec3 Normalized()
        {
            double length = Length;
            return length <= 1e-12 ? Zero : new Vec3(X / length, Y / length, Z / length);
        }

        /// <summary>This vector with its part along <paramref name="unitAxis"/> removed.</summary>
        public Vec3 RejectFrom(Vec3 unitAxis) => this - unitAxis * Dot(unitAxis);

        /// <summary>
        /// A unit vector perpendicular to this one. Prefers the horizontal one, so an opening on a
        /// vertical or sloped axis gets a predictable rotation.
        /// </summary>
        public Vec3 AnyPerpendicular()
        {
            Vec3 unit = Normalized();
            Vec3 candidate = UnitZ.Cross(unit);
            if (candidate.IsNearZero(1e-6))
                candidate = UnitX.RejectFrom(unit);
            return candidate.Normalized();
        }

        /// <inheritdoc />
        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        /// <inheritdoc />
        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", X, Y, Z);
    }
}
