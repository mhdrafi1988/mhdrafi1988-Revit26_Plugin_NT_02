using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.DetailLineManager.V001.Infrastructure
{
    /// <summary>
    /// Runs one model change as a single <see cref="Transaction"/> inside a <see cref="TransactionGroup"/>
    /// named after the action, so it is one Ctrl+Z step in Revit. Any exception or Revit error rolls
    /// everything back; partial work is never committed.
    /// </summary>
    public static class TransactionRunner
    {
        /// <summary>
        /// Runs <paramref name="body"/>. Returns true when it committed. Returns false (logged) when
        /// Revit rolled it back. Rethrows exceptions from <paramref name="body"/> after rolling back.
        /// </summary>
        public static bool Run(Document doc, string name, Action body, Action<LogLevel, string> log)
        {
            using var group = new TransactionGroup(doc, name);
            if (group.Start() != TransactionStatus.Started)
                throw new InvalidOperationException($"Revit could not start \"{name}\". Close any open dialog or edit mode and try again.");

            var policy = new FailurePolicy();
            using (var t = new Transaction(doc, name))
            {
                var options = t.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(policy);
                options.SetClearAfterRollback(true);
                t.SetFailureHandlingOptions(options);

                if (t.Start() != TransactionStatus.Started)
                {
                    group.RollBack();
                    throw new InvalidOperationException($"Revit could not start \"{name}\".");
                }

                try
                {
                    body();
                }
                catch
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    if (group.HasStarted() && !group.HasEnded()) group.RollBack();
                    throw;
                }

                var status = t.Commit();
                foreach (var w in policy.Warnings.Distinct().Take(10))
                    log(LogLevel.Warning, "Revit warning: " + w);

                if (status != TransactionStatus.Committed)
                {
                    if (group.HasStarted() && !group.HasEnded()) group.RollBack();
                    string why = policy.Errors.Count > 0 ? string.Join(" ", policy.Errors.Distinct().Take(3)) : $"status {status}";
                    log(LogLevel.Error, $"Revit rolled back \"{name}\": {why} Nothing was changed.");
                    return false;
                }
            }

            group.Assimilate();
            return true;
        }

        /// <summary>
        /// Runs <paramref name="body"/> in a transaction that is always rolled back, to measure what a
        /// change would do (for example which elements a delete removes) without changing the model.
        /// Returns <paramref name="body"/>'s result, or default when Revit refuses to start the transaction.
        /// </summary>
        public static T RunAndRollBack<T>(Document doc, string name, Func<T> body)
        {
            using var t = new Transaction(doc, name);
            var options = t.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new FailurePolicy());
            options.SetClearAfterRollback(true);
            t.SetFailureHandlingOptions(options);
            if (t.Start() != TransactionStatus.Started) return default;
            try
            {
                return body();
            }
            finally
            {
                if (t.HasStarted() && !t.HasEnded()) t.RollBack();
            }
        }

        /// <summary>
        /// Dismisses warnings (collected for the log) and turns any error into a full rollback.
        /// </summary>
        private sealed class FailurePolicy : IFailuresPreprocessor
        {
            public List<string> Warnings { get; } = new();
            public List<string> Errors { get; } = new();

            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                foreach (var message in accessor.GetFailureMessages())
                {
                    string text = message.GetDescriptionText();
                    if (message.GetSeverity() == FailureSeverity.Warning)
                    {
                        Warnings.Add(text);
                        accessor.DeleteWarning(message);
                    }
                    else
                    {
                        Errors.Add(text);
                    }
                }
                return Errors.Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }
    }
}
