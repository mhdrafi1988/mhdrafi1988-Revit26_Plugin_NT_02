// =======================================================
// File: CurveDividerPayload.cs
// Location: Core/Models/
// New in V004. Carries the Apply request across the ExternalEvent
// boundary. SelectedEdges carries the real Curve geometry captured at
// extraction time, the same way AutoSlopeByDrain's payload carries
// DrainItem.LoopCurves across the same boundary.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Core.Models
{
    public class CurveDividerPayload
    {
        public ElementId FloorId { get; set; }

        public List<CurveEdgeModel> SelectedEdges { get; set; }

        /// <summary>Called by the engine to emit a structured log entry.</summary>
        public Action<LogEntry> Log { get; set; }

        /// <summary>Called exactly once when the engine finishes (success or failure).</summary>
        public Action<CurveDividerResult> OnCompleted { get; set; }
    }
}
