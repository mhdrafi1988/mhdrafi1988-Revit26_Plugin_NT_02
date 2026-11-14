using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Run;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Report
{
    /// <summary>A roof the run created.</summary>
    /// <param name="RoofId">ElementId of the new roof.</param>
    /// <param name="AreaM2">Its area, m².</param>
    /// <param name="Replaced">True when it replaced a roof from an earlier run.</param>
    public sealed record CreatedRoof(long RoofId, double AreaM2, bool Replaced);

    /// <summary>The Summary sheet.</summary>
    public sealed class ReportSummary
    {
        /// <summary>Run ID, as written to Comments.</summary>
        public string RunId { get; set; }
        /// <summary>When the run started.</summary>
        public DateTime Started { get; set; }
        /// <summary>Revit user name.</summary>
        public string User { get; set; }
        /// <summary>Model title.</summary>
        public string Model { get; set; }
        /// <summary>Plugin version.</summary>
        public string PluginVersion { get; set; }
        /// <summary>Tool version.</summary>
        public string ToolVersion { get; set; }
        /// <summary>Run time, seconds.</summary>
        public double DurationSeconds { get; set; }
        /// <summary>Rooms ticked for the run.</summary>
        public int RoomsTicked { get; set; }
        /// <summary>New roofs.</summary>
        public int RoofsCreated { get; set; }
        /// <summary>Roofs that replaced one from an earlier run.</summary>
        public int RoofsReplaced { get; set; }
        /// <summary>Ticked rooms that got no roof.</summary>
        public int Skipped { get; set; }
        /// <summary>Warnings raised.</summary>
        public int Warnings { get; set; }
        /// <summary>Errors raised.</summary>
        public int Errors { get; set; }
        /// <summary>Settings used, as name / value pairs.</summary>
        public List<KeyValuePair<string, string>> Settings { get; set; } = new();
    }

    /// <summary>One row of the Rooms sheet.</summary>
    public sealed class ReportRoom
    {
        /// <summary>Room ElementId.</summary>
        public long RoomId { get; set; }
        /// <summary>Host or link name.</summary>
        public string Source { get; set; }
        /// <summary>Level name.</summary>
        public string Level { get; set; }
        /// <summary>Room number.</summary>
        public string Number { get; set; }
        /// <summary>Room name.</summary>
        public string Name { get; set; }
        /// <summary>Room area, m².</summary>
        public double RoomAreaM2 { get; set; }
        /// <summary>Keyword that matched.</summary>
        public string Matched { get; set; }
        /// <summary>Slab ElementId; 0 = none.</summary>
        public long SlabId { get; set; }
        /// <summary>Slab top, mm.</summary>
        public double? SlabTopMm { get; set; }
        /// <summary>Roof ElementId; 0 = none.</summary>
        public long RoofId { get; set; }
        /// <summary>Roof area, m².</summary>
        public double? RoofAreaM2 { get; set; }
        /// <summary>Outline area against room area, percent.</summary>
        public double? AreaDiffPct { get; set; }
        /// <summary>Clean, Repaired: …, Approximate, Failed, Not enclosed.</summary>
        public string BoundaryResult { get; set; }
        /// <summary>Created, Replaced, Skipped, Failed or Not ticked.</summary>
        public string Status { get; set; }
        /// <summary>Why it was skipped, or the warnings raised.</summary>
        public string Reason { get; set; }
        /// <summary>Room phase.</summary>
        public string Phase { get; set; }
        /// <summary>Room workset.</summary>
        public string Workset { get; set; }
        /// <summary>Plan group (Part C).</summary>
        public string Group { get; set; }
        /// <summary>Plan scale (Part C).</summary>
        public string Scale { get; set; }
        /// <summary>True when the room was done but something needs checking.</summary>
        public bool HasWarning { get; set; }
    }

    /// <summary>One row of the Boundary repair sheet.</summary>
    public sealed class ReportRepair
    {
        /// <summary>Room ElementId.</summary>
        public long RoomId { get; set; }
        /// <summary>Room number.</summary>
        public string Number { get; set; }
        /// <summary>Room name.</summary>
        public string Name { get; set; }
        /// <summary>Method number.</summary>
        public int Method { get; set; }
        /// <summary>Method name.</summary>
        public string MethodName { get; set; }
        /// <summary>Passed or Failed.</summary>
        public string Result { get; set; }
        /// <summary>What was found or changed.</summary>
        public string Detail { get; set; }
        /// <summary>Position X, mm.</summary>
        public double? X { get; set; }
        /// <summary>Position Y, mm.</summary>
        public double? Y { get; set; }
        /// <summary>Room area, m².</summary>
        public double RoomAreaM2 { get; set; }
        /// <summary>Final outline area, m².</summary>
        public double OutlineAreaM2 { get; set; }
    }

    /// <summary>Everything one run writes to Excel.</summary>
    public sealed class RunReport
    {
        /// <summary>Summary sheet.</summary>
        public ReportSummary Summary { get; private set; } = new();
        /// <summary>Rooms sheet.</summary>
        public List<ReportRoom> Rooms { get; } = new();
        /// <summary>Boundary repair sheet.</summary>
        public List<ReportRepair> Repairs { get; } = new();
        /// <summary>Issues sheet.</summary>
        public List<Issue> Issues { get; } = new();

        /// <summary>
        /// Builds the report. Lists every ticked room and every wet room that was not ticked, every
        /// repair attempted on the ticked rooms, and every issue. Null arguments are read as empty.
        /// </summary>
        public static RunReport Build(
            ReportSummary summary,
            IEnumerable<RoomCandidate> rooms,
            IEnumerable<string> tickedKeys,
            PlannedRun plan,
            IReadOnlyDictionary<string, CreatedRoof> created,
            IEnumerable<Issue> issues,
            WetAreaSettings settings)
        {
            var report = new RunReport { Summary = summary ?? new ReportSummary() };
            var ticked = new HashSet<string>(tickedKeys ?? Enumerable.Empty<string>());
            created ??= new Dictionary<string, CreatedRoof>();
            report.Issues.AddRange((issues ?? plan?.Issues ?? Enumerable.Empty<Issue>()).Where(i => i != null));
            var byRoom = report.Issues.Where(i => i.RoomKey != null).ToLookup(i => i.RoomKey);

            foreach (RoomCandidate c in rooms ?? Enumerable.Empty<RoomCandidate>())
            {
                RoomRow row = c?.Row;
                if (row == null) continue;
                bool isTicked = ticked.Contains(row.RoomKey);
                if (!isTicked && string.IsNullOrEmpty(row.Matched)) continue;

                var item = new ReportRoom
                {
                    RoomId = row.RoomId, Source = row.Source, Level = row.Level, Number = row.Number, Name = row.Name,
                    RoomAreaM2 = row.AreaM2, Matched = row.Matched, SlabId = row.SlabId, SlabTopMm = row.SlabTopMm,
                    BoundaryResult = Boundary(c.Outline), Phase = row.Phase, Workset = row.Workset
                };

                var errors = byRoom[row.RoomKey].Where(i => i.Severity == "Error").Select(i => i.Message).ToList();
                var warnings = byRoom[row.RoomKey].Where(i => i.Severity == "Warning").Select(i => i.Message).ToList();
                if (!isTicked)
                {
                    item.Status = "Not ticked";
                    item.Reason = string.Empty;
                }
                else if (created.TryGetValue(row.RoomKey, out CreatedRoof roof))
                {
                    item.Status = roof.Replaced ? "Replaced" : "Created";
                    item.RoofId = roof.RoofId;
                    item.RoofAreaM2 = roof.AreaM2;
                    item.AreaDiffPct = c.Outline?.AreaDiffPct;
                    item.Reason = string.Join(" ", warnings);
                    item.HasWarning = warnings.Count > 0;
                }
                else
                {
                    item.Status = errors.Count > 0 ? "Skipped" : "Failed";
                    item.Reason = errors.Count > 0 ? string.Join(" ", errors) : "The roof was not created.";
                }
                report.Rooms.Add(item);

                if (!isTicked || c.Outline == null) continue;
                foreach (RepairStep step in c.Outline.Steps)
                    report.Repairs.Add(new ReportRepair
                    {
                        RoomId = row.RoomId, Number = row.Number, Name = row.Name, Method = step.Method, MethodName = step.Name,
                        Result = step.Passed ? "Passed" : "Failed", Detail = step.Detail, X = step.At?.X, Y = step.At?.Y,
                        RoomAreaM2 = row.AreaM2, OutlineAreaM2 = c.Outline.AreaM2
                    });
            }

            ReportSummary s = report.Summary;
            s.RoomsTicked = ticked.Count;
            s.RoofsCreated = created.Values.Count(r => !r.Replaced);
            s.RoofsReplaced = created.Values.Count(r => r.Replaced);
            s.Skipped = report.Rooms.Count(r => r.Status == "Skipped" || r.Status == "Failed");
            s.Warnings = report.Issues.Count(i => i.Severity == "Warning");
            s.Errors = report.Issues.Count(i => i.Severity == "Error");
            if (settings != null && s.Settings.Count == 0) s.Settings.AddRange(Describe(settings));
            return report;
        }

        private static string Boundary(RoomOutline outline)
        {
            if (outline == null) return string.Empty;
            switch (outline.Status)
            {
                case RoomStatus.NotEnclosed: return "Not enclosed";
                case RoomStatus.Failed: return "Failed";
                case RoomStatus.Approximate: return "Approximate";
            }
            var passed = outline.Steps.Where(st => st.Passed).Select(st => st.Name).Distinct().ToList();
            string text = passed.Count > 0 ? "Repaired: " + string.Join(", ", passed) : "Clean";
            if (outline.Status == RoomStatus.AreaWarning)
                text += ", area " + outline.AreaDiffPct.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + "%";
            return text;
        }

        private static IEnumerable<KeyValuePair<string, string>> Describe(WetAreaSettings s)
        {
            string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            yield return new("Wet keywords", string.Join(", ", s.Keywords ?? new List<string>()));
            yield return new("Roof type", s.RoofTypeName);
            yield return new("Workset", s.WorksetName);
            yield return new("Offset above slab (mm)", N(s.OffsetMm));
            yield return new("Snap gaps up to (mm)", N(s.SnapMm));
            yield return new("Bridge gaps up to (mm)", N(s.BridgeMm));
            yield return new("Area check (± %)", N(s.AreaTolerancePct));
            yield return new("Allow approximate outline", s.AllowApproximate ? "Yes" : "No");
            yield return new("Host rooms", s.IncludeHost ? "Yes" : "No");
            yield return new("Linked rooms", !s.IncludeLinks ? "No"
                : s.IncludedLinks == null || s.IncludedLinks.Count == 0 ? "All loaded links" : string.Join(", ", s.IncludedLinks));
        }
    }
}
