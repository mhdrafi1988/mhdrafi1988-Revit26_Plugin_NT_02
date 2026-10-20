using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Models
{
    /// <summary>One segment of the Tracker ID format. Also the shape saved in the settings file.</summary>
    public sealed class SegmentDefinition
    {
        /// <summary>Which segment this is.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SegmentKind Kind { get; set; }

        /// <summary>False leaves the segment out of the ID.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Where the value comes from.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SegmentSource Source { get; set; }

        /// <summary>
        /// Extra setting for the source: a parameter name (zone, level or custom parameter),
        /// fixed text, or the number of digits of a running number.
        /// </summary>
        public string Option { get; set; } = string.Empty;

        /// <summary>Returns a copy.</summary>
        public SegmentDefinition Clone() => new() { Kind = Kind, Enabled = Enabled, Source = Source, Option = Option };
    }

    /// <summary>The full Tracker ID rule: segments in order, separator and fallbacks.</summary>
    public sealed class TrackerFormat
    {
        /// <summary>Segments in the order they appear in the ID (disabled ones included).</summary>
        public List<SegmentDefinition> Segments { get; set; } = new();

        /// <summary>Text between segments; may be empty.</summary>
        public string Separator { get; set; } = "-";

        /// <summary>Zone value used when neither the zone parameter nor a scope box gives one.</summary>
        public string NoZoneText { get; set; } = "ZX";

        /// <summary>Value used for any other segment that has nothing to show (no level, no grid, empty parameter).</summary>
        public string MissingText { get; set; } = "NA";

        /// <summary>Elements further than this from their grid crossing are flagged for checking, metres.</summary>
        public double GridToleranceM { get; set; } = 3.0;

        /// <summary>The definition of <paramref name="kind"/>, or null.</summary>
        public SegmentDefinition Get(SegmentKind kind) => Segments.Find(s => s.Kind == kind);
    }
}
