using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Services;
using System.Collections.Generic;
using Xunit;

namespace SheetAlignDistribute.Tests
{
    /// <summary>
    /// Measuring the margins and gaps a sheet already has. The detector takes feet and gives
    /// millimetres, so these cases build an A1 sheet in millimetres and convert.
    /// </summary>
    public class MarginDetectorTests
    {
        /// <summary>An A1 landscape title block, 841 × 594 mm, as a rectangle in feet.</summary>
        private static LayoutRect Frame()
            => new(0, 0, Units.MmToFeet(841), Units.MmToFeet(594));

        /// <summary>A rectangle from millimetres, measured from its bottom-left corner.</summary>
        private static LayoutRect Mm(double x, double y, double w, double h)
            => new(Units.MmToFeet(x), Units.MmToFeet(y), Units.MmToFeet(x + w), Units.MmToFeet(y + h));

        private static double Tolerance => Units.MmToFeet(25);

        [Fact]
        public void The_margins_are_the_space_between_the_title_block_and_the_outermost_views()
        {
            var views = new List<LayoutRect>
            {
                Mm(17, 406, 200, 175),   // leftmost and highest
                Mm(243, 409, 200, 175),
                Mm(24, 18, 200, 160)     // lowest
            };

            var detected = MarginDetector.Detect(Frame(), views, Tolerance, roundToMm: 0);

            Assert.True(detected.HasValues);
            Assert.Equal(17, detected.MarginLeftMm, 3);
            Assert.Equal(18, detected.MarginBottomMm, 3);
            Assert.Equal(594 - 584, detected.MarginTopMm, 3);    // highest top is 409 + 175 = 584
            Assert.Equal(841 - 443, detected.MarginRightMm, 3);  // rightmost right is 243 + 200 = 443
        }

        [Fact]
        public void Detected_values_are_rounded_to_the_step_when_one_is_given()
        {
            var views = new List<LayoutRect> { Mm(17, 18, 200, 175) };

            var detected = MarginDetector.Detect(Frame(), views, Tolerance, roundToMm: 5);

            Assert.Equal(15, detected.MarginLeftMm, 3);
            Assert.Equal(20, detected.MarginBottomMm, 3);
        }

        [Fact]
        public void A_sheet_whose_views_stop_short_of_the_title_strip_reads_that_as_the_right_margin()
        {
            // Views stop at 655 mm; the rest is the 180 mm title strip plus a little.
            var views = new List<LayoutRect> { Mm(20, 200, 200, 150), Mm(460, 200, 195, 150) };

            var detected = MarginDetector.Detect(Frame(), views, Tolerance, roundToMm: 5);

            Assert.Equal(185, detected.MarginRightMm, 3);
        }

        [Fact]
        public void The_horizontal_gap_is_the_median_of_the_gaps_inside_each_row()
        {
            // One row of four views, gaps of 20, 26 and 20 mm.
            var views = new List<LayoutRect>
            {
                Mm(20, 200, 100, 150), Mm(140, 200, 100, 150), Mm(266, 200, 100, 150), Mm(386, 200, 100, 150)
            };

            var detected = MarginDetector.Detect(Frame(), views, Tolerance, roundToMm: 0);

            Assert.Equal(20, detected.GapHorizontalMm!.Value, 3);
        }

        [Fact]
        public void The_vertical_gap_is_the_median_of_the_gaps_between_rows()
        {
            // Three rows: 30 mm between the first two, 20 mm between the next two.
            var views = new List<LayoutRect>
            {
                Mm(20, 400, 200, 150),
                Mm(20, 220, 200, 150),
                Mm(20, 60, 200, 140)
            };

            var detected = MarginDetector.Detect(Frame(), views, Tolerance, roundToMm: 0);

            Assert.Equal(25, detected.GapVerticalMm!.Value, 3);   // median of 30 and 20
        }

        [Fact]
        public void One_view_has_no_gaps_to_measure()
        {
            var detected = MarginDetector.Detect(Frame(), new List<LayoutRect> { Mm(20, 20, 100, 100) },
                Tolerance, roundToMm: 0);

            Assert.True(detected.HasValues);
            Assert.Null(detected.GapHorizontalMm);
            Assert.Null(detected.GapVerticalMm);
        }

        [Fact]
        public void Views_that_overlap_contribute_no_gap()
        {
            var views = new List<LayoutRect> { Mm(20, 200, 200, 150), Mm(180, 200, 200, 150) };

            var detected = MarginDetector.Detect(Frame(), views, Tolerance, roundToMm: 0);

            Assert.Null(detected.GapHorizontalMm);
        }

        [Fact]
        public void An_empty_sheet_has_nothing_to_measure()
        {
            Assert.False(MarginDetector.Detect(Frame(), new List<LayoutRect>(), Tolerance, 0).HasValues);
            Assert.False(MarginDetector.Detect(Frame(), null, Tolerance, 0).HasValues);
        }

        [Fact]
        public void A_frame_with_no_extent_has_nothing_to_measure()
        {
            var empty = new LayoutRect(0, 0, 0, 0);

            Assert.False(MarginDetector.Detect(empty, new List<LayoutRect> { Mm(10, 10, 50, 50) }, Tolerance, 0).HasValues);
        }
    }

    /// <summary>The millimetre / foot conversion and the rounding behind every "detected" number.</summary>
    public class UnitsTests
    {
        [Fact]
        public void A_foot_is_three_hundred_and_four_point_eight_millimetres()
            => Assert.Equal(304.8, Units.FeetToMm(1), 9);

        [Fact]
        public void The_conversion_goes_both_ways()
            => Assert.Equal(123.45, Units.FeetToMm(Units.MmToFeet(123.45)), 9);

        [Theory]
        [InlineData(17, 5, 15)]
        [InlineData(18, 5, 20)]
        [InlineData(22, 5, 20)]
        [InlineData(186, 5, 185)]
        [InlineData(12.5, 5, 15)]   // halfway rounds away from zero
        public void Rounding_snaps_to_the_step(double value, double step, double expected)
            => Assert.Equal(expected, Units.RoundToStep(value, step), 9);

        [Fact]
        public void A_step_of_zero_leaves_the_value_alone()
            => Assert.Equal(17.3, Units.RoundToStep(17.3, 0), 9);

        [Fact]
        public void Rounding_never_returns_a_negative_margin()
            => Assert.Equal(0, Units.RoundToStep(-4, 5), 9);

        [Fact]
        public void Display_values_keep_one_decimal()
            => Assert.Equal(5.7, Units.ForDisplay(5.6999), 9);
    }
}
