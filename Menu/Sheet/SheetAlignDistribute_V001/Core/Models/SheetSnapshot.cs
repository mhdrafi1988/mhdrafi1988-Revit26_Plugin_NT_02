using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models
{
    /// <summary>
    /// One movable element on the sheet, read once so the layout maths and the preview never touch
    /// the Revit API.
    /// </summary>
    public sealed class SheetElementSnapshot
    {
        /// <summary>The Viewport or ScheduleSheetInstance element.</summary>
        public ElementId ElementId { get; init; }

        /// <summary>The element id's value — identifies it across refreshes (0 when there is no id).</summary>
        public long Key => ElementId?.Value ?? 0;

        /// <summary>Whether it is a viewport or a schedule, which decides how it is moved.</summary>
        public SheetElementKind Kind { get; init; }

        /// <summary>Name of the view shown, or of the schedule.</summary>
        public string Name { get; init; }

        /// <summary>Display name of the view type (Floor Plan, Section, Legend, Schedule, ...).</summary>
        public string TypeName { get; init; }

        /// <summary>The viewport's Detail Number; empty for a schedule or a viewport without one.</summary>
        public string DetailNumber { get; init; }

        /// <summary>Footprint in sheet feet: the box, united with the title when the footprint mode asks for it.</summary>
        public LayoutRect Footprint { get; init; }

        /// <summary>True when the element is pinned.</summary>
        public bool IsPinned { get; init; }

        /// <summary>
        /// Why the element cannot be edited right now (owned by another user, or changed in central
        /// and needing Reload Latest), or null when it can be moved.
        /// </summary>
        public string LockReason { get; init; }
    }

    /// <summary>Everything the tool needs to know about one sheet, read in a single pass.</summary>
    public sealed class SheetSnapshot
    {
        /// <summary>The sheet.</summary>
        public ElementId SheetId { get; init; }

        /// <summary>"A101 - Sheet Name".</summary>
        public string SheetLabel { get; init; }

        /// <summary>Title block bounding box in sheet feet, or null when it could not be used.</summary>
        public LayoutRect? TitleBlockFrame { get; init; }

        /// <summary>Why <see cref="TitleBlockFrame"/> is null (no / several title blocks, unreadable geometry).</summary>
        public string FrameProblem { get; init; }

        /// <summary>Movable elements on the sheet, unsorted.</summary>
        public List<SheetElementSnapshot> Elements { get; init; } = new();

        /// <summary>Elements whose outline could not be read; they are left out and never moved.</summary>
        public int UnreadableCount { get; init; }
    }
}
