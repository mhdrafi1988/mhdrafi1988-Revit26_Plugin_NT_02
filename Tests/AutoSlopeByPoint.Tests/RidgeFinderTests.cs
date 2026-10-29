using System;
using System.Linq;
using System.Threading;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services;
using Xunit;

namespace AutoSlopeByPoint.Tests
{
    public class RidgeFinderTests
    {
        static bool Near(Point3 p, double x, double y, double tol) => Math.Abs(p.X - x) <= tol && Math.Abs(p.Y - y) <= tol;

        [Fact] public void TwoDrains_OneLineOnTheBisector_EdgeToEdge()
        {
            var r = TestRoofs.Find(TestRoofs.Rectangle(), new[] { (8.0, 12.0), (32.0, 12.0) });
            var l = Assert.Single(r.Lines);
            Assert.Equal(20, l.A.X, 3); Assert.Equal(20, l.B.X, 3);
            Assert.Equal(new[] { 0.0, 24.0 }, new[] { l.A.Y, l.B.Y }.OrderBy(y => y).Select(y => Math.Round(y, 3)));
            Assert.Equal(1, r.RidgeCount); Assert.Equal(2, r.GroupCount); Assert.Empty(r.Junctions);
        }

        [Fact] public void TwoDrains_OffGrid_IsNotSnapped()          // midline x = 19.5, between any shape points
        {
            var l = Assert.Single(TestRoofs.Find(TestRoofs.Rectangle(), new[] { (8.0, 12.0), (31.0, 12.0) }).Lines);
            Assert.Equal(19.5, l.A.X, 3); Assert.Equal(19.5, l.B.X, 3);
        }

        [Fact] public void TwoDrains_Diagonal_OneStraightLine()      // bisector y = 48 − 2x
        {
            var l = Assert.Single(TestRoofs.Find(TestRoofs.Rectangle(), new[] { (6.0, 6.0), (30.0, 18.0) }).Lines);
            var ends = new[] { l.A, l.B }.OrderBy(p => p.Y).ToArray();
            Assert.True(Near(ends[0], 24, 0, 1e-3) && Near(ends[1], 12, 24, 1e-3));
        }

        [Fact] public void DrainsOnRoofEdge_BisectorEdgeToEdge()
        {
            var l = Assert.Single(TestRoofs.Find(TestRoofs.Rectangle(), new[] { (0.0, 12.0), (40.0, 12.0) }).Lines);
            Assert.Equal(20, l.A.X, 3); Assert.Equal(20, l.B.X, 3);
        }

        [Fact] public void SameGroup_NoRidgeBetweenThem()
        {
            var r = TestRoofs.Find(TestRoofs.Rectangle(), new[] { (8.0, 12.0), (32.0, 12.0) }, new[] { 0, 0 });
            Assert.Empty(r.Lines); Assert.Equal(1, r.GroupCount);
        }

        [Fact] public void GroupOfTwo_RidgeKinksBetweenItsDrains()   // kink near (19.25, 12), ends (22.25, 0) and (22.25, 24)
        {
            var r = TestRoofs.Find(TestRoofs.Rectangle(), new[] { (8.0, 6.0), (8.0, 18.0), (32.0, 12.0) }, new[] { 0, 0, 1 });
            Assert.Equal(2, r.Lines.Count); Assert.Equal(1, r.RidgeCount); Assert.Empty(r.Junctions);
            var pts = r.Lines.SelectMany(x => new[] { x.A, x.B }).ToList();
            Assert.Contains(pts, p => Near(p, 22.25, 0, 0.05));    // kept kink point sits within h of the true kink,
            Assert.Contains(pts, p => Near(p, 22.25, 24, 0.05));   // so the extended ends can drift slightly
            Assert.Contains(pts, p => Near(p, 19.25, 12, 0.25));
        }

        [Fact] public void Opening_RidgeBendsAndStopsAtTheOpening()
        {
            var hole = (16.0, 8.0, 24.0, 14.0);
            var r = TestRoofs.Find(TestRoofs.Rectangle(hole: hole), new[] { (20.0, 4.0), (20.0, 20.0) });
            Assert.NotEmpty(r.Lines);
            Assert.DoesNotContain(r.Lines, l => TestRoofs.HitsHole(l, hole));
            var pts = r.Lines.SelectMany(x => new[] { x.A, x.B }).ToList();
            Assert.Contains(pts, p => Near(p, 0, 12, 1e-2));
            Assert.Contains(pts, p => Near(p, 40, 12, 1e-2));
            Assert.Contains(pts, p => Near(p, 16, 11.777, 0.05));   // round-the-corner tie, not y = 12
            Assert.Contains(pts, p => Near(p, 24, 11.777, 0.05));
        }

        [Fact] public void Opening_TieOnTheOpeningSide_IsTheRoundTheCornerDistance()
        {
            // On the opening's sides both drains are reached round its corners:
            // √32 + (y − 8) = √52 + (14 − y)  →  y = (22 + √52 − √32) / 2 ≈ 11.777.
            double tie = (22 + Math.Sqrt(52) - Math.Sqrt(32)) / 2;
            var hole = (16.0, 8.0, 24.0, 14.0);
            var r = TestRoofs.Find(TestRoofs.Rectangle(hole: hole), new[] { (20.0, 4.0), (20.0, 20.0) });
            var pts = r.Lines.SelectMany(x => new[] { x.A, x.B }).ToList();
            Assert.Contains(pts, p => Near(p, 16, tie, 0.01));
            Assert.Contains(pts, p => Near(p, 24, tie, 0.01));
            Assert.Equal(1, r.RidgeCount);
        }

        [Fact] public void RidgeThroughGridNodes_StaysOneLine()
        {
            // y = x runs exactly through the grid nodes (both drains are exactly as far from each),
            // so neighbouring crossings coincide and zero-length segments appear between them.
            var l = Assert.Single(TestRoofs.Find(TestRoofs.Rectangle(24, 24), new[] { (4.0, 20.0), (20.0, 4.0) }).Lines);
            var ends = new[] { l.A, l.B }.OrderBy(p => p.X).ToArray();
            Assert.True(Near(ends[0], 0, 0, 1e-3) && Near(ends[1], 24, 24, 1e-3), $"{l.A} – {l.B}");
        }

        [Fact] public void DrainRingedByAnotherGroup_ClosedRidge()
        {
            // A drain at the centre, eight drains of another group round it at radius 8: the ridge is
            // the octagon with apothem 4. A closed loop has no free ends, so nothing is extended.
            var drains = new[] { (20.0, 20.0) }.Concat(Enumerable.Range(0, 8).Select(k =>
                (20 + 8 * Math.Cos(k * Math.PI / 4), 20 + 8 * Math.Sin(k * Math.PI / 4)))).ToArray();
            var r = TestRoofs.Find(TestRoofs.Rectangle(40, 40), drains, new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 });
            Assert.Equal(8, r.Lines.Count); Assert.Equal(1, r.RidgeCount);
            var ends = r.Lines.SelectMany(x => new[] { x.A, x.B }).ToList();
            Assert.All(ends, p => Assert.Equal(2, ends.Count(q => q.Equals(p))));
            Assert.All(ends, p => Assert.InRange(Math.Sqrt((p.X - 20) * (p.X - 20) + (p.Y - 20) * (p.Y - 20)),
                4 - 1e-3, 4 / Math.Cos(Math.PI / 8) + 1e-3));
        }

        [Fact] public void MaxDistance_RidgeStopsWhereTheSlopeStops()  // |y − 12| ≤ 5 when 13 from both drains
        {
            var l = Assert.Single(TestRoofs.Find(TestRoofs.Rectangle(), new[] { (8.0, 12.0), (32.0, 12.0) }, maxDistance: 13).Lines);
            var ys = new[] { l.A.Y, l.B.Y }.OrderBy(y => y).ToArray();
            Assert.InRange(ys[0], 6.5, 7.5); Assert.InRange(ys[1], 16.5, 17.5);
        }

        [Fact] public void DrainsJustOutsideTheOutline_AreMovedOntoIt()   // 0.9 mm outside each end
        {
            var r = TestRoofs.Find(TestRoofs.Rectangle(), new[] { (-0.003, 12.0), (40.003, 12.0) });
            var l = Assert.Single(r.Lines);
            Assert.Equal(20, l.A.X, 3); Assert.Equal(20, l.B.X, 3);
            Assert.Equal(2, r.DrainsMoved); Assert.Equal(0, r.DrainsOutside);
        }

        [Fact] public void DrainFarOutsideTheOutline_IsCountedAndLeftOut()
        {
            var r = TestRoofs.Find(TestRoofs.Rectangle(), new[] { (8.0, 12.0), (32.0, 12.0), (41.0, 12.0) });
            Assert.Equal(1, r.DrainsOutside); Assert.Equal(0, r.DrainsMoved); Assert.Equal(3, r.GroupCount);
            var l = Assert.Single(r.Lines);
            Assert.Equal(20, l.A.X, 3); Assert.Equal(20, l.B.X, 3);
        }

        [Fact] public void MaxDistance_ClippedEndNearTheEdge_IsNotExtended()
        {
            // Sloped up to |y − 12| ≤ √(16.8² − 12²) ≈ 11.76, so both cut-off ends lie within 3h (0.49) of the
            // roof edge, but the edge points beyond them are not sloped: the ridge must stop at the cut-off.
            var r = TestRoofs.Find(TestRoofs.Rectangle(), new[] { (8.0, 12.0), (32.0, 12.0) }, maxDistance: 16.8);
            var l = Assert.Single(r.Lines);
            var ys = new[] { l.A.Y, l.B.Y }.OrderBy(y => y).ToArray();
            Assert.InRange(ys[0], 0.22, 0.5); Assert.InRange(ys[1], 23.5, 23.77);
        }

        // Trapezoid 60 × 40 whose top edge runs (60, 10) → (0, 40). Two drains 6 ft apart whose bisector meets
        // that edge at (30, 25), at `deg` degrees to it; their midpoint is `along` ft from (30, 25).
        static RoofOutline Trapezoid() => TestRoofs.Polygon((0, 0), (60, 0), (60, 10), (0, 40));

        static (double X, double Y)[] DrainsMeetingTopEdge(double deg, double along, out double ux, out double uy)
        {
            double ex = 2 / Math.Sqrt(5), ey = -1 / Math.Sqrt(5);   // along the top edge, towards (60, 10)
            double a = deg * Math.PI / 180;
            ux = ex * Math.Cos(a) + ey * Math.Sin(a);               // bisector direction, into the roof
            uy = -ex * Math.Sin(a) + ey * Math.Cos(a);
            double mx = 30 + along * ux, my = 25 + along * uy;
            return new[] { (mx - 3 * uy, my + 3 * ux), (mx + 3 * uy, my - 3 * ux) };
        }

        [Fact] public void ShallowEdge_25Degrees_OneLineReachingTheEdge()
        {
            var drains = DrainsMeetingTopEdge(25, 16, out double ux, out double uy);
            var l = Assert.Single(TestRoofs.Find(Trapezoid(), drains).Lines);
            Assert.True(Near(l.A, 30, 25, 0.05) || Near(l.B, 30, 25, 0.05), $"{l.A} – {l.B}");
            double xBottom = 30 - 25 * ux / uy;                     // the other end, on y = 0
            Assert.True(Near(l.A, xBottom, 0, 0.05) || Near(l.B, xBottom, 0, 0.05), $"{l.A} – {l.B}");
        }

        [Fact] public void ShallowEdge_10Degrees_OneLineReachingTheEdge()
        {
            var drains = DrainsMeetingTopEdge(10, 25, out double ux, out double uy);
            var l = Assert.Single(TestRoofs.Find(Trapezoid(), drains).Lines);
            Assert.True(Near(l.A, 30, 25, 0.05) || Near(l.B, 30, 25, 0.05), $"{l.A} – {l.B}");
            double yRight = 25 + 30 * uy / ux;                      // the other end, on x = 60
            Assert.True(Near(l.A, 60, yRight, 0.05) || Near(l.B, 60, yRight, 0.05), $"{l.A} – {l.B}");
        }

        [Fact] public void OneDrain_NoLines() =>
            Assert.Empty(TestRoofs.Find(TestRoofs.Rectangle(), new[] { (8.0, 12.0) }).Lines);

        [Fact] public void BadInput_Throws()
        {
            Assert.Throws<ArgumentException>(() => RidgeFinder.Find(new RidgeInput { Drains = new Point3[0], DrainGroup = new int[0] }));
            Assert.Throws<ArgumentException>(() => RidgeFinder.Find(new RidgeInput
                { Outline = TestRoofs.Rectangle(), Drains = new[] { new Point3(1, 1, 0) }, DrainGroup = new int[0] }));
        }

        [Fact] public void Cancelled_Throws() =>
            Assert.Throws<OperationCanceledException>(() => TestRoofs.Find(TestRoofs.Rectangle(),
                new[] { (8.0, 12.0), (32.0, 12.0) }, cancel: new CancellationToken(true)));
    }
}
