using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revit26_Plugin_Revised.FloorTypeManager.V001.Core.Models;
using Revit26_Plugin_Revised.FloorTypeManager.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.FloorTypeManager.V001.UI.ViewModels
{
    public partial class FloorTypeManagerViewModel : ObservableObject, IDisposable
    {
        private readonly UIApplication _uiApp;
        private readonly FloorTypeExternalEventHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private readonly Dispatcher _dispatcher;

        // ── Observable Collections ──────────────────────────────
        public ObservableCollection<FloorTypeItem>     AllFloorTypes        { get; } = new();
        public ObservableCollection<FloorTypeItem>     FilteredFloorTypes   { get; } = new();
        public ObservableCollection<ImportPreviewItem> ImportPreviewItems  { get; } = new();
        public ObservableCollection<LogEntry>          LogEntries          { get; } = new();

        // ── Metrics ─────────────────────────────────────────────
        [ObservableProperty] private int    totalFloorTypes;
        [ObservableProperty] private string lastExportDate = "—";
        [ObservableProperty] private string lastImportFile = "—";

        // ── State ───────────────────────────────────────────────
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ExportSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
        [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
        private bool isBusy;

        // ToolWindowShell status strip (CLAUDE.md v1.1)
        [ObservableProperty] private double progress;
        [ObservableProperty] private string summaryText = "";

        [ObservableProperty] private string filterText          = "";
        [ObservableProperty] private string exportFilePath      = "";
        [ObservableProperty] private string importFilePath      = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ExportSelectedCommand))]
        private int selectedExportCount;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
        private int newImportCount;

        [ObservableProperty] private int dupImportCount;

        // ── Header checkbox for export grid ─────────────────────
        [ObservableProperty] private bool allExportItemsSelected;
        partial void OnAllExportItemsSelectedChanged(bool value)
        {
            foreach (var item in FilteredFloorTypes) item.IsSelected = value;
            UpdateSelectedCount();
        }

        // ── Completion summary ───────────────────────────────────
        [ObservableProperty] private int    importedCount;
        [ObservableProperty] private int    renamedCount;
        [ObservableProperty] private int    failedCount;
        [ObservableProperty] private string lastRunTime    = "";
        [ObservableProperty] private string lastExportPath = "";   // non-empty → show Open button

        // ── Ctor ────────────────────────────────────────────────
        public FloorTypeManagerViewModel(UIApplication uiApp)
        {
            _uiApp      = uiApp;
            _dispatcher = Dispatcher.CurrentDispatcher;

            _handler = new FloorTypeExternalEventHandler
            {
                OnTypesLoaded   = items    => _dispatcher.Invoke(() => ApplyLoadedTypes(items)),
                OnExportDone    = path     => _dispatcher.Invoke(() => ApplyExportDone(path)),
                OnPreviewLoaded = items    => _dispatcher.Invoke(() => ApplyPreview(items)),
                OnImportDone    = result   => _dispatcher.Invoke(() => ApplyImportDone(result)),
                OnTemplateDone  = path     => _dispatcher.Invoke(() => IsBusy = false),
                OnLog           = (lv, msg)=> _dispatcher.Invoke(() => AppendLog(lv, msg))
            };

            _externalEvent = ExternalEvent.Create(_handler);
        }

        // ── Property callbacks ──────────────────────────────────
        partial void OnFilterTextChanged(string value) => RebuildFilter();

        // ── Commands ────────────────────────────────────────────

        [RelayCommand]
        private void WindowLoaded() => RaiseRequest(FloorTypeRequest.LoadTypes);

        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private void Refresh() => RaiseRequest(FloorTypeRequest.LoadTypes);
        private bool CanRefresh() => !IsBusy;

        [RelayCommand]
        private void SelectAll()
        {
            foreach (var item in FilteredFloorTypes) item.IsSelected = true;
            UpdateSelectedCount();
        }

        [RelayCommand]
        private void ClearSelection()
        {
            foreach (var item in FilteredFloorTypes) item.IsSelected = false;
            UpdateSelectedCount();
        }

        [RelayCommand]
        private void BrowseExport()
        {
            var dlg = new SaveFileDialog
            {
                Filter   = "Excel Files (*.xlsx)|*.xlsx",
                FileName = $"FloorTypes_Export_{DateTime.Now:yyyy-MM-dd}.xlsx"
            };
            if (dlg.ShowDialog() == true) ExportFilePath = dlg.FileName;
        }

        [RelayCommand(CanExecute = nameof(CanExport))]
        private void ExportSelected()
        {
            if (string.IsNullOrWhiteSpace(ExportFilePath)) { BrowseExport(); }
            if (string.IsNullOrWhiteSpace(ExportFilePath)) return;

            _handler.ItemsToExport  = FilteredFloorTypes.Where(i => i.IsSelected).ToList();
            _handler.ExportFilePath = ExportFilePath;
            RaiseRequest(FloorTypeRequest.ExportSelected);
        }
        private bool CanExport() => !IsBusy && SelectedExportCount > 0;

        [RelayCommand]
        private void BrowseImport()
        {
            var dlg = new OpenFileDialog { Filter = "Excel Files (*.xlsx)|*.xlsx" };
            if (dlg.ShowDialog() != true) return;

            ImportFilePath       = dlg.FileName;
            _handler.ImportFilePath = ImportFilePath;
            LastImportFile       = Path.GetFileName(ImportFilePath);
            RaiseRequest(FloorTypeRequest.PreviewImport);
        }

        [RelayCommand(CanExecute = nameof(CanImport))]
        private void Import()
        {
            _handler.ItemsToImport = ImportPreviewItems.ToList();
            RaiseRequest(FloorTypeRequest.ImportNew);
        }
        private bool CanImport() => !IsBusy && ImportPreviewItems.Count > 0;

        [RelayCommand]
        private void DownloadTemplate()
        {
            var dlg = new SaveFileDialog
            {
                Filter   = "Excel Files (*.xlsx)|*.xlsx",
                FileName = "FloorType_Import_Template.xlsx",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (dlg.ShowDialog() != true) return;
            _handler.TemplateFilePath = dlg.FileName;
            RaiseRequest(FloorTypeRequest.DownloadTemplate);
        }

        [RelayCommand]
        private void OpenExportFile()
        {
            if (!string.IsNullOrEmpty(LastExportPath) && File.Exists(LastExportPath))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LastExportPath) { UseShellExecute = true });
        }

        [RelayCommand]
        private void CopyAllLogs()
        {
            var text = string.Join(Environment.NewLine, LogEntries.Select(l => l.ToString()));
            try { Clipboard.SetText(text); } catch { /* clipboard may be locked */ }
        }

        // ── Helpers ─────────────────────────────────────────────
        private void RaiseRequest(FloorTypeRequest req)
        {
            IsBusy = true;
            _handler.PendingRequest = req;
            _externalEvent.Raise();
        }

        private void RebuildFilter()
        {
            foreach (var item in FilteredFloorTypes)
                item.PropertyChanged -= OnItemPropertyChanged;

            FilteredFloorTypes.Clear();

            var query = string.IsNullOrWhiteSpace(FilterText)
                ? AllFloorTypes.AsEnumerable()
                : AllFloorTypes.Where(i =>
                    i.TypeName.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    i.TypeMark.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) >= 0);

            foreach (var item in query)
            {
                item.PropertyChanged += OnItemPropertyChanged;
                FilteredFloorTypes.Add(item);
            }

            UpdateSelectedCount();
        }

        private void OnItemPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FloorTypeItem.IsSelected))
                UpdateSelectedCount();
        }

        private void UpdateSelectedCount()
        {
            SelectedExportCount = FilteredFloorTypes.Count(i => i.IsSelected);
        }

        private void AppendLog(string levelName, string message)
        {
            if (!Enum.TryParse<LogLevel>(levelName, ignoreCase: true, out var level))
                level = LogLevel.Info;
            LogEntries.Add(new LogEntry(level, message));
        }

        // ── External event callbacks ─────────────────────────────
        private void ApplyLoadedTypes(List<FloorTypeItem> items)
        {
            AllFloorTypes.Clear();
            foreach (var item in items) AllFloorTypes.Add(item);
            TotalFloorTypes           = items.Count;
            _handler.LoadedItems     = items;
            RebuildFilter();
            IsBusy = false;
        }

        private void ApplyExportDone(string path)
        {
            if (path != null)
            {
                LastExportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                LastExportPath = path;
            }
            IsBusy = false;
        }

        private void ApplyPreview(List<ImportPreviewItem> items)
        {
            ImportPreviewItems.Clear();
            foreach (var item in items) ImportPreviewItems.Add(item);
            NewImportCount = items.Count(i => i.Status == ImportStatus.New);
            DupImportCount = items.Count(i => i.Status == ImportStatus.Dup);
            ImportCommand.NotifyCanExecuteChanged();
            IsBusy = false;
        }

        private void ApplyImportDone(ImportResult result)
        {
            ImportedCount = result.Created;
            RenamedCount  = result.Renamed;
            FailedCount   = result.Failed;
            LastRunTime   = $"Last run: {DateTime.Now:HH:mm:ss}";
            IsBusy = false;

            if (result.Created > 0 || result.Renamed > 0) RaiseRequest(FloorTypeRequest.LoadTypes);
        }

        // ── IDisposable ──────────────────────────────────────────
        public void Dispose()
        {
            foreach (var item in FilteredFloorTypes)
                item.PropertyChanged -= OnItemPropertyChanged;
            _externalEvent?.Dispose();
        }
    }
}
