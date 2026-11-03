// =======================================================
// File: AutoSlopePayload.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001
// Changes vs V028:
//   - OnCompleted removed: AutoSlopeEngine.ApplySlope now runs
//     transaction-free (assumes the caller already has a
//     Transaction/SubTransaction open) and returns its AutoSlopeResult
//     directly instead of invoking a callback — MultiSlopeVariantEngine
//     calls it once per active slope row, inside its own SubTransaction,
//     and needs the result synchronously to decide commit vs rollback.
//     Log stays as a callback since real-time per-copy logging during
//     the run is still needed.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Models
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

        // ── Callbacks ────────────────────────────────────────
        /// <summary>
        /// Called by the engine to emit a structured log entry.
        /// Subscriber (ViewModel) wires this to AddLog(LogEntry).
        /// Uses Shared.Models.LogEntry so colour is driven by
        /// LogLevelToColorConverter in the UI — no HTML tags.
        /// </summary>
        public Action<LogEntry> Log { get; set; }

        /// <summary>
        /// NEW. Called at phase boundaries and, throttled, during
        /// DijkstraPathEngine.BuildGraph to report run progress for THIS floor
        /// copy's geometry pass.
        /// </summary>
        public Action<RunProgressInfo> Progress { get; set; }

        /// <summary>
        /// NEW. Checked inside DijkstraPathEngine.BuildGraph. ApplySlope runs
        /// inside the caller's (MultiSlopeVariantEngine) already-open per-row
        /// Transaction — throwing OperationCanceledException here propagates up
        /// to that Transaction's own catch block, which rolls it back, so
        /// cancelling mid-graph-build undoes this row's floor copy cleanly.
        /// </summary>
        public CancellationToken CancelToken { get; set; }
    }
}
