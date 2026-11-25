using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Grid;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Run;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Sections;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Report
{
    /// <summary>A roof the run created.</summary>
    /// <param name="RoofId">ElementId of the new roof.</param>
    /// <param name="AreaM2">Its area, m².</param>
    /// <param name="Replaced">True when it replaced a roof from an earlier run.</param>
    public sealed record CreatedRoof(long RoofId, double AreaM2, bool Replaced);

    /// <summary>A section the run placed (reference section or callout).</summary>
    /// <param name="Plan">What was planned: target, cut line, element cut.</param>
    /// <param name="MarkerId">ElementId of the section marker.</param>
    /// <param name="DraftingViewId">ElementId of the drafting view it points to (0 for callouts).</param>
    /// <param name="DraftingViewName">That view's name as it is in the model (empty for callouts).</param>
    /// <param name="IsCallout">True when this is a callout, not a reference section.</param>
    /// <param name="CalloutViewId">ElementId of the callout detail view (0 for reference sections).</param>
    /// <param name="CalloutViewName">The callout view's name (empty for reference sections).</param>
    public sealed record CreatedSection(PlannedSection Plan, long MarkerId, long DraftingViewId, string DraftingViewName,
        bool IsCallout = false, long CalloutViewId = 0, string CalloutViewName = "");

    /// <summary>The plan and sections a run made (or reused) for one room.</summary>
    /// <param name="PlanId">ElementId of the room's plan; 0 when there is none.</param>
    /// <param name="PlanName">The plan's name.</param>
    /// <param name="PlanIsNew">True when this run created the plan; false when it reused one.</param>
    /// <param name="Sections">Sections placed on it.</param>
    public sealed record CreatedViews(long PlanId, string PlanName, bool PlanIsNew, IReadOnlyList<CreatedSection> Sections);

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
        /// <summary>Plans this run created.</summary>
        public int PlansCreated { get; set; }
        /// <summary>Reference sections this run placed.</summary>
        public int SectionsPlaced { get; set; }
        /// <summary>Callout detail views this run created.</summary>
        public int CalloutsCreated { get; set; }
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
        /// <summary>Slab top, mm, as a project elevation — the figure Revit's UI shows for the slab.</summary>
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
        /// <summary>ElementId of the room's plan; 0 = none.</summary>
        public long PlanViewId { get; set; }
        /// <summary>Name of the room's plan.</summary>
        public string PlanView { get; set; }
        /// <summary>Reference sections placed for the room.</summary>
        public int SectionCount { get; set; }
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

    /// <summary>One row of the Sections sheet.</summary>
    public sealed class ReportSection
    {
        /// <summary>ElementId of the section marker.</summary>
        public long MarkerId { get; set; }
        /// <summary>Section name, e.g. "Floor drain 2".</summary>
        public string Label { get; set; }
        /// <summary>Target kind text.</summary>
        public string Target { get; set; }
        /// <summary>Room ElementId.</summary>
        public long RoomId { get; set; }
        /// <summary>Room number.</summary>
        public string Number { get; set; }
        /// <summary>Room name.</summary>
        public string Name { get; set; }
        /// <summary>ElementId of the element cut; 0 = none.</summary>
        public long CutElementId { get; set; }
        /// <summary>Its category.</summary>
        public string CutCategory { get; set; }
        /// <summary>Its "Family : Type".</summary>
        public string CutName { get; set; }
        /// <summary>Model it is in.</summary>
        public string CutModel { get; set; }
        /// <summary>ElementId of the plan the marker is on.</summary>
        public long PlanViewId { get; set; }
        /// <summary>That plan's name.</summary>
        public string PlanView { get; set; }
        /// <summary>ElementId of the drafting view referenced.</summary>
        public long DraftingViewId { get; set; }
        /// <summary>Its name.</summary>
        public string DraftingView { get; set; }
        /// <summary>Head of the cut line, mm.</summary>
        public double HeadX { get; set; }
        /// <summary>Head of the cut line, mm.</summary>
        public double HeadY { get; set; }
        /// <summary>Tail of the cut line, mm.</summary>
        public double TailX { get; set; }
        /// <summary>Tail of the cut line, mm.</summary>
        public double TailY { get; set; }
        /// <summary>Whether this is a callout instead of a reference section.</summary>
        public bool IsCallout { get; set; }
        /// <summary>ElementId of the callout detail view; 0 when it is a reference section.</summary>
        public long CalloutViewId { get; set; }
        /// <summary>Name of the callout detail view.</summary>
        public string CalloutView { get; set; }
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
        /// <summary>Sections sheet.</summary>
        public List<ReportSection> Sections { get; } = new();
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
            WetAreaSettings settings,
            IReadOnlyDictionary<string, CreatedViews> views = null)
        {
            views ??= new Dictionary<string, CreatedViews>();
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
                    RoomAreaM2 = row.AreaM2, Matched = row.Matched, SlabId = row.SlabId, SlabTopMm = row.SlabTopMm + row.ProjectBaseOffsetMm,
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
                else if (views.ContainsKey(row.RoomKey))
                {
                    item.Status = "Views only"; // plan or sections made, roof step off or roof failed
                    item.Reason = string.Join(" ", errors.Concat(warnings));
                    item.HasWarning = errors.Count + warnings.Count > 0;
                }
                else
                {
                    item.Status = errors.Count > 0 ? "Skipped" : "Failed";
                    item.Reason = errors.Count > 0 ? string.Join(" ", errors) : "Nothing was created for the room.";
                }
                if (isTicked && views.TryGetValue(row.RoomKey, out CreatedViews made))
                {
                    item.PlanViewId = made.PlanId;
                    item.PlanView = made.PlanName;
                    item.SectionCount = made.Sections.Count;
                    foreach (CreatedSection section in made.Sections)
                        report.Sections.Add(new ReportSection
                        {
                            MarkerId = section.MarkerId, Label = section.Plan.Label, Target = SectionTargets.Label(section.Plan.Kind),
                            RoomId = row.RoomId, Number = row.Number, Name = row.Name,
                            CutElementId = section.Plan.CutElementId, CutCategory = section.Plan.CutCategory,
                            CutName = section.Plan.CutName, CutModel = section.Plan.CutModel,
                            PlanViewId = made.PlanId, PlanView = made.PlanName,
                            DraftingViewId = section.DraftingViewId, DraftingView = section.DraftingViewName,
                            HeadX = section.Plan.Head.X, HeadY = section.Plan.Head.Y, TailX = section.Plan.Tail.X, TailY = section.Plan.Tail.Y,
                            IsCallout = section.IsCallout, CalloutViewId = section.CalloutViewId, CalloutView = section.CalloutViewName
                        });
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
            s.PlansCreated = views.Values.Count(v => v.PlanIsNew && v.PlanId != 0);
            s.SectionsPlaced = report.Sections.Count;
            s.CalloutsCreated = report.Sections.Count(x => x.IsCallout);
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
            yield return new("Run steps", string.Join(", ", new[] { s.CreateRoofs ? "Roofs" : null, s.CreatePlans ? "Plans" : null, s.CreateSections ? "Reference sections" : null }.Where(x => x != null)));
            yield return new("Plan type", s.PlanViewTypeName);
            yield return new("Plan template", s.PlanTemplateName);
            yield return new("Plan scale", "1:" + s.PlanScale);
            yield return new("Crop margin (mm)", N(s.CropMarginMm));
            yield return new("Section targets", string.Join(", ", (s.Targets ?? new List<SectionTargetSetting>()).Where(t => t.On)
                .Select(t => SectionTargets.Label(t.Kind) + (t.AllMatches ? " (all)" : " (one)"))));
            yield return new("Drafting view", s.OneDraftingViewForAll ? s.SharedDraftingViewName : "one per target");
            yield return new("Host rooms", s.IncludeHost ? "Yes" : "No");
            yield return new("Linked rooms", !s.IncludeLinks ? "No"
                : s.IncludedLinks == null || s.IncludedLinks.Count == 0 ? "All loaded links" : string.Join(", ", s.IncludedLinks));
        }
    }
}
