// File: AppConstants.cs
// Location: Infrastructure/Helpers/
// Merged from AutoSlopeByPoint V018 and AutoSlopeByDrain V004 (roof) — parameter
// names were already identical between both tools (both target the same
// floor instance parameters), so this is a straight union of constants used
// by either side, plus new V005 defaults for the ported/added fields.
// V013: adds marker defaults and the input ranges used by inline validation.

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Infrastructure.Helpers
{
    public static class AppConstants
    {
        // ── Floor instance parameter names ───────────────────────────────────
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

        // Status Values
        public const int Status_OK      = 1;
        public const int Status_Partial = 2;
        public const int Status_Failed  = 3;

        // ── Defaults ─────────────────────────────────────────────────────────
        public const double DefaultSlopePercent          = 1.5;

        /// <summary>Default for Max Edge Distance (graph connectivity / ConnectionThresholdMeters).</summary>
        public const int DefaultConnectionThresholdMeters = 30;

        /// <summary>NEW (V005): default for Max Path Distance (result cutoff / ThresholdMeters), ported from ByPoint.</summary>
        public const int DefaultThresholdMeters          = 40;

        public const int DefaultPathSampleCount          = 10;
        public const string DefaultExportFolder          = "FloorSlope_Reports";

        // ── NEW (V013): marker defaults and input ranges ─────────────────────
        public const string DefaultDrainColor   = "Blue";
        public const string DefaultHighestColor = "Red";
        public const string DefaultOffsetColor  = "Orange";
        public const double DefaultMarkerRadiusMm  = 250;
        public const double DefaultAllowedOffsetMm = 500;

        public const double SlopeMin = 0.1, SlopeMax = 10;
        public const double DistanceMinM = 0.5, DistanceMaxM = 500;
        public const int PathSamplesMin = 2, PathSamplesMax = 50;
        public const double MarkerRadiusMinMm = 10, MarkerRadiusMaxMm = 5000;

        public const double DistanceTolerance   = 0.5;
        public const double ProjectionTolerance = 0.00328084;

    }
}
