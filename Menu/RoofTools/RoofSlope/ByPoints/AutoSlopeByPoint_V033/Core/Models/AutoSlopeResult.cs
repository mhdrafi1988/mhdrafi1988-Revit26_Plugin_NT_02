// =======================================================
// File: AutoSlopeResult.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models
// Changes vs V030:
//   - Real start/end DateTime and fractional DurationSeconds (V030 B8
//     showed whole seconds × 1000 as "milliseconds").
//   - Carries the vertex rows, roof info and the request, so the manual
//     "Export results…" writes the full workbook instead of a placeholder.
//   - Unreached count and Revit-adjustment stats (V032 D5, D7).
// =======================================================

using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models
{
    /// <summary>Plain roof description captured during the run (for Excel and the log).</summary>
    public sealed record RoofInfo(long IdValue, string TypeName, string BaseLevelName, double BaseOffsetMm);

    public sealed class AutoSlopeResult
    {
        public bool Success { get; set; }
        public bool WasCancelled { get; set; }
        public string ErrorMessage { get; set; }

        public int VerticesProcessed { get; set; }
        /// <summary>Not sloped: beyond the drainage distance or no valid path.</summary>
        public int VerticesSkipped { get; set; }
        /// <summary>Subset of VerticesSkipped with no valid path at all.</summary>
        public int VerticesUnreached { get; set; }

        public int PickedDrainCount { get; set; }
        public int FinalDrainCount { get; set; }
        public int NearbyDrainPointsAdded { get; set; }

        /// <summary>Highest elevation read back from the model, mm above the drains.</summary>
        public double HighestElevationMm { get; set; }
        public double LongestPathM { get; set; }

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double DurationSeconds { get; set; }
        public string RunDate => StartTime.ToString("dd-MM-yy HH:mm");

        public int ArcsFound { get; set; }
        public int ArcPointsInserted { get; set; }
        public int GraphEdges { get; set; }

        public string ToolVersion { get; set; }
        public int ParameterStatus { get; set; }
        public int ParametersWritten { get; set; }
        public int ParametersSkipped { get; set; }

        public string ExportedFilePath { get; set; }

        public int DrainCirclesPlaced { get; set; }
        public int HighestCirclesPlaced { get; set; }
        public int OffsetCirclesPlaced { get; set; }

        /// <summary>V033: ridge detail lines placed (0 when the option is off).</summary>
        public int RidgeLinesPlaced { get; set; }

        /// <summary>V033: drain-group pairs that got at least one ridge line.</summary>
        public int RidgeCount { get; set; }

        /// <summary>V033: line style the ridge lines were drawn with (after any fallback).</summary>
        public string RidgeLineStyleUsed { get; set; }

        public int AdjustedByRevitCount { get; set; }
        public double MaxAdjustmentMm { get; set; }

        public IReadOnlyList<VertexData> Vertices { get; set; } = new List<VertexData>();
        public RoofInfo Roof { get; set; }
        public AutoSlopeRequest Request { get; set; }

        public static AutoSlopeResult Failed(string message, DateTime start) => new AutoSlopeResult
        {
            Success = false,
            ErrorMessage = message,
            StartTime = start,
            EndTime = DateTime.Now,
            DurationSeconds = (DateTime.Now - start).TotalSeconds
        };

        public static AutoSlopeResult Cancelled(DateTime start) => new AutoSlopeResult
        {
            Success = false,
            WasCancelled = true,
            ErrorMessage = "Cancelled by user.",
            StartTime = start,
            EndTime = DateTime.Now,
            DurationSeconds = (DateTime.Now - start).TotalSeconds
        };
    }
}
