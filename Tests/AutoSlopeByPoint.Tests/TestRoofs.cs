using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services;

namespace AutoSlopeByPoint.Tests
{
    /// <summary>Small roof outlines (plan loops, feet) shared by the geometry tests.</summary>
    internal static class TestRoofs
    {
        /// <summary>Axis-aligned rectangle as a 4-point loop, counter-clockwise from (x0, y0).</summary>
        public static IReadOnlyList<Point3> Box(double x0, double y0, double x1, double y1) =>
            new[]
            {
                new Point3(x0, y0, 0), new Point3(x1, y0, 0),
                new Point3(x1, y1, 0), new Point3(x0, y1, 0),
            };

        /// <summary>Regular n-gon of radius <paramref name="r"/> around (cx, cy), counter-clockwise.</summary>
        public static IReadOnlyList<Point3> Circle(double cx, double cy, double r, int n) =>
            Enumerable.Range(0, n)
                .Select(i =>
                {
                    double a = 2 * Math.PI * i / n;
                    return new Point3(cx + r * Math.Cos(a), cy + r * Math.Sin(a), 0);
                })
                .ToArray();

        /// <summary>A w x h rectangle with its lower-left corner at the origin, optionally with one
        /// rectangular opening.</summary>
        public static RoofOutline Rectangle(
            double w = 40, double h = 24, (double X0, double Y0, double X1, double Y1)? hole = null)
        {
            var loops = new List<IReadOnlyList<Point3>> { Box(0, 0, w, h) };
            if (hole.HasValue)
                loops.Add(Box(hole.Value.X0, hole.Value.Y0, hole.Value.X1, hole.Value.Y1));
            return new RoofOutline(loops);
        }

        /// <summary>A roof with a single outer loop through the given plan points.</summary>
        public static RoofOutline Polygon(params (double X, double Y)[] outer) =>
            new RoofOutline(new[]
            {
                (IReadOnlyList<Point3>)outer.Select(p => new Point3(p.X, p.Y, 0)).ToArray()
            });
    }
}
