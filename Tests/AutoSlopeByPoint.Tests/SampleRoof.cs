using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services;

namespace AutoSlopeByPoint.Tests
{
    /// <summary>
    /// A rectangular roof with shape points on a regular grid, sloped the way V033 does it:
    /// elevation = path length to the nearest drain. Same sample roofs as the design preview.
    /// </summary>
    internal sealed class SampleRoof
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
        public static SampleRoof Build(
            (double X, double Y)[] drains,
            int[] drainGroups = null,
            (double X0, double Y0, double X1, double Y1)? hole = null,
            double width = 40, double height = 24, double step = 2)
        {
            var roof = new SampleRoof { Hole = hole };
            for (double x = 0; x <= width + Eps; x += step)
                for (double y = 0; y <= height + Eps; y += step)
                    if (!roof.InHole(x, y)) roof.Points.Add(new Point3(x, y, 0));

            int n = roof.Points.Count;
            roof.Graph = new PathGraph(n);
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    double d = roof.Points[i].DistanceTo(roof.Points[j]);
                    if (d <= 2.25 * step + Eps && !roof.Crosses(roof.Points[i], roof.Points[j]))
                        roof.Graph.AddEdge(i, j, d);
                }

            var drainIdx = drains.Select(d => roof.IndexOf(d.X, d.Y)).ToList();
            var groupOfDrain = new Dictionary<int, int>();
            for (int k = 0; k < drainIdx.Count; k++)
                groupOfDrain[drainIdx[k]] = drainGroups?[k] ?? k;

            ShortestPathResult paths = ShortestPathSolver.Solve(roof.Graph, drainIdx, CancellationToken.None);
            roof.GroupOf = new int[n];
            roof.Elevation = new double[n];
            roof.IsDrain = new bool[n];
            for (int i = 0; i < n; i++)
            {
                bool reached = !double.IsInfinity(paths.Distance[i]);
                roof.GroupOf[i] = reached ? groupOfDrain[paths.SourceDrain[i]] : -1;
                roof.Elevation[i] = reached ? paths.Distance[i] : 0;
                roof.IsDrain[i] = groupOfDrain.ContainsKey(i);
            }
            return roof;
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
