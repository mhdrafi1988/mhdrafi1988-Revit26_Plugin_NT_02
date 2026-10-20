using System;
using System.Collections.Generic;
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

        // Discipline / model codes commonly put in Revit file names.
        private static readonly HashSet<string> KnownModelCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            "ARC", "ARCH", "AR", "STR", "STRUC", "ST", "MEP", "MECH", "ME", "ELE", "ELEC", "EL", "ELV",
            "PLB", "PLU", "PL", "FP", "FPS", "FF", "HVAC", "CIV", "CV", "LND", "LAN", "LS", "INT", "ID",
            "FAC", "SITE", "ICT", "LV", "MV", "DRN", "SAN", "TEL", "SEC", "LIG", "LGT"
        };

        // Words in file names that say nothing about which model it is.
        private static readonly HashSet<string> FileNameNoise = new(StringComparer.OrdinalIgnoreCase)
        {
            "CENTRAL", "LOCAL", "DETACHED", "BACKUP", "MODEL", "RVT", "COPY", "FINAL", "WIP", "NEW", "OLD"
        };

        /// <summary>
        /// Finds the model code in a Revit file name: the first word that is a known discipline code
        /// ("NT_02_ARC_Central" gives "ARC", "NT02-MEP-L01" gives "MEP"). When there is none, returns the
        /// file name's meaningful words joined (at most 12 characters) and sets
        /// <paramref name="recognised"/> to false, so the caller can suggest setting a Model Code.
        /// </summary>
        public static string ModelCodeFromTitle(string title, out bool recognised)
        {
            recognised = false;
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;

            string name = title.Trim();
            if (name.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);

            string[] words = name.Split(new[] { '_', '-', ' ', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
            string known = words.FirstOrDefault(w => KnownModelCodes.Contains(w));
            if (known != null)
            {
                recognised = true;
                return known.ToUpperInvariant();
            }

            // Revit adds "_<username>" to local copies, so the last word after "Central"/"Local" is dropped.
            int cut = Array.FindIndex(words, w => w.Equals("CENTRAL", StringComparison.OrdinalIgnoreCase) ||
                                                   w.Equals("LOCAL", StringComparison.OrdinalIgnoreCase));
            var kept = (cut >= 0 ? words.Take(cut) : words)
                .Where(w => !FileNameNoise.Contains(w) && !Regex.IsMatch(w, @"^(R|V)?\d{2,4}$", RegexOptions.IgnoreCase));
            string joined = string.Concat(kept).ToUpperInvariant();
            return joined.Length <= 12 ? joined : joined.Substring(0, 12);
        }

        /// <summary>Pads a one-digit grid name to two digits ("4" gives "04"); anything else is returned unchanged.</summary>
        public static string PadGridNumber(string name) =>
            !string.IsNullOrEmpty(name) && name.Length == 1 && char.IsDigit(name[0]) ? "0" + name : name ?? string.Empty;
    }
}
