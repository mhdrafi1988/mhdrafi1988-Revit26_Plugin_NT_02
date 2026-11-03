// =======================================================
// File: InnerLoopsAndPerpendicularPayload.cs
// Location: Core/Models/
// New in V005. Carries a request across the ExternalEvent boundary. One
// Handler serves three operations (Analyze / Generate / Apply) — Generate
// and Apply both need a valid Revit API context (Generate reads
// SlabShapeEditor.SlabShapeVertices; Apply writes to it), so both must
// go through the event, not be called directly from the modeless window.
// OuterLoop is supplied by the ViewModel (cached from the last Analyze)
// rather than re-derived here — CurveLoop is a plain geometry value type,
// safe to carry across the boundary, same as AutoSlopeByDrain's
// DrainItem.LoopCurves.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorInnerLoopsAndPerpendicular.V001.Core.Models
{
    public enum InnerLoopsAndPerpendicularOperation { Analyze, Generate, Apply }

    public class InnerLoopsAndPerpendicularPayload
    {
        public ElementId FloorId { get; set; }
        public InnerLoopsAndPerpendicularOperation Operation { get; set; }

        // ── Generate ──────────────────────────────────────────────────────
        public List<FloorLoopModel> SelectedLoops { get; set; }
        public FloorLoopModel OuterLoop { get; set; }
        public double ToleranceMm { get; set; }
        public double GroupProximityMm { get; set; }

        // ── Apply ─────────────────────────────────────────────────────────
        public List<PerpendicularPointModel> SelectedPoints { get; set; }

        public Action<LogEntry> Log { get; set; }
        public Action<InnerLoopsAndPerpendicularResult> OnCompleted { get; set; }
    }
}
