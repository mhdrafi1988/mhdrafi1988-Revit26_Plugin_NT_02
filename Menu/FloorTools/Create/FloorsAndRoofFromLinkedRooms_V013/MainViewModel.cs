using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.FloorsAndRoofFromLinkedRooms.V013
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly Document _hostDoc;
        private readonly RunCreateElementsExternalEventHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private readonly CancelFlag _cancelFlag = new();
        private readonly ToolSettings _settings;

        public ObservableCollection<LinkedDocumentOption> LinkedDocuments { get; } = new();
        public ObservableCollection<LinkInstanceOption> AvailableInstances { get; } = new();
        public ObservableCollection<RoomCandidate> Rooms { get; } = new();
        public ObservableCollection<RoomCandidate> FilteredRooms { get; } = new();
        public ObservableCollection<HostLevelOption> HostLevels { get; } = new();
        public ObservableCollection<FloorType> FloorTypes { get; } = new();
        public ObservableCollection<RoofType> RoofTypes { get; } = new();
        public ObservableCollection<LogEntry> Logs { get; } = new();

        [ObservableProperty] private LinkedDocumentOption selectedLinkedDocument;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunFloorCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunRoofCommand))]
        private LinkInstanceOption selectedInstance;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunFloorCommand))]
        private FloorType selectedFloorType;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunRoofCommand))]
        private RoofType selectedRoofType;

        [ObservableProperty] private RoomCandidate selectedRoom;

        [ObservableProperty] private string filterText = "";

        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private bool isLoading;
        [ObservableProperty] private string floorSummaryText = "";
        [ObservableProperty] private string roofSummaryText = "";
        [ObservableProperty] private string toastMessage = "";
        public bool HasFloorSummary => !string.IsNullOrEmpty(FloorSummaryText);
        public bool HasRoofSummary => !string.IsNullOrEmpty(RoofSummaryText);
        public bool HasToast => !string.IsNullOrEmpty(ToastMessage);

        partial void OnFloorSummaryTextChanged(string value) => OnPropertyChanged(nameof(HasFloorSummary));
        partial void OnRoofSummaryTextChanged(string value) => OnPropertyChanged(nameof(HasRoofSummary));
        partial void OnToastMessageChanged(string value) => OnPropertyChanged(nameof(HasToast));

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(Progress))] private int totalCount;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(Progress))] private int processedCount;

        /// <summary>0–100 for the ToolWindowShell status strip (CLAUDE.md v1.1).</summary>
        public double Progress => TotalCount > 0 ? 100.0 * ProcessedCount / TotalCount : 0;
        [ObservableProperty] private string progressText = "";

        // â”€â”€ Elevation match tolerance â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty] private bool exactElevationMatch;

        /// <summary>Tolerance in millimeters, as entered/displayed in the UI.
        /// Ignored (treated as 0) when ExactElevationMatch is true.</summary>
        [ObservableProperty] private string toleranceMmText = "1";

        /// <summary>True while ExactElevationMatch is false â€” bound to the tolerance
        /// field's IsEnabled so it greys out when exact-match is checked.</summary>
        public bool IsToleranceEditable => !ExactElevationMatch;

        partial void OnExactElevationMatchChanged(bool value)
        {
            OnPropertyChanged(nameof(IsToleranceEditable));
            RematchAllRooms();
        }

        partial void OnToleranceMmTextChanged(string value) => RematchAllRooms();

        /// <summary>Parses ToleranceMmText to feet for use in LevelMatchingService.
        /// Invalid/empty text falls back to 0 (exact match) rather than throwing â€”
        /// a malformed tolerance should never silently widen matching.</summary>
        private double ToleranceFeet()
        {
            if (ExactElevationMatch) return 0.0;
            if (!double.TryParse(ToleranceMmText, out double mm) || mm < 0) return 0.0;
            return UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        }

        // â”€â”€ Live grid metrics (top metrics card, left group) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public int MetricTotalRooms => Rooms.Count;
        public int MetricSelectedCount => Rooms.Count(r => r.IsSelected);
        public int MetricMappedCount => Rooms.Count(r => r.IsMapped);
        public int MetricUnmappedCount => Rooms.Count(r => !r.IsMapped);

        private void NotifyLiveMetricsChanged()
        {
            OnPropertyChanged(nameof(MetricTotalRooms));
            OnPropertyChanged(nameof(MetricSelectedCount));
            OnPropertyChanged(nameof(MetricMappedCount));
            OnPropertyChanged(nameof(MetricUnmappedCount));
        }

        // â”€â”€ Post-run metrics (top metrics card, right group) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty] private int metricCreatedCount;
        [ObservableProperty] private int metricSkippedCount;
        [ObservableProperty] private int metricFailedCount;
        [ObservableProperty] private string metricLastRunLabel = "";
        [ObservableProperty] private bool hasLastRun;

        public MainViewModel(Document hostDoc, RunCreateElementsExternalEventHandler handler, ExternalEvent externalEvent)
        {
            _hostDoc = hostDoc;
            _handler = handler;
            _externalEvent = externalEvent;

            // NOTE (carried from V004): Revit's API is not thread-safe outside the API
            // execution context, so this can't be pushed onto a background Task. IsLoading
            // gives the user a visible "working" state during the scan.
            IsLoading = true;

            _settings = SettingsService.Load(out string settingsMsg);
            AddLog(LogLevel.Info, settingsMsg);

            // Restore tolerance settings before rooms load, so the very first
            // auto-match pass already uses the user's saved preference.
            exactElevationMatch = _settings.ExactElevationMatch;
            toleranceMmText = _settings.ToleranceMm.ToString("0.###");

            LoadHostLevels();
            LoadLinkedDocuments();
            LoadFloorTypes();
            LoadRoofTypes();

            // V005: first link (or the one restored from settings) is selected by
            // default, which cascades to first instance -> rooms auto-load.
            ApplyDefaultLinkSelection();

            IsLoading = false;
        }

        private void LoadHostLevels()
        {
            HostLevels.Clear();
            foreach (var l in LevelMatchingService.GetHostLevels(_hostDoc))
                HostLevels.Add(l);

            AddLog(LogLevel.Info, $"Loaded {HostLevels.Count} host level(s).");
            if (HostLevels.Count == 0)
                AddLog(LogLevel.Warning, "No levels found in the host model â€” New Level mapping will be unavailable.");
        }

        private void LoadLinkedDocuments()
        {
            LinkedDocuments.Clear();
            foreach (var d in LinkedRoomService.GetLinkedDocumentsWithRooms(_hostDoc))
                LinkedDocuments.Add(d);

            AddLog(LogLevel.Info, $"Found {LinkedDocuments.Count} linked file(s) containing rooms.");
            if (LinkedDocuments.Count == 0)
                AddLog(LogLevel.Warning, "No linked files with rooms were found in this model.");
        }

        private void LoadFloorTypes()
        {
            var types = new FilteredElementCollector(_hostDoc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .OrderBy(t => t.Name);

            foreach (var t in types) FloorTypes.Add(t);

            var restored = FloorTypes.FirstOrDefault(t => t.Name == _settings.LastFloorTypeName);
            SelectedFloorType = restored ?? FloorTypes.FirstOrDefault();

            if (restored != null)
                AddLog(LogLevel.Info, $"Floor type restored from settings: '{restored.Name}'.");
        }

        private void LoadRoofTypes()
        {
            var types = new FilteredElementCollector(_hostDoc)
                .OfClass(typeof(RoofType))
                .Cast<RoofType>()
                .OrderBy(t => t.Name);

            foreach (var t in types) RoofTypes.Add(t);

            var restored = RoofTypes.FirstOrDefault(t => t.Name == _settings.LastRoofTypeName);
            SelectedRoofType = restored ?? RoofTypes.FirstOrDefault();

            if (restored != null)
                AddLog(LogLevel.Info, $"Roof type restored from settings: '{restored.Name}'.");

            if (RoofTypes.Count == 0)
                AddLog(LogLevel.Warning, "No roof types found in host model â€” Create Roof will be unavailable.");
        }

        /// <summary>V005: select the link saved in settings if it still exists, otherwise
        /// the first link in the list. Selecting the link cascades (via
        /// OnSelectedLinkedDocumentChanged) to the first instance and auto-loads rooms.</summary>
        private void ApplyDefaultLinkSelection()
        {
            if (LinkedDocuments.Count == 0) return;

            var fromSettings = LinkedDocuments.FirstOrDefault(d => d.DisplayName == _settings.LastLinkName);
            SelectedLinkedDocument = fromSettings ?? LinkedDocuments[0];

            AddLog(LogLevel.Info, fromSettings != null
                ? $"Link restored from settings: '{SelectedLinkedDocument.DisplayName}'."
                : $"Default link selected: '{SelectedLinkedDocument.DisplayName}'.");
        }

        partial void OnSelectedLinkedDocumentChanged(LinkedDocumentOption value)
        {
            AvailableInstances.Clear();
            ClearRooms();
            if (value == null) return;

            foreach (var inst in value.Instances)
                AvailableInstances.Add(inst);

            // V005: first instance always selected by default (V004 only auto-selected
            // when exactly one existed). Triggers OnSelectedInstanceChanged -> room load.
            SelectedInstance = AvailableInstances.FirstOrDefault();
            if (AvailableInstances.Count > 1)
                AddLog(LogLevel.Info, $"{AvailableInstances.Count} instances of this link â€” first selected by default.");
        }

        partial void OnSelectedInstanceChanged(LinkInstanceOption value)
        {
            ClearRooms();
            if (value == null || SelectedLinkedDocument == null) return;

            var found = LinkedRoomService.GetAllRooms(SelectedLinkedDocument.LinkDocument);

            // Elevation-based auto-match.
            // Left null ("unmapped" in the grid) when no host level is within tolerance â€”
            // never defaulted to any level, and no host level is ever created, per
            // confirmed spec.
            double toleranceFeet = ToleranceFeet();
            AddLog(LogLevel.Info, ExactElevationMatch
                ? "Matching rooms to host levels by elevation (exact match)."
                : $"Matching rooms to host levels by elevation (tolerance: {ToleranceMmText}mm).");

            foreach (var r in found)
            {
                r.SelectedHostLevel = LevelMatchingService.FindMatch(
                    r.LinkedLevelElevation, value.Transform, HostLevels, toleranceFeet);

                r.PropertyChanged += RoomCandidate_PropertyChanged;
                Rooms.Add(r);
            }

            ApplyFilter();
            NotifyLiveMetricsChanged();

            // Auto-select the first row so the user has immediate context on load.
            SelectedRoom = FilteredRooms.FirstOrDefault();

            int unmatched = found.Count(r => r.SelectedHostLevel == null);
            if (found.Count == 0)
                AddLog(LogLevel.Warning, "No rooms were found in this link.");
            else
                AddLog(LogLevel.Info,
                    $"Loaded {found.Count} room(s) across {found.Select(r => r.LinkedLevelName).Distinct().Count()} linked level(s) " +
                    $"from '{value.DisplayName}'.");

            if (unmatched > 0)
                AddLog(LogLevel.Warning, $"{unmatched} room(s) had no matching host level within tolerance â€” map manually via New Level.");
        }

        /// <summary>Re-runs elevation matching for all currently loaded rooms
        /// without a full reload â€” used when the user toggles Exact Match or edits the
        /// tolerance field. No-op if no rooms are loaded yet (e.g. during startup restore).</summary>
        private void RematchAllRooms()
        {
            if (Rooms.Count == 0 || SelectedInstance == null) return;

            double toleranceFeet = ToleranceFeet();
            int unmatched = 0;

            foreach (var r in Rooms)
            {
                r.SelectedHostLevel = LevelMatchingService.FindMatch(
                    r.LinkedLevelElevation, SelectedInstance.Transform, HostLevels, toleranceFeet);
                if (r.SelectedHostLevel == null) unmatched++;
            }

            NotifyLiveMetricsChanged();

            AddLog(LogLevel.Info, ExactElevationMatch
                ? $"Re-matched {Rooms.Count} room(s) by elevation (exact match) â€” {unmatched} unmapped."
                : $"Re-matched {Rooms.Count} room(s) by elevation (tolerance: {ToleranceMmText}mm) â€” {unmatched} unmapped.");
        }

        private void ClearRooms()
        {
            foreach (var r in Rooms) r.PropertyChanged -= RoomCandidate_PropertyChanged;
            Rooms.Clear();
            FilteredRooms.Clear();
            SelectedRoom = null;
            NotifyLiveMetricsChanged();
        }

        private void RoomCandidate_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RoomCandidate.IsSelected))
            {
                RunFloorCommand.NotifyCanExecuteChanged();
                RunRoofCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(MetricSelectedCount));
            }
            else if (e.PropertyName == nameof(RoomCandidate.IsMapped))
            {
                // Manual New Level edits from the grid combo also move the
                // Mapped/Unmapped live metric, not just the auto-match pass.
                OnPropertyChanged(nameof(MetricMappedCount));
                OnPropertyChanged(nameof(MetricUnmappedCount));
            }
        }

        private void ApplyFilter()
        {
            FilteredRooms.Clear();
            IEnumerable<RoomCandidate> source = Rooms;

            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                string f = FilterText.Trim();
                source = source.Where(r =>
                    (r.RoomName?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (r.RoomNumber?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (r.LinkedLevelName?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            foreach (var r in source) FilteredRooms.Add(r);
        }

        /// <summary>Select All / Clear are scoped to the currently visible/filtered rows,
        /// per DataGrid spec item 5.</summary>
        [RelayCommand]
        private void SelectAll() { foreach (var r in FilteredRooms) r.IsSelected = true; }

        [RelayCommand]
        private void DeselectAll() { foreach (var r in FilteredRooms) r.IsSelected = false; }

        [RelayCommand]
        private void Refresh()
        {
            if (SelectedInstance != null) OnSelectedInstanceChanged(SelectedInstance);
        }

        private bool CanRunFloor() => !IsBusy && SelectedInstance != null && SelectedFloorType != null
            && Rooms.Any(r => r.IsSelected);

        private bool CanRunRoof() => !IsBusy && SelectedInstance != null && SelectedRoofType != null
            && Rooms.Any(r => r.IsSelected);

        [RelayCommand(CanExecute = nameof(CanRunFloor))]
        private void RunFloor() => StartRun(CreationMode.Floor, SelectedFloorType.Id);

        [RelayCommand(CanExecute = nameof(CanRunRoof))]
        private void RunRoof() => StartRun(CreationMode.Roof, SelectedRoofType.Id);

        private void StartRun(CreationMode mode, ElementId typeId)
        {
            var allSelected = Rooms.Where(r => r.IsSelected).ToList();

            // Unmapped rows are skipped (logged as Warning) and the rest of the run
            // proceeds, per confirmed spec.
            var runnable = allSelected.Where(r => r.IsMapped).ToList();
            var unmapped = allSelected.Where(r => !r.IsMapped).ToList();

            foreach (var r in unmapped)
                AddLog(LogLevel.Warning, $"{r.DisplayName} â€” skipped: no New Level mapped.");

            if (runnable.Count == 0)
            {
                AddLog(LogLevel.Warning, "No mapped rooms to process â€” run cancelled.");
                return;
            }

            IsBusy = true;

            TotalCount = runnable.Count;
            ProcessedCount = 0;
            ProgressText = $"Processing 0 of {TotalCount}";

            _handler.PendingRequest = new CreateRunRequest
            {
                Mode = mode,
                Rooms = runnable,
                LinkTransform = SelectedInstance.Transform,
                TypeId = typeId,
            };

            // Unmapped count is known before the handler runs, so it's passed through
            // rather than recomputed on the Revit API thread.
            _handler.UnmappedSkippedCount = unmapped.Count;

            _externalEvent.Raise();
        }

        /// <summary>Called by the handler on the Revit API thread; ObservableCollection
        /// updates are safe here since the handler runs on the same thread that owns the
        /// modeless window's dispatcher in this pattern.</summary>
        public void AddLog(LogLevel level, string message) => Logs.Add(new LogEntry(level, message));

        public void ReportProgress(int processed)
        {
            ProcessedCount = processed;
            int percent = TotalCount > 0 ? (int)Math.Round(processed * 100.0 / TotalCount) : 0;
            ProgressText = $"Processing {processed} of {TotalCount} ({percent}%)";
        }

        public void OnRunComplete(CreationMode mode, RunSummary summary, bool wasCancelled)
        {
            IsBusy = false;
            ProgressText = wasCancelled ? "Cancelled" : "Completed";

            string text = $"success {summary.SuccessCount} | trimmed/fixed {summary.TrimmedFixedCount} | "
                + $"failed {summary.FailedCount} | inner loops skipped {summary.InnerLoopsSkippedCount} | "
                + $"unmapped skipped {summary.UnmappedSkippedCount} | not processed {summary.NotProcessedCount}";

            if (mode == CreationMode.Floor) FloorSummaryText = text;
            else RoofSummaryText = text;

            // Completion summary also goes to the log panel so it's part of Copy All /
            // Export output, per confirmed spec ("everything to UI log and copyable").
            AddLog(wasCancelled ? LogLevel.Warning : LogLevel.Info,
                $"{(mode == CreationMode.Floor ? "Floor" : "Roof")} run {(wasCancelled ? "cancelled" : "completed")} â€” {text}");

            // Post-run metrics card (right group). Skipped = unmapped + inner-loop
            // skips, matching the same tallies already surfaced in the summary text above.
            MetricCreatedCount = summary.SuccessCount;
            MetricSkippedCount = summary.UnmappedSkippedCount + summary.InnerLoopsSkippedCount;
            MetricFailedCount = summary.FailedCount;
            MetricLastRunLabel = $"{(mode == CreationMode.Floor ? "Floors" : "Roof")} Â· {DateTime.Now:HH:mm:ss}";
            HasLastRun = true;

            SaveSettings("after run");

            RunFloorCommand.NotifyCanExecuteChanged();
            RunRoofCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Persists current selections. Called after each run and from the
        /// window's Closing event (see code-behind). The reason string is only used
        /// for the log line.</summary>
        public void SaveSettings(string reason)
        {
            _settings.LastLinkName = SelectedLinkedDocument?.DisplayName;
            _settings.LastFloorTypeName = SelectedFloorType?.Name;
            _settings.LastRoofTypeName = SelectedRoofType?.Name;

            // Persist tolerance preference. Malformed tolerance text is not
            // persisted as garbage â€” falls back to the last-known-good value (1.0 default).
            _settings.ExactElevationMatch = ExactElevationMatch;
            if (double.TryParse(ToleranceMmText, out double mm) && mm >= 0)
                _settings.ToleranceMm = mm;

            bool ok = SettingsService.Save(_settings, out string msg);
            AddLog(ok ? LogLevel.Info : LogLevel.Warning, $"{msg} ({reason}).");
        }

        /// <summary>Shows a Task Dialog for a transaction-level failure (spec: critical
        /// errors must surface a dialog, not just a log line).</summary>
        public void ShowCriticalError(string reason)
        {
            TaskDialog.Show("Floors and Roofs From Linked Rooms",
                $"The operation could not complete cleanly.\n\n{reason}");
        }

        [RelayCommand]
        private void CopyFloorSummary()
        {
            if (!HasFloorSummary) return;
            System.Windows.Clipboard.SetText(FloorSummaryText);
            ShowToast("Floor summary copied to clipboard");
        }

        [RelayCommand]
        private void CopyRoofSummary()
        {
            if (!HasRoofSummary) return;
            System.Windows.Clipboard.SetText(RoofSummaryText);
            ShowToast("Roof summary copied to clipboard");
        }

        [RelayCommand]
        private void CopyAllLogs()
        {
            var sb = new StringBuilder();
            foreach (var l in Logs) sb.AppendLine(l.ToString());
            System.Windows.Clipboard.SetText(sb.ToString());
            ShowToast("Logs copied to clipboard");
        }

        /// <summary>Called from code-behind after Copy Selected too, so both copy paths
        /// give the same feedback.</summary>
        public void ShowToast(string message)
        {
            ToastMessage = message;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, e) => { ToastMessage = ""; timer.Stop(); };
            timer.Start();
        }

        [RelayCommand]
        private void ClearLogs()
        {
            if (Logs.Count == 0) return;
            var td = new TaskDialog("Floors and Roofs From Linked Rooms");
            td.MainContent = "Clear all logs?";
            td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
            if (td.Show() == TaskDialogResult.Yes) Logs.Clear();
        }

        [RelayCommand]
        private void ExportLogs()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Text file (*.txt)|*.txt",
                FileName = $"FloorsAndRoofFromLinkedRooms_Logs_{DateTime.Now:yyyy-MM-dd_HH-mm}.txt"
            };
            if (dialog.ShowDialog() != true) return;

            var sb = new StringBuilder();
            foreach (var l in Logs) sb.AppendLine(l.ToString());
            sb.AppendLine();
            if (HasFloorSummary) sb.AppendLine("Floor run: " + FloorSummaryText);
            if (HasRoofSummary) sb.AppendLine("Roof run: " + RoofSummaryText);

            File.WriteAllText(dialog.FileName, sb.ToString());
        }

        // Copy Selected is handled in code-behind (DataGrid/list selection is a UI concern),
        // per the suite convention â€” see FloorsFromLinkedRoomsWindow.xaml.cs.
    }
}

