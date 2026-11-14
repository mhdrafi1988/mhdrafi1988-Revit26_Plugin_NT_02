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
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Models;
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Services;
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.UI.ViewModels
{
    /// <summary>
    /// Export DWG to Folder (V004): nothing is scanned when the window opens. The user ticks a scan
    /// scope (source + view types), scans, ticks rows in the grouped grid — individually, per group
    /// via the tri-state checkbox on each group header, or via the shown-rows header tick — and
    /// exports only those. Each exported DWG is produced by temporarily isolating just that one CAD
    /// instance in its host view (see <see cref="Core.Services.DwgExportService"/>), not the whole
    /// view's content.
    /// Scanning, loading the list and exporting each drive a footer progress bar with count and
    /// percentage.
    /// <para>
    /// V004 adds three things to V003:
    /// layer fidelity — a DWG export setup saved in the model can be chosen, and a link with a
    /// resolvable source file is copied rather than re-exported;
    /// a full destination path on every row, shown on hover;
    /// and a post-export <b>review window</b> with a per-row Keep / Delete / Convert-to-import
    /// action, replacing V002/V003's three-command-link <see cref="TaskDialog"/>. Skipped and failed
    /// rows are still never offered. The exported-file list and its capped Open stay reachable from
    /// a "Last export" card after the review is dismissed.
    /// </para>
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

        /// <summary>Decisions sent with the last ApplyPostExport request, for mapping results back.</summary>
        private List<PostExportDecision> _pendingDecisions = new List<PostExportDecision>();

        /// <summary>Rows that exported successfully in the last run, kept for the Last export card.</summary>
        private List<DwgRowViewModel> _lastExportedRows = new List<DwgRowViewModel>();

        /// <summary>Full written path per exported row, keyed by element id value.</summary>
        private Dictionary<long, string> _lastDestinations = new Dictionary<long, string>();

        /// <summary>Export route per exported row ("copied source file" / "re-exported from view").</summary>
        private Dictionary<long, string> _lastRoutes = new Dictionary<long, string>();

        /// <summary>Manifest rows accumulated during the current run, one per attempted export.</summary>
        private List<ExportManifestRow> _manifestRows = new List<ExportManifestRow>();

        /// <summary>When the current run started, used in the manifest's file name.</summary>
        private DateTime _runStarted = DateTime.Now;

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

        // ── Layer fidelity (V004, remembered) ───────────────────────────────

        /// <summary>
        /// DWG/DXF export setups saved in the model, "(Revit defaults)" first. Refreshed on every
        /// scan — the list needs a Document, so it comes back through the external event handler.
        /// </summary>
        public ObservableCollection<string> ExportSetups { get; } =
            new ObservableCollection<string> { DwgExportOptionsFactory.DefaultsLabel };

        /// <summary>
        /// The chosen export setup, or "(Revit defaults)". A setup brings its whole layer, linetype
        /// and pattern table with it, which is what preserves the layer structure.
        /// </summary>
        [ObservableProperty] private string selectedExportSetup = DwgExportOptionsFactory.DefaultsLabel;

        /// <summary>
        /// How entity properties are written into the output — the setting that decides what the
        /// exported layer structure looks like. See <see cref="LayerPropertyMode"/>.
        /// </summary>
        [ObservableProperty] private LayerPropertyMode layerPropertyMode;

        /// <summary>Copy a link's source file instead of re-exporting it through its host view.</summary>
        [ObservableProperty] private bool copyOriginalForLinks;

        /// <summary>Export about the shared coordinate origin rather than the project origin.</summary>
        [ObservableProperty] private bool useSharedCoords;

        // ── Output naming and reporting (V004, remembered) ──────────────────

        /// <summary>
        /// Output-name pattern. Tokens: {name} {sheet} {sheetnumber} {view} {viewtype} {source}
        /// {id}. Applied at scan time, so the grid shows the names the export will write.
        /// </summary>
        [ObservableProperty] private string nameTemplate = DwgCollectorService.DefaultNameTemplate;

        /// <summary>Write one manifest CSV per run at the output root.</summary>
        [ObservableProperty] private bool writeManifest;

        /// <summary>The tokens a name template may use, for the window's hint line.</summary>
        public string NameTokenHint => "Tokens: " + string.Join("  ", DwgCollectorService.NameTokens);

        // ── Last export (V004) ──────────────────────────────────────────────

        /// <summary>True once a run has exported at least one DWG, enabling the Last export card.</summary>
        [ObservableProperty] private bool hasLastExport;

        /// <summary>"5 file(s) exported to D:\Exports\… — 2 deleted, 1 converted".</summary>
        [ObservableProperty] private string lastExportSummary = string.Empty;

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

        /// <summary>
        /// The tool window, set by its constructor. Used only as the owner of the post-export review
        /// dialog so it centres on the tool rather than on the screen.
        /// </summary>
        public Window OwnerWindow { get; set; }

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
            layerPropertyMode = _settings.LayerPropertyMode;
            copyOriginalForLinks = _settings.CopyOriginalForLinks;
            useSharedCoords = _settings.UseSharedCoords;
            nameTemplate = string.IsNullOrWhiteSpace(_settings.NameTemplate)
                ? DwgCollectorService.DefaultNameTemplate
                : _settings.NameTemplate;
            writeManifest = _settings.WriteManifest;

            // The remembered setup name is offered before the first scan confirms it still exists
            // in this model; the scan then replaces the list and re-validates the selection.
            if (!string.IsNullOrWhiteSpace(_settings.ExportSetupName))
            {
                ExportSetups.Add(_settings.ExportSetupName);
                selectedExportSetup = _settings.ExportSetupName;
            }

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
            // V004: the scan needs the settings too — the output-name template is applied while
            // collecting, so the grid shows the names the export will actually write.
            _handler.Settings = BuildSettings();
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
            // V004: the layer-relevant options, stated once per run so an output can be explained
            // months later from the log alone.
            AddLog(LogLevel.Info, DwgExportOptionsFactory.Describe(BuildSettings()));

            _lastDestinations = new Dictionary<long, string>();
            _lastRoutes = new Dictionary<long, string>();
            _lastExportedRows = new List<DwgRowViewModel>();
            _manifestRows = new List<ExportManifestRow>();
            _runStarted = DateTime.Now;
            HasLastExport = false;
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

        /// <summary>
        /// Dry run. Reports exactly what an export of the ticked rows would do — which rows would be
        /// skipped because the file already exists, which would fail and why, and the full
        /// destination path of each — and writes nothing. NEW in V004.
        /// <para>
        /// No Revit call is needed: the pre-check and the destination paths are already computable
        /// here, so this is instant and safe to run as often as you like.
        /// </para>
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExport))]
        private void Check()
        {
            var rows = Rows
                .Where(r => r.IsSelected)
                .OrderBy(r => r.SourceOrder)
                .ThenBy(r => r.ViewOrder)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (rows.Count == 0) return;

            UpdateDestinationPaths();

            LogSection("Check (dry run — nothing is written)");
            AddLog(LogLevel.Info, $"Output folder: {OutputFolder}");
            AddLog(LogLevel.Info, DwgExportOptionsFactory.Describe(BuildSettings()));

            int wouldWrite = 0, wouldSkip = 0, wouldFail = 0, wouldOverwrite = 0;

            foreach (var row in rows)
            {
                string dest = row.DestinationPath;

                // Deliberately does NOT call row.PreCheck(): that would overwrite the Exported /
                // Deleted / Converted statuses of a previous run. A dry run reports; it changes
                // nothing, in the model or in the grid.
                string issue = DescribeBlockingIssue(row);
                if (issue != null)
                {
                    wouldFail++;
                    AddLog(LogLevel.Error, $"✘  {row.Name} — would fail: {issue}");
                    continue;
                }

                bool exists;
                try { exists = !string.IsNullOrWhiteSpace(dest) && File.Exists(dest); }
                catch { exists = false; }

                if (exists && !Overwrite)
                {
                    wouldSkip++;
                    AddLog(LogLevel.Warning, $"◌  {row.Name} — would be skipped, file exists: {dest}");
                    continue;
                }

                string route = CopyOriginalForLinks &&
                               row.Info.SourceType == DwgSourceType.Link &&
                               !string.IsNullOrEmpty(row.Info.LinkedSourcePath)
                    ? "copy source file"
                    : "re-export from view";

                if (exists)
                {
                    wouldOverwrite++;
                    AddLog(LogLevel.Warning, $"⇄  {row.Name} — would OVERWRITE {dest} ({route})");
                }
                else
                {
                    wouldWrite++;
                    AddLog(LogLevel.Success, $"✔  {row.Name} — would write {dest} ({route})");
                }

                if (row.LayersSuspicious)
                    AddLog(LogLevel.Warning,
                        $"⚠  {row.Name} — only {row.LayerText} layer(s) in the model. The DWG was already " +
                        "flattened when it came in, so no export setting will bring its layers back.");
            }

            var parts = new List<string>();
            if (wouldWrite > 0) parts.Add($"{wouldWrite:N0} would be written");
            if (wouldOverwrite > 0) parts.Add($"{wouldOverwrite:N0} would be overwritten");
            if (wouldSkip > 0) parts.Add($"{wouldSkip:N0} would be skipped");
            if (wouldFail > 0) parts.Add($"{wouldFail:N0} would fail");

            string summary = parts.Count > 0 ? string.Join(" | ", parts) : "nothing to do";
            AddLog(wouldFail > 0 ? LogLevel.Warning : LogLevel.Info, $"Check complete — {summary}. Nothing was written.");
            StatusMessage = $"Check: {summary}. Nothing written.";
        }

        /// <summary>
        /// Why <paramref name="row"/> could not be exported right now, or null when it can be.
        /// Same rules as <see cref="DwgRowViewModel.PreCheck"/>, but read-only — the dry run must
        /// not overwrite a previous run's statuses.
        /// </summary>
        private static string DescribeBlockingIssue(DwgRowViewModel row)
        {
            if (row.Info.SourceType == DwgSourceType.Link)
            {
                if (string.IsNullOrEmpty(row.Info.LinkedSourcePath))
                    return "source path unresolved";

                bool exists;
                try { exists = File.Exists(row.Info.LinkedSourcePath); }
                catch { exists = false; }

                // A link with no source file can still be re-exported from its host view, so this
                // only blocks the row when there is also no view to isolate it in.
                if (!exists && (row.Info.OwnerViewId == null ||
                                row.Info.OwnerViewId == Autodesk.Revit.DB.ElementId.InvalidElementId))
                    return $"source not found and no host view to export from: {row.Info.LinkedSourcePath}";

                return null;
            }

            if (row.Info.OwnerViewId == null ||
                row.Info.OwnerViewId == Autodesk.Revit.DB.ElementId.InvalidElementId)
                return "no host view — an import's geometry only exists inside the model";

            return null;
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

        /// <summary>Layer-property chip row (V004). Same one-way-bound chip pattern as above.</summary>
        [RelayCommand]
        private void SetLayerPropertyMode(string mode)
        {
            if (Enum.TryParse<LayerPropertyMode>(mode, out var parsed))
                LayerPropertyMode = parsed;
        }

        /// <summary>
        /// Reopens the review over the last run's exported files, so the per-row actions and the
        /// capped Open stay reachable after the dialog was dismissed.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanReviewLastExport))]
        private void ReviewLastExport() => ShowReview(_lastExportedRows);

        private bool CanReviewLastExport() => !IsBusy && _lastExportedRows.Count > 0;

        /// <summary>Opens the root output folder in Explorer.</summary>
        [RelayCommand]
        private void OpenOutputFolder()
        {
            if (string.IsNullOrWhiteSpace(OutputFolder))
            {
                AddLog(LogLevel.Warning, "No output folder is set yet.");
                return;
            }

            try
            {
                if (!Directory.Exists(OutputFolder))
                {
                    AddLog(LogLevel.Warning, $"Output folder does not exist yet: {OutputFolder}");
                    return;
                }

                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(OutputFolder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Warning, $"Could not open the output folder: {ex.Message}");
            }
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

        partial void OnOutputFolderChanged(string value)
        {
            ExportCommand.NotifyCanExecuteChanged();
            CheckCommand.NotifyCanExecuteChanged();
            UpdateDestinationPaths();
        }

        partial void OnSubfoldersByViewTypeChanged(bool value) => UpdateDestinationPaths();

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
            CheckCommand.NotifyCanExecuteChanged();
            ResetCommand.NotifyCanExecuteChanged();
            BrowseOutputFolderCommand.NotifyCanExecuteChanged();
            SelectAllShownCommand.NotifyCanExecuteChanged();
            SelectNoneShownCommand.NotifyCanExecuteChanged();
            ToggleShownCommand.NotifyCanExecuteChanged();
            ReviewLastExportCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Re-computes every row's full destination path. The row has no reference back here, so the
        /// path is pushed in whenever the output folder or the subfolder option changes — which is
        /// why the hover tooltip stays right without needing a re-scan.
        /// </summary>
        private void UpdateDestinationPaths()
        {
            if (string.IsNullOrWhiteSpace(OutputFolder))
            {
                foreach (var row in Rows) row.SetDestination(string.Empty);
                return;
            }

            var settings = BuildSettings();
            foreach (var row in Rows)
            {
                try
                {
                    row.SetDestination(DwgExportEventHandler.BuildDestPath(OutputFolder, row.Info, settings));
                }
                catch
                {
                    // An invalid folder (bad characters, too long) shouldn't break the grid.
                    row.SetDestination(string.Empty);
                }
            }
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

        private void OnExportItemFinished(int index, int total, string error, string route)
        {
            OnUi(() =>
            {
                if (index < 0 || index >= _exportRows.Count) return;
                var row = _exportRows[index];
                string relative = Path.Combine(
                    DwgExportEventHandler.BuildRelativeFolder(row.Info, SubfoldersByViewType), row.OutputFileName);

                if (error == null)
                {
                    // V004: which route produced the file is recorded on the row and in the log, so
                    // "why does this one differ from the source" is never a guess.
                    string note = string.IsNullOrWhiteSpace(route) ? relative : $"{relative} ({route})";
                    row.SetStatus(DwgRowStatus.Exported, note);
                    _lastRoutes[row.ElementIdValue] = route ?? "exported";
                    _lastDestinations[row.ElementIdValue] =
                        string.IsNullOrWhiteSpace(row.DestinationPath)
                            ? Path.Combine(OutputFolder ?? string.Empty, relative)
                            : row.DestinationPath;
                    AddLog(LogLevel.Success, $"✔  {note}");
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

                // V004: every attempted row goes into the run manifest, successes and failures
                // alike — chasing the three that didn't come out is the whole point of it.
                _manifestRows.Add(BuildManifestRow(row, relative, route, error));

                ExportPhase.Report(index + 1, total);
                Pump(index + 1 >= total);
            });
        }

        /// <summary>Builds one manifest row from a finished export item.</summary>
        private ExportManifestRow BuildManifestRow(DwgRowViewModel row, string relative, string route, string error)
            => new ExportManifestRow
            {
                Name = row.Name,
                ElementId = row.ElementIdValue,
                Source = row.SourceGroup,
                ViewType = row.ViewGroup,
                HostView = row.HostView,
                Sheet = row.Sheet,
                Layers = row.Info.LayerCount < 0 ? string.Empty : row.Info.LayerCount.ToString(),
                SourcePath = row.SourceDescription,
                Destination = string.IsNullOrWhiteSpace(row.DestinationPath)
                    ? Path.Combine(OutputFolder ?? string.Empty, relative)
                    : row.DestinationPath,
                Route = route ?? string.Empty,
                Status = error == null ? "Exported" : error == "SKIP" ? "Skipped" : "Failed",
                Reason = error == null
                    ? string.Empty
                    : error == "SKIP" ? "File exists — Overwrite is off" : error
            };

        /// <summary>
        /// Writes the run manifest. A manifest that cannot be written is a Warning, never a failure:
        /// the DWGs are already on disk and the run succeeded.
        /// </summary>
        private void WriteManifestFile()
        {
            if (!WriteManifest || _manifestRows.Count == 0) return;

            try
            {
                string path = ExportManifestService.Write(
                    OutputFolder, _manifestRows, DwgExportOptionsFactory.Describe(BuildSettings()), _runStarted);

                if (path != null)
                    AddLog(LogLevel.Info, $"Manifest written: {path}");
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Warning, $"Could not write the run manifest: {ex.Message}");
            }
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
                    case DwgExportRequest.ApplyPostExport:
                        CompletePostExport();
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
                if (_handler.DuplicatesSkipped > 0)
                    AddLog(LogLevel.Info, $"{_handler.DuplicatesSkipped:N0} duplicate instance(s) of the same source file left out — the first one found is exported.");

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
                RefreshExportSetups();

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

        /// <summary>
        /// Rebuilds the export-setup list from what the scan found in the model, keeping the
        /// current selection when it is still there. A remembered setup that has since been deleted
        /// falls back to defaults with a warning rather than failing the next export silently.
        /// </summary>
        private void RefreshExportSetups()
        {
            var found = _handler.AvailableExportSetups ??
                        new List<string> { DwgExportOptionsFactory.DefaultsLabel };
            string previous = SelectedExportSetup;

            ExportSetups.Clear();
            foreach (var name in found)
                ExportSetups.Add(name);

            if (!string.IsNullOrWhiteSpace(previous) && ExportSetups.Contains(previous))
            {
                SelectedExportSetup = previous;
            }
            else
            {
                SelectedExportSetup = DwgExportOptionsFactory.DefaultsLabel;
                if (!string.IsNullOrWhiteSpace(previous) && previous != DwgExportOptionsFactory.DefaultsLabel)
                    AddLog(LogLevel.Warning,
                        $"DWG export setup \"{previous}\" is not in this model — switched to {DwgExportOptionsFactory.DefaultsLabel}.");
            }

            int saved = ExportSetups.Count - 1;
            AddLog(LogLevel.Debug, saved > 0
                ? $"DWG export setups in this model: {string.Join(", ", ExportSetups.Skip(1))}"
                : "No DWG export setups are saved in this model — using Revit defaults.");
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

                // A setup that could not be loaded is reported once per run, not once per row.
                if (!string.IsNullOrWhiteSpace(_handler.OptionsWarning))
                    AddLog(LogLevel.Warning, _handler.OptionsWarning);

                ExportPhase.Complete(withWarnings: failed > 0);
                AddLog(failed > 0 ? LogLevel.Warning : LogLevel.Success,
                    $"Done — {exported:N0} exported | {skipped:N0} skipped | {failed:N0} failed");
                StatusMessage = $"{exported:N0} exported | {skipped:N0} skipped | {failed:N0} failed";

                WriteManifestFile();
                OnUi(() => LogExportService.AutoSave(LogEntries, ToolCatalog.ExportDwgToFolder.Title));

                _lastExportedRows = _exportRows.Where(r => r.Status == DwgRowStatus.Exported).ToList();
                HasLastExport = _lastExportedRows.Count > 0;
                LastExportSummary = HasLastExport
                    ? $"{_lastExportedRows.Count:N0} file(s) written to {OutputFolder}"
                    : string.Empty;
                ReviewLastExportCommand.NotifyCanExecuteChanged();

                promptDelete = exported > 0;
            }
            finally
            {
                IsExporting = false;
                IsBusy = false;
            }

            // Asked outside the finally/try above so IsBusy is already back to false while the
            // review dialog is up, and set back to true only once something is applied.
            if (promptDelete)
                PromptDeleteExported();
        }

        /// <summary>
        /// Opens the post-export review over the rows that were just exported successfully. Skipped
        /// and failed rows are never offered — only rows whose DWG actually got written this run.
        /// Honors the remembered <see cref="DeleteAfterExportMode"/>: <c>Never</c> leaves everything
        /// alone, <c>Always</c> deletes every exported row with no dialog (both exactly as in V003),
        /// and <c>Ask</c> now opens the review window instead of V003's three-command-link
        /// <see cref="TaskDialog"/>.
        /// </summary>
        private void PromptDeleteExported()
        {
            var exportedRows = _exportRows.Where(r => r.Status == DwgRowStatus.Exported).ToList();
            if (exportedRows.Count == 0) return;

            switch (DeleteAfterExportMode)
            {
                case DeleteAfterExportMode.Never:
                    AddLog(LogLevel.Info,
                        $"Delete after export is set to Never — {exportedRows.Count:N0} exported CAD instance(s) left in the model.");
                    return;

                case DeleteAfterExportMode.Always:
                    // No dialog: every exported row is deleted, as V003 did in this mode.
                    AddLog(LogLevel.Info,
                        $"Delete after export is set to Always — deleting {exportedRows.Count:N0} exported CAD instance(s) without asking.");
                    ApplyDecisions(exportedRows
                        .Select(r => new PostExportDecision
                        {
                            ElementId = r.Info.ElementId,
                            ElementIdValue = r.ElementIdValue,
                            Name = r.Name,
                            Action = PostExportAction.DeleteFromModel,
                            SourceType = r.Info.SourceType,
                            LinkedSourcePath = r.Info.LinkedSourcePath,
                            OwnerViewId = r.Info.OwnerViewId
                        })
                        .ToList());
                    return;

                default: // Ask
                    ShowReview(exportedRows);
                    return;
            }
        }

        /// <summary>
        /// Shows the review window over <paramref name="exportedRows"/> and applies whatever the
        /// user confirmed. Also used by the Last export card's Review button, so the exported-file
        /// list and its five-file Open stay reachable after the dialog is dismissed.
        /// </summary>
        private void ShowReview(List<DwgRowViewModel> exportedRows)
        {
            if (exportedRows == null || exportedRows.Count == 0) return;

            var reviewVm = new PostExportReviewViewModel(
                exportedRows, _lastDestinations, _lastRoutes, OutputFolder);

            var window = new Views.PostExportReviewWindow(reviewVm);
            if (OwnerWindow != null && OwnerWindow.IsLoaded)
                window.Owner = OwnerWindow;

            bool? result;
            try
            {
                result = window.ShowDialog();
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"Could not open the review window: {ex.Message}");
                return;
            }

            // Files are opened whether or not anything is applied — looking at what was exported is
            // not the same decision as changing the model.
            OpenFiles(reviewVm.FilesToOpen());

            if (result != true || !window.Confirmed)
            {
                AddLog(LogLevel.Info, "Review skipped — exported CAD instances left in the model.");
                return;
            }

            ApplyDecisions(reviewVm.BuildDecisions());
        }

        /// <summary>Sends the confirmed decisions to Revit, if any of them actually do something.</summary>
        private void ApplyDecisions(List<PostExportDecision> decisions)
        {
            var actionable = (decisions ?? new List<PostExportDecision>())
                .Where(d => d.Action != PostExportAction.Keep)
                .ToList();

            if (actionable.Count == 0)
            {
                AddLog(LogLevel.Info, "Nothing to apply — every reviewed row was set to Keep.");
                return;
            }

            int deletes = actionable.Count(d => d.Action == PostExportAction.DeleteFromModel);
            int converts = actionable.Count(d => d.Action == PostExportAction.ConvertToImport);
            int relinks = actionable.Count(d => d.Action == PostExportAction.ReplaceWithLinkToExported);

            LogSection("Apply review");
            if (deletes > 0) AddLog(LogLevel.Info, $"Deleting {deletes:N0} exported CAD instance(s) from the model…");
            if (converts > 0) AddLog(LogLevel.Info, $"Converting {converts:N0} exported CAD link(s) into imports…");
            if (relinks > 0) AddLog(LogLevel.Info, $"Replacing {relinks:N0} CAD instance(s) with a link to the exported file…");

            _pendingDecisions = actionable;
            _handler.Decisions = actionable;
            _handler.FallbackViewId = null; // handler falls back to Revit's active view
            Raise(DwgExportRequest.ApplyPostExport,
                $"Applying review — {deletes:N0} delete, {converts:N0} convert, {relinks:N0} re-link…");
        }

        /// <summary>
        /// Opens up to <see cref="PostExportReviewViewModel.MaxOpenFiles"/> exported DWGs with
        /// whatever application owns .dwg on this machine. Each one is tried on its own, so a
        /// missing file never stops the rest.
        /// </summary>
        private void OpenFiles(List<string> paths)
        {
            if (paths == null || paths.Count == 0) return;

            int opened = 0;
            foreach (var path in paths.Take(PostExportReviewViewModel.MaxOpenFiles))
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        AddLog(LogLevel.Warning, $"Could not open — file no longer exists: {path}");
                        continue;
                    }

                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                    opened++;
                }
                catch (Exception ex)
                {
                    AddLog(LogLevel.Warning, $"Could not open {Path.GetFileName(path)}: {ex.Message}");
                }
            }

            if (opened > 0)
                AddLog(LogLevel.Info, $"Opened {opened:N0} exported file(s).");
        }

        /// <summary>
        /// Maps the applied review's results back onto grid rows. A row that was deleted becomes
        /// <see cref="DwgRowStatus.Deleted"/>, a converted one <see cref="DwgRowStatus.Converted"/>,
        /// and anything that failed stays <see cref="DwgRowStatus.Exported"/> carrying the reason —
        /// the DWG was still written, which is what that status means.
        /// </summary>
        private void CompletePostExport()
        {
            try
            {
                if (!_handler.LastRunSucceeded)
                {
                    ReportCriticalError("Review stopped", _handler.ErrorMessage);
                    return;
                }

                var failedDeleteIds = new HashSet<long>(_handler.FailedDeleteIds);
                var rowsById = Rows.ToDictionary(r => r.ElementIdValue, r => r);
                int deleted = 0, converted = 0, relinked = 0, failed = 0;

                foreach (var decision in _pendingDecisions)
                {
                    rowsById.TryGetValue(decision.ElementIdValue, out var row);

                    if (decision.Action == PostExportAction.DeleteFromModel)
                    {
                        bool ok = decision.Succeeded && !failedDeleteIds.Contains(decision.ElementIdValue);
                        if (ok)
                        {
                            deleted++;
                            row?.SetStatus(DwgRowStatus.Deleted, "Removed from the model");
                        }
                        else
                        {
                            failed++;
                            row?.SetStatus(DwgRowStatus.Exported,
                                string.IsNullOrWhiteSpace(decision.Note)
                                    ? "Exported, but could not be deleted from the model"
                                    : "Exported, but " + decision.Note);
                            AddLog(LogLevel.Warning, $"⚠  {decision.Name} — {decision.Note}");
                        }
                    }
                    else if (decision.Action == PostExportAction.ConvertToImport ||
                             decision.Action == PostExportAction.ReplaceWithLinkToExported)
                    {
                        bool toLink = decision.Action == PostExportAction.ReplaceWithLinkToExported;

                        if (decision.Succeeded)
                        {
                            if (toLink) relinked++;
                            else converted++;
                            row?.SetStatus(toLink ? DwgRowStatus.Relinked : DwgRowStatus.Converted, decision.Note);
                            // Everything the conversion could not carry over is named here rather
                            // than lost quietly.
                            AddLog(LogLevel.Success, $"{(toLink ? "🔗" : "⇄")}  {decision.Name} — {decision.Note}");
                        }
                        else
                        {
                            failed++;
                            row?.SetStatus(DwgRowStatus.Exported, "Exported, but " + decision.Note);
                            AddLog(LogLevel.Error, $"✘  {decision.Name} — {decision.Note}");
                        }
                    }
                }

                int dependent = _handler.DependentDeletedCount;
                string dependentSuffix = dependent > 0 ? $", plus {dependent:N0} dependent element(s)" : "";

                var parts = new List<string>();
                if (deleted > 0) parts.Add($"{deleted:N0} deleted{dependentSuffix}");
                if (converted > 0) parts.Add($"{converted:N0} converted to import");
                if (relinked > 0) parts.Add($"{relinked:N0} re-linked to the exported file");
                if (failed > 0) parts.Add($"{failed:N0} failed");
                string summary = parts.Count > 0 ? string.Join(" | ", parts) : "nothing changed";

                AddLog(failed > 0 ? LogLevel.Warning : LogLevel.Success, $"Review applied — {summary}.");
                if (deleted > 0 || converted > 0 || relinked > 0)
                    AddLog(LogLevel.Info, "The whole review is one undo step in Revit (Ctrl+Z).");

                StatusMessage = $"Review applied — {summary}.";
                LastExportSummary = $"{_lastExportedRows.Count:N0} file(s) written to {OutputFolder} — {summary}";
            }
            finally
            {
                IsBusy = false;
                _pendingDecisions = new List<PostExportDecision>();
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
            _settings.Version = 4;
            _settings.LastOutputFolder = OutputFolder ?? string.Empty;
            _settings.SubfoldersByViewType = SubfoldersByViewType;
            _settings.GenerateSidecar = GenerateSidecar;
            _settings.Overwrite = Overwrite;
            _settings.DeleteAfterExportMode = DeleteAfterExportMode;

            // V004 — "(Revit defaults)" is stored as empty, so a settings file written here still
            // means "no setup" to anything reading ExportSetupName directly.
            _settings.ExportSetupName =
                string.IsNullOrWhiteSpace(SelectedExportSetup) ||
                SelectedExportSetup == DwgExportOptionsFactory.DefaultsLabel
                    ? string.Empty
                    : SelectedExportSetup;
            _settings.LayerPropertyMode = LayerPropertyMode;
            _settings.CopyOriginalForLinks = CopyOriginalForLinks;
            _settings.UseSharedCoords = UseSharedCoords;
            _settings.NameTemplate = string.IsNullOrWhiteSpace(NameTemplate)
                ? DwgCollectorService.DefaultNameTemplate
                : NameTemplate;
            _settings.WriteManifest = WriteManifest;
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
                case DwgExportRequest.ApplyPostExport:
                    _pendingDecisions = new List<PostExportDecision>();
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
            _pendingDecisions = new List<PostExportDecision>();
            UpdateDestinationPaths();
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
            CheckCommand.NotifyCanExecuteChanged();
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
