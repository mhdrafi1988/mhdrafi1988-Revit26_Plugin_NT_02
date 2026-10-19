using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.BulkRename.V001.Core.Models;
using Revit26_Plugin_Revised.BulkRename.V001.Core.Services;
using Revit26_Plugin_Revised.BulkRename.V001.Core.Sources;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.BulkRename.V001.UI.ViewModels
{
    /// <summary>
    /// View model of the Bulk Rename window. It ties the parts together: the user picks what to rename
    /// (<see cref="Sources"/>), builds new names with the <see cref="Rules"/> or by typing in the
    /// <see cref="Table"/>, and applies them. All reads and writes of the model go through a
    /// <see cref="RevitJobQueue"/>, so they run in a Revit API context.
    /// </summary>
    public partial class BulkRenameViewModel : ToolViewModelBase
    {
        private readonly Document _doc;
        private readonly RevitJobQueue _jobs;
        private readonly Dispatcher _dispatcher;
        private readonly Dictionary<string, List<RenameRowViewModel>> _cache = new();

        /// <summary>The kinds of item that can be renamed.</summary>
        public IReadOnlyList<RenameSource> Sources { get; }

        /// <summary>The names grid, its filter and counters.</summary>
        public RenameTableViewModel Table { get; }

        /// <summary>The rules card.</summary>
        public RenameRulesViewModel Rules { get; }

        /// <summary>What is being renamed.</summary>
        [ObservableProperty] private RenameSource selectedSource;

        /// <summary>One-line explanation of the selected kind of item.</summary>
        public string SourceDescription => SelectedSource?.Description ?? string.Empty;

        /// <summary>
        /// Asks the user a yes/no question and returns true for yes. The window replaces it with a dialog
        /// it owns, so the question always opens in front of the window.
        /// </summary>
        public Func<string, bool> Confirm { get; set; }

        /// <summary>
        /// Creates the view model for <paramref name="doc"/> and starts listing the first kind of item.
        /// Must be created in a Revit API context (while the command runs), because it creates the
        /// external event.
        /// </summary>
        public BulkRenameViewModel(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _dispatcher = Dispatcher.CurrentDispatcher;
            Confirm = AskWithoutOwner;

            _jobs = new RevitJobQueue("Bulk Rename V001");
            _jobs.BusyChanged += (_, _) => OnUi(() =>
            {
                IsRunning = _jobs.IsBusy;
                ApplyCommand.NotifyCanExecuteChanged();
            });

            Sources = RenameSourceCatalog.CreateAll();
            Table = new RenameTableViewModel(() => ApplyCommand.NotifyCanExecuteChanged());
            Rules = new RenameRulesViewModel(ApplyRule, Report);

            // Everything the callbacks below touch is ready before the first source is selected.
            SelectedSource = Sources[0];
        }

        partial void OnSelectedSourceChanged(RenameSource value)
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

            ShowRows(new List<RenameRowViewModel>());
            Run($"Loading {source.DisplayName.ToLowerInvariant()}…", app =>
            {
                var doc = DocumentFor(app);
                if (doc != null) LoadInto(doc, source);
            });
        }

        // Runs in the Revit API context: lists the items, then shows them if the user is still on this kind.
        private void LoadInto(Document doc, RenameSource source)
        {
            var rows = source.Load(doc, LogFromRevit).Select(item => new RenameRowViewModel(item)).ToList();

            OnUi(() =>
            {
                _cache[source.Key] = rows;
                if (ReferenceEquals(SelectedSource, source)) ShowRows(rows);
            });
        }

        private void ShowRows(List<RenameRowViewModel> rows)
        {
            Table.Show(rows);
            SummaryText = rows.Count == 0 ? string.Empty : $"{rows.Count} item(s) listed.";
        }

        /// <summary>Lists the selected kind of item again, discarding names that were not applied.</summary>
        [RelayCommand]
        private void Reload()
        {
            if (SelectedSource == null) return;

            if (Table.HasUnappliedEdits && !Confirm("Reloading discards the new names you have not applied. Continue?"))
                return;

            _cache.Remove(SelectedSource.Key);
            LoadSelected(force: true);
        }

        // ── Rules ─────────────────────────────────────────────────────────────

        // Runs one rule on the ticked, visible rows. Every new name is computed first and only then
        // assigned, so a rule that fails (for example a regex that times out) changes nothing.
        private void ApplyRule(string label, Func<string, int, string> transform)
        {
            var targets = Table.VisibleTargets();
            if (targets.Count == 0)
            {
                SummaryText = "Tick at least one visible row first.";
                return;
            }

            List<string> updated;
            try
            {
                updated = targets.Select((row, index) => transform(row.NewName, index)).ToList();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is RegexMatchTimeoutException)
            {
                Report($"{label} failed: {ex.Message}", true);
                return;
            }

            int changed = Table.Assign(targets, updated);
            SummaryText = $"{label}: {changed} of {targets.Count} name(s) changed.";
        }

        // Shows a message in the status line, and in the log as a warning when asked.
        private void Report(string message, bool log)
        {
            SummaryText = message;
            if (log) AddLog(LogLevel.Warning, message);
        }

        // ── Selection ─────────────────────────────────────────────────────────

        /// <summary>Ticks every visible row that can be renamed.</summary>
        [RelayCommand]
        private void SelectAll() => Table.SetTicks(true);

        /// <summary>Unticks every visible row.</summary>
        [RelayCommand]
        private void SelectNone() => Table.SetTicks(false);

        /// <summary>Puts every visible row's new name back to its current name.</summary>
        [RelayCommand]
        private void RevertNames() => SummaryText = $"Reverted {Table.RevertVisible()} name(s).";

        // ── Apply ─────────────────────────────────────────────────────────────

        /// <summary>Confirms and renames the ready rows in one transaction.</summary>
        [RelayCommand(CanExecute = nameof(CanApply))]
        private void Apply()
        {
            var source = SelectedSource;
            if (source == null) return;

            // Captured now, before asking: the run applies exactly the names the user confirms,
            // whatever happens in the window afterwards.
            var requests = Table.ReadyRows()
                .Select(row => new RenameRequest(row.Item.Id, row.CurrentName, row.FinalName))
                .ToList();
            if (requests.Count == 0) return;

            string kind = source.DisplayName.ToLowerInvariant();
            string question = RenameConfirmation.Build(kind, requests.Select(r => (r.CurrentName, r.NewName)).ToList());
            if (!Confirm(question)) return;

            Run($"Renaming {requests.Count} {kind}…", app =>
            {
                var doc = DocumentFor(app);
                if (doc == null) return;

                AddLog(LogLevel.Info, $"Renaming {requests.Count} {kind}…");
                var result = new RenameService().Apply(doc, source, requests, $"Bulk Rename: {source.DisplayName}");

                // The transaction is over, so the outcomes are final: only now are they logged.
                foreach (var outcome in result.Outcomes)
                {
                    if (outcome.Renamed) AddLog(LogLevel.Success, $"Renamed '{outcome.From}' to '{outcome.To}'.");
                    else AddLog(LogLevel.Warning, $"Skipped '{outcome.From}': {outcome.Reason}");
                }
                AddLog(result.Renamed > 0 ? LogLevel.Success : LogLevel.Warning, "— " + result);

                OnUi(() => LogExportService.AutoSave(LogEntries, ToolCatalog.BulkRename.Title));
                RefreshAfterRename(doc, source, result);
            });
        }

        private bool CanApply() => !IsRunning && Table != null && Table.ChangeCount > 0 && Table.ProblemCount == 0;

        // Lists the items again so the grid shows the new names. The renames are already committed, so a
        // failure here must not be reported as "nothing was changed".
        private void RefreshAfterRename(Document doc, RenameSource source, RenameResult result)
        {
            try
            {
                LoadInto(doc, source);
                SummaryText = result.ToString();
            }
            catch (Exception ex)
            {
                SummaryText = $"{result} The list could not be refreshed: click Reload.";
                AddLog(LogLevel.Error, $"The renames were kept, but the list could not be refreshed: {ex.Message} Click Reload.");
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
            => MessageBox.Show(question, ToolCatalog.BulkRename.Title, MessageBoxButton.YesNo, MessageBoxImage.Question)
               == MessageBoxResult.Yes;

        private void LogFromRevit(LogEntry entry) => OnUi(() => AddLog(entry.Level, entry.Message));

        private void OnUi(Action action)
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.Invoke(action);
        }
    }
}
