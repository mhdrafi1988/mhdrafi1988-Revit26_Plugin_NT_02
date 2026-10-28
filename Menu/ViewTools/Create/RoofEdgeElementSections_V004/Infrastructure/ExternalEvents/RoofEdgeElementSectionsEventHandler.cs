using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofEdgeElementSections.V004
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

        /// <summary>
        /// The project the window was opened for. Every action refuses to run while another
        /// project is active, so sections are never planned or created in the wrong one.
        /// </summary>
        public Document TargetDocument { get; set; }

        /// <summary>Callback invoked on the UI thread once BuildPlan finishes.</summary>
        public Action<SectionPlanBuilder.PlanBuildResult, ObservableCollection<LogEntry>> OnPlanBuilt { get; set; }

        /// <summary>Callback invoked on the UI thread once RunCreate finishes.</summary>
        public Action<RunResult, ObservableCollection<LogEntry>> OnRunComplete { get; set; }

        /// <inheritdoc/>
        public void Execute(UIApplication app)
            => ToolGuard.RunHandler(GetType(), app, ExecuteUnguarded);

        private void ExecuteUnguarded(UIApplication app)
        {
            var log = new ObservableCollection<LogEntry>();

            try
            {
                Document doc = app.ActiveUIDocument?.Document;
                string refusal = CheckTargetDocument(doc);
                if (refusal != null)
                {
                    log.Add(new LogEntry(LogLevel.Error, refusal));
                    NotifyFailed(log);
                    return;
                }

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
                NotifyFailed(log);
            }
        }

        /// <summary>
        /// Returns why the requested action must not run in the active project, or null when it may:
        /// no project open, the window's project closed, another project active, or (for RunCreate)
        /// a read-only project.
        /// </summary>
        private string CheckTargetDocument(Document active)
        {
            if (active == null)
                return "No project is open. Open the project this window was started in and try again.";

            if (TargetDocument == null || !TargetDocument.IsValidObject)
                return "The project this window was opened for has been closed. Close this window and start Roof Edge Element Sections again.";

            if (!TargetDocument.Equals(active))
                return $"The active project is '{active.Title}', but this window works on '{TargetDocument.Title}'. " +
                       "Switch back to that project, or close this window and start Roof Edge Element Sections in this one.";

            if (RequestedAction == RoofEdgeElementSectionsAction.RunCreate && active.IsReadOnly)
                return $"'{active.Title}' is read-only, so no section views can be created.";

            return null;
        }

        /// <summary>Calls the window back with an empty result, so it never stays busy after a failure.</summary>
        private void NotifyFailed(ObservableCollection<LogEntry> log)
        {
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

        /// <inheritdoc/>
        public string GetName() => "Roof Edge Element Sections V004 Event Handler";
    }
}
