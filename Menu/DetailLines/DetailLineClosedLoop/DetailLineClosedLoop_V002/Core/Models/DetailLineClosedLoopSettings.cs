namespace Revit26_Plugin_Revised.DetailLineClosedLoop.V002.Core.Models
{
    /// <summary>
    /// Persisted settings for DetailLineClosedLoop, serialized via System.Text.Json to
    /// %AppData%\Revit26_Plugin_Revised\DetailLineClosedLoop\settings.json.
    /// </summary>
    public class DetailLineClosedLoopSettings
    {
        public bool SnapEndpoints { get; set; } = true;
        public double GapToleranceMm { get; set; } = 3.0;
        public bool GroupNewLines { get; set; } = false;
        public string GroupName { get; set; } = "ClosedLoop Group";
    }
}
