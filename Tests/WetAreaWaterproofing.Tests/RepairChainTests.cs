using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static WetAreaWaterproofing.Tests.LoopRepairTests;

namespace WetAreaWaterproofing.Tests
{
    public class RepairChainTests
    {
        private static IReadOnlyList<IReadOnlyList<Seg>> Loops(params List<Seg>[] loops) => loops;

        [Fact]
        public void CleanRoomIsReady()
        {
            var o = LoopRepair.Evaluate(Loops(Rect()), 12.0, Defaults, null);
            Assert.Equal(RoomStatus.Ready, o.Status);
            Assert.Equal(12.0, o.AreaM2, 6);
            Assert.Equal(0, o.AreaDiffPct, 6);
            Assert.Empty(o.Inner);
        }

        [Fact]
        public void SnappedRoomIsRepaired()
        {
            var o = LoopRepair.Evaluate(Loops(RectWithGap(3)), 12.0, Defaults, null);
            Assert.Equal(RoomStatus.Repaired, o.Status);
        }

        [Fact]
        public void InnerLoopIsSubtracted()
        {
            var o = LoopRepair.Evaluate(Loops(Rect(), Rect(500, 500, 1000, 1000)), 11.75, Defaults, null);
            Assert.Equal(RoomStatus.Ready, o.Status);
            Assert.Single(o.Inner);
            Assert.Equal(11.75, o.AreaM2, 6);
        }

        [Fact]
        public void OuterIsTheLargestLoopWhateverTheOrder()
        {
            var o = LoopRepair.Evaluate(Loops(Rect(500, 500, 1000, 1000), Rect()), 11.75, Defaults, null);
            Assert.Equal(11.75, o.AreaM2, 6);
            Assert.Equal(4000, o.Outer.Max(p => p.X));
        }

        [Fact]
        public void AreaOutsideToleranceIsWarningNotFailure()
        {
            var o = LoopRepair.Evaluate(Loops(Rect()), 12.0 / 1.034, Defaults, null);
            Assert.Equal(RoomStatus.AreaWarning, o.Status);
            Assert.Equal(3.4, o.AreaDiffPct, 1);
            Assert.NotEmpty(o.Outer);
        }

        [Fact]
        public void GapOf80mmFailsWithoutApproximate()
        {
            var o = LoopRepair.Evaluate(Loops(RectWithGap(80)), 12.0, Defaults, null);
            Assert.Equal(RoomStatus.Failed, o.Status);
            Assert.Empty(o.Outer);
        }

        [Fact]
        public void GapOf80mmApproximateWhenAllowed()
        {
            var s = new WetAreaSettings { AllowApproximate = true }.Normalize();
            var o = LoopRepair.Evaluate(Loops(RectWithGap(80)), 12.0, s, null);
            Assert.Equal(RoomStatus.Approximate, o.Status);
            Assert.Contains(o.Steps, st => st.Method == 10 && st.Passed);
            Assert.Equal(12.0, o.AreaM2, 3);
        }

        [Fact]
        public void BottomFaceUsedWhenOuterFails()
        {
            var face = Rect().Select(s => s.A).ToList();
            var o = LoopRepair.Evaluate(Loops(RectWithGap(80)), 12.0, Defaults, () => face);
            Assert.Equal(RoomStatus.Repaired, o.Status);
            Assert.Contains(o.Steps, st => st.Method == 8 && st.Passed);
            Assert.Equal(12.0, o.AreaM2, 6);
        }

        [Fact]
        public void ThrowingBottomFaceIsAFailedStepNotACrash()
        {
            var o = LoopRepair.Evaluate(Loops(RectWithGap(80)), 12.0, Defaults, () => throw new System.InvalidOperationException("no solid"));
            Assert.Equal(RoomStatus.Failed, o.Status);
            Assert.Contains(o.Steps, st => st.Method == 8 && !st.Passed);
        }

        [Fact]
        public void FailingInnerLoopIsDroppedNotFatal()
        {
            var hole = Rect(500, 500, 1000, 1000);
            hole.RemoveAt(0); // open hole, 500 mm gap
            var o = LoopRepair.Evaluate(Loops(Rect(), hole), 12.0, Defaults, null);
            Assert.Equal(RoomStatus.Repaired, o.Status);
            Assert.Empty(o.Inner);
            Assert.Contains(o.Steps, st => st.Method == 9 && st.Passed);
            Assert.Equal(12.0, o.AreaM2, 6);
        }

        [Fact]
        public void NoLoopsIsNotEnclosed()
        {
            Assert.Equal(RoomStatus.NotEnclosed, LoopRepair.Evaluate(Loops(), 12.0, Defaults, null).Status);
            Assert.Equal(RoomStatus.NotEnclosed, LoopRepair.Evaluate(null, 12.0, Defaults, null).Status);
        }
    }
}
