using System.Collections.Generic;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models
{
    /// <summary>Which way the spot elevation tags line up.</summary>
    public enum AlignDirection
    {
        /// <summary>Tags end up in one column (same position left-right in the view).</summary>
        Vertical,

        /// <summary>Tags end up in one row (same position up-down in the view).</summary>
        Horizontal
    }

    /// <summary>How the common position is chosen.</summary>
    public enum AlignTarget
    {
        /// <summary>Mean of the selected positions.</summary>
        Average,

        /// <summary>Position shared by the most tags (within the tolerance).</summary>
        MostCommon,

        /// <summary>Middle position when sorted.</summary>
        Median,

        /// <summary>Lowest position (left for vertical, bottom for horizontal).</summary>
        Min,

        /// <summary>Highest position (right for vertical, top for horizontal).</summary>
        Max,

        /// <summary>Position of one chosen reference tag.</summary>
        Reference
    }

    /// <summary>One spot elevation tag: its id, a label and its tag position in view coordinates (feet).</summary>
    public sealed class SpotItem
    {
        /// <summary>Element id value.</summary>
        public long Id { get; set; }

        /// <summary>Text shown in lists, e.g. the elevation text.</summary>
        public string Label { get; set; }

        /// <summary>Tag position along the view's right direction (feet).</summary>
        public double U { get; set; }

        /// <summary>Tag position along the view's up direction (feet).</summary>
        public double V { get; set; }
    }

    /// <summary>The common position the engine picked.</summary>
    public sealed class TargetResult
    {
        /// <summary>The common position (feet, along the aligned axis).</summary>
        public double Value { get; set; }

        /// <summary>True when "Most common" found no shared position and the average was used.</summary>
        public bool FellBackToAverage { get; set; }

        /// <summary>Number of tags that share the chosen position (Most common only).</summary>
        public int SharedCount { get; set; }
    }

    /// <summary>The planned move of one tag.</summary>
    public sealed class PlannedMove
    {
        /// <summary>The tag.</summary>
        public SpotItem Item { get; set; }

        /// <summary>Distance to move along the aligned axis (feet, signed).</summary>
        public double Delta { get; set; }
    }

    /// <summary>Result of reading the selection: usable tags plus the ones skipped, each with a reason.</summary>
    public sealed class SpotReadResult
    {
        /// <summary>Tags that can be moved.</summary>
        public List<SpotItem> Items { get; } = new List<SpotItem>();

        /// <summary>Skipped tags: "Element 123: reason".</summary>
        public List<string> Skipped { get; } = new List<string>();
    }
}
