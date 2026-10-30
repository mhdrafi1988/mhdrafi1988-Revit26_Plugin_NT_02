using System.Text;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Models;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Services
{
    /// <summary>
    /// Turns a 1-based sequence index into a bubble label in the chosen style, and
    /// parses a starting label back to an index. No Revit API.
    /// </summary>
    public static class NumberFormatter
    {
        /// <summary>Label for <paramref name="index"/> (1-based). <paramref name="padWidth"/>
        /// only affects <see cref="NumberStyle.ZeroPadded"/> (minimum 2).</summary>
        public static string Format(int index, NumberStyle style, int padWidth = 2)
        {
            if (index < 1) index = 1;

            switch (style)
            {
                case NumberStyle.ZeroPadded:
                    return index.ToString("D" + System.Math.Max(2, padWidth));
                case NumberStyle.UpperLetters:
                    return ToLetters(index);
                case NumberStyle.LowerLetters:
                    return ToLetters(index).ToLowerInvariant();
                default:
                    return index.ToString();
            }
        }

        /// <summary>Digits needed for the largest index, for zero padding.</summary>
        public static int PadWidthFor(int lastIndex) => System.Math.Max(2, lastIndex.ToString().Length);

        /// <summary>
        /// Reads a starting label ("5", "05", "C", "aa") as a 1-based index. False when it is
        /// not valid for the style or is below 1.
        /// </summary>
        public static bool TryParse(string text, NumberStyle style, out int index)
        {
            index = 0;
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) return false;

            if (style == NumberStyle.Numeric || style == NumberStyle.ZeroPadded)
                return int.TryParse(text, System.Globalization.NumberStyles.None,
                           System.Globalization.CultureInfo.InvariantCulture, out index) && index >= 1;

            long value = 0;
            foreach (char raw in text)
            {
                char c = char.ToUpperInvariant(raw);
                if (c < 'A' || c > 'Z') return false;
                value = value * 26 + (c - 'A' + 1);
                if (value > int.MaxValue) return false;
            }

            index = (int)value;
            return index >= 1;
        }

        private static string ToLetters(int index)
        {
            var sb = new StringBuilder();
            while (index > 0)
            {
                index--;
                sb.Insert(0, (char)('A' + index % 26));
                index /= 26;
            }
            return sb.ToString();
        }
    }
}
