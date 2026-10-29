// =======================================================
// File: GeodesicField.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services
// New in V033 (true ridge position). Answers "how far is this roof point
// from each drain group?" where the distance is the length of the shortest
// path that stays on the roof: a straight line, or a line that bends round
// opening / edge corners. The ridge finder compares these distances to find
// where two drain groups are equally far away (the true watershed).
// How: the nodes are the drains plus the roof's reflex corners (shortest
// paths only bend there). Every pair of nodes that can see each other is an
// edge of a PathGraph, and ShortestPathSolver gives, per group, the path
// length from the group's drains to every node. A query point then looks
// for the cheapest node it can see. Pure .NET, Revit-free.
// =======================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services
{
    /// <summary>
    /// Shortest on-roof distance from any point of a roof to each group of drains. Distances are
    /// plan (XY) distances in Revit feet. Exact when every shortest path bends only at drains or
    /// reflex corners, which holds in a polygon. Built once; the queries do not change it, so they
    /// may be called from several threads.
    /// </summary>
    public sealed class GeodesicField
    {
        /// <summary>Two distances within this many feet are a tie.</summary>
        private const double TieTolerance = 1e-9;

        /// <summary>Candidate lists up to this many nodes live on the stack instead of the heap.</summary>
        private const int StackLimit = 256;

        private readonly RoofOutline _outline;

        /// <summary>The drains (in input order), then the outline's reflex corners.</summary>
        private readonly Point3[] _nodes;

        /// <summary>Distinct group ids, ascending.</summary>
        private readonly int[] _groups;

        /// <summary>Group id to its index in <see cref="_groups"/> and <see cref="_distance"/>.</summary>
        private readonly Dictionary<int, int> _groupIndex = new Dictionary<int, int>();

        /// <summary>Path length from the nearest drain of a group to a node: [group index][node].</summary>
        private readonly double[][] _distance;

        /// <summary>Per node: the shortest path length from any group, +∞ when none reaches it.</summary>
        private readonly double[] _nearestDistance;

        /// <summary>Per node: the lowest group id within the tie tolerance of the shortest, or -1.</summary>
        private readonly int[] _nearestGroup;

        /// <summary>
        /// Builds the field: tests every pair of nodes for line of sight, then runs one shortest-path
        /// search per drain group.
        /// </summary>
        /// <param name="outline">The roof outline (outer boundary and openings).</param>
        /// <param name="drains">Drain positions, plan X/Y in feet.</param>
        /// <param name="drainGroup">Group id of each drain, same length as <paramref name="drains"/>; ids are not negative.</param>
        /// <param name="cancel">Cancels the build; it checks between node pairs.</param>
        /// <exception cref="ArgumentException">An argument is null, the lists differ in length, or a group id is negative.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancel"/> was cancelled.</exception>
        public GeodesicField(
            RoofOutline outline,
            IReadOnlyList<Point3> drains,
            IReadOnlyList<int> drainGroup,
            CancellationToken cancel = default)
        {
            if (outline == null) throw new ArgumentException("The roof outline is required.", nameof(outline));
            if (drains == null) throw new ArgumentException("The drain list is required.", nameof(drains));
            if (drainGroup == null) throw new ArgumentException("The drain group list is required.", nameof(drainGroup));
            if (drains.Count != drainGroup.Count)
                throw new ArgumentException("Every drain needs exactly one group id.", nameof(drainGroup));
            for (int i = 0; i < drainGroup.Count; i++)
                if (drainGroup[i] < 0)
                    throw new ArgumentException("Group ids must not be negative.", nameof(drainGroup));

            _outline = outline;

            int drainCount = drains.Count;
            var pivots = outline.Pivots;
            _nodes = new Point3[drainCount + pivots.Count];
            for (int i = 0; i < drainCount; i++) _nodes[i] = drains[i];
            for (int i = 0; i < pivots.Count; i++) _nodes[drainCount + i] = pivots[i];

            var ids = new SortedSet<int>();
            for (int i = 0; i < drainCount; i++) ids.Add(drainGroup[i]);
            _groups = new int[ids.Count];
            ids.CopyTo(_groups);
            for (int gi = 0; gi < _groups.Length; gi++) _groupIndex[_groups[gi]] = gi;

            var graph = BuildGraph(cancel);

            int n = _nodes.Length;
            _distance = new double[_groups.Length][];
            for (int gi = 0; gi < _groups.Length; gi++)
            {
                var sources = new List<int>();
                for (int i = 0; i < drainCount; i++)
                    if (drainGroup[i] == _groups[gi]) sources.Add(i);
                _distance[gi] = ShortestPathSolver.Solve(graph, sources, cancel).Distance;
            }

            _nearestDistance = new double[n];
            _nearestGroup = new int[n];
            for (int q = 0; q < n; q++)
            {
                double min = double.PositiveInfinity;
                for (int gi = 0; gi < _groups.Length; gi++)
                    if (_distance[gi][q] < min) min = _distance[gi][q];

                _nearestDistance[q] = min;
                _nearestGroup[q] = -1;
                if (double.IsPositiveInfinity(min)) continue;

                for (int gi = 0; gi < _groups.Length; gi++)
                    if (_distance[gi][q] <= min + TieTolerance)
                    {
                        _nearestGroup[q] = _groups[gi];
                        break;
                    }
            }
        }

        /// <summary>The distinct group ids, ascending.</summary>
        public IReadOnlyList<int> Groups => _groups;

        /// <summary>How many distinct drain groups there are.</summary>
        public int GroupCount => _groups.Length;

        /// <summary>
        /// Shortest on-roof distance from a point to the nearest drain of a group.
        /// </summary>
        /// <param name="p">The point; Z is ignored.</param>
        /// <param name="group">A group id from <see cref="Groups"/>.</param>
        /// <returns>
        /// The distance in feet, or +∞ when the point is off the roof, sees no node that reaches the
        /// group, or the group id is unknown.
        /// </returns>
        public double DistanceTo(Point3 p, int group)
        {
            if (!_groupIndex.TryGetValue(group, out int gi)) return double.PositiveInfinity;
            if (!_outline.Contains(p)) return double.PositiveInfinity;

            int n = _nodes.Length;
            Span<double> keys = n <= StackLimit ? stackalloc double[n] : new double[n];
            Span<int> order = n <= StackLimit ? stackalloc int[n] : new int[n];
            int count = SortCandidates(p, _distance[gi], keys, order);

            // The cheapest node in line of sight ends the search: it is the first hop of the best path.
            for (int k = 0; k < count; k++)
                if (_outline.IsVisible(p, _nodes[order[k]])) return keys[k];

            return double.PositiveInfinity;
        }

        /// <summary>
        /// The drain group nearest to a point by on-roof distance. A tie (within 1e-9 ft) goes to the
        /// lower group id.
        /// </summary>
        /// <param name="p">The point; Z is ignored.</param>
        /// <param name="distance">The distance to that group in feet, +∞ when there is none.</param>
        /// <returns>The group id, or -1 when the point is off the roof or reaches no drain.</returns>
        public int Nearest(Point3 p, out double distance)
        {
            distance = double.PositiveInfinity;
            if (_groups.Length == 0 || !_outline.Contains(p)) return -1;

            int n = _nodes.Length;
            Span<double> keys = n <= StackLimit ? stackalloc double[n] : new double[n];
            Span<int> order = n <= StackLimit ? stackalloc int[n] : new int[n];
            int count = SortCandidates(p, _nearestDistance, keys, order);

            // The first visible node gives the distance; later ones that are no farther (within the
            // tolerance) can only lower the group id.
            int best = -1;
            for (int k = 0; k < count; k++)
            {
                if (best >= 0 && keys[k] > distance + TieTolerance) break;

                int q = order[k];
                if (!_outline.IsVisible(p, _nodes[q])) continue;

                if (best < 0)
                {
                    distance = keys[k];
                    best = _nearestGroup[q];
                }
                else if (_nearestGroup[q] < best)
                {
                    best = _nearestGroup[q];
                }
            }
            return best;
        }

        /// <summary>An edge for every pair of nodes that see each other, weighted by plan distance.</summary>
        private PathGraph BuildGraph(CancellationToken cancel)
        {
            int n = _nodes.Length;
            var graph = new PathGraph(n);
            for (int i = 0; i < n; i++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int j = i + 1; j < n; j++)
                    if (_outline.IsVisible(_nodes[i], _nodes[j]))
                        graph.AddEdge(i, j, _nodes[i].DistanceXY(_nodes[j]));
            }
            return graph;
        }

        /// <summary>
        /// Fills <paramref name="keys"/> with |p - node| + nodeDistance[node] for every node that has
        /// a finite nodeDistance, and <paramref name="order"/> with those nodes, both sorted by key.
        /// </summary>
        /// <returns>How many nodes were kept.</returns>
        private int SortCandidates(Point3 p, double[] nodeDistance, Span<double> keys, Span<int> order)
        {
            int count = 0;
            for (int q = 0; q < _nodes.Length; q++)
            {
                if (double.IsPositiveInfinity(nodeDistance[q])) continue;
                keys[count] = p.DistanceXY(_nodes[q]) + nodeDistance[q];
                order[count] = q;
                count++;
            }
            keys.Slice(0, count).Sort(order.Slice(0, count));
            return count;
        }
    }
}
