using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;


namespace Revit26_Plugin_Revised.RoofEdgeAroundSections.V007
{
    /// <summary>
    /// Revit-side actions of the Roof Edge Sections window, routed through one
    /// handler/event pair via <see cref="RoofEdgeSectionsEventHandler.RequestedAction"/>.
    /// </summary>
    public enum RoofEdgeSectionsAction
    {
        /// <summary>Scan the selected roofs and build the planned-sections list.</summary>
        BuildPlan,

        /// <summary>Create the checked planned sections.</summary>
        RunCreate,

        /// <summary>Open the section views the user picked in the "Open created sections" popover.</summary>
        OpenViews
    }

    /// <summary>
    /// Single external-event handler for the window. Every action reports back through
    /// its callback, also after an error, so the window never stays busy.
    /// </summary>
    public class RoofEdgeSectionsEventHandler : IExternalEventHandler
    {
        /// <summary>Which action the next <see cref="Execute"/> runs.</summary>
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

        /// <summary>
        /// The project the window was opened for. Every action refuses to run while another
        /// project is active, so sections are never planned, created or opened in the wrong one.
        /// </summary>
        public Document TargetDocument { get; set; }

        /// <summary>Section views to open, in order, for OpenViews.</summary>
        public IList<ElementId> ViewIdsToOpen { get; set; } = new List<ElementId>();

        /// <summary>Callback invoked on the UI thread once BuildPlan finishes.</summary>
        public Action<SectionPlanBuilder.PlanBuildResult, ObservableCollection<LogEntry>> OnPlanBuilt { get; set; }

        /// <summary>Callback invoked on the UI thread once RunCreate finishes.</summary>
        public Action<RunResult, ObservableCollection<LogEntry>> OnRunComplete { get; set; }

        /// <summary>Callback invoked on the UI thread once OpenViews finishes: (opened, failed, log).</summary>
        public Action<int, int, ObservableCollection<LogEntry>> OnViewsOpened { get; set; }

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
                    case RoofEdgeSectionsAction.BuildPlan:
                        ExecuteBuildPlan(app, doc, log);
                        break;

                    case RoofEdgeSectionsAction.RunCreate:
                        ExecuteRunCreate(doc, log);
                        break;

                    case RoofEdgeSectionsAction.OpenViews:
                        ExecuteOpenViews(app.ActiveUIDocument, log);
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
                return "The project this window was opened for has been closed. Close this window and start Roof Edge Sections again.";

            if (!TargetDocument.Equals(active))
                return $"The active project is '{active.Title}', but this window works on '{TargetDocument.Title}'. " +
                       "Switch back to that project, or close this window and start Roof Edge Sections in this one.";

            if (RequestedAction == RoofEdgeSectionsAction.RunCreate && active.IsReadOnly)
                return $"'{active.Title}' is read-only, so no section views can be created.";

            return null;
        }

        /// <summary>Calls the window back with an empty result, so it never stays busy after a failure.</summary>
        private void NotifyFailed(ObservableCollection<LogEntry> log)
        {
            switch (RequestedAction)
            {
                case RoofEdgeSectionsAction.BuildPlan:
                    OnPlanBuilt?.Invoke(new SectionPlanBuilder.PlanBuildResult
                    {
                        Plan = new ObservableCollection<PlannedSection>(),
                        TotalRoofsCount = 0,
                        DetectedCount = 0,
                        SuggestedCount = 0
                    }, log);
                    break;

                case RoofEdgeSectionsAction.RunCreate:
                    OnRunComplete?.Invoke(new RunResult(), log);
                    break;

                case RoofEdgeSectionsAction.OpenViews:
                    OnViewsOpened?.Invoke(0, ViewIdsToOpen?.Count ?? 0, log);
                    break;
            }
        }

        private void ExecuteBuildPlan(UIApplication app, Document doc, ObservableCollection<LogEntry> log)
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

        /// <summary>
        /// Opens each requested section view as a tab, in order (the last one ends up active).
        /// Views that no longer exist or fail to open are logged and counted, never thrown.
        /// </summary>
        private void ExecuteOpenViews(UIDocument uidoc, ObservableCollection<LogEntry> log)
        {
            IList<ElementId> ids = ViewIdsToOpen ?? new List<ElementId>();
            Document doc = uidoc.Document; // already checked to be TargetDocument

            int opened = 0;
            int failed = 0;

            foreach (ElementId id in ids)
            {
                if (doc.GetElement(id) is not View view)
                {
                    failed++;
                    log.Add(new LogEntry(LogLevel.Warning, $"View Id {id.Value} no longer exists — skipped."));
                    continue;
                }

                try
                {
                    uidoc.ActiveView = view;
                    opened++;
                }
                catch (Exception ex)
                {
                    failed++;
                    log.Add(new LogEntry(LogLevel.Warning, $"{view.Name}: could not be opened — {ex.Message}"));
                }
            }

            log.Add(new LogEntry(failed == 0 ? LogLevel.Success : LogLevel.Warning,
                $"Opened {opened} section view(s)" + (failed > 0 ? $", {failed} could not be opened." : ".")));

            OnViewsOpened?.Invoke(opened, failed, log);
        }

        /// <inheritdoc/>
        public string GetName() => "Roof Edge Around Sections V007 Event Handler";
    }
}
