using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.Core.Engine;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.Core.Models;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.Core.Services;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.Infrastructure.ExternalEvents
{
    /// <summary>
    /// Single IExternalEventHandler/ExternalEvent pair for this tool (per suite
    /// convention -- one handler per distinct Revit-side action; this tool has one:
    /// "Create Detail Lines"). ExternalEvent.Create() is called from MainWindow's
    /// constructor (an active API context), never lazily inside Execute().
    ///
    /// Transaction strategy (spec Section 25): one Transaction wraps ALL enabled
    /// mappings for a single Run -- not one transaction per mapping or per element,
    /// so the entire run is a single Undo step. Per-element failures are caught
    /// inside ProfileProcessingEngine/LinearProcessingEngine and reported, not
    /// allowed to abort the whole transaction (spec Section 26: "one element cannot
    /// be processed, continue with the remaining elements").
    /// </summary>
    public class CreateDetailLinesEventHandler : IExternalEventHandler
    {
        /// <summary>Set by the ViewModel immediately before calling event.Raise().</summary>
        public CreateDetailLinesRequest? PendingRequest { get; set; }

        /// <summary>Invoked on the Revit API thread once processing completes
        /// (success or failure) so the ViewModel can update UI-bound state.
        /// ViewModel must marshal back to the UI thread itself if needed (Revit's
        /// ExternalEvent already runs Execute on the valid API/UI thread, so direct
        /// property updates on ObservableObject are safe here without Dispatcher.Invoke,
        /// consistent with the rest of the suite's ExternalEvent pattern).</summary>
        public Action<ProcessingResult>? OnComplete { get; set; }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            var request = PendingRequest;
            PendingRequest = null;

            if (request == null) return;

            var overallResult = new ProcessingResult();
            var stopwatch = Stopwatch.StartNew();

            UIDocument uiDoc = app.ActiveUIDocument;
            Document hostDoc = uiDoc.Document;
            View activeView = uiDoc.ActiveView;

            var profileEngine = new ProfileProcessingEngine();
            var linearEngine = new LinearProcessingEngine();
            var pointEngine = new PointProcessingEngine();
            var isolatedCleanup = new IsolatedElementCleanupService();
            var allCreatedGroups = new List<CreatedElementGroup>();

            using (TransactionGroup tg = new TransactionGroup(hostDoc, "Linked Detail Line Generator — Create Detail Lines"))
            {
                tg.Start();

                using (Transaction tx = new Transaction(hostDoc, "Create Detail Lines from Linked Model"))
                {
                    try
                    {
                        tx.Start();

                        foreach (var mappingGroup in request.EnabledMappings.GroupBy(m => m.LinkInstanceId))
                        {
                            RevitLinkInstance? linkInstance = new FilteredElementCollector(hostDoc)
                                .OfClass(typeof(RevitLinkInstance))
                                .Cast<RevitLinkInstance>()
                                .FirstOrDefault(li => li.Id.Value == mappingGroup.Key);

                            if (linkInstance == null)
                            {
                                request.OnLog?.Invoke($"Link instance {mappingGroup.Key} not found in host document — {mappingGroup.Count()} mapping(s) skipped.", LogSeverity.Error);
                                continue;
                            }

                            foreach (var mapping in mappingGroup)
                            {
                                // Global Override (Section 3 master toggle): applied here,
                                // right at the point of use, rather than by overwriting the
                                // mapping row's own stored DetailLineStyleName/ColorName —
                                // so turning the override back off leaves every row's
                                // individual choice untouched. Safe to mutate-then-restore
                                // on the shared ElementMapping instance because this whole
                                // loop runs synchronously on the Revit API thread; nothing
                                // else can observe the mapping mid-flip.
                                string originalLineStyleName = mapping.DetailLineStyleName;
                                string originalColorName = mapping.ColorName;

                                if (request.GlobalOverride.IsEnabled)
                                {
                                    if (!string.IsNullOrWhiteSpace(request.GlobalOverride.LineStyleName))
                                        mapping.DetailLineStyleName = request.GlobalOverride.LineStyleName;
                                    mapping.ColorName = request.GlobalOverride.ColorName;
                                }

                                try
                                {
                                    // All three representation groups implemented as of
                                    // Phase 4.
                                    MappingProcessingResult mapResult;

                                    if (mapping.Group == RepresentationGroup.Profile)
                                    {
                                        mapResult = profileEngine.ProcessMapping(
                                            hostDoc, activeView, linkInstance, mapping,
                                            request.ProcessingBoundary, request.ProcessingScope,
                                            request.ComplexCurveSettings, request.OnLog);
                                    }
                                    else if (mapping.Group == RepresentationGroup.Linear)
                                    {
                                        mapResult = linearEngine.ProcessMapping(
                                            hostDoc, activeView, linkInstance, mapping,
                                            request.ProcessingBoundary, request.ProcessingScope,
                                            request.ComplexCurveSettings, request.OnLog);
                                    }
                                    else if (request.ProcessingScope.RemovePointMarkers)
                                    {
                                        // Cleanup toggle (Section 5): skip Point-group mappings
                                        // entirely for this run -- no markers created at all,
                                        // regardless of Section 2c selections.
                                        request.OnLog?.Invoke($"'{mapping.CategoryName}/{mapping.TypeName}': skipped — 'Remove Point Markers' is enabled.", LogSeverity.Info);
                                        mapResult = new MappingProcessingResult { MappingId = mapping.MappingId };
                                    }
                                    else
                                    {
                                        mapResult = pointEngine.ProcessMapping(
                                            hostDoc, activeView, linkInstance, mapping,
                                            request.ProcessingBoundary, request.ProcessingScope,
                                            request.ComplexCurveSettings, request.CircleMarkerSettings,
                                            request.RectangleMarkerSettings, request.ActualProfileSettings, request.OnLog);
                                    }

                                    overallResult.ElementsFound += mapResult.ElementsFound;
                                    overallResult.ElementsProcessed += mapResult.ElementsProcessed;
                                    overallResult.ElementsSkipped += mapResult.ElementsSkipped;
                                    overallResult.DetailLinesCreated += mapResult.DetailLinesCreated;
                                    overallResult.FilledRegionsCreated += mapResult.FilledRegionsCreated;
                                    overallResult.Errors.AddRange(mapResult.Errors);
                                    allCreatedGroups.AddRange(mapResult.CreatedGroups);
                                }
                                finally
                                {
                                    mapping.DetailLineStyleName = originalLineStyleName;
                                    mapping.ColorName = originalColorName;
                                }
                            }
                        }

                        if (request.ProcessingScope.RemoveIsolatedLinesAndPoints)
                        {
                            double toleranceFeet = request.ProcessingScope.LineJoinToleranceMm / 304.8;
                            CleanupResult cleanup = isolatedCleanup.RemoveIsolated(
                                hostDoc, allCreatedGroups, toleranceFeet,
                                msg => request.OnLog?.Invoke(msg, LogSeverity.Warning));

                            overallResult.DetailLinesCreated -= cleanup.LinesRemoved;
                            overallResult.DetailLinesCreated -= cleanup.MarkerCurvesRemoved;

                            request.OnLog?.Invoke(
                                $"Cleanup: removed {cleanup.LinesRemoved} isolated line(s) and {cleanup.MarkersRemoved} isolated point marker(s).",
                                LogSeverity.Info);
                        }

                        TransactionStatus status = tx.Commit();
                        if (status != TransactionStatus.Committed)
                        {
                            overallResult.CriticalErrors++;
                            request.OnLog?.Invoke($"Transaction did not commit (status: {status}). Rolling back.", LogSeverity.Error);
                            tg.RollBack();
                            stopwatch.Stop();
                            overallResult.Duration = stopwatch.Elapsed;
                            OnComplete?.Invoke(overallResult);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        overallResult.CriticalErrors++;
                        request.OnLog?.Invoke($"Critical error during processing — transaction rolled back: {ex.Message}", LogSeverity.Error);
                        if (tx.GetStatus() == TransactionStatus.Started)
                            tx.RollBack();
                        tg.RollBack();
                        stopwatch.Stop();
                        overallResult.Duration = stopwatch.Elapsed;
                        OnComplete?.Invoke(overallResult);
                        return;
                    }
                }

                tg.Assimilate(); // merges into a single Undo entry
            }

            stopwatch.Stop();
            overallResult.Duration = stopwatch.Elapsed;

            request.OnLog?.Invoke(
                $"Complete — {overallResult.ElementsProcessed} processed | {overallResult.DetailLinesCreated} Detail Lines created | {overallResult.FilledRegionsCreated} Filled Regions created | {overallResult.ElementsSkipped} skipped | {overallResult.CriticalErrors} critical errors",
                overallResult.CriticalErrors > 0 ? LogSeverity.Warning : LogSeverity.Success);

            OnComplete?.Invoke(overallResult);
        }

        public string GetName() => "Linked Detail Line Generator — Create Detail Lines";
    }

    /// <summary>Everything the event handler needs, packaged by the ViewModel before
    /// raising the event -- keeps Execute() free of any WPF/ViewModel dependency.</summary>
    public class CreateDetailLinesRequest
    {
        public List<ElementMapping> EnabledMappings { get; set; } = new();
        public List<XYZ> ProcessingBoundary { get; set; } = new();
        public ProcessingScope ProcessingScope { get; set; } = new();
        public ComplexCurveSettings ComplexCurveSettings { get; set; } = new();
        public CircleMarkerSettings CircleMarkerSettings { get; set; } = new();
        public RectangleMarkerSettings RectangleMarkerSettings { get; set; } = new();
        public ActualProfileSettings ActualProfileSettings { get; set; } = new();
        public GlobalOverrideSettings GlobalOverride { get; set; } = new();
        public Action<string, LogSeverity>? OnLog { get; set; }
    }
}
