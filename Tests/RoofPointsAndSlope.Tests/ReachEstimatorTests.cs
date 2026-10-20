using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Planning;
using System.Collections.Generic;
using Xunit;

namespace RoofPointsAndSlope.Tests
{
    public class ReachEstimatorTests
    {
        private static readonly Loop2D Roof = TestShapes.Rect(0, 0, 20, 10);
        private static readonly Loop2D Drain = TestShapes.Circle(2, 5, 0.25);

        private static ReachEstimate Run(List<P2> pts, double maxEdge, double maxPath, params Loop2D[] otherOpenings)
        {
            var openings = new List<Loop2D> { Drain };
            openings.AddRange(otherOpenings);
            return ReachEstimator.Estimate(pts, new[] { Drain }, openings, new[] { Roof }, maxEdge, maxPath);
        }

        [Fact]
        public void Everything_reaches_on_open_roof()
        {
            var pts = new List<P2> { new P2(2.25, 5), new P2(0, 0), new P2(20, 0), new P2(20, 10), new P2(0, 10) };
            var est = Run(pts, 30, 100);
            Assert.Equal(1, est.DrainPointCount);
            Assert.Equal(0, est.LeftAtZero);
        }

        [Fact]
        public void Too_far_without_intermediate_points_is_no_path()
        {
            var pts = new List<P2> { new P2(2.25, 5), new P2(18, 5) };
            var est = Run(pts, 3, 100);
            Assert.Single(est.NoPath);
        }

        [Fact]
        public void Long_chain_is_beyond_max_path()
        {
            var pts = new List<P2> { new P2(2.25, 5) };
            for (double x = 4; x <= 18; x += 2) pts.Add(new P2(x, 5));
            var est = Run(pts, 3, 9);
            Assert.Empty(est.NoPath);
            Assert.True(est.BeyondMaxPath.Count > 0);
            Assert.All(est.BeyondMaxPath, p => Assert.True(p.X > 2.25 + 9 - 1e-6));
        }

        [Fact]
        public void Opening_blocks_the_straight_line()
        {
            var wall = TestShapes.Rect(8, 1, 9, 9);
            var blocked = Run(new List<P2> { new P2(2.25, 5), new P2(15, 5) }, 30, 100, wall);
            Assert.Single(blocked.NoPath);

            var detour = Run(new List<P2> { new P2(2.25, 5), new P2(8.5, 0.5), new P2(15, 5) }, 30, 100, wall);
            Assert.Equal(0, detour.LeftAtZero);
        }

        [Fact]
        public void No_drain_point_means_every_point_has_no_path()
        {
            var est = Run(new List<P2> { new P2(5, 5), new P2(10, 5) }, 30, 100);
            Assert.Equal(0, est.DrainPointCount);
            Assert.Equal(2, est.NoPath.Count);
        }
    }
}
