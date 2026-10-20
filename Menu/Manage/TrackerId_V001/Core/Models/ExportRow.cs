namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Models
{
    /// <summary>One row of the Power BI export. Quantities are in metres, square metres and cubic metres.</summary>
    public sealed class ExportRow
    {
        /// <summary>Tracker_ID.</summary>
        public string TrackerId { get; set; } = string.Empty;

        /// <summary>Category name.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Family name.</summary>
        public string Family { get; set; } = string.Empty;

        /// <summary>Type name.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Type Mark.</summary>
        public string TypeMark { get; set; } = string.Empty;

        /// <summary>Tracker_Zone.</summary>
        public string Zone { get; set; } = string.Empty;

        /// <summary>Tracker_Level.</summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>Tracker_Grid.</summary>
        public string Grid { get; set; } = string.Empty;

        /// <summary>Length, null when the element has none.</summary>
        public double? LengthM { get; set; }

        /// <summary>Area, null when the element has none.</summary>
        public double? AreaM2 { get; set; }

        /// <summary>Volume, null when the element has none.</summary>
        public double? VolumeM3 { get; set; }

        /// <summary>Revit element id.</summary>
        public long ElementId { get; set; }

        /// <summary>Revit UniqueId.</summary>
        public string UniqueId { get; set; } = string.Empty;
    }
}
