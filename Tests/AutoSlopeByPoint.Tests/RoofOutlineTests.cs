using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services;
using Xunit;

namespace AutoSlopeByPoint.Tests
{
    public class RoofOutlineTests
    {
        [Fact] public void Contains_InsideOutsideHoleAndEdge()
        {
            var o = TestRoofs.Rectangle(hole: (16, 8, 24, 14));
            Assert.True(o.Contains(new Point3(1, 1, 0)));
            Assert.False(o.Contains(new Point3(20, 11, 0)));   // in the opening
            Assert.False(o.Contains(new Point3(41, 1, 0)));    // outside
            Assert.True(o.Contains(new Point3(0, 5, 0)));      // on the outer edge
            Assert.True(o.Contains(new Point3(16, 11, 0)));    // on the opening edge
        }

        [Fact] public void IsVisible_OpeningBlocksOnlyLinesThroughIt()
        {
            var o = TestRoofs.Rectangle(hole: (16, 8, 24, 14));
            Assert.False(o.IsVisible(new Point3(10, 11, 0), new Point3(30, 11, 0)));
            Assert.True(o.IsVisible(new Point3(10, 2, 0), new Point3(30, 2, 0)));
            Assert.True(o.IsVisible(new Point3(10, 8, 0), new Point3(30, 8, 0)));   // runs along the opening edge
            Assert.False(o.IsVisible(new Point3(12, 4, 0), new Point3(20, 12, 0))); // through corner (16,8) into the opening
            Assert.True(o.IsVisible(new Point3(12, 4, 0), new Point3(16, 8, 0)));   // ends on that corner
        }

        [Fact] public void IsVisible_FromABoundaryCorner()
        {
            var o = TestRoofs.Rectangle();
            Assert.True(o.IsVisible(new Point3(0, 0, 0), new Point3(20, 12, 0)));
            Assert.True(o.IsVisible(new Point3(0, 12, 0), new Point3(40, 12, 0)));  // edge to edge
        }

        [Fact] public void IsVisible_LShape_InnerCornerBlocks()
        {
            var o = TestRoofs.Polygon((0, 0), (40, 0), (40, 10), (20, 10), (20, 24), (0, 24));
            Assert.False(o.IsVisible(new Point3(35, 5, 0), new Point3(5, 20, 0)));
        }

        [Fact] public void Pivots_AreTheReflexCornersOnly()
        {
            var withHole = TestRoofs.Rectangle(hole: (16, 8, 24, 14));
            Assert.Equal(4, withHole.Pivots.Count);
            Assert.All(withHole.Pivots, p => Assert.True((p.X is 16 or 24) && (p.Y is 8 or 14)));

            var l = TestRoofs.Polygon((0, 0), (40, 0), (40, 10), (20, 10), (20, 24), (0, 24));
            var pivot = Assert.Single(l.Pivots);
            Assert.Equal((20.0, 10.0), (pivot.X, pivot.Y));
        }

        [Fact] public void IsClearRect_RejectsCellsTouchingTheOutline()
        {
            var o = TestRoofs.Rectangle(hole: (16, 8, 24, 14));
            Assert.True(o.IsClearRect(1, 1, 2, 2));
            Assert.False(o.IsClearRect(15, 9, 17, 10));         // crosses the opening edge
            Assert.False(o.IsClearRect(15.5, 7.5, 16.5, 8.5));  // opening corner inside
            Assert.False(o.IsClearRect(38, 1, 41, 2));          // corner outside the roof
        }

        [Fact] public void RayHit_FindsTheEdgeWithinReach()
        {
            var o = TestRoofs.Rectangle();
            Point3? hit = o.RayHit(new Point3(39, 12, 0), 1, 0, 3);
            Assert.NotNull(hit);
            Assert.Equal(40, hit.Value.X, 9);
            Assert.Equal(12, hit.Value.Y, 9);
            Assert.Null(o.RayHit(new Point3(39, 12, 0), 1, 0, 0.5));
        }

        [Fact] public void Constructor_CleansLoopsAndBoundingBox()
        {
            var raw = new List<Point3>
            {
                new Point3(0, 0, 5), new Point3(0, 0, 7),                 // consecutive duplicate (Z ignored)
                new Point3(40, 0, 0), new Point3(40, 24, 0), new Point3(0, 24, 0),
                new Point3(0, 0, 0),                                     // repeated closing point
            };
            var tooShort = new List<Point3> { new Point3(1, 1, 0), new Point3(2, 2, 0) };

            var o = new RoofOutline(new IReadOnlyList<Point3>[] { raw, tooShort });

            var loop = Assert.Single(o.Loops);
            Assert.Equal(4, loop.Count);
            Assert.Equal((0.0, 0.0, 40.0, 24.0), (o.MinX, o.MinY, o.MaxX, o.MaxY));
            Assert.True(o.Contains(new Point3(20, 12, 0)));
        }

        [Fact] public void Pivots_DoNotDependOnLoopOrientation()
        {
            // Same L-shape and same opening, every loop wound the other way round.
            var l = TestRoofs.Polygon((0, 24), (20, 24), (20, 10), (40, 10), (40, 0), (0, 0));
            var pivot = Assert.Single(l.Pivots);
            Assert.Equal((20.0, 10.0), (pivot.X, pivot.Y));

            var outerCw = TestRoofs.Box(0, 0, 40, 24).Reverse().ToList();
            var holeCcw = TestRoofs.Box(16, 8, 24, 14);
            var o = new RoofOutline(new[] { outerCw, holeCcw });
            Assert.Equal(4, o.Pivots.Count);
            Assert.All(o.Pivots, p => Assert.True((p.X is 16 or 24) && (p.Y is 8 or 14)));
        }

        [Fact] public void IsVisible_PolygonOpening_BlocksTheChordButNotTheRim()
        {
            var loops = new[] { TestRoofs.Box(0, 0, 40, 24), TestRoofs.Circle(20, 12, 4, 24) };
            var o = new RoofOutline(loops);
            Assert.False(o.Contains(new Point3(20, 12, 0)));
            Assert.False(o.IsVisible(new Point3(10, 12, 0), new Point3(30, 12, 0)));
            Assert.True(o.IsVisible(new Point3(10, 2, 0), new Point3(30, 2, 0)));
            Assert.True(o.IsVisible(loops[1][0], loops[1][1]));   // along one side of the opening
        }

        [Fact] public void RayHit_NonUnitDirection_ThroughACorner_AndAlongAnEdge()
        {
            var o = TestRoofs.Rectangle();

            // Direction (0, 5) is the same ray as (0, 1): distance is still in feet.
            Point3? up = o.RayHit(new Point3(10, 20, 0), 0, 5, 4);
            Assert.NotNull(up);
            Assert.Equal((10.0, 24.0), (up.Value.X, up.Value.Y));
            Assert.Null(o.RayHit(new Point3(10, 20, 0), 0, 5, 3.9));

            // Straight into the corner (40, 24): shared by two edges, still one hit.
            Point3? corner = o.RayHit(new Point3(30, 14, 0), 1, 1, 20);
            Assert.NotNull(corner);
            Assert.Equal(40, corner.Value.X, 9);
            Assert.Equal(24, corner.Value.Y, 9);

            // Starting on an edge and running along it: the next edge is met at its end.
            Point3? along = o.RayHit(new Point3(10, 0, 0), 1, 0, 100);
            Assert.NotNull(along);
            Assert.Equal((40.0, 0.0), (along.Value.X, along.Value.Y));

            // Starting on an edge and leaving the roof: no hit at distance zero.
            Assert.Null(o.RayHit(new Point3(10, 0, 0), 0, -1, 100));
            Assert.Null(o.RayHit(new Point3(10, 10, 0), 0, 0, 100));   // zero direction
        }

        [Fact] public void IsClearRect_HoleInsideTheCell_AndCellOutsideEveryEdge()
        {
            var o = TestRoofs.Rectangle(hole: (16, 8, 24, 14));
            Assert.False(o.IsClearRect(14, 6, 26, 16));    // the whole opening sits inside the cell
            Assert.False(o.IsClearRect(0, 0, 1, 1));       // sides lie on the outer edge
            Assert.True(o.IsClearRect(26, 16, 25, 15));    // corners given in any order
        }

        [Fact] public void Pivots_SecondOuterPart_OnlyItsReflexCorner()
        {
            // A square and, apart from it, an L-shaped part whose only reflex corner is (25, 5).
            var square = TestRoofs.Box(0, 0, 10, 10);
            var l = new[]
            {
                new Point3(20, 0, 0), new Point3(30, 0, 0), new Point3(30, 5, 0),
                new Point3(25, 5, 0), new Point3(25, 10, 0), new Point3(20, 10, 0),
            };
            var o = new RoofOutline(new IReadOnlyList<Point3>[] { square, l });

            var pivot = Assert.Single(o.Pivots);
            Assert.Equal((25.0, 5.0), (pivot.X, pivot.Y));
        }

        [Fact] public void Pivots_IslandInsideAnOpening_ReflexCornersOfBoth()
        {
            // 40 x 24 roof, opening (4,4)-(16,16), and in the opening an L-shaped island of roof
            // whose only reflex corner is (10, 10).
            var island = new[]
            {
                new Point3(6, 6, 0), new Point3(14, 6, 0), new Point3(14, 10, 0),
                new Point3(10, 10, 0), new Point3(10, 14, 0), new Point3(6, 14, 0),
            };
            var o = new RoofOutline(new IReadOnlyList<Point3>[]
            {
                TestRoofs.Box(0, 0, 40, 24), TestRoofs.Box(4, 4, 16, 16), island,
            });

            var expected = new[] { (4.0, 4.0), (4.0, 16.0), (10.0, 10.0), (16.0, 4.0), (16.0, 16.0) };
            Assert.Equal(expected, o.Pivots.Select(p => (p.X, p.Y)).OrderBy(p => p).ToArray());
            Assert.True(o.Contains(new Point3(7, 7, 0)));      // island is roof again
            Assert.False(o.Contains(new Point3(12, 12, 0)));   // opening around it
        }

        [Fact] public void ClosestPoint_OnAStraightEdge_FromInsideAndOutside()
        {
            var o = TestRoofs.Rectangle(hole: (16, 8, 24, 14));
            var c = o.ClosestPoint(new Point3(10, -2, 5), out double d);
            Assert.Equal(10, c.X, 9); Assert.Equal(0, c.Y, 9); Assert.Equal(0, c.Z); Assert.Equal(2, d, 9);
            c = o.ClosestPoint(new Point3(20, 13, 0), out d);   // in the opening, 1 from its top edge
            Assert.Equal(20, c.X, 9); Assert.Equal(14, c.Y, 9); Assert.Equal(1, d, 9);
        }

        [Fact] public void ClosestPoint_BeyondACorner_IsTheCorner()
        {
            var c = TestRoofs.Rectangle().ClosestPoint(new Point3(43, 28, 0), out double d);
            Assert.Equal(40, c.X, 9); Assert.Equal(24, c.Y, 9); Assert.Equal(5, d, 9);
        }
    }
}
