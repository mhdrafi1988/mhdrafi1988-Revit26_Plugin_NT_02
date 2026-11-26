using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Sections;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace WetAreaWaterproofingV002.Tests
{
    public class TargetCoverageTests
    {
        private static readonly List<Pt> Box = new() { new(0, 0), new(4000, 0), new(4000, 3000), new(0, 3000) };

        private static FoundItem Item(SectionTargetKind kind, double x, double y, long id)
            => new(kind, new Pt(x, y), 3000, id, "Host", "Cat", kind + " " + id);

        private static CoverageRoom Room(string label, params FoundItem[] items)
            => new(label, new RoomSectionInput(label, Box, new Pt(2000, 1500), items.ToList()));

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

        [Fact]
        public void CountsRoomsWithAndWithoutAMatch()
        {
            var rooms = new[]
            {
                Room("A", Item(SectionTargetKind.Drain, 1000, 1000, 1), Item(SectionTargetKind.Drain, 3000, 1000, 2)),
                Room("B", Item(SectionTargetKind.Drain, 1000, 1000, 3)),
                Room("C")
            };
            var rows = TargetCoverage.Count(rooms, Targets((SectionTargetKind.Drain, true)));
            var drain = rows.First(r => r.Kind == SectionTargetKind.Drain);
            Assert.Equal(2, drain.RoomsWith);
            Assert.Equal(3, drain.RoomsTotal);
            Assert.Equal(3, drain.Matches);
            Assert.Equal(3, drain.Makes);
            Assert.Equal(new[] { "C" }, drain.RoomsWithout);
            Assert.Empty(drain.Dropped);
            Assert.True(drain.HasProblem);
        }

        [Fact]
        public void OffTargetsFindButMakeNothing()
        {
            var rows = TargetCoverage.Count(new[] { Room("A", Item(SectionTargetKind.Wc, 1000, 1000, 1)) },
                Targets((SectionTargetKind.FullRoomH, false)));
            var wc = rows.First(r => r.Kind == SectionTargetKind.Wc);
            Assert.Equal(1, wc.RoomsWith);
            Assert.Equal(0, wc.Makes);
            Assert.Empty(wc.Dropped);
            Assert.Equal(1, rows.First(r => r.Kind == SectionTargetKind.FullRoomH).Makes);
        }

        [Fact]
        public void OneNearestCentreMakesOnePerRoom()
        {
            var rooms = new[] { Room("A", Item(SectionTargetKind.Door, 2000, -100, 1), Item(SectionTargetKind.Door, 4100, 1500, 2)) };
            var door = TargetCoverage.Count(rooms, Targets((SectionTargetKind.Door, false))).First(r => r.Kind == SectionTargetKind.Door);
            Assert.Equal(2, door.Matches);
            Assert.Equal(1, door.Makes);
            Assert.Empty(door.Dropped);
        }

        [Fact]
        public void LimitPerRoomIsReportedOnTheLowerRow()
        {
            var drains = Enumerable.Range(1, 6).Select(i => Item(SectionTargetKind.Drain, 500 * i, 1000, i)).ToArray();
            var rooms = new[] { Room("A", drains) };
            var rows = TargetCoverage.Count(rooms, Targets((SectionTargetKind.FullRoomH, false), (SectionTargetKind.Drain, true)));
            var drain = rows.First(r => r.Kind == SectionTargetKind.Drain);
            Assert.Equal(5, drain.Makes);
            Assert.Equal(new[] { "A: 1 dropped" }, drain.Dropped);
            Assert.Empty(rows.First(r => r.Kind == SectionTargetKind.FullRoomH).Dropped);
        }

        [Fact]
        public void RoomsWithoutAnOutlineAreLeftOut()
        {
            var rooms = new[] { Room("A"), new CoverageRoom("B", null) };
            var row = TargetCoverage.Count(rooms, Targets((SectionTargetKind.FullRoomV, false))).First();
            Assert.Equal(1, row.RoomsTotal);
            Assert.Equal(1, row.Makes);
        }
    }
}
