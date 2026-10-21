using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Revit26_Plugin_Revised.TrackerId.V002.Core.Models
{
    /// <summary>
    /// Saved between sessions in %AppData%\Revit26_Plugin\TrackerId\settings.json, so every run in a
    /// project uses the same ID rule. <see cref="SchemaVersion"/> lets a later version migrate the file.
    /// </summary>
    public sealed class TrackerIdSettings
    {
        /// <summary>The version this code writes. 1 = 1.0.0; 2 = 1.1.0 (scope, categories, status, Excel).</summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>Version of this file's shape. A version-1 file loads with defaults for the newer fields.</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

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

        /// <summary>CSV (default, as in 1.0.0) or Excel.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ExportFormat ExportFormat { get; set; } = ExportFormat.Csv;

        /// <summary>Whole model (default, as in 1.0.0), current selection or active view.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ScopeMode Scope { get; set; } = ScopeMode.WholeModel;

        /// <summary>Category ids ticked last time (for the same model they are ticked again).</summary>
        public List<long> CategoryIds { get; set; } = new();

        /// <summary>Last status file read (Excel or CSV).</summary>
        public string StatusFilePath { get; set; } = string.Empty;

        /// <summary>Last status template written.</summary>
        public string StatusTemplatePath { get; set; } = string.Empty;

        /// <summary>Status → colour, in display order. Empty means the defaults.</summary>
        public List<StatusColour> StatusColours { get; set; } = new();

        /// <summary>Writes the whole ID in capitals (naming page). Off = the old behaviour.</summary>
        public bool UpperCase { get; set; }

        /// <summary>True once the first-run guide was closed.</summary>
        public bool GuideSeen { get; set; }

        /// <summary>Type codes from the naming page: type element id → code. Empty = the old behaviour.</summary>
        public Dictionary<long, string> TypeCodes { get; set; } = new();
    }
}
