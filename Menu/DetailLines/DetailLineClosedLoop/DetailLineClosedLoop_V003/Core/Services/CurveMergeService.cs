using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.DetailLineClosedLoop.V003.Core.Services
{
    /// <summary>
    /// Step 3 — groups collinear Lines onto the same infinite line (by
    /// direction + perpendicular offset), then merges overlapping/touching
    /// parameter spans within each group into single continuous Lines. Arcs
    /// pass through untouched.
    /// </summary>
    public static class CurveMergeService
    {
        public static List<Curve> MergeOverlappingCollinear(List<Curve> curves, double tolerance, out int mergedCount, ObservableCollection<LogEntry> log)
        {
            var lines = curves.OfType<Line>().ToList();
            var others = curves.Where(c => !(c is Line)).ToList();

            var groups = new List<List<Line>>();
            foreach (Line line in lines)
            {
                XYZ dir = CanonicalDirection(line.Direction);
                bool placed = false;

                foreach (var group in groups)
                {
                    ToolCancellation.CheckCurrent();
                    XYZ groupDir = CanonicalDirection(group[0].Direction);
                    bool parallel = dir.CrossProduct(groupDir).GetLength() < 1e-6;
                    if (!parallel) continue;

                    XYZ toStart = line.GetEndPoint(0) - group[0].GetEndPoint(0);
                    double offsetDist = toStart.CrossProduct(groupDir).GetLength();
                    if (offsetDist > tolerance) continue;

                    group.Add(line);
                    placed = true;
                    break;
                }

                if (!placed)
                    groups.Add(new List<Line> { line });
            }

            int merged = 0;
            int spansProduced = 0;
            var result = new List<Curve>(others);

            foreach (var group in groups)
            {
                if (group.Count == 1)
                {
                    result.Add(group[0]);
                    continue;
                }

                XYZ origin = group[0].GetEndPoint(0);
                XYZ dir = group[0].Direction.Normalize();

                var intervals = group
                    .Select(l =>
                    {
                        double t0 = (l.GetEndPoint(0) - origin).DotProduct(dir);
                        double t1 = (l.GetEndPoint(1) - origin).DotProduct(dir);
                        return (Min: Math.Min(t0, t1), Max: Math.Max(t0, t1));
                    })
                    .OrderBy(iv => iv.Min)
                    .ToList();

                var mergedIntervals = new List<(double Min, double Max)> { intervals[0] };
                for (int k = 1; k < intervals.Count; k++)
                {
                    var last = mergedIntervals[^1];
                    if (intervals[k].Min <= last.Max + tolerance)
                        mergedIntervals[^1] = (last.Min, Math.Max(last.Max, intervals[k].Max));
                    else
                        mergedIntervals.Add(intervals[k]);
                }

                merged += group.Count - mergedIntervals.Count;

                foreach (var span in mergedIntervals)
                {
                    XYZ p0 = origin + dir * span.Min;
                    XYZ p1 = origin + dir * span.Max;
                    if (p0.DistanceTo(p1) > tolerance)
                    {
                        result.Add(Line.CreateBound(p0, p1));
                        spansProduced++;
                    }
                }
            }

            mergedCount = merged;
            if (merged > 0)
                log.Add(new LogEntry(LogLevel.Warning, $"Merged {merged} overlapping/collinear segment(s) into {spansProduced} continuous span(s)"));

            return result;
        }

        private static XYZ CanonicalDirection(XYZ dir)
        {
            dir = dir.Normalize();
            bool negate = dir.Z < -1e-9
                || (Math.Abs(dir.Z) < 1e-9 && dir.Y < -1e-9)
                || (Math.Abs(dir.Z) < 1e-9 && Math.Abs(dir.Y) < 1e-9 && dir.X < 0);
            return negate ? -dir : dir;
        }
    }
}
