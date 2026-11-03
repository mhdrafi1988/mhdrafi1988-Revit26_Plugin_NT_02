// =======================================================
// File: MultiSlopePayload.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001
// Purpose: Input for MultiSlopeVariantEngine.Execute — the source
//          floor/drain selection plus the 4 slope rows and every
//          setting carried over from AutoSlopePayload (threshold,
//          drain tolerance, curve-intersection, export, circle
//          markers), applied identically to every generated copy.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Models
{
    public class MultiSlopePayload
    {
        // ── Source floor / drains ───────────────────────────────
        public ElementId FloorId { get; set; }
        public List<XYZ> PickedDrainPoints { get; set; }
        public List<XYZ> DrainPoints { get; set; }

        // ── Slope rows ──────────────────────────────────────────
        public List<SlopeRowSetting> SlopeRows { get; set; }

        // ── Shared settings, applied identically to every copy ──
        public double ThresholdMeters { get; set; }
        public bool EnableDrainTolerance { get; set; }
        public int DrainToleranceMm { get; set; }
        public bool InsertCurveIntersectionPoints { get; set; }
        public ExportConfig ExportConfig { get; set; }
        public string ProjectTitle { get; set; }

        public CircleMarkerGroup DrainMarkerGroup { get; set; }
        public CircleMarkerGroup HighestPointMarkerGroup { get; set; }
        public CircleMarkerGroup AllowedOffsetMarkerGroup { get; set; }
        public double AllowedOffsetThresholdMm { get; set; }

        // ── Workset naming ───────────────────────────────────────
        /// <summary>Format string with one placeholder for the slope value, e.g. "Floor_Slope_{0}%".</summary>
        public string WorksetNameFormat { get; set; } = "Floor_Slope_{0}%";

        // ── Callbacks ─────────────────────────────────────────────
        public Action<LogEntry> Log { get; set; }

        /// <summary>Called exactly once when every active slope row has been attempted.</summary>
        public Action<MultiSlopeRunSummary> OnCompleted { get; set; }

        /// <summary>
        /// NEW. Called at phase boundaries and, throttled, during
        /// DijkstraPathEngine.BuildGraph to report run progress for the
        /// row currently being processed. The ViewModel converts this into an
        /// overall batch percentage (weighted by row index within the active
        /// row count) and pumps the UI so it actually repaints mid-run.
        /// </summary>
        public Action<RunProgressInfo> Progress { get; set; }

        /// <summary>
        /// NEW. Shared across every row in the batch (one CancellationTokenSource
        /// per Run click). Checked between rows and inside each row's
        /// DijkstraPathEngine.BuildGraph. A row already committed when Cancel is
        /// clicked stays committed; the row in progress rolls back cleanly
        /// (nothing has been written to it yet at that point); remaining rows
        /// are never started.
        /// </summary>
        public CancellationToken CancelToken { get; set; }
    }
}
