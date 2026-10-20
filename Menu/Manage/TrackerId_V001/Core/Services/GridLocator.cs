using Revit26_Plugin_Revised.TrackerId.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Services
{
    /// <summary>Result of a grid lookup: the label to put in the ID and how far the point is from it.</summary>
    public sealed class GridHit
    {
        /// <summary>Creates the result.</summary>
        public GridHit(string label, double distanceFeet)
        {
            Label = label ?? string.Empty;
            DistanceFeet = distanceFeet;
        }

        /// <summary>"C04" for a crossing, "C" or "04" for a single grid.</summary>
        public string Label { get; }

        /// <summary>Distance from the point to the crossing or grid line, feet.</summary>
        public double DistanceFeet { get; }

        /// <summary>Distance in metres.</summary>
        public double DistanceMetres => DistanceFeet * FeetToMetres;

        /// <summary>Feet to metres.</summary>
        public const double FeetToMetres = 0.3048;
    }

    /// <summary>
    /// Finds the grid crossing or grid line nearest to a plan point. Crossings are worked out once, for
    /// every pair of straight grids that are not parallel, so skewed grids work too. Curved grids take
    /// part in the single-grid lookups only. Grids are treated as unbounded lines, so an element past
    /// the end of a grid still finds it.
    /// </summary>
    public sealed class GridLocator
    {
        // Grids closer than ~5° to parallel never cross in a useful place.
        private const double MinSinAngle = 0.087;

        private readonly List<GridLine2D> _grids;
        private readonly List<(double X, double Y, string Label)> _crossings = new();

        /// <summary>Prepares the lookup for <paramref name="grids"/>.</summary>
        public GridLocator(IEnumerable<GridLine2D> grids)
        {
            _grids = (grids ?? Enumerable.Empty<GridLine2D>())
                .Where(g => g != null && !string.IsNullOrWhiteSpace(g.Name) && IsUsable(g))
                .ToList();

            var straight = _grids.Where(g => !g.IsCurved).ToList();
            for (int i = 0; i < straight.Count; i++)
            {
                for (int j = i + 1; j < straight.Count; j++)
                {
                    if (string.Equals(straight[i].Name, straight[j].Name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (TryIntersect(straight[i], straight[j], out double x, out double y))
                        _crossings.Add((x, y, CrossingLabel(straight[i].Name, straight[j].Name)));
                }
            }
        }

        /// <summary>Number of grids taking part.</summary>
        public int GridCount => _grids.Count;

        /// <summary>
        /// The grid label for the plan point (x, y) under <paramref name="mode"/>, or null when the model
        /// has no usable grids. A crossing lookup with no crossings falls back to the nearest grid line,
        /// and a letter/number lookup with no grid of that kind falls back to any grid.
        /// </summary>
        public GridHit Find(double x, double y, SegmentSource mode)
        {
            if (_grids.Count == 0) return null;

            if (mode == SegmentSource.GridCrossing && _crossings.Count > 0)
            {
                var best = _crossings[0];
                double bestD = double.MaxValue;
                foreach (var c in _crossings)
                {
                    double d = (c.X - x) * (c.X - x) + (c.Y - y) * (c.Y - y);
                    if (d < bestD) { bestD = d; best = c; }
                }
                return new GridHit(best.Label, Math.Sqrt(bestD));
            }

            IEnumerable<GridLine2D> pool = _grids;
            if (mode == SegmentSource.GridLetter && _grids.Any(StartsWithLetter)) pool = _grids.Where(StartsWithLetter);
            if (mode == SegmentSource.GridNumber && _grids.Any(StartsWithDigit)) pool = _grids.Where(StartsWithDigit);

            GridLine2D nearest = null;
            double nearestD = double.MaxValue;
            foreach (var g in pool)
            {
                double d = Distance(g, x, y);
                if (d < nearestD) { nearestD = d; nearest = g; }
            }
            return nearest == null ? null : new GridHit(NameRules.PadGridNumber(nearest.Name), nearestD);
        }

        private static bool StartsWithLetter(GridLine2D g) => char.IsLetter(g.Name.Trim()[0]);

        private static bool StartsWithDigit(GridLine2D g) => char.IsDigit(g.Name.Trim()[0]);

        private static bool IsUsable(GridLine2D g) =>
            g.IsCurved ? g.Radius > 1e-9 : Math.Abs(g.X2 - g.X1) + Math.Abs(g.Y2 - g.Y1) > 1e-9;

        // Letter grid first, number grid (padded) second: "C" + "4" gives "C04", whatever the pick order.
        private static string CrossingLabel(string a, string b)
        {
            a = a.Trim();
            b = b.Trim();
            bool aLetter = char.IsLetter(a[0]), bLetter = char.IsLetter(b[0]);
            if (aLetter == bLetter && string.Compare(a, b, StringComparison.OrdinalIgnoreCase) > 0) (a, b) = (b, a);
            else if (!aLetter && bLetter) (a, b) = (b, a);
            return NameRules.PadGridNumber(a) + NameRules.PadGridNumber(b);
        }

        private static double Distance(GridLine2D g, double x, double y)
        {
            if (g.IsCurved)
                return Math.Abs(Math.Sqrt((x - g.CenterX) * (x - g.CenterX) + (y - g.CenterY) * (y - g.CenterY)) - g.Radius);

            double dx = g.X2 - g.X1, dy = g.Y2 - g.Y1;
            double len = Math.Sqrt(dx * dx + dy * dy);
            return Math.Abs((x - g.X1) * dy - (y - g.Y1) * dx) / len;
        }

        private static bool TryIntersect(GridLine2D a, GridLine2D b, out double x, out double y)
        {
            x = y = 0;
            double ax = a.X2 - a.X1, ay = a.Y2 - a.Y1;
            double bx = b.X2 - b.X1, by = b.Y2 - b.Y1;
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
            double cross = ax * by - ay * bx;
            if (Math.Abs(cross) / (la * lb) < MinSinAngle) return false;

            double t = ((b.X1 - a.X1) * by - (b.Y1 - a.Y1) * bx) / cross;
            x = a.X1 + t * ax;
            y = a.Y1 + t * ay;
            return true;
        }
    }
}
