namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.ViewModels
{
    /// <summary>
    /// Remembered between sessions in %AppData%\Revit26_Plugin_Revised\Base Level to Parameter.
    /// <see cref="Version"/> lets later releases migrate the file. "Overwrite existing values" is not
    /// remembered on purpose: it always starts off.
    /// </summary>
    public class BaseLevelToParameterSettings
    {
        /// <summary>Settings format version.</summary>
        public int Version { get; set; } = 1;

        /// <summary>Shared parameter file last used for a new parameter.</summary>
        public string SharedParamFilePath { get; set; } = string.Empty;

        /// <summary>Group inside the file last used for a new parameter.</summary>
        public string FileGroup { get; set; } = "Data";

        /// <summary>Last target parameter name.</summary>
        public string LastTargetName { get; set; } = string.Empty;
    }
}
