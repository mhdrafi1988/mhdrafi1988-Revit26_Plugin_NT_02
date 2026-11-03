// =======================================================
// File: MultiplePointsResult.cs
// Location: Core/Models/
// Plain result object returned by MultiplePointsEngine. No UI, no WPF,
// no ViewModel references.
// =======================================================

using Revit26_Plugin_Revised.Shared.Models;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorMultiplePoints.V001.Core.Models
{
    public class MultiplePointsResult
    {
        public bool   Success      { get; set; }
        public string ErrorMessage { get; set; }

        /// <summary>Per-edge results built by EdgePointService.ApplyPoints — added to the ViewModel's log on completion.</summary>
        public List<LogEntry> LogEntries { get; set; }
    }
}
