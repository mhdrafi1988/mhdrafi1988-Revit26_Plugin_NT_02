// =======================================================
// File: AutoSlopePayload.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001
// Changes vs V06:
//   - Log callback changed from Action<string> to Action<LogEntry>
//     so the engine emits structured LogEntry (LogLevel + message)
//     instead of raw colour-tagged strings.
//   - LogColorHelper removed — level is expressed via LogLevel enum.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Core.Models
{
    public class AutoSlopePayload
    {
        // ── Inputs ──────────────────────────────────────────
        public ElementId FloorId { get; set; }

        /// <summary>Raw points the user picked — before tolerance expansion.</summary>
        public List<XYZ> PickedDrainPoints { get; set; }

        /// <summary>Final drain points after tolerance radius is applied (used for calculation).</summary>
        public List<XYZ> DrainPoints { get; set; }

        public double SlopePercent { get; set; }
        public double ThresholdMeters { get; set; }
        public bool EnableDrainTolerance { get; set; }
        public int DrainToleranceMm { get; set; }

        /// <summary>
        /// Opt-in: when true, the engine will insert real SlabShapeVertex points
        /// at line/arc intersection locations (where a straight floor-shape edge
        /// partially overlaps a boundary or opening arc) so distances and elevations
        /// account for curved edges instead of using a straight chord.
        /// </summary>
        public bool InsertCurveIntersectionPoints { get; set; }

        // ── Ridge (V001) ─────────────────────────────────────
        /// <summary>
        /// When true the engine detects ridge points between drain groups and
        /// lifts them so water leaves each ridge towards every surrounding drain
        /// at the given slope. When false the tool behaves exactly like V028.
        /// </summary>
        public bool RidgeDetectionEnabled { get; set; } = true;

        /// <summary>
        /// Drains within this XY distance of each other are ONE drain group
        /// (one side of a ridge). 0 = every drain is its own group.
        /// </summary>
        public int DrainGroupRadiusMm { get; set; }

        public ExportConfig ExportConfig { get; set; }

        /// <summary>
        /// Revit document title — passed in from the UI layer so that
        /// Core/Infrastructure never need to touch UIDocument directly.
        /// </summary>
        public string ProjectTitle { get; set; }

        // ── Circle Markers (V026) ─────────────────────────────
        /// <summary>Style/config for circles placed on final drain points. Null or IsEnabled=false skips this group.</summary>
        public CircleMarkerGroup DrainMarkerGroup { get; set; }

        /// <summary>Style/config for circles placed on vertices tied at max elevation. Null or IsEnabled=false skips this group.</summary>
        public CircleMarkerGroup HighestPointMarkerGroup { get; set; }

        /// <summary>Style/config for circles placed on processed vertices whose offset meets/exceeds AllowedOffsetThresholdMm. Null or IsEnabled=false skips this group.</summary>
        public CircleMarkerGroup AllowedOffsetMarkerGroup { get; set; }

        /// <summary>
        /// Threshold in millimeters for the Allowed Offset marker group.
        /// A processed vertex qualifies when its ElevationOffsetMm >= this value.
        /// Only relevant when AllowedOffsetMarkerGroup.IsEnabled is true.
        /// </summary>
        public double AllowedOffsetThresholdMm { get; set; }

        /// <summary>Style/config for circles placed on ridge points (V001). Null or IsEnabled=false skips this group.</summary>
        public CircleMarkerGroup RidgeMarkerGroup { get; set; }

        // ── Callbacks ────────────────────────────────────────
        /// <summary>
        /// Called by the engine to emit a structured log entry.
        /// Subscriber (ViewModel) wires this to AddLog(LogEntry).
        /// Uses Shared.Models.LogEntry so colour is driven by
        /// LogLevelToColorConverter in the UI — no HTML tags.
        /// </summary>
        public Action<LogEntry> Log { get; set; }

        /// <summary>
        /// Called exactly once when the engine finishes (success or failure).
        /// Core never imports the subscriber type — the ViewModel wires itself
        /// up as a lambda from the UI layer.
        /// </summary>
        public Action<AutoSlopeResult> OnCompleted { get; set; }

        /// <summary>
        /// NEW. Called at phase boundaries and, throttled, during
        /// DijkstraPathEngine.BuildGraph to report run progress. The ViewModel
        /// converts this into an on-screen percentage + phase label and pumps the
        /// UI so it actually repaints.
        /// </summary>
        public Action<RunProgressInfo> Progress { get; set; }

        /// <summary>
        /// NEW. Checked inside DijkstraPathEngine.BuildGraph, which runs BEFORE the
        /// "Apply AutoSlope (Ridge)" transaction opens — cancelling here costs
        /// nothing to unwind, since nothing has been written to the model yet.
        /// </summary>
        public CancellationToken CancelToken { get; set; }
    }
}
