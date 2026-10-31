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
            // groups {0,0} and {100,100}, plus 60 -> average 52; group at 100 is not nearer than 0? |0-52|=52, |100-52|=48
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
        public void Plan_SkipsTagsAlreadyOnTarget()
        {
            var moves = AlignEngine.Plan(Us(5, 7, 9), AlignDirection.Vertical, 7);
            Assert.Equal(2, moves.Count);
            Assert.DoesNotContain(moves, m => m.Item.U == 7);
            Assert.Equal(2, moves.First(m => m.Item.U == 5).Delta, 9);
        }
    }
}
