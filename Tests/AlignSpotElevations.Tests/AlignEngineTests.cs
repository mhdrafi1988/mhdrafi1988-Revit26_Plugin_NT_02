using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AlignSpotElevations.Tests
{
    public class AlignEngineTests
    {
        private static List<SpotItem> Us(params double[] u)
            => u.Select((x, i) => new SpotItem { Id = i + 1, U = x, V = 100 - x }).ToList();

        private static SpotItem P(long id, double u, double v, double pu = 0, double pv = 0)
            => new SpotItem { Id = id, U = u, V = v, PointU = pu, PointV = pv };

        [Fact]
        public void Average_IsMean()
        {
            var r = AlignEngine.ChooseTarget(Us(0, 10, 20), AlignDirection.Vertical, AlignTarget.Average, 0);
            Assert.Equal(10, r.Value, 9);
        }

        [Fact]
        public void Horizontal_UsesV()
        {
            var r = AlignEngine.ChooseTarget(Us(0, 10, 20), AlignDirection.Horizontal, AlignTarget.Average, 0);
            Assert.Equal(90, r.Value, 9);
        }

        [Fact]
        public void MostCommon_PicksLargestGroupWithinTolerance()
        {
            var r = AlignEngine.ChooseTarget(Us(5, 5.01, 4.99, 20, 40), AlignDirection.Vertical, AlignTarget.MostCommon, 0.05);
            Assert.False(r.FellBackToAverage);
            Assert.Equal(3, r.SharedCount);
            Assert.Equal(5, r.Value, 2);
        }

        [Fact]
        public void MostCommon_TieGoesToGroupNearestAverage()
        {
            // Groups {0,0} and {100,100} plus 60: average 52, so the group at 100 (48 away) wins.
            var r = AlignEngine.ChooseTarget(Us(0, 0, 60, 100, 100), AlignDirection.Vertical, AlignTarget.MostCommon, 0.1);
            Assert.Equal(100, r.Value, 9);
        }

        [Fact]
        public void MostCommon_NothingShared_FallsBackToAverage()
        {
            var r = AlignEngine.ChooseTarget(Us(0, 10, 20), AlignDirection.Vertical, AlignTarget.MostCommon, 1);
            Assert.True(r.FellBackToAverage);
            Assert.Equal(10, r.Value, 9);
        }

        [Fact]
        public void MedianMinMaxReference()
        {
            var items = Us(1, 2, 3, 10);
            Assert.Equal(2.5, AlignEngine.ChooseTarget(items, AlignDirection.Vertical, AlignTarget.Median, 0).Value, 9);
            Assert.Equal(1, AlignEngine.ChooseTarget(items, AlignDirection.Vertical, AlignTarget.Min, 0).Value, 9);
            Assert.Equal(10, AlignEngine.ChooseTarget(items, AlignDirection.Vertical, AlignTarget.Max, 0).Value, 9);
            Assert.Equal(3, AlignEngine.ChooseTarget(items, AlignDirection.Vertical, AlignTarget.Reference, 0, items[2]).Value, 9);
        }

        [Fact]
        public void Plan_Align_KeepSpacing_MovesOnlyOnAxis()
        {
            var items = new List<SpotItem> { P(1, 0, 50), P(2, 10, 20), P(3, 20, 0) };
            var plan = AlignEngine.Plan(items, new AlignOptions { Target = AlignTarget.Average });
            Assert.Equal(2, plan.MoveCount); // the middle one is already on the average
            Assert.All(plan.Entries, e => Assert.Equal(0, e.DeltaV, 9));
            Assert.Equal(10, plan.Entries[0].DeltaU, 9);
            Assert.Equal(-10, plan.Entries[2].DeltaU, 9);
        }

        [Fact]
        public void Plan_Align_EvenSpacing_ColumnKeepsTopAndBottom()
        {
            var items = new List<SpotItem> { P(1, 0, 100), P(2, 0, 90), P(3, 0, 0) };
            var plan = AlignEngine.Plan(items, new AlignOptions { Distribute = DistributeMode.Even });
            Assert.Equal(0, plan.Entries[0].DeltaV, 9);
            Assert.Equal(-40, plan.Entries[1].DeltaV, 9); // 90 -> 50
            Assert.Equal(0, plan.Entries[2].DeltaV, 9);
        }

        [Fact]
        public void Plan_FixedGap_ColumnStepsDownFromTop()
        {
            var items = new List<SpotItem> { P(1, 0, 10), P(2, 0, 100), P(3, 0, 50) };
            var plan = AlignEngine.Plan(items, new AlignOptions { Distribute = DistributeMode.FixedGap, GapMm = 6 });
            Assert.Equal(100 - 12 - 10, plan.Entries[0].DeltaV, 9); // bottom tag -> 88
            Assert.Equal(0, plan.Entries[1].DeltaV, 9);            // top stays
            Assert.Equal(94 - 50, plan.Entries[2].DeltaV, 9);
        }

        [Fact]
        public void Plan_FixedGap_RowStepsRightFromLeft()
        {
            var items = new List<SpotItem> { P(1, 30, 0), P(2, 0, 5) };
            var plan = AlignEngine.Plan(items, new AlignOptions
            {
                Direction = AlignDirection.Horizontal, Target = AlignTarget.Min, Distribute = DistributeMode.FixedGap, GapMm = 10
            });
            Assert.Equal(-20, plan.Entries[0].DeltaU, 9); // 30 -> 10
            Assert.Equal(0, plan.Entries[1].DeltaU, 9);
            Assert.Equal(0, plan.Entries[0].DeltaV, 9);   // row at min V = 0
            Assert.Equal(-5, plan.Entries[1].DeltaV, 9);
        }

        [Fact]
        public void Plan_DistributeOnly_KeepsOtherAxis_DefaultsToEven()
        {
            var items = new List<SpotItem> { P(1, 3, 0), P(2, 7, 10), P(3, 11, 100) };
            var plan = AlignEngine.Plan(items, new AlignOptions { Mode = ArrangeMode.DistributeOnly, Distribute = DistributeMode.None });
            Assert.All(plan.Entries, e => Assert.Equal(0, e.DeltaU, 9));
            Assert.Equal(40, plan.Entries[1].DeltaV, 9); // 10 -> 50
        }

        [Fact]
        public void Plan_Offset_PlacesTagRelativeToPoint()
        {
            var items = new List<SpotItem> { P(1, 0, 0, 100, 200), P(2, 50, 50, 10, 20) };
            var plan = AlignEngine.Plan(items, new AlignOptions { Mode = ArrangeMode.OffsetFromPoint, OffsetXMm = 8, OffsetYMm = -3 });
            Assert.Equal(108, plan.Entries[0].DeltaU, 9);
            Assert.Equal(197, plan.Entries[0].DeltaV, 9);
            Assert.Equal(-32, plan.Entries[1].DeltaU, 9);
            Assert.Equal(-33, plan.Entries[1].DeltaV, 9);
        }

        [Fact]
        public void Plan_FlagsStackedTags()
        {
            // Horizontal row at the average V with spacing kept: two tags share U, so they stack.
            var items = new List<SpotItem> { P(1, 5, 0), P(2, 5, 10), P(3, 40, 20) };
            var plan = AlignEngine.Plan(items, new AlignOptions { Direction = AlignDirection.Horizontal });
            Assert.Equal(2, plan.StackedCount);
            var spaced = AlignEngine.Plan(items, new AlignOptions { Direction = AlignDirection.Horizontal, Distribute = DistributeMode.Even });
            Assert.Equal(0, spaced.StackedCount);
        }

        [Fact]
        public void Plan_ReferenceById()
        {
            var items = new List<SpotItem> { P(1, 0, 0), P(2, 30, 10), P(3, 7, 20) };
            var plan = AlignEngine.Plan(items, new AlignOptions { Target = AlignTarget.Reference, ReferenceId = 3 });
            Assert.Equal(7, plan.Target.Value, 9);
            Assert.False(plan.Entries[2].Moves);
        }

        [Fact]
        public void Options_JsonRoundTrip_IgnoresReference()
        {
            var o = new AlignOptions { Mode = ArrangeMode.OffsetFromPoint, GapMm = 4, ReferenceId = 9 };
            string json = System.Text.Json.JsonSerializer.Serialize(o);
            Assert.DoesNotContain("ReferenceId", json);
            var back = System.Text.Json.JsonSerializer.Deserialize<AlignOptions>(json);
            Assert.Equal(ArrangeMode.OffsetFromPoint, back.Mode);
            Assert.Equal(4, back.GapMm);
            Assert.Null(back.ReferenceId);
        }
    }
}
