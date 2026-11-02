namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Core.Models
{
    /// <summary>
    /// One viewport on the sheet, flattened to plain data so every ordering and
    /// numbering decision happens in Core with no Revit API in reach.
    /// Built by Infrastructure/Helpers/ViewportReader.
    /// </summary>
    public class BubbleRow
    {
        /// <summary>ElementId.Value of the VIEW (not the viewport) — what gets written to.</summary>
        public long ViewId { get; init; }

        /// <summary>ElementId.Value of the viewport, kept for logging/diagnostics.</summary>
        public long ViewportId { get; init; }

        public BubbleViewKind Kind { get; init; }

        /// <summary>Short human label for the Type pill ("Section", "Drafting", "Floor Plan"…).</summary>
        public string KindLabel { get; init; } = string.Empty;

        /// <summary>
        /// Key into Shared/Converters.cs ViewTypeGroupToBrushConverter's palette
        /// ("SectionOrCallout", "Drafting", "Elevation", "FloorPlan", …). That converter
        /// matches on ToString()/name by design, so this is a string rather than a
        /// shared enum, and unknown values fall back to its neutral grey pill.
        /// </summary>
        public string PillGroup { get; init; } = string.Empty;

        public string ViewName { get; init; } = string.Empty;

        /// <summary>Detail number as it reads now; empty when unset.</summary>
        public string CurrentNumber { get; init; } = string.Empty;

        /// <summary>False when the view has no VIEWPORT_DETAIL_NUMBER parameter at all.</summary>
        public bool HasNumberParameter { get; init; }

        /// <summary>False when the parameter exists but is read-only — such a row keeps
        /// its number, consumes no sequence number, and is reported as skipped.</summary>
        public bool IsWritable { get; init; }

        /// <summary>Bottom edge of the viewport's box outline, in internal units (feet),
        /// in sheet space. Bottom-alignment is what rows are banded on.</summary>
        public double BottomY { get; init; }

        /// <summary>Centre X of the viewport's box outline, in internal units (feet).</summary>
        public double CenterX { get; init; }
    }
}
