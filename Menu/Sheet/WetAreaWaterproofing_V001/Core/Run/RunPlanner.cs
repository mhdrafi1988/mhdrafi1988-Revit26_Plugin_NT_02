using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Run
{
    /// <summary>A room read from the model, ready to plan a roof for.</summary>
    public sealed class RoomCandidate
    {
        /// <summary>Grid data.</summary>
        public RoomRow Row { get; set; }

        /// <summary>The outline and how it was obtained.</summary>
        public RoomOutline Outline { get; set; }

        /// <summary>Host level the roof is created on; 0 when the host has no level.</summary>
        public long HostLevelId { get; set; }

        /// <summary>Elevation of that host level, mm.</summary>
        public double HostLevelElevationMm { get; set; }

        /// <summary>Roofs an earlier run made for this room.</summary>
        public IReadOnlyList<long> ExistingRoofIds { get; set; } = Array.Empty<long>();

        /// <summary>Run ID of the newest of those roofs; null when there is none.</summary>
        public string ExistingRunId { get; set; }
    }

    /// <summary>One roof to create.</summary>
    /// <param name="RoomKey">Room the roof is for.</param>
    /// <param name="Number">Room number, for messages.</param>
    /// <param name="Name">Room name, for messages.</param>
    /// <param name="Outer">Outer outline, mm, host coordinates.</param>
    /// <param name="Inner">Holes.</param>
    /// <param name="HostLevelId">Host level the roof sits on.</param>
    /// <param name="BaseOffsetFromLevelMm">Roof base above that level, mm.</param>
    /// <param name="ReplaceRoofIds">Roofs from an earlier run to delete first.</param>
    /// <param name="CommentsTag">Text for the roof's Comments.</param>
    public sealed record RoofJob(
        string RoomKey, string Number, string Name,
        IReadOnlyList<Pt> Outer, IReadOnlyList<IReadOnlyList<Pt>> Inner,
        long HostLevelId, double BaseOffsetFromLevelMm,
        IReadOnlyList<long> ReplaceRoofIds, string CommentsTag);

    /// <summary>Something the user should know about a room.</summary>
    /// <param name="Severity">Info, Warning or Error (Error = the room was skipped).</param>
    /// <param name="Step">Roof, Boundary, Slab, Report…</param>
    /// <param name="RoomKey">Room concerned; may be null for run-wide issues.</param>
    /// <param name="Number">Room number.</param>
    /// <param name="Message">What happened.</param>
    /// <param name="Fix">What the user can do about it.</param>
    public sealed record Issue(string Severity, string Step, string RoomKey, string Number, string Message, string Fix);

    /// <summary>What a run will do.</summary>
    public sealed record PlannedRun(string RunId, IReadOnlyList<RoofJob> Jobs, IReadOnlyList<Issue> Issues);

    /// <summary>Decides, room by room, whether a roof is created and at what height.</summary>
    public static class RunPlanner
    {
        /// <summary>
        /// Plans the roofs for the ticked rooms. Rooms owned by another user, without a usable
        /// outline or without a host level are skipped with an Error issue. Repaired, approximate
        /// and out-of-tolerance outlines still get a roof, with a Warning. A roof from an earlier
        /// run is replaced.
        /// </summary>
        public static PlannedRun Plan(IEnumerable<RoomCandidate> ticked, WetAreaSettings s, string runId)
        {
            s ??= new WetAreaSettings();
            var jobs = new List<RoofJob>();
            var issues = new List<Issue>();

            foreach (RoomCandidate c in ticked ?? Enumerable.Empty<RoomCandidate>())
            {
                RoomRow row = c?.Row;
                if (row == null) continue;
                RoomOutline outline = c.Outline;

                if (!string.IsNullOrEmpty(row.Owner))
                {
                    issues.Add(new Issue("Error", "Roof", row.RoomKey, row.Number,
                        $"Owned by {row.Owner}: room skipped.", "Ask the owner to synchronise and relinquish, then run this room again."));
                    continue;
                }
                if (outline == null || outline.Status == RoomStatus.NotEnclosed || outline.Status == RoomStatus.Failed
                    || outline.Outer == null || outline.Outer.Count < 3)
                {
                    string why = outline?.Status == RoomStatus.NotEnclosed
                        ? "The room is not placed or not enclosed"
                        : "No closed outline could be built" + LastFailure(outline);
                    issues.Add(new Issue("Error", "Boundary", row.RoomKey, row.Number, why + ": room skipped.",
                        "Close the room's boundary in the model, or tick 'Allow approximate outline'."));
                    continue;
                }
                if (c.HostLevelId == 0)
                {
                    issues.Add(new Issue("Error", "Roof", row.RoomKey, row.Number,
                        "The host model has no level to put the roof on: room skipped.", "Add a level to the host model."));
                    continue;
                }

                double target;
                if (row.SlabTopMm.HasValue) target = row.SlabTopMm.Value + s.OffsetMm;
                else
                {
                    target = row.LevelElevationMm + s.OffsetMm;
                    issues.Add(new Issue("Warning", "Slab", row.RoomKey, row.Number,
                        $"No slab found under the room: roof placed at level {row.Level} + {Num(s.OffsetMm)} mm.",
                        "Check the roof height."));
                }

                foreach (RepairStep step in outline.Steps.Where(st => st.Passed))
                    issues.Add(new Issue("Warning", "Boundary", row.RoomKey, row.Number,
                        $"Outline repaired ({step.Name}): {step.Detail}.", "Check the roof outline."));

                if (outline.Status == RoomStatus.AreaWarning || (outline.Status == RoomStatus.Approximate
                    && Math.Abs(outline.AreaDiffPct) > s.AreaTolerancePct))
                    issues.Add(new Issue("Warning", "Boundary", row.RoomKey, row.Number,
                        $"Roof area differs from the room area by {Signed(outline.AreaDiffPct)}% (limit ± {Num(s.AreaTolerancePct)}%).",
                        "Check the roof outline against the room."));

                var replace = (c.ExistingRoofIds ?? Array.Empty<long>()).ToList();
                if (replace.Count > 0)
                    issues.Add(new Issue("Info", "Roof", row.RoomKey, row.Number,
                        "A roof from an earlier run was replaced.", string.Empty));

                jobs.Add(new RoofJob(row.RoomKey, row.Number, row.Name, outline.Outer, outline.Inner,
                    c.HostLevelId, target - c.HostLevelElevationMm, replace,
                    RunTag.Make(runId, row.RoomKey, s.WriteComments ? row.Number : string.Empty, s.WriteComments ? row.Name : string.Empty)));
            }
            return new PlannedRun(runId, jobs, issues);
        }

        private static string LastFailure(RoomOutline outline)
        {
            RepairStep last = outline?.Steps?.LastOrDefault(st => !st.Passed);
            return last == null ? string.Empty : $" ({last.Name}: {last.Detail})";
        }

        private static string Num(double value) => Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);

        private static string Signed(double value) => Math.Round(value, 1).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
    }
}
