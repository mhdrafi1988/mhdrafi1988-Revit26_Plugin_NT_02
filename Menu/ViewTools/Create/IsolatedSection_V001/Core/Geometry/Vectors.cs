using System;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.Core.Geometry
{
    /// <summary>A plan point or direction (project X/Y, feet). No Revit API dependency.</summary>
    public readonly struct Vec2
    {
        /// <summary>Creates a plan vector.</summary>
        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        /// <summary>Project X (east).</summary>
        public double X { get; }

        /// <summary>Project Y (north).</summary>
        public double Y { get; }

        /// <summary>Length of the vector.</summary>
        public double Length => Math.Sqrt(X * X + Y * Y);

        /// <summary>Unit vector in the same direction; (0, 0) stays (0, 0).</summary>
        public Vec2 Normalized()
        {
            double len = Length;
            return len < 1e-12 ? new Vec2(0, 0) : new Vec2(X / len, Y / len);
        }

        /// <summary>The vector turned 90° counter-clockwise.</summary>
        public Vec2 Perp() => new(-Y, X);

        /// <summary>Dot product.</summary>
        public double Dot(Vec2 other) => X * other.X + Y * other.Y;

        /// <summary>Z of the 3D cross product (positive when <paramref name="other"/> is counter-clockwise).</summary>
        public double Cross(Vec2 other) => X * other.Y - Y * other.X;

        /// <summary>Vector sum.</summary>
        public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

        /// <summary>Vector difference.</summary>
        public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

        /// <summary>Negation.</summary>
        public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);

        /// <summary>Scaling.</summary>
        public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);

        /// <inheritdoc/>
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }

    /// <summary>A model point (project X/Y/Z, feet). No Revit API dependency.</summary>
    public readonly struct Point3
    {
        /// <summary>Creates a model point.</summary>
        public Point3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>Project X.</summary>
        public double X { get; }

        /// <summary>Project Y.</summary>
        public double Y { get; }

        /// <summary>Project Z (elevation).</summary>
        public double Z { get; }

        /// <summary>The point's plan position.</summary>
        public Vec2 Plan => new(X, Y);
    }
}
