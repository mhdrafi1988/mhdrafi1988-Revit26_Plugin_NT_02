using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Models
{
    /// <summary>
    /// Outcome of a single Run — drives the metric cards and the completion
    /// summary log line ("X placed | Y skipped | Z failed").
    /// </summary>
    public class RunResult
    {
        public int TotalViews { get; set; }
        public int Selected { get; set; }
        public int Removed { get; set; }
        public int PlacedSuccessfully { get; set; }
        public int FailedToFit { get; set; }

        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        public List<string> Warnings { get; } = new();
    }
}
