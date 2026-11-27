using System;
using System.Globalization;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core
{
    /// <summary>
    /// Formats the values the tool writes and compares. The two name slots are written as plain text; the two
    /// offset slots are Length parameters, so the text here is only what the grid, the log and the
    /// up-to-date comparison use.
    /// </summary>
    public static class ValueText
    {
        /// <summary>A signed offset in millimetres, rounded to whole millimetres, for example "+450" or "-100".</summary>
        public static string Offset(double offsetMm)
        {
            double rounded = Math.Round(offsetMm, MidpointRounding.AwayFromZero);
            if (Math.Abs(rounded) < 0.5) return "0";
            string sign = rounded > 0 ? "+" : "-";
            return sign + Math.Abs(rounded).ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>The value the run writes to <paramref name="slot"/> for <paramref name="facts"/>.</summary>
        public static string For(ElementFacts facts, TargetSlot slot)
        {
            if (facts == null) return string.Empty;
            // Tier 2 and Tier 3 get the level names but no offsets: their offset is not measured from the
            // level, so any number here would be wrong. An empty slot says "not applicable", not "zero".
            return slot switch
            {
                TargetSlot.BaseLevelName => facts.HasBase ? facts.BaseLevelName : string.Empty,
                TargetSlot.BaseOffset => facts.HasBase && facts.OffsetKnown ? Offset(facts.BaseOffsetMm) : string.Empty,
                TargetSlot.NearestLevelName => facts.NearestFound ? facts.NearestLevelName : string.Empty,
                TargetSlot.NearestOffset => facts.NearestFound && facts.OffsetKnown ? Offset(facts.NearestOffsetMm) : string.Empty,
                _ => string.Empty
            };
        }

        /// <summary>
        /// True when a parameter already showing <paramref name="current"/> would end up unchanged by writing
        /// <paramref name="wanted"/>. Lengths are compared as numbers so that a different unit display
        /// ("450 mm", "450.0") does not count as a difference.
        /// </summary>
        public static bool Matches(string current, string wanted, bool isText)
        {
            string a = (current ?? string.Empty).Trim();
            string b = (wanted ?? string.Empty).Trim();
            if (isText) return string.Equals(a, b, StringComparison.CurrentCulture);
            if (a.Length == 0 || b.Length == 0) return a.Length == b.Length;
            return TryNumber(a, out double x) && TryNumber(b, out double y) && Math.Abs(x - y) < 0.5;
        }

        /// <summary>Reads the leading signed number out of a displayed length such as "-100 mm" or "+450.00".</summary>
        public static bool TryNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var buffer = new System.Text.StringBuilder();
            foreach (char ch in text.Trim())
            {
                if (char.IsDigit(ch) || ch == '-' || ch == '+' || ch == '.' || ch == ',')
                {
                    if (ch == ',') continue;             // thousands separator
                    buffer.Append(ch);
                }
                else if (buffer.Length > 0) break;       // stop at the unit suffix
            }
            return double.TryParse(buffer.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(buffer.ToString(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }
    }
}
