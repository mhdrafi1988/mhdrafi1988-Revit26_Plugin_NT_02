// =======================================================
// File: InnerLoopDividerPayload.cs
// Location: Core/Models/
// Carries a request across the ExternalEvent boundary.
// Changes vs V011:
//   + Document — the document the floor lives in (V011 used whatever
//     document was active when the event ran).
//   + Mode / SpacingFeet / ResetOffsetsFirst for Apply.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Geometry;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Models
{
    public enum InnerLoopDividerOperation { Analyze, Apply }

    public class InnerLoopDividerPayload
    {
        public ElementId FloorId { get; set; }

        /// <summary>Document the floor belongs to. Null = active document (V011 behaviour).</summary>
        public Document Document { get; set; }

        public InnerLoopDividerOperation Operation { get; set; }

        /// <summary>Apply only — the loops the user ticked.</summary>
        public List<FloorLoopModel> SelectedLoops { get; set; }

        public DivisionMode Mode { get; set; } = DivisionMode.Count;

        /// <summary>Spacing mode: largest gap between points, in feet.</summary>
        public double SpacingFeet { get; set; }

        /// <summary>Apply only — set every existing shape point back to offset 0 before adding points.</summary>
        public bool ResetOffsetsFirst { get; set; }

        public Action<LogEntry> Log { get; set; }

        /// <summary>Called exactly once when the engine finishes (success or failure).</summary>
        public Action<InnerLoopDividerResult> OnCompleted { get; set; }
    }
}
