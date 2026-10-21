namespace Revit26_Plugin_Revised.RoofPointElevationSync.V005
{
    /// <summary>Persisted to %AppData%\Revit26_Plugin_Revised\RoofPointElevationSync\settings.json</summary>
    public class RoofSyncSettings
    {
        public double XyToleranceMm { get; set; } = 10.0;
        public string LastLogExportFolder { get; set; } = string.Empty;
    }
}
