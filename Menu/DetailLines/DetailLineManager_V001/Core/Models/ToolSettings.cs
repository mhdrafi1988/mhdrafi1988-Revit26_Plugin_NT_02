using System.Collections.Generic;

namespace Revit26_Plugin_Revised.DetailLineManager.V001.Core.Models
{
    /// <summary>
    /// Last-used filters and options, saved to
    /// %AppData%\Revit26_Plugin_Revised\DetailLineManager\settings.json.
    /// </summary>
    public sealed class ToolSettings
    {
        /// <summary>Settings format this build writes.</summary>
        public const int CurrentVersion = 2;

        /// <summary>
        /// Format version of the file. 0 means a file without the field; it is read as version 1.
        /// Version 1 (tool 1.0.x) has no overlap, keep-pattern, search-on-open or tab fields; they load
        /// with their defaults. Files from a newer build are read field by field; unknown fields are ignored.
        /// </summary>
        public int Version { get; set; } = CurrentVersion;

        // ── Scope & search ────────────────────────────────────────────────
        /// <summary>Search scope; Active view by default.</summary>
        public SearchScope Scope { get; set; } = SearchScope.ActiveView;

        /// <summary>Search text (style, view name, element ID, comment).</summary>
        public string SearchText { get; set; } = string.Empty;

        /// <summary>Treat the search text as a regular expression.</summary>
        public bool UseRegex { get; set; }

        /// <summary>Line styles unticked in the search checklist (by name).</summary>
        public List<string> ExcludedStyles { get; set; } = new();

        /// <summary>View type filter; empty for all.</summary>
        public string ViewType { get; set; } = string.Empty;

        /// <summary>Lowest effective line weight shown (1–16).</summary>
        public int MinWeight { get; set; } = 1;

        /// <summary>Highest effective line weight shown (1–16).</summary>
        public int MaxWeight { get; set; } = 16;

        /// <summary>Shortest length shown in mm, or null for no limit.</summary>
        public double? MinLengthMm { get; set; }

        /// <summary>Longest length shown in mm, or null for no limit.</summary>
        public double? MaxLengthMm { get; set; }

        /// <summary>Include straight lines.</summary>
        public bool IncludeLines { get; set; } = true;

        /// <summary>Include arcs and circles.</summary>
        public bool IncludeArcs { get; set; } = true;

        /// <summary>Include splines.</summary>
        public bool IncludeSplines { get; set; } = true;

        /// <summary>Include ellipses.</summary>
        public bool IncludeEllipses { get; set; } = true;

        /// <summary>Workset filter; empty for all.</summary>
        public string Workset { get; set; } = string.Empty;

        // ── Show only ─────────────────────────────────────────────────────
        /// <summary>Show only duplicate / overlapping lines.</summary>
        public bool OnlyDuplicates { get; set; }

        /// <summary>Show only lines shorter than <see cref="ToleranceMm"/>.</summary>
        public bool OnlyShort { get; set; }

        /// <summary>Show only lines in groups.</summary>
        public bool OnlyInGroups { get; set; }

        /// <summary>Show only lines owned by other users.</summary>
        public bool OnlyOwnedByOthers { get; set; }

        /// <summary>Tolerance in mm for short lines and duplicate geometry.</summary>
        public double ToleranceMm { get; set; } = 5.0;

        // ── Delete unused ─────────────────────────────────────────────────
        /// <summary>Delete ticked unused line styles.</summary>
        public bool DeleteUnusedStyles { get; set; } = true;

        /// <summary>Delete duplicate / overlapping lines.</summary>
        public bool DeleteDuplicates { get; set; } = true;

        /// <summary>Delete lines shorter than the tolerance.</summary>
        public bool DeleteShort { get; set; }

        /// <summary>Skip (and log) lines in groups; lines owned by others are always skipped.</summary>
        public bool SkipBlocked { get; set; } = true;

        /// <summary>Which duplicate survives.</summary>
        public DuplicateKeepRule KeepRule { get; set; } = DuplicateKeepRule.OldestId;

        /// <summary>Handle overlapping straight lines of the same style (version 2; off by default).</summary>
        public bool DeleteOverlaps { get; set; }

        /// <summary>Delete covered lines or join overlapping ones (version 2).</summary>
        public OverlapMode OverlapMode { get; set; } = OverlapMode.DeleteCovered;

        /// <summary>
        /// Line style name patterns that are never deleted, separated by ';' (* and ? wildcards),
        /// e.g. "A-*;S-*" (version 2).
        /// </summary>
        public string KeepStylePatterns { get; set; } = string.Empty;

        // ── Line thickness ────────────────────────────────────────────────
        /// <summary>Line thickness mode.</summary>
        public ThicknessMode ThicknessMode { get; set; } = ThicknessMode.AssignStyle;

        /// <summary>Line weight for the weight modes (1–16).</summary>
        public int Weight { get; set; } = 3;

        /// <summary>Style name last chosen for Assign line style.</summary>
        public string AssignStyleName { get; set; } = string.Empty;

        /// <summary>Per-view override mode: clear existing overrides instead of setting one.</summary>
        public bool ClearOverrides { get; set; }

        // ── Window ────────────────────────────────────────────────────────
        /// <summary>Search the active scope as soon as the window opens (version 2; on by default as before).</summary>
        public bool SearchOnOpen { get; set; } = true;

        /// <summary>Action tab shown last: 0 Delete unused, 1 Line thickness (version 2).</summary>
        public int LastTab { get; set; }

        /// <summary>Brings a file from an older or newer build to a usable state.</summary>
        public void Migrate()
        {
            if (Version <= 0) Version = 1;
            // 1 → 2: new fields already hold their defaults (same behaviour as 1.0.x).
            if (Version < CurrentVersion) Version = CurrentVersion;
            ExcludedStyles ??= new List<string>();
            KeepStylePatterns ??= string.Empty;
            if (LastTab is < 0 or > 1) LastTab = 0;
            SearchText ??= string.Empty;
            ViewType ??= string.Empty;
            Workset ??= string.Empty;
            AssignStyleName ??= string.Empty;
            MinWeight = Clamp(MinWeight, 1, 16);
            MaxWeight = Clamp(MaxWeight, 1, 16);
            Weight = Clamp(Weight, 1, 16);
            if (ToleranceMm < 0 || double.IsNaN(ToleranceMm)) ToleranceMm = 5.0;
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
    }
}
