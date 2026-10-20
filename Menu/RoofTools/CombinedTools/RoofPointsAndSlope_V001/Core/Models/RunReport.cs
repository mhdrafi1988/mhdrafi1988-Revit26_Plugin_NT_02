// =======================================================
// File: RunReport.cs
// Location: Core/Models/
// What the optional run report (.xlsx) holds: a summary sheet (key /
// value lines) and one row per shape point after the run.
// =======================================================

using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models
{
    /// <summary>One shape point after the run.</summary>
    public sealed class ReportPoint
    {
        /// <summary>The step that added it, "Existing" (there before the run) or "Slope step" (added by step 6).</summary>
        public string Step { get; init; } = string.Empty;

        /// <summary>Plan X in metres (Revit internal origin).</summary>
        public double XMeters { get; init; }

        /// <summary>Plan Y in metres (Revit internal origin).</summary>
        public double YMeters { get; init; }

        /// <summary>Height above the roof's lowest shape point, in mm.</summary>
        public double HeightMm { get; init; }
    }

    /// <summary>The whole report.</summary>
    public sealed class RunReport
    {
        /// <summary>Report format version (written on the summary sheet).</summary>
        public const int FormatVersion = 1;

        /// <summary>"Roof name · Id 123".</summary>
        public string Roof { get; init; } = string.Empty;

        /// <summary>When the run finished.</summary>
        public DateTime Date { get; init; } = DateTime.Now;

        /// <summary>Summary lines (label, value), in order.</summary>
        public List<(string Label, string Value)> Summary { get; } = new();

        /// <summary>Every shape point after the run.</summary>
        public List<ReportPoint> Points { get; } = new();
    }
}
