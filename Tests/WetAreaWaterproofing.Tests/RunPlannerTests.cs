using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Run;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static WetAreaWaterproofing.Tests.LoopRepairTests;

namespace WetAreaWaterproofing.Tests
{
    public class RunTagTests
    {
        [Fact]
        public void NewRunIdUsesTheSpecFormat()
            => Assert.Equal("20261114-153007", RunTag.NewRunId(new DateTime(2026, 11, 14, 15, 30, 7)));

        [Fact]
        public void MakeAndParseRoundTrip()
        {
            string tag = RunTag.Make("20261114-153007", "Host:abc-123", "101", "Toilet | 01");
            Assert.StartsWith("WP|20261114-153007|Host:abc-123|101|", tag);
            Assert.True(RunTag.TryParse(tag, out string runId, out string roomKey));
            Assert.Equal("20261114-153007", runId);
            Assert.Equal("Host:abc-123", roomKey);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("some user comment")]
        [InlineData("WP|")]
        [InlineData("WP||key")]
        [InlineData("WP|run|")]
        [InlineData("XX|run|key")]
        public void GarbageDoesNotParse(string comments)
            => Assert.False(RunTag.TryParse(comments, out _, out _));
    }

    public class RunPlannerTests
    {
        private const string RunId = "20261114-153007";

        private static RoomCandidate Candidate(string no, Action<RoomCandidate> edit = null, List<Seg> loop = null, double roomArea = 12)
        {
            var row = new RoomRow
            {
                RoomKey = "Host:" + no, Number = no, Name = "Toilet " + no, Level = "L1", LevelElevationMm = 3000,
                Matched = "toilet", AreaM2 = roomArea, SlabTopMm = 3150
            };
            var c = new RoomCandidate
            {
                Row = row,
                Outline = LoopRepair.Evaluate(new[] { loop ?? Rect() }, roomArea, Defaults, null),
                HostLevelId = 77,
                HostLevelElevationMm = 3000
            };
            edit?.Invoke(c);
            return c;
        }

        [Fact]
        public void CleanRoomGivesOneJobAndNoIssues()
        {
            var plan = RunPlanner.Plan(new[] { Candidate("101") }, Defaults, RunId);
            var job = Assert.Single(plan.Jobs);
            Assert.Empty(plan.Issues);
            Assert.Equal(RunId, plan.RunId);
            Assert.Equal("Host:101", job.RoomKey);
            Assert.Equal(77, job.HostLevelId);
            Assert.Equal(4, job.Outer.Count);
            Assert.True(RunTag.TryParse(job.CommentsTag, out string run, out string key));
            Assert.Equal((RunId, "Host:101"), (run, key));
        }

        [Fact]
        public void SlabOffsetComputed()
            => Assert.Equal(150, RunPlanner.Plan(new[] { Candidate("101") }, Defaults, RunId).Jobs[0].BaseOffsetFromLevelMm, 6);

        [Fact]
        public void UserOffsetIsAddedAboveTheSlab()
        {
            var s = new WetAreaSettings { OffsetMm = 20 }.Normalize();
            Assert.Equal(170, RunPlanner.Plan(new[] { Candidate("101") }, s, RunId).Jobs[0].BaseOffsetFromLevelMm, 6);
        }

        [Fact]
        public void NoSlabUsesLevelPlusOffset()
        {
            var s = new WetAreaSettings { OffsetMm = 20 }.Normalize();
            var plan = RunPlanner.Plan(new[] { Candidate("101", c => c.Row.SlabTopMm = null) }, s, RunId);
            Assert.Equal(20, plan.Jobs[0].BaseOffsetFromLevelMm, 6);
            var issue = Assert.Single(plan.Issues);
            Assert.Equal("Warning", issue.Severity);
            Assert.Contains("No slab", issue.Message);
        }

        [Fact]
        public void LinkedRoomOnAnotherHostLevelKeepsItsHeight()
        {
            // Room level at 3000 in host coordinates, but the nearest host level is at 2800.
            var plan = RunPlanner.Plan(new[] { Candidate("101", c => c.HostLevelElevationMm = 2800) }, Defaults, RunId);
            Assert.Equal(350, plan.Jobs[0].BaseOffsetFromLevelMm, 6);
        }

        [Fact]
        public void ExistingRoofIsReplaced()
        {
            var plan = RunPlanner.Plan(new[] { Candidate("101", c => c.ExistingRoofIds = new long[] { 555, 556 }) }, Defaults, RunId);
            Assert.Equal(new long[] { 555, 556 }, Assert.Single(plan.Jobs).ReplaceRoofIds);
            Assert.Contains(plan.Issues, i => i.Severity == "Info" && i.Message.Contains("replaced"));
        }

        [Fact]
        public void OwnedRoomSkippedWithIssue()
        {
            var plan = RunPlanner.Plan(new[] { Candidate("101", c => c.Row.Owner = "j.smith"), Candidate("102") }, Defaults, RunId);
            Assert.Equal("Host:102", Assert.Single(plan.Jobs).RoomKey);
            var issue = Assert.Single(plan.Issues);
            Assert.Equal("Error", issue.Severity);
            Assert.Contains("Owned by j.smith", issue.Message);
            Assert.Equal("101", issue.Number);
        }

        [Fact]
        public void FailedOutlineSkippedWithIssue()
        {
            var plan = RunPlanner.Plan(new[] { Candidate("101", loop: RectWithGap(80)) }, Defaults, RunId);
            Assert.Empty(plan.Jobs);
            Assert.Equal("Error", Assert.Single(plan.Issues).Severity);
        }

        [Fact]
        public void AreaWarningStillCreatesJob()
        {
            var plan = RunPlanner.Plan(new[] { Candidate("101", roomArea: 12 / 1.034) }, Defaults, RunId);
            Assert.Single(plan.Jobs);
            var issue = Assert.Single(plan.Issues);
            Assert.Equal("Warning", issue.Severity);
            Assert.Contains("3.4", issue.Message);
        }

        [Fact]
        public void RepairedOutlineCreatesJobWithWarning()
        {
            var plan = RunPlanner.Plan(new[] { Candidate("101", loop: RectWithGap(3)) }, Defaults, RunId);
            Assert.Single(plan.Jobs);
            Assert.Contains(plan.Issues, i => i.Severity == "Warning" && i.Message.Contains("snapped"));
        }

        [Fact]
        public void NoHostLevelSkipsTheRoom()
        {
            var plan = RunPlanner.Plan(new[] { Candidate("101", c => c.HostLevelId = 0) }, Defaults, RunId);
            Assert.Empty(plan.Jobs);
            Assert.Contains("level", Assert.Single(plan.Issues).Message);
        }

        [Fact]
        public void NullInputGivesEmptyPlan()
        {
            var plan = RunPlanner.Plan(null, Defaults, RunId);
            Assert.Empty(plan.Jobs);
            Assert.Empty(plan.Issues);
        }
    }
}
