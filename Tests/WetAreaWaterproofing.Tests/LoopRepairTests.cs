using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace WetAreaWaterproofing.Tests
{
    public class LoopRepairTests
    {
        internal static readonly WetAreaSettings Defaults = new WetAreaSettings().Normalize();

        /// <summary>Closed loop through the points, as segments.</summary>
        internal static List<Seg> Loop(params (double x, double y)[] pts)
        {
            var segs = new List<Seg>();
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Length];
                segs.Add(new Seg(new Pt(a.x, a.y), new Pt(b.x, b.y)));
            }
            return segs;
        }

        internal static List<Seg> Rect(double w = 4000, double h = 3000, double x0 = 0, double y0 = 0)
            => Loop((x0, y0), (x0 + w, y0), (x0 + w, y0 + h), (x0, y0 + h));

        /// <summary>4000 x 3000 rectangle whose bottom edge stops <paramref name="gap"/> mm short of the corner.</summary>
        internal static List<Seg> RectWithGap(double gap)
        {
            var segs = Rect();
            segs[0] = new Seg(new Pt(0, 0), new Pt(4000 - gap, 0));
            return segs;
        }

        [Fact]
        public void CleanSquareNeedsNoRepair()
        {
            var r = LoopRepair.Repair(Rect(), Defaults);
            Assert.True(r.Ok);
            Assert.Empty(r.Steps);
            Assert.Equal(4, r.Points.Count);
            Assert.Equal(12.0, Polyline2D.AreaM2(r.Points), 6);
        }

        [Fact]
        public void ReversedAndShuffledSegmentsAreChained()
        {
            var segs = Rect();
            segs[1] = new Seg(segs[1].B, segs[1].A);
            (segs[0], segs[3]) = (segs[3], segs[0]);
            var r = LoopRepair.Repair(segs, Defaults);
            Assert.True(r.Ok);
            Assert.Empty(r.Steps);
            Assert.Equal(12.0, Polyline2D.AreaM2(r.Points), 6);
        }

        [Fact]
        public void CollinearPiecesAreMergedToFourCorners()
        {
            var segs = new List<Seg>();
            for (int i = 0; i < 8; i++) segs.Add(new Seg(new Pt(i * 500, 0), new Pt((i + 1) * 500, 0)));
            segs.AddRange(Rect().Skip(1));
            var r = LoopRepair.Repair(segs, Defaults);
            Assert.True(r.Ok);
            Assert.Equal(4, r.Points.Count);
        }

        [Fact]
        public void ShortSegmentsRemoved()
        {
            var segs = Rect();
            segs.Add(new Seg(new Pt(2000, 1500), new Pt(2000.5, 1500))); // stray 0.5 mm stub
            var r = LoopRepair.Repair(segs, Defaults);
            Assert.True(r.Ok);
            Assert.Contains(r.Steps, s => s.Method == 2 && s.Passed);
            Assert.Equal(12.0, Polyline2D.AreaM2(r.Points), 6);
        }

        [Fact]
        public void OverlappingCollinearPiecesAreMerged()
        {
            var segs = Rect();
            segs[0] = new Seg(new Pt(0, 0), new Pt(2500, 0));
            segs.Add(new Seg(new Pt(1500, 0), new Pt(4000, 0)));
            var r = LoopRepair.Repair(segs, Defaults);
            Assert.True(r.Ok);
            Assert.Contains(r.Steps, s => s.Method == 3 && s.Passed);
            Assert.Equal(12.0, Polyline2D.AreaM2(r.Points), 6);
        }

        [Fact]
        public void GapOf3mmIsSnapped()
        {
            var r = LoopRepair.Repair(RectWithGap(3), Defaults);
            Assert.True(r.Ok);
            var snap = Assert.Single(r.Steps, s => s.Method == 4);
            Assert.True(snap.Passed);
            Assert.Contains("3 mm", snap.Detail);
            Assert.NotNull(snap.At);
            Assert.Equal(12.0, Polyline2D.AreaM2(r.Points), 6); // corner restored at the lines' intersection
        }

        [Fact]
        public void GapOf30mmIsBridged()
        {
            var r = LoopRepair.Repair(RectWithGap(30), Defaults);
            Assert.True(r.Ok);
            Assert.Contains(r.Steps, s => s.Method == 4 && !s.Passed);
            Assert.Contains(r.Steps, s => s.Method == 5 && s.Passed && s.Detail.Contains("30 mm"));
        }

        [Fact]
        public void GapOf80mmFails()
        {
            var r = LoopRepair.Repair(RectWithGap(80), Defaults);
            Assert.False(r.Ok);
            Assert.Contains(r.Steps, s => s.Method == 5 && !s.Passed && s.Detail.Contains("80 mm"));
        }

        [Fact]
        public void BowTieOverlapRemoved()
        {
            var r = LoopRepair.Repair(Loop((0, 0), (4000, 3000), (4000, 0), (0, 3000)), Defaults);
            Assert.True(r.Ok);
            Assert.Contains(r.Steps, s => s.Method == 6 && s.Passed);
            Assert.False(Polyline2D.SelfIntersects(r.Points));
            Assert.Equal(3.0, Polyline2D.AreaM2(r.Points), 3);
        }

        [Fact]
        public void EmptyOrTinyInputFailsWithoutThrowing()
        {
            Assert.False(LoopRepair.Repair(new List<Seg>(), Defaults).Ok);
            Assert.False(LoopRepair.Repair(null, Defaults).Ok);
            Assert.False(LoopRepair.Repair(new List<Seg> { new Seg(new Pt(0, 0), new Pt(1000, 0)) }, Defaults).Ok);
        }

        [Fact]
        public void ResultIsCounterClockwise()
        {
            var r = LoopRepair.Repair(Loop((0, 0), (0, 3000), (4000, 3000), (4000, 0)), Defaults);
            Assert.True(Polyline2D.SignedAreaMm2(r.Points) > 0);
        }
    }
}
