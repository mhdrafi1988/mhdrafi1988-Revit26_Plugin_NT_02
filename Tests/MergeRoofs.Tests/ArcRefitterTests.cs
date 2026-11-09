using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Services;
using Xunit;

namespace MergeRoofs.Tests
{
    public class ArcRefitterTests
    {
        // D-shaped roof: straight base from (-5,0) to (5,0), semicircle of radius 5 over the top.
        private static RoofFootprint DShape()
        {
            var edges = new List<Edge>
            {
                new Edge { A = new Pt(-5, 0), B = new Pt(5, 0) },
                new Edge { A = new Pt(5, 0), B = new Pt(-5, 0), ArcMid = new Pt(0, 5) },
            };
            return new RoofFootprint("d", new Loop(edges), new List<Loop>());
        }

        private static Loop Flattened(RoofFootprint f) =>
            new(ArcGeometry.Ring(f.Outer).Let(pts =>
                (IReadOnlyList<Edge>)Enumerable.Range(0, pts.Count).Select(i => new Edge { A = pts[i], B = pts[(i + 1) % pts.Count] }).ToList()));

        [Fact]
        public void FlattenedHalfCircle_IsRefitToSingleArcEdge()
        {
            var src = DShape();
            var result = ArcRefitter.Refit(Flattened(src), new[] { src }, 0.0164);
            var arcs = result.Edges.Where(e => e.ArcMid != null).ToList();
            var arc = Assert.Single(arcs);
            Assert.True(Math.Abs(arc.ArcMid!.Value.Y - 5) < 0.0164 && Math.Abs(arc.ArcMid.Value.X) < 0.0164 + 0.3);
            Assert.Equal(2, result.Edges.Count);
        }

        [Fact]
        public void StraightEdges_AreNeverConvertedToArcs()
        {
            var rect = TestShapes.Rect("r", 0, 0, 10, 10);
            var result = ArcRefitter.Refit(rect.Outer, new[] { rect }, 0.0164);
            Assert.All(result.Edges, e => Assert.Null(e.ArcMid));
        }

        [Fact]
        public void NoMatchingArc_ReturnsLoopUnchanged()
        {
            var src = DShape();
            var other = TestShapes.Rect("r", 100, 100, 110, 110);
            var flat = Flattened(src);
            var result = ArcRefitter.Refit(flat, new[] { other }, 0.0164);
            Assert.Same(flat, result);
        }
    }

    internal static class Ext { public static R Let<T, R>(this T t, Func<T, R> f) => f(t); }
}
