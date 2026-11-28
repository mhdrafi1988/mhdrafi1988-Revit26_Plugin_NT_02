using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Grid;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Review;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Run;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Sections;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace WetAreaWaterproofingV003.Tests
{
    public class ReviewBuilderTests
    {
        // Sample data only.
        private static readonly List<Pt> Square = new() { new(0, 0), new(3000, 0), new(3000, 3000), new(0, 3000) };

        private static RoomCandidate Room(string key, string number, params FoundItem[] items) => new()
        {
            Row = new RoomRow { RoomKey = key, Level = "L01", Number = number, Name = "Toilet", Source = "Host", SlabText = "Floor · +3.150" },
            Inside = new Pt(1500, 1500),
            Items = items
        };

        private static RoofJob Job(string key, string number, params long[] replace)
            => new(key, number, "Toilet", Square, new List<IReadOnlyList<Pt>>(), 1, 0, replace, "WP|x");

        private static WetAreaSettings Settings(params SectionTargetKind[] on)
        {
            var s = new WetAreaSettings { CreateRoofs = true, CreatePlans = true, CreateSections = true, RoofTypeName = "WP Membrane" }.Normalize();
            s.Targets = SectionTargets.Defaults(); // V002 targets, as these tests were written for them
            s.OneDraftingViewForAll = true;
            foreach (SectionTargetSetting t in s.Targets) t.On = on.Contains(t.Kind);
            return s;
        }

        private static readonly FoundItem Drain = new(SectionTargetKind.Drain, new Pt(1500, 1500), 0, 7, "Host", "Plumbing Fixtures", "FD : 100");

        [Fact]
        public void A_room_gets_a_roof_a_plan_and_one_row_per_section()
        {
            var room = Room("H:1", "101", Drain);
            var plan = new PlannedRun("p", new[] { Job("H:1", "101") }, Array.Empty<Issue>());
            var rows = ReviewBuilder.Build(new[] { room }, plan, Settings(SectionTargetKind.Drain), null, null);

            Assert.Equal(new[] { ReviewKind.Roof, ReviewKind.Plan, ReviewKind.Section }, rows.Select(r => r.Kind));
            Assert.All(rows, r => Assert.Equal(ReviewOutcome.New, r.Outcome));
            Assert.Equal("L01 · 101 Toilet · Host", rows[0].Room);
            Assert.Equal("WP Membrane", rows[0].PointsTo);
            Assert.Contains("new drafting view", rows[2].PointsTo);
        }

        [Fact]
        public void Section_keys_match_the_ones_the_run_computes()
        {
            var room = Room("H:1", "101", Drain);
            var s = Settings(SectionTargetKind.Drain);
            var plan = new PlannedRun("p", new[] { Job("H:1", "101") }, Array.Empty<Issue>());
            ReviewItem row = ReviewBuilder.Build(new[] { room }, plan, s, null, null).Single(r => r.Kind == ReviewKind.Section);

            PlannedSection again = SectionPlanner.Plan(new RoomSectionInput("101", Square, room.Inside, room.Items), s.Targets,
                s.OneDraftingViewForAll, s.SharedDraftingViewName).Sections.Single();
            Assert.Equal(ReviewKeys.Section("H:1", again), row.Key);
        }

        [Fact]
        public void Earlier_roof_and_plan_show_as_replaced_and_updated()
        {
            var room = Room("H:1", "101");
            room.ExistingRunId = "20261124-101500";
            room.ExistingPlanId = 55;
            room.ExistingPlanName = "WP Plan - L01 - 101";
            var plan = new PlannedRun("p", new[] { Job("H:1", "101", 99) }, Array.Empty<Issue>());
            var rows = ReviewBuilder.Build(new[] { room }, plan, Settings(), null, null);

            Assert.Equal(ReviewOutcome.Replaces, rows[0].Outcome);
            Assert.Contains("20261124-101500", rows[0].Happens);
            Assert.Equal(ReviewOutcome.Updates, rows[1].Outcome);
            Assert.Equal("WP Plan - L01 - 101", rows[1].PointsTo);
        }

        [Fact]
        public void A_target_with_nothing_to_cut_is_a_skipped_note_that_cannot_be_ticked()
        {
            var room = Room("H:1", "101"); // no door
            var plan = new PlannedRun("p", new[] { Job("H:1", "101") }, Array.Empty<Issue>());
            ReviewItem note = ReviewBuilder.Build(new[] { room }, plan, Settings(SectionTargetKind.Door), null, null)
                .Single(r => r.Kind == ReviewKind.Note);
            Assert.Equal(ReviewOutcome.Skipped, note.Outcome);
            Assert.False(note.CanTick);
            Assert.DoesNotContain("Room 101:", note.Happens);
        }

        [Fact]
        public void A_room_the_planner_refused_is_one_problem_note()
        {
            var room = Room("H:1", "101");
            var plan = new PlannedRun("p", Array.Empty<RoofJob>(),
                new[] { new Issue("Error", "Boundary", "H:1", "101", "No closed outline: room skipped.", "") });
            var rows = ReviewBuilder.Build(new[] { room }, plan, Settings(), null, null);
            ReviewItem only = Assert.Single(rows);
            Assert.Equal(ReviewOutcome.Problem, only.Outcome);
            Assert.False(only.CanTick);
        }

        [Fact]
        public void Earlier_views_are_listed_kept_or_replaced()
        {
            var room = Room("H:1", "101", Drain);
            var plan = new PlannedRun("p", new[] { Job("H:1", "101") }, Array.Empty<Issue>());
            var earlier = new EarlierViews(2, new List<(string, int)> { ("WP Callout - WC / closet", 4), ("WP Section - Floor drain", 0) });
            var rows = ReviewBuilder.Build(new[] { room }, plan, Settings(SectionTargetKind.Drain), _ => earlier, null);

            Assert.Contains(rows, r => r.Outcome == ReviewOutcome.Kept && r.Happens.Contains("WP Callout - WC / closet"));
            Assert.Contains(rows, r => r.Outcome == ReviewOutcome.Replaces && r.Happens.Contains("WP Section - Floor drain"));
            Assert.Contains(rows, r => r.Happens.StartsWith("2 marker(s)"));
        }

        [Fact]
        public void Only_tickable_unticked_rows_are_excluded()
        {
            var room = Room("H:1", "101");
            var plan = new PlannedRun("p", new[] { Job("H:1", "101") }, Array.Empty<Issue>());
            var rows = ReviewBuilder.Build(new[] { room }, plan, Settings(SectionTargetKind.Door), null, null);
            var unticked = new HashSet<string>(rows.Select(r => r.Key));
            HashSet<string> excluded = ReviewBuilder.Excluded(rows, unticked);
            Assert.Equal(new[] { ReviewKeys.Roof("H:1"), ReviewKeys.Plan("H:1") }.OrderBy(k => k), excluded.OrderBy(k => k));
        }

        [Fact]
        public void Drafting_view_on_a_sheet_shows_its_bubble()
        {
            var room = Room("H:1", "101", Drain);
            var plan = new PlannedRun("p", new[] { Job("H:1", "101") }, Array.Empty<Issue>());
            var views = new List<DraftingViewInfo> { new(1, "WP Detail - Floor Drain", "", "A-501", "Wet area details", "3") };
            WetAreaSettings s = Settings(SectionTargetKind.Drain);
            s.OneDraftingViewForAll = false; // each target points to its own view
            ReviewItem row = ReviewBuilder.Build(new[] { room }, plan, s, null, views)
                .Single(r => r.Kind == ReviewKind.Section);
            Assert.Equal("WP Detail - Floor Drain · A-501 / 3", row.PointsTo);
        }
    }
}
