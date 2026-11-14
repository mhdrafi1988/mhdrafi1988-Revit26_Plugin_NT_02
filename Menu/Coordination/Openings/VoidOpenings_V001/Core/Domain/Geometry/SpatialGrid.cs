using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry
{
    /// <summary>
    /// Uniform grid over a set of boxes: the broad phase of clash detection. A query returns the
    /// indices of the boxes that touch the query box, without testing every box in the model.
    /// </summary>
    public sealed class SpatialGrid
    {
        // A box covering more cells than this (a whole-building slab, say) is kept in one list that
        // every query checks, instead of being written into thousands of cells.
        private const int MaxCellsPerBox = 512;

        private readonly IReadOnlyList<Aabb> _boxes;
        private readonly double _cellSize;
        private readonly Dictionary<(int, int, int), List<int>> _cells = new Dictionary<(int, int, int), List<int>>();
        private readonly List<int> _large = new List<int>();

        /// <summary>Builds the grid.</summary>
        /// <param name="boxes">The boxes to index; a query returns positions in this list.</param>
        /// <param name="cellSize">Edge length of a grid cell, in the same unit as the boxes.</param>
        public SpatialGrid(IReadOnlyList<Aabb> boxes, double cellSize)
        {
            _boxes = boxes ?? throw new ArgumentNullException(nameof(boxes));
            if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
            _cellSize = cellSize;

            for (int i = 0; i < boxes.Count; i++)
            {
                CellRange range = RangeOf(boxes[i]);
                if (range.Count > MaxCellsPerBox)
                {
                    _large.Add(i);
                    continue;
                }
                for (int x = range.X0; x <= range.X1; x++)
                    for (int y = range.Y0; y <= range.Y1; y++)
                        for (int z = range.Z0; z <= range.Z1; z++)
                        {
                            if (!_cells.TryGetValue((x, y, z), out List<int> cell))
                                _cells[(x, y, z)] = cell = new List<int>();
                            cell.Add(i);
                        }
            }
        }

        /// <summary>Indices of the boxes that overlap or touch <paramref name="query"/>, each once.</summary>
        public List<int> Query(Aabb query)
        {
            var result = new List<int>();
            foreach (int i in _large)
                if (_boxes[i].Intersects(query))
                    result.Add(i);

            CellRange range = RangeOf(query);
            if (range.Count > MaxCellsPerBox * 8)
            {
                // Huge query: walking the cells would cost more than checking the boxes directly.
                var large = new HashSet<int>(_large);
                for (int i = 0; i < _boxes.Count; i++)
                    if (!large.Contains(i) && _boxes[i].Intersects(query))
                        result.Add(i);
                return result;
            }

            var seen = new HashSet<int>();
            for (int x = range.X0; x <= range.X1; x++)
                for (int y = range.Y0; y <= range.Y1; y++)
                    for (int z = range.Z0; z <= range.Z1; z++)
                    {
                        if (!_cells.TryGetValue((x, y, z), out List<int> cell))
                            continue;
                        foreach (int i in cell)
                            if (seen.Add(i) && _boxes[i].Intersects(query))
                                result.Add(i);
                    }
            return result;
        }

        private CellRange RangeOf(Aabb box) => new CellRange(
            Cell(box.Min.X), Cell(box.Max.X), Cell(box.Min.Y), Cell(box.Max.Y), Cell(box.Min.Z), Cell(box.Max.Z));

        private int Cell(double value)
        {
            double cell = Math.Floor(value / _cellSize);
            return (int)Math.Max(int.MinValue / 2, Math.Min(int.MaxValue / 2, cell));
        }

        private readonly struct CellRange
        {
            public readonly int X0, X1, Y0, Y1, Z0, Z1;

            public CellRange(int x0, int x1, int y0, int y1, int z0, int z1)
            {
                X0 = x0; X1 = x1; Y0 = y0; Y1 = y1; Z0 = z0; Z1 = z1;
            }

            public long Count => (long)(X1 - X0 + 1) * (Y1 - Y0 + 1) * (Z1 - Z0 + 1);
        }
    }
}
