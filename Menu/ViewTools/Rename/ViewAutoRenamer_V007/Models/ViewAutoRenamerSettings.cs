using System.Collections.Generic;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V007.Models
{
    /// <summary>
    /// Persisted at %AppData%\Revit26_Plugin_Revised\ViewAutoRenamer\settings.json
    /// via System.Text.Json. Loaded on window open, saved on window close
    /// and after Run, per project convention.
    /// </summary>
    public class ViewAutoRenamerSettings
    {
        /// <summary>
        /// Format version of the settings file. Files written before 7.3.0 have no
        /// version (read as 1); every later field has a default, so old files still load.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>Format version written by this release.</summary>
        public const int CurrentVersion = 2;

        /// <summary>
        /// Exact Autodesk.Revit.DB.ViewType names (as strings) that are
        /// checked in the View Type popover. Null/empty on first run —
        /// caller defaults to "all types checked" in that case.
        /// </summary>
        public List<string> CheckedViewTypeNames { get; set; } = new();

        public bool ShowPlacedOnSheet { get; set; } = true;
        public bool ShowNotPlaced     { get; set; } = true;

        public string SheetNumberContains { get; set; } = "";

        public DuplicateFixStrategy DuplicateStrategy { get; set; } = DuplicateFixStrategy.NumberedBrackets;
        public bool IsDryRun { get; set; } = true;

        public bool StandardizeEnabled            { get; set; } = true;
        public StandardizeCaseOption StandardizeCase { get; set; } = StandardizeCaseOption.TitleCase;
        public bool CleanWhitespacePunctuation     { get; set; } = true;

        /// <summary>7.3.0: list the views under a header per view-type group. Missing in older files: grouped.</summary>
        public bool GroupByViewType { get; set; } = true;

        /// <summary>7.3.0: the rename panel is unfolded. Missing in older files: unfolded, as before.</summary>
        public bool IsRenamePanelExpanded { get; set; } = true;
    }
}
