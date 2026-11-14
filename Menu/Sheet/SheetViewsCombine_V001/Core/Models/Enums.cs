namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Models
{
    /// <summary>Where the ticked views go.</summary>
    public enum TargetMode
    {
        /// <summary>Sheet 1.</summary>
        Sheet1 = 0,

        /// <summary>Sheet 2.</summary>
        Sheet2 = 1,

        /// <summary>Another existing sheet, picked from a list.</summary>
        OtherSheet = 2,

        /// <summary>A new sheet the tool creates.</summary>
        NewSheet = 3
    }

    /// <summary>What happens to views already on the target sheet that are not ticked.</summary>
    public enum UntickedTargetMode
    {
        /// <summary>They stay where they are; the packing works around them.</summary>
        KeepInPlace = 0,

        /// <summary>Every view on the target sheet is packed again with the incoming ones.</summary>
        RepackAll = 1
    }

    /// <summary>What happens when one view cannot be moved.</summary>
    public enum FailureMode
    {
        /// <summary>Skip that view (it stays where it was), carry on, and report it.</summary>
        SkipAndReport = 0,

        /// <summary>Stop at the first failure and undo the whole run.</summary>
        StopAndUndo = 1
    }

    /// <summary>Order of the views going into the packing.</summary>
    public enum GroupOrder
    {
        /// <summary>Sheet 1 first, then Sheet 2, then the others; within a sheet by view type, then detail number.</summary>
        SheetThenType = 0,

        /// <summary>By view type, then detail number, whatever sheet they come from.</summary>
        TypeOnly = 1,

        /// <summary>Sheet 1 first, then Sheet 2; within a sheet by detail number.</summary>
        SheetThenDetail = 2
    }

    /// <summary>What the tool does with one view.</summary>
    public enum ViewAction
    {
        /// <summary>Not ticked and not on the target: nothing happens.</summary>
        None = 0,

        /// <summary>On the target, not ticked, kept in place (an obstacle for the packing).</summary>
        StaysInPlace = 1,

        /// <summary>On the target and ticked: moved to a new spot on the same sheet.</summary>
        Repack = 2,

        /// <summary>From another sheet: taken off it and placed on the target.</summary>
        Move = 3,

        /// <summary>A legend from another sheet: copied to the target at the same spot.</summary>
        CopyLegend = 4,

        /// <summary>Ticked but cannot be done (reason in the plan).</summary>
        Skip = 5
    }

    /// <summary>Outcome of one view after a run.</summary>
    public enum ViewOutcome
    {
        /// <summary>Re-packed on the target.</summary>
        Repacked = 0,

        /// <summary>Moved from another sheet.</summary>
        Moved = 1,

        /// <summary>Legend copied.</summary>
        Copied = 2,

        /// <summary>Skipped before or during the run; nothing changed for this view.</summary>
        Skipped = 3,

        /// <summary>Failed (the run stopped and was undone).</summary>
        Failed = 4
    }
}
