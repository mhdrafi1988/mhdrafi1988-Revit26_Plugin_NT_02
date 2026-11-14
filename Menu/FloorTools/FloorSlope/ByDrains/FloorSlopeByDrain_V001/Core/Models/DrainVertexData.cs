// File: DrainVertexData.cs
// Location: Core/Models/
// Base: ported from AutoSlopeByDrain V004 (roof) (unchanged fields below the
// "NEW (V005)" marker).
//
// NEW (V005): ElevationFromModel_mm / ElevationDiff_mm ported from
// AutoSlopeByPoint's VertexData.cs. These are only populated when the
// "Verify elevations after commit" toggle is ON (off by default, per
// Rafi's confirmed decision; V013 turns the toggle on by default). When the toggle is off they remain 0,
// and the Excel export omits the ElevModel_mm / ElevDiff_mm columns
// entirely rather than showing meaningless zeroes — see
// ExcelExportHelper for the toggle-driven column logic.

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models
{
    public class DrainVertexData
    {
        public int VertexIndex { get; set; }
        public XYZ Position { get; set; }
        public double PathLengthMeters { get; set; }

        // Elevation calculated from: PathLength × SlopePercent.
        // This is what the engine WROTE to the vertex.
        private double _elevationOffsetMm;
        public double ElevationOffsetMm
        {
            get => Math.Round(_elevationOffsetMm, 0);
            set => _elevationOffsetMm = value;
        }

        public int NearestDrainId { get; set; }
        public XYZ DirectionVector { get; set; }
        public bool WasProcessed { get; set; }
        public string DrainSize { get; set; }
        public string DrainShape { get; set; }

        // ── NEW (V013) ───────────────────────────────────────────────────
        /// <summary>True for a point on a selected drain opening (set to 0, not "skipped").</summary>
        public bool IsDrain { get; set; }

        /// <summary>Why a non-drain point was left at 0: "No path" or "Beyond distance". Null when processed.</summary>
        public string SkipReason { get; set; }

        /// <summary>True when ElevationFromModel_mm was read back from the model after the run.</summary>
        public bool HasModelValue { get; set; }

        // ── NEW (V005): post-commit verification fields ──────────────────
        // Only populated when payload.VerifyElevationsAfterCommit == true.

        private double _elevationFromModel_mm;
        public double ElevationFromModel_mm
        {
            get => Math.Round(_elevationFromModel_mm, 0);
            set => _elevationFromModel_mm = value;
        }

        /// <summary>
        /// Model value minus calculated value. Zero = Revit accepted exactly
        /// what was written. Non-zero = Revit silently adjusted the vertex.
        /// Meaningless (always 0) when verification is disabled — callers
        /// must check the toggle before treating this as reliable.
        /// </summary>
        public double ElevationDiff_mm => ElevationFromModel_mm - ElevationOffsetMm;

        public string Direction =>
            DirectionVector != null ?
            $"{DirectionVector.X:F3},{DirectionVector.Y:F3},{DirectionVector.Z:F3}" :
            "0,0,0";
    }

    public class DrainExportMetrics
    {
        public int ProcessedVertices { get; set; }
        public int SkippedVertices { get; set; }
        public int DrainCount { get; set; }
        public double HighestElevationMm { get; set; }
        public double LongestPathM { get; set; }
        public double SlopePercent { get; set; }
        public int RunDurationSec { get; set; }
        public string RunDate { get; set; }
        public string FloorId { get; set; }
        public string FloorName { get; set; }

        /// <summary>Wall-clock time the engine started processing this floor, e.g. "14:32:05".</summary>
        public string StartTime { get; set; }

        /// <summary>Wall-clock time the engine finished processing this floor, e.g. "14:32:41".</summary>
        public string EndTime { get; set; }

        // ── NEW (V005) ─────────────────────────────────────────────────────
        /// <summary>Total drains found by DrainDetectionService, before any user selection.</summary>
        public int TotalDetectedCount { get; set; }

        /// <summary>Drains the user had checked in the grid at Run time.</summary>
        public int SelectedCount { get; set; }

        /// <summary>Boundary/opening arcs processed when curve-intersection insertion is enabled. 0 if the feature is off.</summary>
        public int ArcsCalculated { get; set; }

        /// <summary>Tool version string — written to the Run Summary sheet and floor parameter.</summary>
        public string Version { get; set; }

        /// <summary>Vertices skipped specifically because they were unreachable from any selected drain (subset of SkippedVertices).</summary>
        public int UnreachableVertexCount { get; set; }

        /// <summary>Vertices skipped specifically because a path existed but exceeded Max Path Distance (subset of SkippedVertices, distinct from UnreachableVertexCount).</summary>
        public int OverThresholdVertexCount { get; set; }
    }
}
