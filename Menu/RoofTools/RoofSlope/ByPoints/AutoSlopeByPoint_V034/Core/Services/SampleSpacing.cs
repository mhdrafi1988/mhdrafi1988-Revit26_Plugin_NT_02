// =======================================================
// File: SampleSpacing.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services
// New in V034. The path graph keeps a straight link between two shape
// points only when check points along it all lie on the roof's top face.
// Up to V033 the distance between those check points was fixed (a quarter
// of a foot, shown as 75 mm). V034 lets the user choose it from a fixed
// list. Pure .NET, so it is unit-testable.
// =======================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services
{
    /// <summary>One entry of the sample spacing combo box.</summary>
    public sealed record SampleSpacingOption(double Mm, string Label);

    /// <summary>The sample spacings the user can choose, and the check-point count they give.</summary>
    public static class SampleSpacing
    {
        private const double MmPerFoot = 304.8;

        /// <summary>The V033 spacing in feet. The 75 mm choice uses exactly this, so its results match V033.</summary>
        private const double LegacySpacingFt = 0.25;

        /// <summary>The choices, finest first.</summary>
        public static IReadOnlyList<SampleSpacingOption> Options { get; } = AutoSlopeConstants.SampleSpacingChoicesMm
            .Select(mm => new SampleSpacingOption(mm, IsDefault(mm) ? $"{mm:0} mm (default)" : $"{mm:0} mm"))
            .ToList();

        /// <summary>The option used when nothing, or an unknown value, is saved.</summary>
        public static SampleSpacingOption DefaultOption { get; } = Options.First(o => IsDefault(o.Mm));

        /// <summary>The option for a saved value; an unknown value gives the default.</summary>
        public static SampleSpacingOption Find(double mm) =>
            Options.FirstOrDefault(o => Math.Abs(o.Mm - mm) < 1e-6) ?? DefaultOption;

        /// <summary>True when the value is the default (V033) spacing.</summary>
        public static bool IsDefault(double mm) => Math.Abs(mm - AutoSlopeConstants.DefaultSampleSpacingMm) < 1e-6;

        /// <summary>The spacing in feet. A value that is not one of the choices gives the default.</summary>
        public static double ToFeet(double mm)
        {
            double chosen = Find(mm).Mm;
            return IsDefault(chosen) ? LegacySpacingFt : chosen / MmPerFoot;
        }

        /// <summary>
        /// How many steps a link of this length is cut into. Never fewer than
        /// <see cref="AutoSlopeConstants.MinEdgeSamples"/>, so short links are checked more closely.
        /// </summary>
        public static int SampleCount(double lengthFt, double spacingMm) =>
            Math.Max(AutoSlopeConstants.MinEdgeSamples, (int)(lengthFt / ToFeet(spacingMm)));
    }
}
