using System.Collections.Generic;
using System.Globalization;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Range checks for <see cref="Pattern2DSettings"/>; returns messages, never throws.</summary>
    public static class SettingsValidator
    {
        public const double MinSlopePercent = 0.1;
        public const double MaxSlopePercent = 10.0;
        public const double MaxMinThicknessMm = 500.0;
        public const double MinContourIntervalMm = 1.0;
        public const double MaxContourIntervalMm = 500.0;

        public static IReadOnlyList<string> Validate(Pattern2DSettings settings)
        {
            var errors = new List<string>();
            CultureInfo ci = CultureInfo.InvariantCulture;

            if (!InRange(settings.BoardLengthM, Pattern2DSettings.MinBoardSizeM, Pattern2DSettings.MaxBoardSizeM))
            {
                errors.Add(string.Format(ci, "Board length must be {0:0.00}–{1:0.00} m.",
                    Pattern2DSettings.MinBoardSizeM, Pattern2DSettings.MaxBoardSizeM));
            }

            if (!InRange(settings.BoardWidthM, Pattern2DSettings.MinBoardSizeM, Pattern2DSettings.MaxBoardSizeM))
            {
                errors.Add(string.Format(ci, "Board width must be {0:0.00}–{1:0.00} m.",
                    Pattern2DSettings.MinBoardSizeM, Pattern2DSettings.MaxBoardSizeM));
            }

            if (!InRange(settings.SlopePercent, MinSlopePercent, MaxSlopePercent))
            {
                errors.Add(string.Format(ci, "Slope must be {0:0.0}–{1:0.0} %.", MinSlopePercent, MaxSlopePercent));
            }

            if (!InRange(settings.MinThicknessMm, 0, MaxMinThicknessMm))
            {
                errors.Add(string.Format(ci, "Minimum thickness must be 0–{0:0} mm.", MaxMinThicknessMm));
            }

            if (settings.DrawContours && !settings.ContourIntervalLocked && !InRange(settings.ContourIntervalMm, MinContourIntervalMm, MaxContourIntervalMm))
            {
                errors.Add(string.Format(ci, "Contour interval must be {0:0}–{1:0} mm.", MinContourIntervalMm, MaxContourIntervalMm));
            }

            if (!InRange(settings.HipStripWidthM, 0, 1.0))
            {
                errors.Add("Hip strip width must be 0–1.00 m.");
            }

            if (!InRange(settings.MinPieceM, 0.05, 1.0))
            {
                errors.Add("Minimum piece size must be 0.05–1.00 m.");
            }

            if (!InRange(settings.MinSegmentLengthM, 0, 0.1))
            {
                errors.Add("Minimum segment length must be 0–0.1 m.");
            }

            return errors;
        }

        private static bool InRange(double value, double min, double max) =>
            !double.IsNaN(value) && value >= min - 1e-9 && value <= max + 1e-9;
    }
}
