using System.Globalization;
using System.Text;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Models;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Services
{
    /// <summary>
    /// Turns a 1-based position in the sequence into the text written to the detail
    /// number, and reads the window's "start at" text back into a position
    /// (NEW in 9.3.0). No Revit API.
    ///
    /// Numbers: 1 → "1", 2 → "2".
    /// Letters: 1 → "A", 26 → "Z", 27 → "AA", 28 → "AB" (spreadsheet-column order).
    /// </summary>
    public static class SequenceLabel
    {
        /// <summary>Longest start value accepted in letters ("ZZZ"), which keeps the
        /// position well inside an int however many views follow it.</summary>
        public const int MaxLetterCount = 3;

        /// <summary>The text for position <paramref name="index"/> (1-based) in the given style.</summary>
        public static string Format(int index, NumberingStyle style)
        {
            return style == NumberingStyle.Letters
                ? ToLetters(index)
                : index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads the start value the user typed. Numbers: a whole number above zero.
        /// Letters: one to three letters A–Z, upper or lower case. Returns false, with
        /// <paramref name="index"/> 0, for anything else.
        /// </summary>
        public static bool TryParseStart(string text, NumberingStyle style, out int index)
        {
            index = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string trimmed = text.Trim();

            if (style == NumberingStyle.Letters)
                return TryFromLetters(trimmed, out index);

            return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out index)
                   && index > 0;
        }

        /// <summary>1 → "A", 26 → "Z", 27 → "AA". Empty for a position below 1.</summary>
        public static string ToLetters(int index)
        {
            if (index < 1) return string.Empty;

            var letters = new StringBuilder();
            long remaining = index;

            while (remaining > 0)
            {
                remaining--;
                letters.Insert(0, (char)('A' + (int)(remaining % 26)));
                remaining /= 26;
            }

            return letters.ToString();
        }

        /// <summary>"A" → 1, "z" → 26, "AA" → 27. False for anything that is not one to
        /// <see cref="MaxLetterCount"/> letters A–Z.</summary>
        public static bool TryFromLetters(string text, out int index)
        {
            index = 0;
            if (string.IsNullOrEmpty(text) || text.Length > MaxLetterCount) return false;

            int value = 0;

            foreach (char raw in text)
            {
                char c = char.ToUpperInvariant(raw);
                if (c < 'A' || c > 'Z') return false;

                value = value * 26 + (c - 'A' + 1);
            }

            index = value;
            return true;
        }
    }
}
