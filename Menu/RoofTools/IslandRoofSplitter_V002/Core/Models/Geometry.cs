// IslandRoofSplitter V002 — plain geometry value types (no Revit API).
// All lengths are Revit internal units (decimal feet); convert to mm only for display.
#nullable enable
using System;

namespace Revit26_Plugin_Revised.IslandRoofSplitter.V002.Core.Models
{
    public readonly record struct Pt2(double X, double Y)
    {
        public static Pt2 operator +(Pt2 a, Pt2 b) => new(a.X + b.X, a.Y + b.Y);
        public static Pt2 operator -(Pt2 a, Pt2 b) => new(a.X - b.X, a.Y - b.Y);
        public static Pt2 operator *(Pt2 a, double k) => new(a.X * k, a.Y * k);

        public double Length => Math.Sqrt(X * X + Y * Y);
        public double DistanceTo(Pt2 o) => (this - o).Length;

        public static double Cross(Pt2 a, Pt2 b) => a.X * b.Y - a.Y * b.X;
        public static double Dot(Pt2 a, Pt2 b) => a.X * b.X + a.Y * b.Y;

        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }

    public readonly record struct Pt3(double X, double Y, double Z)
    {
        public Pt2 XY => new(X, Y);
    }

    public static class LengthUnits
    {
        public const double MmPerFoot = 304.8;
        public static double MmToFt(double mm) => mm / MmPerFoot;
        public static double FtToMm(double ft) => ft * MmPerFoot;
    }

    /// <summary>Circular arc defined in plan. Sweep is signed (CCW positive).</summary>
    public sealed class ArcInfo
    {
        public Pt2 Center { get; init; }
        public double Radius { get; init; }
        public double StartAngle { get; init; }
        public double Sweep { get; init; }

        public Pt2 PointAt(double angle) =>
            new(Center.X + Radius * Math.Cos(angle), Center.Y + Radius * Math.Sin(angle));

        /// <summary>Projects a point radially onto the circle.</summary>
        public Pt2 Project(Pt2 p)
        {
            var d = p - Center;
            double len = d.Length;
            return len < 1e-12 ? p : Center + d * (Radius / len);
        }
    }
}
