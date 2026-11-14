// =======================================================
// File: AutoSlopeResult.cs
// Location: Core/Models/
// Purpose: Plain result object returned by the engine.
//          No UI, no WPF, no ViewModel references.
//          The ViewModel subscribes via OnCompleted callback
//          in AutoSlopePayload and reads from this object.
// =======================================================

namespace Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Core.Models
{
    public class AutoSlopeResult
    {
        /// <summary>True if the engine completed without fatal errors.</summary>
        public bool Success { get; set; }

        /// <summary>NEW. True when this run was stopped by the user via Cancel.</summary>
        public bool WasCancelled { get; set; }

        /// <summary>Human-readable error message when Success is false.</summary>
        public string ErrorMessage { get; set; }

        public int VerticesProcessed { get; set; }
        public int VerticesSkipped { get; set; }

        /// <summary>Raw drain count from user selection � before tolerance expansion.</summary>
        public int PickedDrainCount { get; set; }

        /// <summary>Final drain count after tolerance radius is applied.</summary>
        public int FinalDrainCount { get; set; }
        public double HighestElevation_mm { get; set; }
        public double LongestPath_m { get; set; }
        public int RunDuration_sec { get; set; }
        public string RunDate { get; set; }

        /// <summary>Wall-clock time this run started, e.g. "14:32:05".</summary>
        public string RunStartTime { get; set; }

        /// <summary>Wall-clock time this run finished, e.g. "14:32:41".</summary>
        public string RunEndTime { get; set; }

        /// <summary>Number of boundary arc edges found on the floor (only populated when curve-intersection handling is enabled).</summary>
        public int CurvesCalculated { get; set; }

        /// <summary>Plugin/tool version string, e.g. "P.10.00".</summary>
        public string Version { get; set; }

        /// <summary>Parameter-write status code: 1=OK, 2=Partial, 3=Failed.</summary>
        public int Status { get; set; }

        /// <summary>
        /// Path of the Excel file auto-exported after Run.
        /// Null if export was skipped (ExportToExcel = false or EPPlus missing).
        /// </summary>
        public string ExportedFilePath { get; set; }

        // ── Circle Markers (V026) ─────────────────────────────
        /// <summary>Number of drain circles placed. 0 if the group was disabled or placement was skipped (e.g. non-plan active view).</summary>
        public int DrainCirclesPlaced { get; set; }

        /// <summary>Number of highest-point circles placed.</summary>
        public int HighestCirclesPlaced { get; set; }

        /// <summary>Number of allowed-offset circles placed.</summary>
        public int OffsetCirclesPlaced { get; set; }

        // ── Ridge (V001) ─────────────────────────────────────
        /// <summary>Number of drain groups after clustering by DrainGroupRadiusMm.</summary>
        public int DrainGroupCount { get; set; }

        /// <summary>Number of vertices the ridge rule applied to.</summary>
        public int RidgePointCount { get; set; }

        /// <summary>Number of vertices whose drain changed because of a ridge ("move watershed").</summary>
        public int WatershedMovedCount { get; set; }

        /// <summary>Number of ridge circles placed.</summary>
        public int RidgeCirclesPlaced { get; set; }
    }
}