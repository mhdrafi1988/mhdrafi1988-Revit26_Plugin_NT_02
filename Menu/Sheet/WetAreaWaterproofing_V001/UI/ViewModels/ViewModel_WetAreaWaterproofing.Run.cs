using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Report;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Run;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Infrastructure;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.UI.ViewModels
{
    /// <summary>One line of the result card.</summary>
    /// <param name="Kind">ok, warn or error.</param>
    /// <param name="Text">The line.</param>
    public sealed record ResultLine(string Kind, string Text);

    public sealed partial class ViewModel_WetAreaWaterproofing
    {
        /// <summary>Lines of the result card.</summary>
        public ObservableCollection<ResultLine> ResultLines { get; } = new();

        /// <summary>Earlier runs found in the model, for "Remove a run".</summary>
        public ObservableCollection<RunInfo> Runs { get; } = new();

        [ObservableProperty] private bool _resultVisible;
        [ObservableProperty] private string _resultTitle = string.Empty;
        [ObservableProperty] private string _lastReportPath;
        [ObservableProperty] private bool _removeVisible;
        [ObservableProperty] private RunInfo _selectedRun;

        partial void OnIsBusyChanged(bool value)
        {
            RunCommand.NotifyCanExecuteChanged();
            RefreshCommand.NotifyCanExecuteChanged();
        }

        partial void OnLastReportPathChanged(string value)
        {
            OpenReportCommand.NotifyCanExecuteChanged();
            ExportNowCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedRunChanged(RunInfo value) => ConfirmRemoveRunCommand.NotifyCanExecuteChanged();

        private bool CanRun() => !IsBusy && _ticked.Count > 0;

        /// <summary>Creates the roofs for the ticked rooms.</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            if (string.IsNullOrEmpty(SelectedRoofType))
            {
                AddLog(LogLevel.Error, "Pick a roof type first. The project has none? Load or create one, then press Refresh.");
                return;
            }
            if (NeedsRefresh)
                AddLog(LogLevel.Info, "Sources or repair settings changed: the model is read again with them before the run.");
            if (!PushSettings()) return;

            var keys = _ticked.ToList();
            IsBusy = true;
            AddLog(LogLevel.Info, $"Run started for {keys.Count} room(s).");
            _session.Run(keys, _settings, OnRunDone);
        }

        private void OnRunDone(RunOutcome outcome)
        {
            IsBusy = false;
            ResultLines.Clear();
            if (outcome.Cancelled)
            {
                AddLog(LogLevel.Warning, "Cancelled by user. Nothing was changed.");
                return;
            }
            if (outcome.Error != null)
            {
                AddLog(LogLevel.Error, outcome.Error);
                ResultTitle = "Wet Area Waterproofing — not run";
                ResultLines.Add(new ResultLine("error", outcome.Error));
                ResultVisible = true;
                return;
            }

            _lastReport = outcome.Report;
            ExportNowCommand.NotifyCanExecuteChanged(); // the report exists even when it was not saved
            LastReportPath = outcome.ReportPath;
            if (outcome.Snapshot != null)
            {
                ApplySnapshot(outcome.Snapshot, firstLoad: false);
                NeedsRefresh = false;
            }
            SaveSettings();

            ResultTitle = "Wet Area Waterproofing — done";
            if (outcome.Created > 0) ResultLines.Add(new ResultLine("ok", $"{outcome.Created} waterproofing roof(s) created"));
            if (outcome.Replaced > 0) ResultLines.Add(new ResultLine("ok", $"{outcome.Replaced} roof(s) from an earlier run replaced"));
            if (outcome.Created + outcome.Replaced == 0) ResultLines.Add(new ResultLine("error", "No roof was created. The model was not changed."));
            if (outcome.Skipped > 0) ResultLines.Add(new ResultLine("error", $"{outcome.Skipped} room(s) skipped"));
            if (outcome.ReportPath != null) ResultLines.Add(new ResultLine("ok", "Excel report saved: " + Path.GetFileName(outcome.ReportPath)));
            if (outcome.ReportError != null) ResultLines.Add(new ResultLine("error", "The Excel report could not be saved: " + outcome.ReportError));

            foreach (Issue issue in outcome.Issues.Where(i => i.Severity != "Info"))
            {
                string text = string.IsNullOrEmpty(issue.Number) ? issue.Message : $"Room {issue.Number}: {issue.Message}";
                ResultLines.Add(new ResultLine(issue.Severity == "Error" ? "error" : "warn", text));
                AddLog(issue.Severity == "Error" ? LogLevel.Error : LogLevel.Warning, text);
            }
            AddLog(outcome.Created + outcome.Replaced > 0 ? LogLevel.Success : LogLevel.Warning,
                $"Run {outcome.RunId}: {outcome.Created} created, {outcome.Replaced} replaced, {outcome.Skipped} skipped. One Undo step.");
            ResultVisible = true;
        }

        /// <summary>Closes the result card.</summary>
        [RelayCommand]
        private void CloseResult() => ResultVisible = false;

        private bool HasReport() => _lastReport != null;

        /// <summary>Saves the last run's report again, to the folder in the window.</summary>
        [RelayCommand(CanExecute = nameof(HasReport))]
        private void ExportNow()
        {
            if (!PushSettings()) return;
            try
            {
                LastReportPath = ReportWriter.Write(_lastReport, _settings.ReportFolder);
                AddLog(LogLevel.Success, "Excel report saved: " + LastReportPath);
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, "The Excel report could not be saved: " + ex.Message);
            }
        }

        private bool HasReportFile() => !string.IsNullOrEmpty(LastReportPath) && File.Exists(LastReportPath);

        /// <summary>Opens the last saved report in Excel.</summary>
        [RelayCommand(CanExecute = nameof(HasReportFile))]
        private void OpenReport() => Launch(LastReportPath);

        /// <summary>Opens the report folder in Explorer.</summary>
        [RelayCommand]
        private void OpenReportFolder()
        {
            if (!PushSettings()) return;
            try
            {
                Directory.CreateDirectory(_settings.ReportFolder);
                Launch(_settings.ReportFolder);
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, "The report folder could not be opened: " + ex.Message);
            }
        }

        private void Launch(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"\"{path}\" could not be opened: {ex.Message}");
            }
        }

        // ── Remove a run ────────────────────────────────────────────────────────────────────────

        /// <summary>Lists the earlier runs still in the model.</summary>
        [RelayCommand]
        private void RemoveRun()
        {
            if (IsBusy) return;
            IsBusy = true;
            _session.ListRuns((runs, error) =>
            {
                IsBusy = false;
                if (runs == null)
                {
                    AddLog(LogLevel.Error, "Earlier runs could not be listed: " + error);
                    return;
                }
                Runs.Clear();
                foreach (RunInfo run in runs) Runs.Add(run);
                SelectedRun = Runs.FirstOrDefault();
                if (Runs.Count == 0) AddLog(LogLevel.Info, "No earlier run was found in this model.");
                RemoveVisible = Runs.Count > 0;
            });
        }

        private bool CanConfirmRemove() => SelectedRun != null;

        /// <summary>Deletes everything the selected run created.</summary>
        [RelayCommand(CanExecute = nameof(CanConfirmRemove))]
        private void ConfirmRemoveRun()
        {
            RunInfo run = SelectedRun;
            if (run == null || IsBusy) return;
            RemoveVisible = false;
            IsBusy = true;
            _session.RemoveRun(run.RunId, (deleted, owned, error) =>
            {
                IsBusy = false;
                if (error != null)
                {
                    AddLog(LogLevel.Error, $"Run {run.RunId} was not removed: {error}");
                    return;
                }
                AddLog(deleted > 0 ? LogLevel.Success : LogLevel.Warning,
                    $"Run {run.RunId}: {deleted} element(s) removed"
                    + (owned > 0 ? $", {owned} left because another user owns them." : "."));
                Refresh();
            });
        }

        /// <summary>Closes the "Remove a run" card without removing anything.</summary>
        [RelayCommand]
        private void CancelRemoveRun() => RemoveVisible = false;
    }
}
