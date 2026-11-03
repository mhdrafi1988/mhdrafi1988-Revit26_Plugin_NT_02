namespace Revit26_Plugin_Revised.FloorViewFocus.V001.Core.Models
{
    /// <summary>
    /// Persisted to %AppData%\Revit26_Plugin_Revised\FloorViewFocus\settings.json.
    /// </summary>
    public class FloorViewFocusSettings
    {
        public double ViewMarginMm { get; set; } = FloorViewFocusDefaults.DefaultMarginMm;
        public double AnnotationMarginMm { get; set; } = FloorViewFocusDefaults.DefaultMarginMm;
    }
}
