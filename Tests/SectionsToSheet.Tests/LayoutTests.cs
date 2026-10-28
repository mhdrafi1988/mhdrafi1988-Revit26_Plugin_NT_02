using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models;
using Xunit;

namespace SectionsToSheet.Tests
{
    /// <summary>Layout maths. Units are arbitrary here (the tool uses feet).</summary>
    public class LayoutTests
    {
        private const double Tol = 1e-9;
        private static readonly LayoutRect Area = new(0, 0, 621, 554);

        private static readonly (double, double)[] Sample =
        {
            (140, 80), (180, 95), (160, 110), (170, 100), (120, 90), (100, 70), (130, 85), (110, 75)
        };

        private static GridLayoutResult Run(ArrangeMode mode, VerticalMode vertical = VerticalMode.Top,
            LastRowMode lastRow = LastRowMode.Left, bool singlePage = false, IReadOnlyList<(double, double)> sizes = null, LayoutRect? area = null)
            => SheetGridLayout.Compute(sizes ?? Sample, area ?? Area, new GridLayoutOptions
            {
                Mode = mode,
                MinHorizontalGap = 20,
                MinVerticalGap = 20,
                Vertical = vertical,
                LastRow = lastRow,
                SinglePage = singlePage
            });

        [Fact]
        public void Grid_picks_most_columns_that_fit_and_columns_line_up()
        {
            var r = Run(ArrangeMode.Grid);

            // 4 columns need 140+180+160+170 + 3×20 = 710 > 621; 3 columns need 170+180+160 + 2×20 = 550.
            Assert.Equal(3, r.Columns);
            var page = Assert.Single(r.Pages);
            Assert.Equal(3, page.RowCount);

            // Same column → same left edge on every row.
            foreach (var column in page.Slots.GroupBy(s => s.Column))
                Assert.Single(column.Select(s => s.MinX).Distinct());

            // Columns spread to the full width: first flush left, gap = (621 − 510) / 2.
            Assert.Equal(0, page.Slots.Single(s => s.Row == 0 && s.Column == 0).MinX, 9);
            Assert.Equal(55.5, page.HorizontalGap.Value, 9);
            Assert.Equal(170 + 55.5, page.Slots.Single(s => s.Row == 0 && s.Column == 1).MinX, 9);
        }

        [Fact]
        public void Views_in_a_row_share_the_bottom_line_and_rows_start_at_the_top()
        {
            var page = Run(ArrangeMode.Grid).Pages[0];
            foreach (var row in page.Slots.GroupBy(s => s.Row))
                Assert.Single(row.Select(s => s.MinY).Distinct());

            // Row 0 is as tall as its tallest view (110) and hangs from the top edge.
            Assert.Equal(554 - 110, page.Slots.First(s => s.Row == 0).MinY, 9);
            // Row 1 (tallest 100) sits one minimum gap below.
            Assert.Equal(554 - 110 - 20 - 100, page.Slots.First(s => s.Row == 1).MinY, 9);
        }

        [Fact]
        public void Rows_wrap_like_text_and_are_justified()
        {
            var page = Run(ArrangeMode.Rows).Pages[0];

            // 140+20+180+20+160 = 520; adding 170 would give 710 > 621.
            var row0 = page.Slots.Where(s => s.Row == 0).ToList();
            Assert.Equal(new[] { 0, 1, 2 }, row0.Select(s => s.Index));
            Assert.Equal(0, row0[0].MinX, 9);
            Assert.Equal(621 - 160, row0[2].MinX, 9); // last view flush right

            // Row 1: 170, 120, 100, 130 (580); row 2: the last view alone.
            Assert.Equal(new[] { 3, 4, 5, 6 }, page.Slots.Where(s => s.Row == 1).Select(s => s.Index));
            var last = page.Slots.Single(s => s.Row == 2);
            Assert.Equal(7, last.Index);
            Assert.Equal(0, last.MinX, 9); // short last row, Left: starts at the left edge
        }

        [Fact]
        public void Single_view_row_is_centred_unless_it_is_a_left_aligned_last_row()
        {
            var one = Run(ArrangeMode.Rows, sizes: new[] { (100.0, 50.0) });
            Assert.Equal((621 - 100) / 2.0, one.Pages[0].Slots[0].MinX, 9);

            var centred = Run(ArrangeMode.Rows, lastRow: LastRowMode.Center).Pages[0].Slots.Single(s => s.Row == 2);
            Assert.Equal((621 - 110) / 2.0, centred.MinX, 9);
        }

        [Fact]
        public void Rows_wrap_when_the_next_view_would_not_fit_at_the_minimum_gap()
        {
            // 200 + 20 + 200 + 20 + 200 = 640 > 621, so the third view starts row 1.
            var sizes = new[] { (200.0, 50.0), (200.0, 50.0), (200.0, 50.0), (100.0, 50.0), (100.0, 50.0) };
            var page = Run(ArrangeMode.Rows, sizes: sizes).Pages[0];

            Assert.Equal(new[] { 0, 1 }, page.Slots.Where(s => s.Row == 0).Select(s => s.Index));
            Assert.Equal(new[] { 2, 3, 4 }, page.Slots.Where(s => s.Row == 1).Select(s => s.Index));
            Assert.Equal(2, page.RowCount);
        }

        [Fact]
        public void Last_row_left_keeps_previous_gap_when_it_has_several_views()
        {
            var sizes = new[] { (150.0, 50.0), (150.0, 50.0), (150.0, 50.0), (150.0, 50.0), (100.0, 50.0), (100.0, 50.0) };
            var page = Run(ArrangeMode.Rows, sizes: sizes).Pages[0];

            // Row 0: 4 × 150 = 600 + 3×20 = 660 > 621 → 3 per row: gap (621 − 450) / 2 = 85.5.
            // Row 1: 150 + 100 + 100 = 350 (fits), last row: gap = min(85.5, full 135.5) = 85.5, from the left.
            var row1 = page.Slots.Where(s => s.Row == 1).ToList();
            Assert.Equal(3, row1.Count);
            Assert.Equal(0, row1[0].MinX, 9);
            Assert.Equal(150 + 85.5, row1[1].MinX, 9);

            var justified = Run(ArrangeMode.Rows, lastRow: LastRowMode.Justify, sizes: sizes).Pages[0].Slots.Where(s => s.Row == 1).ToList();
            Assert.Equal(621 - 100, justified[2].MinX, 9);
        }

        [Fact]
        public void Rows_that_do_not_fit_in_the_height_go_to_the_next_sheet()
        {
            var sizes = Enumerable.Repeat((300.0, 200.0), 8).ToList();
            var r = Run(ArrangeMode.Grid, sizes: sizes);

            // 2 columns (300+20+300 = 620 ≤ 621); rows of 200: 2 rows fit (420 ≤ 554), 3 don't (640).
            Assert.Equal(2, r.Columns);
            Assert.Equal(2, r.Pages.Count);
            Assert.Equal(4, r.Pages[0].Slots.Count);
            Assert.All(r.Pages[1].Slots, s => Assert.Equal(1, s.Page));

            var single = Run(ArrangeMode.Grid, sizes: sizes, singlePage: true);
            Assert.Single(single.Pages);
            Assert.Equal(new[] { 4, 5, 6, 7 }, single.NoRoom);
        }

        [Fact]
        public void Views_bigger_than_the_area_are_reported_not_placed()
        {
            var r = Run(ArrangeMode.Grid, sizes: new[] { (700.0, 50.0), (100.0, 600.0), (100.0, 50.0) });
            Assert.Equal(new[] { 0, 1 }, r.TooBig);
            Assert.Equal(2, Assert.Single(r.Pages[0].Slots).Index);
        }

        [Fact]
        public void Centre_and_spread_place_the_rows_vertically()
        {
            var centre = Run(ArrangeMode.Grid, VerticalMode.Center).Pages[0];
            // Rows 110 + 100 + 85 = 295 + 2×20 = 335 → (554 − 335) / 2 = 109.5 from the top.
            Assert.Equal(554 - 109.5 - 110, centre.Slots.First(s => s.Row == 0).MinY, 9);

            var spread = Run(ArrangeMode.Grid, VerticalMode.Spread).Pages[0];
            Assert.Equal((554 - 295) / 2.0, spread.VerticalGap.Value, 9);
            Assert.Equal(0, spread.Slots.First(s => s.Row == 2).MinY, 9); // last row on the bottom edge
        }

        [Fact]
        public void Invalid_area_places_nothing()
        {
            var r = Run(ArrangeMode.Grid, area: new LayoutRect(0, 0, -1, 10));
            Assert.Empty(r.Pages);
            Assert.Equal(Sample.Length, r.TooBig.Count);
        }

        [Fact]
        public void Reading_order_bands_markers_into_rows_top_to_bottom_left_to_right()
        {
            var items = new List<ReadingItem>
            {
                new(50, 100, "C"),   // top row, middle
                new(10, 98, "A"),    // top row, left (within tolerance)
                new(90, 103, "B"),   // top row, right
                new(5, 40, "D"),     // second row
                new(null, null, "Section 10"),
                new(null, null, "Section 2")
            };
            var order = ReadingOrder.Sort(items, rowTolerance: 5);
            Assert.Equal(new[] { 1, 0, 2, 3, 5, 4 }, order);
        }

        [Fact]
        public void Natural_comparer_compares_numbers_as_numbers()
        {
            var names = new List<string> { "Section 10", "section 2", "Section 1", "Section 1A", "A" };
            names.Sort(NaturalStringComparer.Instance);
            Assert.Equal(new[] { "A", "Section 1", "Section 1A", "section 2", "Section 10" }, names);
        }

        [Fact]
        public void Sheet_numbers_count_up_and_skip_taken_ones()
        {
            Assert.Equal("SEC-010", SheetNumbering.Next("SEC-009", 1));
            Assert.Equal("A-2", SheetNumbering.Next("A", 1));
            Assert.Equal("S105", SheetNumbering.Next("S100", 5));

            var taken = new[] { "SEC-001", "sec-002", "SEC-004" };
            Assert.Equal("SEC-003", SheetNumbering.SuggestFrom("", taken));
            Assert.Equal("SEC-005", SheetNumbering.SuggestAfter("SEC-003", taken.Append("SEC-003")));
            Assert.Equal(new[] { "SEC-003", "SEC-005", "SEC-006" }, SheetNumbering.Sequence("SEC-003", 3, taken));
            Assert.Empty(SheetNumbering.Sequence(" ", 2, taken));
        }
    }
}
