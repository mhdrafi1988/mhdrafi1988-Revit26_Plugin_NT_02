using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.SectionViewAutoTagger.V005
{
    /// <summary>
    /// Handles the batch tag-placement Run. One Transaction wraps the entire
    /// worklist (one logical operation = the whole batch), per suite
    /// convention — never one transaction per element or per view.
    /// </summary>
    public class PlaceTagsEventHandler : IExternalEventHandler
    {
        public IReadOnlyList<WorklistEntry> Worklist { get; set; }
        public TagPlacementSettings Settings { get; set; }

        /// <summary>Sink for real-time log lines during Execute — wired to the ViewModel's ObservableCollection via captured Dispatcher.</summary>
        public Action<LogLevel, string> LogSink { get; set; }

        public RunResult Result { get; private set; }
        public event Action Completed;

        private readonly SectionViewAutoTaggerEngine _engine = new();

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                LogSink?.Invoke(LogLevel.Error, "No active document. Run aborted.");
                Result = new RunResult(new List<TagResult>());
                Completed?.Invoke();
                return;
            }

            void Log(LogLevel level, string message) => LogSink?.Invoke(level, message);

            using (var tx = new Transaction(doc, "Section View Auto Tagger — Place Tags"))
            {
                try
                {
                    if (tx.Start() != TransactionStatus.Started)
                    {
                        Log(LogLevel.Error, "Failed to start transaction. Run aborted.");
                        Result = new RunResult(new List<TagResult>());
                        return;
                    }

                    Result = _engine.RunBatch(doc, Worklist, Settings, Log);

                    if (tx.Commit() != TransactionStatus.Committed)
                    {
                        Log(LogLevel.Error, "Transaction failed to commit. Changes rolled back.");
                        Result = new RunResult(new List<TagResult>());
                    }
                    else
                    {
                        Log(LogLevel.Success, $"Batch complete — {Result}");
                    }
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                        tx.RollBack();

                    Log(LogLevel.Error, $"Unexpected error during tag placement: {ex.Message}. Transaction rolled back.");
                    Result = new RunResult(new List<TagResult>());
                }
                finally
                {
                    Completed?.Invoke();
                }
            }
        }

        public string GetName() => "SectionViewAutoTagger Place Tags Handler";
    }
}
