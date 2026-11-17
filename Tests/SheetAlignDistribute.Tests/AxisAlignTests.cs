using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout;
using System;
using System.Linq;
using Xunit;

namespace SheetAlignDistribute.Tests
{
    /// <summary>The one-dimensional maths both axes share: the median line, its clamp, and the spread.</summary>
    public class AxisAlignTests
    {
        [Fact]
        public void Median_of_an_odd_count_is_the_middle_value()
            => Assert.Equal(185, AxisAlign.Median(new[] { 141.0, 185.0, 188.0 }));

        [Fact]
        public void Median_of_an_even_count_is_the_mean_of_the_middle_two()
            => Assert.Equal(183.5, AxisAlign.Median(new[] { 180.0, 182.0, 185.0, 188.0 }));

        [Fact]
        public void Median_does_not_care_what_order_the_values_arrive_in()
            => Assert.Equal(AxisAlign.Median(new[] { 141.0, 185.0, 188.0 }),
                            AxisAlign.Median(new[] { 188.0, 141.0, 185.0 }));

        [Fact]
        public void Median_of_nothing_is_an_error_rather_than_a_silent_zero()
            => Assert.Throws<ArgumentException>(() => AxisAlign.Median(Array.Empty<double>()));

        /// <summary>
        /// The reason the shared line is a median and not a mean: one view 44 units out of place
        /// must not drag the rest of its row with it.
        /// </summary>
        [Fact]
        public void One_badly_placed_view_does_not_move_the_rest_of_its_row()
        {
            var lowEdges = new[] { 185.0, 188.0, 141.0 };   // the third sits well above the other two
            var sizes = new[] { 175.0, 175.0, 120.0 };

            Assert.True(AxisAlign.SharedLine(lowEdges, sizes, EdgeChoice.Low, 0, 600, out double line));

            Assert.Equal(185, line);                                     // the median, not the mean (171.3)
            Assert.Equal(0, AxisAlign.LowEdgeFor(line, sizes[0], EdgeChoice.Low) - lowEdges[0]);
            Assert.Equal(-3, AxisAlign.LowEdgeFor(line, sizes[1], EdgeChoice.Low) - lowEdges[1]);
            Assert.Equal(44, AxisAlign.LowEdgeFor(line, sizes[2], EdgeChoice.Low) - lowEdges[2]);
        }

        [Fact]
        public void A_high_edge_line_puts_every_member_the_same_distance_down()
        {
            var lowEdges = new[] { 100.0, 110.0 };
            var sizes = new[] { 50.0, 30.0 };

            Assert.True(AxisAlign.SharedLine(lowEdges, sizes, EdgeChoice.High, 0, 600, out double line));

            Assert.Equal(145, line);                                     // median of the tops: 150 and 140
            Assert.Equal(95, AxisAlign.LowEdgeFor(line, sizes[0], EdgeChoice.High));
            Assert.Equal(115, AxisAlign.LowEdgeFor(line, sizes[1], EdgeChoice.High));
        }

        [Fact]
        public void A_centre_line_splits_the_difference()
        {
            var lowEdges = new[] { 100.0, 200.0 };
            var sizes = new[] { 40.0, 40.0 };

            Assert.True(AxisAlign.SharedLine(lowEdges, sizes, EdgeChoice.Center, 0, 600, out double line));

            Assert.Equal(170, line);                                     // median of the centres: 120 and 220
            Assert.Equal(150, AxisAlign.LowEdgeFor(line, sizes[0], EdgeChoice.Center));
        }

        [Fact]
        public void The_line_is_clamped_so_the_tallest_member_stays_inside_the_area()
        {
            // The median bottom would be 580, which would push a 100-tall view out through 600.
            var lowEdges = new[] { 575.0, 580.0, 585.0 };
            var sizes = new[] { 100.0, 100.0, 100.0 };

            Assert.True(AxisAlign.SharedLine(lowEdges, sizes, EdgeChoice.Low, 0, 600, out double line));

            Assert.Equal(500, line);
        }

        [Fact]
        public void The_line_is_clamped_at_the_low_edge_too()
        {
            var lowEdges = new[] { -40.0, -30.0, -20.0 };
            var sizes = new[] { 50.0, 50.0, 50.0 };

            Assert.True(AxisAlign.SharedLine(lowEdges, sizes, EdgeChoice.Low, 10, 600, out double line));

            Assert.Equal(10, line);
        }

        [Fact]
        public void A_member_taller_than_the_whole_area_leaves_the_track_alone()
        {
            Assert.False(AxisAlign.SharedLine(new[] { 0.0 }, new[] { 700.0 }, EdgeChoice.Low, 0, 600, out _));
        }

        [Fact]
        public void Least_movement_picks_the_edge_with_the_smallest_total_travel()
        {
            // Tops already agree (200); bottoms do not. Aligning tops costs nothing.
            var lowEdges = new[] { 100.0, 150.0 };
            var sizes = new[] { 100.0, 50.0 };

            Assert.True(AxisAlign.LeastMovementEdge(lowEdges, sizes, 0, 600, out EdgeChoice best, out double line));

            Assert.Equal(EdgeChoice.High, best);
            Assert.Equal(200, line);
        }

        [Fact]
        public void Spread_shares_the_spare_room_equally_when_the_minimums_match()
        {
            var sizes = new[] { 100.0, 100.0, 100.0 };
            var gaps = new[] { 10.0, 10.0 };

            double[]? placed = AxisAlign.Spread(sizes, gaps, 0, 400);

            Assert.NotNull(placed);
            Assert.Equal(new[] { 0.0, 150.0, 300.0 }, placed);   // 100 of slack split over two gaps
        }

        [Fact]
        public void Spread_gives_a_padded_view_its_wider_gap_and_shares_the_rest_evenly()
        {
            var sizes = new[] { 100.0, 100.0, 100.0 };
            var gaps = new[] { 40.0, 10.0 };   // the first gap carries 30 of per-view clearance

            double[]? placed = AxisAlign.Spread(sizes, gaps, 0, 400);

            Assert.NotNull(placed);
            // 400 − 300 − 50 = 50 spare, 25 added to each gap: gaps become 65 and 35.
            Assert.Equal(new[] { 0.0, 165.0, 300.0 }, placed);
            Assert.Equal(65, placed![1] - (placed[0] + sizes[0]));
            Assert.Equal(35, placed[2] - (placed[1] + sizes[1]));
        }

        [Fact]
        public void Spread_refuses_rather_than_overlapping_when_the_span_is_too_short()
            => Assert.Null(AxisAlign.Spread(new[] { 100.0, 100.0 }, new[] { 10.0 }, 0, 150));

        [Fact]
        public void Spread_fills_the_span_exactly_when_it_only_just_fits()
        {
            double[]? placed = AxisAlign.Spread(new[] { 100.0, 100.0 }, new[] { 10.0 }, 0, 210);

            Assert.NotNull(placed);
            Assert.Equal(new[] { 0.0, 110.0 }, placed);
        }

        [Fact]
        public void Spread_puts_a_single_member_at_the_start_of_the_span()
            => Assert.Equal(new[] { 25.0 }, AxisAlign.Spread(new[] { 50.0 }, Array.Empty<double>(), 25, 400));

        [Fact]
        public void Spread_wants_one_gap_between_each_pair()
            => Assert.Throws<ArgumentException>(() =>
                AxisAlign.Spread(new[] { 10.0, 10.0, 10.0 }, new[] { 5.0 }, 0, 100));

        [Fact]
        public void Spread_never_reorders_what_it_is_given()
        {
            double[]? placed = AxisAlign.Spread(new[] { 50.0, 20.0, 80.0 }, new[] { 10.0, 10.0 }, 0, 300);

            Assert.NotNull(placed);
            Assert.Equal(placed!.OrderBy(p => p), placed);   // still increasing, i.e. the input order
        }
    }
}
