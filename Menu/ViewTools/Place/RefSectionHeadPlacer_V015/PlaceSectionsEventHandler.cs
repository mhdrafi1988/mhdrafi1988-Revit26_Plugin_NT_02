using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.RefSectionHeadPlacer.V015.Core.Engine;
using Revit26_Plugin_Revised.RefSectionHeadPlacer.V015.Core.Models;
using Revit26_Plugin_Revised.RefSectionHeadPlacer.V015.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RefSectionHeadPlacer.V015.Infrastructure.ExternalEvents
{
    /// <summary>
    /// IExternalEventHandler for Run. Execute() runs on Revit's API thread, where
    /// it is safe to open transactions. The parent view for the reference sections
    /// is the ACTIVE host view captured here at execution time.
    ///
    /// NOTE (Cancel): because Execute() runs on Revit's main thread, the WPF window
    /// is frozen for the duration of the run and the Cancel button cannot be
    /// clicked mid-run. The cancellation hook is kept for correctness/future
    /// off-thread work, but in practice Cancel takes effect only if the UI can
    /// process the click — flag this to users.
    /// </summary>
    public class PlaceSectionsEventHandler : IExternalEventHandler
    {
        public class RequestArgs
        {
            public IReadOnlyList<ElementTypeRow> SelectedTypes { get; set; }
            public IReadOnlyList<CategoryMappingRow> Mappings { get; set; }
            public ElementId SectionViewFamilyTypeId { get; set; }
            /// <summary>Head-&gt;tail marker length, Revit internal units (feet). Converted
            /// from the user's mm input in the ViewModel before the request is raised.</summary>
            public double TailLengthFeet { get; set; }
        }

        public RequestArgs PendingRequest { get; set; }

        public event Action<RunSummary> RunCompleted;
        public event Action<Exception> RunFaulted;
        public event Action<LogEntry> LogEmitted;
        public event Action<int, int> ProgressChanged;

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            var request = PendingRequest;
            if (request == null) return;

            var uiDoc = app.ActiveUIDocument;
            var doc = uiDoc.Document;
            var activeView = uiDoc.ActiveView;

            // Esc / Cancel with a Yes-No confirm while the run places sections.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.RefSectionHeadPlacer, app);
            try
            {
                // Guard: reference sections need a graphical parent view (a plan here).
                if (activeView == null || !(activeView is ViewPlan))
                {
                    LogEmitted?.Invoke(new LogEntry(LogLevel.Error,
                        "Active view is not a plan view. Activate the target host plan view, then Run."));
                    RunCompleted?.Invoke(new RunSummary());
                    return;
                }

                var engine = new RefSectionHeadEngine(
                    doc,
                    new SectionOriginService(),
                    new ClashAvoidanceService(),
                    new SectionPlacementService(doc));

                engine.LogEmitted += e => LogEmitted?.Invoke(e);
                engine.ProgressChanged += (c, t) => ProgressChanged?.Invoke(c, t);

                var summary = engine.Run(
                    request.SelectedTypes, request.Mappings, activeView.Id,
                    request.SectionViewFamilyTypeId, request.TailLengthFeet);

                RunCompleted?.Invoke(summary);
            }
            catch (OperationCanceledException)
            {
                LogEmitted?.Invoke(new LogEntry(LogLevel.Warning, "Cancelled by user. No reference sections were placed."));
                RunCompleted?.Invoke(new RunSummary());
                throw; // ToolGuard treats it as a user cancel
            }
            catch (Exception ex)
            {
                RunFaulted?.Invoke(ex);
            }
        }

        public string GetName() => "Place Reference Section Heads";
    }
}
