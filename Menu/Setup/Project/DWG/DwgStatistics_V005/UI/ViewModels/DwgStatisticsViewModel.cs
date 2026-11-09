using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revit26_Plugin_Revised.DwgStatistics.V005.Core.Models;
using Revit26_Plugin_Revised.DwgStatistics.V005.Core.Services;
using Revit26_Plugin_Revised.DwgStatistics.V005.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.DwgStatistics.V005.Infrastructure.RevitApi;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;

namespace Revit26_Plugin_Revised.DwgStatistics.V005.UI.ViewModels
{
    /// <summary>
    /// View model of the DWG Statistics window. Every Revit read or write goes through the tool's
    /// external event; the grid, filters and export work on the last scan in memory.
    /// V004: batched deep scan with progress + Cancel, open options, Hide / Unhide / Delete in ticked
    /// views, comparison with the last scan, remembered settings.
    /// </summary>
    public partial class DwgStatisticsViewModel : ToolViewModelBase
    {
        private readonly Document _doc;
        private readonly DwgStatisticsHandler _handler = new();
        private readonly ExternalEvent _externalEvent;
        private readonly Dispatcher _dispatcher;
        private readonly DwgToolSettings _settings;

        // Queued actions that have not finished; the window shows "running" while above zero.
        private int _busy;

        // True while this view model ticks rows itself, so the counts are recomputed once.
        private bool _suspend;

        // True while settings are applied in the constructor (no save on every property).
        private bool _loadingSettings;

        // Last scan, kept for the export's Scan Info sheet.
        private DwgScanResult _lastScan;
        private DwgProjectTotals _totals = new DwgProjectTotals();

        // V004 scan session state (UI thread only).
        private DwgScanSession _session;
        private bool _cancelRequested;

        // True once the window closed: no new scan may start (the external event is disposed).
        private bool _closed;

        // V004 baseline for "Compare with last scan": loaded once per window, from the previous session.
        private DwgSnapshot _baseline;
        private bool _baselineLoaded;

        // V005 shared history: what was loaded (log lines) and where it lives.
        private DwgHistoryFile _history;
        private DwgHistoryLocation _historyLocation;

        // V005: instance ID → workset ID from the last scan (Move to Workset counts).
        private Dictionary<long, int> _worksetByInstance = new Dictionary<long, int>();

        // The file row whose view ticks drive the Views-tab commands.
        private DwgFileRowViewModel _watchedFile;

        /// <summary>All file rows.</summary>
        public ObservableCollection<DwgFileRowViewModel> Files { get; } = new();

        /// <summary>File rows that pass the quick filter and the search box.</summary>
        public ICollectionView FilesView { get; }

        /// <summary>V004: changes since the last scan (new / removed / changed files).</summary>
        public ObservableCollection<DwgChange> Changes { get; } = new();

        /// <summary>V004: choices for the open-views limit.</summary>
        public IReadOnlyList<int> MaxOpenViewsOptions { get; } = new[] { 1, 2, 3, 5, 8, 10, 15, 20 };

        /// <summary>Active quick-filter chip.</summary>
        [ObservableProperty] private DwgQuickFilter activeQuickFilter = DwgQuickFilter.All;

        /// <summary>Shows only files whose name, status, kind, path or issues contain every word typed.</summary>
        [ObservableProperty] private string searchText = string.Empty;

        /// <summary>File shown in the detail tabs.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedFile))]
        private DwgFileRowViewModel selectedFile;

        /// <summary>True when a file is selected (detail tabs visible).</summary>
        public bool HasSelectedFile => SelectedFile != null;

        // ── Header tiles ─────────────────────────────────────────────────────

        /// <summary>DWG files.</summary>
        [ObservableProperty] private int totalFiles;

        /// <summary>Linked files.</summary>
        [ObservableProperty] private int linkCount;

        /// <summary>Imported files.</summary>
        [ObservableProperty] private int importCount;

        /// <summary>CAD instances.</summary>
        [ObservableProperty] private int totalInstances;

        /// <summary>Views that own or draw CAD.</summary>
        [ObservableProperty] private int viewsUsed;

        /// <summary>Sheets that show CAD.</summary>
        [ObservableProperty] private int sheetsUsed;

        /// <summary>Links not loaded.</summary>
        [ObservableProperty] private int notLoadedCount;

        /// <summary>Files with warning/danger issues.</summary>
        [ObservableProperty] private int issueFileCount;

        /// <summary>Unused types.</summary>
        [ObservableProperty] private int unusedCount;

        /// <summary>V004: files new or changed since the last scan.</summary>
        [ObservableProperty] private int changedCount;

        /// <summary>Hint under the toolbar (views scanned, time).</summary>
        [ObservableProperty] private string scanInfo = "Scanning…";

        // ── V004: scan progress ──────────────────────────────────────────────

        /// <summary>True while a deep scan is running (Cancel visible, bar shows %).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
        private bool isScanning;

        /// <summary>Indeterminate bar for actions; real % while scanning.</summary>
        public bool IsProgressIndeterminate => !IsScanning;

        /// <summary>"120 / 480 views".</summary>
        [ObservableProperty] private string scanProgressText = string.Empty;

        // ── V004: remembered options ─────────────────────────────────────────

        /// <summary>Most views Open Ticked Views opens at once.</summary>
        [ObservableProperty] private int maxOpenViews = DwgStatisticsTool.DefaultMaxOpenViews;

        /// <summary>Close every other open view after opening the ticked ones.</summary>
        [ObservableProperty] private bool closeOtherViews;

        /// <summary>Open the sheet a view is placed on instead of the view.</summary>
        [ObservableProperty] private bool openSheetInstead;

        /// <summary>Select and zoom to the DWG in each opened view.</summary>
        [ObservableProperty] private bool zoomToDwg = true;

        /// <summary>Delete in Ticked Views also purges the type when nothing is left.</summary>
        [ObservableProperty] private bool purgeAfterDelete;

        /// <summary>Selected detail tab (0 Views, 1 Instances).</summary>
        [ObservableProperty] private int selectedDetailTab;

        /// <summary>"Changes since last scan" card expanded.</summary>
        [ObservableProperty] private bool changesExpanded = true;

        /// <summary>V004: "Compared with the scan of 2026-10-10 14:02" / first-scan note.</summary>
        [ObservableProperty] private string baselineText = string.Empty;

        // ── V005: rules, shared history, worksets ────────────────────────────

        /// <summary>Far-from-origin limit (km).</summary>
        [ObservableProperty] private double farFromOriginKm = 10;

        /// <summary>Layer-count limit.</summary>
        [ObservableProperty] private int maxLayers = 100;

        /// <summary>CAD workset names (semicolon-separated "contains" patterns).</summary>
        [ObservableProperty] private string cadWorksetPattern = "CAD;Link";

        /// <summary>Keep the scan history next to the project file (shared with the team).</summary>
        [ObservableProperty] private bool sharedHistory = true;

        /// <summary>True for a workshared model (workset rule + Move to Workset shown).</summary>
        [ObservableProperty] private bool isWorkshared;

        /// <summary>User worksets of the model.</summary>
        public ObservableCollection<WorksetFact> Worksets { get; } = new();

        /// <summary>Target of Move to Workset.</summary>
        [ObservableProperty] private WorksetFact selectedWorkset;

        /// <summary>Model-placed instances of the ticked files not yet on the selected workset.</summary>
        [ObservableProperty] private int tickedMovable;

        /// <summary>"Move (4)".</summary>
        public string MoveToWorksetLabel => $"Move ({TickedMovable})";

        /// <summary>"Hide 3 layer(s) in 2 ticked view(s)".</summary>
        public string HideLayersLabel => $"Hide {SelectedFile?.TickedLayerCount ?? 0} layer(s) in {TickedViews} ticked view(s)";

        /// <summary>"Unhide 3 layer(s) in 2 ticked view(s)".</summary>
        public string UnhideLayersLabel => $"Unhide {SelectedFile?.TickedLayerCount ?? 0} layer(s) in {TickedViews} ticked view(s)";

        /// <summary>Hint in the Layers tab.</summary>
        public string LayerTickHint => TickedViews == 0
            ? "Tick layers here and views in the Views tab, then Hide / Unhide."
            : $"Acts on the {TickedViews} view(s) ticked in the Views tab.";

        /// <summary>The rules currently in force.</summary>
        public DwgRules CurrentRules => new DwgRules
        {
            FarFromOriginKm = FarFromOriginKm,
            MaxLayers = MaxLayers,
            CadWorksetPattern = CadWorksetPattern
        }.Normalized();

        // ── Chip labels ──────────────────────────────────────────────────────

        /// <summary>"All (12)".</summary>
        public string AllChipText => $"All ({TotalFiles})";

        /// <summary>"Links (8)".</summary>
        public string LinksChipText => $"Links ({LinkCount})";

        /// <summary>"Imports (4)".</summary>
        public string ImportsChipText => $"Imports ({ImportCount})";

        /// <summary>"Not loaded (1)".</summary>
        public string NotLoadedChipText => $"Not loaded ({NotLoadedCount})";

        /// <summary>"Unused (2)".</summary>
        public string UnusedChipText => $"Unused ({UnusedCount})";

        /// <summary>"Has issues (5)".</summary>
        public string IssuesChipText => $"Has issues ({IssueFileCount})";

        /// <summary>V004: "Changed (3)".</summary>
        public string ChangedChipText => $"Changed ({ChangedCount})";

        /// <summary>V004: header of the changes card.</summary>
        public string ChangesHeader => Changes.Count == 0
            ? "Changes since last scan — none"
            : $"Changes since last scan ({Changes.Count})";

        // ── File ticks and action labels ─────────────────────────────────────

        /// <summary>Ticked rows.</summary>
        [ObservableProperty] private int tickedCount;

        /// <summary>Unpinned instances among the ticked files.</summary>
        [ObservableProperty] private int tickedUnpinned;

        /// <summary>Linked files among the ticked files.</summary>
        [ObservableProperty] private int tickedLinks;

        /// <summary>Unused types among the ticked files.</summary>
        [ObservableProperty] private int tickedUnused;

        /// <summary>"Pin All (3)".</summary>
        public string PinAllLabel => $"Pin All ({TickedUnpinned})";

        /// <summary>"Reload Links (2)".</summary>
        public string ReloadLabel => $"Reload Links ({TickedLinks})";

        /// <summary>"Purge Unused (1)".</summary>
        public string PurgeLabel => $"Purge Unused ({TickedUnused})";

        /// <summary>Footer hint.</summary>
        public string TickHint => TickedCount == 0
            ? "Tick files to pin, reload or purge them. Click a file to see its views and instances."
            : $"{TickedCount} file(s) ticked · each action is one undo step (Ctrl+Z in Revit).";

        // ── View ticks and labels (selected file) ────────────────────────────

        private int TickedViews => SelectedFile?.TickedViewCount ?? 0;

        /// <summary>"Open Ticked Views (2 / 5)".</summary>
        public string OpenTickedViewsLabel => $"Open Ticked Views ({TickedViews} / {MaxOpenViews})";

        /// <summary>"Hide in Ticked (2)": ticked views that draw the file.</summary>
        public string HideLabel => $"Hide in Ticked ({TickedViewRows().Count(r => r.Usage.VisibleInstances > 0)})";

        /// <summary>"Unhide in Ticked (1)": ticked views where it is hidden.</summary>
        public string UnhideLabel => $"Unhide in Ticked ({TickedViewRows().Count(r => r.Usage.HiddenInstances > 0)})";

        /// <summary>"Delete in Ticked (1)": ticked views that own a 'Current view only' instance.</summary>
        public string DeleteLabel => $"Delete in Ticked ({TickedViewRows().Count(r => r.Usage.PlacedInstances > 0)})";

        /// <summary>Hint next to Open Ticked Views.</summary>
        public string ViewTickHint => TickedViews > MaxOpenViews
            ? $"Too many ticked — Open takes up to {MaxOpenViews} at a time (Hide / Unhide / Delete work on any number)."
            : $"Tick views, then Open, Hide, Unhide or Delete. Open takes up to {MaxOpenViews} at a time.";

        /// <summary>
        /// Creates the view model for <paramref name="doc"/>. The first scan is started by the window
        /// (RefreshCommand on ContentRendered) so the window is visible while it runs.
        /// </summary>
        public DwgStatisticsViewModel(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _dispatcher = Dispatcher.CurrentDispatcher;
            _externalEvent = ExternalEvent.Create(_handler);

            var view = CollectionViewSource.GetDefaultView(Files);
            view.Filter = obj => obj is DwgFileRowViewModel row && PassesFilters(row);
            FilesView = view;

            // V004: remembered settings.
            _settings = DwgSettingsStore.Load();
            _loadingSettings = true;
            try
            {
                if (Enum.TryParse(_settings.QuickFilter, out DwgQuickFilter filter)) ActiveQuickFilter = filter;
                MaxOpenViews = _settings.MaxOpenViews;
                CloseOtherViews = _settings.CloseOtherViews;
                OpenSheetInstead = _settings.OpenSheetInstead;
                ZoomToDwg = _settings.ZoomToDwg;
                PurgeAfterDelete = _settings.PurgeAfterDelete;
                SelectedDetailTab = _settings.DetailTab;
                ChangesExpanded = _settings.ChangesExpanded;
                var rules = (_settings.Rules ?? new DwgRules()).Normalized();
                FarFromOriginKm = rules.FarFromOriginKm;
                MaxLayers = rules.MaxLayers;
                CadWorksetPattern = rules.CadWorksetPattern;
                SharedHistory = _settings.SharedHistory;
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        /// <summary>True when this window was opened for <paramref name="doc"/>.</summary>
        public bool IsFor(Document doc) => doc != null && _doc.IsValidObject && doc.Equals(_doc);

        /// <summary>Stops a running scan, saves settings and releases the external event; called when the window closes.</summary>
        public void Shutdown()
        {
            _closed = true;
            _cancelRequested = true;
            SaveSettings();
            try { _externalEvent.Dispose(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { /* Revit already released it */ }
        }

        // ── Property hooks ───────────────────────────────────────────────────

        partial void OnSearchTextChanged(string value) => FilesView?.Refresh();

        partial void OnActiveQuickFilterChanged(DwgQuickFilter value)
        {
            FilesView?.Refresh();
            SaveSettings();
        }

        partial void OnTotalFilesChanged(int value) => OnPropertyChanged(nameof(AllChipText));
        partial void OnLinkCountChanged(int value) => OnPropertyChanged(nameof(LinksChipText));
        partial void OnImportCountChanged(int value) => OnPropertyChanged(nameof(ImportsChipText));
        partial void OnNotLoadedCountChanged(int value) => OnPropertyChanged(nameof(NotLoadedChipText));
        partial void OnUnusedCountChanged(int value) => OnPropertyChanged(nameof(UnusedChipText));
        partial void OnIssueFileCountChanged(int value) => OnPropertyChanged(nameof(IssuesChipText));
        partial void OnChangedCountChanged(int value) => OnPropertyChanged(nameof(ChangedChipText));

        partial void OnMaxOpenViewsChanged(int value)
        {
            NotifyViewCommands();
            SaveSettings();
        }

        partial void OnCloseOtherViewsChanged(bool value) => SaveSettings();
        partial void OnOpenSheetInsteadChanged(bool value) => SaveSettings();
        partial void OnZoomToDwgChanged(bool value) => SaveSettings();
        partial void OnPurgeAfterDeleteChanged(bool value) => SaveSettings();
        partial void OnSelectedDetailTabChanged(int value) => SaveSettings();
        partial void OnChangesExpandedChanged(bool value) => SaveSettings();

        // V005: rule edits re-evaluate the last scan in memory (no rescan needed).
        partial void OnFarFromOriginKmChanged(double value) => RulesChanged();
        partial void OnMaxLayersChanged(int value) => RulesChanged();
        partial void OnCadWorksetPatternChanged(string value) => RulesChanged();
        partial void OnSharedHistoryChanged(bool value) => SaveSettings();
        partial void OnSelectedWorksetChanged(WorksetFact value) => Recount();

        private void RulesChanged()
        {
            if (_loadingSettings) return;
            SaveSettings();
            Guarded("Apply rules", ReapplyRules);
        }

        private void ReapplyRules()
        {
            if (_lastScan == null || IsScanning) return;
            var stats = DwgStatsBuilder.Build(_lastScan, CurrentRules);
            var changes = DwgSnapshotService.Compare(stats, _baseline);
            ShowRows(_lastScan, stats, changes, rulesOnly: true);
        }

        // Follow the selected file's view ticks so the Views-tab commands enable/disable live.
        partial void OnSelectedFileChanged(DwgFileRowViewModel value)
        {
            if (_watchedFile != null) _watchedFile.PropertyChanged -= OnWatchedFileChanged;
            _watchedFile = value;
            if (_watchedFile != null) _watchedFile.PropertyChanged += OnWatchedFileChanged;
            NotifyViewCommands();
        }

        private void OnWatchedFileChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DwgFileRowViewModel.TickedViewCount) ||
                e.PropertyName == nameof(DwgFileRowViewModel.TickedLayerCount)) NotifyViewCommands();
        }

        private void NotifyViewCommands()
        {
            OnPropertyChanged(nameof(OpenTickedViewsLabel));
            OnPropertyChanged(nameof(HideLabel));
            OnPropertyChanged(nameof(UnhideLabel));
            OnPropertyChanged(nameof(DeleteLabel));
            OnPropertyChanged(nameof(ViewTickHint));
            OpenTickedViewsCommand.NotifyCanExecuteChanged();
            TickAllViewsCommand.NotifyCanExecuteChanged();
            ClearViewTicksCommand.NotifyCanExecuteChanged();
            HideInTickedViewsCommand.NotifyCanExecuteChanged();
            UnhideInTickedViewsCommand.NotifyCanExecuteChanged();
            DeleteInTickedViewsCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HideLayersLabel));
            OnPropertyChanged(nameof(UnhideLayersLabel));
            OnPropertyChanged(nameof(LayerTickHint));
            TickAllLayersCommand.NotifyCanExecuteChanged();
            ClearLayerTicksCommand.NotifyCanExecuteChanged();
            HideLayersInTickedViewsCommand.NotifyCanExecuteChanged();
            UnhideLayersInTickedViewsCommand.NotifyCanExecuteChanged();
        }

        private void SaveSettings()
        {
            if (_loadingSettings || _settings == null) return;
            _settings.QuickFilter = ActiveQuickFilter.ToString();
            _settings.MaxOpenViews = MaxOpenViews;
            _settings.CloseOtherViews = CloseOtherViews;
            _settings.OpenSheetInstead = OpenSheetInstead;
            _settings.ZoomToDwg = ZoomToDwg;
            _settings.PurgeAfterDelete = PurgeAfterDelete;
            _settings.DetailTab = SelectedDetailTab;
            _settings.ChangesExpanded = ChangesExpanded;
            _settings.Rules = CurrentRules;
            _settings.SharedHistory = SharedHistory;
            DwgSettingsStore.Save(_settings);
        }

        // ── Filtering ────────────────────────────────────────────────────────

        /// <summary>Activates a quick-filter chip ("All", "Links", …).</summary>
        [RelayCommand]
        private void SetQuickFilter(string name)
        {
            if (Enum.TryParse(name, out DwgQuickFilter filter)) ActiveQuickFilter = filter;
        }

        private bool PassesFilters(DwgFileRowViewModel row)
        {
            if (!DwgStatsBuilder.Passes(row.Stats, ActiveQuickFilter)) return false;
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            string haystack = $"{row.FileName} {row.Kind} {row.Status} {row.Stats.Path} {row.Stats.IssueText} {row.ChangeTag}";
            return SearchText
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .All(word => haystack.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ── V004: batched scan with progress + Cancel ────────────────────────

        /// <summary>
        /// Scans the model again (deep scan of every view, in batches so the window stays responsive).
        /// Ticks and the selected file are kept.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Refresh() => Guarded("Scan", StartScan);

        private bool CanRun() => !IsRunning;

        /// <summary>Stops the running scan after the current batch; the previous results stay on screen.</summary>
        [RelayCommand(CanExecute = nameof(CanCancelScan))]
        private void CancelScan()
        {
            _cancelRequested = true;
            ScanProgressText = "Cancelling…";
            CancelScanCommand.NotifyCanExecuteChanged();
        }

        private bool CanCancelScan() => IsScanning && !_cancelRequested;

        private void StartScan()
        {
            if (IsScanning || _closed) return;

            _cancelRequested = false;
            _session = null;
            IsScanning = true;
            Progress = 0;
            ScanProgressText = "Reading CAD types and instances…";
            ScanInfo = "Scanning every view for CAD…";
            SetBusy(+1, "Scanning CAD links, imports and every view…");
            CancelScanCommand.NotifyCanExecuteChanged();

            RaiseScanStep(app =>
            {
                if (!TryGetUiDocument(app, out var uidoc))
                {
                    OnUi(() => EndScan("Scan stopped: the active document changed."));
                    return;
                }
                var session = DwgStatsScanner.Begin(uidoc.Document, LogFromRevit);
                OnUi(() =>
                {
                    _session = session;
                    UpdateScanProgress();
                    ScheduleNextBatch();
                });
            });
        }

        // Posted at Background priority, so WPF renders the new progress (and handles a Cancel click)
        // before Revit runs the next batch.
        private void ScheduleNextBatch()
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Guarded("Scan", () =>
            {
                if (_closed) return;
                if (_cancelRequested)
                {
                    AddLog(LogLevel.Warning, $"Scan cancelled after {_session?.DoneViews ?? 0} of {_session?.TotalViews ?? 0} view(s); the previous results are kept.");
                    EndScan("Scan cancelled.");
                    return;
                }

                var session = _session;
                RaiseScanStep(app =>
                {
                    if (!TryGetUiDocument(app, out var uidoc))
                    {
                        OnUi(() => EndScan("Scan stopped: the active document changed."));
                        return;
                    }
                    bool done = DwgStatsScanner.ScanBatch(uidoc.Document, session, DwgStatisticsTool.ScanBatchMilliseconds, LogFromRevit);
                    OnUi(() =>
                    {
                        UpdateScanProgress();
                        if (done) FinishScan(session.Result);
                        else ScheduleNextBatch();
                    });
                });
            })));
        }

        private void UpdateScanProgress()
        {
            if (_session == null) return;
            // int keeps this compiling whether ToolViewModelBase.Progress is int or double.
            Progress = (int)Math.Round(_session.Percent);
            ScanProgressText = _session.TotalViews == 0
                ? "No views to check."
                : $"{_session.DoneViews} / {_session.TotalViews} views";
            SummaryText = $"Scanning views… {_session.Percent:0}%  ({ScanProgressText})";
        }

        // Queues one scan step without touching the busy counter (the whole scan counts as one job).
        private void RaiseScanStep(Action<UIApplication> body)
        {
            _handler.Queue(app =>
            {
                // Esc / Cancel with a Yes-No confirm while the project is scanned (nothing is changed).
                using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.DwgStatistics, app);
                try
                {
                    body(app);
                }
                catch (OperationCanceledException)
                {
                    OnUi(() =>
                    {
                        AddLog(LogLevel.Warning, "Cancelled by user. The scan was stopped.");
                        EndScan("Scan cancelled. Press Refresh to scan again.");
                    });
                }
                catch (Exception ex)
                {
                    OnUi(() =>
                    {
                        AddLog(LogLevel.Error, $"Scan stopped: {ex.Message}");
                        EndScan("Scan failed — see the log.");
                    });
                    ToolGuard.Report(GetType(), ex);
                }
            });

            if (_externalEvent.Raise() == ExternalEventRequest.Denied)
            {
                AddLog(LogLevel.Warning, "Revit is busy. Close any open dialog and press Refresh.");
                EndScan("Revit is busy. Close any open dialog and try again.");
            }
        }

        private void EndScan(string summary)
        {
            if (!IsScanning) return;
            IsScanning = false;
            _session = null;
            ScanProgressText = string.Empty;
            SetBusy(-1, null);
            if (summary != null) SummaryText = summary;
            if (_lastScan == null) ScanInfo = "No scan yet — press Refresh.";
            CancelScanCommand.NotifyCanExecuteChanged();
        }

        private void FinishScan(DwgScanResult scan)
        {
            var stats = DwgStatsBuilder.Build(scan, CurrentRules);

            // V004/V005: compare with the last scan saved before this window opened (shared next to the
            // project when possible, else this computer), then store this scan for the next session.
            if (!_baselineLoaded)
            {
                _history = DwgHistoryStore.Load(SharedHistory, scan.ProjectFolder, scan.ProjectTitle, scan.ProjectKey, out var readFrom);
                _baseline = _history.Latest;
                _historyLocation = readFrom;
                _baselineLoaded = true;
                if (SharedHistory && string.IsNullOrEmpty(scan.ProjectFolder))
                    AddLog(LogLevel.Info, "Cloud / server / unsaved model: the scan history stays on this computer.");
            }
            var changes = DwgSnapshotService.Compare(stats, _baseline);

            var totals = DwgStatsBuilder.Totals(stats);
            var entry = new DwgHistoryEntry
            {
                ScannedAt = DateTime.Now,
                ScannedBy = Environment.UserName,
                Tool = DwgStatisticsTool.Label,
                Files = totals.Files,
                Instances = totals.Instances,
                WithIssues = totals.WithIssues,
                Changes = changes.Count
            };
            var savedTo = DwgHistoryStore.Save(SharedHistory, scan.ProjectFolder, scan.ProjectTitle, scan.ProjectKey,
                DwgSnapshotService.Create(stats, scan.ProjectTitle, entry.ScannedAt), entry, out string saveError, out string sharedError);
            if (sharedError != null)
                AddLog(LogLevel.Warning, $"Could not write the shared history next to the project ({sharedError}); saved on this computer instead.");
            if (savedTo == null)
                AddLog(LogLevel.Warning, $"Could not save the scan for next time: {saveError}");
            else
            {
                _historyLocation = savedTo;
                _history ??= new DwgHistoryFile();
                _history.History.Insert(0, entry);
            }

            ShowRows(scan, stats, changes);
            EndScan(null);
        }

        private void ShowRows(DwgScanResult scan, List<DwgFileStats> stats, List<DwgChange> changes, bool rulesOnly = false)
        {
            var ticked = new HashSet<long>(Files.Where(r => r.IsTicked).Select(r => r.TypeId));
            var tickedViews = Files
                .Where(r => r.TickedViewCount > 0)
                .ToDictionary(r => r.TypeId, r => new HashSet<long>(r.TickedViewIds));
            var tickedLayers = Files
                .Where(r => r.TickedLayerCount > 0)
                .ToDictionary(r => r.TypeId, r => new HashSet<long>(r.TickedLayerIds));
            long? selected = SelectedFile?.TypeId;

            _suspend = true;
            try
            {
                foreach (var row in Files) row.PropertyChanged -= OnRowPropertyChanged;
                Files.Clear();
                foreach (var s in stats)
                {
                    tickedViews.TryGetValue(s.TypeId, out var viewTicks);
                    tickedLayers.TryGetValue(s.TypeId, out var layerTicks);
                    var row = new DwgFileRowViewModel(s, viewTicks, layerTicks) { IsTicked = ticked.Contains(s.TypeId) };
                    row.PropertyChanged += OnRowPropertyChanged;
                    Files.Add(row);
                }
            }
            finally
            {
                _suspend = false;
            }

            Changes.Clear();
            foreach (var c in changes) Changes.Add(c);
            OnPropertyChanged(nameof(ChangesHeader));
            ChangedCount = stats.Count(s => !string.IsNullOrEmpty(s.ChangeTag));
            string where = _historyLocation == null ? "this computer" : _historyLocation.Label;
            BaselineText = _baseline == null
                ? $"First scan recorded — changes will be listed from the next session. History: {where}."
                : $"Compared with the scan of {_baseline.ScannedAt:yyyy-MM-dd HH:mm}" +
                  (string.IsNullOrEmpty(_baseline.ScannedBy) ? string.Empty : $" by {_baseline.ScannedBy}") +
                  $" · history: {where}.";

            // V005: worksets for the rule and Move to Workset (keep the chosen target across rescans).
            if (!rulesOnly)
            {
                _worksetByInstance = scan.Instances.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First().WorksetId);
                int? keepWorkset = SelectedWorkset?.Id;
                IsWorkshared = scan.IsWorkshared;
                Worksets.Clear();
                foreach (var w in scan.Worksets) Worksets.Add(w);
                SelectedWorkset = Worksets.FirstOrDefault(w => w.Id == keepWorkset)
                                  ?? Worksets.FirstOrDefault(w => CurrentRules.IsCadWorkset(w.Name) && !string.IsNullOrWhiteSpace(CadWorksetPattern))
                                  ?? Worksets.FirstOrDefault();
            }

            _lastScan = scan;
            _totals = DwgStatsBuilder.Totals(stats);
            TotalFiles = _totals.Files;
            LinkCount = _totals.Links;
            ImportCount = _totals.Imports;
            TotalInstances = _totals.Instances;
            ViewsUsed = _totals.Views;
            SheetsUsed = _totals.Sheets;
            NotLoadedCount = _totals.NotLoaded;
            IssueFileCount = _totals.WithIssues;
            UnusedCount = _totals.Unused;

            FilesView.Refresh();
            SelectedFile = selected.HasValue ? Files.FirstOrDefault(r => r.TypeId == selected.Value) : null;
            Recount();

            ScanInfo = $"Deep scan: {scan.ViewsScanned} view(s) checked" +
                       (scan.ViewsSkipped > 0 ? $", {scan.ViewsSkipped} skipped" : string.Empty) +
                       $" in {scan.Elapsed.TotalSeconds:0.0} s · '{scan.ProjectTitle}'.";

            if (rulesOnly)
            {
                SummaryText = $"Rules applied — {_totals.WithIssues} file(s) with issues.";
                AddLog(LogLevel.Info, $"Rules applied (far > {CurrentRules.FarFromOriginKm:0.#} km, layers > {CurrentRules.MaxLayers}, CAD worksets \"{CurrentRules.CadWorksetPattern}\"): {_totals.WithIssues} file(s) with issues.");
                ExportCommand.NotifyCanExecuteChanged();
                return;
            }

            string summary = $"Scan complete — {_totals.Files} DWG file(s), {_totals.Instances} instance(s), " +
                             $"{_totals.Views} view(s), {_totals.Sheets} sheet(s), {_totals.WithIssues} file(s) with issues" +
                             (_baseline != null ? $", {changes.Count} change(s) since last scan." : ".");
            AddLog(_totals.WithIssues > 0 ? LogLevel.Warning : LogLevel.Success, summary);
            SummaryText = summary;
            ExportCommand.NotifyCanExecuteChanged();
        }

        // ── File ticks ───────────────────────────────────────────────────────

        /// <summary>Ticks every row that passes the filters.</summary>
        [RelayCommand]
        private void TickAll() => SetTicks(true);

        /// <summary>Unticks every row that passes the filters.</summary>
        [RelayCommand]
        private void ClearTicks() => SetTicks(false);

        private void SetTicks(bool on)
        {
            _suspend = true;
            try
            {
                foreach (var row in FilesView.Cast<DwgFileRowViewModel>()) row.IsTicked = on;
            }
            finally
            {
                _suspend = false;
            }
            Recount();
        }

        private void OnRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_suspend) return;
            if (e.PropertyName == nameof(DwgFileRowViewModel.IsTicked)) Recount();
        }

        private void Recount()
        {
            var ticked = Files.Where(r => r.IsTicked).ToList();
            TickedCount = ticked.Count;
            TickedUnpinned = ticked.Sum(r => r.UnpinnedCount);
            TickedLinks = ticked.Count(r => r.IsLinked);
            TickedUnused = ticked.Count(r => r.InstanceCount == 0);
            int target = SelectedWorkset?.Id ?? -1;
            TickedMovable = ticked.Sum(r => r.Stats.Instances.Count(i => i.CanChangeWorkset && WorksetIdOf(i) != target));
            RefreshCommandState();
        }

        private void RefreshCommandState()
        {
            OnPropertyChanged(nameof(PinAllLabel));
            OnPropertyChanged(nameof(ReloadLabel));
            OnPropertyChanged(nameof(PurgeLabel));
            OnPropertyChanged(nameof(TickHint));
            RefreshCommand.NotifyCanExecuteChanged();
            PinAllCommand.NotifyCanExecuteChanged();
            ReloadLinksCommand.NotifyCanExecuteChanged();
            PurgeUnusedCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(MoveToWorksetLabel));
            MoveToWorksetCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
            NotifyViewCommands();
        }

        // ── Cleanup actions (ticked files) ───────────────────────────────────

        /// <summary>Pins every unpinned instance of the ticked files.</summary>
        [RelayCommand(CanExecute = nameof(CanPinAll))]
        private void PinAll() => Guarded("Pin All", PinAllCore);

        private void PinAllCore()
        {
            var rows = Files.Where(r => r.IsTicked && r.UnpinnedCount > 0).ToList();
            if (!Confirm($"Pin {rows.Sum(r => r.UnpinnedCount)} unpinned instance(s) in {rows.Count} DWG file(s)?",
                    rows.Select(r => $"{r.FileName}  ({r.Kind}, {r.Status})")))
                return;

            var ids = rows.Select(r => r.TypeId).ToList();
            RunAction("Pinning CAD instances…", doc => new DwgCleanupService(LogFromRevit).PinAll(doc, ids));
        }

        private bool CanPinAll() => !IsRunning && TickedUnpinned > 0;

        /// <summary>Reloads the ticked CAD links from their saved paths.</summary>
        [RelayCommand(CanExecute = nameof(CanReload))]
        private void ReloadLinks() => Guarded("Reload Links", ReloadLinksCore);

        private void ReloadLinksCore()
        {
            var rows = Files.Where(r => r.IsTicked && r.IsLinked).ToList();
            if (!Confirm($"Reload {rows.Count} CAD link(s) from their saved paths?",
                    rows.Select(r => $"{r.FileName}  ({r.Status})")))
                return;

            var ids = rows.Select(r => r.TypeId).ToList();
            var reloaded = rows.Select(r => r.Stats).ToList();
            RunAction($"Reloading {ids.Count} link(s)…", doc => new DwgCleanupService(LogFromRevit).ReloadLinks(doc, ids),
                result =>
                {
                    // V005: the model now shows the current file, so "Updated on disk" clears on the rescan.
                    if (result.Done > 0) DwgSnapshotService.MarkReloaded(_baseline, reloaded);
                });
        }

        private bool CanReload() => !IsRunning && TickedLinks > 0;

        /// <summary>Deletes the ticked CAD types that have no instance.</summary>
        [RelayCommand(CanExecute = nameof(CanPurge))]
        private void PurgeUnused() => Guarded("Purge Unused", PurgeUnusedCore);

        private void PurgeUnusedCore()
        {
            var rows = Files.Where(r => r.IsTicked && r.InstanceCount == 0).ToList();
            if (!Confirm($"Purge {rows.Count} unused CAD type(s)? Linked ones are removed from Manage Links.",
                    rows.Select(r => $"{r.FileName}  ({r.Kind})")))
                return;

            var ids = rows.Select(r => r.TypeId).ToList();
            RunAction($"Purging {ids.Count} unused type(s)…", doc => new DwgCleanupService(LogFromRevit).PurgeUnused(doc, ids));
        }

        private bool CanPurge() => !IsRunning && TickedUnused > 0;

        // ── V005: Move to Workset ────────────────────────────────────────────

        /// <summary>Moves the model-placed instances of the ticked files onto the selected workset.</summary>
        [RelayCommand(CanExecute = nameof(CanMoveToWorkset))]
        private void MoveToWorkset() => Guarded("Move to Workset", MoveToWorksetCore);

        private void MoveToWorksetCore()
        {
            var target = SelectedWorkset;
            if (target == null) return;
            var rows = Files.Where(r => r.IsTicked && r.MovableCount > 0).ToList();
            if (!Confirm($"Move {TickedMovable} model-placed instance(s) of {rows.Count} DWG file(s) to workset '{target.Name}'?",
                    rows.Select(r => $"{r.FileName}  ({r.WrongWorksetCount} on a non-CAD workset)")
                        .Append("'Current view only' instances follow their view's workset and are not moved.")))
                return;

            var ids = rows.Select(r => r.TypeId).ToList();
            int worksetId = target.Id;
            string name = target.Name;
            RunAction($"Moving CAD to '{name}'…", doc => new DwgCleanupService(LogFromRevit).MoveToWorkset(doc, ids, worksetId, name));
        }

        private bool CanMoveToWorkset() => !IsRunning && IsWorkshared && SelectedWorkset != null && TickedMovable > 0;

        // Workset ID of an instance (from the last scan's facts).
        private int WorksetIdOf(DwgInstanceStats inst)
            => _worksetByInstance.TryGetValue(inst.Id, out int id) ? id : -1;

        // ── Views tab: open / hide / unhide / delete in ticked views ─────────

        private List<DwgViewRowViewModel> TickedViewRows() => SelectedFile?.TickedViewRows ?? new List<DwgViewRowViewModel>();

        /// <summary>
        /// Opens every view ticked in the selected file's Views tab (1 to MaxOpenViews). V004 options:
        /// open the sheet instead, zoom to the DWG in each view, close every other open view.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanOpenTickedViews))]
        private void OpenTickedViews() => Guarded("Open Ticked Views", OpenTickedViewsCore);

        private void OpenTickedViewsCore()
        {
            var file = SelectedFile;
            var rows = TickedViewRows();
            if (file == null || rows.Count == 0 || rows.Count > MaxOpenViews) return;

            bool useSheet = OpenSheetInstead, zoom = ZoomToDwg, closeOthers = CloseOtherViews;
            var requests = rows.Select(r =>
            {
                bool sheet = useSheet && r.Usage.SheetIds.Count > 0 && r.ViewType != "Sheets";
                long target = sheet ? r.Usage.SheetIds[0] : r.ViewId;
                string label = sheet ? $"sheet {r.Usage.Sheets.Split(',')[0].Trim()} ({r.ViewName})" : r.ViewName;
                var zoomIds = zoom && !sheet
                    ? r.Usage.VisibleInstanceIds.Union(r.Usage.PlacedInstanceIds).ToList()
                    : new List<long>();
                return new DwgOpenRequest(target, label, zoomIds);
            }).ToList();

            if (closeOthers &&
                MessageBox.Show($"Open {requests.Count} view(s) and close every other open view?",
                    DwgStatisticsTool.Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            RunInRevit($"Opening {requests.Count} view(s) of {file.FileName}…", app =>
            {
                if (!TryGetUiDocument(app, out var uidoc)) return;
                int opened = DwgNavigationService.OpenViews(uidoc, requests, closeOthers, LogFromRevit);
                OnUi(() =>
                {
                    int distinct = requests.Select(q => q.ViewId).Distinct().Count();
                    string summary = $"{opened} opened | {distinct - opened} failed";
                    AddLog(opened == distinct ? LogLevel.Success : LogLevel.Warning, $"Open Ticked Views ({file.FileName}): {summary}");
                    SummaryText = summary;
                });
            });
        }

        private bool CanOpenTickedViews() => !IsRunning && TickedViews > 0 && TickedViews <= MaxOpenViews;

        /// <summary>Ticks every view in the selected file's Views tab.</summary>
        [RelayCommand(CanExecute = nameof(HasViewRows))]
        private void TickAllViews() => SelectedFile?.SetAllViewTicks(true);

        /// <summary>Unticks every view in the selected file's Views tab.</summary>
        [RelayCommand(CanExecute = nameof(HasViewRows))]
        private void ClearViewTicks() => SelectedFile?.SetAllViewTicks(false);

        private bool HasViewRows() => SelectedFile != null && SelectedFile.ViewRows.Count > 0;

        /// <summary>V004: hides the selected DWG in every ticked view that draws it.</summary>
        [RelayCommand(CanExecute = nameof(CanHide))]
        private void HideInTickedViews() => Guarded("Hide in Ticked Views", HideInTickedViewsCore);

        private void HideInTickedViewsCore()
        {
            var file = SelectedFile;
            var targets = TickedViewRows()
                .Where(r => r.Usage.VisibleInstances > 0)
                .Select(r => new DwgViewTarget(r.ViewId, r.ViewName, r.Usage.VisibleInstanceIds))
                .ToList();
            if (file == null || targets.Count == 0) return;
            if (!Confirm($"Hide '{file.FileName}' in {targets.Count} view(s)? (Hide in View → Elements)",
                    targets.Select(t => t.ViewName)))
                return;

            string name = file.FileName;
            RunAction($"Hiding {name} in {targets.Count} view(s)…", doc => new DwgViewActionService(LogFromRevit).Hide(doc, name, targets));
        }

        private bool CanHide() => !IsRunning && TickedViewRows().Any(r => r.Usage.VisibleInstances > 0);

        /// <summary>V004: unhides the selected DWG in every ticked view where it is hidden.</summary>
        [RelayCommand(CanExecute = nameof(CanUnhide))]
        private void UnhideInTickedViews() => Guarded("Unhide in Ticked Views", UnhideInTickedViewsCore);

        private void UnhideInTickedViewsCore()
        {
            var file = SelectedFile;
            var targets = TickedViewRows()
                .Where(r => r.Usage.HiddenInstances > 0)
                .Select(r => new DwgViewTarget(r.ViewId, r.ViewName, r.Usage.HiddenInstanceIds))
                .ToList();
            if (file == null || targets.Count == 0) return;
            if (!Confirm($"Unhide '{file.FileName}' in {targets.Count} view(s)?", targets.Select(t => t.ViewName)))
                return;

            string name = file.FileName;
            RunAction($"Unhiding {name} in {targets.Count} view(s)…", doc => new DwgViewActionService(LogFromRevit).Unhide(doc, name, targets));
        }

        private bool CanUnhide() => !IsRunning && TickedViewRows().Any(r => r.Usage.HiddenInstances > 0);

        /// <summary>
        /// V004 (from DWG Manager): deletes the selected DWG's "Current view only" instances in the ticked
        /// views. Model-placed instances are never deleted here (that would remove them everywhere).
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanDelete))]
        private void DeleteInTickedViews() => Guarded("Delete in Ticked Views", DeleteInTickedViewsCore);

        private void DeleteInTickedViewsCore()
        {
            var file = SelectedFile;
            var rows = TickedViewRows();
            var targets = rows
                .Where(r => r.Usage.PlacedInstances > 0)
                .Select(r => new DwgViewTarget(r.ViewId, r.ViewName, r.Usage.PlacedInstanceIds))
                .ToList();
            if (file == null || targets.Count == 0) return;

            var placedIds = new HashSet<long>(targets.SelectMany(t => t.InstanceIds));
            int modelSkipped = rows.SelectMany(r => r.Usage.VisibleInstanceIds).Where(id => !placedIds.Contains(id)).Distinct().Count();
            int count = placedIds.Count;
            bool purge = PurgeAfterDelete;

            var lines = targets.Select(t => $"{t.ViewName}  ({t.InstanceIds.Count})").ToList();
            if (modelSkipped > 0) lines.Add($"({modelSkipped} model-placed instance(s) will NOT be deleted — use Hide)");
            lines.Add(purge ? "Purge is ON: the type is removed if no instance is left anywhere."
                            : "Purge is OFF: the type stays in the project.");
            if (!Confirm($"Delete {count} 'Current view only' instance(s) of '{file.FileName}' in {targets.Count} view(s)?", lines))
                return;

            long typeId = file.TypeId;
            string name = file.FileName;
            AddLog(LogLevel.Warning, $"Deleting {count} instance(s) of {name} in {targets.Count} view(s){(purge ? " + purge if unused" : string.Empty)}…");
            RunAction($"Deleting {count} instance(s)…",
                doc => new DwgViewActionService(LogFromRevit).DeleteInViews(doc, typeId, name, targets, modelSkipped, purge));
        }

        private bool CanDelete() => !IsRunning && TickedViewRows().Any(r => r.Usage.PlacedInstances > 0);

        private static bool Confirm(string question, IEnumerable<string> lines)
        {
            var list = lines.ToList();
            if (list.Count == 0) return false;

            var sb = new StringBuilder();
            sb.AppendLine(question);
            sb.AppendLine();
            foreach (var line in list.Take(14)) sb.AppendLine($"  • {line}");
            if (list.Count > 14) sb.AppendLine($"  … and {list.Count - 14} more");
            sb.AppendLine();
            sb.AppendLine("You can undo this with Ctrl+Z in Revit.");

            return MessageBox.Show(sb.ToString(), DwgStatisticsTool.Title,
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        // Runs one action in Revit, logs its summary, then starts a fresh (batched) scan.
        private void RunAction(string busyText, Func<Document, DwgActionResult> action, Action<DwgActionResult> afterUi = null)
        {
            RunInRevit(busyText, app =>
            {
                if (!TryGetUiDocument(app, out var uidoc)) return;

                var result = action(uidoc.Document);
                OnUi(() =>
                {
                    var level = result.RolledBack || result.Failed > 0 ? LogLevel.Error
                        : result.Done == 0 ? LogLevel.Warning
                        : LogLevel.Success;
                    AddLog(level, result.Summary);
                    SummaryText = result.Summary;
                    afterUi?.Invoke(result);
                });

                // After this job's busy counter is released, rescan so every number is current.
                _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Guarded("Rescan", StartScan)));
            });
        }

        // ── V005: Layers tab ─────────────────────────────────────────────────

        /// <summary>Ticks every layer of the selected file.</summary>
        [RelayCommand(CanExecute = nameof(HasLayerRows))]
        private void TickAllLayers() => SelectedFile?.SetAllLayerTicks(true);

        /// <summary>Unticks every layer of the selected file.</summary>
        [RelayCommand(CanExecute = nameof(HasLayerRows))]
        private void ClearLayerTicks() => SelectedFile?.SetAllLayerTicks(false);

        private bool HasLayerRows() => SelectedFile != null && SelectedFile.LayerRows.Count > 0;

        /// <summary>Hides the ticked layers in the views ticked in the Views tab.</summary>
        [RelayCommand(CanExecute = nameof(CanLayerAction))]
        private void HideLayersInTickedViews() => Guarded("Hide layers", () => LayerAction(hide: true));

        /// <summary>Unhides the ticked layers in the views ticked in the Views tab.</summary>
        [RelayCommand(CanExecute = nameof(CanLayerAction))]
        private void UnhideLayersInTickedViews() => Guarded("Unhide layers", () => LayerAction(hide: false));

        private bool CanLayerAction() => !IsRunning && TickedViews > 0 && (SelectedFile?.TickedLayerCount ?? 0) > 0;

        private void LayerAction(bool hide)
        {
            var file = SelectedFile;
            if (file == null) return;
            var layerIds = file.TickedLayerIds;
            var views = TickedViewRows().Select(r => new DwgViewTarget(r.ViewId, r.ViewName, Array.Empty<long>())).ToList();
            if (layerIds.Count == 0 || views.Count == 0) return;

            var layerNames = file.LayerRows.Where(l => l.IsTicked).Select(l => l.Name).ToList();
            if (!Confirm($"{(hide ? "Hide" : "Unhide")} {layerIds.Count} layer(s) of '{file.FileName}' in {views.Count} view(s)?",
                    layerNames.Take(8).Select(n => "Layer: " + n)
                        .Concat(layerNames.Count > 8 ? new[] { $"… and {layerNames.Count - 8} more layer(s)" } : Array.Empty<string>())
                        .Concat(views.Select(v => "View: " + v.ViewName))))
                return;

            string name = file.FileName;
            RunAction($"{(hide ? "Hiding" : "Unhiding")} {layerIds.Count} layer(s) in {views.Count} view(s)…",
                doc => new DwgViewActionService(LogFromRevit).SetLayersHidden(doc, name, views, layerIds, hide));
        }

        // ── Navigation ───────────────────────────────────────────────────────

        /// <summary>Opens one view from the Views tab.</summary>
        [RelayCommand]
        private void OpenView(DwgViewUsage usage) => Guarded("Open view", () => OpenViewCore(usage));

        private void OpenViewCore(DwgViewUsage usage)
        {
            if (usage == null) return;
            long viewId = OpenSheetInstead && usage.SheetIds.Count > 0 && usage.ViewType != "Sheets" ? usage.SheetIds[0] : usage.ViewId;
            var zoomIds = ZoomToDwg && viewId == usage.ViewId
                ? usage.VisibleInstanceIds.Union(usage.PlacedInstanceIds).ToList()
                : new List<long>();
            var request = new DwgOpenRequest(viewId, usage.ViewName, zoomIds);

            RunInRevit($"Opening {usage.ViewName}…", app =>
            {
                if (!TryGetUiDocument(app, out var uidoc)) return;
                DwgNavigationService.OpenViews(uidoc, new[] { request }, false, LogFromRevit);
            });
        }

        /// <summary>Selects and zooms to an instance from the Instances tab (opens a view that draws it if needed).</summary>
        [RelayCommand]
        private void ZoomInstance(DwgInstanceStats instance) => Guarded("Zoom", () => ZoomInstanceCore(instance));

        private void ZoomInstanceCore(DwgInstanceStats instance)
        {
            if (instance == null) return;
            long id = instance.Id;
            long fallback = instance.BestViewId;
            RunInRevit($"Selecting #{id}…", app =>
            {
                if (!TryGetUiDocument(app, out var uidoc)) return;
                DwgNavigationService.SelectAndZoom(uidoc, id, fallback, LogFromRevit);
            });
        }

        // ── Export ───────────────────────────────────────────────────────────

        /// <summary>Saves the last scan as an .xlsx workbook (all files, not only the filtered ones).</summary>
        [RelayCommand(CanExecute = nameof(CanExport))]
        private void Export() => Guarded("Export", ExportCore);

        private void ExportCore()
        {
            if (_lastScan == null) return;

            var dialog = new SaveFileDialog
            {
                Title = "Export DWG statistics",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                FileName = DwgStatsExportService.DefaultFileName(_lastScan.ProjectTitle),
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var files = Files.Select(r => r.Stats).ToList();
                DwgStatsExportService.Export(dialog.FileName, files, new DwgExportContext
                {
                    ProjectTitle = _lastScan.ProjectTitle,
                    Totals = _totals,
                    ViewsScanned = _lastScan.ViewsScanned,
                    ViewsSkipped = _lastScan.ViewsSkipped,
                    Elapsed = _lastScan.Elapsed,
                    Changes = Changes.ToList(),
                    BaselineScannedAt = _baseline?.ScannedAt,
                    BaselineScannedBy = _baseline?.ScannedBy ?? string.Empty,
                    History = _history?.History ?? new List<DwgHistoryEntry>(),
                    HistoryLocation = _historyLocation?.Label ?? "this computer",
                    Rules = CurrentRules
                });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is ArgumentException || ex is NotSupportedException)
            {
                AddLog(LogLevel.Error, $"Export failed: {ex.Message} (is the file open in Excel?)");
                SummaryText = "Export failed — see the log.";
                return;
            }

            AddLog(LogLevel.Success, $"Exported to {dialog.FileName}");
            SummaryText = $"Exported {Files.Count} DWG file(s) to {(global::System.IO.Path.GetFileName(dialog.FileName))}.";

            if (MessageBox.Show("Export complete. Open the file now?", DwgStatisticsTool.Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                }
                catch (Exception ex) when (ex is global::System.ComponentModel.Win32Exception || ex is InvalidOperationException)
                {
                    AddLog(LogLevel.Warning, $"Could not open the file: {ex.Message}");
                }
            }
        }

        private bool CanExport() => !IsRunning && _lastScan != null;

        // ── Error handling for UI-thread work ────────────────────────────────

        // Runs a command body on the UI thread. An unexpected exception is logged, reported through
        // ToolGuard and shown in the status strip instead of reaching Revit's dispatcher — from a
        // modeless window that would take Revit down. A scan in progress is ended cleanly.
        private void Guarded(string what, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"{what} failed: {ex.Message}");
                ToolGuard.Report(GetType(), ex);
                if (IsScanning) EndScan($"{what} failed — see the log.");
                else SummaryText = $"{what} failed — see the log.";
            }
        }

        // ── Revit plumbing (same pattern as DWG Manager V003) ────────────────

        private sealed class Job
        {
            public bool Cancelled;
        }

        // Queues work for the Revit API context. A failure is logged and reported; the run counter always
        // goes back down so the window never stays stuck on "running".
        private void RunInRevit(string summary, Action<UIApplication> body)
        {
            var job = new Job();
            SetBusy(+1, summary);

            _handler.Queue(app =>
            {
                if (job.Cancelled) return;

                // Esc / Cancel with a Yes-No confirm; the service rolls its transaction back on cancel.
                using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.DwgStatistics, app);
                try
                {
                    body(app);
                }
                catch (OperationCanceledException)
                {
                    OnUi(() => AddLog(LogLevel.Warning, "Cancelled by user. Nothing was changed."));
                }
                catch (Exception ex)
                {
                    OnUi(() => AddLog(LogLevel.Error, $"Stopped, changes were rolled back: {ex.Message}"));
                    ToolGuard.Report(GetType(), ex);
                }
                finally
                {
                    OnUi(() => SetBusy(-1, null));
                }
            });

            if (_externalEvent.Raise() == ExternalEventRequest.Denied)
            {
                job.Cancelled = true;
                SetBusy(-1, null);
                SummaryText = "Revit is busy. Close any open dialog and try again.";
                AddLog(LogLevel.Warning, SummaryText);
            }
        }

        private void SetBusy(int delta, string summary)
        {
            _busy = Math.Max(0, _busy + delta);
            IsRunning = _busy > 0;
            if (summary != null) SummaryText = summary;
            RefreshCommandState();
        }

        private bool TryGetUiDocument(UIApplication app, out UIDocument uidoc)
        {
            uidoc = app.ActiveUIDocument;
            if (uidoc != null && _doc.IsValidObject && uidoc.Document.Equals(_doc)) return true;

            OnUi(() => AddLog(LogLevel.Error,
                "The active document changed. Switch back to the document this window was opened for."));
            uidoc = null;
            return false;
        }

        private void LogFromRevit(LogEntry entry) => OnUi(() => AddLog(entry.Level, entry.Message));

        private void OnUi(Action action)
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.Invoke(action);
        }
    }
}
