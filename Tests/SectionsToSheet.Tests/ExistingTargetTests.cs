using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Services;
using Xunit;

namespace SectionsToSheet.Tests
{
    /// <summary>Placing on an existing sheet: free space and re-arrange.</summary>
    public class ExistingTargetTests
    {
        private const double F = 304.8; // mm per foot
        private const long SheetKey = 5000;

        // A1 title block 841 × 594 mm; margins T30 B150 L30 R200 → usable x 30..641, y 150..564 (Y up).
        private static readonly LayoutRect Frame = new(0, 0, 841 / F, 594 / F);

        private static LayoutRect Mm(double x, double y, double w, double h) => new(x / F, y / F, (x + w) / F, (y + h) / F);

        private static TargetItem Viewport(long key, string name, LayoutRect rect, string detail, string lockReason = null)
            => new()
            {
                Key = key, ViewKey = key + 1, Name = name, IsViewport = true, Rect = rect, DetailNumber = detail, Scale = 100,
                BoxCenterX = (rect.MinX + rect.MaxX) / 2, BoxCenterY = (rect.MinY + rect.MaxY) / 2 + 3 / F, LockReason = lockReason
            };

        private static TargetItem Schedule(LayoutRect rect) => new() { Key = 77, Name = "Door Schedule", Rect = rect };

        /// <summary>Key plan top-left (detail 1), legend next to it (detail 2), schedule top-right.</summary>
        private static TargetSheetContent Sheet(params TargetItem[] extra)
        {
            var items = new List<TargetItem>
            {
                Viewport(100, "Key Plan", Mm(30, 404, 220, 160), "1"),
                Viewport(200, "Legend", Mm(270, 474, 170, 90), "2"),
                Schedule(Mm(471, 394, 170, 170))
            };
            items.AddRange(extra);
            return new TargetSheetContent { Key = SheetKey, Number = "A-301", Name = "SECTIONS", Frame = Frame, Items = items };
        }

        private static SectionCandidate Section(long key, double across, long sheetKey = 0, string sheetNumber = null)
            => new() { Key = key, Name = "Wall Section " + key, Scale = 20, MarkerAcross = across, MarkerUp = 0, InSourceView = true,
                       CurrentSheetKey = sheetKey, CurrentSheetNumber = sheetNumber };

        private static PlanRequest Request(IEnumerable<SectionCandidate> sections, ExistingSheetMode mode, TargetSheetContent target = null,
            double w = 80, double h = 85, OverflowMode overflow = OverflowMode.AddSheets, string first = "A-302")
        {
            var list = sections.ToList();
            return new PlanRequest
            {
                Sections = list,
                Footprints = list.ToDictionary(s => s.Key, s => new Footprint { Width = w / F, Height = h / F, CenterOffsetX = w / 2 / F, CenterOffsetY = (h / 2 + 5) / F }),
                TitleBlockFrame = Frame,
                Target = target ?? Sheet(),
                SourceViewScale = 100,
                FirstSheetNumber = first,
                SheetName = "SECTIONS",
                ExistingSheetNumbers = new[] { "A-301" },
                Settings = new SectionsToSheetSettings
                {
                    Target = TargetMode.ExistingSheet, ExistingMode = mode, Mode = ArrangeMode.Rows, Overflow = overflow,
                    MarginTopMm = 30, MarginBottomMm = 150, MarginLeftMm = 30, MarginRightMm = 200,
                    MinHorizontalGapMm = 20, MinVerticalGapMm = 20
                }
            };
        }

        private static IEnumerable<SectionCandidate> Sections(int n) => Enumerable.Range(1, n).Select(i => Section(i, i));

        [Fact]
        public void Free_space_keeps_everything_and_fills_the_largest_empty_area()
        {
            var plan = SectionPlanner.Build(Request(Sections(8), ExistingSheetMode.FreeSpace));

            Assert.True(plan.CanApply, plan.Blocker);
            var sheet = Assert.Single(plan.Sheets);
            Assert.True(sheet.IsExisting);
            Assert.Equal("A-301", sheet.Number);
            Assert.Empty(plan.Rearranged);
            Assert.Equal(3, sheet.Obstacles.Count);
            Assert.Equal(0, plan.NewSheetCount);

            // Free area: below the schedule's gap (394 − 20 = 374) down to the bottom margin (150), full width.
            var free = sheet.FreeArea.Value;
            Assert.Equal(30, free.MinX * F, 6);
            Assert.Equal(641, free.MaxX * F, 6);
            Assert.Equal(150, free.MinY * F, 6);
            Assert.Equal(374, free.MaxY * F, 6);

            foreach (var v in sheet.Viewports)
            {
                Assert.True(v.Target.MinX >= free.MinX - 1e-9 && v.Target.MaxX <= free.MaxX + 1e-9);
                Assert.True(v.Target.MinY >= free.MinY - 1e-9 && v.Target.MaxY <= free.MaxY + 1e-9);
                Assert.DoesNotContain(sheet.Obstacles, o => o.Rect.Intersects(v.Target));
            }
            Assert.Equal(Enumerable.Range(3, 8), sheet.Viewports.Select(v => v.DetailNumber));
        }

        [Fact]
        public void Rearrange_moves_the_views_first_and_keeps_their_numbers_and_the_schedule()
        {
            var plan = SectionPlanner.Build(Request(Sections(8), ExistingSheetMode.Rearrange));

            Assert.True(plan.CanApply, plan.Blocker);
            var sheet = Assert.Single(plan.Sheets);
            Assert.Equal(new[] { "Key Plan", "Legend" }, sheet.Viewports.Take(2).Select(v => v.Name));
            Assert.Equal(new[] { "1", "2" }, plan.Rearranged.Select(v => v.DetailLabel));
            Assert.Equal(Enumerable.Range(3, 8), plan.Placed.Select(v => v.DetailNumber));
            Assert.Equal("Door Schedule", Assert.Single(sheet.Obstacles).Name);
            foreach (var v in sheet.Viewports)
                Assert.False(v.Target.Intersects(sheet.Obstacles[0].Rect), v.Name);

            // A moved viewport keeps its box centre at the same offset from its footprint.
            var key = plan.Rearranged[0];
            Assert.Equal(key.Target.MinX + 110 / F, key.CenterX, 9);
            Assert.Equal(key.Target.MinY + 83 / F, key.CenterY, 9);
        }

        [Fact]
        public void A_full_sheet_sends_the_sections_to_a_new_sheet_or_skips_them()
        {
            var full = new TargetSheetContent { Key = SheetKey, Number = "A-301", Frame = Frame, Items = { Schedule(Mm(0, 0, 841, 594)) } };

            var plan = SectionPlanner.Build(Request(Sections(3), ExistingSheetMode.FreeSpace, full));
            Assert.True(plan.CanApply, plan.Blocker);
            Assert.Null(plan.Sheets[0].FreeArea);
            Assert.Empty(plan.Sheets[0].Viewports);
            Assert.Equal("A-302", plan.Sheets[1].Number);
            Assert.Equal(3, plan.Sheets[1].Viewports.Count);
            Assert.Equal(new[] { 1, 2, 3 }, plan.Sheets[1].Viewports.Select(v => v.DetailNumber));

            var skip = SectionPlanner.Build(Request(Sections(3), ExistingSheetMode.FreeSpace, full, overflow: OverflowMode.Skip));
            Assert.False(skip.CanApply);
            Assert.Equal(3, skip.Skipped.Count);
            Assert.All(skip.Skipped, s => Assert.Contains("No room on A-301", s.Reason));
        }

        [Fact]
        public void Overflow_continues_on_a_new_sheet_and_checks_its_number()
        {
            // 20 sections of 80 × 85 don't all fit in the 611 × 224 mm free area.
            var plan = SectionPlanner.Build(Request(Sections(20), ExistingSheetMode.FreeSpace));
            Assert.True(plan.CanApply, plan.Blocker);
            Assert.Equal(2, plan.Sheets.Count);
            Assert.Equal(1, plan.NewSheetCount);
            Assert.Equal(20, plan.PlacedCount);
            Assert.Equal(1, plan.Sheets[1].Viewports[0].DetailNumber);

            var taken = SectionPlanner.Build(Request(Sections(20), ExistingSheetMode.FreeSpace, first: "A-301"));
            Assert.Contains("already exists", taken.Blocker);
        }

        [Fact]
        public void A_section_already_on_the_target_is_a_note_and_is_not_moved()
        {
            var sections = new[] { Section(1, 1), Section(2, 2, SheetKey, "A-301") };

            var free = SectionPlanner.Build(Request(sections, ExistingSheetMode.FreeSpace));
            var note = Assert.Single(free.Skipped);
            Assert.True(note.IsNote);
            Assert.Contains("stays where it is", note.Reason);
            Assert.Empty(free.Moves);
            Assert.Empty(free.SheetsLeftEmpty);

            var re = SectionPlanner.Build(Request(sections, ExistingSheetMode.Rearrange));
            Assert.True(Assert.Single(re.Skipped).IsNote);
            Assert.Contains("moves with the sheet", re.Skipped[0].Reason);
        }

        [Fact]
        public void New_detail_numbers_continue_after_the_highest_plain_number()
        {
            var target = Sheet(Viewport(300, "Detail A", Mm(30, 160, 60, 40), "A3"), Viewport(400, "Detail 7", Mm(100, 160, 60, 40), "7"));
            Assert.Equal(7, SectionPlanner.HighestDetailNumber(target.Items.Where(i => i.IsViewport)));

            var none = new[] { Viewport(1, "X", Mm(0, 0, 10, 10), "A3"), Viewport(2, "Y", Mm(0, 0, 10, 10), null) };
            Assert.Equal(0, SectionPlanner.HighestDetailNumber(none));
        }

        [Fact]
        public void Rearrange_blocks_when_an_existing_view_does_not_fit()
        {
            var huge = Sheet(Viewport(300, "Big Plan", Mm(30, 150, 430, 230), "3"));
            var plan = SectionPlanner.Build(Request(Sections(2), ExistingSheetMode.Rearrange, huge));
            Assert.False(plan.CanApply);
            Assert.Contains("don’t fit", plan.Blocker);
            Assert.Contains("Big Plan", plan.Blocker);
        }

        [Fact]
        public void Locked_target_blocks_and_locked_viewports_stay_put()
        {
            var locked = new TargetSheetContent { Key = SheetKey, Number = "A-301", Frame = Frame, LockReason = "sheet A-301 is owned by alex.k" };
            Assert.Contains("owned by alex.k", SectionPlanner.Build(Request(Sections(2), ExistingSheetMode.FreeSpace, locked)).Blocker);

            var oneLocked = Sheet();
            oneLocked.Items[0] = Viewport(100, "Key Plan", Mm(30, 404, 220, 160), "1", "the viewport is owned by sam.p");
            var plan = SectionPlanner.Build(Request(Sections(4), ExistingSheetMode.Rearrange, oneLocked));
            Assert.True(plan.CanApply, plan.Blocker);
            Assert.Equal(new[] { "Legend" }, plan.Rearranged.Select(v => v.Name));
            Assert.Contains(plan.Sheets[0].Obstacles, o => o.Name == "Key Plan");
            Assert.Contains(plan.Warnings, w => w.Contains("Key Plan") && w.Contains("sam.p"));
        }

        [Fact]
        public void No_target_yet_blocks_with_the_reason()
        {
            var r = Request(Sections(1), ExistingSheetMode.FreeSpace);
            var noTarget = new PlanRequest
            {
                Sections = r.Sections, Footprints = r.Footprints, TitleBlockFrame = Frame, Settings = r.Settings,
                TargetProblem = "Choose the sheet to place the sections on."
            };
            Assert.Equal("Choose the sheet to place the sections on.", SectionPlanner.Build(noTarget).Blocker);
        }

        [Fact]
        public void Signature_changes_with_the_mode_and_the_target()
        {
            string free = SectionPlanner.Build(Request(Sections(3), ExistingSheetMode.FreeSpace)).Signature;
            Assert.Equal(free, SectionPlanner.Build(Request(Sections(3), ExistingSheetMode.FreeSpace)).Signature);
            Assert.NotEqual(free, SectionPlanner.Build(Request(Sections(3), ExistingSheetMode.Rearrange)).Signature);

            var other = Sheet();
            var moved = new TargetSheetContent { Key = SheetKey + 1, Number = "A-305", Frame = Frame, Items = other.Items };
            Assert.NotEqual(free, SectionPlanner.Build(Request(Sections(3), ExistingSheetMode.FreeSpace, moved)).Signature);
        }

        [Fact]
        public void New_sheet_target_ignores_the_existing_sheet()
        {
            var r = Request(Sections(2), ExistingSheetMode.FreeSpace);
            r.Settings.Target = TargetMode.NewSheet;
            var plan = SectionPlanner.Build(r);
            Assert.True(plan.CanApply, plan.Blocker);
            Assert.False(Assert.Single(plan.Sheets).IsExisting);
            Assert.Null(plan.Target);
        }

        [Fact]
        public void Rearrange_all_sorts_the_sheet_views_and_the_sections_together()
        {
            // Plan order: the sheet's views have no marker in the source view, so they follow the marked sections.
            var plan = SectionPlanner.Build(Request(Sections(4), ExistingSheetMode.RearrangeAll));
            Assert.True(plan.CanApply, plan.Blocker);
            var sheet = Assert.Single(plan.Sheets);
            Assert.Equal(new[] { "Wall Section 1", "Wall Section 2", "Wall Section 3", "Wall Section 4", "Key Plan", "Legend" },
                         sheet.Viewports.Select(v => v.Name));
            Assert.Equal(new[] { "1", "2" }, plan.Rearranged.Select(v => v.DetailLabel));
            Assert.Equal(new[] { 3, 4, 5, 6 }, plan.Placed.Select(v => v.DetailNumber));

            // Size order: the larger sheet views lead.
            var bySize = Request(Sections(4), ExistingSheetMode.RearrangeAll);
            bySize.Settings.Order = SectionOrder.Size;
            Assert.Equal("Key Plan", SectionPlanner.Build(bySize).Sheets[0].Viewports[0].Name);

            // Existing first keeps the sheet's views in front.
            var first = SectionPlanner.Build(Request(Sections(4), ExistingSheetMode.Rearrange));
            Assert.Equal(new[] { "Key Plan", "Legend" }, first.Sheets[0].Viewports.Take(2).Select(v => v.Name));
        }

        [Fact]
        public void Rearrange_all_uses_the_marker_of_a_ticked_section_already_on_the_sheet()
        {
            // The legend's view (key 201) is also a ticked section whose marker sits left of section 1.
            var onSheet = new SectionCandidate { Key = 201, Name = "Legend", Scale = 20, MarkerAcross = 0, MarkerUp = 0,
                                                 InSourceView = true, CurrentSheetKey = SheetKey, CurrentSheetNumber = "A-301" };
            var sections = Sections(2).Prepend(onSheet).ToList();
            var r = Request(sections, ExistingSheetMode.RearrangeAll);
            var plan = SectionPlanner.Build(r);
            Assert.True(plan.CanApply, plan.Blocker);
            Assert.Equal(new[] { "Legend", "Wall Section 1", "Wall Section 2", "Key Plan" }, plan.Sheets[0].Viewports.Select(v => v.Name));
            Assert.True(Assert.Single(plan.Skipped).IsNote);
        }

        [Fact]
        public void Pinned_views_are_moved_in_both_rearrange_modes()
        {
            var target = Sheet();
            var key = target.Items[0];
            target.Items[0] = new TargetItem
            {
                Key = key.Key, ViewKey = key.ViewKey, Name = key.Name, IsViewport = true, Rect = key.Rect, DetailNumber = "1",
                BoxCenterX = key.BoxCenterX, BoxCenterY = key.BoxCenterY, IsPinned = true
            };
            foreach (var mode in new[] { ExistingSheetMode.Rearrange, ExistingSheetMode.RearrangeAll })
            {
                var plan = SectionPlanner.Build(Request(Sections(2), mode, target));
                Assert.Contains(plan.Rearranged, v => v.Existing.IsPinned && v.Name == "Key Plan");
            }
            Assert.Empty(SectionPlanner.Build(Request(Sections(2), ExistingSheetMode.FreeSpace, target)).Rearranged);
        }

        [Fact]
        public void Signature_differs_between_the_two_rearrange_modes()
        {
            Assert.NotEqual(SectionPlanner.Build(Request(Sections(3), ExistingSheetMode.Rearrange)).Signature,
                            SectionPlanner.Build(Request(Sections(3), ExistingSheetMode.RearrangeAll)).Signature);
        }
    }
}
