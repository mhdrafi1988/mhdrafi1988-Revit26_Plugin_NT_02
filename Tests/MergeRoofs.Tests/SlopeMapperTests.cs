using System.Linq;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Services;
using Xunit;
using static MergeRoofs.Tests.TestShapes;

namespace MergeRoofs.Tests
{
    public class SlopeMapperTests
    {
        [Fact]
        public void MergedEdge_TakesSlopeOfContainingSourceEdge()
        {
            var a = Rect("a", 0, 0, 10, 10, southSlope: 0.1);
            var b = Rect("b", 10, 0, 20, 10, southSlope: 0.2);
            var merged = new[] { new Edge { A = new Pt(0, 0), B = new Pt(10, 0) }, new Edge { A = new Pt(10, 0), B = new Pt(20, 0) } };
            var r = SlopeMapper.Apply(merged, new[] { a, b }, 0.0164);
            Assert.Equal(0.1, r[0].SlopeAngle);
            Assert.Equal(0.2, r[1].SlopeAngle);
            Assert.All(r, e => Assert.True(e.DefinesSlope));
        }

        [Fact]
        public void Analyze_CarriesSlopeToMergedSouthEdges()
        {
            var res = MergeAnalyzer.Analyze(new[] { Rect("a", 0, 0, 10, 10, 0.1), Rect("b", 10, 0, 20, 10, 0.2) }, 0.0164);
            var south = res.Outer[0].Edges.Where(e => e.A.Y == 0 && e.B.Y == 0).ToList();
            Assert.Contains(south, e => e.SlopeAngle == 0.1);
            Assert.Contains(south, e => e.SlopeAngle == 0.2);
        }
    }
}
