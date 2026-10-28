using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Commands;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Services;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Infrastructure;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.UI.ViewModels
{
    /// <summary>
    /// Window state for Sections To Sheet. Holds the latest read of the model and the measured
    /// sizes, and rebuilds the <see cref="SectionPlan"/> in memory on every tick or setting change,
    /// so the preview is live. All Revit access goes through <see cref="ISectionsSession"/>.
    /// </summary>
    public partial class SectionsToSheetViewModel : ObservableObject, IDisposable
    {
        private const string NotMeasured = "Not measured yet";

        private readonly ISectionsSession _session;
        private readonly ListCollectionView _rowsView;
        private readonly Dictionary<long, SectionRowViewModel> _rowsByKey = new();
        private readonly HashSet<long> _unticked = new();
        private readonly Dictionary<long, Dictionary<long, Footprint>> _footprints = new();
        private readonly Dictionary<long, Dictionary<long, string>> _problems = new();
        private readonly Dictionary<long, (LayoutRect? Frame, string Problem)> _frames = new();
        private SourceSnapshot _snapshot;
        private List<long> _pickedKeys = new();
        private string _lastSheetNumber;
        private string _busyText = string.Empty;
        private bool _ready;
        private bool _syncingLists;
        private bool _measurePending;

        /// <summary>Raised whenever <see cref="Plan"/> is rebuilt (the window redraws the preview).</summary>
        public event EventHandler PlanChanged;

        /// <summary>Creates the view model over <paramref name="session"/> with the saved <paramref name="settings"/>.</summary>
        public SectionsToSheetViewModel(ISectionsSession session, SectionsToSheetSettings settings)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _rowsView = new ListCollectionView(Rows) { Filter = o => o is SectionRowViewModel r && MatchesSearch(r) };

            var s = (settings ?? new SectionsToSheetSettings()).Normalize();
            SheetName = s.SheetName;
            OpenSheetWhenDone = s.OpenSheetWhenDone;
            Mode = s.Mode;
            Order = s.Order;
            Vertical = s.Vertical;
            LastRow = s.LastRow;
            Overflow = s.Overflow;
            RemoveFromOldSheets = s.RemoveFromOldSheets;
            DeleteEmptySheets = s.DeleteEmptySheets;
            MarginTopMm = s.MarginTopMm;
            MarginBottomMm = s.MarginBottomMm;
            MarginLeftMm = s.MarginLeftMm;
            MarginRightMm = s.MarginRightMm;
            MinHorizontalGapMm = s.MinHorizontalGapMm;
            MinVerticalGapMm = s.MinVerticalGapMm;
            RowToleranceMm = s.RowToleranceMm;
            _lastSheetNumber = s.LastSheetNumber;
            UpdateNotes();

            LoadSnapshot(session.Initial);
            _syncingLists = true;
            SelectedTitleBlock = TitleBlocks.FirstOrDefault(t => t.Key == session.InitialTitleBlockKey) ?? TitleBlocks.FirstOrDefault();
            SelectedViewportType = ViewportTypes.FirstOrDefault(t => t.Key == session.InitialViewportTypeKey) ?? ViewportTypes.FirstOrDefault();
            _syncingLists = false;
            StoreMeasure(session.InitialMeasure, session.Initial.Sections.Keys);

            SheetNumber = SheetNumbering.SuggestAfter(_lastSheetNumber, _snapshot.ExistingSheetNumbers);
            SourceMode = _snapshot.SelectionKeys.Count > 0 ? SourceMode.Selection : SourceMode.View;

            _ready = true;
            RebuildRows();
            Recompute();

            Log(LogLevel.Info, $"Read {_snapshot.InViewKeys.Count} section marker(s) shown in '{_snapshot.SourceViewName}'"
                + (_snapshot.SelectionKeys.Count > 0 ? $", {_snapshot.SelectionKeys.Count} selected" : string.Empty) + ".");
            if (_snapshot.IgnoredSelectionCount > 0)
                Log(LogLevel.Info, $"{_snapshot.IgnoredSelectionCount} selected element(s) are not section markers and were left out.");
            if (TitleBlocks.Count == 0)
                Log(LogLevel.Warning, "The project has no title block. Load one, then press Refresh.");
        }

        // ── Collections ─────────────────────────────────────────────────

        /// <summary>The sections from the chosen source, in placement order.</summary>
        public ObservableCollection<SectionRowViewModel> Rows { get; } = new();

        /// <summary><see cref="Rows"/> filtered by the search box.</summary>
        public ICollectionView RowsView => _rowsView;

        /// <summary>Activity log.</summary>
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        /// <summary>Title block types.</summary>
        public ObservableCollection<NamedType> TitleBlocks { get; } = new();

        /// <summary>Viewport types.</summary>
        public ObservableCollection<NamedType> ViewportTypes { get; } = new();

        /// <summary>One tab per new sheet (shown when there are two or more).</summary>
        public ObservableCollection<PageTabViewModel> PageTabs { get; } = new();

        /// <summary>Sections that come off their sheet (confirm dialog).</summary>
        public ObservableCollection<MoveLine> ConfirmMoves { get; } = new();

        /// <summary>Current plan; never null after construction.</summary>
        public SectionPlan Plan { get; private set; }

        /// <summary>The new sheet shown in the preview, or null.</summary>
        public PlannedSheet CurrentSheet => Plan?.Sheets.ElementAtOrDefault(CurrentPage);

        // ── Source ──────────────────────────────────────────────────────

        [ObservableProperty] private string sourceViewName = string.Empty;
        [ObservableProperty] private SourceMode sourceMode;
        [ObservableProperty] private string viewSourceLabel = string.Empty;
        [ObservableProperty] private string selectionSourceLabel = string.Empty;
        [ObservableProperty] private string pickedSourceLabel = string.Empty;
        [ObservableProperty] private bool hasPicked;
        [ObservableProperty] private string sourceNote = string.Empty;
        [ObservableProperty] private string searchText = string.Empty;
        [ObservableProperty] private string listCountText = string.Empty;

        // ── New sheet ───────────────────────────────────────────────────

        [ObservableProperty] private NamedType selectedTitleBlock;
        [ObservableProperty] private NamedType selectedViewportType;
        [ObservableProperty] private string sheetNumber = string.Empty;
        [ObservableProperty] private string sheetName = string.Empty;
        [ObservableProperty] private bool openSheetWhenDone;

        // ── Layout ──────────────────────────────────────────────────────

        [ObservableProperty] private ArrangeMode mode;
        [ObservableProperty] private SectionOrder order;
        [ObservableProperty] private VerticalMode vertical;
        [ObservableProperty] private LastRowMode lastRow;
        [ObservableProperty] private OverflowMode overflow;
        [ObservableProperty] private bool isRowsMode;
        [ObservableProperty] private bool isPlanOrder;
        [ObservableProperty] private string modeNote = string.Empty;
        [ObservableProperty] private string orderNote = string.Empty;

        // ── Already on a sheet ──────────────────────────────────────────

        [ObservableProperty] private bool removeFromOldSheets;
        [ObservableProperty] private bool deleteEmptySheets;
        [ObservableProperty] private string placedSummary = string.Empty;
        [ObservableProperty] private string emptySheetsText = string.Empty;
        [ObservableProperty] private string lockedText = string.Empty;
        [ObservableProperty] private bool hasLocked;

        // ── Spacing (mm) ────────────────────────────────────────────────

        [ObservableProperty] private double marginTopMm;
        [ObservableProperty] private double marginBottomMm;
        [ObservableProperty] private double marginLeftMm;
        [ObservableProperty] private double marginRightMm;
        [ObservableProperty] private double minHorizontalGapMm;
        [ObservableProperty] private double minVerticalGapMm;
        [ObservableProperty] private double rowToleranceMm;

        // ── Metrics, preview, status ────────────────────────────────────

        [ObservableProperty] private string pickedText = string.Empty;
        [ObservableProperty] private int movingCount;
        [ObservableProperty] private int cantPlaceCount;
        [ObservableProperty] private int sheetCount;
        [ObservableProperty] private string fillText = "—";
        [ObservableProperty] private int currentPage;
        [ObservableProperty] private bool hasTabs;
        [ObservableProperty] private string previewTitle = string.Empty;
        [ObservableProperty] private string layoutText = "—";
        [ObservableProperty] private string horizontalGapText = "—";
        [ObservableProperty] private string verticalGapText = "—";
        [ObservableProperty] private string scalesText = "—";
        [ObservableProperty] private bool hasMixedScales;
        [ObservableProperty] private string blockerText = string.Empty;
        [ObservableProperty] private bool hasBlocker;
        [ObservableProperty] private string infoText = string.Empty;
        [ObservableProperty] private bool hasInfo;
        [ObservableProperty] private string warningsText = string.Empty;
        [ObservableProperty] private bool hasWarnings;
        [ObservableProperty] private string statusText = string.Empty;
        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private bool isPicking;
        [ObservableProperty] private bool canCreate;

        // ── Confirm dialog ──────────────────────────────────────────────

        [ObservableProperty] private bool isConfirmOpen;
        [ObservableProperty] private string confirmTitle = string.Empty;
        [ObservableProperty] private string confirmSkipText = string.Empty;
        [ObservableProperty] private bool hasConfirmSkip;
        [ObservableProperty] private string confirmEmptyText = string.Empty;

        /// <summary>True when no request is running in Revit.</summary>
        public bool IsIdle => !IsBusy;

        // ── Change handlers → live re-plan ──────────────────────────────

        partial void OnSourceModeChanged(SourceMode value)
        {
            UpdateSourceNote();
            if (!_ready) return;
            RebuildRows();
            Recompute();
        }

        partial void OnSearchTextChanged(string value)
        {
            _rowsView.Refresh();
            UpdateListCount();
        }

        partial void OnSelectedTitleBlockChanged(NamedType value) { if (!_syncingLists) Recompute(); }
        partial void OnSelectedViewportTypeChanged(NamedType value) { if (!_syncingLists) { RebuildRows(); Recompute(); } }
        partial void OnSheetNumberChanged(string value) => Recompute();
        partial void OnSheetNameChanged(string value) => Recompute();

        partial void OnModeChanged(ArrangeMode value) { UpdateNotes(); Recompute(); }
        partial void OnOrderChanged(SectionOrder value) { UpdateNotes(); RebuildRows(); Recompute(); }
        partial void OnVerticalChanged(VerticalMode value) => Recompute();
        partial void OnLastRowChanged(LastRowMode value) => Recompute();
        partial void OnOverflowChanged(OverflowMode value) => Recompute();
        partial void OnRemoveFromOldSheetsChanged(bool value) => Recompute();
        partial void OnDeleteEmptySheetsChanged(bool value) => Recompute();

        partial void OnMarginTopMmChanged(double value) => ClampOrRecompute(value, v => MarginTopMm = v);
        partial void OnMarginBottomMmChanged(double value) => ClampOrRecompute(value, v => MarginBottomMm = v);
        partial void OnMarginLeftMmChanged(double value) => ClampOrRecompute(value, v => MarginLeftMm = v);
        partial void OnMarginRightMmChanged(double value) => ClampOrRecompute(value, v => MarginRightMm = v);
        partial void OnMinHorizontalGapMmChanged(double value) => ClampOrRecompute(value, v => MinHorizontalGapMm = v);
        partial void OnMinVerticalGapMmChanged(double value) => ClampOrRecompute(value, v => MinVerticalGapMm = v);

        partial void OnRowToleranceMmChanged(double value)
        {
            if (!double.IsFinite(value) || value < 0) { RowToleranceMm = 0; return; }
            if (!_ready) return;
            RebuildRows();
            Recompute();
        }

        partial void OnCurrentPageChanged(int value)
        {
            foreach (var tab in PageTabs)
                tab.IsCurrent = tab.Index == value;
            UpdatePreviewTexts();
            PlanChanged?.Invoke(this, EventArgs.Empty);
        }

        partial void OnIsBusyChanged(bool value)
        {
            OnPropertyChanged(nameof(IsIdle));
            RefreshCommand.NotifyCanExecuteChanged();
            PickInViewCommand.NotifyCanExecuteChanged();
            CreateCommand.NotifyCanExecuteChanged();
            ConfirmCreateCommand.NotifyCanExecuteChanged();
            UpdateStatus();
        }

        partial void OnCanCreateChanged(bool value) => CreateCommand.NotifyCanExecuteChanged();
        partial void OnIsConfirmOpenChanged(bool value) => CreateCommand.NotifyCanExecuteChanged();

        /// <summary>Negative or non-finite lengths are reset to 0 (which re-enters and re-plans).</summary>
        private void ClampOrRecompute(double value, Action<double> set)
        {
            if (!double.IsFinite(value) || value < 0)
                set(0);
            else
                Recompute();
        }

        // ── Settings ────────────────────────────────────────────────────

        /// <summary>Settings as currently shown in the window.</summary>
        public SectionsToSheetSettings CurrentSettings() => new()
        {
            TitleBlockName = SelectedTitleBlock?.Name ?? string.Empty,
            ViewportTypeName = SelectedViewportType?.Name ?? string.Empty,
            LastSheetNumber = _lastSheetNumber ?? string.Empty,
            SheetName = string.IsNullOrWhiteSpace(SheetName) ? "SECTIONS" : SheetName.Trim(),
            OpenSheetWhenDone = OpenSheetWhenDone,
            Mode = Mode,
            Order = Order,
            Vertical = Vertical,
            LastRow = LastRow,
            Overflow = Overflow,
            RemoveFromOldSheets = RemoveFromOldSheets,
            DeleteEmptySheets = DeleteEmptySheets,
            MarginTopMm = MarginTopMm,
            MarginBottomMm = MarginBottomMm,
            MarginLeftMm = MarginLeftMm,
            MarginRightMm = MarginRightMm,
            MinHorizontalGapMm = MinHorizontalGapMm,
            MinVerticalGapMm = MinVerticalGapMm,
            RowToleranceMm = RowToleranceMm
        };

        /// <summary>Writes the current settings to disk (window close, and after each successful run).</summary>
        public void SaveSettings() => SettingsService<SectionsToSheetSettings>.Save(SectionsToSheetCommand.SettingsFolder, CurrentSettings());

        // ── Reading and measuring ───────────────────────────────────────

        private long TitleBlockKey => SelectedTitleBlock?.Key ?? 0;

        private long ViewportTypeKey => SelectedViewportType?.Key ?? 0;

        private Dictionary<long, Footprint> CurrentFootprints()
            => _footprints.TryGetValue(ViewportTypeKey, out var d) ? d : new Dictionary<long, Footprint>();

        private Dictionary<long, string> CurrentProblems()
            => _problems.TryGetValue(ViewportTypeKey, out var d) ? d : new Dictionary<long, string>();

        private (LayoutRect? Frame, string Problem) CurrentFrame()
        {
            if (TitleBlockKey == 0)
                return (null, "The project has no title block. Load a title block family, then press Refresh.");
            return _frames.TryGetValue(TitleBlockKey, out var f) ? f : (null, "Measuring the title block…");
        }

        /// <summary>Takes a fresh read: lists, labels and the picked keys that still exist.</summary>
        private void LoadSnapshot(SourceSnapshot snapshot)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            SourceViewName = snapshot.SourceViewName;

            _syncingLists = true;
            try
            {
                long tb = TitleBlockKey, vp = ViewportTypeKey;
                SyncList(TitleBlocks, snapshot.TitleBlocks);
                SyncList(ViewportTypes, snapshot.ViewportTypes);
                SelectedTitleBlock = TitleBlocks.FirstOrDefault(t => t.Key == tb) ?? SelectedTitleBlock ?? TitleBlocks.FirstOrDefault();
                if (SelectedTitleBlock != null && !TitleBlocks.Contains(SelectedTitleBlock))
                    SelectedTitleBlock = TitleBlocks.FirstOrDefault();
                SelectedViewportType = ViewportTypes.FirstOrDefault(t => t.Key == vp)
                                       ?? ViewportTypes.FirstOrDefault(t => t.Key == snapshot.DefaultViewportTypeKey)
                                       ?? ViewportTypes.FirstOrDefault();
            }
            finally
            {
                _syncingLists = false;
            }

            _pickedKeys = _pickedKeys.Where(snapshot.Sections.ContainsKey).ToList();
            HasPicked = _pickedKeys.Count > 0;
            if (!HasPicked && SourceMode == SourceMode.Picked)
                SourceMode = SourceMode.View;

            ViewSourceLabel = $"All in view ({snapshot.InViewKeys.Count})";
            SelectionSourceLabel = $"Selection ({snapshot.SelectionKeys.Count})";
            PickedSourceLabel = $"Picked ({_pickedKeys.Count})";
            UpdateSourceNote();
        }

        private static void SyncList(ObservableCollection<NamedType> list, IReadOnlyList<NamedType> wanted)
        {
            if (list.SequenceEqual(wanted))
                return;
            list.Clear();
            foreach (var item in wanted)
                list.Add(item);
        }

        /// <summary>Keeps measured sizes; a key asked for but not returned gets a problem, so it is not asked for again.</summary>
        private void StoreMeasure(MeasureResult measure, IEnumerable<long> requested)
        {
            if (measure == null)
                return;
            if (measure.TitleBlockKey != 0)
                _frames[measure.TitleBlockKey] = (measure.Frame, measure.FrameProblem);

            if (!_footprints.TryGetValue(measure.ViewportTypeKey, out var footprints))
                _footprints[measure.ViewportTypeKey] = footprints = new Dictionary<long, Footprint>();
            if (!_problems.TryGetValue(measure.ViewportTypeKey, out var problems))
                _problems[measure.ViewportTypeKey] = problems = new Dictionary<long, string>();

            foreach (var kv in measure.Footprints)
            {
                footprints[kv.Key] = kv.Value;
                problems.Remove(kv.Key);
            }
            foreach (var kv in measure.Problems)
                problems[kv.Key] = kv.Value;

            foreach (long key in requested ?? Array.Empty<long>())
            {
                bool locked = _snapshot != null && _snapshot.Sections.TryGetValue(key, out var c) && c.LockReason != null;
                if (!locked && !footprints.ContainsKey(key) && !problems.ContainsKey(key))
                    problems[key] = "Could not be measured — press Refresh";
            }
        }

        /// <summary>Measures what the list needs and has not been measured for the chosen title block / viewport type.</summary>
        private void RequestMeasureIfNeeded()
        {
            if (!_ready || _snapshot == null)
                return;

            long tb = TitleBlockKey, vp = ViewportTypeKey;
            bool needFrame = tb != 0 && !_frames.ContainsKey(tb);
            var footprints = CurrentFootprints();
            var problems = CurrentProblems();
            var missing = Rows.Select(r => r.Data)
                .Where(d => d.LockReason == null && !footprints.ContainsKey(d.Key) && !problems.ContainsKey(d.Key))
                .Select(d => d.Key).ToList();
            if (!needFrame && missing.Count == 0)
                return;

            if (IsBusy)
            {
                _measurePending = true;
                return;
            }
            _measurePending = false;
            Run(done => _session.Measure(tb, vp, missing, done),
                "Measuring the views on a temporary sheet (nothing in the model changes)…",
                missing);
        }

        // ── Rows ────────────────────────────────────────────────────────

        /// <summary>Rebuilds the list for the chosen source, in placement order.</summary>
        private void RebuildRows()
        {
            if (!_ready || _snapshot == null)
                return;

            IEnumerable<long> keys = SourceMode switch
            {
                SourceMode.Selection => _snapshot.SelectionKeys,
                SourceMode.Picked => _pickedKeys,
                _ => _snapshot.InViewKeys
            };
            var candidates = keys.Where(_snapshot.Sections.ContainsKey).Select(k => _snapshot.Sections[k]).ToList();
            var footprints = CurrentFootprints();
            var problems = CurrentProblems();
            var ordered = SectionPlanner.Order(candidates, CurrentSettings(), _snapshot.SourceViewScale, footprints);

            Rows.Clear();
            foreach (var candidate in ordered)
            {
                if (!_rowsByKey.TryGetValue(candidate.Key, out var row))
                {
                    row = new SectionRowViewModel(candidate, !_unticked.Contains(candidate.Key), OnRowTickChanged);
                    _rowsByKey[candidate.Key] = row;
                }
                else
                {
                    row.SetTickSilently(!_unticked.Contains(candidate.Key));
                }

                footprints.TryGetValue(candidate.Key, out var fp);
                problems.TryGetValue(candidate.Key, out var problem);
                row.Update(candidate, fp, candidate.LockReason != null ? "not measured (locked)" : problem != null ? "can’t be measured" : null);
                Rows.Add(row);
            }
            UpdateListCount();
        }

        private bool MatchesSearch(SectionRowViewModel row)
            => string.IsNullOrWhiteSpace(SearchText)
               || row.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)
               || row.Chip.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);

        private void UpdateListCount()
        {
            int shown = Rows.Count(MatchesSearch);
            int ticked = Rows.Count(r => r.IsTicked);
            ListCountText = shown == Rows.Count ? $"{Rows.Count} in the list · {ticked} ticked" : $"{shown} of {Rows.Count} shown · {ticked} ticked";
        }

        private void OnRowTickChanged(SectionRowViewModel row)
        {
            if (row.IsTicked) _unticked.Remove(row.Key);
            else _unticked.Add(row.Key);
            Recompute();
        }

        private void SetTicks(bool value)
        {
            int changed = 0;
            foreach (var row in Rows.Where(MatchesSearch))
            {
                if (row.IsTicked == value) continue;
                row.SetTickSilently(value);
                if (value) _unticked.Remove(row.Key);
                else _unticked.Add(row.Key);
                changed++;
            }
            if (changed > 0)
                Recompute();
        }

        // ── Plan ────────────────────────────────────────────────────────

        /// <summary>Re-plans from the current list, sizes and settings, and refreshes every text.</summary>
        private void Recompute()
        {
            if (!_ready || _snapshot == null)
                return;

            var frame = CurrentFrame();
            var ticked = Rows.Where(r => r.IsTicked).Select(r => r.Data).ToList();
            Plan = SectionPlanner.Build(new PlanRequest
            {
                Sections = ticked,
                Footprints = CurrentFootprints(),
                MeasureProblems = CurrentProblems(),
                TitleBlockFrame = frame.Frame,
                FrameProblem = frame.Problem,
                Sheets = _snapshot.Sheets,
                ExistingSheetNumbers = _snapshot.ExistingSheetNumbers,
                SourceViewScale = _snapshot.SourceViewScale,
                FirstSheetNumber = SheetNumber,
                SheetName = SheetName,
                Settings = CurrentSettings()
            });

            // Row status lines.
            var placed = Plan.Viewports.ToDictionary(v => v.Section.Key);
            var skipped = new Dictionary<long, string>();
            foreach (var s in Plan.Skipped)
                skipped[s.Section.Key] = s.Reason;
            foreach (var row in Rows)
            {
                if (!row.IsTicked)
                    row.SetStatus(null, RowStatusKind.None);
                else if (placed.TryGetValue(row.Key, out var v))
                    row.SetStatus((v.IsMove ? $"{v.Section.CurrentSheetNumber} → " : "→ ") + $"{Plan.Sheets[v.Sheet].Number} · detail {v.DetailNumber}",
                        v.IsMove ? RowStatusKind.Moved : RowStatusKind.Placed);
                else if (skipped.TryGetValue(row.Key, out var reason))
                    row.SetStatus(reason == NotMeasured ? "Measuring…" : reason,
                        reason == NotMeasured || reason.StartsWith("Already on", StringComparison.Ordinal) ? RowStatusKind.Warning : RowStatusKind.Problem);
                else
                    row.SetStatus(null, RowStatusKind.None);
            }

            // Metrics.
            PickedText = $"{ticked.Count} of {Rows.Count}";
            MovingCount = Plan.Moves.Count;
            CantPlaceCount = Plan.Skipped.Count(s => s.Reason != NotMeasured);
            SheetCount = Plan.Sheets.Count;

            // Tabs.
            int page = Math.Max(0, Math.Min(CurrentPage, Plan.Sheets.Count - 1));
            var labels = Plan.Sheets.Select(s => s.Number).ToList();
            if (!PageTabs.Select(t => t.Label).SequenceEqual(labels))
            {
                PageTabs.Clear();
                for (int i = 0; i < labels.Count; i++)
                    PageTabs.Add(new PageTabViewModel(i, labels[i], i == page, index => CurrentPage = index));
            }
            HasTabs = Plan.Sheets.Count > 1;
            if (CurrentPage != page)
                CurrentPage = page; // raises PlanChanged and the preview texts
            UpdatePreviewTexts();

            // Already on a sheet.
            var onSheets = ticked.Where(c => c.IsOnSheet && c.LockReason == null).GroupBy(c => c.CurrentSheetNumber).ToList();
            int onSheetCount = onSheets.Sum(g => g.Count());
            PlacedSummary = onSheetCount == 0
                ? "None of the ticked sections is on a sheet yet."
                : $"{onSheetCount} ticked section{(onSheetCount == 1 ? " is" : "s are")} already on " +
                  string.Join(", ", onSheets.Select(g => $"{g.Key} ({g.Count()})")) + ".";
            EmptySheetsText = Plan.SheetsLeftEmpty.Count > 0
                ? "Left empty after the move: " + string.Join(", ", Plan.SheetsLeftEmpty.Select(s => s.Number)) + "."
                : "No sheet will be left empty.";
            var locked = ticked.Where(c => c.LockReason != null).ToList();
            HasLocked = locked.Count > 0;
            LockedText = string.Join(Environment.NewLine, locked.Select(c => $"Can’t move {c.Name}: {c.LockReason}. It will be skipped."));

            // Banners.
            BlockerText = Plan.Blocker ?? string.Empty;
            HasBlocker = Plan.Blocker != null;
            InfoText = Plan.Sheets.Count > 1
                ? $"Doesn’t fit on one sheet: continues on {string.Join(", ", Plan.Sheets.Skip(1).Select(s => s.Number))}. Use the tabs above the preview to check each sheet."
                : string.Empty;
            HasInfo = InfoText.Length > 0;
            WarningsText = string.Join(Environment.NewLine, Plan.Warnings);
            HasWarnings = Plan.Warnings.Count > 0;

            CanCreate = Plan.CanApply;
            UpdateListCount();
            UpdateStatus();
            PlanChanged?.Invoke(this, EventArgs.Empty);

            RequestMeasureIfNeeded();
        }

        private void UpdatePreviewTexts()
        {
            var sheet = CurrentSheet;
            string titleBlock = SelectedTitleBlock?.Name ?? "no title block";
            PreviewTitle = sheet != null ? $"{sheet.Number} · {titleBlock}" : titleBlock;
            FillText = sheet == null ? "—" : $"{sheet.FillPercent:0}%";
            LayoutText = sheet == null ? "—"
                : Mode == ArrangeMode.Grid ? $"{Plan.Columns} column(s) × {sheet.RowCount} row(s)"
                : $"{sheet.RowCount} row(s)";
            HorizontalGapText = sheet?.HorizontalGap is double h ? $"{h * SectionPlanner.MmPerFoot:0} mm (min {MinHorizontalGapMm:0})" : "—";
            VerticalGapText = sheet?.VerticalGap is double v ? $"{v * SectionPlanner.MmPerFoot:0} mm" : "—";

            var scales = (Plan?.Viewports ?? Enumerable.Empty<PlannedViewport>())
                .GroupBy(x => x.Section.Scale).OrderBy(g => g.Key == 0 ? int.MaxValue : g.Key).ToList();
            ScalesText = scales.Count == 0 ? "—" : string.Join(", ", scales.Select(g => $"{(g.Key > 0 ? "1:" + g.Key : "?")} ×{g.Count()}"));
            HasMixedScales = scales.Count > 1;
        }

        private void UpdateNotes()
        {
            IsRowsMode = Mode == ArrangeMode.Rows;
            IsPlanOrder = Order == SectionOrder.Plan;
            ModeNote = Mode == ArrangeMode.Grid
                ? "Views sit in columns that line up from row to row; each column is as wide as its widest view. The columns are spread to the full width."
                : "Views flow left to right and wrap like text; each row is spread to the full width.";
            OrderNote = Order switch
            {
                SectionOrder.Number => "By view name, numbers as numbers (Section 2 before Section 10).",
                SectionOrder.Scale => "Grouped by scale (1:20, then 1:50 …), plan order inside each group.",
                SectionOrder.Size => "Largest first: tighter, but the plan order is lost.",
                _ => "Same order as the markers in the view: left to right, then the next row down."
            };
        }

        private void UpdateSourceNote()
        {
            SourceNote = SourceMode switch
            {
                SourceMode.Selection => "Section markers (or section views in the Project Browser) selected in Revit. Press Refresh after changing the selection.",
                SourceMode.Picked => "The markers you picked in the view.",
                _ => "Every section marker shown in the view. Untick the ones you don’t want."
            };
        }

        private void UpdateStatus()
        {
            if (Plan == null)
                return;
            if (IsBusy)
            {
                StatusText = _busyText;
                return;
            }

            int cant = CantPlaceCount;
            StatusText = Plan.Blocker
                ?? $"{Plan.PlacedCount} section(s) ready for {string.Join(", ", Plan.Sheets.Select(s => s.Number))}"
                   + (MovingCount > 0 ? $" · {MovingCount} come off {string.Join(", ", Plan.Moves.Select(m => m.Section.CurrentSheetNumber).Distinct())}" : string.Empty)
                   + (cant > 0 ? $" · {cant} can’t be placed (see the list)" : string.Empty);
        }

        // ── Commands ────────────────────────────────────────────────────

        /// <summary>Re-reads the model and the Revit selection, and measures again.</summary>
        [RelayCommand(CanExecute = nameof(IsIdle))]
        private void Refresh()
        {
            _footprints.Clear();
            _problems.Clear();
            _frames.Clear();
            Run(done => _session.Refresh(_pickedKeys, done), "Reading the model…", null);
        }

        /// <summary>Lets the user pick section markers in the view (the window steps aside meanwhile).</summary>
        [RelayCommand(CanExecute = nameof(IsIdle))]
        private void PickInView()
        {
            IsPicking = true;
            Run(done => _session.Pick(_pickedKeys, done),
                "Pick section markers in the view, then click Finish on the Options Bar (Esc cancels).", null);
        }

        /// <summary>Ticks every shown row.</summary>
        [RelayCommand]
        private void TickAll() => SetTicks(true);

        /// <summary>Unticks every shown row.</summary>
        [RelayCommand]
        private void UntickAll() => SetTicks(false);

        /// <summary>Asks for confirmation when sections come off other sheets, then creates the sheet(s).</summary>
        [RelayCommand(CanExecute = nameof(CanRunCreate))]
        private void Create()
        {
            Recompute();
            if (!Plan.CanApply)
                return;

            if (Plan.Moves.Count == 0)
            {
                DoApply();
                return;
            }

            ConfirmMoves.Clear();
            foreach (var move in Plan.Moves)
                ConfirmMoves.Add(new MoveLine(move.Section.Name, move.Section.CurrentSheetNumber, Plan.Sheets[move.Sheet].Number));
            ConfirmTitle = $"Move {Plan.Moves.Count} section{(Plan.Moves.Count == 1 ? "" : "s")} to the new sheet?";
            var locked = Plan.Skipped.Where(s => s.Section.LockReason != null).ToList();
            HasConfirmSkip = locked.Count > 0;
            ConfirmSkipText = string.Join(Environment.NewLine, locked.Select(s => $"Skipped: {s.Section.Name} — {s.Section.LockReason}."));
            ConfirmEmptyText = Plan.SheetsLeftEmpty.Count > 0
                ? "Also delete the sheets left empty: " + string.Join(", ", Plan.SheetsLeftEmpty.Select(s => s.Number))
                : "Also delete sheets left empty (none will be)";
            IsConfirmOpen = true;
        }

        private bool CanRunCreate() => CanCreate && !IsBusy && !IsConfirmOpen;

        /// <summary>Confirm dialog: go ahead.</summary>
        [RelayCommand(CanExecute = nameof(IsIdle))]
        private void ConfirmCreate()
        {
            IsConfirmOpen = false;
            Recompute();
            if (Plan.CanApply)
                DoApply();
        }

        /// <summary>Confirm dialog: back to the window, nothing changed.</summary>
        [RelayCommand]
        private void CancelConfirm() => IsConfirmOpen = false;

        /// <summary>Copies every log line to the clipboard.</summary>
        [RelayCommand]
        private void CopyAllLogs() => LogClipboardService.CopyAll(LogEntries);

        private void DoApply()
        {
            var ticked = Rows.Where(r => r.IsTicked).Select(r => r.Key).ToList();
            var request = new ApplyRequest
            {
                TickedKeys = ticked,
                KeepKeys = _pickedKeys.ToList(),
                Settings = CurrentSettings(),
                FirstSheetNumber = SheetNumber ?? string.Empty,
                SheetName = SheetName ?? string.Empty,
                TitleBlockKey = TitleBlockKey,
                ViewportTypeKey = ViewportTypeKey,
                OpenSheet = OpenSheetWhenDone,
                Signature = Plan.Signature
            };
            Run(done => _session.Apply(request, done), "Creating the sheet and placing the views…", ticked);
        }

        /// <summary>Sends one request to Revit and shows the busy state until it comes back.</summary>
        private void Run(Func<Action<SessionResult>, bool> request, string busyText, IReadOnlyCollection<long> measuredKeys)
        {
            _busyText = busyText;
            IsBusy = true;
            bool accepted = request(result =>
            {
                IsBusy = false;
                IsPicking = false;
                Handle(result, measuredKeys);
                if (_measurePending && !IsBusy)
                {
                    _measurePending = false;
                    RequestMeasureIfNeeded();
                }
            });

            if (!accepted)
            {
                IsBusy = false;
                IsPicking = false;
                Log(LogLevel.Error, "Revit did not accept the request. Finish any active command (press Esc in Revit) and try again.");
            }
        }

        private void Handle(SessionResult result, IReadOnlyCollection<long> measuredKeys)
        {
            if (result == null)
                return;
            if (result.Message != null)
                Log(result.Level, result.Message);
            foreach (var line in result.Details ?? new List<LogEntry>())
                LogEntries.Add(line);

            if (result.Snapshot != null)
                LoadSnapshot(result.Snapshot);
            if (result.Measure != null)
                StoreMeasure(result.Measure, measuredKeys);

            if (result.PickedKeys != null && result.PickedKeys.Count > 0)
            {
                _pickedKeys = result.PickedKeys.ToList();
                foreach (long key in _pickedKeys)
                    _unticked.Remove(key);
                HasPicked = true;
                PickedSourceLabel = $"Picked ({_pickedKeys.Count})";
                _ready = false; // one rebuild below, not two
                SourceMode = SourceMode.Picked;
                _ready = true;
            }

            if (result.Applied)
            {
                _lastSheetNumber = result.LastSheetNumber;
                SheetNumber = SheetNumbering.SuggestAfter(_lastSheetNumber, _snapshot.ExistingSheetNumbers);
                SaveSettings();
            }

            RebuildRows();
            Recompute();
        }

        private void Log(LogLevel level, string message) => LogEntries.Add(new LogEntry(level, message));

        /// <summary>Releases the Revit session.</summary>
        public void Dispose() => _session.Dispose();
    }
}
