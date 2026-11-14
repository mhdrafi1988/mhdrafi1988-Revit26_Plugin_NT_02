// =======================================================
// File: CurveDividerResult.cs
// Location: Core/Models/
// New in V004. Plain result object returned by CurveDividerEngine.
// No UI, no WPF, no ViewModel references.
// =======================================================

using Revit26_Plugin_Revised.Shared.Models;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Core.Models
{
    public class CurveDividerResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }

        /// <summary>Per-edge results built by CurveDivisionService.ApplyDivisions — added to the ViewModel's log on completion.</summary>
        public List<LogEntry> LogEntries { get; set; }
    }
}
