// ==============================================
// File: DwgToLinesViewModel.cs
// Layer: UI/ViewModels
// Changes vs V004:
//   FIX  AvailableCads was declared but never populated (dropdown was
//        always empty) â€” now loaded from CadImportCollectorService in
//        the constructor.
//   FIX  LogEntries collection did not exist at all (XAML bound to it
//        anyway, a silent binding failure) and the log callback passed
//        into CadConversionService was a no-op. Both wired to the shared
//        LogEntry model now, matching every other tool in the suite.
//   ADDED Close/Copy All/Copy Selected/Export log commands (log panel
//        convention from MasterGuide.md section 10).
// ==============================================

using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using Revit26_Plugin_Revised.DwgToLines.V007.Core.Models;
using Revit26_Plugin_Revised.DwgToLines.V007.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.DwgToLines.V007.UI.ViewModels
{
    /// <summary>
    /// DWG To Lines window state. Convert runs through <see cref="RevitApiQueue"/>, because the window is
    /// modeless and Revit refuses transactions started from its button clicks.
    /// </summary>
    public partial class DwgToLinesViewModel : ObservableObject, IDisposable
    {
        private readonly UIApplication _uiApp;

        // Runs the conversion in Revit's API context. Created by the constructor, which the command runs.
        private readonly RevitApiQueue _revit;

        public ObservableCollection<CadImportItem> AvailableCads { get; } = new();
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        [ObservableProperty] private CadImportItem selectedCad;
        [ObservableProperty] private PlacementMode placementMode = PlacementMode.SymbolicOnly;
        [ObservableProperty] private SplineHandlingMode splineHandlingMode = SplineHandlingMode.Preserve;
        [ObservableProperty] private bool extrusionReadyMode;

        // ── ToolWindowShell status strip (CLAUDE.md v1.1) ──
        [ObservableProperty] private bool   isRunning;
        [ObservableProperty] private double progress;
        [ObservableProperty] private string summaryText = string.Empty;

        public IRelayCommand ConvertCommand { get; }
        public IRelayCommand CloseCommand { get; }
        public IRelayCommand CopyAllLogsCommand { get; }
        public IRelayCommand CopySelectedLogsCommand { get; }
        public IRelayCommand ExportLogCommand { get; }

        /// <summary>Set by the View's SelectionChanged handler for "Copy Selected".</summary>
        public IList SelectedLogEntries { get; set; }

        public event Action RequestClose;

        /// <summary>Lists the family's CAD imports. Call from the command (a Revit API context).</summary>
        public DwgToLinesViewModel(UIApplication app)
        {
            _uiApp = app;
            _revit = new RevitApiQueue("DWG To Lines V007");

            ConvertCommand = new RelayCommand(Convert, () => SelectedCad != null && !IsRunning);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
            CopyAllLogsCommand = new RelayCommand(CopyAllLogs);
            CopySelectedLogsCommand = new RelayCommand(CopySelectedLogs);
            ExportLogCommand = new RelayCommand(ExportLog);

            LoadCadImports();
        }

        private void LoadCadImports()
        {
            AvailableCads.Clear();
            foreach (var item in new CadImportCollectorService(_uiApp).GetAllCadImports())
                AvailableCads.Add(item);

            AddLog(LogLevel.Info, $"Found {AvailableCads.Count} CAD import(s) in this family.");
        }

        partial void OnSelectedCadChanged(CadImportItem value)
            => ConvertCommand.NotifyCanExecuteChanged();

        partial void OnIsRunningChanged(bool value)
            => ConvertCommand.NotifyCanExecuteChanged();

        private void Convert()
        {
            if (IsRunning || SelectedCad == null) return;

            // Snapshot the options now; Revit runs the job a moment later, once it is idle.
            var cad = SelectedCad.ImportInstance;
            var placement = PlacementMode;
            var spline = SplineHandlingMode;
            bool extrusionReady = ExtrusionReadyMode;

            IsRunning = true;
            bool queued = _revit.TryRun(app =>
            {
                try { ConvertInRevit(app, cad, placement, spline, extrusionReady); }
                finally { IsRunning = false; }
            });

            if (!queued)
            {
                IsRunning = false;
                AddLog(LogLevel.Warning, "Revit is busy. Finish the current command or close any open dialog, then click Convert again.");
            }
        }

        // Runs in the Revit API context (through _revit).
        private void ConvertInRevit(UIApplication app, Autodesk.Revit.DB.ImportInstance cad,
            PlacementMode placement, SplineHandlingMode spline, bool extrusionReady)
        {
            var activeDoc = app.ActiveUIDocument?.Document;
            if (cad == null || !cad.IsValidObject || activeDoc == null || !cad.Document.Equals(activeDoc))
            {
                AddLog(LogLevel.Error, "The family this window was opened for is not the active document (or the CAD import was deleted). Switch back to it, or reopen DWG To Lines.");
                return;
            }

            try
            {
                new CadConversionService(app, entry => AddLog(entry.Level, entry.Message))
                    .Execute(cad, placement, spline, extrusionReady);
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"Conversion failed: {ex.Message}");
            }
        }

        /// <summary>Releases the Revit external event. Called when the window closes.</summary>
        public void Dispose() => _revit.Dispose();

        private void AddLog(LogLevel level, string message)
            => LogEntries.Add(new LogEntry(level, message));

        private void CopyAllLogs()
        {
            if (!LogEntries.Any()) return;

            try
            {
                System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, LogEntries.Select(e => e.ToString())));
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"Copy failed: {ex.Message}");
            }
        }

        private void CopySelectedLogs()
        {
            if (SelectedLogEntries == null || SelectedLogEntries.Count == 0)
            {
                AddLog(LogLevel.Warning, "No log rows selected.");
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(string.Join(
                    Environment.NewLine,
                    SelectedLogEntries.Cast<LogEntry>().Select(e => e.ToString())));
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"Copy failed: {ex.Message}");
            }
        }

        private void ExportLog()
        {
            if (!LogEntries.Any())
            {
                AddLog(LogLevel.Warning, "Nothing to export.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                FileName = $"DwgToLines_Logs_{DateTime.Now:yyyy-MM-dd_HH-mm}.txt",
                Filter = "Text file (*.txt)|*.txt"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                File.WriteAllLines(dialog.FileName, LogEntries.Select(e => e.ToString()));
                AddLog(LogLevel.Info, $"Log exported to '{dialog.FileName}'");
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"Export failed: {ex.Message}");
            }
        }
    }
}

