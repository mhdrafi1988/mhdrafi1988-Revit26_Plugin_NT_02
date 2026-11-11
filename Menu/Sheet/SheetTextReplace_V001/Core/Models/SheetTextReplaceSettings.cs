using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models
{
    /// <summary>
    /// Saved state of Sheet Text Replace, kept between windows and Revit sessions in
    /// %AppData%\Revit26_Plugin_Revised\SheetTextReplace\settings.json.
    /// A file without <see cref="Version"/> (or a missing file) loads as defaults.
    /// </summary>
    public sealed class SheetTextReplaceSettings
    {
        /// <summary>Format version of the file. 1 = first version (tool 1.3.0).</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>The format version this build writes.</summary>
        public const int CurrentVersion = 1;

        /// <summary>The search last used (shown again when the window opens).</summary>
        public SavedSearch Last { get; set; }

        /// <summary>Recent searches, newest first, at most <see cref="MaxRecent"/>.</summary>
        public List<SavedSearch> Recent { get; set; } = new();

        /// <summary>How many recent searches are kept.</summary>
        public const int MaxRecent = 10;
    }

    /// <summary>One search: the Find / Replace / Prefix / Suffix boxes and the four options.</summary>
    public sealed class SavedSearch
    {
        /// <summary>Find box.</summary>
        public string Find { get; set; } = string.Empty;

        /// <summary>Replace with box.</summary>
        public string ReplaceWith { get; set; } = string.Empty;

        /// <summary>Prefix box.</summary>
        public string Prefix { get; set; } = string.Empty;

        /// <summary>Suffix box.</summary>
        public string Suffix { get; set; } = string.Empty;

        /// <summary>Match case.</summary>
        public bool MatchCase { get; set; }

        /// <summary>Whole word.</summary>
        public bool WholeWord { get; set; }

        /// <summary>Regex.</summary>
        public bool UseRegex { get; set; }

        /// <summary>Trim spaces (on by default, as in the window).</summary>
        public bool TrimSpaces { get; set; } = true;

        /// <summary>"MEMBRANE → MEMBRANE TYPE 2 · Match case", shown in the Recent list.</summary>
        public string Label
        {
            get
            {
                var opts = new List<string>();
                if (!string.IsNullOrEmpty(Prefix)) opts.Add("prefix \"" + Prefix + "\"");
                if (!string.IsNullOrEmpty(Suffix)) opts.Add("suffix \"" + Suffix + "\"");
                if (MatchCase) opts.Add("Match case");
                if (WholeWord) opts.Add("Whole word");
                if (UseRegex) opts.Add("Regex");
                string replace = string.IsNullOrEmpty(ReplaceWith) ? "(delete)" : ReplaceWith;
                return $"{Find}  →  {replace}" + (opts.Count > 0 ? "  ·  " + string.Join(", ", opts) : string.Empty);
            }
        }

        /// <summary>True when both searches have the same boxes and options.</summary>
        public bool SameAs(SavedSearch other)
            => other != null && Find == other.Find && ReplaceWith == other.ReplaceWith && Prefix == other.Prefix
               && Suffix == other.Suffix && MatchCase == other.MatchCase && WholeWord == other.WholeWord
               && UseRegex == other.UseRegex && TrimSpaces == other.TrimSpaces;
    }
}
