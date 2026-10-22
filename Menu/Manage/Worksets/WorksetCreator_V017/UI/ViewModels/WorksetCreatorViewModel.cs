// ==============================================
// File: WorksetCreatorViewModel.cs
// Layer: UI/ViewModels
// Workset Creator V017.
//
// Changes vs V015:
//   CHG  LoadData builds its rows from WorksetService.ScanLinks — one pass over
//        the model — instead of asking the service five questions per link.
//        Rows are keyed on the RevitLinkType's id, so two links that share a
//        file name are two rows (V015's Distinct(name) merged them).
//   CHG  Runs are submitted as WorksetRequest objects carrying the link type id
//        and the load state, which is what lets the service reach an unloaded
//        link's instances at all.
//   NEW  ExclusionText / HasExclusions — the IFC and nested links the scan threw
//        away, surfaced in the window and written into the run report so a
//        missing link is explained rather than simply absent.
//
// Added later in V016 — IFC toggle and duplicate prevention:
//   NEW  The inclusion rule is by extension and has no switch: a link is listed
//        when its file ends in .rvt, so a Revit-linked IFC (".ifc.rvt") always
//        gets a workset, and a bare .ifc is never listed. IFC rows are tinted
//        via WorksetItem.IsIfc, which is presentation only.
//   NEW  The matching layer. LoadData reads every workset, hands them and the
//        links to WorksetMatchService, and maps the verdicts onto the rows:
//        Existing match, a per-row Action picker, and the Final workset the link
//        will end up in. Rows the matcher will not decide (a residue too rare to
//        trust, or more than one candidate) start unticked and must be resolved.
//   NEW  The pre-flight gate. ExecuteCreateFor runs WorksetPreflightService
//        before anything is submitted; conflicts block the run and nothing is
//        committed. This is what makes "one workset per link" a guarantee
//        rather than a hope.
//   NEW  Settings — pattern, the IFC box, the two matcher thresholds and the log
//        state persist to %AppData%\Revit26_Plugin_Revised\WorksetCreator\settings.json.
// Everything else — the three grids, the pattern engine, duplicate handling,
// every command guard, Create and all three Resync paths, the single
// ExternalEvent, the run summary — is V015 behaviour.
// ==============================================

using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.ViewModels;
using Revit26_Plugin_Revised.WorksetCreator.V017.Core.Models;
using Revit26_Plugin_Revised.WorksetCreator.V017.Core.Services;
using Revit26_Plugin_Revised.WorksetCreator.V017.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.WorksetCreator.V017.UI.ViewModels
{
    public partial class WorksetCreatorViewModel : ToolViewModelBase
    {
        private readonly UIDocument _uidoc;
        private readonly WorksetService _service;
        private readonly WorksetMatchService _matcher;
        private readonly WorksetPreflightService _preflight = new();
        private readonly WorksetSettingsService _settingsService;
        private readonly Document _doc;
        private readonly WorksetActionHandler _handler = new();
        private readonly ExternalEvent _externalEvent;

        /// <summary>
        /// Persisted settings. Loaded before the first scan, saved whenever something
        /// the user chose changes. Never load-bearing — a failed read or write logs a
        /// Warning and the tool carries on with defaults.
        /// </summary>
        private readonly WorksetCreatorSettings _settings;

        // Captured on the UI thread at construction time. Do NOT use
        // System.Windows.Application.Current.Dispatcher here — Revit doesn't
        // guarantee a System.Windows.Application instance exists in-process,
        // so System.Windows.Application.Current can be null and NullReferenceException.
        private readonly Dispatcher _dispatcher;

        /// <summary>Exclusion counts from the most recent scan, for the report.</summary>
        private int _excludedIfc;
        private int _excludedNested;
        private int _includedIfc;

        /// <summary>
        /// The model's worksets as of the most recent scan. Used to seed duplicate
        /// resolution; the pre-flight gate re-reads rather than trusting this, because
        /// another user may have added a workset since.
        /// </summary>
        private List<WorksetInfo> _worksets = new();

        /// <summary>Guards the save-on-change handlers while settings are being applied.</summary>
        private bool _suppressSettingsSave;

        public ObservableCollection<WorksetItem> Items { get; } = new();

        /// <summary>
        /// Naming patterns the matcher found in this model, most used first. Shown in
        /// the window so the user can see the logic agreeing with reality before
        /// pressing anything.
        /// </summary>
        public ObservableCollection<NamingConvention> DetectedConventions { get; } = new();

        public bool HasConventions => DetectedConventions.Count > 0;

        // ── Filtered views ───────────────────────────────────────────────────

        private ICollectionView _grid1AssignedItems;
        public ICollectionView Grid1AssignedItems
        {
            get => _grid1AssignedItems;
            private set => SetProperty(ref _grid1AssignedItems, value);
        }

        private ICollectionView _grid2ActionableItems;
        public ICollectionView Grid2ActionableItems
        {
            get => _grid2ActionableItems;
            private set => SetProperty(ref _grid2ActionableItems, value);
        }

        private ICollectionView _grid3NoInstanceItems;
        public ICollectionView Grid3NoInstanceItems
        {
            get => _grid3NoInstanceItems;
            private set => SetProperty(ref _grid3NoInstanceItems, value);
        }

        // ── Observable properties ────────────────────────────────────────────
        // IsRunning / Progress / SummaryText live on ToolViewModelBase.

        [ObservableProperty] private string pattern              = "+Link({name})";
        [ObservableProperty] private string patternErrorMessage  = string.Empty;
        [ObservableProperty] private bool   hasPatternError;
        [ObservableProperty] private bool   isCreateEnabled      = true;
        [ObservableProperty] private bool   isResyncEnabled      = true;
        [ObservableProperty] private int    grid1Count;
        [ObservableProperty] private int    grid2Count;
        [ObservableProperty] private int    grid3Count;
        [ObservableProperty] private int    totalLinksCount;
        [ObservableProperty] private int    totalSelectedCount;
        [ObservableProperty] private int    grid2SelectedCount;
        [ObservableProperty] private int    grid3SelectedCount;
        [ObservableProperty] private int    grid2ResyncableCount;
        [ObservableProperty] private int    grid3ResyncableCount;
        [ObservableProperty] private int    unloadedCount;
        [ObservableProperty] private string searchText1          = string.Empty;
        [ObservableProperty] private string searchText2          = string.Empty;
        [ObservableProperty] private string searchText3          = string.Empty;
        [ObservableProperty] private bool   isLogExpanded        = false;

        /// <summary>Links whose existing workset will be renamed to the pattern name.</summary>
        [ObservableProperty] private int renameMatchCount;

        /// <summary>Links the matcher will not decide for the user.</summary>
        [ObservableProperty] private int needsReviewCount;

        /// <summary>Grid 2 toolbar toggle — show only the rows awaiting a decision.</summary>
        [ObservableProperty] private bool showReviewOnly;

        /// <summary>"3 IFC, 1 nested link(s) hidden" — empty when there is nothing to say.</summary>
        [ObservableProperty] private string exclusionText = string.Empty;

        public bool HasExclusions => !string.IsNullOrWhiteSpace(ExclusionText);

        partial void OnExclusionTextChanged(string value) => OnPropertyChanged(nameof(HasExclusions));

        // ── Run summary ──────────────────────────────────────────────────────

        [ObservableProperty] private RunSummary lastRunSummary;

        /// <summary>True once a run has finished — drives the "Last run" card's visibility.</summary>
        public bool HasLastRun => LastRunSummary != null;

        partial void OnLastRunSummaryChanged(RunSummary value) => OnPropertyChanged(nameof(HasLastRun));

        public string LogChevron => IsLogExpanded ? "▾" : "▸";

        // ── Commands ─────────────────────────────────────────────────────────
        // Log commands (CopyAllLog / CopySelectedLog / ClearLog) come from the base.

        public IRelayCommand CreateGrid2Command  { get; }
        public IRelayCommand CreateGrid3Command  { get; }
        public IRelayCommand ResyncCommand       { get; }
        public IRelayCommand ResyncGrid2Command  { get; }
        public IRelayCommand ResyncGrid3Command  { get; }
        public IRelayCommand CloseCommand        { get; }
        public IRelayCommand SelectAllCommand    { get; }
        public IRelayCommand SelectNoneCommand   { get; }
        public IRelayCommand SelectAllG3Command  { get; }
        public IRelayCommand SelectNoneG3Command { get; }
        public IRelayCommand ToggleLogCommand    { get; }
        public IRelayCommand CopySummaryCommand  { get; }
        public IRelayCommand ShowSummaryCommand  { get; }
        public IRelayCommand RefreshCommand      { get; }
        public IRelayCommand ToggleReviewOnlyCommand { get; }

        public event Action RequestClose;

        // ── Constructor ──────────────────────────────────────────────────────

        public WorksetCreatorViewModel(ExternalCommandData commandData)
        {
            _uidoc      = commandData.Application.ActiveUIDocument;
            _doc        = _uidoc.Document;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _service    = new WorksetService(LogFromService);
            _matcher    = new WorksetMatchService(LogFromService);
            _settingsService = new WorksetSettingsService(LogFromService);
            _externalEvent   = ExternalEvent.Create(_handler);

            // Settings first: the pattern and the IFC box both change what the very
            // first scan produces, so they have to be in place before LoadData runs.
            _settings = _settingsService.Load();
            ApplySettings(_settings);

            RecreateFilteredViews();

            CreateGrid2Command   = new RelayCommand(ExecuteCreateGrid2,    CanCreateGrid2);
            CreateGrid3Command   = new RelayCommand(ExecuteCreateGrid3,    CanCreateGrid3);
            ResyncCommand        = new RelayCommand(ExecuteResync,         CanResync);
            ResyncGrid2Command   = new RelayCommand(
                () => ExecuteResyncFor(WorksetGridCategory.NeedsWorkset, "Grid 2"),
                () => CanResyncGrid(WorksetGridCategory.NeedsWorkset));
            ResyncGrid3Command   = new RelayCommand(
                () => ExecuteResyncFor(WorksetGridCategory.NoInstances, "Grid 3"),
                () => CanResyncGrid(WorksetGridCategory.NoInstances));
            CloseCommand         = new RelayCommand(ExecuteClose);
            SelectAllCommand     = new RelayCommand(() => SetGrid2Selection(true));
            SelectNoneCommand    = new RelayCommand(() => SetGrid2Selection(false));
            SelectAllG3Command   = new RelayCommand(() => SetGrid3Selection(true));
            SelectNoneG3Command  = new RelayCommand(() => SetGrid3Selection(false));
            RefreshCommand       = new RelayCommand(RefreshData, () => !IsRunning);
            ToggleReviewOnlyCommand = new RelayCommand(() => ShowReviewOnly = !ShowReviewOnly);
            ToggleLogCommand     = new RelayCommand(() =>
            {
                IsLogExpanded = !IsLogExpanded;
                OnPropertyChanged(nameof(LogChevron));
            });
            CopySummaryCommand   = new RelayCommand(
                () => RunSummaryDialog.CopyReport(LastRunSummary),
                () => LastRunSummary != null);
            ShowSummaryCommand   = new RelayCommand(
                () => RunSummaryDialog.Show(LastRunSummary),
                () => LastRunSummary != null);

            Items.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (WorksetItem item in e.NewItems)
                        AttachItemHandler(item);
                RefreshCommandStates();
            };

            DetectedConventions.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasConventions));

            LoadData();
            ValidatePattern();
        }

        private void AttachItemHandler(WorksetItem item)
        {
            item.PropertyChanged += (s, e) =>
            {
                // A row's action and its review state both change whether the run can
                // go ahead, so all three re-evaluate the command guards.
                if (e.PropertyName == nameof(WorksetItem.IsSelected) ||
                    e.PropertyName == nameof(WorksetItem.SelectedAction) ||
                    e.PropertyName == nameof(WorksetItem.IsReviewResolved))
                {
                    if (e.PropertyName != nameof(WorksetItem.IsSelected))
                        Grid2ActionableItems?.Refresh();
                    RefreshCommandStates();
                }
            };
        }

        // ── Settings ─────────────────────────────────────────────────────────

        private void ApplySettings(WorksetCreatorSettings settings)
        {
            if (settings == null) return;

            _suppressSettingsSave = true;
            try
            {
                Pattern       = settings.Pattern;
                IsLogExpanded = settings.IsLogExpanded;
                OnPropertyChanged(nameof(LogChevron));
            }
            finally
            {
                _suppressSettingsSave = false;
            }
        }

        /// <summary>
        /// Copies the current UI state back onto the settings object and writes it.
        /// Called when the user changes something they would expect to stick, after a
        /// run, and on Close.
        /// </summary>
        private void SaveSettings()
        {
            if (_suppressSettingsSave || _settings == null) return;

            _settings.Pattern       = Pattern;
            _settings.IsLogExpanded = IsLogExpanded;

            _settingsService.Save(_settings);
        }

        partial void OnIsLogExpandedChanged(bool value) => SaveSettings();

        partial void OnShowReviewOnlyChanged(bool value)
        {
            Grid2ActionableItems?.Refresh();
            OnPropertyChanged(nameof(ReviewFilterText));
        }

        /// <summary>Label for the Grid 2 review-only toggle.</summary>
        public string ReviewFilterText =>
            ShowReviewOnly ? $"Showing review only ({NeedsReviewCount})"
                           : $"Needs review only ({NeedsReviewCount})";

        partial void OnNeedsReviewCountChanged(int value) => OnPropertyChanged(nameof(ReviewFilterText));

        private void ExecuteClose()
        {
            SaveSettings();
            RequestClose?.Invoke();
        }

        // ── Logging ──────────────────────────────────────────────────────────

        /// <summary>
        /// Service-side logger. The service runs inside the external event (Revit's
        /// UI thread), but the marshal is kept from V014/V015 so a log line can never
        /// touch LogEntries from the wrong thread.
        /// </summary>
        private void LogFromService(LogEntry entry)
        {
            if (entry == null) return;
            _dispatcher.Invoke(() => AddLog(entry.Level, entry.Message), DispatcherPriority.Background);
        }

        private void Log(LogLevel level, string message) => LogFromService(new LogEntry(level, message));

        // ── Filtered view management ─────────────────────────────────────────

        private void RecreateFilteredViews()
        {
            Grid1AssignedItems   = CreateFilteredView(i => i.GridCategory == WorksetGridCategory.AlreadyAssigned,  1);
            Grid2ActionableItems = CreateFilteredView(i => i.GridCategory == WorksetGridCategory.NeedsWorkset,     2);
            Grid3NoInstanceItems = CreateFilteredView(i => i.GridCategory == WorksetGridCategory.NoInstances,      3);
        }

        private ICollectionView CreateFilteredView(Predicate<WorksetItem> categoryFilter, int grid)
        {
            var cvs = new CollectionViewSource { Source = Items };
            cvs.Filter += (s, e) =>
            {
                var item = (WorksetItem)e.Item;
                if (!categoryFilter(item)) { e.Accepted = false; return; }

                // Grid 2 only: the review-only toggle narrows to rows still awaiting a
                // decision, so a 59-link model shows the 2 that matter.
                if (grid == 2 && ShowReviewOnly && !(item.NeedsReview && !item.IsReviewResolved))
                {
                    e.Accepted = false;
                    return;
                }

                string q = grid == 1 ? SearchText1 : grid == 2 ? SearchText2 : SearchText3;
                e.Accepted = string.IsNullOrWhiteSpace(q) ||
                             (item.DisplayName ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                             (item.LinkName    ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            };
            return cvs.View;
        }

        partial void OnSearchText1Changed(string value) => Grid1AssignedItems?.Refresh();
        partial void OnSearchText2Changed(string value) => Grid2ActionableItems?.Refresh();
        partial void OnSearchText3Changed(string value) => Grid3NoInstanceItems?.Refresh();

        // ── Data loading ─────────────────────────────────────────────────────

        private void LoadData()
        {
            LinkScan scan = _service.ScanLinks(_doc);

            _excludedIfc    = scan.ExcludedIfc;
            _excludedNested = scan.ExcludedNested;
            _includedIfc    = scan.IncludedIfc;
            ExclusionText   = scan.ExclusionText();

            // Every user workset in the model — the other half of the match.
            _worksets = _service.ReadAllWorksets(_doc);

            // Build the rows first, so each one carries the pattern-assembled name the
            // matcher needs for its exact-match test.
            int serial = 1;
            foreach (LinkRecord record in scan.Links)
            {
                var item = new WorksetItem
                {
                    SerialNumber  = serial++,
                    LinkTypeId    = record.TypeId,
                    LinkName      = record.Name,
                    DisplayName   = string.IsNullOrWhiteSpace(record.DisplayName)
                                        ? record.Name
                                        : record.DisplayName,
                    IsIfc         = record.IsIfc,
                    LoadState     = record.LoadState,
                    HasInstances  = record.HasInstances,
                    InstanceCount = record.InstanceCount
                };

                string proposedName = AssembleWorksetName(record.Name);
                string existingWs   = _service.CheckExistingWorkset(_doc, proposedName);
                item.ExistingWorksetName = existingWs;
                item.IsExistingWorkset   = !string.IsNullOrEmpty(existingWs);

                if (!record.HasInstances)
                {
                    // Genuinely nothing placed. Unloaded links no longer land here —
                    // their instances are counted by type id, so they go to Grid 2.
                    item.GridCategory        = WorksetGridCategory.NoInstances;
                    item.ProposedWorksetName = proposedName;
                    item.CurrentWorksetName  = "No instances";
                    item.IsSelected          = false;
                }
                else
                {
                    item.CurrentWorksetName = record.CurrentWorksetName;
                    item.IsMixedWorkset     = record.IsMixedWorkset;

                    if (item.IsExistingWorkset && record.IsFullyAssignedTo(existingWs))
                    {
                        item.GridCategory          = WorksetGridCategory.AlreadyAssigned;
                        item.ProposedWorksetName   = existingWs;
                        item.IsSelected            = false;
                        item.IsExactMatchAssigned  = true;
                        item.ProposedWorksetTooltip = $"All instances already assigned to '{existingWs}'";
                    }
                    else
                    {
                        item.GridCategory        = WorksetGridCategory.NeedsWorkset;
                        item.ProposedWorksetName = proposedName;
                        item.IsSelected          = true;

                        item.ProposedWorksetTooltip = item.IsExistingWorkset
                            ? $"Workset '{existingWs}' exists but assignment is incomplete"
                            : null;

                        if (record.IsUnloaded)
                            item.ProposedWorksetTooltip =
                                (item.ProposedWorksetTooltip == null ? string.Empty : item.ProposedWorksetTooltip + " · ") +
                                "Link is unloaded — its instances are still in the model and will be re-assigned";
                    }
                }

                Items.Add(item);
            }

            ApplyMatches();

            // Seeded from the whole model now, not just Grid 1 — the original version
            // could not see a workset that already existed under another pattern.
            _service.ResolveDuplicates(Items, _worksets);

            RefreshCounts();
            RefreshCommandStates();

            Log(LogLevel.Info,
                $"Loaded {Items.Count} linked file(s): " +
                $"{Grid1Count} assigned, {Grid2Count} actionable, {Grid3Count} with no instances" +
                (UnloadedCount > 0 ? $" ({UnloadedCount} unloaded)" : string.Empty));

            if (scan.IncludedIfc > 0)
                Log(LogLevel.Info,
                    $"Included {scan.IncludedIfc} IFC link(s) — each is a .ifc.rvt shadow model, " +
                    "so it ends in .rvt, is listed, and gets a workset like any Revit link.");

            if (scan.ExcludedTotal > 0)
                Log(LogLevel.Info,
                    $"Not listed: {scan.ExcludedTotal} link(s) — {scan.ExcludedIfc} bare .ifc " +
                    $"(not a Revit file), {scan.ExcludedNonRevit} other non-Revit file(s), " +
                    $"{scan.ExcludedNested} nested (workset follows the parent link).");

            if (NeedsReviewCount > 0)
                Log(LogLevel.Warning,
                    $"{NeedsReviewCount} link(s) need your decision — they start unticked and are held " +
                    "out of the run until you choose an action.");
        }

        /// <summary>
        /// Runs the matcher over the freshly built rows and maps its verdicts onto them:
        /// the existing match, the suggested action, the picker's options, and whether
        /// the row needs the user's eyes.
        /// </summary>
        private void ApplyMatches()
        {
            DetectedConventions.Clear();

            if (_worksets.Count == 0)
            {
                // No worksets to match against — every link simply gets a new one.
                foreach (WorksetItem item in Items)
                    BuildOptionsFor(item, new LinkMatch
                    {
                        LinkTypeId  = item.LinkTypeId,
                        Explanation = "No worksets could be read from this model, so nothing was " +
                                      "matched — every link simply gets a new workset."
                    });
                return;
            }

            var inputs = Items.Select(i => new WorksetMatchService.MatchInput
            {
                LinkTypeId   = i.LinkTypeId,
                LinkName     = i.LinkName,
                ProposedName = i.ProposedWorksetName
            });

            MatchResult matches = _matcher.Match(inputs, _worksets, _settings);

            foreach (NamingConvention convention in matches.Conventions)
                DetectedConventions.Add(convention);

            foreach (WorksetItem item in Items)
                BuildOptionsFor(item, matches.For(item.LinkTypeId));
        }

        /// <summary>
        /// Puts one match onto one row and builds its Action picker. The option set
        /// depends on the match: there is no "Rename" to offer when nothing matched,
        /// and no "Create new" worth offering when a workset already carries the name.
        /// </summary>
        private void BuildOptionsFor(WorksetItem item, LinkMatch match)
        {
            WorksetCandidate best = match.Best;

            item.MatchedWorksetName         = best?.Workset?.Name ?? string.Empty;
            item.MatchedWorksetId           = best?.Workset?.Id ?? 0;
            item.MatchedWorksetElementCount = best?.Workset?.ElementCount ?? 0;
            item.MatchConfidence            = match.Confidence;
            item.CandidateCount             = match.Candidates.Count;
            item.MatchExplanation           = match.Explanation;
            item.NeedsReview                = match.NeedsReview;
            item.IsReviewResolved           = false;

            var options = new List<WorksetActionOption>();

            if (match.Confidence == MatchConfidence.None)
            {
                options.Add(new WorksetActionOption
                {
                    Action     = WorksetAction.Create,
                    Label      = "Create new",
                    TargetName = item.ProposedWorksetName,
                    Tooltip    = "Create the pattern-named workset and assign this link's instances to it."
                });
            }
            else if (match.Confidence == MatchConfidence.Exact)
            {
                options.Add(new WorksetActionOption
                {
                    Action          = WorksetAction.Reuse,
                    Label           = "Use existing",
                    TargetName      = item.MatchedWorksetName,
                    TargetWorksetId = item.MatchedWorksetId,
                    Tooltip         = "A workset already carries this name — instances are assigned " +
                                      "into it and nothing is created."
                });
            }
            else if (match.Confidence == MatchConfidence.Ambiguous)
            {
                // The model ALREADY holds more than one workset for this link. The user
                // picks the survivor, so the picker offers one option per candidate —
                // each naming its workset — rather than a bare "Rename" that would
                // silently act on whichever happened to rank first.
                foreach (WorksetCandidate candidate in match.Candidates)
                {
                    string name = candidate.Workset?.Name ?? string.Empty;
                    int    id   = candidate.Workset?.Id ?? 0;
                    int    held = candidate.Workset?.ElementCount ?? 0;

                    options.Add(new WorksetActionOption
                    {
                        Action          = WorksetAction.Rename,
                        Label           = $"Keep '{name}' → rename",
                        TargetName      = item.ProposedWorksetName,
                        TargetWorksetId = id,
                        Tooltip         = $"Make '{name}' the survivor: rename it to the pattern name and " +
                                          $"assign every instance of this link into it. It currently holds " +
                                          $"{held} element(s). The other candidate(s) are left in place and " +
                                          "named in the summary — the Revit API cannot delete a workset."
                    });

                    options.Add(new WorksetActionOption
                    {
                        Action          = WorksetAction.Adopt,
                        Label           = $"Keep '{name}' as is",
                        TargetName      = name,
                        TargetWorksetId = id,
                        Tooltip         = $"Make '{name}' the survivor but leave its name alone. " +
                                          $"It currently holds {held} element(s)."
                    });
                }

                options.Add(new WorksetActionOption
                {
                    Action     = WorksetAction.Create,
                    Label      = "Create new anyway",
                    TargetName = item.ProposedWorksetName,
                    Tooltip    = "Create a separate workset. This leaves this link with MORE than one " +
                                 "workset — only pick it if every match is wrong."
                });
            }
            else
            {
                // Convention or Weak — one matched workset, so it can be renamed or
                // adopted. Rename is the one that leaves the model with a single,
                // consistently named workset for this link.
                options.Add(new WorksetActionOption
                {
                    Action          = WorksetAction.Rename,
                    Label           = "Rename → pattern",
                    TargetName      = item.ProposedWorksetName,
                    TargetWorksetId = item.MatchedWorksetId,
                    Tooltip         = $"Rename '{item.MatchedWorksetName}' to the pattern name, then assign. " +
                                      $"One workset for this link, consistently named. That workset currently " +
                                      $"holds {item.MatchedWorksetElementCount} element(s)."
                });

                options.Add(new WorksetActionOption
                {
                    Action          = WorksetAction.Adopt,
                    Label           = "Keep existing name",
                    TargetName      = item.MatchedWorksetName,
                    TargetWorksetId = item.MatchedWorksetId,
                    Tooltip         = $"Assign into '{item.MatchedWorksetName}' as it stands — nothing " +
                                      "created, nothing renamed, naming stays inconsistent."
                });

                options.Add(new WorksetActionOption
                {
                    Action     = WorksetAction.Create,
                    Label      = "Create new anyway",
                    TargetName = item.ProposedWorksetName,
                    Tooltip    = "Create a separate workset. This leaves TWO worksets for this link — " +
                                 "only pick it if the match is wrong."
                });
            }

            options.Add(new WorksetActionOption
            {
                Action     = WorksetAction.Skip,
                Label      = "Skip this link",
                TargetName = string.Empty,
                Tooltip    = "Leave this link without a workset. Reported in the completion summary."
            });

            item.BuildActionOptions(options, match.SuggestedAction);

            // A row the matcher will not decide starts unticked. The user resolves it by
            // picking an action, which is what IsReviewResolved records.
            if (item.NeedsReview) item.IsSelected = false;
        }

        private void RefreshData()
        {
            _dispatcher.Invoke(() =>
            {
                Log(LogLevel.Info, "Refreshing UI data...");
                Items.Clear();
                LoadData();
                RecreateFilteredViews();
                Log(LogLevel.Info, "UI refresh complete.");
            });
        }

        private void ValidatePattern()
        {
            if (string.IsNullOrWhiteSpace(Pattern) || !Pattern.Contains("{name}"))
            {
                HasPatternError      = true;
                PatternErrorMessage  = "Pattern must contain {name}";
                IsCreateEnabled      = false;
            }
            else
            {
                HasPatternError      = false;
                PatternErrorMessage  = string.Empty;
                IsCreateEnabled      = true;
            }
        }

        partial void OnPatternChanged(string value)
        {
            ValidatePattern();
            UpdateProposedNamesSilent();
            RefreshCommandStates();
            SaveSettings();
        }

        private string AssembleWorksetName(string linkName)
        {
            string cleaned   = _service.CleanName(linkName);
            string assembled = Pattern.Replace("{name}", cleaned);
            return _service.CleanName(assembled);
        }

        private string AssembleWorksetNameSilent(string linkName)
        {
            string cleaned   = _service.CleanNameSilent(linkName);
            string assembled = Pattern.Replace("{name}", cleaned);
            return _service.CleanNameSilent(assembled);
        }

        /// <summary>Updates proposed names without writing to the log (live typing).</summary>
        private void UpdateProposedNamesSilent()
        {
            foreach (var item in Items.Where(i =>
                i.GridCategory != WorksetGridCategory.AlreadyAssigned))
            {
                // Setting ProposedWorksetName refreshes FinalWorksetName through the
                // model's own partial handler, so Create and Rename rows follow the
                // pattern box as it is typed. The picker's labels are deliberately
                // action-only ("Rename → pattern"), so they never go stale.
                item.ProposedWorksetName = AssembleWorksetNameSilent(item.LinkName);
            }
        }

        // ── Selection helpers ────────────────────────────────────────────────

        /// <summary>
        /// Select all / none for Grid 2, scoped to the rows the view is currently
        /// showing (filter and review-only toggle included) per the DataGrid spec.
        /// Unresolved review rows are never ticked by Select all: the user has to make
        /// the decision, and ticking them would only be blocked by the gate.
        /// </summary>
        private void SetGrid2Selection(bool selected)
        {
            int held = 0;

            foreach (WorksetItem item in VisibleRows(Grid2ActionableItems, WorksetGridCategory.NeedsWorkset))
            {
                if (selected && item.NeedsReview && !item.IsReviewResolved)
                {
                    held++;
                    continue;
                }
                item.IsSelected = selected;
            }

            if (held > 0)
                Log(LogLevel.Warning,
                    $"{held} row(s) left unticked — they need a decision first " +
                    "(pick an action in the Action column).");

            RefreshCommandStates();
        }

        private void SetGrid3Selection(bool selected)
        {
            foreach (WorksetItem item in VisibleRows(Grid3NoInstanceItems, WorksetGridCategory.NoInstances))
                item.IsSelected = selected;
            RefreshCommandStates();
        }

        /// <summary>
        /// The rows a grid is actually showing. Falls back to the category when the
        /// view has not been built yet, so a command can never act on nothing.
        /// </summary>
        private List<WorksetItem> VisibleRows(ICollectionView view, WorksetGridCategory category)
        {
            if (view == null)
                return Items.Where(i => i.GridCategory == category).ToList();

            return view.Cast<WorksetItem>().ToList();
        }

        // ── Command guards ───────────────────────────────────────────────────

        private bool CanResync() =>
            IsResyncEnabled &&
            Items.Any(i => i.IsSelected && i.IsExistingWorkset &&
                           (i.GridCategory == WorksetGridCategory.NeedsWorkset ||
                            i.GridCategory == WorksetGridCategory.NoInstances));

        private bool CanResyncGrid(WorksetGridCategory category) =>
            IsResyncEnabled && Items.Any(i => i.IsSelected && i.IsExistingWorkset && i.GridCategory == category);

        /// <summary>
        /// A row counts towards Create only when it would actually do something: ticked,
        /// not skipped, and either not a review row or one the user has resolved.
        /// </summary>
        private bool IsActionable(WorksetItem item) =>
            item.IsSelected &&
            item.SelectedAction != WorksetAction.Skip &&
            (!item.NeedsReview || item.IsReviewResolved);

        private bool CanCreateGrid2() =>
            IsCreateEnabled && !HasPatternError &&
            Items.Any(i => IsActionable(i) && i.GridCategory == WorksetGridCategory.NeedsWorkset);

        private bool CanCreateGrid3() =>
            IsCreateEnabled && !HasPatternError &&
            Items.Any(i => IsActionable(i) && i.GridCategory == WorksetGridCategory.NoInstances);

        // ── Command executions ───────────────────────────────────────────────

        private void RunInRevit(
            List<WorksetRequest> toProcess,
            RunSummary summary,
            string doneMessage,
            string errorPrefix)
        {
            IsCreateEnabled = false;
            IsResyncEnabled = false;
            IsRunning       = true;
            Progress        = 0;
            SummaryText     = $"{summary.Mode} running — {toProcess.Count} link(s)...";
            RefreshCommandStates();

            _handler.Queue(_ =>
            {
                RunSummary result = summary;
                try
                {
                    result = _service.CreateAndAssign(_doc, toProcess, summary, ReportProgress);
                    Log(LogLevel.Info, doneMessage);
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Error, $"{errorPrefix}: {ex.Message}");
                }
                finally
                {
                    IsCreateEnabled = !HasPatternError;
                    IsResyncEnabled = true;
                    IsRunning       = false;
                    Progress        = 100;
                    RefreshData();
                    PublishSummary(result);
                    SaveSettings();
                    RefreshCommandStates();
                }
            });

            ExternalEventRequest request = _externalEvent.Raise();
            if (request != ExternalEventRequest.Accepted)
            {
                Log(LogLevel.Error, $"Revit rejected the request ({request}). Try again.");
                IsCreateEnabled = !HasPatternError;
                IsResyncEnabled = true;
                IsRunning       = false;
                SummaryText     = "Revit rejected the request — nothing was changed.";
                RefreshCommandStates();
            }
        }

        /// <summary>Progress callback handed to the service — (processed, total).</summary>
        private void ReportProgress(int processed, int total)
        {
            if (total > 0) Progress = processed * 100.0 / total;
            if (processed % 5 == 0) KeepUIResponsive();
        }

        /// <summary>
        /// The run report: status strip, the window's "Last run" card, then the
        /// TaskDialog — on the UI thread, after RefreshData, never mid-transaction.
        /// </summary>
        private void PublishSummary(RunSummary summary)
        {
            if (summary == null) return;

            _dispatcher.Invoke(() =>
            {
                LastRunSummary = summary;
                SummaryText    = summary.HeadlineText();

                Log(summary.Errors > 0 ? LogLevel.Error
                        : summary.HasProblems ? LogLevel.Warning
                        : LogLevel.Success,
                    summary.HeadlineText());

                // The completion line, always — including its zeroes.
                Log(LogLevel.Info, summary.CompletionLine());

                if (summary.LinksWithoutWorkset.Count > 0)
                    Log(LogLevel.Warning,
                        $"{summary.LinksWithoutWorkset.Count} link(s) still have no workset: " +
                        string.Join(", ", summary.LinksWithoutWorkset));

                CopySummaryCommand?.NotifyCanExecuteChanged();
                ShowSummaryCommand?.NotifyCanExecuteChanged();
                RunSummaryDialog.Show(summary);
            });
        }

        private RunSummary NewSummary(string mode, string label, int selectedCount) => new()
        {
            Mode           = mode,
            Label          = label,
            LinksInModel   = Items.Count,
            LinksSelected  = selectedCount,
            ExcludedIfc    = _excludedIfc,
            ExcludedNested = _excludedNested,
            IncludedIfc    = _includedIfc
        };

        /// <summary>
        /// Builds the request from the row's decision. ProposedName is the row's
        /// FinalWorksetName — the single answer to "which workset does this link end
        /// up in" — so the service never re-derives it.
        /// </summary>
        private static WorksetRequest ToRequest(WorksetItem item, bool createNew)
        {
            WorksetAction action = item.SelectedAction;

            // Resync asks for assignment into an existing workset, never creation.
            if (!createNew && action == WorksetAction.Create)
                action = WorksetAction.Reuse;

            return new WorksetRequest
            {
                LinkTypeId       = item.LinkTypeId,
                LinkName         = item.DisplayName,
                ProposedName     = string.IsNullOrWhiteSpace(item.FinalWorksetName)
                                       ? item.ProposedWorksetName
                                       : item.FinalWorksetName,
                Action           = action,

                // CreateNew means "creation is permitted", not "create instead of the
                // action". The service tries the action's own target first and only
                // creates when nothing is found, so this is the fallback that matters
                // when the pattern box was edited after the scan: an exact-match row
                // whose name no longer exists gets a new workset rather than being
                // stranded as a skip. A Resync run passes createNew: false and so can
                // still never create anything.
                CreateNew        = createNew && action != WorksetAction.Skip,

                // The chosen option's workset, not the best match: on an ambiguous row
                // the user picked which candidate survives, and that choice lives on the
                // option.
                MatchedWorksetId = action == WorksetAction.Rename || action == WorksetAction.Adopt
                                       ? (item.SelectedTargetWorksetId != 0
                                              ? item.SelectedTargetWorksetId
                                              : item.MatchedWorksetId)
                                       : 0,
                RenameFrom       = !string.IsNullOrWhiteSpace(item.SelectedTargetName)
                                       ? item.SelectedTargetName
                                       : item.MatchedWorksetName ?? string.Empty,
                IsUnloaded       = item.IsUnloaded
            };
        }

        private void ExecuteResync() =>
            ResyncExisting(i => i.GridCategory == WorksetGridCategory.NeedsWorkset ||
                                i.GridCategory == WorksetGridCategory.NoInstances, "Grid 2 & 3");

        private void ExecuteResyncFor(WorksetGridCategory category, string label) =>
            ResyncExisting(i => i.GridCategory == category, label);

        // Resync never creates worksets — it only reassigns links to worksets that already exist.
        private void ResyncExisting(Func<WorksetItem, bool> scope, string label)
        {
            var selected = Items.Where(i => i.IsSelected && scope(i)).ToList();
            var toProcess = selected
                .Where(i => i.IsExistingWorkset)
                .Select(i => ToRequest(i, createNew: false))
                .ToList();

            var summary = NewSummary("Resync", label, selected.Count);

            foreach (var skipped in selected.Where(i => !i.IsExistingWorkset))
            {
                Log(LogLevel.Warning,
                    $"Skipped '{skipped.LinkName}' — workset '{skipped.ProposedWorksetName}' does not exist (use Create).");

                summary.Outcomes.Add(new LinkOutcome
                {
                    LinkName       = skipped.DisplayName,
                    WorksetName    = skipped.ProposedWorksetName,
                    Outcome        = RunOutcome.Skipped,
                    InstancesTotal = skipped.InstanceCount,
                    WasUnloaded    = skipped.IsUnloaded,
                    Note           = "Workset does not exist — use Create"
                });
            }

            if (!toProcess.Any())
            {
                Log(LogLevel.Warning, $"Nothing to resync in {label} — no checked link has an existing workset.");
                if (summary.Outcomes.Count > 0) PublishSummary(summary);
                return;
            }

            Log(LogLevel.Info, $"Resyncing {toProcess.Count} link(s) from {label}...");
            RunInRevit(toProcess, summary, $"Resync complete for {label}.", "Resync error");
        }

        private void ExecuteCreateGrid2() => ExecuteCreateFor(WorksetGridCategory.NeedsWorkset, "Grid 2");
        private void ExecuteCreateGrid3() => ExecuteCreateFor(WorksetGridCategory.NoInstances,  "Grid 3");

        private void ExecuteCreateFor(WorksetGridCategory category, string label)
        {
            var selected = Items
                .Where(i => i.IsSelected && i.GridCategory == category)
                .ToList();

            if (!selected.Any())
            {
                Log(LogLevel.Warning, $"No {label} links selected for creation.");
                return;
            }

            // ── The gate ────────────────────────────────────────────────────
            // Runs before anything is submitted and before the TransactionGroup opens.
            // The workset list is re-read rather than taken from the last scan: another
            // user may have added one since, and the whole point of this check is to be
            // right rather than quick.
            List<WorksetInfo> currentWorksets = _service.ReadAllWorksets(_doc);
            PreflightResult check = _preflight.Check(selected, Items, currentWorksets);

            if (!check.CanRun)
            {
                Log(LogLevel.Error,
                    $"Pre-flight check failed with {check.Conflicts.Count} conflict(s) — " +
                    "nothing was changed.");

                foreach (PreflightConflict conflict in check.Conflicts)
                    Log(LogLevel.Error, $"{conflict.Title}: {conflict.Detail}");

                SummaryText = $"Blocked — {check.Conflicts.Count} conflict(s) would create duplicate worksets.";
                PreflightConflictDialog.Show(check);
                return;
            }

            Log(LogLevel.Success,
                $"Pre-flight check passed — one workset per link across {selected.Count} selected row(s).");

            var toProcess = selected
                .Select(i => ToRequest(i, createNew: true))
                .ToList();

            RunSummary summary = NewSummary("Create", label, toProcess.Count);

            // Carry the user's deliberate omissions into the report, so "every link has
            // a workset" is verifiable rather than assumed.
            foreach (string name in check.LinksWithoutWorkset)
                if (!summary.LinksWithoutWorkset.Contains(name))
                    summary.LinksWithoutWorkset.Add(name);

            if (check.SkippedLinkNames.Count > 0)
                Log(LogLevel.Warning,
                    $"{check.SkippedLinkNames.Count} selected link(s) set to Skip: " +
                    string.Join(", ", check.SkippedLinkNames));

            RunInRevit(toProcess, summary,
                $"Done — processed {toProcess.Count} link(s) from {label}.", "Error");
        }

        // ── UI helpers ───────────────────────────────────────────────────────

        private void RefreshCounts()
        {
            Grid1Count = Items.Count(i => i.GridCategory == WorksetGridCategory.AlreadyAssigned);
            Grid2Count = Items.Count(i => i.GridCategory == WorksetGridCategory.NeedsWorkset);
            Grid3Count = Items.Count(i => i.GridCategory == WorksetGridCategory.NoInstances);
            TotalLinksCount    = Grid1Count + Grid2Count + Grid3Count;
            UnloadedCount      = Items.Count(i => i.IsUnloaded);
            Grid2SelectedCount = Items.Count(i => i.IsSelected && i.GridCategory == WorksetGridCategory.NeedsWorkset);
            Grid3SelectedCount = Items.Count(i => i.IsSelected && i.GridCategory == WorksetGridCategory.NoInstances);
            TotalSelectedCount = Grid2SelectedCount + Grid3SelectedCount;
            Grid2ResyncableCount = Items.Count(i => i.IsSelected && i.IsExistingWorkset && i.GridCategory == WorksetGridCategory.NeedsWorkset);
            Grid3ResyncableCount = Items.Count(i => i.IsSelected && i.IsExistingWorkset && i.GridCategory == WorksetGridCategory.NoInstances);

            RenameMatchCount = Items.Count(i => i.SelectedAction == WorksetAction.Rename);
            NeedsReviewCount = Items.Count(i => i.NeedsReview && !i.IsReviewResolved);
        }

        /// <summary>
        /// Re-evaluates every command guard.
        ///
        /// The null-conditional calls are load-bearing, not defensive noise: applying
        /// saved settings assigns Pattern, which fires OnPatternChanged, which lands
        /// here — and that happens before the commands are constructed. Do not remove
        /// the ?. operators.
        /// </summary>
        private void RefreshCommandStates()
        {
            RefreshCounts();
            CreateGrid2Command?.NotifyCanExecuteChanged();
            CreateGrid3Command?.NotifyCanExecuteChanged();
            ResyncCommand?.NotifyCanExecuteChanged();
            ResyncGrid2Command?.NotifyCanExecuteChanged();
            ResyncGrid3Command?.NotifyCanExecuteChanged();
            RefreshCommand?.NotifyCanExecuteChanged();
        }

        public void KeepUIResponsive()
        {
            _dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        }
    }
}
