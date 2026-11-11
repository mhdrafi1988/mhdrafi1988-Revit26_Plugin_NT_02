// =======================================================
// File: SpatialGrid2D.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services
// New in V032 (V030 P1/P2). Buckets points by XY cell so neighbour queries
// only look at nearby cells instead of every point. With a cell larger
// than the roof it degrades to "check everything", i.e. V030's behaviour.
// =======================================================

using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services
{
    public sealed class SpatialGrid2D
    {
        private readonly IReadOnlyList<Point3> _points;
        private readonly double _cell;
        private readonly Dictionary<(long, long), List<int>> _cells = new Dictionary<(long, long), List<int>>();

        public SpatialGrid2D(IReadOnlyList<Point3> points, double cellSize)
        {
            _points = points ?? throw new ArgumentNullException(nameof(points));
            _cell = cellSize > 1e-6 ? cellSize : 1.0;

            for (int i = 0; i < _points.Count; i++)
            {
                var key = KeyOf(_points[i]);
                if (!_cells.TryGetValue(key, out var list))
                    _cells[key] = list = new List<int>();
                list.Add(i);
            }
        }

        private (long, long) KeyOf(Point3 p) =>
            ((long)Math.Floor(p.X / _cell), (long)Math.Floor(p.Y / _cell));

        /// <summary>Indices whose XY distance to <paramref name="p"/> is ≤ radius.</summary>
        public List<int> QueryXY(Point3 p, double radius)
        {
            var found = new List<int>();
            if (radius < 0) return found;

            long span = (long)Math.Ceiling(radius / _cell);
            // Guard against pathological spans (radius far larger than the cell):
            // fall back to a full scan, which is what V030 always did.
            if (span > 64)
            {
                for (int i = 0; i < _points.Count; i++)
                    if (_points[i].DistanceXY(p) <= radius) found.Add(i);
                return found;
            }

            var (cx, cy) = KeyOf(p);
            for (long gx = cx - span; gx <= cx + span; gx++)
            {
                for (long gy = cy - span; gy <= cy + span; gy++)
                {
                    if (!_cells.TryGetValue((gx, gy), out var list)) continue;
                    foreach (int i in list)
                        if (_points[i].DistanceXY(p) <= radius) found.Add(i);
                }
            }
            return found;
        }

        /// <summary>Index of the XY-nearest point, or -1 when empty.</summary>
        public int NearestXY(Point3 p, out double distance)
        {
            int best = -1;
            distance = double.MaxValue;
            for (int i = 0; i < _points.Count; i++)
            {
                double d = _points[i].DistanceXY(p);
                if (d < distance) { distance = d; best = i; }
            }
            return best;
        }
    }
}
