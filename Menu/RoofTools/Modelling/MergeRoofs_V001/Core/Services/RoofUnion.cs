// Merge Roofs V001 — thin Clipper2 wrapper: footprints -> integer regions, union, closing, loop extraction.
using System;
using System.Collections.Generic;
using Clipper2Lib;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models;

namespace Revit26_Plugin_Revised.MergeRoofs.V001.Core.Services
{
    /// <summary>Polygon operations on footprints (Clipper2, 1e-6 ft integer grid).</summary>
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

        /// <summary>Union of regions (non-zero fill).</summary>
        public static Paths64 Union(IEnumerable<Paths64> regions)
        {
            var all = new Paths64();
            foreach (var r in regions) all.AddRange(r);
            return Clipper.Union(all, FillRule.NonZero);
        }

        /// <summary>Morphological closing: grows by half the tolerance then shrinks it, closing gaps up to the tolerance.</summary>
        public static Paths64 Close(Paths64 region, double toleranceFeet)
        {
            double d = toleranceFeet * Scale / 2.0 + 1.0;
            var grown = Clipper.InflatePaths(region, d, JoinType.Miter, EndType.Polygon, 2.0);
            grown = Clipper.Union(grown, FillRule.NonZero);
            return Clipper.InflatePaths(grown, -d, JoinType.Miter, EndType.Polygon, 2.0);
        }

        /// <summary>Splits a region into outer loops and their holes (hole polygons nested deeper than one level become outers).</summary>
        public static (List<List<Pt>> Outers, List<List<Pt>> Holes) Split(Paths64 region)
        {
            var tree = new PolyTree64();
            var c = new Clipper64();
            c.AddSubject(region);
            c.Execute(ClipType.Union, FillRule.NonZero, tree);
            var outers = new List<List<Pt>>();
            var holes = new List<List<Pt>>();
            void Walk(PolyPath64 node, bool hole)
            {
                for (int i = 0; i < node.Count; i++)
                {
                    var child = node[i];
                    (hole ? holes : outers).Add(FromPath(child.Polygon!));
                    Walk(child, !hole);
                }
            }
            Walk(tree, false);
            return (outers, holes);
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
