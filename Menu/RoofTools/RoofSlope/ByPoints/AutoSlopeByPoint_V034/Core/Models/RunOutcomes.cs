// =======================================================
// File: RunOutcomes.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models
// New in V034. What each step of a run returns, as plain data. These
// classes lived next to the Revit code in V033 (DrainResolver,
// ElevationApplier, CircleMarkerService, RidgeLineService, ModelReadback,
// AutoSlopeParameterWriter, RevitConvert). They hold no Revit types, so
// they move here: the Application layer's interfaces return them, and the
// runner can be tested without Revit. Fields are unchanged.
// =======================================================

using System.Collections.Generic;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models
{
    /// <summary>The roof after the prepare step, as plain data: shape point positions
    /// (internal units, feet) in the same order every later step uses.</summary>
    public sealed class PreparedRoofData
    {
        /// <summary>Shape point positions after reset, arc points and drain points.</summary>
        public IReadOnlyList<Point3> Positions { get; init; } = new List<Point3>();

        /// <summary>Arc edges found on the roof.</summary>
        public int ArcsFound { get; init; }

        /// <summary>Shape points inserted where straight edges meet arcs.</summary>
        public int ArcPointsInserted { get; init; }
    }

    /// <summary>Which shape points are drains, and their drain groups.</summary>
    public sealed class DrainResolution
    {
        /// <summary>Drain vertex indices, ascending. Position in this list = drain ordinal.</summary>
        public List<int> DrainVertices { get; } = new List<int>();
        public int PickedCount { get; set; }
        public int NearbyAdded { get; set; }

        /// <summary>V033: drain vertex index → drain group (0-based, in pick order).</summary>
        public Dictionary<int, int> GroupOfDrain { get; } = new Dictionary<int, int>();

        /// <summary>V033: number of drain groups.</summary>
        public int GroupCount { get; set; }
    }

    /// <summary>What the elevation step did: one row per shape point plus counts.</summary>
    public sealed class ApplyOutcome
    {
        public List<VertexData> Vertices { get; } = new List<VertexData>();
        public int Processed { get; set; }
        public int BeyondDistance { get; set; }
        public int NoPath { get; set; }
        public double LongestPathFt { get; set; }
    }

    /// <summary>How many circle markers and corner notes were placed.</summary>
    public sealed class MarkerCounts
    {
        public int Drain { get; set; }
        public int Highest { get; set; }
        public int Offset { get; set; }
        public int CornerLabels { get; set; }
    }

    /// <summary>What the ridge-line step did.</summary>
    public sealed class RidgeLineOutcome
    {
        public int Placed { get; set; }
        public int Failed { get; set; }
        public int TooShort { get; set; }

        /// <summary>Line style the lines were drawn with, after any fallback.</summary>
        public string StyleUsed { get; set; }
    }

    /// <summary>Elevations read back from the model after the change.</summary>
    public sealed class ReadbackOutcome
    {
        public double HighestElevationMm { get; set; }
        public int Unmatched { get; set; }
        public int AdjustedCount { get; set; }
        public double MaxAdjustmentMm { get; set; }
    }

    /// <summary>What the roof-parameter step wrote and skipped.</summary>
    public sealed class ParameterWriteOutcome
    {
        public int Status { get; set; }
        public int Written { get; set; }
        public int Skipped { get; set; }
        /// <summary>One line per parameter that could not be written, with the reason.</summary>
        public System.Collections.Generic.List<string> Failures { get; } = new System.Collections.Generic.List<string>();
    }

    /// <summary>A run cannot continue. The message is shown to the user as-is,
    /// and the handler rolls the whole TransactionGroup back.</summary>
    public sealed class AutoSlopeAbortException : System.Exception
    {
        public AutoSlopeAbortException(string message) : base(message) { }
    }
}
