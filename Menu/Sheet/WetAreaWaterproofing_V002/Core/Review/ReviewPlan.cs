using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Run;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Sections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Review
{
    /// <summary>What a row of the Review table is about.</summary>
    public enum ReviewKind
    {
        /// <summary>The room's waterproofing roof.</summary>
        Roof,
        /// <summary>The room's plan.</summary>
        Plan,
        /// <summary>One section, callout or reference on the plan.</summary>
        Section,
        /// <summary>A note about the room (earlier views, nothing to cut, room skipped). Cannot be ticked.</summary>
        Note
    }

    /// <summary>What the run will do with a row.</summary>
    public enum ReviewOutcome
    {
        /// <summary>Something new is made.</summary>
        New,
        /// <summary>Something from an earlier run is deleted and made again.</summary>
        Replaces,
        /// <summary>Something from an earlier run is kept and updated (a plan's crop).</summary>
        Updates,
        /// <summary>Something from an earlier run is kept as it is (a view you drew in).</summary>
        Kept,
        /// <summary>Nothing is made, and that is expected (no drain in the room, limit of 6 reached).</summary>
        Skipped,
        /// <summary>Nothing is made because something is wrong (outline, slab, owner).</summary>
        Problem
    }

    /// <summary>One row of the Review table.</summary>
    /// <param name="Key">Stable key: the run leaves out the rows whose key is unticked. See <see cref="ReviewKeys"/>.</param>
    /// <param name="RoomKey">The room's key.</param>
    /// <param name="Room">"L01 · 101 Toilet · Host", the group header.</param>
    /// <param name="Kind">Roof, plan, section or note.</param>
    /// <param name="Target">Section target label ("Floor drain 2"), or empty.</param>
    /// <param name="CutsThrough">Element cut, the slab for a roof, or empty.</param>
    /// <param name="Mode">Mode list text for sections, or empty.</param>
    /// <param name="PointsTo">Drafting view, view made, roof type or plan name.</param>
    /// <param name="Outcome">What happens.</param>
    /// <param name="Happens">The text shown for <paramref name="Outcome"/>.</param>
    public sealed record ReviewItem(string Key, string RoomKey, string Room, ReviewKind Kind, string Target, string CutsThrough,
        string Mode, string PointsTo, ReviewOutcome Outcome, string Happens)
    {
        /// <summary>True when the row is something the run makes, so it can be unticked.</summary>
        public bool CanTick => Kind != ReviewKind.Note && Outcome is ReviewOutcome.New or ReviewOutcome.Replaces or ReviewOutcome.Updates;
    }

    /// <summary>What an earlier run left in a room, read from the model before the Review.</summary>
    /// <param name="Markers">Tagged section markers and reference callouts that a run deletes and places again.</param>
    /// <param name="Views">Tagged callout and section views: their name and how many elements are drawn in each.</param>
    public sealed record EarlierViews(int Markers, IReadOnlyList<(string Name, int Drawn)> Views)
    {
        /// <summary>Nothing from an earlier run.</summary>
        public static readonly EarlierViews None = new(0, Array.Empty<(string, int)>());
    }

    /// <summary>Keys shared by the Review table and the run, so an unticked row is left out of the run.</summary>
    public static class ReviewKeys
    {
        /// <summary>The room's roof.</summary>
        public static string Roof(string roomKey) => roomKey + "|Roof";

        /// <summary>The room's plan (and with it every section on it).</summary>
        public static string Plan(string roomKey) => roomKey + "|Plan";

        /// <summary>One planned section: target, element cut and label, which are the same in the Review and the run.</summary>
        public static string Section(string roomKey, PlannedSection p)
            => $"{roomKey}|Section|{(int)p.Kind}|{p.CutElementId}|{p.Label}";
    }

    /// <summary>Builds the Review table from the same planning the run uses. No Revit types.</summary>
    public static class ReviewBuilder
    {
        /// <summary>
        /// The rows for <paramref name="ticked"/> rooms, grouped by room in grid order. <paramref name="plan"/> is the
        /// run planner's answer for those rooms; <paramref name="earlier"/> tells what earlier runs left per room key;
        /// <paramref name="draftingViews"/> gives the sheet and bubble of drafting views.
        /// </summary>
        public static IReadOnlyList<ReviewItem> Build(IReadOnlyList<RoomCandidate> ticked, PlannedRun plan, WetAreaSettings s,
            Func<string, EarlierViews> earlier, IReadOnlyList<DraftingViewInfo> draftingViews)
        {
            var rows = new List<ReviewItem>();
            if (ticked == null || plan == null || s == null) return rows;
            var jobs = plan.Jobs.ToDictionary(j => j.RoomKey);
            earlier ??= _ => EarlierViews.None;

            foreach (RoomCandidate c in ticked.Where(c => c?.Row != null))
            {
                string key = c.Row.RoomKey;
                string room = $"{c.Row.Level} · {c.Row.Number} {c.Row.Name} · {c.Row.Source}";

                if (!jobs.TryGetValue(key, out RoofJob job))
                {
                    foreach (Issue issue in plan.Issues.Where(i => i.RoomKey == key && i.Severity == "Error"))
                        rows.Add(Note(key, room, issue.Message, ReviewOutcome.Problem));
                    if (rows.All(r => r.RoomKey != key))
                        rows.Add(Note(key, room, "The room cannot be run.", ReviewOutcome.Problem));
                    continue;
                }

                if (s.CreateRoofs)
                {
                    bool replaces = job.ReplaceRoofIds.Count > 0;
                    rows.Add(new ReviewItem(ReviewKeys.Roof(key), key, room, ReviewKind.Roof, string.Empty, c.Row.SlabText ?? string.Empty,
                        string.Empty, s.RoofTypeName ?? string.Empty,
                        replaces ? ReviewOutcome.Replaces : ReviewOutcome.New,
                        replaces ? (string.IsNullOrEmpty(c.ExistingRunId) ? "Replaces the earlier roof" : $"Replaces roof from run {c.ExistingRunId}") : "New"));
                }

                IReadOnlyList<PlannedSection> planned = Array.Empty<PlannedSection>();
                IReadOnlyList<string> skipped = Array.Empty<string>();
                if (s.CreateSections)
                {
                    SectionPlan sp = SectionPlanner.Plan(new RoomSectionInput(job.Number, job.Outer, c.Inside, c.Items),
                        s.Targets, s.OneDraftingViewForAll, s.SharedDraftingViewName);
                    planned = sp.Sections;
                    skipped = sp.Skipped;
                }

                bool makesPlan = s.CreatePlans || planned.Count > 0 || c.ExistingPlanId != 0;
                if (makesPlan && (s.CreatePlans || s.CreateSections))
                {
                    bool kept = c.ExistingPlanId != 0;
                    string name = kept ? c.ExistingPlanName ?? string.Empty
                        : ViewNames.Fill(s.PlanNamePattern, c.Row.Level, c.Row.Number, c.Row.Name);
                    rows.Add(new ReviewItem(ReviewKeys.Plan(key), key, room, ReviewKind.Plan, string.Empty, string.Empty, string.Empty, name,
                        kept ? ReviewOutcome.Updates : ReviewOutcome.New, kept ? "Kept, crop updated" : "New"));
                }

                if (!s.CreateSections) continue;

                EarlierViews old = earlier(key) ?? EarlierViews.None;
                if (old.Markers > 0)
                    rows.Add(Note(key, room, $"{old.Markers} marker(s) from an earlier run are removed and placed again", ReviewOutcome.Replaces));
                foreach ((string viewName, int drawn) in old.Views)
                    rows.Add(drawn > 0
                        ? Note(key, room, $"Old view kept: \"{viewName}\" has {drawn} item(s) drawn in it", ReviewOutcome.Kept)
                        : Note(key, room, $"Old view \"{viewName}\" is deleted and made again", ReviewOutcome.Replaces));

                foreach (PlannedSection p in planned)
                {
                    string cut = string.IsNullOrEmpty(p.CutName) ? (p.CutElementId == 0 ? "Room / wall" : p.CutCategory) : p.CutName;
                    string to = SectionModes.UsesDraftingView(p.Mode)
                        ? PointsTo(p.DraftingViewName, draftingViews)
                        : p.Mode == SectionMode.Section ? $"New section view: WP Section - {p.Label}" : $"New detail view: WP Callout - {p.Label}";
                    rows.Add(new ReviewItem(ReviewKeys.Section(key, p), key, room, ReviewKind.Section, p.Label, cut,
                        SectionModes.Label(p.Mode), to, ReviewOutcome.New, "New"));
                }
                foreach (string text in skipped)
                    rows.Add(Note(key, room, StripRoom(text, c.Row.Number), ReviewOutcome.Skipped));
            }
            return rows;
        }

        /// <summary>The keys the run must leave out: every row that can be ticked and is in <paramref name="unticked"/>.</summary>
        public static HashSet<string> Excluded(IEnumerable<ReviewItem> rows, ISet<string> unticked)
            => new((rows ?? Enumerable.Empty<ReviewItem>()).Where(r => r.CanTick && unticked != null && unticked.Contains(r.Key)).Select(r => r.Key));

        private static ReviewItem Note(string roomKey, string room, string text, ReviewOutcome outcome)
            => new($"{roomKey}|Note|{text}", roomKey, room, ReviewKind.Note, string.Empty, string.Empty, string.Empty, string.Empty, outcome, text);

        private static string PointsTo(string name, IReadOnlyList<DraftingViewInfo> views)
        {
            DraftingViewInfo v = DraftingViewSearch.FindByName(views, name);
            return v == null ? $"{name} (new drafting view)" : v.IsOnSheet ? $"{v.Name} · {v.Reference}" : v.Name;
        }

        private static string StripRoom(string text, string number)
        {
            string prefix = $"Room {number}: ";
            return text != null && text.StartsWith(prefix, StringComparison.Ordinal) ? text.Substring(prefix.Length) : text ?? string.Empty;
        }
    }
}
