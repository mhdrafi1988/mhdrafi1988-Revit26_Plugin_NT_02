using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SheetAlignDistribute.Tests
{
    /// <summary>
    /// The whole layout decision. The engine is unit-agnostic, so these cases are written in
    /// millimetres on a 660 × 600 usable area, with Y up as on a sheet.
    /// </summary>
    public class AlignEngineTests
    {
        private static readonly LayoutRect Usable = new(0, 0, 660, 600);

        private static TrackBox Box(int key, double x, double y, double w, double h,
            double padLeft = 0, double padRight = 0, double padBottom = 0, double padTop = 0)
            => new(key, new LayoutRect(x, y, x + w, y + h), padLeft, padRight, padBottom, padTop);

        private static AlignEngineOptions Options(
            RowAlign rows = RowAlign.Bottom,
            ColumnAlign columns = ColumnAlign.Left,
            DistributeMode horizontal = DistributeMode.Off,
            DistributeMode vertical = DistributeMode.Off,
            double snap = 2, double maxMove = 25, bool reportOverLimit = true,
            bool keepInsideMargins = true, bool stopOnOverlap = true,
            double minGapH = 20, double minGapV = 20,
            LayoutRect? usable = null) => new()
            {
                Usable = usable ?? Usable,
                RowTolerance = 25,
                ColumnTolerance = 25,
                AlignRows = rows,
                AlignColumns = columns,
                DistributeHorizontal = horizontal,
                DistributeVertical = vertical,
                MinGapHorizontal = minGapH,
                MinGapVertical = minGapV,
                SnapTolerance = snap,
                MaxMove = maxMove,
                ReportOverLimit = reportOverLimit,
                KeepInsideMargins = keepInsideMargins,
                StopOnOverlap = stopOnOverlap
            };

        /// <summary>
        /// Eight views in a 3 × 3 near-grid, each a few millimetres off. The third view of the top
        /// row sits 42 mm low — the one case the move limit exists for.
        /// </summary>
        private static List<TrackBox> Sheet() => new()
        {
            Box(0, 20, 363, 200, 170),   Box(1, 243, 360, 200, 170), Box(2, 463, 318, 190, 100),
            Box(3, 24, 174, 200, 130),   Box(4, 243, 170, 185, 110), Box(5, 456, 166, 195, 130),
            Box(6, 20, 11, 200, 120),    Box(7, 240, 9, 200, 100)
        };

        private static BoxPlacement For(AlignEngineResult result, int key)
            => result.Placements.Single(p => p.Key == key);

        // ── The worked example ──────────────────────────────────────────────

        [Fact]
        public void A_near_grid_is_read_as_three_rows_and_three_columns()
        {
            var result = AlignEngine.Plan(Sheet(), null, Options());

            Assert.Equal(3, result.RowCount);
            Assert.Equal(3, result.ColumnCount);
            Assert.Null(result.Overlap);
            Assert.Empty(result.Notes);
        }

        [Fact]
        public void Each_view_is_told_which_row_and_column_it_is_in()
        {
            var result = AlignEngine.Plan(Sheet(), null, Options());

            Assert.Equal((1, 1), (For(result, 0).Row, For(result, 0).Column));
            Assert.Equal((1, 3), (For(result, 2).Row, For(result, 2).Column));
            Assert.Equal((2, 2), (For(result, 4).Row, For(result, 4).Column));
            Assert.Equal((3, 2), (For(result, 7).Row, For(result, 7).Column));
        }

        [Fact]
        public void A_row_ends_up_sharing_one_bottom_edge()
        {
            var result = AlignEngine.Plan(Sheet(), null, Options());

            // Row 2's bottoms were 174, 170 and 166; the median, 170, is the line.
            Assert.Equal(170, For(result, 3).Target.MinY, 6);
            Assert.Equal(170, For(result, 4).Target.MinY, 6);
            Assert.Equal(170, For(result, 5).Target.MinY, 6);
        }

        [Fact]
        public void A_column_ends_up_sharing_one_left_edge()
        {
            var result = AlignEngine.Plan(Sheet(), null, Options());

            // Column 1's lefts were 20, 24 and 20; the median, 20, is the line.
            Assert.Equal(20, For(result, 0).Target.MinX, 6);
            Assert.Equal(20, For(result, 3).Target.MinX, 6);
            Assert.Equal(20, For(result, 6).Target.MinX, 6);
        }

        [Fact]
        public void The_plan_moves_only_what_is_out_of_line()
        {
            var result = AlignEngine.Plan(Sheet(), null, Options());

            Assert.Equal(new[] { 0, 3, 5, 7 }, result.Placements
                .Where(p => p.Status == AlignStatus.Move).Select(p => p.Key).OrderBy(k => k));
            Assert.Equal(new[] { 1, 4, 6 }, result.Placements
                .Where(p => p.Status == AlignStatus.InPlace).Select(p => p.Key).OrderBy(k => k));
            Assert.Equal(new[] { 2 }, result.Placements
                .Where(p => p.Status == AlignStatus.OverLimit).Select(p => p.Key));
        }

        [Fact]
        public void Nothing_travels_more_than_a_few_millimetres()
        {
            var result = AlignEngine.Plan(Sheet(), null, Options());

            double largest = result.Placements.Where(p => p.Status == AlignStatus.Move).Max(p => p.Distance);
            Assert.InRange(largest, 5, 6);
        }

        // ── The limits ──────────────────────────────────────────────────────

        [Fact]
        public void A_view_past_the_move_limit_is_reported_and_left_exactly_where_it_was()
        {
            var sheet = Sheet();
            var result = AlignEngine.Plan(sheet, null, Options());

            var placement = For(result, 2);
            Assert.Equal(AlignStatus.OverLimit, placement.Status);
            Assert.Equal(sheet[2].Rect.MinX, placement.Target.MinX, 6);
            Assert.Equal(sheet[2].Rect.MinY, placement.Target.MinY, 6);
            Assert.InRange(placement.Distance, 42, 43);   // what it would have needed
            Assert.Contains("over the", placement.Note, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Raising_the_limit_lets_that_view_move_after_all()
        {
            var result = AlignEngine.Plan(Sheet(), null, Options(maxMove: 50));

            Assert.Equal(AlignStatus.Move, For(result, 2).Status);
            Assert.Equal(360, For(result, 2).Target.MinY, 6);   // row 1's line
        }

        [Fact]
        public void Turning_the_report_off_moves_it_anyway()
        {
            var result = AlignEngine.Plan(Sheet(), null, Options(reportOverLimit: false));

            Assert.Equal(AlignStatus.Move, For(result, 2).Status);
        }

        [Fact]
        public void A_zero_limit_means_no_limit()
        {
            var result = AlignEngine.Plan(Sheet(), null, Options(maxMove: 0));

            Assert.Equal(AlignStatus.Move, For(result, 2).Status);
        }

        [Fact]
        public void A_view_inside_the_snap_tolerance_is_left_untouched()
        {
            var sheet = Sheet();
            var result = AlignEngine.Plan(sheet, null, Options());

            var placement = For(result, 6);   // 1 mm out, snap is 2 mm
            Assert.Equal(AlignStatus.InPlace, placement.Status);
            Assert.Equal(sheet[6].Rect.MinY, placement.Target.MinY, 6);
            Assert.Equal(0, placement.Distance);
        }

        [Fact]
        public void A_tighter_snap_tolerance_moves_what_it_used_to_leave()
        {
            var result = AlignEngine.Plan(Sheet(), null, Options(snap: 0));

            Assert.Equal(AlignStatus.Move, For(result, 6).Status);
            Assert.Equal(AlignStatus.InPlace, For(result, 1).Status);   // this one is exactly on its lines
        }

        // ── Off ─────────────────────────────────────────────────────────────

        [Fact]
        public void Rows_off_leaves_every_Y_alone()
        {
            var sheet = Sheet();
            var result = AlignEngine.Plan(sheet, null, Options(rows: RowAlign.Off));

            foreach (var placement in result.Placements)
                Assert.Equal(sheet[placement.Key].Rect.MinY, placement.Target.MinY, 6);
        }

        [Fact]
        public void Columns_off_leaves_every_X_alone()
        {
            var sheet = Sheet();
            var result = AlignEngine.Plan(sheet, null, Options(columns: ColumnAlign.Off));

            foreach (var placement in result.Placements)
                Assert.Equal(sheet[placement.Key].Rect.MinX, placement.Target.MinX, 6);
        }

        [Fact]
        public void Both_off_is_a_plan_that_does_nothing()
        {
            var result = AlignEngine.Plan(Sheet(), null,
                Options(rows: RowAlign.Off, columns: ColumnAlign.Off));

            Assert.DoesNotContain(result.Placements, p => p.Status == AlignStatus.Move);
        }

        [Fact]
        public void A_view_with_no_partner_on_either_axis_says_so()
        {
            var boxes = new List<TrackBox> { Box(0, 20, 20, 100, 100), Box(1, 400, 400, 100, 100) };

            var result = AlignEngine.Plan(boxes, null, Options());

            Assert.All(result.Placements, p => Assert.Equal(AlignStatus.NoPartner, p.Status));
            Assert.All(result.Placements, p => Assert.Contains("no line to snap to", p.Note));
        }

        // ── Distribute ──────────────────────────────────────────────────────

        [Fact]
        public void Even_gaps_equalises_a_row_without_moving_its_outer_edges()
        {
            // Three 100-wide views between 0 and 400, bunched to the left.
            var boxes = new List<TrackBox>
            {
                Box(0, 0, 100, 100, 100), Box(1, 110, 100, 100, 100), Box(2, 300, 100, 100, 100)
            };

            var result = AlignEngine.Plan(boxes, null,
                Options(columns: ColumnAlign.Off, horizontal: DistributeMode.EvenGaps, maxMove: 0, minGapH: 10));

            Assert.Equal(0, For(result, 0).Target.MinX, 6);     // the outer edges hold
            Assert.Equal(300, For(result, 2).Target.MinX, 6);
            Assert.Equal(150, For(result, 1).Target.MinX, 6);   // the middle one is centred
        }

        [Fact]
        public void Fit_margins_spreads_a_row_from_one_margin_to_the_other()
        {
            var boxes = new List<TrackBox>
            {
                Box(0, 30, 100, 100, 100), Box(1, 200, 100, 100, 100), Box(2, 400, 100, 100, 100)
            };

            var result = AlignEngine.Plan(boxes, null,
                Options(columns: ColumnAlign.Off, horizontal: DistributeMode.FitMargins, maxMove: 0));

            Assert.Equal(0, For(result, 0).Target.MinX, 6);
            Assert.Equal(560, For(result, 2).Target.MinX, 6);   // 660 − 100
            Assert.Equal(280, For(result, 1).Target.MinX, 6);
        }

        [Fact]
        public void A_view_carrying_its_own_clearance_keeps_a_wider_gap_than_its_neighbours()
        {
            // The middle view asks for 30 mm more on its left than the sheet gap.
            var boxes = new List<TrackBox>
            {
                Box(0, 0, 100, 100, 100),
                Box(1, 150, 100, 100, 100, padLeft: 30),
                Box(2, 300, 100, 100, 100)
            };

            var result = AlignEngine.Plan(boxes, null,
                Options(columns: ColumnAlign.Off, horizontal: DistributeMode.EvenGaps, maxMove: 0, minGapH: 10));

            double firstGap = For(result, 1).Target.MinX - For(result, 0).Target.MaxX;
            double secondGap = For(result, 2).Target.MinX - For(result, 1).Target.MaxX;

            Assert.True(firstGap > secondGap, $"the padded side should be wider: {firstGap} vs {secondGap}");
            Assert.Equal(30, firstGap - secondGap, 6);
        }

        [Fact]
        public void A_row_too_narrow_for_its_views_keeps_its_own_spacing_and_says_why()
        {
            var narrow = new LayoutRect(0, 0, 150, 600);
            var boxes = new List<TrackBox> { Box(0, 0, 100, 100, 100), Box(1, 20, 100, 100, 100) };

            var result = AlignEngine.Plan(boxes, null, Options(
                columns: ColumnAlign.Off, horizontal: DistributeMode.FitMargins,
                keepInsideMargins: false, stopOnOverlap: false, maxMove: 0, usable: narrow));

            Assert.Contains(result.Notes, note => note.Contains("more width"));
            Assert.Equal(0, For(result, 0).Target.MinX, 6);     // left where they were
            Assert.Equal(20, For(result, 1).Target.MinX, 6);
        }

        [Fact]
        public void Distributing_down_the_sheet_moves_each_row_as_one_block()
        {
            var sheet = Sheet();
            var result = AlignEngine.Plan(sheet, null,
                Options(vertical: DistributeMode.FitMargins, maxMove: 0));

            // Row 2's three views still share one bottom edge after the rows were spread.
            double bottom = For(result, 3).Target.MinY;
            Assert.Equal(bottom, For(result, 4).Target.MinY, 6);
            Assert.Equal(bottom, For(result, 5).Target.MinY, 6);
        }

        // ── Safety ──────────────────────────────────────────────────────────

        /// <summary>
        /// Alignment on its own can never leave the margins, because the shared line is clamped to
        /// keep the largest member of the track inside. The guard earns its keep once Distribute is
        /// on and a view already sits outside margins the user has since tightened.
        /// </summary>
        private static readonly LayoutRect Tightened = new(0, 0, 250, 600);

        private static List<TrackBox> RowWithOneViewOutsideTheMargins() => new()
        {
            Box(0, 0, 100, 80, 80),
            Box(1, 90, 100, 80, 80),
            Box(2, 400, 100, 80, 80)   // outside the 250 mm right margin
        };

        [Fact]
        public void A_view_whose_distributed_place_would_leave_the_margins_stays_put()
        {
            var boxes = RowWithOneViewOutsideTheMargins();

            var result = AlignEngine.Plan(boxes, null, Options(
                columns: ColumnAlign.Off, horizontal: DistributeMode.EvenGaps,
                snap: 0, maxMove: 0, stopOnOverlap: false, usable: Tightened));

            // Evening the row out would put the middle view at 200..280, past the 250 mm margin.
            var blocked = For(result, 1);
            Assert.Equal(AlignStatus.Blocked, blocked.Status);
            Assert.Contains("cross the margins", blocked.Note);
            Assert.Equal(boxes[1].Rect.MinX, blocked.Target.MinX, 6);   // left exactly where it was
        }

        [Fact]
        public void Turning_the_margin_guard_off_lets_it_move_out()
        {
            var result = AlignEngine.Plan(RowWithOneViewOutsideTheMargins(), null, Options(
                columns: ColumnAlign.Off, horizontal: DistributeMode.EvenGaps,
                snap: 0, maxMove: 0, keepInsideMargins: false, stopOnOverlap: false, usable: Tightened));

            Assert.DoesNotContain(result.Placements, p => p.Status == AlignStatus.Blocked);
            Assert.Equal(200, For(result, 1).Target.MinX, 6);
        }

        [Fact]
        public void Aligning_alone_never_puts_a_view_outside_the_margins()
        {
            // The same tightened sheet, with Distribute off: the clamp does the work and nothing is blocked.
            var result = AlignEngine.Plan(RowWithOneViewOutsideTheMargins(), null, Options(
                snap: 0, maxMove: 0, stopOnOverlap: false, usable: Tightened));

            Assert.DoesNotContain(result.Placements, p => p.Status == AlignStatus.Blocked);
        }

        [Fact]
        public void Two_views_that_would_end_up_on_top_of_each_other_are_reported()
        {
            // Two views in the same row and the same column: aligning both edges stacks them.
            var boxes = new List<TrackBox>
            {
                Box(0, 100, 100, 100, 100),
                Box(1, 110, 110, 100, 100)
            };

            var result = AlignEngine.Plan(boxes, null, Options(snap: 0, maxMove: 0));

            Assert.NotNull(result.Overlap);
            Assert.Equal((0, 1), result.Overlap!.Value);
        }

        [Fact]
        public void A_view_moving_into_one_that_is_not_moving_is_caught_too()
        {
            // 0 and 1 share a row; aligning 1 to the row's left column line lands it on the frozen box.
            var boxes = new List<TrackBox> { Box(0, 0, 100, 100, 100), Box(1, 300, 104, 100, 100) };
            var frozen = new List<TrackBox> { Box(99, 302, 100, 100, 100) };

            var result = AlignEngine.Plan(boxes, frozen, Options(columns: ColumnAlign.Off, snap: 0, maxMove: 0));

            Assert.NotNull(result.Overlap);
            Assert.Contains(99, new[] { result.Overlap!.Value.A, result.Overlap.Value.B });
        }

        [Fact]
        public void Views_that_only_touch_edges_are_not_an_overlap()
        {
            var boxes = new List<TrackBox> { Box(0, 0, 100, 100, 100), Box(1, 100, 100, 100, 100) };

            var result = AlignEngine.Plan(boxes, null, Options(snap: 0, maxMove: 0));

            Assert.Null(result.Overlap);
        }

        [Fact]
        public void Turning_the_overlap_guard_off_stops_the_check()
        {
            var boxes = new List<TrackBox> { Box(0, 100, 100, 100, 100), Box(1, 110, 110, 100, 100) };

            var result = AlignEngine.Plan(boxes, null, Options(snap: 0, maxMove: 0, stopOnOverlap: false));

            Assert.Null(result.Overlap);
        }

        // ── Edges ───────────────────────────────────────────────────────────

        [Fact]
        public void Nothing_in_gives_an_empty_plan_rather_than_an_error()
        {
            var result = AlignEngine.Plan(new List<TrackBox>(), null, Options());

            Assert.Empty(result.Placements);
            Assert.Equal(0, result.RowCount);
        }

        [Fact]
        public void The_options_are_required()
            => Assert.Throws<ArgumentNullException>(() => AlignEngine.Plan(Sheet(), null, null!));

        [Fact]
        public void Every_input_box_gets_exactly_one_placement()
        {
            var result = AlignEngine.Plan(Sheet(), null, Options());

            Assert.Equal(8, result.Placements.Count);
            Assert.Equal(Enumerable.Range(0, 8), result.Placements.Select(p => p.Key).OrderBy(k => k));
        }

        [Fact]
        public void Planning_twice_over_gives_the_same_answer()
        {
            var first = AlignEngine.Plan(Sheet(), null, Options());
            var second = AlignEngine.Plan(Sheet(), null, Options());

            Assert.Equal(first.Placements.Select(p => (p.Key, p.Target.MinX, p.Target.MinY, p.Status)),
                         second.Placements.Select(p => (p.Key, p.Target.MinX, p.Target.MinY, p.Status)));
        }

        /// <summary>
        /// Running the tool again on a sheet it has just aligned must do nothing, or a second click
        /// would nudge the layout a little further every time.
        /// </summary>
        [Fact]
        public void Running_it_again_on_an_aligned_sheet_changes_nothing()
        {
            var options = Options();
            var first = AlignEngine.Plan(Sheet(), null, options);

            var aligned = first.Placements.Select(p => new TrackBox(p.Key, p.Target)).ToList();
            var second = AlignEngine.Plan(aligned, null, options);

            Assert.DoesNotContain(second.Placements, p => p.Status == AlignStatus.Move);
        }
    }
}
