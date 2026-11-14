// Merge Roofs V001 — user settings (stored by MergeSettingsStore, versioned JSON).
namespace Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models
{
    /// <summary>User-adjustable settings for the Merge Roofs window.</summary>
    public sealed class MergeSettings
    {
        /// <summary>Millimetres per Revit foot.</summary>
        public const double MmPerFoot = 304.8;
        /// <summary>Settings file format version.</summary>
        public int Version { get; set; } = 1;
        /// <summary>Gap tolerance in millimetres (default 5).</summary>
        public double ToleranceMm { get; set; } = 5.0;
        /// <summary>Draw connecting lines across snapped gaps.</summary>
        public bool PlaceGapLinks { get; set; }
        /// <summary>Tolerance in feet.</summary>
        public double ToleranceFeet => ToleranceMm / MmPerFoot;
    }
}
