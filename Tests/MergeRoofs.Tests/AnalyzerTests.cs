using System;
using System.Linq;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Services;
using Xunit;
using static MergeRoofs.Tests.TestShapes;

namespace MergeRoofs.Tests
{
    public class AnalyzerTests
    {
        private const double Tol = 5.0 / 304.8;   // 0.016404 ft

        [Fact]
        public void GapBelowTolerance_IsSnapped_WithGapLink()
        {
            var r = MergeAnalyzer.Analyze(new[] { Rect("1", 0, 0, 10, 10), Rect("2", 10.01, 0, 20.01, 10) }, Tol);
            Assert.Single(r.Outer);
            Assert.Contains(r.Issues, i => i.Kind == IssueKind.Gap);
            Assert.NotEmpty(r.GapLinks);
            Assert.True(r.CanCreate);
        }

        [Fact]
        public void GapAboveTolerance_IsNotJoined()
        {
            var r = MergeAnalyzer.Analyze(new[] { Rect("1", 0, 0, 10, 10), Rect("2", 10.05, 0, 20.05, 10) }, Tol);
            Assert.Contains(r.Issues, i => i.Kind == IssueKind.NotJoined);
            Assert.False(r.CanCreate);
        }

        [Fact]
        public void GapEqualToTolerance_Snaps()
        {
            var r = MergeAnalyzer.Analyze(new[] { Rect("1", 0, 0, 10, 10), Rect("2", 10 + Tol, 0, 20 + Tol, 10) }, Tol);
            Assert.True(r.CanCreate);
        }

        [Fact]
        public void ToleranceZero_SnapsNothing()
        {
            var r = MergeAnalyzer.Analyze(new[] { Rect("1", 0, 0, 10, 10), Rect("2", 10.01, 0, 20.01, 10) }, 0);
            Assert.False(r.CanCreate);
        }

        [Fact]
        public void CrossingBoundaries_ReportIntersectionAtCrossPoint()
        {
            var r = MergeAnalyzer.Analyze(new[] { Rect("h", 0, 4, 10, 6), Rect("v", 4, 0, 6, 10) }, Tol);
            var x = Assert.Single(r.Issues, i => i.Kind == IssueKind.Intersection);
            double[] xs = { 4, 6 }, ys = { 4, 6 };
            Assert.Contains(xs, v => Math.Abs(v - x.Location.X) < 1e-6);
            Assert.Contains(ys, v => Math.Abs(v - x.Location.Y) < 1e-6);
        }

        [Fact]
        public void ShortSegmentsBelow0_00256ft_AreRemoved()
        {
            // A rectangle with a 0.001 ft jog in its south edge.
            var pts = new[] { new Pt(0, 0), new Pt(5, 0), new Pt(5, 0.001), new Pt(10, 0.001), new Pt(10, 10), new Pt(0, 10) };
            var edges = Enumerable.Range(0, pts.Length).Select(i => new Edge { A = pts[i], B = pts[(i + 1) % pts.Length] }).ToList();
            var roof = new RoofFootprint("j", new Loop(edges), new System.Collections.Generic.List<Loop>());
            var r = MergeAnalyzer.Analyze(new[] { roof, Rect("2", 10, 0, 20, 10) }, Tol);
            Assert.All(r.Outer[0].Edges, e =>
                Assert.True(Math.Sqrt(Math.Pow(e.A.X - e.B.X, 2) + Math.Pow(e.A.Y - e.B.Y, 2)) >= MergeAnalyzer.ShortCurve));
        }
    }
}
