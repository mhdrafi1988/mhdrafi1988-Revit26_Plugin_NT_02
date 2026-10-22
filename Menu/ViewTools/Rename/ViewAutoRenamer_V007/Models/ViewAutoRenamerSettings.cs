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
    }
}
