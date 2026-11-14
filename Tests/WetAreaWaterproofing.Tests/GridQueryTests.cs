using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace WetAreaWaterproofing.Tests
{
    public class GridQueryTests
    {
        private static RoomRow Row(string no, string name, string level, double elev, string source = "Host",
            double area = 5, string matched = "toilet", RoomStatus status = RoomStatus.Ready)
            => new RoomRow
            {
                RoomKey = source + ":" + no, Number = no, Name = name, Level = level, LevelElevationMm = elev,
                Source = source, AreaM2 = area, Matched = matched, Status = status
            };

        private static readonly List<RoomRow> Rows = new()
        {
            Row("101", "Toilet 01", "L1", 0, area: 4.2),
            Row("102", "Kitchen", "L1", 0, area: 12.85, matched: "kitchen"),
            Row("103", "Office", "L1", 0, area: 20, matched: null),
            Row("201", "Master Bath", "L2", 3000, "ARC_Model", 7.6, "bath", RoomStatus.Repaired),
            Row("202", "Shower Room", "L2", 3000, "ARC_Model", 3.1, "shower", RoomStatus.NoSlab),
            Row("1001", "Balcony", "L10", 30000, area: 9, matched: "balcony"),
        };

        private static string[] Numbers(GridState st) => GridQuery.Apply(Rows, st).Select(r => r.Number).ToArray();

        [Fact]
        public void WetOnlyIsDefaultAndHidesUnmatchedRooms()
            => Assert.DoesNotContain("103", Numbers(new GridState()));

        [Fact]
        public void AllRoomsShowsUnmatched()
            => Assert.Contains("103", Numbers(new GridState { WetOnly = false }));

        [Fact]
        public void LevelFilterKeepsOnlyChosenLevels()
        {
            var st = new GridState();
            st.Levels.Add("L1"); st.Levels.Add("L2");
            Assert.Equal(new[] { "101", "102", "201", "202" }, Numbers(st));
        }

        [Fact]
        public void ToggleLevelAddsRemovesAndSolos()
        {
            var st = new GridState();
            var all = new[] { "L1", "L2", "L10" };
            st.ToggleLevel("L1", false, all);            // from "all": hide L1
            Assert.Equal(new[] { "L10", "L2" }, st.Levels.OrderBy(x => x));
            st.ToggleLevel("L1", false, all);            // show it again → everything → stored as "all"
            Assert.Empty(st.Levels);
            st.ToggleLevel("L2", true, all);             // solo
            Assert.Equal(new[] { "L2" }, st.Levels);
            st.ToggleLevel("L2", true, all);             // solo on the only one → all
            Assert.Empty(st.Levels);
            st.ToggleLevel("L2", true, all);
            st.ToggleLevel("L2", false, all);            // hiding the last level → all, never an empty grid
            Assert.Empty(st.Levels);
        }

        [Fact]
        public void TickFilterOnSource()
        {
            var st = new GridState();
            st.TickFilters["Source"] = new HashSet<string> { "ARC_Model" };
            Assert.Equal(new[] { "201", "202" }, Numbers(st));
        }

        [Fact]
        public void TickFilterOnStatus()
        {
            var st = new GridState();
            st.TickFilters["Status"] = new HashSet<string> { "Repaired", "NoSlab" };
            Assert.Equal(new[] { "201", "202" }, Numbers(st));
        }

        [Fact]
        public void ContainsOnNameIgnoresCase()
        {
            var st = new GridState();
            st.Contains["Name"] = "BATH";
            Assert.Equal(new[] { "201" }, Numbers(st));
        }

        [Fact]
        public void AreaRangeIsInclusive()
            => Assert.Equal(new[] { "101", "201" }, Numbers(new GridState { AreaMin = 4.2, AreaMax = 7.6 }));

        [Fact]
        public void SearchLooksInEveryTextColumn()
        {
            Assert.Equal(new[] { "201", "202" }, Numbers(new GridState { Search = "arc_" }));
            Assert.Equal(new[] { "102" }, Numbers(new GridState { Search = "kitch" }));
            Assert.Equal(new[] { "1001" }, Numbers(new GridState { Search = "L10" }));
        }

        [Fact]
        public void DefaultOrderIsLevelElevationThenNumber()
            => Assert.Equal(new[] { "101", "102", "201", "202", "1001" }, Numbers(new GridState()));

        [Fact]
        public void MultiSortLevelAscThenNumberDesc()
        {
            var st = new GridState();
            st.Sort.Add(new SortKey("Level", false));
            st.Sort.Add(new SortKey("Number", true));
            Assert.Equal(new[] { "102", "101", "202", "201", "1001" }, Numbers(st));
        }

        [Fact]
        public void NumbersSortNaturally()
        {
            var st = new GridState();
            st.Sort.Add(new SortKey("Number", false));
            Assert.Equal(new[] { "101", "102", "201", "202", "1001" }, Numbers(st));
        }

        [Fact]
        public void SortByAreaIsNumeric()
        {
            var st = new GridState();
            st.Sort.Add(new SortKey("Area", true));
            Assert.Equal("102", Numbers(st)[0]);
        }

        [Fact]
        public void LevelCountsAreOrderedByElevation()
        {
            var counts = GridQuery.LevelCounts(Rows, true);
            Assert.Equal(new[] { "L1", "L2", "L10" }, counts.Select(c => c.Level));
            Assert.Equal(new[] { 2, 2, 1 }, counts.Select(c => c.Count));
            Assert.Equal(3, GridQuery.LevelCounts(Rows, false)[0].Count);
        }

        [Fact]
        public void ActiveFilterCountCountsEachKind()
        {
            var st = new GridState { Search = "x", AreaMin = 1 };
            st.Levels.Add("L1");
            st.TickFilters["Source"] = new HashSet<string> { "Host" };
            st.Contains["Name"] = "a";
            st.Contains["Number"] = " ";
            Assert.Equal(5, GridQuery.ActiveFilterCount(st));
            st.Clear();
            Assert.Equal(0, GridQuery.ActiveFilterCount(st));
            Assert.True(st.WetOnly);
        }

        [Fact]
        public void NullRowsGiveEmptyResult()
            => Assert.Empty(GridQuery.Apply(null, new GridState()));
    }
}
