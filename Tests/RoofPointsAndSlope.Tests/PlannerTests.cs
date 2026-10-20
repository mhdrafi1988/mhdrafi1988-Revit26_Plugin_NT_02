using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Planning;
using System.Linq;
using Xunit;

namespace RoofPointsAndSlope.Tests
{
    public class PlannerTests
    {
        private static PipelineSettings MidOnBoundary(bool quarters = false, double minSpacingMm = 0)
        {
            var s = TestShapes.Off();
            s.MidQuarterEnabled = true;
            s.AddMidpoint = true;
            s.AddQuarterPoints = quarters;
            s.MidQuarterScope = new EdgeScope { Boundary = true, Openings = false, Lines = true, Curves = true };
            s.MinPointSpacingMm = minSpacingMm;
            return s.Normalize();
        }

        [Fact]
        public void Midpoints_one_per_boundary_side()
        {
            var roof = TestShapes.Roof(TestShapes.Rect(0, 0, 10, 10));
            var plan = PointPipelinePlanner.Plan(roof, MidOnBoundary());
            var mids = plan[PointStep.MidQuarter].Points.Select(p => p.Position).ToList();
            Assert.Equal(4, mids.Count);
            Assert.Contains(mids, p => p.DistanceTo(new P2(5, 0)) < 1e-6);
            Assert.Contains(mids, p => p.DistanceTo(new P2(0, 5)) < 1e-6);
        }

        [Fact]
        public void Existing_point_is_not_added_twice()
        {
            var roof = TestShapes.Roof(TestShapes.Rect(0, 0, 10, 10));
            roof.ExistingVertices.Add(new P2(5, 0));
            var step = PointPipelinePlanner.Plan(roof, MidOnBoundary())[PointStep.MidQuarter];
            Assert.Equal(3, step.Points.Count);
            Assert.Equal(1, step.AlreadyThere);
        }

        [Fact]
        public void Min_spacing_skips_close_points()
        {
            // 1 ft side: midpoint and quarter points are 0.25 ft (76 mm) apart.
            var roof = TestShapes.Roof(TestShapes.Rect(0, 0, 1, 1));
            var free = PointPipelinePlanner.Plan(roof, MidOnBoundary(quarters: true, minSpacingMm: 0))[PointStep.MidQuarter];
            var spaced = PointPipelinePlanner.Plan(roof, MidOnBoundary(quarters: true, minSpacingMm: 100))[PointStep.MidQuarter];
            Assert.Equal(12, free.Points.Count);
            Assert.True(spaced.TooClose > 0);
            Assert.Equal(free.Points.Count, spaced.Points.Count + spaced.TooClose);
            var pts = spaced.Points.Select(p => p.Position).Concat(roof.ExistingVertices).ToList();
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                    Assert.True(pts[i].DistanceTo(pts[j]) >= 100 / 304.8 - 1e-9);
        }

        [Fact]
        public void Step1_ignores_min_spacing()
        {
            var roof = TestShapes.Roof(TestShapes.Rect(0, 0, 20, 20), TestShapes.Circle(10, 10, 0.2));
            roof.Openings[0].AddInnerPoints = true;
            roof.Openings[0].PointCount = 8;
            var s = TestShapes.Off();
            s.InnerPointsEnabled = true;
            s.MinPointSpacingMm = 1000;
            var step = PointPipelinePlanner.Plan(roof, s.Normalize())[PointStep.OpeningPoints];
            // One of the 8 is the circle's existing start vertex; none is dropped for spacing.
            Assert.Equal(8, step.Points.Count + step.AlreadyThere);
            Assert.Equal(0, step.TooClose);
            Assert.All(step.Points, p => Assert.True(roof.Openings[0].Loop.DistanceTo(p.Position) < 1e-6));
        }

        [Fact]
        public void Gap_fill_respects_interval()
        {
            var roof = TestShapes.Roof(TestShapes.Rect(0, 0, 30, 10));
            var s = TestShapes.Off();
            s.GapFillEnabled = true;
            s.GapIntervalMeters = 2;
            s.GapScope = new EdgeScope { Boundary = true, Openings = true, Lines = true, Curves = true };
            var step = PointPipelinePlanner.Plan(roof, s.Normalize())[PointStep.GapFill];
            Assert.NotEmpty(step.Points);
            // Along each side, consecutive points (corners included) are no more than 2 m apart.
            double interval = 2 / 0.3048 + 1e-6;
            var all = step.Points.Select(p => p.Position).Concat(roof.ExistingVertices).ToList();
            var bottom = all.Where(p => p.Y < 1e-6).OrderBy(p => p.X).ToList();
            for (int i = 1; i < bottom.Count; i++)
                Assert.True(bottom[i].X - bottom[i - 1].X <= interval);
        }

        [Fact]
        public void Clear_first_starts_from_corners_only()
        {
            var roof = TestShapes.Roof(TestShapes.Rect(0, 0, 10, 10));
            roof.ExistingVertices.Add(new P2(5, 0));
            var s = MidOnBoundary();
            s.ClearExistingFirst = true;
            Assert.Equal(4, PointPipelinePlanner.StartingPoints(roof, s).Count);
            var step = PointPipelinePlanner.Plan(roof, s)[PointStep.MidQuarter];
            Assert.Equal(4, step.Points.Count);
            Assert.Equal(0, step.AlreadyThere);
        }
    }
}
