#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.RoofNameBuilder.V002.Core.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofNameBuilder.V002.Infrastructure.ExternalEvents
{
    /// <summary>
    /// Runs on Revit's API thread. Wraps the whole batch in one TransactionGroup, with each
    /// row's rename in its own Transaction that commits or rolls back independently — the
    /// established multi-item batch pattern (ARCHITECTURE.md §5): the group is Assimilate()'d as
    /// long as at least one item succeeded, since RollBack() on the group would undo every item.
    ///
    /// V002 changes vs V001:
    /// - Execute goes through Shared ToolGuard.RunHandler, so an unexpected error is logged and shown
    ///   instead of reaching Revit's idling loop.
    /// - Works on <see cref="TargetDocument"/> (the document the window was opened on), not
    ///   app.ActiveUIDocument — the window is modeless, so the user may have switched documents.
    /// - OnCompleted always fires (try/finally), so the window never gets stuck in "running".
    /// - Revit warnings raised by a row's commit (e.g. duplicate Mark values) are captured into that
    ///   row's result and dismissed, instead of interrupting a long batch.
    /// </summary>
    public class RoofNameBuilderEventHandler : IExternalEventHandler
    {
        public RenamePayload? PendingPayload { get; set; }
        public Document? TargetDocument { get; set; }
        public Action<List<RenameRowResult>>? OnCompleted { get; set; }

        public void Execute(UIApplication app) => ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            var payload = PendingPayload;
            var doc = TargetDocument;
            var onCompleted = OnCompleted;
            PendingPayload = null;

            var results = new List<RenameRowResult>();
            var finished = false;

            try
            {
                if (payload == null) { finished = true; return; }

                if (doc == null || !doc.IsValidObject)
                {
                    results.AddRange(payload.Items.Select(i => Fail(i, "The document this window was opened on is no longer open.")));
                    finished = true;
                    return;
                }

                using var group = new TransactionGroup(doc, "Roof Name Builder — Bulk Rename");
                group.Start();

                var anySucceeded = false;

                foreach (var item in payload.Items)
                {
                    var collector = new WarningCollector();
                    using var t = new Transaction(doc, $"Rename roof {item.ElementId}");
                    var options = t.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(collector);
                    t.SetFailureHandlingOptions(options);

                    try
                    {
                        t.Start();

                        var element = doc.GetElement(new ElementId(item.ElementId))
                            ?? throw new InvalidOperationException("Element no longer exists in the model.");

                        if (item.IsType)
                        {
                            // RoofType rename — global: every placed instance of this type picks it up.
                            element.Name = item.NewName;
                        }
                        else
                        {
                            // Roof instances have no settable Name; write into the configured
                            // instance parameter instead (default "Mark").
                            var p = element.LookupParameter(item.InstanceParameterName)
                                ?? throw new InvalidOperationException(
                                    $"Parameter '{item.InstanceParameterName}' not found on this roof.");
                            if (p.IsReadOnly)
                                throw new InvalidOperationException($"Parameter '{item.InstanceParameterName}' is read-only.");
                            if (p.StorageType != StorageType.String)
                                throw new InvalidOperationException($"Parameter '{item.InstanceParameterName}' is not a text parameter.");
                            p.Set(item.NewName);
                        }

                        var status = t.Commit();
                        if (status != TransactionStatus.Committed)
                            throw new InvalidOperationException($"Revit did not commit the change ({status}).");

                        results.Add(new RenameRowResult
                        {
                            ElementId = item.ElementId,
                            Success = true,
                            Message = $"Renamed → {item.NewName}",
                            Warnings = collector.Messages
                        });
                        anySucceeded = true;
                    }
                    catch (Exception ex)
                    {
                        if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                        results.Add(Fail(item, ex.Message));
                    }
                }

                if (anySucceeded) group.Assimilate();
                else group.RollBack();

                finished = true;
            }
            finally
            {
                if (!finished && payload != null)
                {
                    // Something outside the per-row try failed; the group was disposed (rolled back),
                    // so nothing from this batch is in the model.
                    results = payload.Items.Select(i => Fail(i, "Batch aborted — no changes were kept.")).ToList();
                }
                onCompleted?.Invoke(results);
            }
        }

        private static RenameRowResult Fail(RenameItem item, string message) =>
            new() { ElementId = item.ElementId, Success = false, Message = message };

        public string GetName() => "Roof Name Builder — Rename Handler";

        /// <summary>Records and dismisses warning-level failures; errors are left for Revit to handle (row rolls back).</summary>
        private sealed class WarningCollector : IFailuresPreprocessor
        {
            public List<string> Messages { get; } = new();

            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                foreach (var f in failuresAccessor.GetFailureMessages())
                {
                    if (f.GetSeverity() != FailureSeverity.Warning) continue;
                    Messages.Add(f.GetDescriptionText());
                    failuresAccessor.DeleteWarning(f);
                }
                return FailureProcessingResult.Continue;
            }
        }
    }
}
