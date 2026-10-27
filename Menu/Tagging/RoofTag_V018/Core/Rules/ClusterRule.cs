using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RoofTag.V018.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofTag.V018.Core
{
    /// <summary>Rule 1: keeps one point per cluster of same-elevation points within a radius.</summary>
    internal sealed class ClusterRule : IPointRule
    {
        private readonly double _radiusMm;

        public ClusterRule(double radiusMm) => _radiusMm = radiusMm;

        /// <inheritdoc/>
        public PointRuleResult Apply(RoofBase roof, List<XYZ> points)
        {
            List<XYZ> kept = RoofTagClusterHelper.Filter(points, MmLogFormatter.ToFeet(_radiusMm), out List<XYZ> skipped);

            var logs = new List<LogEntry>();
            foreach (XYZ sp in skipped)
                logs.Add(new LogEntry(LogLevel.Warning,
                    $"Skipped (clustered within {_radiusMm:0} mm, same elevation ±5 mm) — {MmLogFormatter.Point(sp)}"));
            logs.Add(new LogEntry(LogLevel.Info,
                $"Cluster filter — {points.Count} pts → {kept.Count} after grouping (radius {_radiusMm:0} mm, Z tol 5 mm)"));

            return new PointRuleResult(kept, logs);
        }
    }
}
