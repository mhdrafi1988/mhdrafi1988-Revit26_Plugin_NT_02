using System.Collections.Generic;

namespace Revit26_Plugin_Revised.DetailLineManager.V001.Core.Models
{
    /// <summary>Result of one Find lines run, handed from the Revit API context to the window.</summary>
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
    }
}
