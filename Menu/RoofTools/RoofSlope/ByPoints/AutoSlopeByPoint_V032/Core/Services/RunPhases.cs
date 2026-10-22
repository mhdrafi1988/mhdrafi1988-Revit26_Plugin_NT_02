// =======================================================
// File: RunPhases.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Core.Services
// New in V032 (V030 P4). Each phase owns a slice of 0–100 so the bar only
// ever moves forward. Weights reflect where the time goes on a large roof:
// the path graph dominates.
// =======================================================

using System;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Core.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Core.Services
{
    public enum RunPhase
    {
        Checking,
        ResettingPoints,
        InsertingArcPoints,
        AddingDrainPoints,
        ResolvingDrains,
        BuildingGraph,
        ComputingPaths,
        ApplyingElevations,
        PlacingMarkers,
        ReadingBack,
        WritingParameters,
        Exporting
    }

    public static class RunPhases
    {
        private static readonly (RunPhase Phase, string Label, double Start, double End)[] Table =
        {
            (RunPhase.Checking,           "Checking roof",               0,  2),
            (RunPhase.ResettingPoints,    "Resetting roof shape points", 2,  5),
            (RunPhase.InsertingArcPoints, "Inserting arc points",        5,  8),
            (RunPhase.AddingDrainPoints,  "Adding drain points",         8,  9),
            (RunPhase.ResolvingDrains,    "Resolving drains",            9, 10),
            (RunPhase.BuildingGraph,      "Building path graph",        10, 74),
            (RunPhase.ComputingPaths,     "Computing shortest paths",   74, 78),
            (RunPhase.ApplyingElevations, "Applying elevations",        78, 90),
            (RunPhase.PlacingMarkers,     "Placing circle markers",     90, 93),
            (RunPhase.ReadingBack,        "Reading back elevations",    93, 95),
            (RunPhase.WritingParameters,  "Writing roof parameters",    95, 97),
            (RunPhase.Exporting,          "Exporting Excel",            97, 100),
        };

        /// <summary>Progress snapshot for a phase, optionally part-way through it (0–100).</summary>
        public static RunProgressInfo Report(RunPhase phase, double? percentWithinPhase = null, int? itemsTotal = null)
        {
            foreach (var row in Table)
            {
                if (row.Phase != phase) continue;
                double within = Math.Max(0, Math.Min(100, percentWithinPhase ?? 0));
                double overall = row.Start + (row.End - row.Start) * within / 100.0;
                int? done = itemsTotal.HasValue ? (int)Math.Round(within * itemsTotal.Value / 100.0) : null;
                return new RunProgressInfo(row.Label, overall, percentWithinPhase, done, itemsTotal);
            }
            return new RunProgressInfo(phase.ToString(), 0, percentWithinPhase);
        }
    }
}
