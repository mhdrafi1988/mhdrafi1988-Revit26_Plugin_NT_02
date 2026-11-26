namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models
{
    /// <summary>The shared horizontal line the views of one row snap to.</summary>
    public enum RowAlign
    {
        /// <summary>Rows are not aligned; every view keeps its Y.</summary>
        Off,

        /// <summary>Per row, whichever of top / centre / bottom needs the least total travel.</summary>
        LeastMovement,

        /// <summary>The row shares one top edge.</summary>
        Top,

        /// <summary>The row shares one horizontal centre line.</summary>
        Center,

        /// <summary>The row shares one bottom edge, so viewport titles line up. The default.</summary>
        Bottom
    }

    /// <summary>The shared vertical line the views of one column snap to.</summary>
    public enum ColumnAlign
    {
        /// <summary>Columns are not aligned; every view keeps its X.</summary>
        Off,

        /// <summary>Per column, whichever of left / centre / right needs the least total travel.</summary>
        LeastMovement,

        /// <summary>The column shares one left edge. The default.</summary>
        Left,

        /// <summary>The column shares one vertical centre line.</summary>
        Center,

        /// <summary>The column shares one right edge.</summary>
        Right
    }

    /// <summary>What happens to the gaps between views on one axis.</summary>
    public enum DistributeMode
    {
        /// <summary>
        /// Every gap is left exactly as it is — pure alignment, the smallest possible change.
        /// The default, and what "do not reposition other than the required tolerance" means.
        /// </summary>
        Off,

        /// <summary>The gaps inside a row (or between rows) are made equal, without moving the block's outer edges.</summary>
        EvenGaps,

        /// <summary>The block is spread from one margin to the other, with equal gaps between.</summary>
        FitMargins
    }

    /// <summary>Where the four sheet margins come from.</summary>
    public enum MarginSource
    {
        /// <summary>Measured off the sheet as it stands: title block edge to the outermost view on each side.</summary>
        Detected,

        /// <summary>The numbers the user typed.</summary>
        Custom
    }

    /// <summary>What a view's footprint is measured as.</summary>
    public enum FootprintMode
    {
        /// <summary>The viewport box united with its title (label) outline. This is what lines titles up.</summary>
        BoxAndTitle,

        /// <summary>The viewport box alone, the title ignored. The default since 1.2.0.</summary>
        BoxOnly
    }

    /// <summary>What to do with a view that would have to travel further than the move limit.</summary>
    public enum OverLimitMode
    {
        /// <summary>Report it and leave it exactly where it is. The default.</summary>
        Report,

        /// <summary>Move it to its aligned position anyway.</summary>
        MoveAnyway
    }

    /// <summary>What kind of element one row of the grid stands for.</summary>
    public enum SheetElementKind
    {
        /// <summary>A Viewport — a plan, section, detail, drafting view or legend placed on the sheet.</summary>
        Viewport,

        /// <summary>A ScheduleSheetInstance — a schedule placed on the sheet.</summary>
        Schedule
    }

    /// <summary>What the plan does with one element.</summary>
    public enum AlignStatus
    {
        /// <summary>It will be moved to its aligned position.</summary>
        Move,

        /// <summary>It is already within the snap tolerance of its line — nothing to do.</summary>
        InPlace,

        /// <summary>Its move is longer than the Max move limit, so it is left where it is.</summary>
        OverLimit,

        /// <summary>No partner within tolerance on either axis, so there is no line to snap to.</summary>
        NoPartner,

        /// <summary>Left where it is: unticked, pinned while "Move pinned" is off, or owned by another user.</summary>
        Skipped,

        /// <summary>Its aligned position would leave the margins or clash with another view, so it is left where it is.</summary>
        Blocked
    }
}
