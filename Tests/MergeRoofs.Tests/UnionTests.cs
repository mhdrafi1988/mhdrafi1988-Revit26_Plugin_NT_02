using System.Linq;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Services;
using Xunit;
using static MergeRoofs.Tests.TestShapes;

namespace MergeRoofs.Tests
{
    public class UnionTests
    {
        private const double Tol = 5.0 / 304.8;

        [Fact]
        public void TwoOverlappingRects_GiveOneOuterLoop_AndOverlapIssue()
        {
            var r = MergeAnalyzer.Analyze(new[] { Rect("1", 0, 0, 10, 10), Rect("2", 5, 0, 15, 10) }, Tol);
            Assert.Single(r.Outer);
            Assert.Contains(r.Issues, i => i.Kind == IssueKind.Overlap);
            Assert.True(r.CanCreate);
        }

        [Fact]
        public void SharedFullEdge_IsJoined_NoIssues()
        {
            var r = MergeAnalyzer.Analyze(new[] { Rect("1", 0, 0, 10, 10), Rect("2", 10, 0, 20, 10) }, Tol);
            Assert.Single(r.Outer);
            Assert.Empty(r.Issues);
            Assert.True(r.CanCreate);
        }

        [Fact]
        public void RingOfFourRoofs_GivesOneOuterAndOneHole()
        {
            var r = MergeAnalyzer.Analyze(new[]
            {
                Rect("b", 0, 0, 30, 10), Rect("t", 0, 20, 30, 30), Rect("l", 0, 10, 10, 20), Rect("r", 20, 10, 30, 20),
            }, Tol);
            Assert.Single(r.Outer);
            Assert.Single(r.Holes);
            Assert.True(r.CanCreate);
        }

        [Theory]
        [InlineData(Tol)]
        [InlineData(0.0)]
        public void PointContactOnly_IsNotJoined(double tol)
        {
            var r = MergeAnalyzer.Analyze(new[] { Rect("1", 0, 0, 10, 10), Rect("2", 10, 10, 20, 20) }, tol);
            Assert.Contains(r.Issues, i => i.Kind == IssueKind.NotJoined);
            Assert.False(r.CanCreate);
        }

        [Fact]
        public void IdenticalRoofs_AndNestedRoof_GiveSingleOuter()
        {
            var r = MergeAnalyzer.Analyze(new[] { Rect("1", 0, 0, 10, 10), Rect("2", 0, 0, 10, 10), Rect("3", 2, 2, 5, 5) }, Tol);
            Assert.Single(r.Outer);
            Assert.Empty(r.Holes);
            Assert.True(r.CanCreate);
        }

        [Fact]
        public void RoofHoles_ArePreserved()
        {
            var r = MergeAnalyzer.Analyze(new[] { RectWithHole("1", 0, 0, 10, 10, 3, 3, 6, 6), Rect("2", 10, 0, 20, 10) }, Tol);
            Assert.Single(r.Outer);
            Assert.Single(r.Holes);
        }

        [Fact]
        public void SingleRoof_ReturnsItsOwnOutline()
        {
            var r = MergeAnalyzer.Analyze(new[] { Rect("1", 0, 0, 10, 10) }, Tol);
            Assert.Single(r.Outer);
            Assert.Equal(4, r.Outer[0].Edges.Count);
        }
    }
}
