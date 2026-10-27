using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofTag.V018.Core
{
    /// <summary>The tag run: collect points, de-duplicate, filter by the rules, place the tags.</summary>
    internal sealed class RoofTagWorkflow
    {
        private readonly UIDocument _uiDoc;
        private readonly RoofTagViewModel _vm;

        public RoofTagWorkflow(UIDocument uiDoc, RoofTagViewModel vm)
        {
            _uiDoc = uiDoc;
            _vm = vm;
        }

        /// <summary>Runs the whole workflow for <paramref name="roof"/> using the window's current settings.</summary>
        public Result Run(RoofBase roof)
        {
            _vm.ResetCounters();
            RoofTagOptions options = RoofTagOptions.From(_vm);

            List<XYZ> rawPoints = PointCollector.Collect(_uiDoc, roof, options.UseManualMode);
            if (rawPoints.Count == 0)
            {
                TaskDialog.Show(ToolCatalog.RoofTag.Title, "No valid points found.");
                return Result.Cancelled;
            }

            List<XYZ> points = PointCollector.Deduplicate(rawPoints, PointCollector.DedupTolFt);
            _vm.AddLog(new LogEntry(LogLevel.Info,
                $"Roof id {roof.Id} — {rawPoints.Count} raw pts → {points.Count} unique"));

            points = PointFilterPipeline.Run(roof, points, PointFilterPipeline.BuildRules(options), _vm.AddLog);

            TagPlacementRunner.Run(_uiDoc.Document, roof, points, options, _vm.AddLog);
            return Result.Succeeded;
        }
    }
}
