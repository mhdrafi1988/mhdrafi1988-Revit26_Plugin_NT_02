using Revit26_Plugin_Revised.TrackerId.V002.Core.Models;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TrackerId.Tests
{
    public class NamingTests
    {
        [Fact]
        public void A_type_code_replaces_the_type_mark()
        {
            var door = TestData.Door(1);
            door.TypeId = 500;
            var format = TestData.Format();
            format.TypeCodes = new Dictionary<long, string> { { 500, "DR" } };

            var row = TrackerIdPlanner.Plan(new[] { door }, format, TestData.Grids(), new List<TrackerIdHolder>()).Single();
            Assert.Equal("NT02-ARC-ZA-L01-DR-A01-0001", row.ResultId);
        }

        [Fact]
        public void A_blank_type_code_keeps_the_old_behaviour()
        {
            var door = TestData.Door(1);
            door.TypeId = 500;
            var format = TestData.Format();
            format.TypeCodes = new Dictionary<long, string> { { 500, "  " } };

            var row = TrackerIdPlanner.Plan(new[] { door }, format, TestData.Grids(), new List<TrackerIdHolder>()).Single();
            Assert.Equal("NT02-ARC-ZA-L01-D09-A01-0001", row.ResultId);
        }

        [Fact]
        public void Upper_case_capitalises_the_whole_id_and_is_off_by_default()
        {
            var door = TestData.Door(1, zone: "za");
            var format = TestData.Format();
            Assert.False(format.UpperCase);
            string normal = TrackerIdPlanner.Plan(new[] { door }, format, TestData.Grids(), new List<TrackerIdHolder>()).Single().ResultId;

            format.UpperCase = true;
            string upper = TrackerIdPlanner.Plan(new[] { door }, format, TestData.Grids(), new List<TrackerIdHolder>()).Single().ResultId;

            Assert.Equal(normal.ToUpperInvariant(), upper);
        }
    }
}
