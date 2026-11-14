using System.Collections.Generic;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models;

namespace MergeRoofs.Tests
{
    internal static class TestShapes
    {
        public static Loop RectLoop(double x0, double y0, double x1, double y1, double southSlope = 0)
        {
            var p = new[] { new Pt(x0, y0), new Pt(x1, y0), new Pt(x1, y1), new Pt(x0, y1) };
            var edges = new List<Edge>();
            for (int i = 0; i < 4; i++)
                edges.Add(new Edge { A = p[i], B = p[(i + 1) % 4], DefinesSlope = i == 0 && southSlope != 0, SlopeAngle = i == 0 ? southSlope : 0 });
            return new Loop(edges);
        }

        public static RoofFootprint Rect(string id, double x0, double y0, double x1, double y1, double southSlope = 0)
            => new(id, RectLoop(x0, y0, x1, y1, southSlope), new List<Loop>());

        public static RoofFootprint RectWithHole(string id, double x0, double y0, double x1, double y1, double h0, double h1, double h2, double h3)
            => new(id, RectLoop(x0, y0, x1, y1), new List<Loop> { RectLoop(h0, h1, h2, h3) });
    }
}
