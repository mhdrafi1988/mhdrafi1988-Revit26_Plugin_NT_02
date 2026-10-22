using System.Collections.Generic;

namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Models
{
    /// <summary>
    /// Saved between sessions in %AppData%\Revit26_Plugin_Revised\TrackerId\settings.json, so every run in a
    /// project uses the same ID rule. <see cref="SchemaVersion"/> lets a later version migrate the file.
    /// </summary>
    public sealed class TrackerIdSettings
    {
        /// <summary>Version of this file's shape.</summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>Segments in ID order.</summary>
        public List<SegmentDefinition> Segments { get; set; } = new();

        /// <summary>Text between segments.</summary>
        public string Separator { get; set; } = "-";

        /// <summary>Zone value when none is found.</summary>
        public string NoZoneText { get; set; } = "ZX";

        /// <summary>Value for any other segment with nothing to show.</summary>
        public string MissingText { get; set; } = "NA";

        /// <summary>Grid distance that flags an element for checking, metres.</summary>
        public double GridToleranceM { get; set; } = 3.0;

        /// <summary>Write Tracker_Zone, Tracker_Level and Tracker_Grid on every run.</summary>
        public bool WriteLocationParameters { get; set; } = true;

        /// <summary>Write Tracker_SourceGuid, which lets the tool spot copied elements.</summary>
        public bool WriteSourceGuid { get; set; } = true;

        /// <summary>Last CSV path used for the Power BI export.</summary>
        public string ExportPath { get; set; } = string.Empty;

        /// <summary>Export every element with a Tracker_ID (true) or only the chosen types (false).</summary>
        public bool ExportWholeModel { get; set; } = true;

        /// <summary>Also save a copy of the export with today's date in its name.</summary>
        public bool ExportDatedCopy { get; set; }
    }
}
