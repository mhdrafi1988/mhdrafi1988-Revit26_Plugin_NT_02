using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Layout;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Models
{
    /// <summary>A sheet in the project, for the sheet pickers.</summary>
    public sealed class SheetSummary
    {
        /// <summary>ElementId value of the sheet.</summary>
        public long Key { get; init; }

        /// <summary>Sheet number.</summary>
        public string Number { get; init; } = string.Empty;

        /// <summary>Sheet name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>"Number - Name", as shown in the pickers.</summary>
        public string Label => string.IsNullOrEmpty(Name) ? Number : $"{Number} - {Name}";

        /// <inheritdoc/>
        public override string ToString() => Label;
    }

    /// <summary>A title block type (or other named type) in the project.</summary>
    public sealed class NamedType
    {
        /// <summary>ElementId value.</summary>
        public long Key { get; init; }

        /// <summary>"Family : Type".</summary>
        public string Name { get; init; } = string.Empty;

        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>One viewport on a sheet, as read from the model.</summary>
    public sealed class PlacedView
    {
        /// <summary>ElementId value of the viewport.</summary>
        public long ViewportKey { get; init; }

        /// <summary>ElementId value of the view it shows.</summary>
        public long ViewKey { get; init; }

        /// <summary>View name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Readable view type ("Floor Plan", "Section" …).</summary>
        public string ViewTypeLabel { get; init; } = string.Empty;

        /// <summary>Sort position of <see cref="ViewTypeLabel"/> (plans first, legends last).</summary>
        public int TypeOrder { get; init; }

        /// <summary>View scale denominator (0 when the view has none).</summary>
        public int Scale { get; init; }

        /// <summary>True for a legend view (copied, not moved).</summary>
        public bool IsLegend { get; init; }

        /// <summary>ElementId value of the sheet it is on.</summary>
        public long SheetKey { get; init; }

        /// <summary>Number of the sheet it is on.</summary>
        public string SheetNumber { get; init; } = string.Empty;

        /// <summary>Detail number on its sheet.</summary>
        public string DetailNumber { get; init; } = string.Empty;

        /// <summary>ElementId value of the viewport type.</summary>
        public long ViewportTypeKey { get; init; }

        /// <summary>Viewport box plus title, where it is now (sheet feet).</summary>
        public SheetRect Footprint { get; init; }

        /// <summary>Box centre X relative to <see cref="Footprint"/>'s left edge (feet).</summary>
        public double CenterOffsetX { get; init; }

        /// <summary>Box centre Y relative to <see cref="Footprint"/>'s bottom edge (feet).</summary>
        public double CenterOffsetY { get; init; }

        /// <summary>Why it cannot be changed (checked out by another user …), or null.</summary>
        public string LockReason { get; init; }

        /// <summary>"1:100", or "—" for views without a scale.</summary>
        public string ScaleLabel => Scale > 0 ? $"1:{Scale}" : "—";
    }

    /// <summary>Everything on one sheet the planner needs.</summary>
    public sealed class SheetContent
    {
        /// <summary>ElementId value of the sheet.</summary>
        public long Key { get; init; }

        /// <summary>Sheet number.</summary>
        public string Number { get; init; } = string.Empty;

        /// <summary>Sheet name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Title block type on the sheet (0 when it has none).</summary>
        public long TitleBlockTypeKey { get; init; }

        /// <summary>Title block extent on the sheet, or null.</summary>
        public SheetRect? Frame { get; init; }

        /// <summary>Title block insertion point X (new sheets put it at 0,0).</summary>
        public double TitleBlockOriginX { get; init; }

        /// <summary>Title block insertion point Y.</summary>
        public double TitleBlockOriginY { get; init; }

        /// <summary>Why <see cref="Frame"/> is missing, or null.</summary>
        public string FrameProblem { get; init; }

        /// <summary>Why the sheet itself cannot be changed, or null.</summary>
        public string LockReason { get; init; }

        /// <summary>The viewports on the sheet.</summary>
        public List<PlacedView> Views { get; init; } = new();

        /// <summary>Other things that take room (schedules), as fixed rectangles.</summary>
        public List<SheetRect> FixedItems { get; init; } = new();

        /// <summary>"Number - Name".</summary>
        public string Label => string.IsNullOrEmpty(Name) ? Number : $"{Number} - {Name}";
    }

    /// <summary>The project's sheets and title blocks, read when the tool opens or on Refresh.</summary>
    public sealed class ProjectCatalog
    {
        /// <summary>Every sheet, by number.</summary>
        public List<SheetSummary> Sheets { get; init; } = new();

        /// <summary>The active view's sheet, or 0 when the active view is not a sheet.</summary>
        public long ActiveSheetKey { get; init; }

        /// <summary>Title block types, by name.</summary>
        public List<NamedType> TitleBlocks { get; init; } = new();

        /// <summary>Every sheet number in use (case-insensitive).</summary>
        public HashSet<string> ExistingSheetNumbers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
