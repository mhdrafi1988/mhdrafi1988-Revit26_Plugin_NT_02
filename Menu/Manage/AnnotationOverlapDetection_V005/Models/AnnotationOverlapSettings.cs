namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V005.Models
{
    /// <summary>
    /// Remembered window options, saved to
    /// %AppData%\Revit26_Plugin_Revised\AnnotationOverlapDetection\settings.json.
    /// </summary>
    public sealed class AnnotationOverlapSettings
    {
        /// <summary>Current file format version.</summary>
        public const int CurrentVersion = 1;

        /// <summary>File format version, for future migrations.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>Last used resolve mode (0 Move Up, 1 Move Down, 2 Distribute).</summary>
        public int Mode { get; set; } = 2;

        /// <summary>Last used clear gap, mm.</summary>
        public double GapMm { get; set; } = 1.0;
    }
}
