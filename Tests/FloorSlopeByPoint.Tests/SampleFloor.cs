using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Revit26_Plugin_Revised.FloorSlopeByPoint.V001.Core.Models;
using Revit26_Plugin_Revised.FloorSlopeByPoint.V001.Core.Services;

namespace FloorSlopeByPoint.Tests
{
    /// <summary>
    /// A rectangular floor with shape points on a regular grid, sloped the way the floor tool does it:
    /// elevation = path length to the nearest drain. Same sample floors as the design preview.
    /// </summary>
    internal sealed class SampleFloor
    {
        private const double Eps = 1e-6;

        public List<Point3> Points { get; } = new List<Point3>();
        public PathGraph Graph { get; private set; }
        public int[] GroupOf { get; private set; }
        public double[] Elevation { get; private set; }
        public bool[] IsDrain { get; private set; }
        public (double X0, double Y0, double X1, double Y1)? Hole { get; private set; }

        /// <param name="drains">Drain positions (each must lie on the grid).</param>
        /// <param name="drainGroups">Group of each drain; null = every drain is its own group.</param>
        /// <param name="hole">Opening (no points inside, no links across it).</param>
        public static SampleFloor Build(
            (double X, double Y)[] drains,
            int[] drainGroups = null,
            (double X0, double Y0, double X1, double Y1)? hole = null,
            double width = 40, double height = 24, double step = 2)
        {
            var floor = new SampleFloor { Hole = hole };
            for (double x = 0; x <= width + Eps; x += step)
                for (double y = 0; y <= height + Eps; y += step)
                    if (!floor.InHole(x, y)) floor.Points.Add(new Point3(x, y, 0));

            int n = floor.Points.Count;
            floor.Graph = new PathGraph(n);
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    double d = floor.Points[i].DistanceTo(floor.Points[j]);
                    if (d <= 2.25 * step + Eps && !floor.Crosses(floor.Points[i], floor.Points[j]))
                        floor.Graph.AddEdge(i, j, d);
                }

            var drainIdx = drains.Select(d => floor.IndexOf(d.X, d.Y)).ToList();
            var groupOfDrain = new Dictionary<int, int>();
            for (int k = 0; k < drainIdx.Count; k++)
                groupOfDrain[drainIdx[k]] = drainGroups?[k] ?? k;

            ShortestPathResult paths = ShortestPathSolver.Solve(floor.Graph, drainIdx, CancellationToken.None);
            floor.GroupOf = new int[n];
            floor.Elevation = new double[n];
            floor.IsDrain = new bool[n];
            for (int i = 0; i < n; i++)
            {
                bool reached = !double.IsInfinity(paths.Distance[i]);
                floor.GroupOf[i] = reached ? groupOfDrain[paths.SourceDrain[i]] : -1;
                floor.Elevation[i] = reached ? paths.Distance[i] : 0;
                floor.IsDrain[i] = groupOfDrain.ContainsKey(i);
            }
            return floor;
        }

        public RidgeResult Find() => RidgeFinder.Find(new RidgeInput
        {
            Positions = Points,
            Graph = Graph,
            GroupOf = GroupOf,
            Elevation = Elevation,
            IsDrain = IsDrain,
            TieTolerance = 1e-3
        }, CancellationToken.None);

        public int IndexOf(double x, double y)
        {
            int i = Points.FindIndex(p => Math.Abs(p.X - x) < Eps && Math.Abs(p.Y - y) < Eps);
            if (i < 0) throw new ArgumentException($"No grid point at ({x}, {y}).");
            return i;
        }

        public bool InHole(double x, double y) =>
            Hole is { } h && x > h.X0 + Eps && x < h.X1 - Eps && y > h.Y0 + Eps && y < h.Y1 - Eps;

        /// <summary>True when the straight link a–b passes through the opening.</summary>
        public bool Crosses(Point3 a, Point3 b)
        {
            if (Hole == null) return false;
            for (double t = 0.02; t < 1; t += 0.02)
                if (InHole(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t)) return true;
            return false;
        }
    }
}
