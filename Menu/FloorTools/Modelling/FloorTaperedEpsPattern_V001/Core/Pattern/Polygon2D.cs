using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Immutable simple polygon in plan (closed implicitly), metres.</summary>
    public sealed class Polygon2D
    {
        private readonly Vec2[] _vertices;

        public Polygon2D(IEnumerable<Vec2> vertices)
        {
            _vertices = (vertices ?? throw new ArgumentNullException(nameof(vertices))).ToArray();
        }

        public static Polygon2D Empty { get; } = new Polygon2D(Array.Empty<Vec2>());

        public IReadOnlyList<Vec2> Vertices => _vertices;
        public int Count => _vertices.Length;
        public bool IsEmpty => _vertices.Length < 3;

        /// <summary>Shoelace area, positive when counter-clockwise.</summary>
        public double SignedArea
        {
            get
            {
                if (_vertices.Length < 3)
                {
                    return 0;
                }

                double sum = 0;
                for (int i = 0; i < _vertices.Length; i++)
                {
                    Vec2 a = _vertices[i];
                    Vec2 b = _vertices[(i + 1) % _vertices.Length];
                    sum += a.Cross(b);
                }

                return sum * 0.5;
            }
        }

        public double Area => Math.Abs(SignedArea);
        public bool IsCounterClockwise => SignedArea > 0;

        public double Perimeter => Edges.Sum(e => e.Length);

        public Segment2D Edge(int index) =>
            new Segment2D(_vertices[index], _vertices[(index + 1) % _vertices.Length]);

        public IEnumerable<Segment2D> Edges
        {
            get
            {
                for (int i = 0; i < _vertices.Length; i++)
                {
                    yield return Edge(i);
                }
            }
        }

        /// <summary>Area centroid (falls back to vertex average for degenerate loops).</summary>
        public Vec2 Centroid
        {
            get
            {
                if (_vertices.Length == 0)
                {
                    return Vec2.Zero;
                }

                double a = SignedArea;
                if (Math.Abs(a) < 1e-12)
                {
                    return _vertices.Aggregate(Vec2.Zero, (s, v) => s + v) / _vertices.Length;
                }

                double cx = 0, cy = 0;
                for (int i = 0; i < _vertices.Length; i++)
                {
                    Vec2 p = _vertices[i];
                    Vec2 q = _vertices[(i + 1) % _vertices.Length];
                    double f = p.Cross(q);
                    cx += (p.X + q.X) * f;
                    cy += (p.Y + q.Y) * f;
                }

                return new Vec2(cx / (6 * a), cy / (6 * a));
            }
        }

        public Polygon2D ToCounterClockwise() =>
            SignedArea < 0 ? new Polygon2D(_vertices.Reverse()) : this;

        /// <summary>
        /// Removes repeated vertices and vertices lying on the line through their
        /// neighbours (within <paramref name="tolerance"/>), i.e. merges collinear edges.
        /// </summary>
        public Polygon2D Cleaned(double tolerance)
        {
            var list = new List<Vec2>(_vertices);
            bool changed = true;
            while (changed && list.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < list.Count && list.Count >= 3; i++)
                {
                    Vec2 prev = list[(i - 1 + list.Count) % list.Count];
                    Vec2 cur = list[i];
                    Vec2 next = list[(i + 1) % list.Count];

                    // Duplicate vertex, collinear pass-through vertex, or a zero-width spike.
                    bool duplicate = cur.AlmostEquals(next, tolerance);
                    bool onLine = new Segment2D(prev, next).DistanceToLine(cur) <= tolerance;

                    if (duplicate || onLine)
                    {
                        list.RemoveAt(i);
                        changed = true;
                        i--;
                    }
                }
            }

            return list.Count >= 3 ? new Polygon2D(list) : Empty;
        }

        /// <summary>True when every turn is to the left (polygon assumed CCW).</summary>
        public bool IsConvex(double tolerance)
        {
            if (_vertices.Length < 3)
            {
                return false;
            }

            double turning = 0;
            for (int i = 0; i < _vertices.Length; i++)
            {
                Vec2 a = _vertices[i];
                Vec2 b = _vertices[(i + 1) % _vertices.Length];
                Vec2 c = _vertices[(i + 2) % _vertices.Length];
                Vec2 e1 = (b - a).Normalized();
                Vec2 e2 = (c - b).Normalized();
                double cross = e1.Cross(e2);
                if (cross < -tolerance)
                {
                    return false;
                }

                turning += Math.Atan2(cross, e1.Dot(e2));
            }

            // A self-overlapping "star" also turns left everywhere but winds twice.
            return Math.Abs(turning - 2 * Math.PI) < 1e-6;
        }

        /// <summary>Point-in-convex-CCW-polygon test with outward tolerance.</summary>
        public bool ContainsConvex(Vec2 point, double tolerance)
        {
            if (IsEmpty)
            {
                return false;
            }

            foreach (Segment2D e in Edges)
            {
                Vec2 d = e.End - e.Start;
                double len = d.Length;
                if (len < 1e-15)
                {
                    continue;
                }

                double signedDist = d.Cross(point - e.Start) / len;
                if (signedDist < -tolerance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True when the segment lies on one of this polygon's edge lines (within tolerance).</summary>
        public bool IsOnBoundaryLine(Segment2D segment, double tolerance)
        {
            foreach (Segment2D e in Edges)
            {
                if (e.DistanceTo(segment.Start) <= tolerance &&
                    e.DistanceTo(segment.End) <= tolerance)
                {
                    return true;
                }
            }

            return false;
        }

        public (Vec2 Min, Vec2 Max) BoundingBox()
        {
            if (_vertices.Length == 0)
            {
                return (Vec2.Zero, Vec2.Zero);
            }

            return (new Vec2(_vertices.Min(v => v.X), _vertices.Min(v => v.Y)),
                    new Vec2(_vertices.Max(v => v.X), _vertices.Max(v => v.Y)));
        }
    }
}
