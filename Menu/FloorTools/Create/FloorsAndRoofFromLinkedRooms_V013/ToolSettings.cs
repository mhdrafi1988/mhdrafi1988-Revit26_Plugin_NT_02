namespace Revit26_Plugin_Revised.FloorsAndRoofFromLinkedRooms.V013
{
    /// <summary>
    /// Per-tool persisted settings (System.Text.Json POCO), stored at
    /// %AppData%\Revit26_Plugin_Revised\FloorsAndRoofFromLinkedRooms\settings.json.
    /// Loaded on window open, saved on window close and after each run.
    /// Fields per confirmed spec: last link, last floor type, last roof type —
    /// matched by name on restore and silently skipped if no longer present in the model.
    /// </summary>
    public class ToolSettings
    {
        public string LastLinkName { get; set; }
        public string LastFloorTypeName { get; set; }
        public string LastRoofTypeName { get; set; }

        /// <summary>When true, level matching requires exact elevation equality
        /// (ToleranceMm forced to 0 in the UI). When false (default), matching uses
        /// ToleranceMm as the allowed elevation difference.</summary>
        public bool ExactElevationMatch { get; set; } = false;

        /// <summary>Elevation match tolerance in millimeters, used when
        /// ExactElevationMatch is false. Default 1mm per confirmed spec.</summary>
        public double ToleranceMm { get; set; } = 1.0;
    }
}
