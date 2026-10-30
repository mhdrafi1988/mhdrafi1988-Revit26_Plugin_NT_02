// =======================================================
// File: AutoSlopeConstants.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models
// Replaces V030's Infrastructure/Helpers/AppConstants.cs. Moved into Core
// so Core no longer depends on Infrastructure (V030 had that backwards).
// Single source of defaults: AutoSlopeSettings initialises from these
// (fixes V030 B2, where the settings POCO silently overrode them).
// =======================================================

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models
{
    public static class AutoSlopeConstants
    {
        // ── Roof parameters (unchanged names from V030) ──────────────────
        public const string Param_HighestElevation      = "AutoSlope_HighestElevation";
        public const string Param_VerticesProcessed     = "AutoSlope_VerticesProcessed";
        public const string Param_VerticesSkipped       = "AutoSlope_VerticesSkipped";
        public const string Param_DrainCount            = "AutoSlope_DrainCount";
        public const string Param_RunDuration           = "AutoSlope_RunDuration_sec";
        public const string Param_LongestPath           = "AutoSlope_LongestPath";
        public const string Param_SlopePercent          = "AutoSlope_SlopePercent";
        public const string Param_SlopePercent_Text     = "AutoSlope_SlopePercent_Text";
        public const string Param_Threshold             = "AutoSlope_Threshold";
        public const string Param_RunDate               = "AutoSlope_RunDate";
        public const string Param_Status                = "AutoSlope_Status";
        public const string Param_Versions              = "AutoSlope_Versions";
        public const string Param_DrainToleranceMm      = "AutoSlope_DrainToleranceMm";
        public const string Param_DrainToleranceEnabled = "AutoSlope_DrainToleranceEnabled";

        public const int Status_OK      = 1;
        public const int Status_Partial = 2;
        public const int Status_Failed  = 3;

        /// <summary>Fallback when ToolCatalog has no entry for this namespace yet.</summary>
        public const string FallbackToolVersion = "33.0.1";

        // ── Defaults ─────────────────────────────────────────────────────
        public const double DefaultSlopePercent            = 1.5;
        public const double DefaultMaxDrainDistanceM       = 100;
        /// <summary>0 = use the drainage distance (same graph as V030).</summary>
        public const double DefaultMaxEdgeLengthM          = 0;
        public const double DefaultSnapRadiusMm            = 500;
        public const double DefaultMarkerRadiusMm          = 250;
        public const double DefaultAllowedOffsetThresholdMm = 500;
        public const string DefaultExportFolder            = "AutoSlope_Reports";

        /// <summary>Ridge lines (V033): the line style picked by default, and the first fallback
        /// when the chosen style is missing or Revit refuses it.</summary>
        public const string DefaultRidgeLineStyleName = "Thin Lines";

        public const string DefaultDrainColor   = "Blue";
        public const string DefaultHighestColor = "Red";
        public const string DefaultOffsetColor  = "Orange";

        public static readonly double[] SlopePresets = { 0.5, 1.0, 1.5, 2.0, 2.5 };

        // ── Validation ranges ────────────────────────────────────────────
        public const double SlopeMin = 0.1, SlopeMax = 10;
        public const double DrainDistanceMinM = 0.5, DrainDistanceMaxM = 500;
        public const double EdgeLengthMinM = 0.5, EdgeLengthMaxM = 500;
        public const double SnapRadiusMinMm = 1, SnapRadiusMaxMm = 5000;
        public const double MarkerRadiusMinMm = 10, MarkerRadiusMaxMm = 5000;
        public const double OffsetThresholdMinMm = 0, OffsetThresholdMaxMm = 100000;

        // ── Geometry tolerances (named, with units — V030 used bare literals) ──
        /// <summary>Graph links shorter than this are ignored (V030: 0.033 ft).</summary>
        public const double MinEdgeLengthFt = 0.033;
        /// <summary>Tolerance for "a point lies on an arc".</summary>
        public const double CurveToleranceMm = 2.0;
        /// <summary>An added drain point is found again within this XY distance.</summary>
        public const double AddedPointMatchMm = 5.0;
        /// <summary>Snap further than this logs a warning.</summary>
        public const double SnapWarningDistanceMm = 1000;
        /// <summary>Post-commit read-back match tolerance (XY), V030: 0.001 ft.</summary>
        public const double ReadbackMatchFt = 0.003;
        /// <summary>Face projection retry offset (1 mm).</summary>
        public const double FaceProjectionToleranceFt = 0.00328084;
        /// <summary>Elevation difference treated as "Revit adjusted the point".</summary>
        public const double AdjustmentWarningMm = 1.0;

        public const string RedHighestPointTextTypeName = "AutoSlope Highest Point (Red)";
        public const double HighestPointLabelOffsetMm   = 250;
        /// <summary>A boundary corner takes the elevation of the shape point within this plan distance (32.3.0).</summary>
        public const double CornerLabelMatchMm          = 50;
        /// <summary>Direction change, in degrees, that counts as a corner on the outer boundary (32.3.0).</summary>
        public const double CornerMinAngleDeg           = 1.0;
        /// <summary>Ridge lines (V033): two link ends this close in height count as equal.</summary>
        public const double RidgeTieToleranceMm         = 1.0;
    }
}
