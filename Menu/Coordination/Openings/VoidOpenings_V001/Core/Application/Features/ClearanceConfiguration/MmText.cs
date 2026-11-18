using System.Globalization;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClearanceConfiguration
{
    /// <summary>Reads and writes the millimetre values typed on the OPENING CLEARANCE card.</summary>
    public static class MmText
    {
        /// <summary>Largest length the card accepts, in millimetres.</summary>
        public const double MaxMm = 100000;

        /// <summary>True when the text is empty or only spaces.</summary>
        public static bool IsBlank(string text) => string.IsNullOrWhiteSpace(text);

        /// <summary>
        /// Reads a length of 0 to <see cref="MaxMm"/>. Accepts a decimal point or a decimal comma
        /// ("12.5" or "12,5"), whatever the Windows region; no thousands separators.
        /// </summary>
        public static bool TryParse(string text, out double valueMm)
        {
            valueMm = 0;
            if (IsBlank(text))
                return false;

            string normalised = text.Trim().Replace(',', '.');
            if (!double.TryParse(normalised, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value))
                return false;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > MaxMm)
                return false;

            valueMm = value;
            return true;
        }

        /// <summary>Writes a length with up to three decimals and a decimal point, for example "12.5".</summary>
        public static string Format(double valueMm) => valueMm.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
