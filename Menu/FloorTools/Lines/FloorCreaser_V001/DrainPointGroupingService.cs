// ==================================
// File: DrainPointGroupingService.cs
// Namespace: Revit26_Plugin_Revised.FloorCreaser.V001
// ==================================

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorCreaser.V001.Services
{
    /// <summary>
    /// Groups crease lines by drain points (lowest Z endpoints) within a proximity radius.
    /// For each drain group, keeps only the shortest line per start point.
    ///
    /// Algorithm:
    ///   1. Identify drain endpoint (lowest Z) on each 3D crease curve
    ///   2. Cluster drain points by XY proximity (ignoring Z)
    ///   3. Within each cluster, group by start point of 2D line
    ///   4. Keep shortest line per start point, remove others
    /// </summary>
    public class DrainPointGroupingService
    {
        private readonly LoggingService _log;

        public DrainPointGroupingService(LoggingService log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>
        /// Filters crease lines by drain point grouping and removes longest
        /// line per (start point, drain group) pair.
        /// </summary>
        /// <param name="originalCurves3d">Original 3D crease curves (before projection).</param>
        /// <param name="projectedLines2d">Projected 2D lines (1:1 mapping with originalCurves3d).</param>
        /// <param name="proximityRadius">Max distance between drain points to form a group (in model units).</param>
        /// <returns>Filtered 2D lines after drain grouping logic.</returns>
        public IList<Line> FilterByDrainProximity(
            IList<Curve> originalCurves3d,
            IList<Line>  projectedLines2d,
            double       proximityRadius)
        {
            if (originalCurves3d.Count != projectedLines2d.Count)
                throw new ArgumentException("Curve and line counts must match.");

            if (originalCurves3d.Count == 0)
            {
                _log.Info("No crease curves to group by drain points.");
                return new List<Line>();
            }

            // ────────────────────────────────────────────────────────────
            // Step 1: Identify drain points (lowest Z endpoint) for each curve
            // ────────────────────────────────────────────────────────────

            var drainInfo = new List<(int index, XYZ drainPoint3d, XYZ startPoint2d, Line line2d, double length)>();

            for (int i = 0; i < originalCurves3d.Count; i++)
            {
                Curve curve = originalCurves3d[i];
                Line  line  = projectedLines2d[i];

                if (curve == null || line == null)
                    continue;

                XYZ p0 = curve.GetEndPoint(0);
                XYZ p1 = curve.GetEndPoint(1);

                // Find drain point: lowest Z endpoint
                XYZ drainPt3d = p0.Z < p1.Z ? p0 : p1;

                XYZ startPt2d = line.GetEndPoint(0);
                double len    = line.Length;

                drainInfo.Add((i, drainPt3d, startPt2d, line, len));
            }

            _log.Info($"Drain points identified: {drainInfo.Count}");

            // ────────────────────────────────────────────────────────────
            // Step 2: Cluster drain points by XY proximity
            // ────────────────────────────────────────────────────────────

            var drainGroups = ClusterByProximity(drainInfo, proximityRadius);
            _log.Info($"Drain groups formed: {drainGroups.Count}");

            // ────────────────────────────────────────────────────────────
            // Step 3: For each drain group, filter by start point
            // ────────────────────────────────────────────────────────────

            var result = new List<Line>();
            int totalRemoved = 0;
            int groupNum = 1;

            foreach (var group in drainGroups)
            {
                int groupRemoved = FilterDrainGroup(group, result, groupNum);
                totalRemoved += groupRemoved;
                groupNum++;
            }

            _log.Info($"Drain grouping complete: kept {result.Count}, removed {totalRemoved}");
            return result;
        }

        // ────────────────────────────────────────────────────────────────
        // Clustering helper
        // ────────────────────────────────────────────────────────────────

        private List<List<(int idx, XYZ drain3d, XYZ start2d, Line line2d, double len)>>
            ClusterByProximity(
                List<(int idx, XYZ drain3d, XYZ start2d, Line line2d, double len)> drainInfo,
                double radius)
        {
            var groups = new List<List<(int, XYZ, XYZ, Line, double)>>();
            var visited = new HashSet<int>();

            for (int i = 0; i < drainInfo.Count; i++)
            {
                if (visited.Contains(i))
                    continue;

                var (idx, drain, start, line, len) = drainInfo[i];
                var currentGroup = new List<(int, XYZ, XYZ, Line, double)> { (idx, drain, start, line, len) };
                visited.Add(i);

                // Find all points within radius
                for (int j = i + 1; j < drainInfo.Count; j++)
                {
                    if (visited.Contains(j))
                        continue;

                    var (otherIdx, otherDrain, otherStart, otherLine, otherLen) = drainInfo[j];
                    double dist = ProjectToXY(drain).DistanceTo(ProjectToXY(otherDrain));

                    if (dist <= radius)
                    {
                        currentGroup.Add((otherIdx, otherDrain, otherStart, otherLine, otherLen));
                        visited.Add(j);
                    }
                }

                groups.Add(currentGroup);
            }

            return groups;
        }

        // ────────────────────────────────────────────────────────────────
        // Filter logic for one drain group
        // ────────────────────────────────────────────────────────────────

        private int FilterDrainGroup(
            List<(int idx, XYZ drain3d, XYZ start2d, Line line2d, double len)> group,
            List<Line> result,
            int groupNum)
        {
            // Group by start point (XY)
            var byStartPt = new Dictionary<string, List<(int idx, Line line, double len)>>();

            foreach (var (idx, drain, start, line, len) in group)
            {
                string key = StartPointKey(start);

                if (!byStartPt.ContainsKey(key))
                    byStartPt[key] = new List<(int, Line, double)>();

                byStartPt[key].Add((idx, line, len));
            }

            int groupRemoved = 0;

            // For each start point, keep shortest only
            _log.Debug($"  Drain group {groupNum}: {group.Count} lines, {byStartPt.Count} start points");

            foreach (var kvp in byStartPt)
            {
                string startKey = kvp.Key;
                var linesFromStart = kvp.Value.OrderBy(x => x.len).ToList();

                // Keep shortest
                result.Add(linesFromStart[0].line);

                // Remove others
                for (int i = 1; i < linesFromStart.Count; i++)
                {
                    groupRemoved++;
                    _log.Debug($"  Removed: start {startKey} ft, length {linesFromStart[i].len * 304.8:F0} mm");
                }

                if (linesFromStart.Count > 1)
                {
                    _log.Debug($"  Kept: start {startKey} ft, length {linesFromStart[0].len * 304.8:F0} mm " +
                        $"({linesFromStart.Count - 1} removed)");
                }
            }

            return groupRemoved;
        }

        // ────────────────────────────────────────────────────────────────
        // Helper: project to XY
        // ────────────────────────────────────────────────────────────────

        private static XYZ ProjectToXY(XYZ pt) => new XYZ(pt.X, pt.Y, 0);

        private static string StartPointKey(XYZ pt) => $"{pt.X:F3}_{pt.Y:F3}";
    }
}
