using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.Shared.ViewModels
{
    /// <summary>
    /// Shared base for all tool ViewModels.
    /// Provides: log collection + helpers, log commands, and the three
    /// ToolWindowShell-bound status properties (IsRunning / Progress / SummaryText).
    /// </summary>
    public abstract partial class ToolViewModelBase : ObservableObject
    {
        private const int MaxLogEntries = 2000;

        // ── ToolWindowShell status strip ──────────────────────────────────────
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private double _progress;
        [ObservableProperty] private string _summaryText = string.Empty;

        // ── Log ───────────────────────────────────────────────────────────────
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        /// <summary>Bound from the log ListBox's SelectedItems in code-behind;
        /// set before calling CopySelectedLogCommand.</summary>
        public IList<LogEntry> SelectedLogEntries { get; set; } = new List<LogEntry>();

        // ── Log commands ──────────────────────────────────────────────────────
        [RelayCommand]
        private void ClearLog() => OnClearLog();

        /// <summary>Override to add tool-specific reset alongside the log clear
        /// (e.g. reset metrics). Always call base.OnClearLog() to clear the list.</summary>
        protected virtual void OnClearLog() => LogEntries.Clear();

        [RelayCommand]
        private void CopyAllLog()
        {
            var text = string.Join(System.Environment.NewLine, LogEntries.Select(e => e.ToString()));
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); } catch { /* clipboard busy */ }
        }

        [RelayCommand]
        private void CopySelectedLog()
        {
            if (SelectedLogEntries == null || SelectedLogEntries.Count == 0) return;
            var text = string.Join(System.Environment.NewLine, SelectedLogEntries.Select(e => e.ToString()));
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); } catch { /* clipboard busy */ }
        }

        // ── Cancellable run ───────────────────────────────────────────────────
        private ToolCancellation _activeRun;

        /// <summary>The tool window; set by the window's code-behind so the Yes/No dialog and Escape key use it. May stay null.</summary>
        public Window OwnerWindow { get; set; }

        /// <summary>
        /// Starts a cancellable run: sets <see cref="IsRunning"/>, routes <c>Report</c> to
        /// <see cref="Progress"/> / <see cref="SummaryText"/> and lets <see cref="CancelRunCommand"/> and the
        /// shell's Cancel button stop it. Call on the Revit thread; always pair with <see cref="EndRun"/> in a <c>finally</c>.
        /// </summary>
        protected ToolCancellation BeginRun(ToolInfo tool)
        {
            _activeRun = ToolCancellation.Begin(tool, OwnerWindow, (percent, text) =>
            {
                Progress = percent;
                SummaryText = text;
            });
            Progress = 0;
            IsRunning = true;
            return _activeRun;
        }

        /// <summary>Ends the run started by <see cref="BeginRun"/> (clears <see cref="IsRunning"/>, releases the window).</summary>
        protected void EndRun(ToolCancellation run)
        {
            IsRunning = false;
            run?.Dispose();
            if (ReferenceEquals(_activeRun, run)) _activeRun = null;
        }

        /// <summary>Writes "Cancelled by user" to the log after a confirmed cancel (called by <see cref="ToolCancellation"/>).</summary>
        public void NotifyCancelled(int done, int total) =>
            AddLog(LogLevel.Warning, total > 0
                ? $"Cancelled by user after {done} of {total}. Model changes made by this run were undone."
                : "Cancelled by user. Model changes made by this run were undone.");

        /// <summary>Cancel button: asks the active run to cancel (a Yes/No confirmation follows).</summary>
        [RelayCommand]
        private void CancelRun() => _activeRun?.RequestCancel();

        // ── Log helper ────────────────────────────────────────────────────────
        protected void AddLog(LogLevel level, string message)
        {
            LogEntries.Add(new LogEntry(level, message));
            while (LogEntries.Count > MaxLogEntries)
                LogEntries.RemoveAt(0);
        }
    }
}
