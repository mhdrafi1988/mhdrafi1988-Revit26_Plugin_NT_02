using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RoofTag.V018.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofTag.V018.Core
{
    /// <summary>Rule 2 (interior loops) and rule 3 (exterior loop): keeps the top points on each loop.</summary>
    internal sealed class LoopReductionRule : IPointRule
    {
        private readonly LoopScope _scope;
        private readonly double _spacingMm;
        private readonly double _toleranceMm;

        public LoopReductionRule(LoopScope scope, double spacingMm, double toleranceMm)
        {
            _scope = scope;
            _spacingMm = spacingMm;
            _toleranceMm = toleranceMm;
        }

        /// <inheritdoc/>
        public PointRuleResult Apply(RoofBase roof, List<XYZ> points)
        {
            bool interior = _scope == LoopScope.Interior;
            string label = interior ? "interior" : "exterior";
            string title = interior ? "Interior" : "Exterior";
            int ruleNo = interior ? 2 : 3;

            List<XYZ> kept = RoofTagLoopReductionHelper.Filter(
                roof, points, MmLogFormatter.ToFeet(_spacingMm), MmLogFormatter.ToFeet(_toleranceMm), _scope,
                out List<XYZ> skipped, out List<string> loopLogs);

            var logs = new List<LogEntry>();
            foreach (string line in loopLogs)
                logs.Add(new LogEntry(LogLevel.Info, line));
            foreach (XYZ sp in skipped)
                logs.Add(new LogEntry(LogLevel.Warning,
                    $"Skipped ({label} loop reduction, spacing {_spacingMm:0} mm) — {MmLogFormatter.Point(sp)}"));
            logs.Add(new LogEntry(LogLevel.Info,
                $"{title} loop reduction — {points.Count} pts → {kept.Count} after rule {ruleNo} " +
                $"(spacing {_spacingMm:0} mm, tol {_toleranceMm:0} mm)"));

            return new PointRuleResult(kept, logs);
        }
    }
}
