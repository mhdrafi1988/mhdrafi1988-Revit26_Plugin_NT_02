using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Li(p): signed inward distance from p to the line of outline edge i
    /// (outline convex, counter-clockwise, metres).
    /// </summary>
    public sealed class EdgeDistanceField
    {
        private readonly Vec2[] _origins;
        private readonly Vec2[] _normals;

        public EdgeDistanceField(Polygon2D outlineCcw)
        {
            Outline = outlineCcw ?? throw new ArgumentNullException(nameof(outlineCcw));
            int n = outlineCcw.Count;
            _origins = new Vec2[n];
            _normals = new Vec2[n];
            for (int i = 0; i < n; i++)
            {
                Segment2D e = outlineCcw.Edge(i);
                _origins[i] = e.Start;
                _normals[i] = (e.End - e.Start).PerpLeft.Normalized();
            }
        }

        public Polygon2D Outline { get; }
        public int EdgeCount => _origins.Length;

        public double Distance(int edgeIndex, Vec2 p) =>
            _normals[edgeIndex].Dot(p - _origins[edgeIndex]);

        public double MinDistance(Vec2 p)
        {
            double min = double.PositiveInfinity;
            for (int i = 0; i < _origins.Length; i++)
            {
                min = Math.Min(min, Distance(i, p));
            }

            return min;
        }

        /// <summary>{ p : Li(p) ≥ distance }.</summary>
        public HalfPlane InsetHalfPlane(int edgeIndex, double distance) =>
            new HalfPlane(_normals[edgeIndex], -_normals[edgeIndex].Dot(_origins[edgeIndex]) - distance);

        /// <summary>{ p : Lj(p) − Li(p) ≥ 0 } — the side of the bisector closer to edge i.</summary>
        public HalfPlane CloserToEdgeHalfPlane(int i, int j)
        {
            Vec2 normal = _normals[j] - _normals[i];
            double offset = -_normals[j].Dot(_origins[j]) + _normals[i].Dot(_origins[i]);
            return new HalfPlane(normal, offset);
        }

        public IEnumerable<HalfPlane> InsetHalfPlanes(double distance)
        {
            for (int i = 0; i < _origins.Length; i++)
            {
                yield return InsetHalfPlane(i, distance);
            }
        }
    }
}
