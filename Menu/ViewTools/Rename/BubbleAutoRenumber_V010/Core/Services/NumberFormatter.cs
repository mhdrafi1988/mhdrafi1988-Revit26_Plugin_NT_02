using System.Text;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Core.Models;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Core.Services
{
    /// <summary>
    /// Converts between a 1-based sequence index and the text written to a view's
    /// detail number, for each <see cref="NumberingStyle"/>. No Revit API.
    /// </summary>
    public static class NumberFormatter
    {
        /// <summary>Text for a 1-based <paramref name="index"/>. <paramref name="padWidth"/> only affects ZeroPadded.</summary>
        public static string Format(int index, NumberingStyle style, int padWidth = 2)
        {
            switch (style)
            {
                case NumberingStyle.ZeroPadded:
                    return index.ToString().PadLeft(padWidth < 1 ? 1 : padWidth, '0');
                case NumberingStyle.LowerLetters:
                    return ToLetters(index, 'a');
                case NumberingStyle.UpperLetters:
                    return ToLetters(index, 'A');
                default:
                    return index.ToString();
            }
        }

        /// <summary>
        /// Reads the user's starting value ("5", "05", "c", "AA") as a 1-based index.
        /// Returns false when the text is not valid for the style.
        /// </summary>
        public static bool TryParseStart(string text, NumberingStyle style, out int index)
        {
            index = 0;
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) return false;

            if (style == NumberingStyle.Numeric || style == NumberingStyle.ZeroPadded)
                return int.TryParse(text, out index) && index > 0;

            long value = 0;
            foreach (char raw in text)
            {
                char c = style == NumberingStyle.LowerLetters ? raw : char.ToLowerInvariant(raw);
                if (style == NumberingStyle.LowerLetters && (c < 'a' || c > 'z')) return false;
                if (style == NumberingStyle.UpperLetters && (raw < 'A' || raw > 'Z')) return false;

                value = value * 26 + (c - 'a' + 1);
                if (value > int.MaxValue / 2) return false;
            }

            index = (int)value;
            return index > 0;
        }

        /// <summary>Bijective base-26: 1→a … 26→z, 27→aa.</summary>
        private static string ToLetters(int index, char first)
        {
            var sb = new StringBuilder();
            while (index > 0)
            {
                index--;
                sb.Insert(0, (char)(first + index % 26));
                index /= 26;
            }
            return sb.ToString();
        }
    }
}
