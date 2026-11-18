// Split Roof V001 — thin Clipper2 wrapper: footprints -> integer regions, area and path conversion.
// Same grid as Merge Roofs V001's RoofUnion, kept per tool so the two tools stay independent.
using System;
using System.Collections.Generic;
using Clipper2Lib;
using Revit26_Plugin_Revised.SplitRoof.V001.Core.Models;

namespace Revit26_Plugin_Revised.SplitRoof.V001.Core.Services
{
    /// <summary>Polygon conversions for footprints (Clipper2, 1e-6 ft integer grid).</summary>
    public static class RoofUnion
    {
        /// <summary>Feet-to-integer scale.</summary>
        public const double Scale = 1e6;

        /// <summary>One footprint as a region: outer counter-clockwise, holes clockwise.</summary>
        public static Paths64 ToRegion(RoofFootprint roof)
        {
            var region = new Paths64 { Oriented(ToPath(ArcGeometry.Ring(roof.Outer)), true) };
            foreach (var h in roof.Holes) region.Add(Oriented(ToPath(ArcGeometry.Ring(h)), false));
            return region;
        }

        /// <summary>Signed area in square feet.</summary>
        public static double AreaFeet(Paths64 region) => Clipper.Area(region) / (Scale * Scale);

        /// <summary>Path from points in feet.</summary>
        public static Path64 ToPath(IEnumerable<Pt> pts)
        {
            var p = new Path64();
            foreach (var q in pts) p.Add(new Point64((long)Math.Round(q.X * Scale), (long)Math.Round(q.Y * Scale)));
            return p;
        }

        /// <summary>Points in feet from a path.</summary>
        public static List<Pt> FromPath(Path64 path)
        {
            var l = new List<Pt>(path.Count);
            foreach (var p in path) l.Add(new Pt(p.X / Scale, p.Y / Scale));
            return l;
        }

        private static Path64 Oriented(Path64 p, bool ccw)
        {
            bool isCcw = Clipper.Area(p) > 0;
            if (isCcw != ccw) p.Reverse();
            return p;
        }
    }
}
