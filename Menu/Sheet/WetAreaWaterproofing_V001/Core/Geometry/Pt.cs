using System;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry
{
    /// <summary>A plan point in millimetres (host model coordinates).</summary>
    public readonly record struct Pt(double X, double Y)
    {
        /// <summary>Distance to <paramref name="other"/>, mm.</summary>
        public double DistanceTo(Pt other)
        {
            double dx = X - other.X, dy = Y - other.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Point halfway to <paramref name="other"/>.</summary>
        public Pt MidTo(Pt other) => new((X + other.X) / 2, (Y + other.Y) / 2);
    }

    /// <summary>A straight boundary piece from <see cref="A"/> to <see cref="B"/>, mm.</summary>
    public readonly record struct Seg(Pt A, Pt B)
    {
        /// <summary>Length, mm.</summary>
        public double Length => A.DistanceTo(B);

        /// <summary>The same piece drawn the other way.</summary>
        public Seg Reversed() => new(B, A);
    }
}
