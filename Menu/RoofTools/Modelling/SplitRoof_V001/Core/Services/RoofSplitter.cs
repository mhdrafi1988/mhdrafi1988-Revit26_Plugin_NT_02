// Split Roof V001 — divides a footprint by straight cut lines (extended to half-planes) using Clipper2.
using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using Revit26_Plugin_Revised.SplitRoof.V001.Core.Models;

namespace Revit26_Plugin_Revised.SplitRoof.V001.Core.Services
{
    /// <summary>Splits a roof footprint into pieces along cut lines.</summary>
    public static class RoofSplitter
    {
        /// <summary>Shortest edge Revit accepts, in feet (about 1/32 inch).</summary>
        public const double ShortCurve = 0.00256;
        /// <summary>Pieces smaller than this plan area (sq ft, about 1 cm²) are dropped as slivers.</summary>
        public const double MinArea = 0.001;

        /// <summary>
        /// Cuts <paramref name="roof"/> along every line, one after another. Each line is treated as infinite.
        /// Lines that do not cross any piece are reported in the notes. Slopes are copied from the source edges and arcs
        /// are re-fitted where a source arc survives.
        /// </summary>
        public static SplitResult Split(RoofFootprint roof, IReadOnlyList<CutLine> lines)
        {
            var notes = new List<string>();
            var regions = new List<Paths64> { RoofUnion.ToRegion(roof) };
            double reach = Reach(roof);

            foreach (var line in lines)
            {
                if (Length(line.A, line.B) < ShortCurve) { notes.Add($"Cut line {line.Id} is too short or vertical in plan and was ignored."); continue; }
                var next = new List<Paths64>();
                bool crossed = false;
                foreach (var region in regions)
                {
                    var left = Clipper.Intersect(region, HalfPlane(line, reach, +1), FillRule.NonZero);
                    var right = Clipper.Intersect(region, HalfPlane(line, reach, -1), FillRule.NonZero);
                    if (Area(left) > MinArea && Area(right) > MinArea) { next.Add(left); next.Add(right); crossed = true; }
                    else next.Add(region);
                }
                if (!crossed) notes.Add($"Cut line {line.Id} does not cross the roof and was ignored.");
                regions = next;
            }

            var pieces = new List<SplitPiece>();
            foreach (var region in regions)
                foreach (var piece in ToPieces(region, roof, notes))
                    pieces.Add(piece);

            pieces = pieces.OrderBy(p => Centroid(p).X).ThenBy(p => Centroid(p).Y).ToList();
            foreach (var p in pieces.Where(p => !p.HasSlope))
                notes.Add($"Piece {pieces.IndexOf(p) + 1} has no sloped edge and will be created flat.");
            return new SplitResult { Pieces = pieces, Notes = notes };
        }

        /// <summary>Straightens loops built from flattened points back into edges (no slope, no arcs).</summary>
        private static Loop ToLoop(List<Pt> ring)
        {
            var pts = new List<Pt>();
            foreach (var p in ring)
                if (pts.Count == 0 || Length(pts[^1], p) >= ShortCurve) pts.Add(p);
            while (pts.Count > 1 && Length(pts[^1], pts[0]) < ShortCurve) pts.RemoveAt(pts.Count - 1);
            var edges = new List<Edge>();
            for (int i = 0; i < pts.Count; i++) edges.Add(new Edge { A = pts[i], B = pts[(i + 1) % pts.Count] });
            return new Loop(edges);
        }

        private static IEnumerable<SplitPiece> ToPieces(Paths64 region, RoofFootprint roof, List<string> notes)
        {
            var tree = new PolyTree64();
            var c = new Clipper64();
            c.AddSubject(region);
            c.Execute(ClipType.Union, FillRule.NonZero, tree);
            var sources = new List<RoofFootprint> { roof };

            var outers = new List<PolyPath64>();
            void Walk(PolyPath64 node, bool hole)
            {
                for (int i = 0; i < node.Count; i++)
                {
                    if (!hole) outers.Add(node[i]);
                    Walk(node[i], !hole);
                }
            }
            Walk(tree, false);

            foreach (var o in outers)
            {
                double area = Clipper.Area(o.Polygon!) / (RoofUnion.Scale * RoofUnion.Scale);
                if (Math.Abs(area) < MinArea) { notes.Add("A sliver smaller than 1 cm² was dropped."); continue; }
                var outer = Refit(ToLoop(RoofUnion.FromPath(o.Polygon!)), sources);
                var holes = new List<Loop>();
                double holeArea = 0;
                for (int i = 0; i < o.Count; i++)
                {
                    holes.Add(Refit(ToLoop(RoofUnion.FromPath(o[i].Polygon!)), sources));
                    holeArea += Math.Abs(Clipper.Area(o[i].Polygon!)) / (RoofUnion.Scale * RoofUnion.Scale);
                }
                if (outer.Edges.Count < 3) continue;
                yield return new SplitPiece
                {
                    Outer = outer, Holes = holes,
                    AreaFeet = Math.Abs(area) - holeArea,
                    HasSlope = outer.Edges.Concat(holes.SelectMany(h => h.Edges)).Any(e => e.DefinesSlope),
                };
            }
        }

        private static Loop Refit(Loop flat, IReadOnlyList<RoofFootprint> sources)
        {
            var slopes = SlopeMapper.Apply(flat.Edges, sources, 0.001);
            return ArcRefitter.Refit(new Loop(slopes), sources, 0.001);
        }

        /// <summary>Half-plane (huge rectangle) on one side of the line; side +1 is the left of A→B.</summary>
        private static Paths64 HalfPlane(CutLine l, double reach, int side)
        {
            double dx = l.B.X - l.A.X, dy = l.B.Y - l.A.Y, len = Math.Sqrt(dx * dx + dy * dy);
            double ux = dx / len, uy = dy / len, nx = -uy * side, ny = ux * side;
            Pt P(double along, double across) => new(l.A.X + ux * along + nx * across, l.A.Y + uy * along + ny * across);
            return new Paths64 { RoofUnion.ToPath(new[] { P(-reach, 0), P(reach, 0), P(reach, reach), P(-reach, reach) }) };
        }

        private static double Reach(RoofFootprint roof)
        {
            var ring = ArcGeometry.Ring(roof.Outer);
            double minX = ring.Min(p => p.X), maxX = ring.Max(p => p.X), minY = ring.Min(p => p.Y), maxY = ring.Max(p => p.Y);
            return (maxX - minX + maxY - minY) * 4 + 1000;
        }

        private static double Area(Paths64 r) => Math.Abs(RoofUnion.AreaFeet(r));
        private static double Length(Pt a, Pt b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static Pt Centroid(SplitPiece p)
        {
            var r = ArcGeometry.Ring(p.Outer);
            return new Pt(r.Average(q => q.X), r.Average(q => q.Y));
        }
    }
}
