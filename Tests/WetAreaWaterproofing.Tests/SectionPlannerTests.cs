using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Matching;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Sections;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace WetAreaWaterproofing.Tests
{
    public class SectionSettingsTests
    {
        [Fact]
        public void DefaultTargetsMatchThePlan()
        {
            var s = new WetAreaSettings().Normalize();
            Assert.Equal(new[]
            {
                SectionTargetKind.FullRoomH, SectionTargetKind.FullRoomV, SectionTargetKind.Door, SectionTargetKind.Drain,
                SectionTargetKind.WallCorner, SectionTargetKind.Wc, SectionTargetKind.Shower, SectionTargetKind.WallFloor
            }, s.Targets.Select(t => t.Kind));
            Assert.Equal(new[] { true, true, true, true, false, false, false, false }, s.Targets.Select(t => t.On));
            Assert.Equal(1500, s.Targets[2].LengthMm);
            Assert.True(s.OneDraftingViewForAll);
            Assert.Equal("WP Detail - Typical Wet Area", s.SharedDraftingViewName);
            Assert.Equal(500, s.CropMarginMm);
            Assert.Equal(100, s.PlanScale);
            Assert.Equal("WP - {Level} - {Number} {Name}", s.PlanNamePattern);
            Assert.Equal(new[] { "drain", "fd", "floor drain" }, s.DrainKeywords);
            Assert.True(s.CreateRoofs && s.CreatePlans && s.CreateSections);
        }

        [Fact]
        public void NormalizeRestoresMissingTargetsAndKeepsOrder()
        {
            var s = new WetAreaSettings
            {
                Targets = new List<SectionTargetSetting>
                {
                    new() { Kind = SectionTargetKind.Drain, On = true, LengthMm = -5 },
                    new() { Kind = SectionTargetKind.Drain, On = true },      // duplicate
                    null,
                    new() { Kind = SectionTargetKind.Door, On = false, LengthMm = 900 }
                },
                CropMarginMm = -1, PlanScale = 0, PlanNamePattern = " ", SharedDraftingViewName = null
            }.Normalize();
            Assert.Equal(8, s.Targets.Count);
            Assert.Equal(SectionTargetKind.Drain, s.Targets[0].Kind);
            Assert.Equal(1200, s.Targets[0].LengthMm);       // bad length → default
            Assert.Equal(SectionTargetKind.Door, s.Targets[1].Kind);
            Assert.Equal(900, s.Targets[1].LengthMm);
            Assert.False(s.Targets[1].On);
            Assert.Equal(500, s.CropMarginMm);
            Assert.Equal(100, s.PlanScale);
            Assert.False(string.IsNullOrWhiteSpace(s.PlanNamePattern));
            Assert.False(string.IsNullOrWhiteSpace(s.SharedDraftingViewName));
        }
    }

    public class ItemClassifierTests
    {
        private static readonly WetAreaSettings S = new WetAreaSettings().Normalize();

        [Theory]
        [InlineData("Doors", "Single Flush 900x2100", SectionTargetKind.Door)]
        [InlineData("Plumbing Fixtures", "Floor Drain 100mm", SectionTargetKind.Drain)]
        [InlineData("Pipe Accessories", "FD-01 : Standard", SectionTargetKind.Drain)]
        [InlineData("Plumbing Fixtures", "Shower Drain Linear", SectionTargetKind.Drain)]
        [InlineData("Plumbing Fixtures", "Water Closet - Wall Hung", SectionTargetKind.Wc)]
        [InlineData("Plumbing Fixtures", "WC Floor Mounted", SectionTargetKind.Wc)]
        [InlineData("Plumbing Fixtures", "Shower Tray 900", SectionTargetKind.Shower)]
        [InlineData("Plumbing Fixtures", "Bath Tub 1700", SectionTargetKind.Shower)]
        public void KnownItemsAreClassified(string category, string name, SectionTargetKind expected)
            => Assert.Equal(expected, ItemClassifier.Classify(category, name, S));

        [Theory]
        [InlineData("Plumbing Fixtures", "Wash Basin 600")]
        [InlineData("Pipe Accessories", "Gate Valve 50")]
        [InlineData("Furniture", "Floor Drain")]
        [InlineData("Plumbing Fixtures", null)]
        public void OtherItemsAreNotTargets(string category, string name)
            => Assert.Null(ItemClassifier.Classify(category, name, S));

        [Fact]
        public void MultiWordAndDottedKeywordsMatch()
        {
            Assert.Equal("powder room", KeywordMatcher.Match("Powder Room 2", new[] { "powder room" }));
            Assert.Null(KeywordMatcher.Match("Powder Store Room", new[] { "powder room" }));
            Assert.Equal("w.c.", KeywordMatcher.Match("WC 01", new[] { "w.c." }));
        }
    }

    public class RoomItemMatcherTests
    {
        private static readonly List<Pt> Room = new() { new(0, 0), new(4000, 0), new(4000, 3000), new(0, 3000) };

        private static FoundItem Item(SectionTargetKind kind, double x, double y, double z = 3000, long id = 1)
            => new(kind, new Pt(x, y), z, id, "Host", "Cat", "Name");

        [Fact]
        public void ItemsInsideTheRoomAreKept()
            => Assert.Single(RoomItemMatcher.ForRoom(Room, 3000, new[] { Item(SectionTargetKind.Drain, 2000, 1500) }));

        [Fact]
        public void FixtureOutsideTheRoomIsLeftOut()
            => Assert.Empty(RoomItemMatcher.ForRoom(Room, 3000, new[] { Item(SectionTargetKind.Drain, 4200, 1500) }));

        [Fact]
        public void DoorInTheBoundaryWallIsFound()
        {
            // Door sits in the wall, 100 mm outside the finish outline.
            Assert.Single(RoomItemMatcher.ForRoom(Room, 3000, new[] { Item(SectionTargetKind.Door, 2000, -100) }));
            Assert.Empty(RoomItemMatcher.ForRoom(Room, 3000, new[] { Item(SectionTargetKind.Door, 2000, -900) }));
        }

        [Fact]
        public void DoorBetweenTwoRoomsBelongsToBoth()
        {
            var next = new List<Pt> { new(4200, 0), new(8000, 0), new(8000, 3000), new(4200, 3000) };
            var door = Item(SectionTargetKind.Door, 4100, 1500);
            Assert.Single(RoomItemMatcher.ForRoom(Room, 3000, new[] { door }));
            Assert.Single(RoomItemMatcher.ForRoom(next, 3000, new[] { door }));
        }

        [Fact]
        public void ItemsOfTheFloorAboveAreLeftOutWhenStoreysAreLow()
        {
            // Storey height 2900: the door and the drain of the room above must not count for this room.
            var above = new[] { Item(SectionTargetKind.Door, 2000, -100, z: 2900), Item(SectionTargetKind.Drain, 2000, 1500, z: 2750) };
            Assert.Empty(RoomItemMatcher.ForRoom(Room, 0, above, nextLevelMm: 2900));
            Assert.Empty(RoomItemMatcher.ForRoom(Room, 0, above));                       // no level above known: 2000 mm band
            Assert.Single(RoomItemMatcher.ForRoom(Room, 0, new[] { Item(SectionTargetKind.Wc, 2000, 1500, z: 400) }, nextLevelMm: 2900));
            // Very low storey (mezzanine 1800 above): the band stops 500 below the next level.
            Assert.Empty(RoomItemMatcher.ForRoom(Room, 0, new[] { Item(SectionTargetKind.Drain, 2000, 1500, z: 1400) }, nextLevelMm: 1800));
        }

        [Fact]
        public void ItemsOnOtherLevelsAreLeftOut()
        {
            Assert.Empty(RoomItemMatcher.ForRoom(Room, 3000, new[] { Item(SectionTargetKind.Drain, 2000, 1500, z: 6300) }));
            Assert.Empty(RoomItemMatcher.ForRoom(Room, 3000, new[] { Item(SectionTargetKind.Drain, 2000, 1500, z: 0) }));
            Assert.Single(RoomItemMatcher.ForRoom(Room, 3000, new[] { Item(SectionTargetKind.Drain, 2000, 1500, z: 2850) })); // drain set into the slab
        }
    }

    public class SectionPlannerTests
    {
        private static readonly List<Pt> Room = new() { new(0, 0), new(4000, 0), new(4000, 3000), new(0, 3000) };

        private static FoundItem Item(SectionTargetKind kind, double x, double y, long id = 1)
            => new(kind, new Pt(x, y), 3000, id, "Host", "Cat", kind + " " + id);

        private static List<SectionTargetSetting> Targets(params (SectionTargetKind kind, bool all)[] on)
        {
            var targets = new WetAreaSettings().Normalize().Targets;
            foreach (var t in targets) t.On = false;
            var ordered = new List<SectionTargetSetting>();
            foreach (var (kind, all) in on)
            {
                var t = targets.First(x => x.Kind == kind);
                t.On = true; t.AllMatches = all;
                ordered.Add(t);
            }
            ordered.AddRange(targets.Where(t => !t.On));
            return ordered;
        }

        private static SectionPlan Plan(List<SectionTargetSetting> targets, IEnumerable<FoundItem> items = null,
            List<Pt> room = null, Pt? inside = null, bool oneForAll = true)
            => SectionPlanner.Plan(new RoomSectionInput("101", room ?? Room, inside ?? new Pt(2000, 1500),
                (items ?? new FoundItem[0]).ToList()), targets, oneForAll, "Shared Detail", 6);

        [Fact]
        public void FullRoomCutsSpanTheRoomPlusOvershoot()
        {
            var plan = Plan(Targets((SectionTargetKind.FullRoomH, false), (SectionTargetKind.FullRoomV, false)));
            Assert.Equal(2, plan.Sections.Count);
            var h = plan.Sections[0];
            Assert.Equal(new Pt(-300, 1500), h.Head);
            Assert.Equal(new Pt(4300, 1500), h.Tail);
            var v = plan.Sections[1];
            Assert.Equal(new Pt(2000, -300), v.Head);
            Assert.Equal(new Pt(2000, 3300), v.Tail);
            Assert.Empty(plan.Skipped);
        }

        [Fact]
        public void LShapedRoomIsCutThroughThePointInsideIt()
        {
            var l = new List<Pt> { new(0, 0), new(4000, 0), new(4000, 1000), new(1000, 1000), new(1000, 3000), new(0, 3000) };
            var plan = Plan(Targets((SectionTargetKind.FullRoomH, false)), room: l, inside: new Pt(500, 2000));
            Assert.Equal(2000, plan.Sections[0].Head.Y);
        }

        [Fact]
        public void DoorIsCutSquareToItsWallThroughTheDoor()
        {
            var plan = Plan(Targets((SectionTargetKind.Door, false)), new[] { Item(SectionTargetKind.Door, 2000, -100, 77) });
            var s = Assert.Single(plan.Sections);
            Assert.Equal(2000, s.Head.X, 6);
            Assert.Equal(2000, s.Tail.X, 6);
            Assert.Equal(1500, s.Head.DistanceTo(s.Tail), 6);
            Assert.Equal(-100, (s.Head.Y + s.Tail.Y) / 2, 6);
            Assert.Equal(77, s.CutElementId);
        }

        [Fact]
        public void OnePerItemPicksTheMatchNearestTheCentre()
        {
            var items = new[] { Item(SectionTargetKind.Drain, 300, 300, 1), Item(SectionTargetKind.Drain, 1900, 1400, 2) };
            var s = Assert.Single(Plan(Targets((SectionTargetKind.Drain, false)), items).Sections);
            Assert.Equal(2, s.CutElementId);
        }

        [Fact]
        public void AllMatchesGivesOneSectionEach()
        {
            var items = new[] { Item(SectionTargetKind.Drain, 300, 300, 1), Item(SectionTargetKind.Drain, 1900, 1400, 2) };
            var plan = Plan(Targets((SectionTargetKind.Drain, true)), items);
            Assert.Equal(new long[] { 2, 1 }, plan.Sections.Select(s => s.CutElementId));
            Assert.Equal(new[] { "Floor drain 1", "Floor drain 2" }, plan.Sections.Select(s => s.Label));
        }

        [Fact]
        public void MissingTargetIsSkippedAndTheRestStillPlaced()
        {
            var plan = Plan(Targets((SectionTargetKind.Drain, false), (SectionTargetKind.FullRoomH, false)));
            Assert.Single(plan.Sections);
            Assert.Contains("no floor drain", Assert.Single(plan.Skipped));
        }

        [Fact]
        public void NeverMoreThanSixAndHigherRowsWin()
        {
            var drains = Enumerable.Range(1, 8).Select(i => Item(SectionTargetKind.Drain, 400 * i, 1500, i));
            var plan = Plan(Targets((SectionTargetKind.FullRoomH, false), (SectionTargetKind.Drain, true), (SectionTargetKind.FullRoomV, false)), drains);
            Assert.Equal(6, plan.Sections.Count);
            Assert.Equal(SectionTargetKind.FullRoomH, plan.Sections[0].Kind);
            Assert.Equal(5, plan.Sections.Count(s => s.Kind == SectionTargetKind.Drain));
            Assert.DoesNotContain(plan.Sections, s => s.Kind == SectionTargetKind.FullRoomV);
            Assert.Contains(plan.Skipped, m => m.Contains("limit of 6"));
        }

        [Fact]
        public void WallCornerCutsSquareToTheLongerWall300FromTheCorner()
        {
            // Nearest corner to (500, 400) is (0, 0); its longer wall runs along X (4000 > 3000).
            var s = Assert.Single(Plan(Targets((SectionTargetKind.WallCorner, false)), inside: new Pt(500, 400)).Sections);
            Assert.Equal(300, s.Head.X, 6);
            Assert.Equal(300, s.Tail.X, 6);
            Assert.Equal(1000, s.Head.DistanceTo(s.Tail), 6);
        }

        [Fact]
        public void WallFloorCutsTheLongestWallAtItsMiddle()
        {
            var s = Assert.Single(Plan(Targets((SectionTargetKind.WallFloor, false))).Sections);
            Assert.Equal(2000, s.Head.X, 6);
            Assert.Equal(2000, s.Tail.X, 6);
        }

        [Fact]
        public void DraftingViewIsSharedOrPerTarget()
        {
            var targets = Targets((SectionTargetKind.FullRoomH, false));
            targets[0].DraftingViewName = "WP Detail - Room";
            Assert.Equal("Shared Detail", Plan(targets).Sections[0].DraftingViewName);
            Assert.Equal("WP Detail - Room", Plan(targets, oneForAll: false).Sections[0].DraftingViewName);
        }

        [Fact]
        public void NoOutlineGivesNoSections()
            => Assert.Empty(Plan(Targets((SectionTargetKind.FullRoomH, false)), room: new List<Pt>()).Sections);
    }

    public class ViewNamesTests
    {
        [Fact]
        public void PatternIsFilled()
            => Assert.Equal("WP - L02 - 201 Master Bath", ViewNames.Fill("WP - {Level} - {Number} {Name}", "L02", "201", "Master Bath"));

        [Fact]
        public void CharactersRevitRejectsAreReplaced()
            => Assert.Equal("WP - L1 - 1-2 Bath - WC", ViewNames.Fill("WP - {Level} - {Number} {Name}", "L1", "1:2", "Bath | WC"));

        [Fact]
        public void EmptyResultFallsBack()
            => Assert.Equal("WP", ViewNames.Fill(" ", "", "", ""));

        [Fact]
        public void TakenNameGetsASuffix()
        {
            var taken = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "WP Detail", "wp detail (2)" };
            Assert.Equal("WP Detail (3)", ViewNames.Unique("WP Detail", taken));
            Assert.Equal("Other", ViewNames.Unique("Other", taken));
        }
    }
}
