using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Services;
using Xunit;

namespace SectionsToSheet.Tests
{
    public class PlannerTests
    {
        private const double MmPerFoot = 304.8;

        // A1 title block: 841 × 594 mm.
        private static readonly LayoutRect Frame = new(0, 0, 841 / MmPerFoot, 594 / MmPerFoot);

        private static SectionCandidate Section(long key, string name, double across, double up, int scale = 50,
            long sheetKey = 0, string sheetNumber = null, string lockReason = null)
            => new()
            {
                Key = key, Name = name, Scale = scale, MarkerAcross = across, MarkerUp = up, InSourceView = true,
                CurrentSheetKey = sheetKey, CurrentSheetNumber = sheetNumber, LockReason = lockReason
            };

        private static Footprint Size(double wMm, double hMm)
            => new() { Width = wMm / MmPerFoot, Height = hMm / MmPerFoot, CenterOffsetX = wMm / 2 / MmPerFoot, CenterOffsetY = (hMm / 2 + 7) / MmPerFoot };

        private static PlanRequest Request(IEnumerable<SectionCandidate> sections, SectionsToSheetSettings settings = null,
            Dictionary<long, SheetContent> sheets = null, string first = "SEC-001", string[] existing = null)
        {
            var list = sections.ToList();
            return new PlanRequest
            {
                Sections = list,
                Footprints = list.ToDictionary(s => s.Key, s => Size(100 + s.Key * 10, 60 + s.Key * 5)),
                TitleBlockFrame = Frame,
                Sheets = sheets ?? new Dictionary<long, SheetContent>(),
                ExistingSheetNumbers = existing ?? new string[0],
                SourceViewScale = 100,
                FirstSheetNumber = first,
                SheetName = "SECTIONS",
                Settings = settings ?? new SectionsToSheetSettings
                {
                    MarginTopMm = 20, MarginBottomMm = 20, MarginLeftMm = 20, MarginRightMm = 200
                }
            };
        }

        [Fact]
        public void Places_in_plan_order_with_detail_numbers_and_box_centre_offsets()
        {
            // Markers: 2 is top-left, 1 top-right, 3 below.
            var sections = new[]
            {
                Section(1, "Section 1", 30, 10),
                Section(2, "Section 2", 5, 10.2),
                Section(3, "Section 3", 10, 2)
            };
            var plan = SectionPlanner.Build(Request(sections));

            Assert.True(plan.CanApply, plan.Blocker);
            var sheet = Assert.Single(plan.Sheets);
            Assert.Equal("SEC-001", sheet.Number);
            Assert.Equal(new long[] { 2, 1, 3 }, sheet.Viewports.Select(v => v.Section.Key));
            Assert.Equal(new[] { 1, 2, 3 }, sheet.Viewports.Select(v => v.DetailNumber));

            var v0 = sheet.Viewports[0];
            Assert.Equal(v0.Target.MinX + 120 / 2.0 / MmPerFoot, v0.CenterX, 9);
            Assert.Equal(v0.Target.MinY + (70 / 2.0 + 7) / MmPerFoot, v0.CenterY, 9);
            Assert.True(v0.Target.MinX >= 20 / MmPerFoot - 1e-9);
        }

        [Fact]
        public void Locked_and_not_removable_sections_are_skipped_with_a_reason()
        {
            var sections = new[]
            {
                Section(1, "Section 1", 0, 0, sheetKey: 900, sheetNumber: "A-101"),
                Section(2, "Section 2", 1, 0, sheetKey: 901, sheetNumber: "A-104", lockReason: "A-104 is owned by alex.k"),
                Section(3, "Section 3", 2, 0)
            };

            var plan = SectionPlanner.Build(Request(sections));
            Assert.Equal(new long[] { 1, 3 }, plan.Viewports.Select(v => v.Section.Key));
            Assert.Contains("owned by alex.k", Assert.Single(plan.Skipped).Reason);
            Assert.Single(plan.Moves);

            var keep = Request(sections).Settings;
            keep.RemoveFromOldSheets = false;
            var noRemove = SectionPlanner.Build(Request(sections, keep));
            Assert.Equal(new long[] { 3 }, noRemove.Viewports.Select(v => v.Section.Key));
            Assert.Contains(noRemove.Skipped, s => s.Section.Key == 1 && s.Reason.Contains("A-101"));
        }

        [Fact]
        public void Reports_old_sheets_left_empty()
        {
            var sections = new[]
            {
                Section(1, "Section 1", 0, 0, sheetKey: 900, sheetNumber: "A-101"),
                Section(2, "Section 2", 1, 0, sheetKey: 901, sheetNumber: "A-103")
            };
            var sheets = new Dictionary<long, SheetContent>
            {
                [900] = new() { Key = 900, Number = "A-101", ViewportCount = 3 },
                [901] = new() { Key = 901, Number = "A-103", ViewportCount = 1 }
            };
            var plan = SectionPlanner.Build(Request(sections, sheets: sheets));
            Assert.Equal("A-103", Assert.Single(plan.SheetsLeftEmpty).Number);
        }

        [Fact]
        public void Blocks_when_the_sheet_number_exists_or_is_blank()
        {
            var sections = new[] { Section(1, "Section 1", 0, 0) };
            Assert.Contains("already exists", SectionPlanner.Build(Request(sections, first: "sec-001", existing: new[] { "SEC-001" })).Blocker);
            Assert.Contains("number", SectionPlanner.Build(Request(sections, first: "  ")).Blocker);
            Assert.False(SectionPlanner.Build(Request(sections, first: "  ")).CanApply);
        }

        [Fact]
        public void Blocks_without_a_usable_area_or_anything_to_place()
        {
            var tight = new SectionsToSheetSettings { MarginLeftMm = 500, MarginRightMm = 400 };
            Assert.Contains("usable area", SectionPlanner.Build(Request(new[] { Section(1, "S", 0, 0) }, tight)).Blocker);

            var none = SectionPlanner.Build(Request(new SectionCandidate[0]));
            Assert.Equal("Tick at least one section.", none.Blocker);

            var noFrame = Request(new[] { Section(1, "S", 0, 0) });
            noFrame = new PlanRequest
            {
                Sections = noFrame.Sections, Footprints = noFrame.Footprints, FrameProblem = "No title block.",
                FirstSheetNumber = "X", SheetName = "Y"
            };
            Assert.Equal("No title block.", SectionPlanner.Build(noFrame).Blocker);
        }

        [Fact]
        public void Overflow_adds_numbered_sheets_or_skips()
        {
            var sections = Enumerable.Range(1, 30).Select(i => Section(i, "Section " + i, i, 0)).ToList();
            var request = Request(sections, existing: new[] { "SEC-002" });
            var plan = SectionPlanner.Build(request);
            Assert.True(plan.Sheets.Count > 1);
            Assert.Equal("SEC-001", plan.Sheets[0].Number);
            Assert.Equal("SEC-003", plan.Sheets[1].Number);
            Assert.Equal("SECTIONS (2)", plan.Sheets[1].Name);
            Assert.All(plan.Sheets, s => Assert.Equal(1, s.Viewports[0].DetailNumber));

            var skip = request.Settings;
            skip.Overflow = OverflowMode.Skip;
            var single = SectionPlanner.Build(Request(sections, skip, existing: new[] { "SEC-002" }));
            Assert.Single(single.Sheets);
            Assert.Equal(30, single.PlacedCount + single.Skipped.Count);
            Assert.All(single.Skipped, s => Assert.StartsWith("No room", s.Reason));
        }

        [Fact]
        public void Warns_about_mixed_scales_and_orders_by_scale()
        {
            var sections = new[]
            {
                Section(1, "Section 1", 0, 0, scale: 50),
                Section(2, "Section 2", 1, 0, scale: 20),
                Section(3, "Section 3", 2, 0, scale: 50)
            };
            var byScale = Request(sections).Settings;
            byScale.Order = SectionOrder.Scale;
            var plan = SectionPlanner.Build(Request(sections, byScale));
            Assert.Equal(new long[] { 2, 1, 3 }, plan.Viewports.Select(v => v.Section.Key));
            Assert.Equal("Mixed scales on the new sheet: 1:20 ×1, 1:50 ×2.", Assert.Single(plan.Warnings));
        }

        [Fact]
        public void Signature_is_stable_and_changes_with_the_outcome()
        {
            var sections = new[] { Section(1, "Section 1", 0, 0), Section(2, "Section 2", 1, 0) };
            string a = SectionPlanner.Build(Request(sections)).Signature;
            Assert.Equal(a, SectionPlanner.Build(Request(sections)).Signature);

            var moved = new[] { Section(1, "Section 1", 0, 0, sheetKey: 5, sheetNumber: "A-1"), Section(2, "Section 2", 1, 0) };
            Assert.NotEqual(a, SectionPlanner.Build(Request(moved)).Signature);
            Assert.NotEqual(a, SectionPlanner.Build(Request(sections, first: "SEC-009")).Signature);
        }

        [Fact]
        public void Settings_load_old_or_broken_files_with_defaults()
        {
            var fromV0 = JsonSerializer.Deserialize<SectionsToSheetSettings>("{\"MarginTopMm\": 12, \"Mode\": 7, \"MinHorizontalGapMm\": -3}").Normalize();
            Assert.Equal(12, fromV0.MarginTopMm);
            Assert.Equal(ArrangeMode.Grid, fromV0.Mode);
            Assert.Equal(20, fromV0.MinHorizontalGapMm);
            Assert.Equal(SectionsToSheetSettings.CurrentVersion, fromV0.Version);
            Assert.True(fromV0.RemoveFromOldSheets);
            Assert.False(fromV0.DeleteEmptySheets);

            var roundTrip = JsonSerializer.Deserialize<SectionsToSheetSettings>(JsonSerializer.Serialize(new SectionsToSheetSettings { Order = SectionOrder.Size }));
            Assert.Equal(SectionOrder.Size, roundTrip.Order);
        }
    }
}
