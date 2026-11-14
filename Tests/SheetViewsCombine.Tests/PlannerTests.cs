using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Models;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SheetViewsCombine.Tests
{
    public class PackerTests
    {
        private static double F(double mm) => mm / 304.8;
        private static SheetRect Region => new(0, 0, F(700), F(560));

        [Fact]
        public void Rows_PlacesLeftToRightThenDown()
        {
            var items = new[] { new PackItem(1, F(300), F(200)), new PackItem(2, F(300), F(100)), new PackItem(3, F(300), F(100)) };
            var r = CombinePacker.Pack(Region, new SheetRect[0], items, F(10), F(10), PackStrategy.Rows);

            Assert.Empty(r.Unplaced);
            var a = r.Find(1).Value; var b = r.Find(2).Value; var c = r.Find(3).Value;
            Assert.Equal(Region.MaxY, a.MaxY, 6);
            Assert.Equal(a.MaxY, b.MaxY, 6);           // same row
            Assert.True(b.MinX >= a.MaxX + F(10) - 1e-9);
            Assert.True(c.MaxY <= a.MinY - F(10) + 1e-9); // next row below the tallest
        }

        [Fact]
        public void Rows_SkipsPastObstacles()
        {
            var obstacle = new SheetRect(F(100), F(400), F(250), F(560));
            var items = new[] { new PackItem(1, F(120), F(100)), new PackItem(2, F(120), F(100)) };
            var r = CombinePacker.Pack(Region, new[] { obstacle }, items, F(10), F(10), PackStrategy.Rows);

            Assert.Empty(r.Unplaced);
            foreach (var p in r.Placed)
                Assert.False(p.Value.Overlaps(obstacle.Grow(F(10) - 1e-9, F(10) - 1e-9)));
        }

        [Fact]
        public void TooBigItem_IsUnplaced_OthersStillPlaced()
        {
            var items = new[] { new PackItem(1, F(800), F(100)), new PackItem(2, F(100), F(100)) };
            var r = CombinePacker.Pack(Region, new SheetRect[0], items, F(10), F(10), PackStrategy.Rows);
            Assert.Equal(new long[] { 1 }, r.Unplaced);
            Assert.NotNull(r.Find(2));
        }

        [Theory]
        [InlineData(PackStrategy.Rows)]
        [InlineData(PackStrategy.MaxFill)]
        public void NothingOverlaps_AndEverythingStaysInside(PackStrategy strategy)
        {
            var obstacles = new[] { new SheetRect(F(500), F(0), F(700), F(150)) };
            var items = Enumerable.Range(1, 12).Select(i => new PackItem(i, F(60 + i * 7), F(40 + (i % 4) * 25))).ToList();
            var r = CombinePacker.Pack(Region, obstacles, items, F(8), F(8), strategy);

            var all = r.Placed.Select(p => p.Value).ToList();
            foreach (var p in all)
            {
                Assert.True(Region.Contains(p, 1e-6));
                Assert.False(p.Overlaps(obstacles[0]));
            }
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                    Assert.False(all[i].Overlaps(all[j]));
            Assert.Equal(12, r.Placed.Count + r.Unplaced.Count);
        }
    }

    public class PlannerTests
    {
        private static double F(double mm) => mm / 304.8;

        private static PlacedView V(long key, long sheet, string sheetNo, string name, double xMm, double yMm, double wMm, double hMm,
            string detail, bool legend = false, string lockReason = null, long viewKey = 0)
            => new()
            {
                ViewportKey = key,
                ViewKey = viewKey == 0 ? key + 1000 : viewKey,
                Name = name,
                ViewTypeLabel = legend ? "Legend" : "Floor Plan",
                TypeOrder = legend ? 20 : 0,
                IsLegend = legend,
                SheetKey = sheet,
                SheetNumber = sheetNo,
                DetailNumber = detail,
                Footprint = new SheetRect(F(xMm), F(yMm), F(xMm + wMm), F(yMm + hMm)),
                CenterOffsetX = F(wMm / 2),
                CenterOffsetY = F(hMm / 2) + F(5),
                LockReason = lockReason
            };

        private static SheetContent Sheet(long key, string number, params PlacedView[] views) => new()
        {
            Key = key,
            Number = number,
            Name = "Sheet " + number,
            TitleBlockTypeKey = 77,
            Frame = new SheetRect(0, 0, F(841), F(594)),
            Views = views.ToList()
        };

        /// <summary>The sample from the UI preview.</summary>
        private static (SheetContent S1, SheetContent S2) Sample(string lockEaves = null)
        {
            var s1 = Sheet(1, "A-101",
                V(11, 1, "A-101", "Level 01 - Floor Plan", 30, 200, 480, 340, "1"),
                V(12, 1, "A-101", "Stair 1", 530, 400, 180, 140, "2"),
                V(13, 1, "A-101", "Key Plan", 585, 34, 120, 90, "3"));
            var s2 = Sheet(2, "A-501",
                V(21, 2, "A-501", "Section A-A", 30, 400, 300, 120, "1"),
                V(22, 2, "A-501", "Section B-B", 30, 250, 300, 120, "2"),
                V(23, 2, "A-501", "Typical Eaves Detail", 350, 380, 150, 150, "3", lockReason: lockEaves),
                V(24, 2, "A-501", "Parapet Flashing", 350, 200, 140, 110, "4"),
                V(25, 2, "A-501", "Roof Build-up Legend", 525, 249, 120, 160, "5", legend: true));
            return (s1, s2);
        }

        private static CombineRequest Request(SheetContent s1, SheetContent s2, SheetViewsCombineSettings s, params long[] ticked) => new()
        {
            Sheet1 = s1,
            Sheet2 = s2,
            Ticked = ticked,
            Settings = s ?? new SheetViewsCombineSettings(),
            ExistingSheetNumbers = new[] { "A-101", "A-501", "A-101.1" }
        };

        [Fact]
        public void PreviewSample_GivesExpectedActionsAndDetailNumbers()
        {
            var (s1, s2) = Sample();
            var preview = new SheetViewsCombineSettings { MarginLeftMm = 15, MarginRightMm = 130, MarginTopMm = 15, MarginBottomMm = 15 };
            var plan = CombinePlanner.Build(Request(s1, s2, preview, 11, 12, 21, 23, 25));

            Assert.Null(plan.Blocker);
            Assert.True(plan.CanApply);
            Assert.Single(plan.Sheets);
            Assert.Equal(2, plan.MoveCount);
            Assert.Equal(2, plan.RepackCount);
            Assert.Equal(1, plan.CopyCount);
            Assert.Equal(1, plan.StayCount);

            string Detail(long key) => plan.Items.Single(i => i.View.ViewportKey == key).DetailNumber;
            Assert.Equal("1", Detail(11));
            Assert.Equal("2", Detail(12));
            Assert.Equal("5", Detail(25)); // legend keeps 5: free on A-101
            Assert.Equal("4", Detail(21)); // 1 is taken → lowest free
            Assert.Equal("6", Detail(23)); // 3 is taken → next free after 4, 5

            // Legend copy at the exact spot it has on A-501.
            var legend = plan.Items.Single(i => i.View.ViewportKey == 25);
            Assert.Equal(legend.View.Footprint, legend.Target);

            // Nothing on the target overlaps.
            var rects = plan.Target.Views.Select(v => v.Target.Value).ToList();
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                    Assert.False(rects[i].Overlaps(rects[j]), $"{plan.Target.Views[i].View.Name} overlaps {plan.Target.Views[j].View.Name}");

            // Unticked views elsewhere do nothing; the unticked target view stays put.
            Assert.Equal(ViewAction.None, plan.Items.Single(i => i.View.ViewportKey == 22).Action);
            var key = plan.Items.Single(i => i.View.ViewportKey == 13);
            Assert.Equal(ViewAction.StaysInPlace, key.Action);
            Assert.Equal(key.View.Footprint, key.Target);
        }

        [Fact]
        public void RepackAll_IncludesUntickedTargetViews()
        {
            var (s1, s2) = Sample();
            var plan = CombinePlanner.Build(Request(s1, s2, new SheetViewsCombineSettings { Unticked = UntickedTargetMode.RepackAll }, 21));
            Assert.Equal(3, plan.RepackCount);
            Assert.Equal(0, plan.StayCount);
            Assert.Equal(1, plan.MoveCount);
        }

        [Fact]
        public void LockedView_IsSkipped_AndStopModeBlocks()
        {
            var (s1, s2) = Sample(lockEaves: "it is checked out by another user");
            var skip = CombinePlanner.Build(Request(s1, s2, null, 21, 23));
            Assert.Null(skip.Blocker);
            var eaves = skip.Items.Single(i => i.View.ViewportKey == 23);
            Assert.Equal(ViewAction.Skip, eaves.Action);
            Assert.Contains("checked out", eaves.Reason);

            var stop = CombinePlanner.Build(Request(s1, s2, new SheetViewsCombineSettings { OnFailure = FailureMode.StopAndUndo }, 21, 23));
            Assert.NotNull(stop.Blocker);
            Assert.False(stop.CanApply);
        }

        [Fact]
        public void LegendAlreadyOnTarget_IsSkipped()
        {
            var (s1, s2) = Sample();
            s1.Views.Add(V(14, 1, "A-101", "Roof Build-up Legend", 30, 30, 120, 160, "9", legend: true, viewKey: 1025));
            var plan = CombinePlanner.Build(Request(s1, s2, null, 25));
            Assert.Equal(ViewAction.Skip, plan.Items.Single(i => i.View.ViewportKey == 25).Action);
        }

        [Fact]
        public void Overflow_GoesToContinuationSheets_WithFreeNumbers()
        {
            var (s1, s2) = Sample();
            for (int i = 0; i < 6; i++)
                s2.Views.Add(V(30 + i, 2, "A-501", "Big " + i, 0, 0, 500, 300, (10 + i).ToString()));
            var ticked = s2.Views.Select(v => v.ViewportKey).Where(k => k >= 30).ToArray();
            var plan = CombinePlanner.Build(Request(s1, s2, null, ticked));

            Assert.Null(plan.Blocker);
            Assert.True(plan.Sheets.Count > 1);
            Assert.Equal("A-101.2", plan.Sheets[1].Number); // A-101.1 is already used
            Assert.All(plan.Sheets.Skip(1), s => Assert.True(s.IsNew));
            Assert.All(plan.Items.Where(i => i.View.ViewportKey >= 30), i => Assert.Equal(ViewAction.Move, i.Action));
        }

        [Fact]
        public void TooBigForTheSheet_IsSkippedWithAReason()
        {
            var (s1, s2) = Sample();
            s2.Views.Add(V(40, 2, "A-501", "Huge", 0, 0, 900, 700, "8"));
            var plan = CombinePlanner.Build(Request(s1, s2, null, 40, 21));
            var huge = plan.Items.Single(i => i.View.ViewportKey == 40);
            Assert.Equal(ViewAction.Skip, huge.Action);
            Assert.Contains("bigger than the usable area", huge.Reason);
            Assert.True(plan.CanApply);
        }

        [Fact]
        public void NewSheet_NeedsAFreeNumberAndAFrame()
        {
            var (s1, s2) = Sample();
            var s = new SheetViewsCombineSettings { Target = TargetMode.NewSheet };
            var used = CombinePlanner.Build(new CombineRequest
            {
                Sheet1 = s1, Sheet2 = s2, Ticked = new long[] { 21 }, Settings = s, NewSheetNumber = "A-101",
                NewTitleBlockKey = 77, NewTitleBlockFrame = s1.Frame, ExistingSheetNumbers = new[] { "A-101", "A-501" }
            });
            Assert.Contains("already used", used.Blocker);

            var ok = CombinePlanner.Build(new CombineRequest
            {
                Sheet1 = s1, Sheet2 = s2, Ticked = new long[] { 21, 11 }, Settings = s, NewSheetNumber = "A-102", NewSheetName = "Combined",
                NewTitleBlockKey = 77, NewTitleBlockFrame = s1.Frame, ExistingSheetNumbers = new[] { "A-101", "A-501" }
            });
            Assert.Null(ok.Blocker);
            Assert.True(ok.Target.IsNew);
            Assert.Equal(2, ok.MoveCount); // both sheets' views move to the new sheet
        }

        [Fact]
        public void SameSheetTwice_IsBlocked()
        {
            var (s1, _) = Sample();
            var plan = CombinePlanner.Build(Request(s1, s1, null, 11));
            Assert.NotNull(plan.Blocker);
        }

        [Fact]
        public void Signature_IsStable_AndFollowsTicks()
        {
            var (s1, s2) = Sample();
            var a = CombinePlanner.Build(Request(s1, s2, null, 21, 25));
            var b = CombinePlanner.Build(Request(s1, s2, null, 25, 21));
            var c = CombinePlanner.Build(Request(s1, s2, null, 21));
            Assert.Equal(a.Signature, b.Signature);
            Assert.NotEqual(a.Signature, c.Signature);
        }

        [Fact]
        public void KeepDetailNumbersOff_RenumbersIncomingViews()
        {
            var (s1, s2) = Sample();
            var plan = CombinePlanner.Build(Request(s1, s2, new SheetViewsCombineSettings { KeepDetailNumbers = false }, 25));
            Assert.Equal("4", plan.Items.Single(i => i.View.ViewportKey == 25).DetailNumber); // 1, 2, 3 stay on A-101
        }

        [Fact]
        public void Settings_Normalize_FixesBadValues()
        {
            var s = new SheetViewsCombineSettings { Target = (TargetMode)42, MarginTopMm = -3, HorizontalGapMm = double.NaN, NewSheetName = " " }.Normalize();
            var d = new SheetViewsCombineSettings();
            Assert.Equal(d.Target, s.Target);
            Assert.Equal(d.MarginTopMm, s.MarginTopMm);
            Assert.Equal(d.HorizontalGapMm, s.HorizontalGapMm);
            Assert.Equal(d.NewSheetName, s.NewSheetName);
        }

        [Fact]
        public void NaturalOrder_ForDetailNumbers()
        {
            var list = new List<string> { "10", "2", "A", "1" }.OrderBy(CombinePlanner.DetailSortKey).ToList();
            Assert.Equal(new[] { "1", "2", "10", "A" }, list);
        }
    }
}
