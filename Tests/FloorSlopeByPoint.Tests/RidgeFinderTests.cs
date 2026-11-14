using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.FloorSlopeByPoint.V001.Core.Services;
using Xunit;

namespace FloorSlopeByPoint.Tests
{
    public class RidgeFinderTests
    {
        [Fact]
        public void TwoDrains_GiveOneStraightLineOnTheMidline()
        {
            var floor = SampleFloor.Build(new[] { (8.0, 12.0), (32.0, 12.0) });

            RidgeResult r = floor.Find();

            RidgeLine line = Assert.Single(r.Lines);
            var a = floor.Points[line.A];
            var b = floor.Points[line.B];
            Assert.Equal(20, a.X, 6);
            Assert.Equal(20, b.X, 6);
            Assert.Equal(new[] { 0.0, 24.0 }, new[] { a.Y, b.Y }.OrderBy(y => y).ToArray());
            Assert.Equal(1, r.RidgeCount);
            Assert.Equal(2, r.GroupCount);
        }

        [Fact]
        public void TieBetweenTwoColumns_GivesOneLineNotTwo()
        {
            // Midline at x = 19 falls between the grid columns 18 and 20, which tie in height.
            var floor = SampleFloor.Build(new[] { (8.0, 12.0), (30.0, 12.0) });

            RidgeResult r = floor.Find();

            RidgeLine line = Assert.Single(r.Lines);
            Assert.Equal(floor.Points[line.A].X, floor.Points[line.B].X, 6);
        }

        [Fact]
        public void ThreeDrains_RidgesMeetAtOneJunction()
        {
            var floor = SampleFloor.Build(new[] { (6.0, 6.0), (34.0, 8.0), (18.0, 20.0) });

            RidgeResult r = floor.Find();

            int junction = Assert.Single(r.Junctions);
            Assert.Equal(3, r.RidgeCount);
            Assert.True(r.Lines.Count(l => l.A == junction || l.B == junction) >= 3,
                "Every ridge should reach the junction.");
            Assert.True(r.Lines.Count <= 8, $"Expected straightened ridges, got {r.Lines.Count} lines.");
        }

        [Fact]
        public void LinesStartAndEndOnRidgePoints()
        {
            var floor = SampleFloor.Build(new[] { (6.0, 6.0), (34.0, 8.0), (18.0, 20.0) });

            RidgeResult r = floor.Find();

            var ridge = new HashSet<int>(r.RidgePoints);
            Assert.NotEmpty(r.Lines);
            Assert.All(r.Lines, l =>
            {
                Assert.Contains(l.A, ridge);
                Assert.Contains(l.B, ridge);
                Assert.NotEqual(l.A, l.B);
            });
        }

        [Fact]
        public void RidgePointsAreTheHigherEnd()
        {
            var floor = SampleFloor.Build(new[] { (6.0, 6.0), (34.0, 8.0), (18.0, 20.0) });

            RidgeResult r = floor.Find();

            // Every ridge point is the higher end of at least one link across the ridge
            // (on this grid the short links are the 2 m links along the axes).
            Assert.NotEmpty(r.RidgePoints);
            foreach (int v in r.RidgePoints)
            {
                bool higherEnd = floor.Graph.Neighbours(v).Any(e =>
                    floor.GroupOf[e.To] >= 0 && floor.GroupOf[e.To] != floor.GroupOf[v]
                    && floor.Points[v].DistanceXY(floor.Points[e.To]) <= 2.0 + 1e-6
                    && floor.Elevation[v] >= floor.Elevation[e.To] - 1e-3);
                Assert.True(higherEnd, $"Ridge point {v} is not the higher end of any link across the ridge.");
            }
        }

        [Fact]
        public void OneDrainGroup_GivesNoLines()
        {
            var floor = SampleFloor.Build(new[] { (8.0, 12.0), (32.0, 12.0) }, drainGroups: new[] { 0, 0 });

            RidgeResult r = floor.Find();

            Assert.Empty(r.Lines);
            Assert.Empty(r.RidgePoints);
            Assert.Equal(1, r.GroupCount);
        }

        [Fact]
        public void Opening_NoLineCrossesIt()
        {
            var floor = SampleFloor.Build(
                new[] { (6.0, 18.0), (34.0, 18.0), (8.0, 4.0), (30.0, 4.0) },
                hole: (16, 8, 24, 14));

            RidgeResult r = floor.Find();

            Assert.NotEmpty(r.Lines);
            Assert.All(r.Lines, l => Assert.False(floor.Crosses(floor.Points[l.A], floor.Points[l.B]),
                $"Line {l.A}–{l.B} crosses the opening."));
        }

        [Fact]
        public void DrainsAndUnslopedPoints_AreNeverRidgePoints()
        {
            var floor = SampleFloor.Build(new[] { (8.0, 12.0), (32.0, 12.0) });
            // Points on the top row count as "beyond the drainage distance".
            for (int i = 0; i < floor.Points.Count; i++)
                if (floor.Points[i].Y > 23) floor.GroupOf[i] = -1;

            RidgeResult r = floor.Find();

            Assert.NotEmpty(r.RidgePoints);
            Assert.All(r.RidgePoints, v =>
            {
                Assert.False(floor.IsDrain[v]);
                Assert.True(floor.GroupOf[v] >= 0);
            });
        }

        [Fact]
        public void EmptyFloor_GivesNoLines()
        {
            var r = RidgeFinder.Find(new RidgeInput
            {
                Positions = new List<Revit26_Plugin_Revised.FloorSlopeByPoint.V001.Core.Models.Point3>(),
                Graph = new PathGraph(0),
                GroupOf = new int[0],
                Elevation = new double[0],
                IsDrain = new bool[0]
            });

            Assert.Empty(r.Lines);
            Assert.Equal(0, r.GroupCount);
        }
    }
}
