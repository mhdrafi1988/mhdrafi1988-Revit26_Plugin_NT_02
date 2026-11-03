// =======================================================
// File: MultiplePointsPayload.cs
// Location: Core/Models/
// Carries the Apply request across the ExternalEvent boundary. Settings
// values are copied out as plain fields (not the ObservableObject
// itself) so the engine never touches a WPF-bound object off the UI
// thread.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorMultiplePoints.V001.Core.Models
{
    public class MultiplePointsPayload
    {
        public ElementId FloorId { get; set; }

        public List<EdgePointModel> SelectedEdges { get; set; }

        public bool   AddMidpoint        { get; set; }
        public bool   AddQuarterPoints   { get; set; }
        public bool   AddExtraOnLongEdges { get; set; }
        public double ThresholdMeters    { get; set; }

        /// <summary>Called by the engine to emit a structured log entry.</summary>
        public Action<LogEntry> Log { get; set; }

        /// <summary>Called exactly once when the engine finishes (success or failure).</summary>
        public Action<MultiplePointsResult> OnCompleted { get; set; }
    }
}
