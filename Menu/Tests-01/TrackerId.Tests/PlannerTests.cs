using Revit26_Plugin_Revised.TrackerId.V001.Core.Models;
using Revit26_Plugin_Revised.TrackerId.V001.Core.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TrackerId.Tests
{
    public class PlannerTests
    {
        [Fact]
        public void New_elements_get_the_default_format_with_model_code_and_running_number()
        {
            var rows = TrackerIdPlanner.Plan(new[] { TestData.Door(1), TestData.Door(2) }, TestData.Format(), TestData.Grids(), new List<TrackerIdHolder>());
            Assert.Equal(new[] { "NT02-ARC-ZA-L01-D09-A01-0001", "NT02-ARC-ZA-L01-D09-A01-0002" }, rows.Select(r => r.ResultId));
            Assert.All(rows, r => Assert.Equal(PlanAction.Create, r.Action));
        }

        [Fact]
        public void A_copy_gets_a_new_id_and_the_original_keeps_its_id()
        {
            var original = TestData.Door(10, "NT02-ARC-ZA-L01-D09-A01-0001", "u10");
            var copy = TestData.Door(11, "NT02-ARC-ZA-L01-D09-A01-0001", "u10");
            var elements = new[] { original, copy };
            var rows = TrackerIdPlanner.Plan(elements, TestData.Format(), TestData.Grids(), TestData.Holders(elements));

            Assert.Equal(PlanAction.Keep, rows[0].Action);
            Assert.Equal(PlanAction.Copy, rows[1].Action);
            Assert.Equal("NT02-ARC-ZA-L01-D09-A01-0002", rows[1].ResultId);
        }

        [Fact]
        public void A_moved_element_keeps_its_id()
        {
            var moved = TestData.Door(10, "OLD-ID-1", "u10");
            moved.StoredZone = "ZB";
            var row = TrackerIdPlanner.Plan(new[] { moved }, TestData.Format(), TestData.Grids(), TestData.Holders(new[] { moved })).Single();

            Assert.Equal(PlanAction.KeepMoved, row.Action);
            Assert.Equal("OLD-ID-1", row.ResultId);
            Assert.True(TrackerIdPlanner.IsMoved(row));
        }

        [Fact]
        public void Running_number_skips_ids_used_elsewhere_ignoring_case()
        {
            var holders = new List<TrackerIdHolder> { new(99, "u99", "nt02-arc-za-l01-d09-a01-0001", "u99") };
            var row = TrackerIdPlanner.Plan(new[] { TestData.Door(1) }, TestData.Format(), TestData.Grids(), holders).Single();
            Assert.Equal("NT02-ARC-ZA-L01-D09-A01-0002", row.ResultId);
        }

        [Fact]
        public void Without_running_number_identical_elements_are_duplicates()
        {
            var format = TestData.Format();
            format.Get(SegmentKind.Custom3).Enabled = false;
            var rows = TrackerIdPlanner.Plan(new[] { TestData.Door(1), TestData.Door(2) }, format, TestData.Grids(), new List<TrackerIdHolder>());
            Assert.Equal(PlanAction.Create, rows[0].Action);
            Assert.Equal(PlanAction.Duplicate, rows[1].Action);
        }

        [Fact]
        public void Grid_crossing_is_found_from_the_centre_point()
        {
            var hit = TestData.Grids().Find(TestData.Ft(17), TestData.Ft(25), SegmentSource.GridCrossing);
            Assert.Equal("C04", hit.Label);
            Assert.Equal(1.41, hit.DistanceMetres, 2);
        }
    }
}
