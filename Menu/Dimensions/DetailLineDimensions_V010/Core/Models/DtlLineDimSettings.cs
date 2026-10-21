namespace Revit26_Plugin_Revised.DtlLineDim.V010.Core.Models
{
    /// <summary>
    /// Persisted settings for DtlLineDim, serialized via System.Text.Json to
    /// %AppData%\Revit26_Plugin_Revised\DtlLineDim\settings.json (load/save mechanics
    /// now in Revit26_Plugin_Revised.Shared.Services.SettingsService&lt;T&gt;).
    /// ElementIds are not stable across documents, so last-used type is
    /// stored by Family/Type name string and re-resolved on load.
    /// </summary>
    public class DtlLineDimSettings
    {
        public double TickSizeMm { get; set; } = 50.0;
        public double OffsetMm { get; set; } = 300.0;
        public string LastDetailItemTypeName { get; set; } = string.Empty;
        public string LastDimensionTypeName { get; set; } = string.Empty;
        public string LastExportFolder { get; set; } = string.Empty;
    }
}
