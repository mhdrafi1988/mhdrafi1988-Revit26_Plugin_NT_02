using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.DetailLineClosedLoop.V003.Core.Geometry;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.DetailLineClosedLoop.V003.Core.Services
{
    /// <summary>
    /// Step 6 — repeatedly pairs the nearest two remaining loose ends
    /// (endpoints with only one connected curve) and bridges them with a
    /// straight Line, as long as the gap is within the user's tolerance.
    /// Ends left unpaired beyond tolerance are surfaced by loop validation.
    /// </summary>
    public static class GapClosureService
    {
        public static List<Curve> CloseGaps(List<Curve> curves, double gapTolerance, double topologyEpsilon, out int gapsClosed, out double maxGapClosedFeet, ObservableCollection<LogEntry> log)
        {
            var result = new List<Curve>(curves);
            int closed = 0;
            double maxGap = 0;
            bool changed = true;

            while (changed)
            {
                ToolCancellation.CheckCurrent();
                changed = false;
                EndpointGraph graph = EndpointGraph.Build(result, topologyEpsilon);
                var looseNodes = Enumerable.Range(0, graph.Nodes.Count)
                    .Where(n => graph.Degree(n) == 1)
                    .ToList();

                if (looseNodes.Count < 2) break;

                double bestDist = double.MaxValue;
                int bestA = -1, bestB = -1;
                for (int a = 0; a < looseNodes.Count; a++)
                {
                    for (int b = a + 1; b < looseNodes.Count; b++)
                    {
                        double dist = graph.Nodes[looseNodes[a]].DistanceTo(graph.Nodes[looseNodes[b]]);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestA = looseNodes[a];
                            bestB = looseNodes[b];
                        }
                    }
                }

                if (bestA < 0 || bestDist <= topologyEpsilon || bestDist > gapTolerance)
                    break;

                result.Add(Line.CreateBound(graph.Nodes[bestA], graph.Nodes[bestB]));
                closed++;
                maxGap = Math.Max(maxGap, bestDist);
                changed = true;
            }

            gapsClosed = closed;
            maxGapClosedFeet = maxGap;
            if (closed > 0)
            {
                double maxGapMm = UnitUtils.ConvertFromInternalUnits(maxGap, UnitTypeId.Millimeters);
                log.Add(new LogEntry(LogLevel.Success, $"Auto-closed {closed} gap(s) with straight bridge line(s), largest {maxGapMm:F1} mm"));
            }

            return result;
        }
    }
}
