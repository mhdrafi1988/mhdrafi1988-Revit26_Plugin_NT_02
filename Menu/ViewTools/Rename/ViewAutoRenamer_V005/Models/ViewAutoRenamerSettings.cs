using System.Collections.Generic;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V005.Models
{
    /// <summary>
    /// Persisted at %AppData%\Revit26_Plugin_Revised\ViewAutoRenamer\settings.json
    /// via System.Text.Json. Loaded on window open, saved on window close
    /// and after Run, per project convention.
    /// </summary>
    public class ViewAutoRenamerSettings
    {
        /// <summary>
        /// Exact Autodesk.Revit.DB.ViewType names (as strings) that are
        /// checked in the View Type popover. Null/empty on first run —
        /// caller defaults to "all types checked" in that case.
        ///
        /// Since the on-demand-load change these are the types that get READ
        /// FROM THE MODEL when the user clicks Load, not just a display
        /// filter. Same storage, wider meaning. They are restored as the
        /// pre-ticked selection on open; the tool deliberately does NOT
        /// auto-load them, since loading everything up front is the cost the
        /// change exists to avoid.
        /// </summary>
        public List<string> CheckedViewTypeNames { get; set; } = new();

        /// <summary>Load-time criteria alongside CheckedViewTypeNames — see ViewLoadCriteria.</summary>
        public bool ShowPlacedOnSheet { get; set; } = true;
        public bool ShowNotPlaced     { get; set; } = true;

        public string SheetNumberContains { get; set; } = "";

        public DuplicateFixStrategy DuplicateStrategy { get; set; } = DuplicateFixStrategy.NumberedBrackets;
        public bool IsDryRun { get; set; } = true;

        public bool StandardizeEnabled            { get; set; } = true;
        public StandardizeCaseOption StandardizeCase { get; set; } = StandardizeCaseOption.TitleCase;
        public bool CleanWhitespacePunctuation     { get; set; } = true;
    }
}
