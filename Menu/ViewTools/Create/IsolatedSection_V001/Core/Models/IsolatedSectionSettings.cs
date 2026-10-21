namespace Revit26_Plugin_Revised.IsolatedSection.V001.Core.Models
{
    /// <summary>
    /// Window options saved to %AppData%\Revit26_Plugin\IsolatedSection\settings.json.
    /// <see cref="SchemaVersion"/> lets later versions read or migrate older files.
    /// </summary>
    public sealed class IsolatedSectionSettings
    {
        /// <summary>Current settings format.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Format of the file this was read from.</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>Side the view looks at. A picked face is never saved; it falls back to Auto.</summary>
        public SideChoice Side { get; set; } = SideChoice.Auto;

        /// <summary>How other elements are hidden.</summary>
        public IsolationMode Isolation { get; set; } = IsolationMode.Temporary;

        /// <summary>Camera placement.</summary>
        public CameraMode Camera { get; set; } = CameraMode.FacingSide;

        /// <summary>Detail level of the new view.</summary>
        public DetailChoice Detail { get; set; } = DetailChoice.Fine;

        /// <summary>Visual style of the new view.</summary>
        public StyleChoice Style { get; set; } = StyleChoice.Shaded;

        /// <summary>Section box margin left and right of the elements (mm).</summary>
        public double SideMarginMm { get; set; } = 300;

        /// <summary>Section box margin above and below the elements (mm).</summary>
        public double TopBottomMarginMm { get; set; } = 300;

        /// <summary>Section box margin in front of and behind the elements (mm).</summary>
        public double DepthMarginMm { get; set; } = 300;

        /// <summary>View name pattern; tokens {Category} {Id} {Count} {Side} {Date}.</summary>
        public string NamePattern { get; set; } = "ISO-3D-{Category}-{Id}";

        /// <summary>Open the new view and zoom to fit.</summary>
        public bool OpenView { get; set; } = true;

        /// <summary>Name of the last 3D view type used (matched by name next time).</summary>
        public string ViewTypeName { get; set; } = string.Empty;

        /// <summary>Name of the last view template used, or empty for none.</summary>
        public string TemplateName { get; set; } = string.Empty;
    }
}
