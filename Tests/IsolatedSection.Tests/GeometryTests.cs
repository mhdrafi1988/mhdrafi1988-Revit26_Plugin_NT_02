using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Geometry;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Models;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Services;
using Xunit;

namespace IsolatedSection.Tests
{
    public class GeometryTests
    {
        private const double Tol = 1e-6;

        /// <summary>Box of size length × width × height, turned by angleDeg in plan, centred at (cx, cy).</summary>
        private static List<Point3> Box(double length, double width, double height, double angleDeg,
            double cx = 0, double cy = 0, double z0 = 0)
        {
            double a = angleDeg * Math.PI / 180;
            Vec2 u = new(Math.Cos(a), Math.Sin(a)), v = u.Perp();
            var pts = new List<Point3>();
            foreach (double su in new[] { -0.5, 0.5 })
            foreach (double sv in new[] { -0.5, 0.5 })
            foreach (double z in new[] { z0, z0 + height })
            {
                Vec2 p = new Vec2(cx, cy) + u * (su * length) + v * (sv * width);
                pts.Add(new Point3(p.X, p.Y, z));
            }
            return pts;
        }

        private static void Near(double expected, double actual) => Assert.InRange(actual, expected - Tol, expected + Tol);

        [Fact]
        public void ConvexHull_DropsInteriorAndCollinearPoints()
        {
            var pts = new[] { new Vec2(0, 0), new Vec2(2, 0), new Vec2(1, 0), new Vec2(2, 2), new Vec2(0, 2), new Vec2(1, 1) };
            var hull = PlanGeometry.ConvexHull(pts);
            Assert.Equal(4, hull.Count);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(23)]
        [InlineData(90)]
        [InlineData(137)]
        public void MinAreaRectangle_FindsTurnedBox(double angle)
        {
            var f = ElementFootprint.FromPoints(Box(10, 2, 3, angle, 5, -4));
            Near(10, f.Rectangle.Length);
            Near(2, f.Rectangle.Width);
            Near(5, f.Rectangle.Center.X);
            Near(-4, f.Rectangle.Center.Y);
            Near(30, f.LongSideArea);

            // Along is parallel to the box's long axis.
            double a = angle * Math.PI / 180;
            Near(1, Math.Abs(f.Rectangle.Along.Dot(new Vec2(Math.Cos(a), Math.Sin(a)))));
        }

        [Fact]
        public void FromPoints_ReturnsNullWithoutPoints()
            => Assert.Null(ElementFootprint.FromPoints(Array.Empty<Point3>()));

        [Fact]
        public void Sides_AFacesSouth_CFacesWest_ForAxisAlignedBox()
        {
            var r = ElementFootprint.FromPoints(Box(10, 2, 3, 0)).Rectangle;
            var sides = PlanSides.Of(r);
            Assert.Equal("S", sides[0].Compass);
            Assert.Equal("N", sides[1].Compass);
            Assert.Equal("W", sides[2].Compass);
            Assert.Equal("E", sides[3].Compass);
            Near(10, sides[0].Length);
            Near(2, sides[2].Length);
            Near(-1, sides[0].Start.Y);
            Near(-1, sides[0].End.Y);
        }

        [Fact]
        public void LargestSide_IsTheBiggestLongSideOfAnyElement()
        {
            var small = ElementFootprint.FromPoints(Box(4, 1, 3, 0));          // 12 ft²
            var tallShort = ElementFootprint.FromPoints(Box(5, 1, 10, 45));    // 50 ft²
            var longLow = ElementFootprint.FromPoints(Box(20, 1, 2, 10));      // 40 ft²
            var list = new[] { small, tallShort, longLow };

            Assert.Equal(1, AlignmentPlanner.LargestSideIndex(list));

            Alignment al = AlignmentPlanner.Choose(list, SideChoice.Auto);
            Assert.Equal(1, al.SourceIndex);
            // The view looks at the largest side, so it is parallel to it: forward ⟂ the element's long axis.
            Near(0, Math.Abs(al.Forward.Dot(tallShort.Rectangle.Along)));
            Near(-1, al.Forward.Dot(al.Side.OutwardNormal));
        }

        [Fact]
        public void LargestSide_FlatElementsUseLength()
        {
            var a = ElementFootprint.FromPoints(Box(4, 1, 0, 0));
            var b = ElementFootprint.FromPoints(Box(9, 1, 0, 0));
            Assert.Equal(1, AlignmentPlanner.LargestSideIndex(new[] { a, b }));
        }

        [Fact]
        public void Choose_SideBLooksOppositeToA_AndShortSidesAreAcross()
        {
            var list = new[] { ElementFootprint.FromPoints(Box(10, 2, 3, 0)) };
            Alignment a = AlignmentPlanner.Choose(list, SideChoice.LongA);
            Alignment b = AlignmentPlanner.Choose(list, SideChoice.LongB);
            Alignment c = AlignmentPlanner.Choose(list, SideChoice.ShortC);

            Near(1, a.Forward.Y);   // looks north at the south face
            Near(-1, b.Forward.Y);
            Near(1, c.Forward.X);   // looks east at the west face
        }

        [Fact]
        public void Choose_PickedFaceLooksIntoTheFace()
        {
            var list = new[] { ElementFootprint.FromPoints(Box(10, 2, 3, 0)) };
            Alignment al = AlignmentPlanner.Choose(list, SideChoice.PickedFace, new Vec2(0, 2));
            Near(-1, al.Forward.Y);
            Assert.Equal(-1, al.SourceIndex);
            Assert.Throws<InvalidOperationException>(() => AlignmentPlanner.Choose(list, SideChoice.PickedFace));
        }

        [Fact]
        public void SectionBox_HoldsAllElementsPlusMargins()
        {
            var e1 = ElementFootprint.FromPoints(Box(10, 2, 3, 30, 0, 0, 0));
            var e2 = ElementFootprint.FromPoints(Box(2, 2, 6, 0, 8, 8, 1));
            var list = new[] { e1, e2 };
            Alignment al = AlignmentPlanner.Choose(list, SideChoice.Auto);
            SectionBoxPlan box = SectionBoxPlanner.Plan(list, al, 1, 2, 0.5, CameraMode.FacingSide);

            foreach (var f in list)
            foreach (Vec2 p in f.Hull)
            {
                Vec2 local = p - box.Origin.Plan;
                double x = local.Dot(box.Right), y = local.Dot(box.Forward);
                Assert.InRange(x, box.Min.X + 1 - Tol, box.Max.X - 1 + Tol);
                Assert.InRange(y, box.Min.Y + 2 - Tol, box.Max.Y - 2 + Tol);
            }
            Near(-0.5, box.Min.Z);
            Near(7.5, box.Max.Z);
        }

        [Theory]
        [InlineData(CameraMode.FacingSide)]
        [InlineData(CameraMode.Angled)]
        public void Camera_LooksAtTheBoxWithPerpendicularUp(CameraMode mode)
        {
            var list = new[] { ElementFootprint.FromPoints(Box(10, 2, 3, 17)) };
            Alignment al = AlignmentPlanner.Choose(list, SideChoice.Auto);
            SectionBoxPlan box = SectionBoxPlanner.Plan(list, al, 0, 0, 0, mode);

            Point3 d = box.ViewDirection, u = box.Up;
            Near(1, Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z));
            Near(1, Math.Sqrt(u.X * u.X + u.Y * u.Y + u.Z * u.Z));
            Near(0, d.X * u.X + d.Y * u.Y + d.Z * u.Z);
            Assert.True(u.Z > 0);

            // The horizontal part of the view direction points at the chosen side.
            Assert.True(d.X * al.Forward.X + d.Y * al.Forward.Y > 0.5);
            if (mode == CameraMode.Angled) Assert.True(d.Z < 0); // looks down
        }

        [Fact]
        public void ViewName_ReplacesTokensAndInvalidCharacters()
        {
            string name = ViewNameBuilder.Build("ISO-{category}-{Id}:{Side}", "Walls", 42, 3, "A", new DateTime(2026, 10, 20));
            Assert.Equal("ISO-Walls-42-A", name);
            Assert.Equal(ViewNameBuilder.Fallback, ViewNameBuilder.Build("  ", "x", 1, 1, "", DateTime.Today));
        }

        [Fact]
        public void ViewName_MakeUniqueAddsCounter()
        {
            var existing = new List<string> { "ISO", "iso (2)" };
            Assert.Equal("ISO (3)", ViewNameBuilder.MakeUnique("ISO", existing));
            Assert.Equal("Other", ViewNameBuilder.MakeUnique("Other", existing));
            Assert.True(existing.All(e => e != "ISO (3)"));
        }
    }
}
