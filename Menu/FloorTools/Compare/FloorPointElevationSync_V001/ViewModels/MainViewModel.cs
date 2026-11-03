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

namespace Revit26_Plugin_Revised.FloorPointElevationSync.V001
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly UIDocument _uiDoc;
        private readonly ExternalEvent _externalEvent;
        private readonly FloorSyncEventHandler _handler;
        private readonly string _settingsPath;
        private FloorSyncSettings _settings;

        public ObservableCollection<PointMatchRow> MatchRows { get; } = new();
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        public ICollectionView MatchRowsView { get; }

        [ObservableProperty] private string floorAName = "(none selected)";
        [ObservableProperty] private string floorBName = "(none selected)";
        [ObservableProperty] private string xyToleranceMm = "10";
        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private string summaryText = "0 to apply | 0 excluded | 0 failed";
        [ObservableProperty] private double progress;   // ToolWindowShell status strip (CLAUDE.md v1.1)
        [ObservableProperty] private bool? headerAllIncluded = false;

        private bool _suppressHeaderSync;

        private Floor _floorA;
        private Floor _floorB;

        public int MatchedCount => MatchRows.Count;
        public int ToApplyCount => MatchRows.Count(r => r.IsIncluded && !r.IsEqual);
        public int ExcludedCount => MatchRows.Count(r => !r.IsIncluded || r.IsEqual);
        public double MaxDeltaMm => MatchRows.Any() ? MatchRows.Max(r => Math.Abs(r.ElevationA - r.ElevationB)) : 0;

        public MainViewModel(UIDocument uiDoc, Floor preselectedFloorA = null, Floor preselectedFloorB = null)
        {
            _uiDoc = uiDoc;

            _handler = new FloorSyncEventHandler
            {
                OnMatchesFound = HandleMatchesFound,
                OnApplyCompleted = HandleApplyCompleted,
                Log = AddLog
            };
            _externalEvent = ExternalEvent.Create(_handler);

            MatchRowsView = CollectionViewSource.GetDefaultView(MatchRows);
            MatchRowsView.Filter = FilterRow;

            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Revit26_Plugin_Revised", "FloorPointElevationSync", "settings.json");

            LoadSettings();

            // Floors picked in Command.Execute before the window was shown.
            if (preselectedFloorA != null)
            {
                _floorA = preselectedFloorA;
                FloorAName = DescribeFloor(preselectedFloorA);
                AddLog($"Floor A set: {FloorAName}", LogLevel.Info);
            }

            if (preselectedFloorB != null)
            {
                _floorB = preselectedFloorB;
                FloorBName = DescribeFloor(preselectedFloorB);
                AddLog($"Floor B set: {FloorBName}", LogLevel.Info);
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
        private void PickFloorA()
        {
            var floor = PickFloor();
            if (floor == null) return;
            _floorA = floor;
            FloorAName = DescribeFloor(floor);
            AddLog($"Floor A set: {FloorAName}", LogLevel.Info);
        }

        [RelayCommand]
        private void PickFloorB()
        {
            var floor = PickFloor();
            if (floor == null) return;
            _floorB = floor;
            FloorBName = DescribeFloor(floor);
            AddLog($"Floor B set: {FloorBName}", LogLevel.Info);
        }

        private Floor PickFloor()
        {
            try
            {
                var reference = _uiDoc.Selection.PickObject(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    new FloorSelectionFilter(),
                    "Select a floor");
                return _uiDoc.Document.GetElement(reference) as Floor;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }

        private static string DescribeFloor(Floor floor) =>
            $"{floor.Name} (Id {floor.Id.Value})";

        [RelayCommand]
        private void FindMatchingPoints()
        {
            if (_floorA == null || _floorB == null)
            {
                AddLog("Select both Floor A and Floor B before finding matching points.", LogLevel.Warning);
                return;
            }

            if (!double.TryParse(XyToleranceMm, out double toleranceMm) || toleranceMm <= 0)
            {
                AddLog("Enter a valid positive XY tolerance in mm.", LogLevel.Warning);
                return;
            }

            IsBusy = true;
            _handler.RequestedMode = FloorSyncEventHandler.Mode.FindMatches;
            _handler.FloorA = _floorA;
            _handler.FloorB = _floorB;
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
            _handler.RequestedMode = FloorSyncEventHandler.Mode.Apply;
            _handler.FloorA = _floorA;
            _handler.FloorB = _floorB;
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
            FloorAName = "(none selected)";
            FloorBName = "(none selected)";
            _floorA = null;
            _floorB = null;
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
                FileName = $"FloorPointElevationSync_Logs_{DateTime.Now:yyyy-MM-dd_HH-mm}.txt",
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
                    _settings = JsonSerializer.Deserialize<FloorSyncSettings>(File.ReadAllText(_settingsPath))
                                ?? new FloorSyncSettings();
                }
                else
                {
                    _settings = new FloorSyncSettings();
                }
                XyToleranceMm = _settings.XyToleranceMm.ToString();
            }
            catch
            {
                _settings = new FloorSyncSettings();
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

    internal class FloorSelectionFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
    {
        public bool AllowElement(Element elem) =>
            elem is Floor && (elem.GroupId == null || elem.GroupId == ElementId.InvalidElementId);
        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
