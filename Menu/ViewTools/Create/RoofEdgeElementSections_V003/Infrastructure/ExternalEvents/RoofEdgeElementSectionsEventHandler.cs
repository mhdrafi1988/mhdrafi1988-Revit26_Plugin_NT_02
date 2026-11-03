using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofEdgeElementSections.V003
{
    /// <summary>
    /// Single handler/event pair for RoofEdgeElementSections V002 — one window, two
    /// distinct Revit-side actions routed through RequestedAction (BuildPlan,
    /// RunCreate), matching RoofEdgeAroundSections_V004's convention. Roof picking
    /// and link/tree browsing do NOT go through this handler — those use
    /// uiDoc.Selection.PickObjects directly / are pure UI-thread reads, no
    /// ExternalEvent needed.
    /// </summary>
    public enum RoofEdgeElementSectionsAction
    {
        BuildPlan,
        RunCreate
    }

    public class RoofEdgeElementSectionsEventHandler : IExternalEventHandler
    {
        public RoofEdgeElementSectionsAction RequestedAction { get; set; }

        /// <summary>Roofs picked into the "1. Roofs" list, for BuildPlan.</summary>
        public IList<RoofBase> SelectedRoofs { get; set; } = new List<RoofBase>();

        /// <summary>Snapshot of the current Category→Family→Type tree, for BuildPlan.</summary>
        public IEnumerable<LinkTreeNode> ElementTree { get; set; } = Enumerable.Empty<LinkTreeNode>();

        /// <summary>Current settings snapshot at time of request.</summary>
        public RoofEdgeElementSectionsSettings Settings { get; set; }

        /// <summary>The plan rows the user has checked, for RunCreate.</summary>
        public IEnumerable<PlannedSection> RowsToProcess { get; set; } = Enumerable.Empty<PlannedSection>();

        /// <summary>Selected View Template option (or None), for RunCreate.</summary>
        public ViewTemplateOption ViewTemplate { get; set; }

        /// <summary>Callback invoked on the UI thread once BuildPlan finishes.</summary>
        public Action<SectionPlanBuilder.PlanBuildResult, ObservableCollection<LogEntry>> OnPlanBuilt { get; set; }

        /// <summary>Callback invoked on the UI thread once RunCreate finishes.</summary>
        public Action<RunResult, ObservableCollection<LogEntry>> OnRunComplete { get; set; }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            var log = new ObservableCollection<LogEntry>();

            try
            {
                switch (RequestedAction)
                {
                    case RoofEdgeElementSectionsAction.BuildPlan:
                        ExecuteBuildPlan(app, doc, log);
                        break;

                    case RoofEdgeElementSectionsAction.RunCreate:
                        ExecuteRunCreate(doc, log);
                        break;
                }
            }
            catch (Exception ex)
            {
                log.Add(new LogEntry(LogLevel.Error, $"Unhandled error: {ex.Message}"));
                if (RequestedAction == RoofEdgeElementSectionsAction.BuildPlan)
                    OnPlanBuilt?.Invoke(new SectionPlanBuilder.PlanBuildResult
                    {
                        Plan = new ObservableCollection<PlannedSection>(),
                        TotalRoofsCount = 0,
                        DetectedElementCount = 0,
                        SuggestedSectionCount = 0
                    }, log);
                else
                    OnRunComplete?.Invoke(new RunResult(), log);
            }
        }

        private void ExecuteBuildPlan(UIApplication app, Document doc, ObservableCollection<LogEntry> log)
        {
            double viewRotationRadians = 0.0;
            View activeView = app.ActiveUIDocument.ActiveView;
            if (activeView is ViewPlan)
            {
                viewRotationRadians = activeView.CropBox?.Transform != null
                    ? Math.Atan2(activeView.CropBox.Transform.BasisX.Y, activeView.CropBox.Transform.BasisX.X)
                    : 0.0;
            }

            var builder = new SectionPlanBuilder();
            var result = builder.BuildPlan(doc, SelectedRoofs, ElementTree, viewRotationRadians, Settings, log);

            OnPlanBuilt?.Invoke(result, log);
        }

        private void ExecuteRunCreate(Document doc, ObservableCollection<LogEntry> log)
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

        public string GetName() => "RoofEdgeElementSections V002 Event Handler";
    }
}
