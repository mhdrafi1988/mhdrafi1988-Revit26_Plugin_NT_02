// IslandRoofSplitter V002 — vertical "ray-cast" onto the source roof's top surface, done in Core against the
// triangulated top faces (point-in-triangle + barycentric Z). Also exposes the roof's creases (ridges/valleys/
// hips = mesh edges shared by two different faces) for the ridge & valley points option.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin_Revised.IslandRoofSplitter.V002.Core.Services
{
    public sealed class SurfaceZSampler
    {
        private readonly List<MeshTriangle> _tris;
        private readonly Dictionary<(long, long), List<int>> _grid = new();
        private readonly double _cell;
        private readonly double _edgeTolFt;

        public List<(Pt3 A, Pt3 B)> Creases { get; } = new();
        public List<Pt3> CreaseVertices { get; } = new();
        public bool IsEmpty => _tris.Count == 0;

        /// <param name="edgeTolFt">Points this close outside a triangle still count as on the roof (edge snapping).</param>
        public SurfaceZSampler(RoofSurfaceMesh mesh, double edgeTolFt)
        {
            _tris = mesh.Triangles;
            _edgeTolFt = edgeTolFt;
            if (_tris.Count == 0) { _cell = 1; return; }

            double minX = _tris.Min(t => Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)));
            double maxX = _tris.Max(t => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)));
            double minY = _tris.Min(t => Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)));
            double maxY = _tris.Max(t => Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)));
            double diag = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
            _cell = Math.Max(diag / Math.Sqrt(_tris.Count), 0.5);

            for (int i = 0; i < _tris.Count; i++)
            {
                var t = _tris[i];
                long x0 = C(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) - edgeTolFt), x1 = C(Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) + edgeTolFt);
                long y0 = C(Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) - edgeTolFt), y1 = C(Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) + edgeTolFt);
                for (long x = x0; x <= x1; x++)
                for (long y = y0; y <= y1; y++)
                {
                    if (!_grid.TryGetValue((x, y), out var l)) _grid[(x, y)] = l = new List<int>();
                    l.Add(i);
                }
            }
            BuildCreases();
        }

        private long C(double v) => (long)Math.Floor(v / _cell);

        /// <summary>Top-surface Z at plan point p, or null when p is not over the roof.</summary>
        public double? Sample(Pt2 p)
        {
            if (!_grid.TryGetValue((C(p.X), C(p.Y)), out var cand)) return null;
            double? best = null;
            double nearestD = double.MaxValue;
            double? nearestZ = null;
            foreach (int i in cand)
            {
                var t = _tris[i];
                if (!Barycentric(p, t, out double u, out double v, out double w)) continue;
                const double eps = 1e-9;
                if (u >= -eps && v >= -eps && w >= -eps)
                {
                    double z = u * t.A.Z + v * t.B.Z + w * t.C.Z;
                    best = best.HasValue ? Math.Max(best.Value, z) : z;
                }
                else if (!best.HasValue)
                {
                    // near-miss: within edge tolerance of this triangle → clamp and interpolate
                    double d = Math.Min(Geometry2D.DistancePointSegment(p, t.A.XY, t.B.XY),
                               Math.Min(Geometry2D.DistancePointSegment(p, t.B.XY, t.C.XY),
                                        Geometry2D.DistancePointSegment(p, t.C.XY, t.A.XY)));
                    if (d <= _edgeTolFt && d < nearestD)
                    {
                        u = Math.Max(u, 0); v = Math.Max(v, 0); w = Math.Max(w, 0);
                        double s = u + v + w;
                        nearestD = d;
                        nearestZ = (u * t.A.Z + v * t.B.Z + w * t.C.Z) / s;
                    }
                }
            }
            return best ?? nearestZ;
        }

        private static bool Barycentric(Pt2 p, MeshTriangle t, out double u, out double v, out double w)
        {
            var a = t.A.XY; var b = t.B.XY; var c = t.C.XY;
            double det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            u = v = w = 0;
            if (Math.Abs(det) < 1e-14) return false; // vertical / sliver triangle
            u = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / det;
            v = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / det;
            w = 1 - u - v;
            return true;
        }

        private void BuildCreases()
        {
            static (long, long, long) K(Pt3 p) => ((long)Math.Round(p.X * 1e5), (long)Math.Round(p.Y * 1e5), (long)Math.Round(p.Z * 1e5));
            var faces = new Dictionary<((long, long, long), (long, long, long)), (Pt3 a, Pt3 b, HashSet<int> f)>();
            foreach (var t in _tris)
            foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
            {
                var ka = K(a); var kb = K(b);
                var key = ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka);
                if (!faces.TryGetValue(key, out var entry)) faces[key] = entry = (a, b, new HashSet<int>());
                entry.f.Add(t.FaceIndex);
            }
            var verts = new Dictionary<(long, long, long), Pt3>();
            foreach (var (a, b, f) in faces.Values)
            {
                if (f.Count < 2) continue;
                Creases.Add((a, b));
                verts[K(a)] = a;
                verts[K(b)] = b;
            }
            CreaseVertices.AddRange(verts.Values);
        }
    }

    /// <summary>Ridge/valley support for Sloped mode: crease crossings on the boundary + crease vertices inside.</summary>
    public static class InteriorPointSampler
    {
        /// <summary>Inserts a vertex wherever a roof crease crosses a loop edge (straight loops only).</summary>
        public static List<LoopVertex> InsertCreaseCrossings(List<LoopVertex> loop, SurfaceZSampler zs, double minSpacingFt)
        {
            if (zs.Creases.Count == 0) return loop;
            var result = new List<LoopVertex>();
            int n = loop.Count;
            for (int i = 0; i < n; i++)
            {
                var a = loop[i].P;
                var b = loop[(i + 1) % n].P;
                result.Add(loop[i]);
                var hits = new List<(double t, Pt2 p)>();
                foreach (var (ca, cb) in zs.Creases)
                {
                    if (!Geometry2D.SegmentIntersection(a, b, ca.XY, cb.XY, 0, 0, out double t, out _)) continue;
                    var p = a + (b - a) * t;
                    if (p.DistanceTo(a) < minSpacingFt || p.DistanceTo(b) < minSpacingFt) continue;
                    if (hits.Any(h => h.p.DistanceTo(p) < minSpacingFt)) continue;
                    hits.Add((t, p));
                }
                foreach (var h in hits.OrderBy(h => h.t))
                    result.Add(new LoopVertex(h.p, -1, loop[i].SourceId));
            }
            return result;
        }

        /// <summary>Crease vertices strictly inside the island (outside its openings), at least minDist from any boundary.</summary>
        public static List<Pt3> InsidePoints(IReadOnlyList<Pt2> outer, IReadOnlyList<IReadOnlyList<Pt2>> holes,
                                             SurfaceZSampler zs, double minDistFt)
        {
            var list = new List<Pt3>();
            foreach (var p in zs.CreaseVertices)
            {
                var xy = p.XY;
                if (!Geometry2D.PointInPolygon(xy, outer)) continue;
                if (Geometry2D.DistanceToLoop(xy, outer) < minDistFt) continue;
                bool inHole = false;
                foreach (var h in holes)
                    if (Geometry2D.PointInPolygon(xy, h) || Geometry2D.DistanceToLoop(xy, h) < minDistFt) { inHole = true; break; }
                if (inHole) continue;
                double z = zs.Sample(xy) ?? p.Z;
                list.Add(new Pt3(p.X, p.Y, z));
            }
            return list;
        }
    }
}
