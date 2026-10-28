namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models
{
    /// <summary>Where the list of sections comes from.</summary>
    public enum SourceMode
    {
        /// <summary>Every section marker visible in the source view.</summary>
        View,

        /// <summary>The section markers / section views selected in Revit.</summary>
        Selection,

        /// <summary>Markers picked in the view with "Pick in view…".</summary>
        Picked
    }

    /// <summary>How the views are arranged on the sheet.</summary>
    public enum ArrangeMode
    {
        /// <summary>Fixed columns that line up from row to row; columns spread to the full width.</summary>
        Grid,

        /// <summary>Views flow left to right and wrap like text; each row spread to the full width.</summary>
        Rows
    }

    /// <summary>Order the views are placed in (left to right, then the next row down).</summary>
    public enum SectionOrder
    {
        /// <summary>Same order as the markers in the source view: left to right, top to bottom.</summary>
        Plan,

        /// <summary>By view name, numbers compared as numbers (2 before 10).</summary>
        Number,

        /// <summary>Grouped by scale (1:20, then 1:50 …), plan order inside each group.</summary>
        Scale,

        /// <summary>Largest first, then plan order.</summary>
        Size
    }

    /// <summary>Where the rows sit vertically in the usable area.</summary>
    public enum VerticalMode
    {
        /// <summary>From the top, at the minimum gap between rows.</summary>
        Top,

        /// <summary>The block of rows centred, at the minimum gap between rows.</summary>
        Center,

        /// <summary>Rows spread to fill the height (one row sits at the top).</summary>
        Spread
    }

    /// <summary>How a short last row is placed (Rows arrangement, two or more rows).</summary>
    public enum LastRowMode
    {
        /// <summary>Same gap as the row above, from the left edge.</summary>
        Left,

        /// <summary>Same gap as the row above, centred.</summary>
        Center,

        /// <summary>Spread to the full width like every other row.</summary>
        Justify
    }

    /// <summary>What happens when the views do not fit on one sheet.</summary>
    public enum OverflowMode
    {
        /// <summary>Create more sheets, numbered on from the first.</summary>
        AddSheets,

        /// <summary>Fill one sheet; the rest are skipped and reported.</summary>
        Skip
    }
}
