namespace Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Models
{
    /// <summary>
    /// Determines what happens to ticked views that don't fit the titleblock
    /// usable area after packing.
    /// NOTE: named OverflowHandlingMode (not OverflowMode) to avoid an
    /// ambiguous-reference collision with System.Windows.Controls.OverflowMode,
    /// which is pulled in wherever WPF ToolBar/ToolBarPanel types are used.
    /// </summary>
    public enum OverflowHandlingMode
    {
        /// <summary>
        /// Place all views even if they extend past sheet bounds.
        /// NOT IMPLEMENTED in V002 or V006 — selecting this logs a warning and
        /// falls back to PlaceWhatsPlaceable behavior.
        /// </summary>
        ForceFit,

        /// <summary>
        /// Duplicate the titleblock onto new sheet(s) for views that don't fit.
        /// NOT IMPLEMENTED in V002 or V006 — selecting this logs a warning and
        /// falls back to PlaceWhatsPlaceable behavior.
        /// </summary>
        AutoOverflowSheet,

        /// <summary>
        /// Views that don't fit the usable area are still placed — positioned
        /// directly below the sheet's bottom edge, continuing the same row/
        /// column layout — rather than left at their old position. They stay
        /// ticked in the grid and are flagged Overflow so they're easy to
        /// find and drag onto a real sheet. Only mode implemented in V002 or V006.
        /// </summary>
        PlaceWhatsPlaceable
    }
}
