// =======================================================
// File: InputParsing.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services
// New in V032 (plan 2.3). Text inputs are parsed and range-checked here so
// the window can show an inline message and keep Run disabled with the
// reason, instead of V030's silent red border on an int TextBox.
// =======================================================

using System.Globalization;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services
{
    public static class InputParsing
    {
        /// <summary>Accepts "1.5", "1,5" (current culture) and an optional trailing % or unit-less spaces.</summary>
        public static bool TryParse(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string t = text.Trim().TrimEnd('%').Trim();
            return double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Returns null when valid, otherwise a message the user can act on.</summary>
        public static string CheckRange(string text, double min, double max, string label, string unit, out double value)
        {
            if (!TryParse(text, out value))
                return $"{label}: enter a number.";
            if (value < min || value > max)
                return $"{label} must be between {Format(min)} and {Format(max)} {unit}.";
            return null;
        }

        public static string Format(double v) => v.ToString("0.###", CultureInfo.CurrentCulture);
    }
}
