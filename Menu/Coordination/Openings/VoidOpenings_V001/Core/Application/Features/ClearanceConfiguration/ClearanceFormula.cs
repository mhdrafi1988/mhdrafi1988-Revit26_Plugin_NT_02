using System;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClearanceConfiguration
{
    /// <summary>Shows a clearance as a worked example, so the user can check a rule at a glance.</summary>
    public static class ClearanceFormula
    {
        /// <summary>
        /// Opening size for a straight crossing of an element of <paramref name="sizeMm"/>: the size
        /// plus the offset (twice for each side), rounded up to the module. Insulation and the extra
        /// for a sloped crossing are not included.
        /// </summary>
        public static double OpeningFor(double sizeMm, double clearanceMm, ClearanceMode mode, double roundUpToMm)
        {
            double opening = sizeMm + (mode == ClearanceMode.PerSide ? 2 * clearanceMm : clearanceMm);
            return roundUpToMm > 0 ? Math.Ceiling(opening / roundUpToMm - 1e-9) * roundUpToMm : opening;
        }

        /// <summary>
        /// Worked example for a rule, on the largest size it covers (the smallest when it has no upper
        /// limit): "50 + 2 × 30 = 110 mm", or "500 + 75 = 575 → 580 mm" when rounding changes it.
        /// </summary>
        public static string Example(ClearanceRule rule, double roundUpToMm)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            return Example(rule.HasUpperLimit ? rule.ToMm : rule.FromMm, rule.ClearanceMm, rule.Mode, roundUpToMm);
        }

        /// <summary>Worked example for one size, offset and mode.</summary>
        public static string Example(double sizeMm, double clearanceMm, ClearanceMode mode, double roundUpToMm)
        {
            double exact = OpeningFor(sizeMm, clearanceMm, mode, 0);
            double rounded = OpeningFor(sizeMm, clearanceMm, mode, roundUpToMm);
            string sum = mode == ClearanceMode.PerSide
                ? $"{MmText.Format(sizeMm)} + 2 × {MmText.Format(clearanceMm)}"
                : $"{MmText.Format(sizeMm)} + {MmText.Format(clearanceMm)}";
            return Math.Abs(rounded - exact) < 1e-6
                ? $"{sum} = {MmText.Format(exact)} mm"
                : $"{sum} = {MmText.Format(exact)} → {MmText.Format(rounded)} mm";
        }
    }
}
