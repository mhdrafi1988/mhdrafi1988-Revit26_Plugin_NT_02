using System.Collections.Generic;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V007.Models
{
    /// <summary>
    /// Remembered window options, saved to
    /// %AppData%\Revit26_Plugin_Revised\AnnotationOverlapDetection\V007\settings.json.
    /// On first run V007 reads the V006 file (AnnotationOverlapDetection\V006\settings.json, version 2),
    /// else the V005 file (AnnotationOverlapDetection\settings.json, version 1); missing fields keep
    /// their defaults. Version 3 (V007) adds <see cref="AvoidOthers"/> and <see cref="NeighbourToleranceMm"/>;
    /// a version 2 file loads with their defaults (off, 1 mm), which is the V006 behaviour.
    /// </summary>
    public sealed class AnnotationOverlapSettings
    {
        /// <summary>Current file format version (2 = V006 priority fields, 3 = neighbour clearance).</summary>
        public const int CurrentVersion = 3;

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

        /// <summary>V007: keep moved annotations clear of all other annotations (false = V006 behaviour).</summary>
        public bool AvoidOthers { get; set; } = false;

        /// <summary>V007: least space kept from neighbouring annotations, mm.</summary>
        public double NeighbourToleranceMm { get; set; } = 1.0;
    }
}
