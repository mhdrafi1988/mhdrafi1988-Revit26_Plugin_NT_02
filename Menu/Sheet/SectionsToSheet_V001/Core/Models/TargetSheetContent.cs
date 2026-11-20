using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models
{
    /// <summary>
    /// One thing already on the target sheet: a viewport (box united with its title) or any other
    /// item (schedule, text, legend component …) by its bounding box. Sheet coordinates, feet.
    /// </summary>
    public sealed class TargetItem
    {
        /// <summary>The element's id value (the viewport for a view).</summary>
        public long Key { get; init; }

        /// <summary>The view shown, for a viewport; 0 otherwise.</summary>
        public long ViewKey { get; init; }

        /// <summary>View name, or the item's category / name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>True for a viewport; false for schedules and every other item.</summary>
        public bool IsViewport { get; init; }

        /// <summary>Where it sits on the sheet: viewport box ∪ title, or the item's bounding box.</summary>
        public LayoutRect Rect { get; init; }

        /// <summary>Viewport box centre, X (Revit places viewports by it).</summary>
        public double BoxCenterX { get; init; }

        /// <summary>Viewport box centre, Y.</summary>
        public double BoxCenterY { get; init; }

        /// <summary>The viewport's detail number as shown, or null.</summary>
        public string DetailNumber { get; init; }

        /// <summary>Scale denominator of the view, or 0.</summary>
        public int Scale { get; init; }

        /// <summary>Why the item can't be moved (owned by another user, changed in central), or null.</summary>
        public string LockReason { get; init; }
    }

    /// <summary>
    /// What is on the existing sheet chosen as the target, read once so the planner and the preview
    /// never touch the Revit API. Keys are element id values; lengths in feet.
    /// </summary>
    public sealed class TargetSheetContent
    {
        /// <summary>The sheet's element id value.</summary>
        public long Key { get; init; }

        /// <summary>Sheet number.</summary>
        public string Number { get; init; } = string.Empty;

        /// <summary>Sheet name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>"Family : Type" of its title block, or null when it has none.</summary>
        public string TitleBlockName { get; init; }

        /// <summary>The title block's extent on the sheet, or null.</summary>
        public LayoutRect? Frame { get; init; }

        /// <summary>Why <see cref="Frame"/> is null.</summary>
        public string FrameProblem { get; init; }

        /// <summary>Why nothing can be placed on the sheet (owned by another user …), or null.</summary>
        public string LockReason { get; init; }

        /// <summary>Viewports and other items on the sheet (the title block and its revision schedule left out).</summary>
        public List<TargetItem> Items { get; init; } = new();
    }
}
