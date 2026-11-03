using System;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// H(p) = tmin + k × min_i Li(p), k = slope% × 10 mm/m. Water falls to the perimeter.
    /// Thickness in millimetres, distances in metres.
    /// </summary>
    public sealed class ThicknessField
    {
        public ThicknessField(EdgeDistanceField distances, double minThicknessMm, double slopePercent)
        {
            Distances = distances ?? throw new ArgumentNullException(nameof(distances));
            MinThicknessMm = minThicknessMm;
            SlopePercent = slopePercent;
        }

        public EdgeDistanceField Distances { get; }
        public double MinThicknessMm { get; }
        public double SlopePercent { get; }

        /// <summary>Thickness gradient in mm per metre of horizontal distance.</summary>
        public double K => SlopePercent * 10.0;

        public double ThicknessAt(Vec2 p) => MinThicknessMm + K * Math.Max(0.0, Distances.MinDistance(p));

        /// <summary>Inward distance (m) at which the thickness reaches <paramref name="thicknessMm"/>.</summary>
        public double DistanceForThickness(double thicknessMm) =>
            K <= 0 ? double.PositiveInfinity : (thicknessMm - MinThicknessMm) / K;
    }
}
