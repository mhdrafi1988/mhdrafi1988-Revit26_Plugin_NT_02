using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.ExportDwgToFolder.V003.Core.Models;
using Revit26_Plugin_Revised.ExportDwgToFolder.V003.Core.Services;
using Revit26_Plugin_Revised.ExportDwgToFolder.V003.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V003.UI.ViewModels
{
    /// <summary>
    /// Export DWG to Folder (V003): nothing is scanned when the window opens. The user ticks a scan
    /// scope (source + view types), scans, ticks rows in the grouped grid — individually, per group
    /// via the tri-state checkbox on each group header, or via the shown-rows header tick — and
    /// exports only those. Each exported DWG is produced by temporarily isolating just that one CAD
    /// instance in its host view (see <see cref="Core.Services.DwgExportService"/>), not the whole
    /// view's content. After a successful export, the user is asked whether to delete the
    /// successfully-exported CAD instances from the model; skipped/failed rows are never offered.
    /// Scanning, loading the list and exporting each drive a footer progress bar with count and
    /// percentage.
    /// </summary>
    public partial class ExportDwgToFolderViewModel : ObservableObject
    {
        private const string ToolName = "ExportDwgToFolder";

        /// <summary>Minimum time between forced repaints while Revit is busy (ms).</summary>
        private const double PumpIntervalMs = 40;

        private readonly DwgExportEventHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly ExportDwgSettings _settings;

        private DateTime _lastPump = DateTime.MinValue;
        private bool _bulkSelecting;
        private string _sortMember = nameof(DwgRowViewModel.Name);
        private ListSortDirection _sortDirection = ListSortDirection.Ascending;
        private List<DwgRowViewModel> _exportRows = new List<DwgRowViewModel>();
        private List<DwgRowViewModel> _deleteRows = new List<DwgRowViewModel>();

        // ── Metrics ──────────────────────────────────────────────────────────

        /// <summary>Header metric tiles.</summary>
        public ObservableCollection<SummaryCard> SummaryCards { get; } = new ObservableCollection<SummaryCard>();

        private readonly SummaryCard _cardScanned = new SummaryCard("Scanned", 0, CardTone.Accent, "DWG instances found by the last scan");
        private readonly SummaryCard _cardLinked = new SummaryCard("Linked", 0, CardTone.Neutral, "CAD links in the list");
        private readonly SummaryCard _cardImported = new SummaryCard("Imported", 0, CardTone.Neutral, "CAD imports in the list");
        private readonly SummaryCard _cardReady = new SummaryCard("Ready", 0, CardTone.Success, "Rows that passed the pre-check");
        private readonly SummaryCard _cardIssues = new SummaryCard("Issues", 0, CardTone.Neutral, "Rows whose source file or host view is missing");
        private readonly SummaryCard _cardSelected = new SummaryCard("Selected", 0, CardTone.Accent, "Rows ticked for export");

        // ── Progress bars ────────────────────────────────────────────────────

        /// <summary>Scan progress (DWG instances processed).</summary>
        public PhaseProgress ScanPhase { get; } = new PhaseProgress("Scanning");

        /// <summary>Loading progress (rows added to the list).</summary>
        public PhaseProgress LoadPhase { get; } = new PhaseProgress("Loading list");

        /// <summary>Export progress (ticked DWGs processed).</summary>
        public PhaseProgress ExportPhase { get; } = new PhaseProgress("Exporting");

        /// <summary>The three bars, in footer order.</summary>
        public ObservableCollection<PhaseProgress> Phases { get; } = new ObservableCollection<PhaseProgress>();

        // ── Output folder + options (remembered) ─────────────────────────────

        /// <summary>Root output folder.</summary>
        [ObservableProperty] private string outputFolder = string.Empty;

        /// <summary>Put each DWG in a view-type subfolder.</summary>
        [ObservableProperty] private bool subfoldersByViewType;

        /// <summary>Write a sidecar .txt per DWG.</summary>
        [ObservableProperty] private bool generateSidecar;

        /// <summary>Overwrite existing files.</summary>
        [ObservableProperty] private bool overwrite;

        /// <summary>Whether (and how) to offer deleting successfully-exported rows from the model.</summary>
        [ObservableProperty] private DeleteAfterExportMode deleteAfterExportMode;

        // ── Scan scope (never remembered — always starts unticked) ──────────

        /// <summary>Scope: CAD links.</summary>
        [ObservableProperty] private bool scanLinked;

        /// <summary>Scope: CAD imports.</summary>
        [ObservableProperty] private bool scanImported;

        /// <summary>Scope: plan views.</summary>
        [ObservableProperty] private bool scanPlan;

        /// <summary>Scope: section / elevation views.</summary>
        [ObservableProperty] private bool scanSection;

        /// <summary>Scope: drafting views.</summary>
        [ObservableProperty] private bool scanDrafting;

        /// <summary>Scope: DWGs with no host view.</summary>
        [ObservableProperty] private bool scanNoViews;

        // ── Grid ─────────────────────────────────────────────────────────────

        /// <summary>All scanned rows.</summary>
        public BulkObservableCollection<DwgRowViewModel> Rows { get; } = new BulkObservableCollection<DwgRowViewModel>();

        /// <summary>Grouped (Source → View type), sorted and filtered view of <see cref="Rows"/>.</summary>
        public ListCollectionView RowsView { get; }

        /// <summary>Grid filter text; every word must match.</summary>
        [ObservableProperty] private string filterText = string.Empty;

        /// <summary>Rows ticked for export (shown or not).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(GridSummaryText), nameof(ExportButtonText))]
        private int selectedCount;

        /// <summary>Rows passing the filter.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(GridSummaryText))]
        private int shownCount;

        /// <summary>Header tick box: true = all shown rows ticked, false = none, null = some.</summary>
        [ObservableProperty] private bool? shownTickState = false;

        /// <summary>"14 items · 14 shown · 3 selected".</summary>
        public string GridSummaryText => Rows.Count == 0
            ? "Nothing scanned yet"
            : $"{Rows.Count:N0} items · {ShownCount:N0} shown · {SelectedCount:N0} selected";

        /// <summary>Footer primary button caption.</summary>
        public string ExportButtonText => IsExporting ? "Exporting…" : $"Export ({SelectedCount:N0})";

        // ── State ────────────────────────────────────────────────────────────

        /// <summary>True while a scan, load or export runs.</summary>
        [ObservableProperty] private bool isBusy;

        /// <summary>True while an export runs.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ExportButtonText))]
        private bool isExporting;

        /// <summary>Footer status line.</summary>
        [ObservableProperty] private string statusMessage = "Tick a scan scope, then click Scan.";

        /// <summary>Real-time log.</summary>
        public ObservableCollection<LogEntry> LogEntries { get; } = new ObservableCollection<LogEntry>();

        /// <summary>Raised after the grid rows are replaced (new scan / reset) so the window can reset group state.</summary>
        public event EventHandler RowsReloaded;

        /// <summary>Creates the view model. Loads remembered options; does NOT scan.</summary>
        public ExportDwgToFolderViewModel(DwgExportEventHandler handler, ExternalEvent externalEvent)
        {
            _handler = handler;
            _externalEvent = externalEvent;

            // Load settings into backing fields (not properties) so On*Changed
            // doesn't fire during construction.
            _settings = SettingsService<ExportDwgSettings>.Load(ToolName);
            outputFolder = _settings.LastOutputFolder ?? string.Empty;
            subfoldersByViewType = _settings.SubfoldersByViewType;
            generateSidecar = _settings.GenerateSidecar;
            overwrite = _settings.Overwrite;
            deleteAfterExportMode = _settings.DeleteAfterExportMode;

            SummaryCards.Add(_cardScanned);
            SummaryCards.Add(_cardLinked);
            SummaryCards.Add(_cardImported);
            SummaryCards.Add(_cardReady);
            SummaryCards.Add(_cardIssues);
            SummaryCards.Add(_cardSelected);

            Phases.Add(ScanPhase);
            Phases.Add(LoadPhase);
            Phases.Add(ExportPhase);

            RowsView = new ListCollectionView(Rows);
            RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DwgRowViewModel.SourceGroup)));
            RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DwgRowViewModel.ViewGroup)));
            RowsView.Filter = FilterRow;
            ApplySortDescriptions();

            _handler.RequestCompleted += OnRequestCompleted;
            _handler.ScanProgress += OnScanProgress;
            _handler.ExportItemStarted += OnExportItemStarted;
            _handler.ExportItemFinished += OnExportItemFinished;

            AddLog(LogLevel.Info, "Ready. Nothing is scanned until you choose a scope and click Scan.");
        }

        // ── Commands ─────────────────────────────────────────────────────────

        /// <summary>Scans the document within the ticked scope (also used by Refresh).</summary>
        [RelayCommand(CanExecute = nameof(CanScan))]
        private void Scan()
        {
            var scope = BuildScope();

            LogSection("Scan");
            AddLog(LogLevel.Info, $"Scope — {scope}");

            ReplaceRows(Enumerable.Empty<DwgRowViewModel>());
            ScanPhase.Start(0);
            LoadPhase.Reset();
            ExportPhase.Reset();

            _handler.Scope = scope;
            Raise(DwgExportRequest.Scan, "Scanning…");
        }

        /// <summary>Picks the output folder.</summary>
        [RelayCommand(CanExecute = nameof(NotBusy))]
        private void BrowseOutputFolder()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select the output folder for the exported DWGs",
                Multiselect = false
            };
            if (!string.IsNullOrEmpty(OutputFolder) && Directory.Exists(OutputFolder))
                dlg.InitialDirectory = OutputFolder;

            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
                OutputFolder = dlg.FolderName;
        }

        /// <summary>Exports the ticked rows.</summary>
        [RelayCommand(CanExecute = nameof(CanExport))]
        private void Export()
        {
            var rows = Rows
                .Where(r => r.IsSelected)
                .OrderBy(r => r.SourceOrder)
                .ThenBy(r => r.ViewOrder)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (rows.Count == 0) return;

            SaveSettings();
            LogSection("Export");
            AddLog(LogLevel.Info, $"Output folder: {OutputFolder}");
            AddLog(LogLevel.Info, $"{rows.Count:N0} ticked DWG(s) | subfolders {(SubfoldersByViewType ? "ON" : "OFF")} | " +
                                  $"overwrite {(Overwrite ? "ON" : "OFF")} | sidecar {(GenerateSidecar ? "ON" : "OFF")}");
            int hidden = rows.Count(r => !FilterRow(r));
            if (hidden > 0)
                AddLog(LogLevel.Info, $"{hidden:N0} ticked row(s) are hidden by the filter and are exported too.");

            foreach (var row in rows)
                row.SetStatus(DwgRowStatus.Queued);

            _exportRows = rows;
            ExportPhase.Start(rows.Count);

            _handler.Settings = BuildSettings();
            _handler.OutputFolder = OutputFolder;
            _handler.ToExport = rows.Select(r => r.Info).ToList();

            IsExporting = true;
            Raise(DwgExportRequest.Export, $"Exporting 0 of {rows.Count:N0}…");
        }

        /// <summary>Clears the scope, the list and the progress bars. Output folder and options stay.</summary>
        [RelayCommand(CanExecute = nameof(NotBusy))]
        private void Reset()
        {
            ScanLinked = ScanImported = false;
            ScanPlan = ScanSection = ScanDrafting = ScanNoViews = false;
            FilterText = string.Empty;
            ReplaceRows(Enumerable.Empty<DwgRowViewModel>());
            ScanPhase.Reset();
            LoadPhase.Reset();
            ExportPhase.Reset();
            LogSection("Reset");
            StatusMessage = "Tick a scan scope, then click Scan.";
        }

        /// <summary>Ticks every row that passes the filter.</summary>
        [RelayCommand(CanExecute = nameof(CanTickShown))]
        private void SelectAllShown() => SetShown(true);

        /// <summary>Unticks every row that passes the filter.</summary>
        [RelayCommand(CanExecute = nameof(CanTickShown))]
        private void SelectNoneShown() => SetShown(false);

        /// <summary>Header tick box: all shown ticked → untick them, otherwise tick them all.</summary>
        [RelayCommand(CanExecute = nameof(CanTickShown))]
        private void ToggleShown() => SetShown(ShownTickState != true);

        /// <summary>Ask / Always / Never chip row for the post-export delete prompt.</summary>
        [RelayCommand]
        private void SetDeleteAfterExportMode(string mode)
        {
            if (Enum.TryParse<DeleteAfterExportMode>(mode, out var parsed))
                DeleteAfterExportMode = parsed;
        }

        /// <summary>Copies the whole log to the clipboard.</summary>
        [RelayCommand]
        private void CopyLog()
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, LogEntries.Select(l => l.ToString())));
                StatusMessage = $"Copied {LogEntries.Count} log line(s).";
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Warning, $"Clipboard error: {ex.Message}");
            }
        }

        // ── Public helpers used by the window ────────────────────────────────

        /// <summary>Space bar / tick click on several rows: tick them all, or untick if all are ticked.</summary>
        public void ToggleRows(IEnumerable<DwgRowViewModel> rows)
        {
            if (IsBusy) return;
            var list = rows?.ToList() ?? new List<DwgRowViewModel>();
            if (list.Count == 0) return;

            bool target = list.Any(r => !r.IsSelected);
            _bulkSelecting = true;
            foreach (var row in list) row.IsSelected = target;
            _bulkSelecting = false;
            RecountSelection();
        }

        /// <summary>Sorts within the groups by <paramref name="member"/>.</summary>
        public void ApplySort(string member, ListSortDirection direction)
        {
            if (string.IsNullOrEmpty(member)) return;
            _sortMember = member;
            _sortDirection = direction;
            ApplySortDescriptions();
        }

        /// <summary>Current sort column member path.</summary>
        public string SortMember => _sortMember;

        /// <summary>Current sort direction.</summary>
        public ListSortDirection SortDirection => _sortDirection;

        /// <summary>Saves the output folder and export options (called after Export and on close).</summary>
        public void SaveSettings()
        {
            try { SettingsService<ExportDwgSettings>.Save(ToolName, BuildSettings()); }
            catch (Exception ex) { AddLog(LogLevel.Warning, $"Could not save settings: {ex.Message}"); }
        }

        // ── Property-change callbacks ────────────────────────────────────────

        partial void OnOutputFolderChanged(string value) => ExportCommand.NotifyCanExecuteChanged();
        partial void OnScanLinkedChanged(bool value) => ScanCommand.NotifyCanExecuteChanged();
        partial void OnScanImportedChanged(bool value) => ScanCommand.NotifyCanExecuteChanged();
        partial void OnScanPlanChanged(bool value) => ScanCommand.NotifyCanExecuteChanged();
        partial void OnScanSectionChanged(bool value) => ScanCommand.NotifyCanExecuteChanged();
        partial void OnScanDraftingChanged(bool value) => ScanCommand.NotifyCanExecuteChanged();
        partial void OnScanNoViewsChanged(bool value) => ScanCommand.NotifyCanExecuteChanged();

        partial void OnFilterTextChanged(string value)
        {
            RowsView.Refresh();
            RecountSelection();
        }

        partial void OnIsBusyChanged(bool value)
        {
            ScanCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
            ResetCommand.NotifyCanExecuteChanged();
            BrowseOutputFolderCommand.NotifyCanExecuteChanged();
            SelectAllShownCommand.NotifyCanExecuteChanged();
            SelectNoneShownCommand.NotifyCanExecuteChanged();
            ToggleShownCommand.NotifyCanExecuteChanged();
        }

        // ── Handler callbacks (raised on Revit's thread = this window's thread) ─

        private void OnScanProgress(string stage, int current, int total)
        {
            OnUi(() =>
            {
                ScanPhase.Report(current, total);
                StatusMessage = stage == DwgCollectorService.StageFindingViews
                    ? $"Finding views for model-space imports… {current:N0} of {total:N0}"
                    : $"Scanning DWG {current:N0} of {total:N0}…";
                Pump(current >= total);
            });
        }

        private void OnExportItemStarted(int index, int total)
        {
            OnUi(() =>
            {
                if (index < 0 || index >= _exportRows.Count) return;
                var row = _exportRows[index];
                row.SetStatus(DwgRowStatus.Exporting);
                StatusMessage = $"Exporting {index + 1:N0} of {total:N0} — {row.OutputFileName}";
                Pump(force: true); // an import export can take seconds — show which one
            });
        }

        private void OnExportItemFinished(int index, int total, string error)
        {
            OnUi(() =>
            {
                if (index < 0 || index >= _exportRows.Count) return;
                var row = _exportRows[index];
                string relative = Path.Combine(
                    DwgExportEventHandler.BuildRelativeFolder(row.Info, SubfoldersByViewType), row.OutputFileName);

                if (error == null)
                {
                    row.SetStatus(DwgRowStatus.Exported, relative);
                    AddLog(LogLevel.Success, $"✔  {relative}");
                }
                else if (error == "SKIP")
                {
                    row.SetStatus(DwgRowStatus.Skipped, "File exists — Overwrite is off");
                    AddLog(LogLevel.Warning, $"◌  Skipped (file exists): {relative}");
                }
                else
                {
                    row.SetStatus(DwgRowStatus.Failed, error);
                    AddLog(LogLevel.Error, $"✘  {row.OutputFileName} — {error}");
                }

                ExportPhase.Report(index + 1, total);
                Pump(index + 1 >= total);
            });
        }

        private void OnRequestCompleted()
        {
            OnUi(() =>
            {
                switch (_handler.Request)
                {
                    case DwgExportRequest.Scan:
                        CompleteScan();
                        break;
                    case DwgExportRequest.Export:
                        CompleteExport();
                        break;
                    case DwgExportRequest.DeleteElements:
                        CompleteDelete();
                        break;
                }
            });
        }

        // ── Completion ───────────────────────────────────────────────────────

        private void CompleteScan()
        {
            try
            {
                if (!_handler.LastRunSucceeded)
                {
                    ScanPhase.Complete(stopped: true);
                    ReportCriticalError("Scan stopped", _handler.ErrorMessage);
                    return;
                }

                var items = _handler.ScannedInstances ?? new List<DwgInstanceInfo>();
                ScanPhase.Complete();
                int linked = items.Count(i => i.SourceType == DwgSourceType.Link);
                AddLog(LogLevel.Info, $"Found {items.Count:N0} DWG instance(s) in scope — {linked:N0} linked, {items.Count - linked:N0} imported.");

                // Loading: build each row (pre-checks the linked source file / host view).
                LoadPhase.Start(items.Count);
                StatusMessage = "Loading list…";
                Pump(force: true);

                var rows = new List<DwgRowViewModel>(items.Count);
                for (int i = 0; i < items.Count; i++)
                {
                    rows.Add(new DwgRowViewModel(items[i]));
                    LoadPhase.Report(i + 1, items.Count);
                    StatusMessage = $"Loading list… {i + 1:N0} of {items.Count:N0}";
                    Pump(i + 1 >= items.Count);
                }

                ReplaceRows(rows);
                LoadPhase.Complete();

                foreach (var row in rows.Where(r => r.HasIssue))
                    AddLog(LogLevel.Warning, $"⚠  {row.Name} — {row.StatusNote}" +
                                             (row.Info.LinkedSourcePath != null ? $": {row.Info.LinkedSourcePath}" : string.Empty));

                int issues = rows.Count(r => r.HasIssue);
                AddLog(LogLevel.Success, $"Listed {rows.Count:N0} DWG(s) — {rows.Count - issues:N0} ready, {issues:N0} with issues. All rows start unticked.");
                StatusMessage = rows.Count > 0
                    ? $"{rows.Count:N0} DWG(s) found — tick the ones to export."
                    : "No DWG instances found in the selected scope.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void CompleteExport()
        {
            bool promptDelete = false;
            try
            {
                if (!_handler.LastRunSucceeded)
                {
                    foreach (var row in _exportRows.Where(r => r.Status == DwgRowStatus.Queued || r.Status == DwgRowStatus.Exporting))
                        row.SetStatus(DwgRowStatus.Failed, "Stopped: " + _handler.ErrorMessage);
                    ExportPhase.Complete(stopped: true);
                    ReportCriticalError("Export stopped", _handler.ErrorMessage);
                    return;
                }

                int exported = _handler.ExportedCount;
                int skipped = _handler.SkippedCount;
                int failed = _handler.FailedCount;

                ExportPhase.Complete(withWarnings: failed > 0);
                AddLog(failed > 0 ? LogLevel.Warning : LogLevel.Success,
                    $"Done — {exported:N0} exported | {skipped:N0} skipped | {failed:N0} failed");
                StatusMessage = $"{exported:N0} exported | {skipped:N0} skipped | {failed:N0} failed";

                OnUi(() => LogExportService.AutoSave(LogEntries, ToolCatalog.ExportDwgToFolder.Title));

                promptDelete = exported > 0;
            }
            finally
            {
                IsExporting = false;
                IsBusy = false;
            }

            // Asked outside the finally/try above so IsBusy is already back to false while the
            // MessageBox is up, then set back to true only if the user says Yes.
            if (promptDelete)
                PromptDeleteExported();
        }

        /// <summary>
        /// Asks whether to delete the rows that were just exported successfully. Skipped and failed
        /// rows are never offered — only rows whose DWG actually got written this run. Honors the
        /// remembered <see cref="DeleteAfterExportMode"/> (Ask / Always / Never) so a user who
        /// doesn't want the prompt every run can turn it off.
        /// </summary>
        private void PromptDeleteExported()
        {
            var exportedRows = _exportRows.Where(r => r.Status == DwgRowStatus.Exported).ToList();
            if (exportedRows.Count == 0) return;

            List<DwgRowViewModel> toDelete;
            switch (DeleteAfterExportMode)
            {
                case DeleteAfterExportMode.Never:
                    AddLog(LogLevel.Info,
                        $"Delete after export is set to Never — {exportedRows.Count:N0} exported CAD instance(s) left in the model.");
                    return;

                case DeleteAfterExportMode.Always:
                    toDelete = exportedRows;
                    break;

                default: // Ask
                    toDelete = AskWhichToDelete(exportedRows);
                    break;
            }

            if (toDelete.Count == 0)
            {
                AddLog(LogLevel.Info, "Delete declined — exported CAD instances left in the model.");
                return;
            }

            LogSection("Delete");
            AddLog(LogLevel.Info, $"Deleting {toDelete.Count:N0} successfully-exported CAD instance(s)…");

            _deleteRows = toDelete;
            _handler.ToDelete = toDelete.Select(r => r.Info.ElementId).ToList();
            Raise(DwgExportRequest.DeleteElements, $"Deleting {toDelete.Count:N0} exported CAD instance(s)…");
        }

        /// <summary>
        /// The Ask-mode prompt. A batch mixing both source types gets a three-way choice
        /// (Imports only / Links only / All) via a Revit <see cref="TaskDialog"/> with command
        /// links; a batch of just one source type gets the simpler Yes/No. Either way, the text
        /// reminds the user this can be undone with Revit's own Undo afterward.
        /// </summary>
        private static List<DwgRowViewModel> AskWhichToDelete(List<DwgRowViewModel> exportedRows)
        {
            var importRows = exportedRows.Where(r => r.Info.SourceType == DwgSourceType.Import).ToList();
            var linkRows = exportedRows.Where(r => r.Info.SourceType == DwgSourceType.Link).ToList();

            if (importRows.Count > 0 && linkRows.Count > 0)
            {
                var td = new TaskDialog(ToolCatalog.ExportDwgToFolder.Title)
                {
                    MainInstruction = $"{exportedRows.Count:N0} CAD instance(s) were exported successfully.",
                    MainContent =
                        "Delete which of these from the model now? Skipped and failed rows are never touched.\n\n" +
                        "This can be undone afterward with Revit's own Undo (Ctrl+Z).",
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Cancel
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Delete imports only ({importRows.Count:N0})");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"Delete links only ({linkRows.Count:N0})");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, $"Delete all ({exportedRows.Count:N0})");

                return td.Show() switch
                {
                    TaskDialogResult.CommandLink1 => importRows,
                    TaskDialogResult.CommandLink2 => linkRows,
                    TaskDialogResult.CommandLink3 => exportedRows,
                    _ => new List<DwgRowViewModel>()
                };
            }

            var result = MessageBox.Show(
                $"{exportedRows.Count:N0} CAD instance(s) were exported successfully.\n\n" +
                "Delete these from the Revit model now? Skipped and failed rows are left untouched.\n\n" +
                "This can be undone afterward with Revit's own Undo (Ctrl+Z).",
                ToolCatalog.ExportDwgToFolder.Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            return result == MessageBoxResult.Yes ? exportedRows : new List<DwgRowViewModel>();
        }

        private void CompleteDelete()
        {
            try
            {
                if (!_handler.LastRunSucceeded)
                {
                    ReportCriticalError("Delete stopped", _handler.ErrorMessage);
                    return;
                }

                var failedIds = new HashSet<long>(_handler.FailedDeleteIds);
                int deleted = 0, failed = 0;

                foreach (var row in _deleteRows)
                {
                    if (failedIds.Contains(row.ElementIdValue))
                    {
                        row.SetStatus(DwgRowStatus.Exported, "Exported, but could not be deleted from the model");
                        failed++;
                    }
                    else
                    {
                        row.SetStatus(DwgRowStatus.Deleted, "Removed from the model");
                        deleted++;
                    }
                }

                int dependent = _handler.DependentDeletedCount;
                string dependentSuffix = dependent > 0 ? $", plus {dependent:N0} dependent element(s)" : "";

                AddLog(failed > 0 ? LogLevel.Warning : LogLevel.Success,
                    $"Deleted {deleted:N0} CAD instance(s) from the model{dependentSuffix}" +
                    (failed > 0 ? $" — {failed:N0} could not be deleted." : "."));
                if (deleted > 0)
                    AddLog(LogLevel.Info, "You can undo this in Revit (Ctrl+Z) if needed.");

                StatusMessage = $"Deleted {deleted:N0} CAD instance(s) from the model{dependentSuffix}" +
                    (failed > 0 ? $" ({failed:N0} failed)." : ".");
            }
            finally
            {
                IsBusy = false;
                _deleteRows = new List<DwgRowViewModel>();
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private bool NotBusy() => !IsBusy;

        private bool CanScan() => !IsBusy && BuildScope().IsValid;

        private bool CanExport()
            => !IsBusy
            && SelectedCount > 0
            && !string.IsNullOrWhiteSpace(OutputFolder);

        private bool CanTickShown() => !IsBusy && ShownCount > 0;

        private DwgScanScope BuildScope() => new DwgScanScope
        {
            Linked = ScanLinked,
            Imported = ScanImported,
            Plan = ScanPlan,
            Section = ScanSection,
            Drafting = ScanDrafting,
            NoViews = ScanNoViews
        };

        private ExportDwgSettings BuildSettings()
        {
            // Start from the loaded file so the ignored v1 fields round-trip unchanged.
            _settings.Version = 3;
            _settings.LastOutputFolder = OutputFolder ?? string.Empty;
            _settings.SubfoldersByViewType = SubfoldersByViewType;
            _settings.GenerateSidecar = GenerateSidecar;
            _settings.Overwrite = Overwrite;
            _settings.DeleteAfterExportMode = DeleteAfterExportMode;
            return _settings;
        }

        private void Raise(DwgExportRequest request, string status)
        {
            IsBusy = true;
            StatusMessage = status;
            _handler.Request = request;

            var result = _externalEvent.Raise();
            if (result == ExternalEventRequest.Accepted)
                return;

            // Revit refused the request (e.g. it is in a modal state) — don't stay busy forever.
            AddLog(LogLevel.Warning, $"Revit did not accept the request ({result}). Close any open dialog in Revit and try again.");
            switch (request)
            {
                case DwgExportRequest.Scan:
                    ScanPhase.Reset();
                    break;
                case DwgExportRequest.Export:
                    foreach (var row in _exportRows) row.PreCheck();
                    ExportPhase.Reset();
                    IsExporting = false;
                    break;
                case DwgExportRequest.DeleteElements:
                    _deleteRows = new List<DwgRowViewModel>();
                    break;
            }
            StatusMessage = "Revit is busy — try again.";
            IsBusy = false;
        }

        private void ReplaceRows(IEnumerable<DwgRowViewModel> rows)
        {
            foreach (var old in Rows)
                old.PropertyChanged -= OnRowPropertyChanged;

            var list = rows.ToList();
            foreach (var row in list)
                row.PropertyChanged += OnRowPropertyChanged;

            Rows.ReplaceAll(list);
            _exportRows = new List<DwgRowViewModel>();
            _deleteRows = new List<DwgRowViewModel>();
            RecountSelection();
            UpdateMetrics();
            OnPropertyChanged(nameof(GridSummaryText));
            RowsReloaded?.Invoke(this, EventArgs.Empty);
        }

        private void OnRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DwgRowViewModel.IsSelected) && !_bulkSelecting)
                RecountSelection();
        }

        private void SetShown(bool selected)
        {
            _bulkSelecting = true;
            foreach (var row in RowsView.Cast<DwgRowViewModel>())
                row.IsSelected = selected;
            _bulkSelecting = false;
            RecountSelection();
        }

        private void RecountSelection()
        {
            SelectedCount = Rows.Count(r => r.IsSelected);

            int shown = 0, shownTicked = 0;
            foreach (var row in RowsView.Cast<DwgRowViewModel>())
            {
                shown++;
                if (row.IsSelected) shownTicked++;
            }
            ShownCount = shown;
            ShownTickState = shownTicked == 0 ? false : shownTicked == shown ? true : (bool?)null;

            _cardSelected.Set(SelectedCount, CardTone.Accent);
            ExportCommand.NotifyCanExecuteChanged();
            SelectAllShownCommand.NotifyCanExecuteChanged();
            SelectNoneShownCommand.NotifyCanExecuteChanged();
            ToggleShownCommand.NotifyCanExecuteChanged();
        }

        private void UpdateMetrics()
        {
            int total = Rows.Count;
            int linked = Rows.Count(r => r.Info.SourceType == DwgSourceType.Link);
            int issues = Rows.Count(r => r.HasIssue);

            _cardScanned.Set(total, CardTone.Accent);
            _cardLinked.Set(linked, CardTone.Neutral);
            _cardImported.Set(total - linked, CardTone.Neutral);
            _cardReady.Set(total - issues, CardTone.Success);
            _cardIssues.Set(issues, issues > 0 ? CardTone.Warning : CardTone.Neutral);
            _cardSelected.Set(SelectedCount, CardTone.Accent);
        }

        private bool FilterRow(object item)
        {
            if (item is not DwgRowViewModel row) return false;
            if (string.IsNullOrWhiteSpace(FilterText)) return true;

            string haystack = row.SearchText;
            return FilterText
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .All(word => haystack.Contains(word.ToLowerInvariant()));
        }

        private void ApplySortDescriptions()
        {
            using (RowsView.DeferRefresh())
            {
                RowsView.SortDescriptions.Clear();
                // Keep the groups in a fixed order: Link → Import, then Plan → Section → Drafting → No Views.
                RowsView.SortDescriptions.Add(new SortDescription(nameof(DwgRowViewModel.SourceOrder), ListSortDirection.Ascending));
                RowsView.SortDescriptions.Add(new SortDescription(nameof(DwgRowViewModel.ViewOrder), ListSortDirection.Ascending));
                RowsView.SortDescriptions.Add(new SortDescription(_sortMember, _sortDirection));
            }
        }

        private void ReportCriticalError(string title, string message)
        {
            AddLog(LogLevel.Error, $"{title}: {message}");
            StatusMessage = $"{title} — see log.";
            MessageBox.Show(
                $"{title}.\n\n{message}",
                ToolCatalog.ExportDwgToFolder.Title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        /// <summary>
        /// Revit runs the scan / export on the window's own thread, so the window can't repaint
        /// until the work returns. This processes pending render and input work (at most every
        /// <see cref="PumpIntervalMs"/> ms) so the progress bars move.
        /// </summary>
        private void Pump(bool force = false)
        {
            var now = DateTime.UtcNow;
            if (!force && (now - _lastPump).TotalMilliseconds < PumpIntervalMs) return;
            _lastPump = now;

            try { _dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { })); }
            catch { /* repaint is best-effort */ }
        }

        private void OnUi(Action action)
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.Invoke(action);
        }

        private void LogSection(string title)
            => AddLog(LogLevel.Debug, $"──────── {title} ────────");

        private void AddLog(LogLevel level, string message)
        {
            var entry = new LogEntry(level, message);
            if (_dispatcher.CheckAccess()) LogEntries.Add(entry);
            else _dispatcher.Invoke(() => LogEntries.Add(entry));
        }
    }
}
