namespace Revit26_Plugin_Revised.FloorPointElevationSync.V001
{
    /// <summary>Persisted to %AppData%\Revit26_Plugin_Revised\FloorPointElevationSync\settings.json</summary>
    public class FloorSyncSettings
    {
        public double XyToleranceMm { get; set; } = 10.0;
        public string LastLogExportFolder { get; set; } = string.Empty;
    }
}
