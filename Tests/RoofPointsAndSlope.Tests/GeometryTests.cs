using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry;
using System;
using Xunit;

namespace RoofPointsAndSlope.Tests
{
    public class GeometryTests
    {
        [Fact]
        public void Arc_projection_ccw_and_cw()
        {
            var ccw = ArcSegment2D.FromThreePoints(new P2(0, 0), 1, new P2(1, 0), new P2(0, 1), new P2(-1, 0));
            var cw = ArcSegment2D.FromThreePoints(new P2(0, 0), 1, new P2(1, 0), new P2(0, -1), new P2(-1, 0));
            var r = ccw.Project(new P2(0, 2));
            Assert.Equal(0.5, r.T, 9);
            Assert.Equal(1, r.Distance, 9);
            r = cw.Project(new P2(0, -2));
            Assert.Equal(0.5, r.T, 9);
            r = ccw.Project(new P2(0, -2));
            Assert.Equal(Math.Sqrt(5), r.Distance, 9); // outside the sweep: nearest end
        }

        [Fact]
        public void Polyline_length_point_and_reverse()
        {
            var pl = new PolylineSegment2D(new[] { new P2(0, 0), new P2(1, 0), new P2(1, 1) });
            Assert.Equal(2, pl.Length, 12);
            Assert.True(pl.PointAt(0.75).DistanceTo(new P2(1, 0.5)) < 1e-12);
            Assert.True(pl.Reversed().PointAt(0.25).DistanceTo(new P2(1, 0.5)) < 1e-12);
        }

        [Fact]
        public void Loops_classify_and_contain()
        {
            var circle = TestShapes.Circle(0, 0, 1);
            Assert.Equal(LoopShape.Circular, LoopClassifier.Classify(circle).Shape);
            Assert.True(circle.Contains(new P2(0.2, 0.1)));
            Assert.False(circle.Contains(new P2(1.2, 0)));

            var rect = TestShapes.Rect(0, 0, 4, 2);
            Assert.Equal(LoopShape.Rectangle, LoopClassifier.Classify(rect).Shape);
            Assert.Equal(8, rect.Area, 9);
            Assert.Equal(1, rect.DistanceTo(new P2(2, 3)), 9);
        }
    }
}
