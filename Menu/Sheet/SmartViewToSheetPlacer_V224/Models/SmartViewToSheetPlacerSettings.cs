namespace Revit26_Plugin_Revised.SmartViewToSheetPlacer.V224.Models
{
    /// <summary>
    /// Persisted per-tool settings, stored at
    /// %AppData%\Revit26_Plugin_Revised\SmartViewToSheetPlacer\settings.json
    /// via System.Text.Json. Loaded on window open, saved on window close.
    /// Last-used Titleblock name, per-side Margins, and the global H/V gap
    /// are remembered.
    /// V222: per-ViewType-group gap settings (GapSettingsByType) reverted
    /// back to a single global H/V gap pair, applied uniformly to every
    /// group — confirmed with Rafi, moved next to the Margin fields on the
    /// Titleblock card.
    /// </summary>
    public class SmartViewToSheetPlacerSettings
    {
        /// <summary>Name of the last-used titleblock type (matched by name on reload; falls back to first available if not found).</summary>
        public string? LastTitleblockName { get; set; }

        /// <summary>Last-used top margin value in mm. Defaults to 10 if not yet saved.</summary>
        public double LastMarginTopMm { get; set; } = 10.0;

        /// <summary>Last-used bottom margin value in mm. Defaults to 10 if not yet saved.</summary>
        public double LastMarginBottomMm { get; set; } = 10.0;

        /// <summary>Last-used left margin value in mm. Defaults to 10 if not yet saved.</summary>
        public double LastMarginLeftMm { get; set; } = 10.0;

        /// <summary>Last-used right margin value in mm. Defaults to 10 if not yet saved.</summary>
        public double LastMarginRightMm { get; set; } = 10.0;

        /// <summary>Last-used global horizontal gap in mm, applied to every ViewType group.</summary>
        public double LastHorizontalGapMm { get; set; } = 5.0;

        /// <summary>Last-used global vertical gap in mm, applied to every ViewType group.</summary>
        public double LastVerticalGapMm { get; set; } = 5.0;

        /// <summary>Last-used global reading-order direction. Defaults to top-to-bottom, left-to-right.</summary>
        public string LastReadingDirection { get; set; } = "TopToBottom_LeftToRight";

        /// <summary>Last-used global row tiebreak. Defaults to XPosition.</summary>
        public string LastRowTiebreak { get; set; } = "XPosition";

        /// <summary>Last-used global row Y-tolerance in mm. Defaults to 500.</summary>
        public double LastYToleranceMm { get; set; } = 500.0;

        /// <summary>
        /// V220 NEW: last-used global default packing strategy (Stage 2's
        /// Placement Algorithm card) — stored by enum name (string), same
        /// pattern as LastReadingDirection/LastRowTiebreak, so settings.json
        /// stays stable across any future RowFillStrategy re-ordering.
        /// Matched back via Enum.Parse on load; falls back to MaxFill (the
        /// suite-wide new default per SheetAutoRearrange's RowFillStrategy
        /// remarks) if unset or unparseable. Applied to every newly-created
        /// SheetGroup's FillStrategy at packing time, before any per-sheet
        /// override.
        /// </summary>
        public string LastFillStrategy { get; set; } = "MaxFill";

        /// <summary>Whether Stage 5's Activity Log card was last left
        /// expanded or collapsed. Confirmed with Rafi: persisted across
        /// sessions (not just this session), restored on every future launch.
        /// V213 UPDATE: default changed to collapsed (false) — Rafi confirmed
        /// the log should start collapsed on a first-ever run, with the
        /// user's last choice remembered on every launch after that.</summary>
        public bool IsActivityLogExpanded { get; set; } = false;
    }
}
