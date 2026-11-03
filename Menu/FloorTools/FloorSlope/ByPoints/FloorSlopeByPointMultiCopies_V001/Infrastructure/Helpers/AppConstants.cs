namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Infrastructure.Helpers
{
    public static class AppConstants
    {
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

        public const double DefaultSlopePercent    = 1.5;
        public const int    DefaultThresholdMeters = 100;
        public const int    DefaultDrainToleranceMm = 500;
        public const string DefaultExportFolder    = "FloorSlope_Reports";

        /// <summary>Format string for each slope variant's workset name — one placeholder for the slope value, e.g. "Floor_Slope_2%".</summary>
        public const string WorksetNameFormat = "Floor_Slope_{0}%";

        public const string Color_Success    = "#2ECC71";
        public const string Color_Warning    = "#F1C40F";
        public const string Color_Error      = "#E74C3C";
        public const string Color_Info       = "#1ABC9C";
        public const string Color_Ready      = "#27AE60";
        public const string Color_Processing = "#E67E22";

        public const double DistanceTolerance    = 0.5;
        public const double ProjectionTolerance  = 0.00328084;
    }
}
