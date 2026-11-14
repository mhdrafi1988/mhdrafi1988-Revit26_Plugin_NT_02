using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.FloorsAndRoofFromLinkedRoomsViaPlanView.V006
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly Document _hostDoc;
        private readonly ViewPlan _planView;
        private readonly RunCreateElementsExternalEventHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private readonly Level _activeLevel;
        private readonly CancelFlag _cancelFlag = new();

        public ObservableCollection<LinkedDocumentOption> LinkedDocuments { get; } = new();
        public ObservableCollection<LinkInstanceOption> AvailableInstances { get; } = new();
        public ObservableCollection<RoomCandidate> Rooms { get; } = new();
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

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(Progress))] private int totalCount;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(Progress))] private int processedCount;

        /// <summary>ToolWindowShell status strip (CLAUDE.md v1.1): Processed ÷ Total as 0–100.</summary>
        public double Progress => TotalCount > 0 ? 100.0 * ProcessedCount / TotalCount : 0;
        [ObservableProperty] private string progressText = "";

        public MainViewModel(Document hostDoc, ViewPlan planView, RunCreateElementsExternalEventHandler handler, ExternalEvent externalEvent)
        {
            _hostDoc = hostDoc;
            _planView = planView;
            _handler = handler;
            _externalEvent = externalEvent;
            _activeLevel = planView.GenLevel;

            // NOTE (fix #10): Revit's API is not thread-safe outside the API execution
            // context, so this can't be pushed onto a background Task â€” genuine async
            // loading isn't available here. IsLoading at least gives the user a visible
            // "working" state instead of the window silently freezing during the scan.
            IsLoading = true;
            LoadLinkedDocuments();
            LoadFloorTypes();
            LoadRoofTypes();
            IsLoading = false;
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
            SelectedFloorType = FloorTypes.FirstOrDefault();
        }

        private void LoadRoofTypes()
        {
            var types = new FilteredElementCollector(_hostDoc)
                .OfClass(typeof(RoofType))
                .Cast<RoofType>()
                .OrderBy(t => t.Name);

            foreach (var t in types) RoofTypes.Add(t);
            SelectedRoofType = RoofTypes.FirstOrDefault();

            if (RoofTypes.Count == 0)
                AddLog(LogLevel.Warning, "No roof types found in host model — Create Roof will be unavailable.");
        }

        partial void OnSelectedLinkedDocumentChanged(LinkedDocumentOption value)
        {
            AvailableInstances.Clear();
            foreach (var r in Rooms) r.PropertyChanged -= RoomCandidate_PropertyChanged;
            Rooms.Clear();
            if (value == null) return;

            foreach (var inst in value.Instances)
                AvailableInstances.Add(inst);

            SelectedInstance = AvailableInstances.Count == 1 ? AvailableInstances[0] : null;
        }

        partial void OnSelectedInstanceChanged(LinkInstanceOption value)
        {
            foreach (var r in Rooms) r.PropertyChanged -= RoomCandidate_PropertyChanged;
            Rooms.Clear();
            if (value == null || SelectedLinkedDocument == null) return;

            var found = LinkedRoomService.GetRoomsAtLevel(SelectedLinkedDocument.LinkDocument, value, _activeLevel);
            foreach (var r in found)
            {
                r.PropertyChanged += RoomCandidate_PropertyChanged;
                Rooms.Add(r);
            }

            if (found.Count == 0)
                AddLog(LogLevel.Warning, "No rooms in this link intersect the active view's level.");
        }

        private void RoomCandidate_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(RoomCandidate.IsSelected)) return;
            RunFloorCommand.NotifyCanExecuteChanged();
            RunRoofCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void SelectAll() { foreach (var r in Rooms) r.IsSelected = true; }

        [RelayCommand]
        private void DeselectAll() { foreach (var r in Rooms) r.IsSelected = false; }

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
            IsBusy = true;

            var selected = Rooms.Where(r => r.IsSelected).ToList();
            TotalCount = selected.Count;
            ProcessedCount = 0;
            ProgressText = $"Processing 0 of {TotalCount}";

            _handler.PendingRequest = new CreateRunRequest
            {
                Mode = mode,
                Rooms = selected,
                LinkTransform = SelectedInstance.Transform,
                TypeId = typeId,
                TargetLevel = _activeLevel,
            };
            _externalEvent.Raise();
        }

        /// <summary>Called by the handler on the Revit API thread; ObservableCollection
        /// updates are safe here since the handler runs on Revit's idling/API context,
        /// same thread that owns the modeless window's dispatcher in this pattern.</summary>
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
                + $"failed {summary.FailedCount} | inner loops skipped {summary.InnerLoopsSkippedCount}";

            if (mode == CreationMode.Floor) FloorSummaryText = text;
            else RoofSummaryText = text;

            AddLog(wasCancelled ? LogLevel.Warning : LogLevel.Info,
                $"{(mode == CreationMode.Floor ? "Floor" : "Roof")} run {(wasCancelled ? "cancelled" : "completed")} — {text}");

            RunFloorCommand.NotifyCanExecuteChanged();
            RunRoofCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Shows a Task Dialog for a transaction-level failure (spec: critical
        /// errors must surface a dialog, not just a log line). Safe to call from the
        /// handler since it runs on the same thread as the Revit UI.</summary>
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
                FileName = $"FloorsAndRoofFromLinkedRoomsViaPlanView_Logs_{DateTime.Now:yyyy-MM-dd_HH-mm}.txt"
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

