using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        [Fact] public void MaxDistance_ClippedEnd_IsNotPushedIntoAnotherGroupsSlope()
        {
            // The a–b ridge is cut off by the drainage distance 2h before the top edge (10° to it). The edge
            // point beyond the cut-off is sloped, but only by a third drain c 22 ft away; a and b do not reach
            // it. The end must stay at the cut-off, so no line runs over roof that no drain slopes.
            var ab = DrainsMeetingTopEdge(10, 25, out double ux, out double uy);
            double h = Math.Sqrt(60 * 40 / 40000.0), s = 2 * h / Math.Sin(10 * Math.PI / 180);
            double cutX = 30 + s * ux, cutY = 25 + s * uy;
            double max = Math.Sqrt((cutX - ab[0].X) * (cutX - ab[0].X) + (cutY - ab[0].Y) * (cutY - ab[0].Y));
            var drains = ab.Append((X: 10.3, Y: 34.7)).ToArray();
            var r = TestRoofs.Find(Trapezoid(), drains, maxDistance: max);
            Assert.NotEmpty(r.Lines);
            foreach (var l in r.Lines)
                for (int k = 0; k <= 20; k++)
                {
                    double x = l.A.X + k / 20.0 * (l.B.X - l.A.X), y = l.A.Y + k / 20.0 * (l.B.Y - l.A.Y);
                    double d = drains.Min(q => Math.Sqrt((q.X - x) * (q.X - x) + (q.Y - y) * (q.Y - y)));
                    Assert.True(d <= max + 1e-3, $"({x:0.###}, {y:0.###}) on {l.A} – {l.B} is {d - max:0.###} ft past the slope");
                }
            Assert.DoesNotContain(r.Lines.SelectMany(l => new[] { l.A, l.B }), p => Near(p, 30, 25, 0.5));
        }

        [Fact] public void ThreeDrains_JunctionIsTheCircumcentre()   // Voronoi vertex = Delaunay circumcentre
        {
            var r = TestRoofs.Find(TestRoofs.Rectangle(), new[] { (6.0, 6.0), (34.0, 8.0), (18.0, 20.0) });
            var j = Assert.Single(r.Junctions);
            Assert.True(Near(j, 14768.0 / 736, 4480.0 / 736, 1e-3), $"junction at {j}");
            Assert.Equal(3, r.Lines.Count); Assert.Equal(3, r.RidgeCount);
            Assert.All(r.Lines, l => Assert.True(Near(l.A, j.X, j.Y, 1e-6) || Near(l.B, j.X, j.Y, 1e-6)));
        }

        [Fact] public void FourDrainsInASquare_OneFourWayJunction()
        {
            var r = TestRoofs.Find(TestRoofs.Rectangle(), new[] { (10.0, 6.0), (30.0, 6.0), (10.0, 18.0), (30.0, 18.0) });
            var j = Assert.Single(r.Junctions);
            Assert.True(Near(j, 20, 12, 1e-3));
            Assert.Equal(4, r.Lines.Count); Assert.Equal(4, r.RidgeCount);
        }

        // Three drains 5 ft from V at the given angles (degrees). On the 40 × 24 roof the grid step is
        // RidgeFinder.MinGridStepFt and the nodes sit at multiples of it.
        static (double X, double Y)[] DrainsRound(double vx, double vy, params double[] deg) =>
            deg.Select(a => (vx + 5 * Math.Cos(a * Math.PI / 180), vy + 5 * Math.Sin(a * Math.PI / 180))).ToArray();

        static void AssertThreeLinesMeetAt(RidgeResult r, double vx, double vy)
        {
            var j = Assert.Single(r.Junctions);
            Assert.True(Near(j, vx, vy, 1e-3), $"junction at {j}");
            Assert.Equal(3, r.Lines.Count);
            Assert.All(r.Lines, l => Assert.True(Near(l.A, j.X, j.Y, 1e-6) || Near(l.B, j.X, j.Y, 1e-6), $"{l.A} – {l.B}"));
        }

        [Fact] public void JunctionInACellWhoseCornersShowTwoGroups_ThreeLinesMeetThere()
        {
            // V sits 0.6 h above a grid line and c's area (below V, 60° wide) passes between the two lower
            // corners of V's cell, so the corners show only a and b. The a–b tie on that side lies in c's area,
            // 0.098 ft below V: joined to it, the a–b ridge would fold back on itself.
            double h = RidgeFinder.MinGridStepFt, vx = 122.5 * h, vy = 37.6 * h;
            var r = TestRoofs.Find(TestRoofs.Rectangle(), DrainsRound(vx, vy, 330, 210, 270));
            AssertThreeLinesMeetAt(r, vx, vy);
        }

        [Fact] public void JunctionOnAGridNode_ThreeLinesMeetThere()
        {
            double h = RidgeFinder.MinGridStepFt, vx = 122 * h, vy = 37 * h;
            var r = TestRoofs.Find(TestRoofs.Rectangle(), DrainsRound(vx, vy, 90, 210, 330));
            AssertThreeLinesMeetAt(r, vx, vy);
        }

        [Fact] public void JunctionNextToAGridNode_ThreeLinesMeetThere()
        {
            // V ≈ (18.6933, 18.8796) lies 0.05 h left of and 0.09 h above a grid node, so the cells round
            // that node show three groups although only one holds V (a case found by random testing).
            var drains = new[] { (29.112862, 7.470299), (9.57859, 6.403148), (34.022788, 20.815098) };
            var r = TestRoofs.Find(TestRoofs.Rectangle(), drains);
            var j = Assert.Single(r.Junctions);
            AssertThreeLinesMeetAt(r, j.X, j.Y);
            Assert.True(Near(j, 18.6933, 18.8796, 1e-3), $"junction at {j}");
        }

        [Fact] public void VertexJustOutsideTheRoof_TwoLinesToTheEdge_NoJunction()
        {
            // V = (20.1, −0.05) is outside, below the bottom edge. Only the a–b and b–c ridges reach the roof,
            // meeting the edge 0.035 ft either side of x = V.x; between them b's area touches the edge.
            double h = RidgeFinder.MinGridStepFt, vx = 122.5 * h, vy = -0.05;
            var r = TestRoofs.Find(TestRoofs.Rectangle(), DrainsRound(vx, vy, 20, 90, 160));
            Assert.Empty(r.Junctions);
            Assert.Equal(2, r.Lines.Count); Assert.Equal(2, r.RidgeCount);
            double dx = 0.05 / Math.Tan(55 * Math.PI / 180);
            var ends = r.Lines.SelectMany(l => new[] { l.A, l.B }).ToList();
            Assert.Contains(ends, p => Near(p, vx - dx, 0, 1e-3));
            Assert.Contains(ends, p => Near(p, vx + dx, 0, 1e-3));
        }

        // Proper crossing: each line has its ends strictly on both sides of the other (a shared end is not one).
        static bool Cross(RidgeLine p, RidgeLine q)
        {
            static double Side(Point3 a, Point3 b, Point3 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            const double e = 1e-7;
            double d1 = Side(p.A, p.B, q.A), d2 = Side(p.A, p.B, q.B), d3 = Side(q.A, q.B, p.A), d4 = Side(q.A, q.B, p.B);
            return ((d1 > e && d2 < -e) || (d1 < -e && d2 > e)) && ((d3 > e && d4 < -e) || (d3 < -e && d4 > e));
        }

        [Theory, InlineData(0.1), InlineData(0.3)]
        public void JunctionNearTheEdge_NoLinesCross(double vy)
        {
            // Three drains round the Voronoi vertex V = (20, vy), just inside the bottom edge: a and b mirror each
            // other about the a–b ridge, which leaves V at 15° to the edge; c is as far from V, up and to the left.
            double t = 15 * Math.PI / 180, ux = Math.Cos(t), uy = Math.Sin(t);
            double mx = 20 + 10 * ux, my = vy + 10 * uy, radius = Math.Sqrt(104), cl = Math.Sqrt(0.958 * 0.958 + 0.287 * 0.287);
            var drains = new[] { (mx - 2 * uy, my + 2 * ux), (mx + 2 * uy, my - 2 * ux),
                                 (20 - radius * 0.958 / cl, vy + radius * 0.287 / cl) };
            var r = TestRoofs.Find(TestRoofs.Rectangle(), drains);
            for (int i = 0; i < r.Lines.Count; i++)
                for (int k = i + 1; k < r.Lines.Count; k++)
                    Assert.False(Cross(r.Lines[i], r.Lines[k]), $"{r.Lines[i].A} – {r.Lines[i].B} crosses {r.Lines[k].A} – {r.Lines[k].B}");
            var j = Assert.Single(r.Junctions);
            Assert.True(Near(j, 20, vy, 1e-3), $"junction at {j}");
            Assert.Equal(3, r.Lines.Count);
        }

        // A clean ridge network: no two lines cross (except at a shared end), no line appears twice (same ends
        // within 1e-6, either order), and no closed loop is shorter than 3 grid steps. The shortest loop through
        // a line is that line plus the shortest path between its ends without it (ends within 1e-6 are one node).
        static void AssertCleanNetwork(RidgeResult r)
        {
            var lines = r.Lines;
            for (int i = 0; i < lines.Count; i++)
                for (int k = i + 1; k < lines.Count; k++)
                {
                    RidgeLine p = lines[i], q = lines[k];
                    Assert.False(Cross(p, q), $"{p.A} – {p.B} crosses {q.A} – {q.B}");
                    bool same = (Near(p.A, q.A.X, q.A.Y, 1e-6) && Near(p.B, q.B.X, q.B.Y, 1e-6)) ||
                                (Near(p.A, q.B.X, q.B.Y, 1e-6) && Near(p.B, q.A.X, q.A.Y, 1e-6));
                    Assert.False(same, $"{p.A} – {p.B} appears twice");
                }

            var nodes = new List<Point3>();
            int Id(Point3 x)
            {
                int i = nodes.FindIndex(n => Near(n, x.X, x.Y, 1e-6));
                if (i >= 0) return i;
                nodes.Add(x);
                return nodes.Count - 1;
            }
            var edges = lines.Select(l => (A: Id(l.A), B: Id(l.B), Len: Math.Sqrt((l.A.X - l.B.X) * (l.A.X - l.B.X) + (l.A.Y - l.B.Y) * (l.A.Y - l.B.Y)))).ToList();
            for (int e = 0; e < edges.Count; e++)
            {
                var dist = Enumerable.Repeat(double.PositiveInfinity, nodes.Count).ToArray();
                var done = new bool[nodes.Count];
                dist[edges[e].A] = 0;
                while (true)   // Dijkstra from one end to the other, without line e
                {
                    int u = -1;
                    for (int n = 0; n < nodes.Count; n++)
                        if (!done[n] && dist[n] < double.PositiveInfinity && (u < 0 || dist[n] < dist[u])) u = n;
                    if (u < 0 || u == edges[e].B) break;
                    done[u] = true;
                    for (int f = 0; f < edges.Count; f++)
                    {
                        if (f == e) continue;
                        int v = edges[f].A == u ? edges[f].B : edges[f].B == u ? edges[f].A : -1;
                        if (v >= 0 && dist[u] + edges[f].Len < dist[v]) dist[v] = dist[u] + edges[f].Len;
                    }
                }
                double loop = dist[edges[e].B] + edges[e].Len;
                Assert.True(loop >= 3 * r.GridStep, $"closed loop of {loop:0.###} ft through {lines[e].A} – {lines[e].B}");
            }
        }

        [Fact] public void TwoVerticesAQuarterFootApart_NoCrossingsDuplicatesOrSmallLoops()
        {
            // A 3 × 2 grid of drains on a 164 × 110 roof (h ≈ 0.67): the vertices (109.2819, 55.0585) and
            // (109.4839, 54.8537) are 0.29 ft apart and merge into one junction; a crossing between them
            // made a small triangle whose middle line crossed two of the junction's lines.
            var drains = new[] { (27.64146685816416, 27.403492883967), (27.233744679127703, 82.56994621295945),
                                 (81.95716904660556, 27.61938853683853), (81.93826531569393, 82.47884323656506),
                                 (137.2794878693668, 27.89390542446352), (137.10726821893235, 81.98985429806163) };
            var r = TestRoofs.Find(TestRoofs.Rectangle(164.45488648929395, 109.63659099286264), drains);
            AssertCleanNetwork(r);
        }

        [Fact] public void TwoVerticesOneCellApart_NoCrossingsDuplicatesOrSmallLoops()
        {
            // Vertices (19.5455, 13.7614) and (19.5151, 13.6678), 0.6 h apart: the line between them came out twice.
            var drains = new[] { (24.1214, 12.2146), (14.9330, 15.1955), (21.0360, 18.3560), (18.0246, 9.0735) };
            var r = TestRoofs.Find(TestRoofs.Rectangle(), drains);
            AssertCleanNetwork(r);
        }

        [Fact] public void TwoVerticesNearTheEdge_NoCrossingsDuplicatesOrSmallLoops()
        {
            // Vertices (22.4158, 23.1997) and (22.4268, 23.3636), one cell apart near the top edge: a ridge
            // folded back between them into a small triangle (a case found by random testing).
            var drains = new[] { (34.7017, 9.0509), (18.2153, 4.9381), (34.8392, 7.8884),
                                 (8.3505, 10.8184), (13.89, 3.058), (4.9747, 16.2258) };
            var r = TestRoofs.Find(TestRoofs.Rectangle(), drains);
            AssertCleanNetwork(r);
        }

        [Fact] public void LargeRoof_FinishesInSeconds()
        {
            var holes = new[] { TestRoofs.Box(30, 20, 40, 30), TestRoofs.Box(80, 60, 95, 70),
                                TestRoofs.Box(110, 15, 120, 40), TestRoofs.Circle(150, 50, 6, 72) };
            var outline = new RoofOutline(new[] { TestRoofs.Box(0, 0, 200, 100) }.Concat(holes));
            var rnd = new Random(33);
            var drains = new List<(double X, double Y)>();
            while (drains.Count < 30)                       // each its own group, ≥ 1 away from every opening's box
            {
                double x = 2 + rnd.NextDouble() * 196, y = 2 + rnd.NextDouble() * 96;
                if (holes.All(h => x < h.Min(q => q.X) - 1 || x > h.Max(q => q.X) + 1
                                || y < h.Min(q => q.Y) - 1 || y > h.Max(q => q.Y) + 1)) drains.Add((x, y));
            }
            var watch = Stopwatch.StartNew();
            RidgeResult r = TestRoofs.Find(outline, drains.ToArray());
            Assert.NotEmpty(r.Lines); Assert.NotEmpty(r.Junctions);
            Assert.True(watch.Elapsed.TotalSeconds < 10, $"took {watch.Elapsed.TotalSeconds:0.0} s");
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
