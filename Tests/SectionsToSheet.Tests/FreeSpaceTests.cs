using System.Text.Json;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models;
using Xunit;

namespace SectionsToSheet.Tests
{
    public class FreeSpaceTests
    {
        private static readonly LayoutRect Area = new(0, 0, 100, 50);

        [Fact]
        public void Empty_area_is_free_as_a_whole()
        {
            Assert.Equal(Area, FreeSpace.Largest(Area, new LayoutRect[0]));
        }

        [Fact]
        public void Obstacle_in_the_top_left_leaves_the_larger_strip()
        {
            // 30 wide × 20 high in the top-left corner: right strip 70×50 = 3500 beats bottom strip 100×30 = 3000.
            var free = FreeSpace.Largest(Area, new[] { new LayoutRect(0, 30, 30, 50) });
            Assert.Equal(new LayoutRect(30, 0, 100, 50), free);
        }

        [Fact]
        public void Obstacles_across_the_top_leave_the_band_below()
        {
            var free = FreeSpace.Largest(Area, new[] { new LayoutRect(0, 30, 40, 50), new LayoutRect(50, 35, 100, 50) });
            Assert.Equal(new LayoutRect(0, 0, 100, 30), free);
        }

        [Fact]
        public void Fully_covered_area_has_no_free_space()
        {
            Assert.Null(FreeSpace.Largest(Area, new[] { new LayoutRect(-5, -5, 105, 55) }));
        }

        [Fact]
        public void Obstacles_outside_the_area_are_ignored()
        {
            Assert.Equal(Area, FreeSpace.Largest(Area, new[] { new LayoutRect(120, 0, 150, 50) }));
        }

        [Fact]
        public void Equal_areas_prefer_the_higher_then_the_left_one()
        {
            // A bar across the middle leaves two equal strips; the upper one wins.
            var free = FreeSpace.Largest(Area, new[] { new LayoutRect(0, 20, 100, 30) });
            Assert.Equal(new LayoutRect(0, 30, 100, 50), free);

            // A bar down the middle leaves two equal columns; the left one wins.
            free = FreeSpace.Largest(Area, new[] { new LayoutRect(45, 0, 55, 50) });
            Assert.Equal(new LayoutRect(0, 0, 45, 50), free);
        }

        [Fact]
        public void Version_1_settings_load_as_a_new_sheet_target()
        {
            var s = JsonSerializer.Deserialize<SectionsToSheetSettings>("{\"Version\":1,\"SheetName\":\"SEC\"}").Normalize();
            Assert.Equal(TargetMode.NewSheet, s.Target);
            Assert.Equal(ExistingSheetMode.FreeSpace, s.ExistingMode);
            Assert.Equal(string.Empty, s.TargetSheetNumber);
            Assert.Equal(SectionsToSheetSettings.CurrentVersion, s.Version);
        }

        [Fact]
        public void Unknown_target_values_fall_back_to_the_defaults()
        {
            var s = JsonSerializer.Deserialize<SectionsToSheetSettings>("{\"Target\":7,\"ExistingMode\":9,\"TargetSheetNumber\":null}").Normalize();
            Assert.Equal(TargetMode.NewSheet, s.Target);
            Assert.Equal(ExistingSheetMode.FreeSpace, s.ExistingMode);
            Assert.Equal(string.Empty, s.TargetSheetNumber);
        }
    }
}
