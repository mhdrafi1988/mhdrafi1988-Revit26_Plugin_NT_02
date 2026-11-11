using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;
using Xunit;

namespace VoidOpenings.Tests
{
    public class GeometryTests
    {
        private static readonly Triangle Tri = new Triangle(new Vec3(0, 0, 0), new Vec3(10, 0, 0), new Vec3(0, 10, 0));

        [Fact]
        public void Segment_through_triangle_reports_parameter_along_the_segment()
        {
            var seg = new Segment3(new Vec3(2, 2, 5), new Vec3(2, 2, -15));

            Assert.True(Intersect.SegmentTriangle(seg, Tri, out double t, out bool entering));

            Assert.Equal(0.25, t, 6);
            Assert.True(entering); // travelling against the +Z normal
        }

        [Fact]
        public void Segment_leaving_through_the_back_of_a_triangle_is_not_entering()
        {
            var seg = new Segment3(new Vec3(2, 2, -5), new Vec3(2, 2, 5));

            Assert.True(Intersect.SegmentTriangle(seg, Tri, out _, out bool entering));

            Assert.False(entering);
        }

        [Theory]
        [InlineData(20, 20, 5, 20, 20, -5)]   // beside the triangle
        [InlineData(2, 2, 5, 2, 2, 1)]        // stops short of it
        [InlineData(1, 1, 3, 8, 1, 3)]        // parallel to it
        public void Segment_that_does_not_cross_the_triangle_misses(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            var seg = new Segment3(new Vec3(x1, y1, z1), new Vec3(x2, y2, z2));

            Assert.False(Intersect.SegmentTriangle(seg, Tri, out _, out _));
        }

        [Fact]
        public void Aabb_intersection_includes_touching_boxes_and_excludes_separate_ones()
        {
            var a = new Aabb(new Vec3(0, 0, 0), new Vec3(10, 10, 10));

            Assert.True(a.Intersects(new Aabb(new Vec3(10, 0, 0), new Vec3(20, 10, 10))));
            Assert.False(a.Intersects(new Aabb(new Vec3(10.5, 0, 0), new Vec3(20, 10, 10))));
        }

        [Fact]
        public void SpatialGrid_returns_only_boxes_near_the_query()
        {
            var boxes = new List<Aabb>
            {
                new Aabb(new Vec3(0, 0, 0), new Vec3(100, 100, 100)),
                new Aabb(new Vec3(50000, 0, 0), new Vec3(50100, 100, 100)),
                new Aabb(new Vec3(-1e6, -1e6, -10), new Vec3(1e6, 1e6, 0)), // very large: spans the whole grid
            };
            var grid = new SpatialGrid(boxes, 1000);

            var hits = grid.Query(new Aabb(new Vec3(50, 50, -5), new Vec3(60, 60, 50))).OrderBy(i => i).ToList();

            Assert.Equal(new[] { 0, 2 }, hits);
        }
    }
}
