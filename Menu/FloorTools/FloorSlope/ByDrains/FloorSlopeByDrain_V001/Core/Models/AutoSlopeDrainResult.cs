// File: AutoSlopeDrainResult.cs
// Location: Core/Models/
// Plain result object returned by AutoSlopeDrainEngine. No UI/WPF references.
//
// Base fields ported unchanged from AutoSlopeByDrain V004 (roof).
//
// NEW (V005), per Rafi's confirmed decisions on 2026-07-21:
//   Version                — ported from ByPoint's AutoSlopeResult.Version.
//                            Default value is "006" (confirmed by Rafi).
//   TotalDetectedCount /
//   SelectedCount           — replaces ByPoint's PickedDrainCount/FinalDrainCount
//                             pattern, adapted for drain-detection workflow:
//                             TotalDetectedCount = every drain the grid found;
//                             SelectedCount = how many were checked at Run time.
//   ArcsCalculated          — boundary/opening arc count when curve-intersection
//                             insertion is enabled; 0 when the feature is off.
//   UnreachableVertexCount  — subset of VerticesSkipped that were skipped because
//                             they had no path to any selected drain at all
//                             (disconnected graph), not just "too far" — see
//                             AutoSlopeDrainEngine's Max Edge Distance handling.
//
// RENAMED: ExportedDetailedFilePath / ExportedSummaryFilePath (CSV, two files)
// collapsed into a single ExportedFilePath, since export is now one Excel
// workbook, not two CSVs.

//
// NEW (V013): DurationSeconds (decimal, wall clock), StartedAt / EndedAt,
// Vertices (rows for the results side panel) and DrainPointCount.

using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models
{
    public class AutoSlopeDrainResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }

        /// <summary>NEW (V010). True when this floor's run was stopped by the user's Cancel button, as distinct from a genuine failure. Success is also false in this case.</summary>
        public bool WasCancelled { get; set; }

        public int VerticesModified { get; set; }
        public int VerticesProcessed { get; set; }
        public int VerticesSkipped { get; set; }
        public int DrainCount { get; set; }
        public double HighestElevation_mm { get; set; }
        public double LongestPath_m { get; set; }
        public int RunDuration_sec { get; set; }
        public string RunDate { get; set; }

        /// <summary>Wall-clock time this floor's run started, e.g. "14:32:05".</summary>
        public string StartTime { get; set; }

        /// <summary>Wall-clock time this floor's run finished, e.g. "14:32:41".</summary>
        public string EndTime { get; set; }

        /// <summary>Parameter-write status code: 1=OK, 2=Partial, 3=Failed.</summary>
        public int Status { get; set; }

        /// <summary>Tool version from ToolCatalog, e.g. "13.0.0".</summary>
        public string Version { get; set; }

        /// <summary>Total drains DrainDetectionService found, before user selection.</summary>
        public int TotalDetectedCount { get; set; }

        /// <summary>Drains the user had checked in the grid at Run time.</summary>
        public int SelectedCount { get; set; }

        /// <summary>Boundary/opening arcs processed when curve-intersection insertion is enabled. 0 if off.</summary>
        public int ArcsCalculated { get; set; }

        /// <summary>Vertices skipped because they were unreachable from any selected drain (subset of VerticesSkipped).</summary>
        public int UnreachableVertexCount { get; set; }

        /// <summary>Vertices skipped because a path existed but exceeded Max Path Distance (subset of VerticesSkipped, distinct from UnreachableVertexCount).</summary>
        public int OverThresholdVertexCount { get; set; }

        /// <summary>Path of the exported Excel workbook, or null if export was skipped/failed.</summary>
        public string ExportedFilePath { get; set; }

        /// <summary>Circles placed on selected drain openings. 0 if the marker group was disabled or the active view wasn't a plan view.</summary>
        public int DrainCirclesPlaced { get; set; }

        /// <summary>Circles placed on vertices tied at max elevation. 0 if the marker group was disabled or the active view wasn't a plan view.</summary>
        public int HighestCirclesPlaced { get; set; }

        /// <summary>Circles placed on processed vertices meeting the Allowed Offset threshold. 0 if the marker group was disabled or the active view wasn't a plan view.</summary>
        public int OffsetCirclesPlaced { get; set; }

        // ── NEW (V013) ─────────────────────────────────────────────────────
        public DateTime StartedAt { get; set; }
        public DateTime EndedAt { get; set; }

        /// <summary>Wall-clock seconds, one decimal is meaningful (RunDuration_sec stays whole seconds for the floor parameter).</summary>
        public double DurationSeconds { get; set; }

        /// <summary>Slope applied to this floor (%), shown in the results side panel.</summary>
        public double SlopePercent { get; set; }

        /// <summary>Shape points matched on the selected drain openings.</summary>
        public int DrainPointCount { get; set; }

        /// <summary>Every shape point of the floor after the run (same rows as the Excel "Vertex Data" sheet).</summary>
        public List<DrainVertexData> Vertices { get; set; } = new List<DrainVertexData>();
    }
}
