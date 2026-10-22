using System.Collections.Generic;

namespace Revit26_Plugin_Revised.DetailLineManager.V001.Core.Models
{
    /// <summary>Result of one Find lines run (or a usage-only count), handed from the Revit API context to the window.</summary>
    public sealed class ScanResult
    {
        /// <summary>Detail lines in scope.</summary>
        public List<DetailLineRow> Rows { get; } = new();

        /// <summary>Every line style in the project with its project-wide usage.</summary>
        public List<LineStyleItem> Styles { get; } = new();

        /// <summary>Human-readable scope description for the log and status strip.</summary>
        public string ScopeText { get; set; } = string.Empty;

        /// <summary>Number of duplicate clusters found.</summary>
        public int DuplicateClusters { get; set; }

        /// <summary>Number of overlap groups (straight lines of the same style lying on each other).</summary>
        public int OverlapGroups { get; set; }

        /// <summary>True when line-style usage was counted; false leaves usage unknown.</summary>
        public bool UsageCounted { get; set; }

        /// <summary>True when this result only carries line styles (a usage count), not rows.</summary>
        public bool UsageOnly { get; set; }
    }
}
