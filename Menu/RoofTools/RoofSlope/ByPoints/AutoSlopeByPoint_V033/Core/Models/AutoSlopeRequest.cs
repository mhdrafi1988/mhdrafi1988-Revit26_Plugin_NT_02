// =======================================================
// File: AutoSlopeRequest.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models
// Replaces V030's mutable AutoSlopePayload (inputs + callbacks + token on
// one object, with the marker groups passed by reference so UI edits
// leaked into a running job — V030 B7). This is an immutable snapshot
// taken when Run is pressed; callbacks travel separately on the job.
// =======================================================

using System.Collections.Generic;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models
{
    /// <summary>Marker style snapshot for one circle-marker group.</summary>
    /// <remarks>ShowCornerOffsetText (32.3.0, Highest point only): also place the Offset text note
    /// at every outer roof boundary corner. Applies only when ShowOffsetText is on.</remarks>
    public sealed record MarkerStyle(
        MarkerGroupKind Kind,
        string Label,
        bool IsEnabled,
        long LineStyleIdValue,
        string LineStyleName,
        string ColorName,
        double RadiusMm,
        bool ShowOffsetText,
        bool ShowCornerOffsetText = false);

    public sealed record AutoSlopeRequest
    {
        public long RoofIdValue { get; init; }

        /// <summary>Points the user picked on the roof (before any snapping).</summary>
        public IReadOnlyList<Point3> PickedPoints { get; init; } = new List<Point3>();

        public double SlopePercent { get; init; }

        /// <summary>Points further than this (along the path) from every drain stay at 0.</summary>
        public double MaxDrainDistanceM { get; init; }

        /// <summary>Longest straight link in the path graph. 0 = same as MaxDrainDistanceM.</summary>
        public double MaxEdgeLengthM { get; init; }

        public DrainMode DrainMode { get; init; }

        /// <summary>Also make shape points within SnapRadiusMm (same edge/opening) drains.</summary>
        public bool IncludeNearbyPoints { get; init; }

        public double SnapRadiusMm { get; init; }

        public bool InsertCurveIntersectionPoints { get; init; }

        public bool ExportToExcel { get; init; }
        public string ExportFolder { get; init; }

        public IReadOnlyList<MarkerStyle> Markers { get; init; } = new List<MarkerStyle>();

        /// <summary>V033: draw detail lines along the ridges between drain groups.</summary>
        public bool DrawRidgeLines { get; init; }

        /// <summary>V033: line style chosen for the ridge lines (0 / null = none chosen).</summary>
        public long RidgeLineStyleIdValue { get; init; }
        public string RidgeLineStyleName { get; init; }
        public double AllowedOffsetThresholdMm { get; init; }

        public string ProjectTitle { get; init; }

        /// <summary>Outer roof boundary corners in plan (feet), read before the run's transaction.
        /// Filled by the runner only when the corner Offset notes are on.</summary>
        public IReadOnlyList<Point3> BoundaryCorners { get; init; } = new List<Point3>();

        public double EffectiveMaxEdgeLengthM => MaxEdgeLengthM > 0 ? MaxEdgeLengthM : MaxDrainDistanceM;

        public double EffectiveSnapRadiusMm => IncludeNearbyPoints ? SnapRadiusMm : 0;
    }
}
