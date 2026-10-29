using System;
using System.Collections.Generic;
using System.Threading;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services;
using Xunit;

namespace AutoSlopeByPoint.Tests
{
    public class GeodesicFieldTests
    {
        [Fact] public void OpenRoof_IsTheStraightLine()
        {
            var f = new GeodesicField(TestRoofs.Rectangle(), new[] { new Point3(8, 12, 0) }, new[] { 0 });
            Assert.Equal(12, f.DistanceTo(new Point3(20, 12, 0), 0), 9);
        }

        [Fact] public void Opening_DistanceGoesRoundTheCorners()
        {
            var f = new GeodesicField(TestRoofs.Rectangle(hole: (16, 8, 24, 14)), new[] { new Point3(20, 4, 0) }, new[] { 0 });
            Assert.Equal(6 + 8 * Math.Sqrt(2), f.DistanceTo(new Point3(20, 18, 0), 0), 6);
        }

        [Fact] public void Nearest_TieGoesToTheLowerGroup()
        {
            var f = new GeodesicField(TestRoofs.Rectangle(),
                new[] { new Point3(8, 12, 0), new Point3(32, 12, 0) }, new[] { 1, 0 });
            Assert.Equal(0, f.Nearest(new Point3(20, 12, 0), out double d));
            Assert.Equal(12, d, 9);
            Assert.Equal(1, f.Nearest(new Point3(10, 12, 0), out _));
        }

        [Fact] public void OutsideTheRoof_IsUnreachable()
        {
            var f = new GeodesicField(TestRoofs.Rectangle(hole: (16, 8, 24, 14)), new[] { new Point3(8, 12, 0) }, new[] { 0 });
            Assert.True(double.IsPositiveInfinity(f.DistanceTo(new Point3(20, 11, 0), 0)));
            Assert.Equal(-1, f.Nearest(new Point3(50, 11, 0), out double d));
            Assert.True(double.IsPositiveInfinity(d));
        }

        [Fact] public void Groups_AreDistinctAndAscending()
        {
            var f = new GeodesicField(TestRoofs.Rectangle(),
                new[] { new Point3(5, 5, 0), new Point3(10, 5, 0), new Point3(15, 5, 0), new Point3(20, 5, 0) },
                new[] { 7, 2, 7, 4 });
            Assert.Equal(new[] { 2, 4, 7 }, f.Groups);
            Assert.Equal(3, f.GroupCount);
        }

        [Fact] public void DistanceTo_UnknownGroup_IsInfinite()
        {
            var f = new GeodesicField(TestRoofs.Rectangle(), new[] { new Point3(8, 12, 0) }, new[] { 3 });
            Assert.True(double.IsPositiveInfinity(f.DistanceTo(new Point3(20, 12, 0), 0)));
            Assert.True(double.IsPositiveInfinity(f.DistanceTo(new Point3(20, 12, 0), -1)));
            Assert.Equal(12, f.DistanceTo(new Point3(20, 12, 0), 3), 9);
        }

        [Fact] public void NoDrains_NothingIsReachable()
        {
            var f = new GeodesicField(TestRoofs.Rectangle(), new Point3[0], new int[0]);
            Assert.Equal(0, f.GroupCount);
            Assert.Equal(-1, f.Nearest(new Point3(20, 12, 0), out double d));
            Assert.True(double.IsPositiveInfinity(d));
        }

        [Fact] public void Nearest_ComparesDistanceRoundTheOpening_NotTheStraightLine()
        {
            // Group 0 is 16 ft from the point in a straight line, but the opening is in the way:
            // 5 + 8 + 5 = 18 ft round it. Group 1 is 16.28 ft away in the open.
            var f = new GeodesicField(TestRoofs.Rectangle(hole: (16, 8, 24, 14)),
                new[] { new Point3(12, 11, 0), new Point3(40, 0, 0) }, new[] { 0, 1 });
            var p = new Point3(28, 11, 0);
            Assert.Equal(18, f.DistanceTo(p, 0), 9);
            Assert.Equal(1, f.Nearest(p, out double d));
            Assert.Equal(Math.Sqrt(12 * 12 + 11 * 11), d, 9);
        }

        [Fact] public void LShape_PathBendsAtTheInnerCorner()
        {
            var roof = TestRoofs.Polygon((0, 0), (40, 0), (40, 10), (20, 10), (20, 24), (0, 24));
            var f = new GeodesicField(roof, new[] { new Point3(35, 5, 0) }, new[] { 0 });
            double expected = Math.Sqrt(15 * 15 + 5 * 5) + Math.Sqrt(10 * 10 + 10 * 10);
            Assert.Equal(expected, f.DistanceTo(new Point3(10, 20, 0), 0), 9);
        }

        [Fact] public void DrainOnAnOpeningCorner_IsFine()
        {
            var f = new GeodesicField(TestRoofs.Rectangle(hole: (16, 8, 24, 14)), new[] { new Point3(16, 8, 0) }, new[] { 0 });
            Assert.Equal(Math.Sqrt(32), f.DistanceTo(new Point3(20, 4, 0), 0), 9);
            Assert.Equal(6, f.DistanceTo(new Point3(16, 14, 0), 0), 9);
        }

        [Fact] public void BadInput_IsRejected()
        {
            var roof = TestRoofs.Rectangle();
            var one = new[] { new Point3(8, 12, 0) };
            Assert.Throws<ArgumentException>(() => new GeodesicField(null, one, new[] { 0 }));
            Assert.Throws<ArgumentException>(() => new GeodesicField(roof, null, new[] { 0 }));
            Assert.Throws<ArgumentException>(() => new GeodesicField(roof, one, null));
            Assert.Throws<ArgumentException>(() => new GeodesicField(roof, one, new[] { 0, 1 }));
            Assert.Throws<ArgumentException>(() => new GeodesicField(roof, one, new[] { -1 }));
        }

        [Fact] public void Cancellation_IsHonoured()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    new GeodesicField(TestRoofs.Rectangle(hole: (16, 8, 24, 14)),
                        new[] { new Point3(8, 12, 0) }, new[] { 0 }, cts.Token));
            }
        }
    }
}
