using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Run;
using System;
using Xunit;
using static WetAreaWaterproofing.Tests.LoopRepairTests;

namespace WetAreaWaterproofing.Tests
{
    public class RoomStatusRulesTests
    {
        private static RoomCandidate Candidate(Action<RoomCandidate> edit = null, System.Collections.Generic.List<Seg> loop = null, double roomArea = 12)
        {
            var c = new RoomCandidate
            {
                Row = new RoomRow { RoomKey = "Host:1", Number = "1", Level = "L1", LevelElevationMm = 3000, SlabTopMm = 3150, SlabId = 5 },
                Outline = LoopRepair.Evaluate(new[] { loop ?? Rect() }, roomArea, Defaults, null),
                HostLevelId = 1
            };
            edit?.Invoke(c);
            RoomStatusRules.Apply(c);
            return c;
        }

        [Fact]
        public void CleanRoomWithSlabIsReady()
        {
            var c = Candidate();
            Assert.Equal(RoomStatus.Ready, c.Row.Status);
            Assert.Equal("Floor · +3.150", c.Row.SlabText);
        }

        [Fact]
        public void OwnerWinsOverEverything()
        {
            var c = Candidate(x => x.Row.Owner = "j.smith", RectWithGap(3));
            Assert.Equal(RoomStatus.Owned, c.Row.Status);
            Assert.Equal("Owned by j.smith", c.Row.StatusDetail);
        }

        [Fact]
        public void FailedOutlineSaysWhy()
        {
            var c = Candidate(loop: RectWithGap(80));
            Assert.Equal(RoomStatus.Failed, c.Row.Status);
            Assert.Contains("80 mm", c.Row.StatusDetail);
        }

        [Fact]
        public void RepairedShowsTheRepair()
        {
            var c = Candidate(loop: RectWithGap(3));
            Assert.Equal(RoomStatus.Repaired, c.Row.Status);
            Assert.Contains("3 mm", c.Row.StatusDetail);
        }

        [Fact]
        public void AreaWarningShowsThePercentage()
        {
            var c = Candidate(roomArea: 12 / 1.034);
            Assert.Equal(RoomStatus.AreaWarning, c.Row.Status);
            Assert.Equal("Area +3.4%", c.Row.StatusDetail);
        }

        [Fact]
        public void NoSlabIsFlagged()
        {
            var c = Candidate(x => { x.Row.SlabTopMm = null; x.Row.SlabId = 0; });
            Assert.Equal(RoomStatus.NoSlab, c.Row.Status);
            Assert.Equal("No slab", c.Row.SlabText);
        }

        [Fact]
        public void ExistingRoofShowsAlreadyDone()
        {
            var c = Candidate(x => { x.ExistingRoofIds = new long[] { 9 }; x.ExistingRunId = "20261114-153007"; });
            Assert.Equal(RoomStatus.AlreadyDone, c.Row.Status);
            Assert.Contains("20261114-153007", c.Row.StatusDetail);
        }

        [Fact]
        public void NegativeSlabElevationKeepsItsSign()
            => Assert.Equal("Floor · -0.150", Candidate(x => x.Row.SlabTopMm = -150).Row.SlabText);
    }
}
