using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Services
{
    /// <summary>Small text rules shared by the planner: cleaning segment values, level codes and disciplines.</summary>
    public static class NameRules
    {
        private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);
        private static readonly char[] WordBreaks = { ' ', '-', '_', '.', '(', ')', '/' };

        /// <summary>
        /// Makes a value safe to put in an ID: trims it, removes spaces and any occurrence of
        /// <paramref name="separator"/>. Returns an empty string when nothing is left.
        /// </summary>
        public static string Clean(string value, string separator)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var sb = new StringBuilder(value.Length);
            foreach (char c in value.Trim())
                if (!char.IsWhiteSpace(c) && !char.IsControl(c)) sb.Append(c);

            string cleaned = sb.ToString();
            if (!string.IsNullOrEmpty(separator))
                cleaned = cleaned.Replace(separator, string.Empty, StringComparison.Ordinal);
            return cleaned;
        }

        /// <summary>
        /// Short code for a level name. "L01 - Level 01" gives "L01" (a leading code is used as is),
        /// "Level 01" gives "L01", "Ground Floor" gives "GF", "Basement 1" gives "B1".
        /// </summary>
        public static string LevelShortCode(string levelName)
        {
            if (string.IsNullOrWhiteSpace(levelName)) return string.Empty;

            string[] words = levelName.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return string.Empty;

            // A leading token that already mixes letters and digits is the code: "L01", "B2", "RF1".
            string first = words[0];
            if (first.Length <= 6 && first.Any(char.IsDigit) && first.Any(char.IsLetter))
                return first.ToUpperInvariant();

            var sb = new StringBuilder();
            foreach (string word in words)
            {
                if (char.IsLetter(word[0])) sb.Append(char.ToUpperInvariant(word[0]));
                foreach (Match m in Digits.Matches(word)) sb.Append(m.Value);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Groups a category into Architecture, Structure or MEP from its built-in category name
        /// (e.g. "OST_StructuralColumns"), which does not depend on Revit's display language.
        /// </summary>
        public static string Discipline(string builtInCategoryName)
        {
            string n = builtInCategoryName ?? string.Empty;
            string[] structure = { "Structural", "Rebar", "Truss", "Fabric", "Joist", "StructConnection", "Coupler" };
            string[] mep =
            {
                "Duct", "Pipe", "Plumbing", "Mechanical", "Electrical", "Lighting", "Sprinkler", "Fire", "AirTerminal",
                "CableTray", "Conduit", "Data", "Communication", "Security", "NurseCall", "Telephone", "Flex", "MEP",
                "Fabrication", "Wire", "Hvac", "Audio", "Signal"
            };

            if (structure.Any(s => n.Contains(s, StringComparison.OrdinalIgnoreCase))) return "Structure";
            if (mep.Any(s => n.Contains(s, StringComparison.OrdinalIgnoreCase))) return "MEP";
            return "Architecture";
        }

        /// <summary>Pads a one-digit grid name to two digits ("4" gives "04"); anything else is returned unchanged.</summary>
        public static string PadGridNumber(string name) =>
            !string.IsNullOrEmpty(name) && name.Length == 1 && char.IsDigit(name[0]) ? "0" + name : name ?? string.Empty;
    }
}
