namespace Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Models
{
    /// <summary>Per-row alignment for Sheet Order Rearrange — how shorter views in a row align against the row's tallest view.</summary>
    public enum RowAlignment { Top, Center, Bottom }

    /// <summary>Whole-block horizontal alignment against the titleblock usable area (Sheet Order Rearrange).</summary>
    public enum BlockAlignmentH { Left, Center, Right }

    /// <summary>Whole-block vertical alignment against the titleblock usable area (Sheet Order Rearrange).</summary>
    public enum BlockAlignmentV { Top, Center, Bottom }

    /// <summary>
    /// How the leftover views the main fill strategy couldn't place (its
    /// Unplaced list) get grouped and laid out: Row groups them by Y-proximity
    /// (Row Tolerance) into horizontal bands filled left-to-right using Row
    /// Alignment; Column groups them by X-proximity (Column Tolerance) into
    /// vertical bands filled top-to-bottom using Within-Column Align.
    /// </summary>
    public enum LeftoverGroupMode { Row, Column }
}
