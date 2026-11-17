using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SheetAlignDistribute.Tests
{
    /// <summary>
    /// Reading the rows and columns the views are already in, off their current positions.
    /// Coordinates are sheet coordinates: millimetres with Y up, so a larger Y is further up the
    /// sheet and the top row is the one with the largest Y.
    /// </summary>
    public class TrackDetectionTests
    {
        /// <summary>A box from its bottom-left corner and size, so the cases read like a sheet.</summary>
        private static TrackBox Box(int key, double x, double y, double w, double h)
            => new(key, new LayoutRect(x, y, x + w, y + h));

        /// <summary>
        /// A 3 × 3 sheet of views, each a few millimetres off a clean grid. Keys 0-2 are the top
        /// row, 3-5 the middle, 6-8 the bottom.
        /// </summary>
        private static List<TrackBox> Grid() => new()
        {
            Box(0, 20, 363, 200, 170),  Box(1, 243, 360, 200, 170), Box(2, 463, 360, 190, 100),
            Box(3, 24, 174, 200, 130),  Box(4, 243, 170, 185, 110), Box(5, 456, 166, 195, 130),
            Box(6, 20, 11, 200, 120),   Box(7, 240, 9, 200, 100),   Box(8, 460, 13, 195, 110)
        };

        [Fact]
        public void A_near_grid_reads_as_three_rows_and_three_columns()
        {
            var boxes = Grid();

            var rows = TrackDetection.DetectRows(boxes, 25);
            var columns = TrackDetection.DetectColumns(boxes, 25);

            Assert.Equal(3, rows.Count);
            Assert.Equal(3, columns.Count);
            Assert.All(rows, row => Assert.Equal(3, row.Count));
            Assert.All(columns, column => Assert.Equal(3, column.Count));
        }

        [Fact]
        public void Rows_come_back_top_first_and_each_row_reads_left_to_right()
        {
            var rows = TrackDetection.DetectRows(Grid(), 25);

            Assert.Equal(new[] { 0, 1, 2 }, rows[0]);
            Assert.Equal(new[] { 3, 4, 5 }, rows[1]);
            Assert.Equal(new[] { 6, 7, 8 }, rows[2]);
        }

        [Fact]
        public void Columns_come_back_left_first_and_each_column_reads_top_to_bottom()
        {
            var columns = TrackDetection.DetectColumns(Grid(), 25);

            Assert.Equal(new[] { 0, 3, 6 }, columns[0]);
            Assert.Equal(new[] { 1, 4, 7 }, columns[1]);
            Assert.Equal(new[] { 2, 5, 8 }, columns[2]);
        }

        [Fact]
        public void Every_view_lands_in_exactly_one_row_and_one_column()
        {
            var boxes = Grid();

            var rowMembers = TrackDetection.DetectRows(boxes, 25).SelectMany(r => r).OrderBy(i => i);
            var columnMembers = TrackDetection.DetectColumns(boxes, 25).SelectMany(c => c).OrderBy(i => i);

            Assert.Equal(Enumerable.Range(0, boxes.Count), rowMembers);
            Assert.Equal(Enumerable.Range(0, boxes.Count), columnMembers);
        }

        [Fact]
        public void Views_that_overlap_vertically_share_a_row_however_far_their_edges_are_apart()
        {
            // 44 mm between the bottoms — well past the tolerance — but the boxes still overlap.
            var boxes = new List<TrackBox> { Box(0, 0, 10, 100, 175), Box(1, 120, 54, 100, 120) };

            var rows = TrackDetection.DetectRows(boxes, 5);

            Assert.Single(rows);
            Assert.Equal(2, rows[0].Count);
        }

        [Fact]
        public void Views_in_separate_bands_do_not_share_a_row()
        {
            var boxes = new List<TrackBox> { Box(0, 0, 0, 100, 100), Box(1, 0, 200, 100, 100) };

            Assert.Equal(2, TrackDetection.DetectRows(boxes, 25).Count);
        }

        /// <summary>
        /// The tolerance rule on its own, with no vertical overlap to fall back on: two shallow
        /// views side by side, one 20 mm higher than the other.
        /// </summary>
        [Fact]
        public void An_edge_just_inside_the_tolerance_is_enough_to_share_a_row()
        {
            var boxes = new List<TrackBox> { Box(0, 0, 0, 100, 10), Box(1, 120, 20, 100, 10) };

            Assert.Equal(0, boxes[0].Rect.VerticalOverlap(boxes[1].Rect) > 0 ? 1 : 0);  // they do not overlap
            Assert.Single(TrackDetection.DetectRows(boxes, 25));                        // 20 mm apart, within 25
            Assert.Equal(2, TrackDetection.DetectRows(boxes, 10).Count);                // and outside 10
        }

        [Fact]
        public void Membership_is_transitive_so_a_chain_of_near_neighbours_is_one_row()
        {
            // Each is 20 mm above the one before: 0-1 and 1-2 are within tolerance, 0-2 is not,
            // and no two of them overlap vertically.
            var boxes = new List<TrackBox>
            {
                Box(0, 0, 0, 80, 10), Box(1, 100, 20, 80, 10), Box(2, 200, 40, 80, 10)
            };

            var rows = TrackDetection.DetectRows(boxes, 25);

            Assert.Single(rows);
            Assert.Equal(3, rows[0].Count);
        }

        [Fact]
        public void Detection_does_not_depend_on_the_order_the_views_are_read_in()
        {
            var forwards = TrackDetection.DetectRows(Grid(), 25)
                .Select(row => string.Join(",", row.OrderBy(i => i)))
                .OrderBy(s => s)
                .ToList();

            var shuffled = Grid();
            shuffled.Reverse();
            var backwards = TrackDetection.DetectRows(shuffled, 25)
                // Indices are into the shuffled list, so map them back to the original keys.
                .Select(row => string.Join(",", row.Select(i => shuffled[i].Key).OrderBy(k => k)))
                .OrderBy(s => s)
                .ToList();

            Assert.Equal(forwards, backwards);
        }

        [Fact]
        public void A_tall_view_spanning_two_bands_joins_them_into_one_row()
        {
            // Documented behaviour: the plan reports one wide row rather than splitting it silently.
            var boxes = new List<TrackBox>
            {
                Box(0, 0, 0, 80, 40),      // low band
                Box(1, 0, 200, 80, 40),    // high band
                Box(2, 100, 0, 80, 240)    // spans both
            };

            Assert.Single(TrackDetection.DetectRows(boxes, 5));
        }

        [Fact]
        public void A_lone_view_is_its_own_row_and_its_own_column()
        {
            var boxes = new List<TrackBox> { Box(0, 0, 0, 50, 50) };

            Assert.Single(TrackDetection.DetectRows(boxes, 25));
            Assert.Single(TrackDetection.DetectColumns(boxes, 25));
        }

        [Fact]
        public void Nothing_in_gives_nothing_out()
        {
            Assert.Empty(TrackDetection.DetectRows(new List<TrackBox>(), 25));
            Assert.Empty(TrackDetection.DetectColumns(new List<TrackBox>(), 25));
        }

        [Fact]
        public void A_negative_tolerance_is_treated_as_none_rather_than_throwing()
        {
            var boxes = new List<TrackBox> { Box(0, 0, 0, 100, 10), Box(1, 120, 20, 100, 10) };

            Assert.Equal(2, TrackDetection.DetectRows(boxes, -5).Count);
        }
    }
}
