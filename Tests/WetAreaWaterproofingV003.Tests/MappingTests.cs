using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Mapping;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Sections;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace WetAreaWaterproofingV003.Tests
{
    /// <summary>Tool 3.0.0: the Mapping table, its migration from V002 settings, and how the planner uses it.</summary>
    public class MappingTests
    {
        private static readonly List<Pt> Room = new() { new(0, 0), new(4000, 0), new(4000, 3000), new(0, 3000) };

        private static WetAreaSettings V002File(bool oneForAll = true) => new()
        {
            Version = 1, Mappings = null, OneDraftingViewForAll = oneForAll, SharedDraftingViewName = "WP Detail - Typical Wet Area",
            Targets = new List<SectionTargetSetting>
            {
                new() { Kind = SectionTargetKind.FullRoomH, On = true, LengthMm = 400 },
                new() { Kind = SectionTargetKind.FullRoomV, On = true, LengthMm = 400 },
                new() { Kind = SectionTargetKind.Door, On = true, LengthMm = 1600, Mode = SectionMode.Callout, DraftingViewName = "Door view" },
                new() { Kind = SectionTargetKind.Drain, On = false, AllMatches = true, DraftingViewName = "Drain view" },
                new() { Kind = SectionTargetKind.Wc, On = true, DraftingViewName = "WC view" }
            },
            DrainKeywords = new() { "gully" }, WcKeywords = new() { "pan" }, ShowerKeywords = new() { "tray" }
        };

        [Fact]
        public void V002FileIsMigratedRowByRow()
        {
            var s = V002File(oneForAll: false).Normalize();
            MappingRule Row(string id) => s.Mappings.Single(m => m.Id == id);

            Assert.Equal(2, s.Version);
            Assert.True(Row("door").On);
            Assert.Equal(SectionMode.Callout, Row("door").Mode);
            Assert.Equal(1600, Row("door").LengthMm);
            Assert.Equal("Door view", Row("door").DraftingViewName);
            Assert.False(Row("drain").On);
            Assert.True(Row("drain").AllMatches);
            Assert.Equal(new[] { "gully" }, Row("drain").Keywords);
            Assert.Equal(new[] { "pan" }, Row("wc").Keywords);
            Assert.Equal(MappingCut.FullRoomHV, Row("room").Cut);
            Assert.Equal(400, Row("room").LengthMm);
            Assert.False(Row("basin").On);       // new in 3.0.0
            Assert.False(Row("wallup").On);      // not in the old file
        }

        [Fact]
        public void SharedDraftingViewIsCopiedIntoEveryRow()
        {
            var s = V002File(oneForAll: true).Normalize();
            Assert.Equal("WP Detail - Typical Wet Area", s.Mappings.Single(m => m.Id == "wc").DraftingViewName);
            Assert.False(s.OneDraftingViewForAll);
        }

        [Fact]
        public void AV003FileKeepsItsTable()
        {
            var rows = new List<MappingRule> { new() { Id = "x", Name = "Floor finish", Category = "Floors", Cut = MappingCut.ThroughElement } };
            var s = new WetAreaSettings { Mappings = rows, MappingCategories = new() { "Floors", " floors ", "Room" } }.Normalize();
            Assert.Equal("x", Assert.Single(s.Mappings).Id);
            Assert.Equal(new[] { "Floors" }, s.MappingCategories); // duplicates and the Room pseudo category removed
            Assert.Equal(SectionTargetKind.Element, Assert.Single(s.Targets).Kind);
        }

        [Fact]
        public void RowsOfACategoryNotLoadedAreSwitchedOff()
        {
            var s = new WetAreaSettings().Normalize();
            s.MappingCategories = new() { "Doors" };
            s.Normalize();
            Assert.True(s.Targets.Single(t => t.RuleId == "door").On);
            Assert.False(s.Targets.Single(t => t.RuleId == "wc").On);
            Assert.True(s.Targets.Where(t => t.RuleId == "room").All(t => t.On));      // needs no element
            Assert.True(s.Targets.Single(t => t.RuleId == "wallup").On);               // any wall type: the outline is enough
        }

        [Fact]
        public void KeywordAndPickedTypeMatching()
        {
            var kw = new MappingRule { Category = "Plumbing Fixtures", Match = TypeMatch.Keywords, Keywords = new() { "basin" } };
            Assert.True(MappingRules.Matches(kw, "Plumbing Fixtures", "Wash Basin : 600"));
            Assert.False(MappingRules.Matches(kw, "Plumbing Fixtures", "WC : Wall hung"));
            Assert.False(MappingRules.Matches(kw, "Furniture", "Wash Basin : 600"));

            var picked = new MappingRule { Category = "Floors", Match = TypeMatch.Types, TypeNames = new() { "Floor : Screed 50" } };
            Assert.True(MappingRules.Matches(picked, "Floors", "Floor : Screed 50"));
            Assert.False(MappingRules.Matches(picked, "Floors", "Floor : Tile"));

            var types = new[] { new LoadedType("Floors", "Floor : Screed 50", "Host", 4), new LoadedType("Floors", "Floor : Screed 50", "Link A", 2),
                new LoadedType("Floors", "Floor : Tile", "Host", 9) };
            Assert.Equal(6, MappingRules.CountFound(picked, types));
        }

        private static RoomSectionInput Input(params FoundItem[] items) => new("101", Room, new Pt(2000, 1500), items.ToList());

        [Fact]
        public void MappedElementIsCutThroughByItsRowOnly()
        {
            var target = new SectionTargetSetting { Kind = SectionTargetKind.Element, On = true, LengthMm = 1200, RuleId = "basin", Label = "Basin", Mode = SectionMode.Callout };
            var basin = new FoundItem(SectionTargetKind.Element, new Pt(3800, 1500), 3000, 5, "Host", "Plumbing Fixtures", "Basin") { RuleId = "basin" };
            var wc = new FoundItem(SectionTargetKind.Element, new Pt(500, 500), 3000, 6, "Host", "Plumbing Fixtures", "WC") { RuleId = "wc" };
            SectionPlan plan = SectionPlanner.Plan(Input(basin, wc), new[] { target }, false, string.Empty);
            PlannedSection s = Assert.Single(plan.Sections);
            Assert.Equal(5, s.CutElementId);
            Assert.Equal("Basin", s.TargetName);
            Assert.Equal(SectionMode.Callout, s.Mode);
        }

        [Fact]
        public void PickedWallTypeLimitsTheWallCutToEdgesAlongSuchAWall()
        {
            // Only the short right-hand wall (x = 4000) is of the picked type: the "longest wall" cut goes there.
            var wall = new FoundItem(SectionTargetKind.Element, new Pt(4100, 1500), 3000, 9, "Host", "Walls", "Basic Wall : Tiled")
            {
                RuleId = "wallup", IsLine = true, LineStart = new Pt(4100, 0), LineEnd = new Pt(4100, 3000), HalfWidthMm = 100
            };
            var target = new SectionTargetSetting { Kind = SectionTargetKind.WallFloor, On = true, LengthMm = 1000, RuleId = "wallup", WallTypeFilter = true };
            PlannedSection s = Assert.Single(SectionPlanner.Plan(Input(wall), new[] { target }, false, string.Empty).Sections);
            Assert.Equal(4000, (s.Head.X + s.Tail.X) / 2, 6);
            Assert.Equal(1500, s.Head.Y, 6);

            SectionPlan none = SectionPlanner.Plan(Input(), new[] { target }, false, string.Empty);
            Assert.Empty(none.Sections);
            Assert.Contains("no wall", Assert.Single(none.Skipped));
        }

        [Fact]
        public void AWallBelongsToTheRoomItRunsAlong()
        {
            var along = new FoundItem(SectionTargetKind.Element, new Pt(2000, -100), 3000, 1, "Host", "Walls", "W") { IsLine = true, LineStart = new Pt(-100, -100), LineEnd = new Pt(4100, -100), HalfWidthMm = 100 };
            var away = new FoundItem(SectionTargetKind.Element, new Pt(2000, -2000), 3000, 2, "Host", "Walls", "W") { IsLine = true, LineStart = new Pt(0, -2000), LineEnd = new Pt(4000, -2000), HalfWidthMm = 100 };
            Assert.Equal(1, Assert.Single(RoomItemMatcher.ForRoom(Room, 3000, new[] { along, away })).ElementId);
        }

        [Fact]
        public void FullRoomHVRowGivesTwoTargets()
        {
            var t = MappingRules.ToTargets(new[] { new MappingRule { Id = "r", Name = "Full room", Category = MappingRules.RoomCategory, Cut = MappingCut.FullRoomHV } });
            Assert.Equal(new[] { SectionTargetKind.FullRoomH, SectionTargetKind.FullRoomV }, t.Select(x => x.Kind));
            Assert.Equal(new[] { "Full room – H", "Full room – V" }, t.Select(x => x.Label));
        }
    }
}
