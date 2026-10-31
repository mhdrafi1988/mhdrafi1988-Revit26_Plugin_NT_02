using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services;
using Xunit;

namespace AutoSlopeByPoint.Tests
{
    public class RidgeFinderTests
    {
        [Fact]
        public void TwoDrains_GiveOneStraightLineOnTheMidline()
        {
            var roof = SampleRoof.Build(new[] { (8.0, 12.0), (32.0, 12.0) });

            RidgeResult r = roof.Find();

            RidgeLine line = Assert.Single(r.Lines);
            var a = roof.Points[line.A];
            var b = roof.Points[line.B];
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
            var roof = SampleRoof.Build(new[] { (8.0, 12.0), (30.0, 12.0) });

            RidgeResult r = roof.Find();

            RidgeLine line = Assert.Single(r.Lines);
            Assert.Equal(roof.Points[line.A].X, roof.Points[line.B].X, 6);
        }

        [Fact]
        public void ThreeDrains_RidgesMeetAtOneJunction()
        {
            var roof = SampleRoof.Build(new[] { (6.0, 6.0), (34.0, 8.0), (18.0, 20.0) });

            RidgeResult r = roof.Find();

            int junction = Assert.Single(r.Junctions);
            Assert.Equal(3, r.RidgeCount);
            Assert.True(r.Lines.Count(l => l.A == junction || l.B == junction) >= 3,
                "Every ridge should reach the junction.");
            Assert.True(r.Lines.Count <= 8, $"Expected straightened ridges, got {r.Lines.Count} lines.");
        }

        [Fact]
        public void LinesStartAndEndOnRidgePoints()
        {
            var roof = SampleRoof.Build(new[] { (6.0, 6.0), (34.0, 8.0), (18.0, 20.0) });

            RidgeResult r = roof.Find();

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
            var roof = SampleRoof.Build(new[] { (6.0, 6.0), (34.0, 8.0), (18.0, 20.0) });

            RidgeResult r = roof.Find();

            // Every ridge point is the higher end of at least one link across the ridge
            // (on this grid the short links are the 2 m links along the axes).
            Assert.NotEmpty(r.RidgePoints);
            foreach (int v in r.RidgePoints)
            {
                bool higherEnd = roof.Graph.Neighbours(v).Any(e =>
                    roof.GroupOf[e.To] >= 0 && roof.GroupOf[e.To] != roof.GroupOf[v]
                    && roof.Points[v].DistanceXY(roof.Points[e.To]) <= 2.0 + 1e-6
                    && roof.Elevation[v] >= roof.Elevation[e.To] - 1e-3);
                Assert.True(higherEnd, $"Ridge point {v} is not the higher end of any link across the ridge.");
            }
        }

        [Fact]
        public void OneDrainGroup_GivesNoLines()
        {
            var roof = SampleRoof.Build(new[] { (8.0, 12.0), (32.0, 12.0) }, drainGroups: new[] { 0, 0 });

            RidgeResult r = roof.Find();

            Assert.Empty(r.Lines);
            Assert.Empty(r.RidgePoints);
            Assert.Equal(1, r.GroupCount);
        }

        [Fact]
        public void Opening_NoLineCrossesIt()
        {
            var roof = SampleRoof.Build(
                new[] { (6.0, 18.0), (34.0, 18.0), (8.0, 4.0), (30.0, 4.0) },
                hole: (16, 8, 24, 14));

            RidgeResult r = roof.Find();

            Assert.NotEmpty(r.Lines);
            Assert.All(r.Lines, l => Assert.False(roof.Crosses(roof.Points[l.A], roof.Points[l.B]),
                $"Line {l.A}–{l.B} crosses the opening."));
        }

        [Fact]
        public void DrainsAndUnslopedPoints_AreNeverRidgePoints()
        {
            var roof = SampleRoof.Build(new[] { (8.0, 12.0), (32.0, 12.0) });
            // Points on the top row count as "beyond the drainage distance".
            for (int i = 0; i < roof.Points.Count; i++)
                if (roof.Points[i].Y > 23) roof.GroupOf[i] = -1;

            RidgeResult r = roof.Find();

            Assert.NotEmpty(r.RidgePoints);
            Assert.All(r.RidgePoints, v =>
            {
                Assert.False(roof.IsDrain[v]);
                Assert.True(roof.GroupOf[v] >= 0);
            });
        }

        [Fact]
        public void EmptyRoof_GivesNoLines()
        {
            var r = RidgeFinder.Find(new RidgeInput
            {
                Positions = new List<Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models.Point3>(),
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
