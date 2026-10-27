using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RoofTag.V018.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofTag.V018.Core
{
    /// <summary>Runs the enabled filter rules in order: interior loops, exterior loop, then clusters.</summary>
    internal static class PointFilterPipeline
    {
        /// <summary>Builds the rules the options switch on, in run order.</summary>
        public static List<IPointRule> BuildRules(RoofTagOptions o)
        {
            var rules = new List<IPointRule>();
            if (o.InteriorLoopEnabled)
                rules.Add(new LoopReductionRule(LoopScope.Interior, o.InteriorLoopSpacingMm, o.InteriorLoopToleranceMm));
            if (o.ExteriorLoopEnabled)
                rules.Add(new LoopReductionRule(LoopScope.Exterior, o.ExteriorLoopSpacingMm, o.ExteriorLoopToleranceMm));
            if (o.ClusterEnabled)
                rules.Add(new ClusterRule(o.ClusterRadiusMm));
            return rules;
        }

        /// <summary>Applies each rule to the output of the one before; every rule's log lines go to <paramref name="log"/>.</summary>
        public static List<XYZ> Run(RoofBase roof, List<XYZ> points, IEnumerable<IPointRule> rules, Action<LogEntry> log)
        {
            foreach (IPointRule rule in rules)
            {
                PointRuleResult result = rule.Apply(roof, points);
                foreach (LogEntry entry in result.Logs)
                    log(entry);
                points = result.Kept;
            }
            return points;
        }
    }
}
