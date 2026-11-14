// =======================================================
// File: Point3.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models
// New in V032: Revit-free point (internal units, feet) so Core/ stays
// free of the Revit API (ARCHITECTURE.md §3). Infrastructure converts
// XYZ <-> Point3 via RoofOps.RevitConvert.
// =======================================================

using System;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models
{
    /// <summary>3D point in Revit internal units (decimal feet).</summary>
    public readonly struct Point3 : IEquatable<Point3>
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public Point3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>Plan (XY) distance. Drain matching and clustering use this,
        /// because picks are taken before the run resets point elevations (V032 D3).</summary>
        public double DistanceXY(Point3 other)
        {
            double dx = X - other.X, dy = Y - other.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public double DistanceTo(Point3 other)
        {
            double dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public bool Equals(Point3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Point3 p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
    }
}
