using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofTag.V018.Core
{
    /// <summary>Outcome of one filter rule: the points to keep and the log lines it produced.</summary>
    internal sealed class PointRuleResult
    {
        public PointRuleResult(List<XYZ> kept, List<LogEntry> logs)
        {
            Kept = kept;
            Logs = logs;
        }

        public List<XYZ> Kept { get; }
        public List<LogEntry> Logs { get; }
    }

    /// <summary>A step that removes redundant points before tags are placed.</summary>
    internal interface IPointRule
    {
        /// <summary>Applies the rule to <paramref name="points"/> and returns what is left.</summary>
        PointRuleResult Apply(RoofBase roof, List<XYZ> points);
    }
}
