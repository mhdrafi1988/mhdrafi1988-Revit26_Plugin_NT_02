using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;


namespace Revit26_Plugin_Revised.RoofEdgeAroundSections.V006
{
    /// <summary>
    /// Single handler/event pair for RoofEdgeSections V002 (one window, distinct
    /// Revit-side actions routed through RequestedAction rather than separate
    /// handler/event pairs, per convention: "shared orchestrator only if one
    /// window has multiple distinct Revit-side actions" — this window has two:
    /// BuildPlan and RunCreate).
    /// </summary>
    public enum RoofEdgeSectionsAction
    {
        BuildPlan,
        RunCreate
    }

    public class RoofEdgeSectionsEventHandler : IExternalEventHandler
    {
        public RoofEdgeSectionsAction RequestedAction { get; set; }

        /// <summary>Roofs captured from the pre-launch selection (set once, by the command).</summary>
        public IList<Element> SelectedRoofs { get; set; } = new List<Element>();

        /// <summary>Non-roof elements from the pre-launch selection, for the skipped-count summary.</summary>
        public IList<Element> SkippedNonRoofElements { get; set; } = new List<Element>();

        /// <summary>Current settings snapshot (offset/depth/crop/etc.) at time of request.</summary>
        public RoofEdgeSectionsSettings Settings { get; set; }

        /// <summary>The plan rows the user has checked, for RunCreate.</summary>
        public IEnumerable<PlannedSection> RowsToProcess { get; set; } = Enumerable.Empty<PlannedSection>();

        /// <summary>Selected View Template option (or None), for RunCreate.</summary>
        public ViewTemplateOption ViewTemplate { get; set; }

        /// <summary>Callback invoked on the UI thread once BuildPlan finishes.</summary>
        public Action<SectionPlanBuilder.PlanBuildResult, System.Collections.ObjectModel.ObservableCollection<LogEntry>> OnPlanBuilt { get; set; }

        /// <summary>Callback invoked on the UI thread once RunCreate finishes.</summary>
        public Action<RunResult, System.Collections.ObjectModel.ObservableCollection<LogEntry>> OnRunComplete { get; set; }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            var log = new System.Collections.ObjectModel.ObservableCollection<LogEntry>();

            try
            {
                switch (RequestedAction)
                {
                    case RoofEdgeSectionsAction.BuildPlan:
                        ExecuteBuildPlan(app, doc, log);
                        break;

                    case RoofEdgeSectionsAction.RunCreate:
                        ExecuteRunCreate(doc, log);
                        break;
                }
            }
            catch (Exception ex)
            {
                log.Add(new LogEntry(LogLevel.Error, $"Unhandled error: {ex.Message}"));
                if (RequestedAction == RoofEdgeSectionsAction.BuildPlan)
                    OnPlanBuilt?.Invoke(new SectionPlanBuilder.PlanBuildResult
                    {
                        Plan = new System.Collections.ObjectModel.ObservableCollection<PlannedSection>(),
                        TotalRoofsCount = 0,
                        DetectedCount = 0,
                        SuggestedCount = 0
                    }, log);
                else
                    OnRunComplete?.Invoke(new RunResult(), log);
            }
        }

        private void ExecuteBuildPlan(UIApplication app, Document doc, System.Collections.ObjectModel.ObservableCollection<LogEntry> log)
        {
            double viewRotationRadians = 0.0;
            View activeView = app.ActiveUIDocument.ActiveView;
            if (activeView is ViewPlan)
            {
                // ViewPlan rotation relative to true north, in radians.
                viewRotationRadians = activeView.CropBox?.Transform != null
                    ? Math.Atan2(activeView.CropBox.Transform.BasisX.Y, activeView.CropBox.Transform.BasisX.X)
                    : 0.0;
            }

            var builder = new SectionPlanBuilder();
            var result = builder.BuildPlan(doc, SelectedRoofs, SkippedNonRoofElements, viewRotationRadians, Settings, log);

            OnPlanBuilt?.Invoke(result, log);
        }

        private void ExecuteRunCreate(Document doc, System.Collections.ObjectModel.ObservableCollection<LogEntry> log)
        {
            ViewFamilyType sectionVft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.Section);

            if (sectionVft == null)
            {
                log.Add(new LogEntry(LogLevel.Error, "No Section ViewFamilyType found in the document — cannot create sections."));
                OnRunComplete?.Invoke(new RunResult(), log);
                return;
            }

            var creationService = new SectionCreationService();
            RunResult result = creationService.CreateSections(doc, RowsToProcess, Settings, sectionVft, ViewTemplate, log);

            OnRunComplete?.Invoke(result, log);
        }

        public string GetName() => "RoofEdgeSections V002 Event Handler";
    }
}
