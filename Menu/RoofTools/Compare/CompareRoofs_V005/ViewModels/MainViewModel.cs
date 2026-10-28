using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Data;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofPointElevationSync.V005
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly UIDocument _uiDoc;
        private readonly ExternalEvent _externalEvent;
        private readonly RoofSyncEventHandler _handler;

        // Runs the Roof A / Roof B picks in Revit's API context (a modeless window's click is outside it).
        private readonly RevitApiQueue _pickQueue;
        private readonly string _settingsPath;
        private RoofSyncSettings _settings;

        public ObservableCollection<PointMatchRow> MatchRows { get; } = new();
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        public ICollectionView MatchRowsView { get; }

        [ObservableProperty] private string roofAName = "(none selected)";
        [ObservableProperty] private string roofBName = "(none selected)";
        [ObservableProperty] private string xyToleranceMm = "10";
        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private string summaryText = "0 to apply | 0 excluded | 0 failed";
        [ObservableProperty] private double progress;   // ToolWindowShell status strip (CLAUDE.md v1.1)
        [ObservableProperty] private bool? headerAllIncluded = false;

        private bool _suppressHeaderSync;

        private RoofBase _roofA;
        private RoofBase _roofB;

        public int MatchedCount => MatchRows.Count;
        public int ToApplyCount => MatchRows.Count(r => r.IsIncluded && !r.IsEqual);
        public int ExcludedCount => MatchRows.Count(r => !r.IsIncluded || r.IsEqual);
        public double MaxDeltaMm => MatchRows.Any() ? MatchRows.Max(r => Math.Abs(r.ElevationA - r.ElevationB)) : 0;

        public MainViewModel(UIDocument uiDoc, RoofBase preselectedRoofA = null, RoofBase preselectedRoofB = null)
        {
            _uiDoc = uiDoc;

            _handler = new RoofSyncEventHandler
            {
                OnMatchesFound = HandleMatchesFound,
                OnApplyCompleted = HandleApplyCompleted,
                Log = AddLog
            };
            _externalEvent = ExternalEvent.Create(_handler);
            _pickQueue = new RevitApiQueue("Elevation Sync V005 — Pick Roof");

            MatchRowsView = CollectionViewSource.GetDefaultView(MatchRows);
            MatchRowsView.Filter = FilterRow;

            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Revit26_Plugin_Revised", "RoofPointElevationSync", "settings.json");

            LoadSettings();

            // Roofs picked in Command.Execute before the window was shown.
            if (preselectedRoofA != null)
            {
                _roofA = preselectedRoofA;
                RoofAName = DescribeRoof(preselectedRoofA);
                AddLog($"Roof A set: {RoofAName}", LogLevel.Info);
            }

            if (preselectedRoofB != null)
            {
                _roofB = preselectedRoofB;
                RoofBName = DescribeRoof(preselectedRoofB);
                AddLog($"Roof B set: {RoofBName}", LogLevel.Info);
            }
        }

        private bool FilterRow(object obj)
        {
            if (string.IsNullOrWhiteSpace(FilterText)) return true;
            var row = (PointMatchRow)obj;
            return row.PointId.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
        }

        partial void OnFilterTextChanged(string value) => MatchRowsView.Refresh();

        [RelayCommand]
        private void PickRoofA() => PickRoof("Roof A", roof =>
        {
            _roofA = roof;
            RoofAName = DescribeRoof(roof);
            AddLog($"Roof A set: {RoofAName}", LogLevel.Info);
        });

        [RelayCommand]
        private void PickRoofB() => PickRoof("Roof B", roof =>
        {
            _roofB = roof;
            RoofBName = DescribeRoof(roof);
            AddLog($"Roof B set: {RoofBName}", LogLevel.Info);
        });

        // Queues the pick: Revit only allows PickObject inside its API context.
        private void PickRoof(string label, Action<RoofBase> onPicked)
        {
            if (IsBusy) return;

            IsBusy = true;
            bool queued = _pickQueue.TryRun(app =>
            {
                try { PickRoofInRevit(app, label, onPicked); }
                finally { IsBusy = false; }
            });

            if (!queued)
            {
                IsBusy = false;
                AddLog("Revit is busy. Finish the current command or close any open dialog, then pick again.", LogLevel.Warning);
            }
        }

        // Runs in the Revit API context (through _pickQueue).
        private void PickRoofInRevit(UIApplication app, string label, Action<RoofBase> onPicked)
        {
            UIDocument active = app.ActiveUIDocument;
            if (active == null || !_uiDoc.Document.IsValidObject || !active.Document.Equals(_uiDoc.Document))
            {
                AddLog($"Pick {label}: switch back to the project this window was opened for, then try again.", LogLevel.Error);
                return;
            }

            try
            {
                var reference = active.Selection.PickObject(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    new RoofSelectionFilter(),
                    $"Select {label}");
                if (active.Document.GetElement(reference) is RoofBase roof) onPicked(roof);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                AddLog($"Pick {label}: cancelled.", LogLevel.Info);
            }
        }

        /// <summary>Releases the pick external event. Called when the window closes.</summary>
        public void ReleaseRevitQueue() => _pickQueue.Dispose();

        private static string DescribeRoof(RoofBase roof) =>
            $"{roof.Name} (Id {roof.Id.Value})";

        [RelayCommand]
        private void FindMatchingPoints()
        {
            if (_roofA == null || _roofB == null)
            {
                AddLog("Select both Roof A and Roof B before finding matching points.", LogLevel.Warning);
                return;
            }

            if (!double.TryParse(XyToleranceMm, out double toleranceMm) || toleranceMm <= 0)
            {
                AddLog("Enter a valid positive XY tolerance in mm.", LogLevel.Warning);
                return;
            }

            IsBusy = true;
            _handler.RequestedMode = RoofSyncEventHandler.Mode.FindMatches;
            _handler.RoofA = _roofA;
            _handler.RoofB = _roofB;
            _handler.XyToleranceFeet = UnitUtils.ConvertToInternalUnits(toleranceMm, UnitTypeId.Millimeters);
            _externalEvent.Raise();
        }

        private void HandleMatchesFound(System.Collections.Generic.List<PointMatchRow> rows)
        {
            foreach (var oldRow in MatchRows) oldRow.PropertyChanged -= OnMatchRowPropertyChanged;
            MatchRows.Clear();
            foreach (var row in rows)
            {
                row.PropertyChanged += OnMatchRowPropertyChanged;
                MatchRows.Add(row);
            }
            RefreshMetrics();
            IsBusy = false;
        }

        private void OnMatchRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PointMatchRow.IsIncluded))
                RefreshMetrics();
        }

        [RelayCommand]
        private void SelectAllVisible()
        {
            foreach (PointMatchRow row in MatchRowsView)
                if (!row.IsEqual) row.IsIncluded = true;
            RefreshMetrics();
        }

        [RelayCommand]
        private void ClearSelectionVisible()
        {
            foreach (PointMatchRow row in MatchRowsView)
                row.IsIncluded = false;
            RefreshMetrics();
        }

        partial void OnHeaderAllIncludedChanged(bool? value)
        {
            if (_suppressHeaderSync) return;
            if (value == true) SelectAllVisible();
            else ClearSelectionVisible();
        }

        [RelayCommand]
        private void Refresh() => FindMatchingPoints();

        [RelayCommand(CanExecute = nameof(CanApply))]
        private void Apply()
        {
            IsBusy = true;
            _handler.RequestedMode = RoofSyncEventHandler.Mode.Apply;
            _handler.RoofA = _roofA;
            _handler.RoofB = _roofB;
            _handler.MatchRows = MatchRows.ToList();
            _externalEvent.Raise();
        }

        private bool CanApply() => !IsBusy && MatchRows.Any(r => r.IsIncluded && !r.IsEqual);

        private void HandleApplyCompleted(bool success, string message)
        {
            IsBusy = false;
            if (success)
            {
                AddLog($"Apply complete: {message}", LogLevel.Info);
                SummaryText = $"{ToApplyCount} applied | {ExcludedCount} excluded | 0 failed";
            }
            else
            {
                AddLog($"Apply failed: {message}", LogLevel.Error);
                SummaryText = $"0 applied | {ExcludedCount} excluded | 1 failed";
            }
            ApplyCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void Reset()
        {
            foreach (var row in MatchRows) row.PropertyChanged -= OnMatchRowPropertyChanged;
            MatchRows.Clear();
            LogEntries.Clear();
            RoofAName = "(none selected)";
            RoofBName = "(none selected)";
            _roofA = null;
            _roofB = null;
            RefreshMetrics();
        }

        [RelayCommand]
        private void CopyAllLog()
        {
            var text = string.Join(Environment.NewLine,
                LogEntries.Select(e => $"{e.Time:HH:mm:ss}  [{e.Level}]  {e.Message}"));
            System.Windows.Clipboard.SetText(text);
        }

        /// <summary>Bound to the log ListBox's SelectedItems via code-behind (see MainWindow.xaml.cs).</summary>
        public System.Collections.IList SelectedLogEntries { get; set; }

        [RelayCommand]
        private void CopySelectedLog()
        {
            if (SelectedLogEntries == null || SelectedLogEntries.Count == 0)
            {
                CopyAllLog();
                return;
            }
            var text = string.Join(Environment.NewLine,
                SelectedLogEntries.Cast<LogEntry>().Select(e => $"{e.Time:HH:mm:ss}  [{e.Level}]  {e.Message}"));
            System.Windows.Clipboard.SetText(text);
        }

        [RelayCommand]
        private void ExportLogs()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"RoofPointElevationSync_Logs_{DateTime.Now:yyyy-MM-dd_HH-mm}.txt",
                InitialDirectory = string.IsNullOrEmpty(_settings.LastLogExportFolder)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                    : _settings.LastLogExportFolder,
                Filter = "Text file (*.txt)|*.txt"
            };

            if (dialog.ShowDialog() != true) return;

            File.WriteAllLines(dialog.FileName,
                LogEntries.Select(e => $"{e.Time:HH:mm:ss}  [{e.Level}]  {e.Message}"));

            _settings.LastLogExportFolder = Path.GetDirectoryName(dialog.FileName);
            SaveSettings();
            AddLog($"Logs exported to {dialog.FileName}", LogLevel.Info);
        }

        private void AddLog(string message, LogLevel level)
        {
            LogEntries.Add(new LogEntry(level, message));
        }

        private void RefreshMetrics()
        {
            OnPropertyChanged(nameof(MatchedCount));
            OnPropertyChanged(nameof(ToApplyCount));
            OnPropertyChanged(nameof(ExcludedCount));
            OnPropertyChanged(nameof(MaxDeltaMm));
            SummaryText = $"{ToApplyCount} to apply | {ExcludedCount} excluded | 0 failed";
            ApplyCommand.NotifyCanExecuteChanged();
            SyncHeaderCheckbox();
        }

        private void SyncHeaderCheckbox()
        {
            var selectable = MatchRows.Where(r => !r.IsEqual).ToList();
            _suppressHeaderSync = true;
            if (selectable.Count == 0) HeaderAllIncluded = false;
            else if (selectable.All(r => r.IsIncluded)) HeaderAllIncluded = true;
            else if (selectable.All(r => !r.IsIncluded)) HeaderAllIncluded = false;
            else HeaderAllIncluded = null;
            _suppressHeaderSync = false;
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    _settings = JsonSerializer.Deserialize<RoofSyncSettings>(File.ReadAllText(_settingsPath))
                                ?? new RoofSyncSettings();
                }
                else
                {
                    _settings = new RoofSyncSettings();
                }
                XyToleranceMm = _settings.XyToleranceMm.ToString();
            }
            catch
            {
                _settings = new RoofSyncSettings();
            }
        }

        public void SaveSettings()
        {
            try
            {
                if (double.TryParse(XyToleranceMm, out double tol))
                    _settings.XyToleranceMm = tol;

                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
                File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings));
            }
            catch (Exception ex)
            {
                AddLog($"Could not save settings: {ex.Message}", LogLevel.Warning);
            }
        }
    }

    internal class RoofSelectionFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is RoofBase;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
