using System.Collections.Generic;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Models
{
    /// <summary>
    /// Remembered window options, saved to
    /// %AppData%\Revit26_Plugin_Revised\AnnotationOverlapDetection\V006\settings.json.
    /// On first run V006 reads the V005 file (AnnotationOverlapDetection\settings.json, version 1):
    /// mode and gap carry over, the V006 fields keep their defaults.
    /// </summary>
    public sealed class AnnotationOverlapSettings
    {
        /// <summary>Current file format version (2 = V006 priority fields).</summary>
        public const int CurrentVersion = 2;

        /// <summary>File format version, for future migrations.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>Last used resolve mode (0 Move Up, 1 Move Down, 2 Distribute, 3 By priority).</summary>
        public int Mode { get; set; } = 2;

        /// <summary>Last used clear gap, mm.</summary>
        public double GapMm { get; set; } = 1.0;

        /// <summary>V006: selected preset (0 Architectural, 1 Notes stay, 2 Tags stay, 3 Custom).</summary>
        public int Preset { get; set; } = 0;

        /// <summary>V006: priority order, highest first, as <c>AnnotationCategory</c> names.</summary>
        public List<string> PriorityOrder { get; set; } = new();

        /// <summary>V006: categories marked "Never move", as <c>AnnotationCategory</c> names.</summary>
        public List<string> NeverMove { get; set; } = new();

        /// <summary>V006: same-priority rule (0 Distribute, 1 Move Up, 2 Move Down, 3 Skip).</summary>
        public int Tie { get; set; } = 0;

        /// <summary>V006: also compare annotations of different types (false = V005 behaviour).</summary>
        public bool AcrossTypes { get; set; } = false;
    }
}
