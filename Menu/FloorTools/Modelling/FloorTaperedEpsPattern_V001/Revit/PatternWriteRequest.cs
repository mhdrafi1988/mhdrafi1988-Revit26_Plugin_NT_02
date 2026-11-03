using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>Everything the writer needs, captured on the UI side before ExternalEvent.Raise().</summary>
    public sealed class PatternWriteRequest
    {
        public PatternWriteRequest(FloorContext floor, Pattern2DResult pattern)
        {
            Floor = floor;
            Pattern = pattern;
        }

        public FloorContext Floor { get; }
        public Pattern2DResult Pattern { get; }

        /// <summary>GraphicsStyle id per layer (projection style of an OST_Lines subcategory).</summary>
        public IReadOnlyDictionary<PatternLayer, ElementId> LineStyles { get; init; } =
            new Dictionary<PatternLayer, ElementId>();

        public ElementId? TextNoteTypeId { get; init; }

        /// <summary>Where hatch fill types come from (only used when the pattern has hatches).</summary>
        public HatchTypeSource HatchSource { get; init; } = HatchTypeSource.CreateToolTypes;

        /// <summary>PickExisting: the picked type per slot (repeats when there are more slots).</summary>
        public IReadOnlyList<ElementId> PickedHatchTypeIds { get; init; } = new List<ElementId>();
        public GroupingMode Grouping { get; init; } = GroupingMode.SingleGroup;

        /// <summary>Delete the previous grouped pattern for this floor and view first (needs a grouping mode other than NoGroup).</summary>
        public bool ReplacePrevious { get; init; } = true;
    }
}
