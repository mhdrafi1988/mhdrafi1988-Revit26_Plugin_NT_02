using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Models
{
    /// <summary>One status, with the colour views show it in. Also the shape saved in the settings file.</summary>
    public sealed class StatusColour
    {
        /// <summary>Status text exactly as in the status file, e.g. "Installed".</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Colour as "#RRGGBB".</summary>
        public string Color { get; set; } = "#808080";
    }

    /// <summary>The latest status row for one Tracker_ID, read from the status file.</summary>
    public sealed class StatusRecord
    {
        /// <summary>Tracker_ID.</summary>
        public string TrackerId { get; set; } = string.Empty;

        /// <summary>Status text, empty when the cell is blank.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Progress 0–100, null when blank or not a number.</summary>
        public double? Progress { get; set; }

        /// <summary>Date of the row, null when blank or not a date.</summary>
        public DateTime? Date { get; set; }

        /// <summary>Row number in the file (1 = header), for messages.</summary>
        public int Row { get; set; }

        /// <summary>The date as written to Tracker_StatusDate (yyyy-MM-dd), empty when none.</summary>
        public string DateText => Date.HasValue ? Date.Value.ToString("yyyy-MM-dd") : string.Empty;
    }

    /// <summary>What reading a status file produced.</summary>
    public sealed class StatusFileResult
    {
        /// <summary>Latest row per Tracker_ID, keyed without regard to case.</summary>
        public Dictionary<string, StatusRecord> Records { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Data rows read (header excluded).</summary>
        public int RowCount { get; set; }

        /// <summary>Rows that had a Tracker_ID.</summary>
        public int RowsWithId { get; set; }

        /// <summary>Problems that did not stop the read (bad numbers, missing optional columns…).</summary>
        public List<string> Warnings { get; } = new();
    }

    /// <summary>What a status import does to one element.</summary>
    public sealed class StatusPlanRow
    {
        /// <summary>The element.</summary>
        public ElementSnapshot Element { get; set; }

        /// <summary>The file's row for its Tracker_ID, null when none.</summary>
        public StatusRecord Record { get; set; }

        /// <summary>The decision.</summary>
        public StatusChange Change { get; set; }
    }

    /// <summary>One finding of the model health check.</summary>
    public sealed class HealthIssue
    {
        /// <summary>What was found.</summary>
        public HealthIssueKind Kind { get; set; }

        /// <summary>The element, null for an ID that is only in the status file.</summary>
        public long? ElementId { get; set; }

        /// <summary>The Tracker_ID involved, empty when the element has none.</summary>
        public string TrackerId { get; set; } = string.Empty;

        /// <summary>Category name.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Category id (0 for an ID only in the status file).</summary>
        public long CategoryId { get; set; }

        /// <summary>Type name (with Type Mark when set).</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Plain-language explanation.</summary>
        public string Detail { get; set; } = string.Empty;
    }
}
