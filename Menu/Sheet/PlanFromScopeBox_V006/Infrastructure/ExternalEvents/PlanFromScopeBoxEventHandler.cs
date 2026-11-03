using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.PlanFromScopeBox.V006.Core.Engine;
using Revit26_Plugin_Revised.PlanFromScopeBox.V006.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.PlanFromScopeBox.V006.Infrastructure.ExternalEvents
{
    /// <summary>Marker for queued tool requests processed by the event handler.</summary>
    public interface IToolRequest { }

    /// <summary>Read-only request: measure the selected title block's sheet extents.</summary>
    public sealed class MeasureTitleBlockRequest : IToolRequest
    {
        public ElementId TitleBlockTypeId { get; init; }
        public Action<TitleBlockExtents> OnComplete { get; init; }
    }

    /// <summary>Full generation run request.</summary>
    public sealed class GenerateRequest : IToolRequest
    {
        public GenerationRequest Payload { get; init; }
        public Action<GenerationSummary> OnComplete { get; init; }
    }

    /// <summary>
    /// Orchestrating external-event handler. Requests are IMMUTABLE typed objects placed on
    /// a thread-safe queue and drained in Execute(). This removes the shared-mutable-state
    /// race where a later request (e.g. a title-block measure fired by a ComboBox change)
    /// could overwrite a pending Generate before Revit processed it, leaving the UI stuck
    /// busy. Each request carries its own completion callback.
    /// </summary>
    public sealed class PlanFromScopeBoxEventHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<IToolRequest> _queue = new();

        /// <summary>Shared live log sink (marshalled to UI thread by the ViewModel).</summary>
        public Action<LogLevel, string> Log { get; set; }

        /// <summary>Queues a request. Caller must follow with ExternalEvent.Raise().</summary>
        public void Enqueue(IToolRequest request) => _queue.Enqueue(request);

        /// <summary>Drops all pending requests (used when Raise() is rejected, so a stale
        /// request can't fire unexpectedly on a later raise).</summary>
        public void ClearPending()
        {
            while (_queue.TryDequeue(out _)) { }
        }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null)
            {
                Log?.Invoke(LogLevel.Error, "No active document.");
                ClearPending();
                return;
            }
            Document doc = uidoc.Document;

            // Drain everything queued: multiple Raise() calls can coalesce into one Execute.
            while (_queue.TryDequeue(out IToolRequest request))
            {
                try
                {
                    switch (request)
                    {
                        case MeasureTitleBlockRequest m:
                            RunMeasure(doc, m);
                            break;
                        case GenerateRequest g:
                            RunGenerate(uidoc, doc, g);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log?.Invoke(LogLevel.Error, $"Request failed: {ex.Message}");
                }
            }
        }

        private void RunMeasure(Document doc, MeasureTitleBlockRequest req)
        {
            try
            {
                TitleBlockExtents extents = new TitleBlockMeasurer().Measure(doc, req.TitleBlockTypeId);
                req.OnComplete?.Invoke(extents);
            }
            catch (Exception ex)
            {
                Log?.Invoke(LogLevel.Warning, $"Title-block measure failed — {ex.Message}");
                req.OnComplete?.Invoke(new TitleBlockExtents { Measured = false });
            }
        }

        private void RunGenerate(UIDocument uidoc, Document doc, GenerateRequest req)
        {
            try
            {
                var generator = new PlanSheetGenerator(doc, Log);
                GenerationSummary summary = generator.Run(req.Payload);

                if (req.Payload.OpenSheetsAfter && summary.CreatedSheetIds.Count > 0)
                {
                    foreach (ElementId sheetId in summary.CreatedSheetIds)
                        if (doc.GetElement(sheetId) is ViewSheet vs)
                            uidoc.RequestViewChange(vs);
                }

                req.OnComplete?.Invoke(summary);
            }
            catch (Exception ex)
            {
                Log?.Invoke(LogLevel.Error, $"Run failed: {ex.Message}");
                req.OnComplete?.Invoke(new GenerationSummary());
            }
        }

        public string GetName() => "PlanFromScopeBox.EventHandler";
    }
}
