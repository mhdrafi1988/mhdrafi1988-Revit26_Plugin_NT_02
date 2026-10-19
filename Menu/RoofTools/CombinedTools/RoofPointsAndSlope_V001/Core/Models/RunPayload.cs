// =======================================================
// File: RunPayload.cs
// Location: Core/Models/
// A request carried across the ExternalEvent boundary, and its result.
// The payload holds snapshots (settings clone, opening choices), so a run
// never reads the window's live objects.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Core.Models;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Engine;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models
{
    /// <summary>What the handler is asked to do.</summary>
    public enum RunOperation
    {
        /// <summary>Re-read the roof (read-only).</summary>
        Analyze,

        /// <summary>Run the enabled steps.</summary>
        Run
    }

    /// <summary>A request for the handler.</summary>
    public sealed class RunPayload
    {
        /// <summary>Analyze or Run.</summary>
        public RunOperation Operation { get; init; }

        /// <summary>The roof.</summary>
        public ElementId RoofId { get; init; }

        /// <summary>The document the roof lives in.</summary>
        public Document Document { get; init; }

        /// <summary>Run: the roof outline read when the window opened (or at the last Analyze).</summary>
        public RoofOutline Outline { get; init; }

        /// <summary>Run: a copy of the window's settings.</summary>
        public PipelineSettings Settings { get; init; }

        /// <summary>Run: the roof data the ridge step needs (step 5).</summary>
        public RidgeInput RidgeInput { get; init; }

        /// <summary>Run: the openings ticked as drains (ridge seeds, step 5).</summary>
        public List<OpeningModel> DrainOpenings { get; init; }

        /// <summary>Run: the openings to slope towards (step 6).</summary>
        public List<DrainItem> SelectedDrains { get; init; }

        /// <summary>Run: number of openings the drain detection found on the roof.</summary>
        public int DetectedDrainCount { get; init; }

        /// <summary>Called for each log line (on Revit's thread, which is also the window's thread).</summary>
        public Action<LogEntry> Log { get; init; }

        /// <summary>Called with progress 0–100 and a status text.</summary>
        public Action<double, string> Progress { get; init; }

        /// <summary>Called exactly once when the request finishes.</summary>
        public Action<RunResult> OnCompleted { get; init; }
    }

    /// <summary>Outcome of a request.</summary>
    public sealed class RunResult
    {
        /// <summary>True when everything requested succeeded (Run) or the roof was read (Analyze).</summary>
        public bool Success { get; set; }

        /// <summary>Why it failed.</summary>
        public string ErrorMessage { get; set; }

        /// <summary>Analyze: the freshly read roof.</summary>
        public RoofOutline Outline { get; set; }

        /// <summary>Analyze: the freshly read ridge input.</summary>
        public RidgeInput RidgeInput { get; set; }

        /// <summary>Run: ridge detail lines drawn.</summary>
        public int RidgeLines { get; set; }

        /// <summary>Run: points added per step name.</summary>
        public Dictionary<string, int> AddedPerStep { get; } = new Dictionary<string, int>();

        /// <summary>Run: points added over all steps.</summary>
        public int TotalAdded { get; set; }

        /// <summary>Run: points Revit rejected.</summary>
        public int TotalRejected { get; set; }

        /// <summary>Run: whether the slope step ran and succeeded.</summary>
        public bool Sloped { get; set; }

        /// <summary>Run: the slope step's highest point (mm), when it ran.</summary>
        public double HighestElevationMm { get; set; }

        /// <summary>Run: shape points after the run (plan), to refresh the preview.</summary>
        public List<P2> VerticesAfter { get; set; } = new List<P2>();
    }
}
