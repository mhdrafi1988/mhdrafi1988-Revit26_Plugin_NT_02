using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.BulkDelete.V001.Core.Models;
using Revit26_Plugin_Revised.BulkDelete.V001.Core.Services;
using Revit26_Plugin_Revised.BulkDelete.V001.Core.Sources;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.BulkDelete.V001.UI.ViewModels
{
    /// <summary>
    /// View model of the Bulk Delete window. The user picks what to delete (<see cref="Sources"/>), ticks
    /// rows in the <see cref="Table"/>, and deletes them after a confirmation. All reads and writes of the
    /// model go through a <see cref="RevitJobQueue"/>, so they run in a Revit API context.
    /// </summary>
    public partial class BulkDeleteViewModel : ToolViewModelBase
    {
        private readonly Document _doc;
        private readonly RevitJobQueue _jobs;
        private readonly Dispatcher _dispatcher;
        private readonly Dictionary<string, List<DeleteRowViewModel>> _cache = new();

        /// <summary>The kinds of item that can be deleted.</summary>
        public IReadOnlyList<DeleteSource> Sources { get; }

        /// <summary>The items grid, its filter and counters.</summary>
        public DeleteTableViewModel Table { get; }

        /// <summary>What is being deleted.</summary>
        [ObservableProperty] private DeleteSource selectedSource;

        /// <summary>One-line explanation of the selected kind of item.</summary>
        public string SourceDescription => SelectedSource?.Description ?? string.Empty;

        /// <summary>
        /// Asks the user a yes/no question and returns true for yes. The window replaces it with a dialog
        /// it owns, so the question always opens in front of the window. The default answer is No.
        /// </summary>
        public Func<string, bool> Confirm { get; set; }

        /// <summary>
        /// Creates the view model for <paramref name="doc"/> and starts listing the first kind of item.
        /// Must be created in a Revit API context (while the command runs), because it creates the
        /// external event.
        /// </summary>
        public BulkDeleteViewModel(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _dispatcher = Dispatcher.CurrentDispatcher;
            Confirm = AskWithoutOwner;

            _jobs = new RevitJobQueue("Bulk Delete V001");
            _jobs.BusyChanged += (_, _) => OnUi(() =>
            {
                IsRunning = _jobs.IsBusy;
                DeleteCommand.NotifyCanExecuteChanged();
            });

            Sources = DeleteSourceCatalog.CreateAll();
            Table = new DeleteTableViewModel(() => DeleteCommand.NotifyCanExecuteChanged());

            // Everything the callbacks below touch is ready before the first source is selected.
            SelectedSource = Sources[0];
        }

        partial void OnSelectedSourceChanged(DeleteSource value)
        {
            OnPropertyChanged(nameof(SourceDescription));
            LoadSelected(force: false);
        }

        /// <summary>Releases the external event; called when the window closes.</summary>
        public void Shutdown() => _jobs.Dispose();

        // ── Loading ───────────────────────────────────────────────────────────

        private void LoadSelected(bool force)
        {
            var source = SelectedSource;
            if (source == null) return;

            if (!force && _cache.TryGetValue(source.Key, out var cached))
            {
                ShowRows(cached);
                return;
            }

            ShowRows(new List<DeleteRowViewModel>());
            Run($"Loading {source.DisplayName.ToLowerInvariant()}…", app =>
            {
                var doc = DocumentFor(app);
                if (doc != null) LoadInto(doc, source);
            });
        }

        // Runs in the Revit API context: lists the items, then shows them if the user is still on this kind.
        private void LoadInto(Document doc, DeleteSource source)
        {
            var rows = source.Load(doc, LogFromRevit).Select(item => new DeleteRowViewModel(item)).ToList();

            OnUi(() =>
            {
                _cache[source.Key] = rows;
                if (ReferenceEquals(SelectedSource, source)) ShowRows(rows);
            });
        }

        private void ShowRows(List<DeleteRowViewModel> rows)
        {
            Table.Show(rows);
            SummaryText = rows.Count == 0 ? string.Empty : $"{rows.Count} item(s) listed.";
        }

        /// <summary>Lists the selected kind of item again, clearing the ticks.</summary>
        [RelayCommand]
        private void Reload()
        {
            if (SelectedSource == null) return;

            _cache.Remove(SelectedSource.Key);
            LoadSelected(force: true);
        }

        // ── Selection ─────────────────────────────────────────────────────────

        /// <summary>Ticks every visible row that can be deleted.</summary>
        [RelayCommand]
        private void SelectAll() => Table.SetTicks(true);

        /// <summary>Unticks every visible row.</summary>
        [RelayCommand]
        private void SelectNone() => Table.SetTicks(false);

        /// <summary>Ticks the visible rows that are known to be unused and unticks the others.</summary>
        [RelayCommand]
        private void SelectUnused()
        {
            int ticked = Table.TickUnused();
            SummaryText = ticked == 0
                ? "No item of this kind is known to be unused (usage is only known for line styles)."
                : $"Ticked {ticked} unused item(s).";
        }

        // ── Delete ────────────────────────────────────────────────────────────

        /// <summary>Confirms and deletes the ticked rows in one transaction.</summary>
        [RelayCommand(CanExecute = nameof(CanDelete))]
        private void Delete()
        {
            var source = SelectedSource;
            if (source == null) return;

            // Captured now, before asking: the run deletes exactly the items the user confirms,
            // whatever happens in the window afterwards.
            var requests = Table.TickedRows()
                .Select(row => new DeleteRequest(row.Item.Id, row.Name))
                .ToList();
            if (requests.Count == 0) return;

            string kind = source.DisplayName.ToLowerInvariant();
            string question = DeleteConfirmation.Build(kind, requests.Select(r => r.Name).ToList());
            if (!Confirm(question)) return;

            Run($"Deleting {requests.Count} {kind}…", app =>
            {
                var doc = DocumentFor(app);
                if (doc == null) return;

                AddLog(LogLevel.Info, $"Deleting {requests.Count} {kind}…");
                var result = new DeleteService().Apply(doc, source, requests, $"Bulk Delete: {source.DisplayName}");

                // The transaction is over, so the outcomes are final: only now are they logged.
                foreach (var outcome in result.Outcomes)
                {
                    if (outcome.Deleted) AddLog(LogLevel.Success, $"Deleted '{outcome.Name}'.");
                    else AddLog(LogLevel.Warning, $"Skipped '{outcome.Name}': {outcome.Reason}");
                }
                AddLog(result.Deleted > 0 ? LogLevel.Success : LogLevel.Warning, "— " + result);

                OnUi(() => LogExportService.AutoSave(LogEntries, ToolCatalog.BulkDelete.Title));
                RefreshAfterDelete(doc, source, result);
            });
        }

        private bool CanDelete() => !IsRunning && Table != null && Table.TickedCount > 0;

        // Lists the items again so the grid no longer shows the deleted ones. The deletions are already
        // committed, so a failure here must not be reported as "nothing was changed".
        private void RefreshAfterDelete(Document doc, DeleteSource source, DeleteResult result)
        {
            try
            {
                LoadInto(doc, source);
                SummaryText = result.ToString();
            }
            catch (Exception ex)
            {
                SummaryText = $"{result} The list could not be refreshed: click Reload.";
                AddLog(LogLevel.Error, $"The deletions were kept, but the list could not be refreshed: {ex.Message} Click Reload.");
                ToolGuard.Report(GetType(), ex);
            }
        }

        // ── Revit plumbing ────────────────────────────────────────────────────

        // Queues work for the Revit API context. A failure is logged and reported, and the window never
        // stays stuck on "running".
        private void Run(string summary, Action<UIApplication> body)
        {
            SummaryText = summary;
            if (_jobs.TryRun(body, OnJobFailed)) return;

            SummaryText = "Revit is busy. Close any open dialog and try again.";
            AddLog(LogLevel.Warning, SummaryText);
        }

        private void OnJobFailed(Exception ex)
        {
            OnUi(() =>
            {
                SummaryText = "Stopped, nothing was changed: " + ex.Message;
                AddLog(LogLevel.Error, SummaryText);
            });
            ToolGuard.Report(GetType(), ex);
        }

        // The document this window was opened for, when it is still open and active. Otherwise null, with
        // the reason in the status line and the log.
        private Document DocumentFor(UIApplication app)
        {
            string problem;
            if (!_doc.IsValidObject)
            {
                problem = "The document this window was opened for has been closed. Close this window.";
            }
            else
            {
                var active = app.ActiveUIDocument?.Document;
                if (active != null && active.Equals(_doc)) return active;
                problem = $"The active document changed. Switch back to '{_doc.Title}' to continue.";
            }

            OnUi(() =>
            {
                SummaryText = problem;
                AddLog(LogLevel.Error, problem);
            });
            return null;
        }

        private static bool AskWithoutOwner(string question)
            => MessageBox.Show(question, ToolCatalog.BulkDelete.Title, MessageBoxButton.YesNo,
                   MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

        private void LogFromRevit(LogEntry entry) => OnUi(() => AddLog(entry.Level, entry.Message));

        private void OnUi(Action action)
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.Invoke(action);
        }
    }
}
