using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Commands;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Models;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Services;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Infrastructure;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.UI.ViewModels
{
    /// <summary>Which rows the grid shows.</summary>
    public enum RowFilter
    {
        /// <summary>Every row.</summary>
        All = 0,

        /// <summary>Sheet 1 only.</summary>
        Sheet1 = 1,

        /// <summary>Sheet 2 only.</summary>
        Sheet2 = 2
    }

    /// <summary>Raised when a Move Views run has finished.</summary>
    public sealed class RunFinishedEventArgs : EventArgs
    {
        /// <summary>Creates the event data.</summary>
        public RunFinishedEventArgs(string message, bool hasProblems)
        {
            Message = message;
            HasProblems = hasProblems;
        }

        /// <summary>Text for the result dialog.</summary>
        public string Message { get; }

        /// <summary>True when something was skipped or the run failed.</summary>
        public bool HasProblems { get; }
    }

    /// <summary>
    /// Window state for Sheet Views Combine: the two sheets, the grouped grid of their views, the
    /// options, and a <see cref="CombinePlan"/> rebuilt in memory on every change so the preview is
    /// live. All Revit access goes through <see cref="ICombineSession"/>.
    /// </summary>
    public sealed partial class SheetViewsCombineViewModel : ObservableObject, IDisposable
    {
        private readonly ICombineSession _session;
        private readonly ListCollectionView _rowsView;
        private readonly HashSet<long> _ticked = new();
        private readonly Dictionary<long, (SheetRect? Frame, string Problem)> _titleBlockFrames = new();
        private readonly HashSet<long> _measuring = new();
        private readonly HashSet<long> _pendingLoads = new();
        private bool _measurePending;
        private int _busyCount;
        private SheetContent _sheet1;
        private SheetContent _sheet2;
        private SheetContent _other;
        private ProjectCatalog _catalog;
        private bool _ready;
        private bool _suppress;

        /// <summary>Raised whenever <see cref="Plan"/> is rebuilt (the window redraws the preview).</summary>
        public event EventHandler PlanChanged;

        /// <summary>Raised when a run has finished (the window shows the result dialog).</summary>
        public event EventHandler<RunFinishedEventArgs> RunFinished;

        /// <summary>Creates the view model over <paramref name="session"/> with the saved <paramref name="settings"/>.</summary>
        public SheetViewsCombineViewModel(ICombineSession session, SheetViewsCombineSettings settings)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _rowsView = new ListCollectionView(Rows) { Filter = o => o is ViewRowViewModel r && MatchesFilter(r) };
            _rowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ViewRowViewModel.SheetGroupKey)));
            _rowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ViewRowViewModel.TypeGroupKey)));
            _rowsView.SortDescriptions.Add(new SortDescription(nameof(ViewRowViewModel.SheetGroupKey), ListSortDirection.Ascending));

            var s = (settings ?? new SheetViewsCombineSettings()).Normalize();
            _suppress = true;
            // Fields, not properties: the change hooks must not run (or call Revit) before the window is ready.
#pragma warning disable MVVMTK0034
            target = s.Target == TargetMode.OtherSheet ? TargetMode.Sheet1 : s.Target;
            unticked = s.Unticked;
            onFailure = s.OnFailure;
            strategy = s.Strategy;
            order = s.Order;
            keepViewportType = s.KeepViewportType;
            keepDetailNumbers = s.KeepDetailNumbers;
            openSheetWhenDone = s.OpenSheetWhenDone;
            newSheetName = s.NewSheetName;
            marginTopMm = s.MarginTopMm;
            marginBottomMm = s.MarginBottomMm;
            marginLeftMm = s.MarginLeftMm;
            marginRightMm = s.MarginRightMm;
            horizontalGapMm = s.HorizontalGapMm;
            verticalGapMm = s.VerticalGapMm;

            LoadCatalog(session.InitialCatalog);
            if (session.InitialActiveSheet != null)
            {
                _sheet1 = session.InitialActiveSheet;
                sheet1Choice = Sheets.FirstOrDefault(x => x.Key == _sheet1.Key);
                Sheet1Note = "Defaults to the sheet open in Revit. Change it here to use any other sheet.";
            }
            else
            {
                Sheet1Note = "The active view is not a sheet, so pick Sheet 1 here.";
            }
            selectedTitleBlock = TitleBlocks.FirstOrDefault(t => t.Key == _sheet1?.TitleBlockTypeKey) ?? TitleBlocks.FirstOrDefault();
            newSheetNumber = SuggestNewNumber();
#pragma warning restore MVVMTK0034
            _suppress = false;

            _ready = true;
            RebuildRows();
            Recompute();
            UpdateOtherSheets();
            if (Target == TargetMode.NewSheet)
                EnsureTitleBlockMeasured();

            Log(LogLevel.Info, $"{Sheets.Count} sheet(s) in the project."
                + (_sheet1 != null ? $" Sheet 1: {_sheet1.Label} (active sheet), {_sheet1.Views.Count} viewport(s)." : " The active view is not a sheet."));
            if (TitleBlocks.Count == 0)
                Log(LogLevel.Warning, "The project has no title block, so a new target sheet cannot be made.");
        }

        // ── Collections ─────────────────────────────────────────────────

        /// <summary>Every sheet in the project.</summary>
        public ObservableCollection<SheetSummary> Sheets { get; } = new();

        /// <summary>Sheets other than Sheet 1 and Sheet 2 (target picker).</summary>
        public ObservableCollection<SheetSummary> OtherSheets { get; } = new();

        /// <summary>Title block types.</summary>
        public ObservableCollection<NamedType> TitleBlocks { get; } = new();

        /// <summary>Views on the sheets, as rows.</summary>
        public ObservableCollection<ViewRowViewModel> Rows { get; } = new();

        /// <summary><see cref="Rows"/> filtered, grouped by sheet, then view type.</summary>
        public ICollectionView RowsView => _rowsView;

        /// <summary>The Confirm &amp; Move plan.</summary>
        public ObservableCollection<PlanLine> PlanLines { get; } = new();

        /// <summary>The Move Complete report.</summary>
        public ObservableCollection<ResultLine> ResultLines { get; } = new();

        /// <summary>One tab per sheet in the plan.</summary>
        public ObservableCollection<PageTabViewModel> PageTabs { get; } = new();

        /// <summary>Activity log.</summary>
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        /// <summary>Current plan; never null after construction.</summary>
        public CombinePlan Plan { get; private set; }

        /// <summary>The planned sheet shown in the preview, or null.</summary>
        public PlannedSheet CurrentSheet => Plan?.Sheets.ElementAtOrDefault(CurrentPage);

        // ── Stage 1: sheets ─────────────────────────────────────────────

        [ObservableProperty] private SheetSummary sheet1Choice;
        [ObservableProperty] private SheetSummary sheet2Choice;
        [ObservableProperty] private string sheet1Note = string.Empty;
        [ObservableProperty] private string sheetsSummary = "Pick Sheet 2.";

        // ── Stage 2: target and options ────────────────────────────────

        [ObservableProperty] private RowFilter filter;
        [ObservableProperty] private TargetMode target;
        [ObservableProperty] private SheetSummary otherTargetChoice;
        [ObservableProperty] private string newSheetNumber = string.Empty;
        [ObservableProperty] private string newSheetName = string.Empty;
        [ObservableProperty] private NamedType selectedTitleBlock;
        [ObservableProperty] private UntickedTargetMode unticked;
        [ObservableProperty] private FailureMode onFailure;
        [ObservableProperty] private bool keepViewportType;
        [ObservableProperty] private bool keepDetailNumbers;
        [ObservableProperty] private string sheet1TargetLabel = "Sheet 1";
        [ObservableProperty] private string sheet2TargetLabel = "Sheet 2";
        [ObservableProperty] private string tickCountText = string.Empty;

        // ── Stage 3: layout ─────────────────────────────────────────────

        [ObservableProperty] private PackStrategy strategy;
        [ObservableProperty] private GroupOrder order;
        [ObservableProperty] private double marginTopMm;
        [ObservableProperty] private double marginBottomMm;
        [ObservableProperty] private double marginLeftMm;
        [ObservableProperty] private double marginRightMm;
        [ObservableProperty] private double horizontalGapMm;
        [ObservableProperty] private double verticalGapMm;
        [ObservableProperty] private int currentPage;
        [ObservableProperty] private string previewTitle = string.Empty;
        [ObservableProperty] private string fitSummary = string.Empty;

        // ── Stage 4/5 ───────────────────────────────────────────────────

        [ObservableProperty] private bool openSheetWhenDone;
        [ObservableProperty] private string resultSummary = "Nothing moved yet.";
        [ObservableProperty] private long lastTargetKey;
        [ObservableProperty] private string failureModeText = string.Empty;

        // ── Banners and metrics ─────────────────────────────────────────

        [ObservableProperty] private string blockerText = string.Empty;
        [ObservableProperty] private string warningsText = string.Empty;
        [ObservableProperty] private int sheet1Count;
        [ObservableProperty] private int sheet2Count;
        [ObservableProperty] private int selectedCount;
        [ObservableProperty] private int moveCount;
        [ObservableProperty] private int copyCount;
        [ObservableProperty] private int skipCount;
        [ObservableProperty] private int repackCount;
        [ObservableProperty] private int sheetCount;

        // ── Stages ──────────────────────────────────────────────────────

        [ObservableProperty] private bool isStage1Open = true;
        [ObservableProperty] private bool isStage2Open;
        [ObservableProperty] private bool isStage3Open;
        [ObservableProperty] private bool isStage4Open;
        [ObservableProperty] private bool isStage5Open;

        // ── Busy ────────────────────────────────────────────────────────

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(MoveViewsCommand))]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
        [NotifyCanExecuteChangedFor(nameof(OpenTargetSheetCommand))]
        private bool isBusy;

        [ObservableProperty] private string statusText = "Ready.";

        /// <summary>True when the plan blocks the run.</summary>
        public bool HasBlocker => !string.IsNullOrEmpty(BlockerText);

        /// <summary>True when there are warnings.</summary>
        public bool HasWarnings => !string.IsNullOrEmpty(WarningsText);

        /// <summary>True when the plan has more than one sheet.</summary>
        public bool HasTabs => PageTabs.Count > 1;

        /// <summary>Both sheets are loaded.</summary>
        public bool SheetsReady => _sheet1 != null && _sheet2 != null;

        /// <summary>The new-sheet fields are shown.</summary>
        public bool IsNewSheetTarget => Target == TargetMode.NewSheet;

        /// <summary>The other-sheet picker is shown.</summary>
        public bool IsOtherSheetTarget => Target == TargetMode.OtherSheet;

        /// <summary>There is a report to show.</summary>
        public bool HasResults => ResultLines.Count > 0;

        // ── Property hooks ──────────────────────────────────────────────

        partial void OnSheet1ChoiceChanged(SheetSummary value) => SheetChoiceChanged(1, value);

        partial void OnSheet2ChoiceChanged(SheetSummary value) => SheetChoiceChanged(2, value);

        partial void OnOtherTargetChoiceChanged(SheetSummary value)
        {
            if (_suppress) return;
            _other = null;
            if (value != null)
                Load(new[] { value.Key });
            RebuildRows();
            Recompute();
        }

        partial void OnFilterChanged(RowFilter value) => _rowsView.Refresh();

        partial void OnTargetChanged(TargetMode value)
        {
            OnPropertyChanged(nameof(IsNewSheetTarget));
            OnPropertyChanged(nameof(IsOtherSheetTarget));
            if (value == TargetMode.NewSheet)
                EnsureTitleBlockMeasured();
            if (value != TargetMode.OtherSheet && _other != null)
            {
                _other = null;
                RebuildRows();
            }
            else if (value == TargetMode.OtherSheet && OtherTargetChoice != null && _other == null)
            {
                Load(new[] { OtherTargetChoice.Key });
            }
            Recompute();
        }

        partial void OnSelectedTitleBlockChanged(NamedType value)
        {
            if (Target == TargetMode.NewSheet) EnsureTitleBlockMeasured();
            Recompute();
        }

        partial void OnNewSheetNumberChanged(string value) => Recompute();
        partial void OnNewSheetNameChanged(string value) => Recompute();
        partial void OnUntickedChanged(UntickedTargetMode value) => Recompute();
        partial void OnOnFailureChanged(FailureMode value) => Recompute();
        partial void OnKeepViewportTypeChanged(bool value) => Recompute();
        partial void OnKeepDetailNumbersChanged(bool value) => Recompute();
        partial void OnStrategyChanged(PackStrategy value) => Recompute();
        partial void OnOrderChanged(GroupOrder value) => Recompute();
        partial void OnMarginTopMmChanged(double value) => Recompute();
        partial void OnMarginBottomMmChanged(double value) => Recompute();
        partial void OnMarginLeftMmChanged(double value) => Recompute();
        partial void OnMarginRightMmChanged(double value) => Recompute();
        partial void OnHorizontalGapMmChanged(double value) => Recompute();
        partial void OnVerticalGapMmChanged(double value) => Recompute();
        partial void OnBlockerTextChanged(string value) => OnPropertyChanged(nameof(HasBlocker));
        partial void OnWarningsTextChanged(string value) => OnPropertyChanged(nameof(HasWarnings));

        partial void OnCurrentPageChanged(int value)
        {
            foreach (var tab in PageTabs)
                tab.IsCurrent = tab.Index == value;
            UpdatePreviewTitle();
            PlanChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Commands ────────────────────────────────────────────────────

        [RelayCommand] private void ShowAll() => Filter = RowFilter.All;
        [RelayCommand] private void ShowSheet1() => Filter = RowFilter.Sheet1;
        [RelayCommand] private void ShowSheet2() => Filter = RowFilter.Sheet2;

        /// <summary>Ticks every row the filter shows.</summary>
        [RelayCommand]
        private void SelectAll() => SetTicks(Rows.Where(MatchesFilter), true);

        /// <summary>Unticks every row the filter shows.</summary>
        [RelayCommand]
        private void ClearSelection() => SetTicks(Rows.Where(MatchesFilter), false);

        /// <summary>Ticks every view in one sheet group (parameter: the group key).</summary>
        [RelayCommand]
        private void TickGroup(object key) => SetTicks(Rows.Where(r => r.SheetGroupKey == key as string || r.TypeGroupKey == key as string), true);

        /// <summary>Unticks every view in one sheet group (parameter: the group key).</summary>
        [RelayCommand]
        private void UntickGroup(object key) => SetTicks(Rows.Where(r => r.SheetGroupKey == key as string || r.TypeGroupKey == key as string), false);

        /// <summary>Re-reads the sheets list and the chosen sheets.</summary>
        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private void Refresh()
        {
            var keys = new[] { Sheet1Choice?.Key ?? 0, Sheet2Choice?.Key ?? 0, Target == TargetMode.OtherSheet ? OtherTargetChoice?.Key ?? 0 : 0 }
                .Where(k => k != 0).Distinct().ToList();
            _titleBlockFrames.Clear();
            Begin("Reading the sheets…");
            if (!_session.Refresh(keys, r =>
                {
                    End();
                    if (r.Catalog != null) LoadCatalog(r.Catalog);
                    StoreSheets(r.Sheets);
                    LogResult(r);
                    if (Target == TargetMode.NewSheet) EnsureTitleBlockMeasured();
                    if (r.Level != LogLevel.Error) Log(LogLevel.Info, "Refreshed.");
                }))
                NotQueued();
        }

        private bool CanRefresh() => !IsBusy;

        [RelayCommand] private void GoToStage1() => OpenStage(1);
        [RelayCommand] private void GoToStage2() => OpenStage(2);
        [RelayCommand] private void GoToStage3() => OpenStage(3);
        [RelayCommand] private void GoToStage4() => OpenStage(4);

        /// <summary>Moves the views as planned (one Undo step).</summary>
        [RelayCommand(CanExecute = nameof(CanMoveViews))]
        private void MoveViews()
        {
            if (Plan == null || !Plan.CanApply)
            {
                Log(LogLevel.Warning, "Nothing was changed: " + (Plan?.Blocker ?? "nothing to do."));
                return;
            }

            var settings = CurrentSettings();
            var request = new ApplyRequest
            {
                Sheet1Key = _sheet1.Key,
                Sheet2Key = _sheet2.Key,
                OtherTargetKey = _other?.Key ?? 0,
                Ticked = _ticked.ToList(),
                Settings = settings,
                NewSheetNumber = NewSheetNumber,
                NewSheetName = NewSheetName,
                NewTitleBlockKey = SelectedTitleBlock?.Key ?? 0,
                Signature = Plan.Signature
            };
            bool open = OpenSheetWhenDone;
            Begin("Moving views…");
            Log(LogLevel.Info, $"Moving: {Plan.MoveCount} to move, {Plan.RepackCount} to re-pack, {Plan.CopyCount} legend(s) to copy"
                + (Plan.SkipCount > 0 ? $", {Plan.SkipCount} skipped" : string.Empty) + $" → {Plan.Target?.Number}.");
            if (!_session.Apply(request, r => OnApplied(r, open)))
                NotQueued();
        }

        private bool CanMoveViews() => !IsBusy;

        /// <summary>Opens the target sheet of the last run.</summary>
        [RelayCommand(CanExecute = nameof(CanOpenTarget))]
        private void OpenTargetSheet()
        {
            if (LastTargetKey == 0) return;
            Begin("Opening the sheet…");
            if (!_session.OpenSheet(LastTargetKey, r => { End(); LogResult(r); }))
                NotQueued();
        }

        private bool CanOpenTarget() => !IsBusy;

        /// <summary>Copies the Move Complete report.</summary>
        [RelayCommand]
        private void CopyReport()
        {
            string text = string.Join(Environment.NewLine, ResultLines.Select(l => $"{l.Name}\t{l.Outcome}\t{l.Detail}"));
            if (text.Length == 0) return;
            try { Clipboard.SetText(text); Log(LogLevel.Info, "Report copied."); }
            catch (System.Runtime.InteropServices.ExternalException) { Log(LogLevel.Warning, "The clipboard is busy. Try again."); }
        }

        /// <summary>Copies the whole log.</summary>
        [RelayCommand]
        private void CopyAllLogs() => LogClipboardService.CopyAll(LogEntries);

        /// <summary>Saves the log to a text file.</summary>
        [RelayCommand]
        private void ExportLogs()
        {
            string path = LogExportService.ExportWithPicker(LogEntries, OwnerWindow, ToolCatalog.SheetViewsCombine.Name);
            if (!string.IsNullOrEmpty(path)) Log(LogLevel.Info, "Log saved to " + path);
        }

        /// <summary>The tool window (set by the window), used as the owner of the save dialog.</summary>
        public Window OwnerWindow { get; set; }

        // ── Loading ─────────────────────────────────────────────────────

        private void SheetChoiceChanged(int slot, SheetSummary value)
        {
            if (_suppress) return;
            if (slot == 1) _sheet1 = null; else _sheet2 = null;
            UpdateOtherSheets();
            if (value != null)
            {
                var other = slot == 1 ? Sheet2Choice : Sheet1Choice;
                if (other != null && other.Key == value.Key)
                    Log(LogLevel.Warning, "Sheet 1 and Sheet 2 must be different sheets.");
                else
                    Load(new[] { value.Key });
            }
            OnPropertyChanged(nameof(SheetsReady));
            RebuildRows();
            Recompute();
        }

        private void Load(IReadOnlyCollection<long> keys)
        {
            if (IsBusy)
            {
                // Runs as soon as the current request has finished.
                _pendingLoads.UnionWith(keys);
                return;
            }
            Begin("Reading the sheet…");
            if (!_session.LoadSheets(keys, r =>
                {
                    End();
                    StoreSheets(r.Sheets);
                    LogResult(r);
                }))
                NotQueued();
        }

        private void StoreSheets(Dictionary<long, SheetContent> sheets)
        {
            if (sheets == null || sheets.Count == 0)
                return;
            foreach (var pair in sheets)
            {
                var content = pair.Value;
                if (Sheet1Choice?.Key == pair.Key) _sheet1 = content;
                if (Sheet2Choice?.Key == pair.Key) _sheet2 = content;
                if (OtherTargetChoice?.Key == pair.Key) _other = content;
                if (content == null)
                    Log(LogLevel.Warning, "A chosen sheet is no longer in the model. Pick it again.");
                else
                    Log(LogLevel.Info, $"Read {content.Label}: {content.Views.Count} viewport(s)"
                        + (content.Views.Count(v => v.LockReason != null) is int locked && locked > 0 ? $", {locked} checked out by others" : string.Empty)
                        + (content.Frame == null ? " — no title block" : string.Empty) + ".");
            }
            OnPropertyChanged(nameof(SheetsReady));
            RebuildRows();
            Recompute();
        }

        private void LoadCatalog(ProjectCatalog catalog)
        {
            _catalog = catalog;
            bool was = _suppress;
            _suppress = true;
            long s1 = Sheet1Choice?.Key ?? 0, s2 = Sheet2Choice?.Key ?? 0, ot = OtherTargetChoice?.Key ?? 0, tb = SelectedTitleBlock?.Key ?? 0;
            Sheets.Clear();
            foreach (var s in catalog.Sheets) Sheets.Add(s);
            TitleBlocks.Clear();
            foreach (var t in catalog.TitleBlocks) TitleBlocks.Add(t);
            Sheet1Choice = Sheets.FirstOrDefault(x => x.Key == s1);
            Sheet2Choice = Sheets.FirstOrDefault(x => x.Key == s2);
            SelectedTitleBlock = TitleBlocks.FirstOrDefault(x => x.Key == tb) ?? TitleBlocks.FirstOrDefault();
            UpdateOtherSheets();
            OtherTargetChoice = OtherSheets.FirstOrDefault(x => x.Key == ot);
            _suppress = was;
        }

        private void UpdateOtherSheets()
        {
            bool was = _suppress;
            _suppress = true;
            long keep = OtherTargetChoice?.Key ?? 0;
            OtherSheets.Clear();
            foreach (var s in Sheets.Where(s => s.Key != (Sheet1Choice?.Key ?? -1) && s.Key != (Sheet2Choice?.Key ?? -1)))
                OtherSheets.Add(s);
            OtherTargetChoice = OtherSheets.FirstOrDefault(s => s.Key == keep);
            if (OtherTargetChoice == null) _other = null;
            _suppress = was;
        }

        private void EnsureTitleBlockMeasured()
        {
            if (!_ready) return;
            long key = SelectedTitleBlock?.Key ?? 0;
            if (key == 0 || _titleBlockFrames.ContainsKey(key) || _measuring.Contains(key))
                return;
            if (IsBusy)
            {
                _measurePending = true;
                return;
            }
            _measuring.Add(key);
            Begin("Measuring the title block…");
            if (!_session.MeasureTitleBlock(key, r =>
                {
                    End();
                    _measuring.Remove(key);
                    if (r.Level == LogLevel.Error) LogResult(r);
                    _titleBlockFrames[key] = (r.TitleBlockFrame, r.TitleBlockProblem ?? r.Message);
                    Recompute();
                }))
            {
                _measuring.Remove(key);
                NotQueued();
            }
        }

        // ── Rows and plan ───────────────────────────────────────────────

        private void RebuildRows()
        {
            if (!_ready) return;
            Rows.Clear();
            var seen = new HashSet<long>();
            void Add(SheetContent sheet, int slot)
            {
                if (sheet == null) return;
                foreach (var v in sheet.Views.Where(v => seen.Add(v.ViewportKey)))
                    Rows.Add(new ViewRowViewModel(v, sheet, slot, _ticked.Contains(v.ViewportKey), OnRowTicked));
            }
            Add(_sheet1, 1);
            Add(_sheet2, 2);
            if (Target == TargetMode.OtherSheet) Add(_other, 3);

            // Forget ticks of views no longer listed.
            _ticked.IntersectWith(Rows.Select(r => r.Key));
            Sheet1Count = _sheet1?.Views.Count ?? 0;
            Sheet2Count = _sheet2?.Views.Count ?? 0;
            Sheet1TargetLabel = _sheet1 != null ? $"Sheet 1 — {_sheet1.Label}" : "Sheet 1";
            Sheet2TargetLabel = _sheet2 != null ? $"Sheet 2 — {_sheet2.Label}" : "Sheet 2";
            SheetsSummary = SheetsReady
                ? $"{Sheet1Count + Sheet2Count} view(s) on the two sheets. Schedules are not listed; they stay where they are."
                : Sheet2Choice == null ? "Pick Sheet 2." : "Reading the sheets…";
            _rowsView.Refresh();
        }

        private void OnRowTicked(ViewRowViewModel row)
        {
            if (row.UserTicked) _ticked.Add(row.Key); else _ticked.Remove(row.Key);
            Recompute();
        }

        private void SetTicks(IEnumerable<ViewRowViewModel> rows, bool value)
        {
            foreach (var row in rows.ToList())
            {
                row.SetTickQuietly(value);
                if (value) _ticked.Add(row.Key); else _ticked.Remove(row.Key);
            }
            Recompute();
        }

        private bool MatchesFilter(ViewRowViewModel r) => Filter switch
        {
            RowFilter.Sheet1 => r.Slot == 1,
            RowFilter.Sheet2 => r.Slot == 2,
            _ => true
        };

        /// <summary>The options as shown, for a plan or a run.</summary>
        public SheetViewsCombineSettings CurrentSettings() => new SheetViewsCombineSettings
        {
            Target = Target,
            Unticked = Unticked,
            OnFailure = OnFailure,
            Strategy = Strategy,
            Order = Order,
            KeepViewportType = KeepViewportType,
            KeepDetailNumbers = KeepDetailNumbers,
            OpenSheetWhenDone = OpenSheetWhenDone,
            NewSheetName = string.IsNullOrWhiteSpace(NewSheetName) ? new SheetViewsCombineSettings().NewSheetName : NewSheetName.Trim(),
            MarginTopMm = MarginTopMm,
            MarginBottomMm = MarginBottomMm,
            MarginLeftMm = MarginLeftMm,
            MarginRightMm = MarginRightMm,
            HorizontalGapMm = HorizontalGapMm,
            VerticalGapMm = VerticalGapMm
        }.Normalize();

        private void Recompute()
        {
            if (!_ready || _suppress) return;

            // "Re-pack them too" includes every view on the target.
            long targetKey = Target switch
            {
                TargetMode.Sheet1 => _sheet1?.Key ?? 0,
                TargetMode.Sheet2 => _sheet2?.Key ?? 0,
                TargetMode.OtherSheet => _other?.Key ?? 0,
                _ => 0
            };
            foreach (var row in Rows)
                row.IsForced = Unticked == UntickedTargetMode.RepackAll && row.View.SheetKey == targetKey;

            long tb = SelectedTitleBlock?.Key ?? 0;
            var frame = _titleBlockFrames.TryGetValue(tb, out var f) ? f : (null, _measuring.Contains(tb) ? "Measuring the title block…" : "The title block has not been measured yet. Press Refresh.");
            Plan = CombinePlanner.Build(new CombineRequest
            {
                Sheet1 = _sheet1,
                Sheet2 = _sheet2,
                OtherTarget = Target == TargetMode.OtherSheet ? _other : null,
                Ticked = _ticked.ToList(),
                Settings = CurrentSettings(),
                NewSheetNumber = NewSheetNumber,
                NewSheetName = NewSheetName,
                NewTitleBlockKey = tb,
                NewTitleBlockFrame = frame.Item1,
                NewTitleBlockProblem = frame.Item2,
                ExistingSheetNumbers = _catalog?.ExistingSheetNumbers ?? new HashSet<string>()
            });

            UpdateRowStatus();
            UpdatePlanLines();
            UpdateTabs();
            UpdateMetrics();
            BlockerText = Plan.Blocker ?? string.Empty;
            WarningsText = string.Join(Environment.NewLine, Plan.Warnings);
            FailureModeText = OnFailure == FailureMode.StopAndUndo ? "If a view fails: stop and undo everything" : "If a view fails: skip it and report";
            OnPropertyChanged(nameof(Plan));
            OnPropertyChanged(nameof(CurrentSheet));
            PlanChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateRowStatus()
        {
            var byKey = Plan.Items.ToDictionary(i => i.View.ViewportKey);
            foreach (var row in Rows)
            {
                if (!byKey.TryGetValue(row.Key, out var item))
                {
                    row.Status = Plan.Blocker != null && row.IsTicked ? "Waiting: " + Plan.Blocker : string.Empty;
                    row.StatusKind = RowStatusKind.None;
                    continue;
                }
                string sheet = item.SheetIndex >= 0 && item.SheetIndex < Plan.Sheets.Count ? Plan.Sheets[item.SheetIndex].Number : string.Empty;
                (row.Status, row.StatusKind) = item.Action switch
                {
                    ViewAction.StaysInPlace => ("Stays in place", RowStatusKind.Stay),
                    ViewAction.Repack => (item.SheetIndex == 0 ? "Stays, re-packed" : $"Re-packed onto {sheet}", RowStatusKind.Repack),
                    ViewAction.Move => ($"Moves to {sheet}", RowStatusKind.Move),
                    ViewAction.CopyLegend => (item.OverlapWarning ? "Copy, same location (overlaps)" : "Copy, same location", RowStatusKind.Copy),
                    ViewAction.Skip => ("Skip: " + item.Reason, RowStatusKind.Skip),
                    _ => ($"Stays on {item.View.SheetNumber}", RowStatusKind.None)
                };
            }
            TickCountText = $"{Rows.Count(r => r.IsTicked)} of {Rows.Count} ticked";
        }

        private void UpdatePlanLines()
        {
            PlanLines.Clear();
            foreach (var item in CombinePlanner.Order(Plan.Items.Where(i => i.Action != ViewAction.None), GroupOrder.SheetThenType)
                         .OrderBy(i => i.SheetIndex < 0 ? int.MaxValue : i.SheetIndex))
            {
                var sheet = item.SheetIndex >= 0 && item.SheetIndex < Plan.Sheets.Count ? Plan.Sheets[item.SheetIndex] : null;
                string detail = item.DetailNumber ?? item.View.DetailNumber;
                if (item.DetailNumber != null && item.View.DetailNumber != item.DetailNumber && !string.IsNullOrEmpty(item.View.DetailNumber))
                    detail += $" (was {item.View.DetailNumber})";
                PlanLines.Add(new PlanLine
                {
                    Name = item.View.Name,
                    From = item.View.SheetNumber,
                    To = sheet?.Number ?? "—",
                    Detail = detail,
                    Location = item.Action switch
                    {
                        ViewAction.StaysInPlace => "Unchanged",
                        ViewAction.CopyLegend => $"Same as on {item.View.SheetNumber}",
                        ViewAction.Skip => "—",
                        _ => sheet?.Area is SheetRect area && item.Target is SheetRect t ? Position(area, t) : "—"
                    },
                    Action = item.Action switch
                    {
                        ViewAction.StaysInPlace => "Stays in place",
                        ViewAction.Repack => "Re-pack",
                        ViewAction.Move => "Remove + place",
                        ViewAction.CopyLegend => "Copy, same location",
                        _ => "Skip: " + item.Reason
                    },
                    Kind = item.Action switch
                    {
                        ViewAction.StaysInPlace => RowStatusKind.Stay,
                        ViewAction.Repack => RowStatusKind.Repack,
                        ViewAction.Move => RowStatusKind.Move,
                        ViewAction.CopyLegend => RowStatusKind.Copy,
                        _ => RowStatusKind.Skip
                    },
                    Slot = item.Slot
                });
            }
        }

        /// <summary>"Top left", "Middle right" … for a footprint inside an area.</summary>
        private static string Position(SheetRect area, SheetRect t)
        {
            double cx = ((t.MinX + t.MaxX) / 2 - area.MinX) / area.Width;
            double cy = ((t.MinY + t.MaxY) / 2 - area.MinY) / area.Height;
            string v = cy > 2.0 / 3 ? "Top" : cy < 1.0 / 3 ? "Bottom" : "Middle";
            string h = cx < 1.0 / 3 ? "left" : cx > 2.0 / 3 ? "right" : "centre";
            return $"{v} {h}";
        }

        private void UpdateTabs()
        {
            int keep = Math.Min(CurrentPage, Math.Max(0, Plan.Sheets.Count - 1));
            PageTabs.Clear();
            foreach (var sheet in Plan.Sheets)
                PageTabs.Add(new PageTabViewModel(sheet.Index,
                    $"{sheet.Number}{(sheet.Index == 0 ? " (target)" : string.Empty)} · {sheet.Views.Count}",
                    sheet.Index == keep, i => CurrentPage = i));
            OnPropertyChanged(nameof(HasTabs));
            if (CurrentPage != keep) CurrentPage = keep;
            UpdatePreviewTitle();
            FitSummary = Plan.Blocker != null ? string.Empty
                : Plan.Sheets.Count == 1 ? $"All placed views fit on {Plan.Target?.Number}."
                : $"{Plan.Sheets.Count - 1} extra sheet(s) needed.";
        }

        private void UpdatePreviewTitle()
        {
            var sheet = CurrentSheet;
            PreviewTitle = sheet == null ? string.Empty
                : $"{sheet.Label}{(sheet.IsNew ? " (new)" : string.Empty)} · {sheet.Views.Count} view(s)";
        }

        private void UpdateMetrics()
        {
            SelectedCount = Rows.Count(r => r.IsTicked);
            MoveCount = Plan.MoveCount;
            CopyCount = Plan.CopyCount;
            SkipCount = Plan.SkipCount;
            RepackCount = Plan.RepackCount;
            SheetCount = Plan.Blocker == null ? Plan.Sheets.Count : 0;
        }

        // ── Run result ──────────────────────────────────────────────────

        private void OnApplied(SessionResult r, bool open)
        {
            End();
            LogResult(r);
            if (r.Run != null)
            {
                ResultLines.Clear();
                foreach (var line in r.Run.Results.OrderBy(x => x.Outcome == ViewOutcome.Skipped).ThenBy(x => x.Slot))
                    ResultLines.Add(new ResultLine
                    {
                        Name = line.Name,
                        Outcome = line.Outcome switch
                        {
                            ViewOutcome.Moved => "Moved",
                            ViewOutcome.Repacked => "Re-packed",
                            ViewOutcome.Copied => "Copied",
                            ViewOutcome.Failed => "Failed",
                            _ => r.Applied ? "Skipped" : "Not done"
                        },
                        Detail = line.Detail,
                        Kind = line.Outcome switch
                        {
                            ViewOutcome.Moved => RowStatusKind.Move,
                            ViewOutcome.Repacked => RowStatusKind.Repack,
                            ViewOutcome.Copied => RowStatusKind.Copy,
                            _ => RowStatusKind.Skip
                        },
                        Slot = line.Slot
                    });
                OnPropertyChanged(nameof(HasResults));
            }

            if (r.Catalog != null) LoadCatalog(r.Catalog);

            if (r.Applied)
            {
                LastTargetKey = r.Run?.TargetSheetKey ?? 0;
                int skipped = r.Run?.Results.Count(x => x.Outcome == ViewOutcome.Skipped) ?? 0;
                int done = (r.Run?.Results.Count ?? 0) - skipped;
                ResultSummary = $"{done} of {done + skipped} view(s) done" + (skipped > 0 ? $", {skipped} skipped" : string.Empty) + ".";

                // A new target is now an existing sheet: select it so the next run uses it.
                if (Target == TargetMode.NewSheet && LastTargetKey != 0)
                {
                    _suppress = true;
                    Target = TargetMode.OtherSheet;
                    UpdateOtherSheets();
                    OtherTargetChoice = OtherSheets.FirstOrDefault(s => s.Key == LastTargetKey);
                    _suppress = false;
                    OnPropertyChanged(nameof(IsNewSheetTarget));
                    OnPropertyChanged(nameof(IsOtherSheetTarget));
                    NewSheetNumber = SuggestNewNumber();
                }
                _ticked.Clear();
                StoreSheets(r.Sheets);
                OpenStage(5);
                RunFinished?.Invoke(this, new RunFinishedEventArgs(
                    skipped == 0
                        ? $"{done} view(s) done. One Undo (Ctrl+Z) reverts the whole run."
                        : $"{done} of {done + skipped} view(s) done. {skipped} skipped:\n\n"
                          + string.Join("\n", r.Run.Results.Where(x => x.Outcome == ViewOutcome.Skipped).Take(8).Select(x => $"• {x.Name}: {x.Detail}"))
                          + (skipped > 8 ? "\n…" : string.Empty)
                          + "\n\nFull details are in step 5 and in the Activity Log.",
                    skipped > 0));
                if (open && LastTargetKey != 0)
                    OpenTargetSheet();
            }
            else
            {
                StoreSheets(r.Sheets);
                if (r.Run != null)
                {
                    ResultSummary = "Nothing was changed.";
                    OpenStage(5);
                }
                if (r.Level == LogLevel.Error || r.Run != null)
                    RunFinished?.Invoke(this, new RunFinishedEventArgs((r.Message ?? "Nothing was changed.")
                        + "\n\nUnexpected errors are also written to %AppData%\\Revit26_Plugin_Revised\\Logs.", true));
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private void OpenStage(int stage)
        {
            IsStage1Open = stage == 1;
            IsStage2Open = stage == 2;
            IsStage3Open = stage == 3;
            IsStage4Open = stage == 4;
            IsStage5Open = stage == 5;
        }

        private string SuggestNewNumber()
        {
            var used = _catalog?.ExistingSheetNumbers ?? new HashSet<string>();
            string candidate = (_sheet1?.Number ?? "SH") + "-C";
            return used.Contains(candidate) ? CombinePlanner.NextNumber(candidate, 1, used) : candidate;
        }

        private void Begin(string text)
        {
            _busyCount++;
            IsBusy = true;
            StatusText = text;
        }

        private void End()
        {
            _busyCount = Math.Max(0, _busyCount - 1);
            IsBusy = _busyCount > 0;
            if (IsBusy) return;
            StatusText = "Ready.";

            // Requests that came in while Revit was busy.
            if (_pendingLoads.Count > 0)
            {
                var keys = _pendingLoads.ToList();
                _pendingLoads.Clear();
                Load(keys);
            }
            else if (_measurePending)
            {
                _measurePending = false;
                if (Target == TargetMode.NewSheet) EnsureTitleBlockMeasured();
            }
        }

        private void NotQueued()
        {
            End();
            Log(LogLevel.Error, "Revit did not accept the request. Close the window and start the tool again.");
        }

        private void LogResult(SessionResult r)
        {
            if (r == null) return;
            if (!string.IsNullOrEmpty(r.Message)) Log(r.Level, r.Message);
            foreach (var d in r.Details) Log(d.Level, d.Message);
        }

        private void Log(LogLevel level, string message)
        {
            LogEntries.Add(new LogEntry(level, message));
            while (LogEntries.Count > 2000) LogEntries.RemoveAt(0);
        }

        /// <summary>Saves the options for next time.</summary>
        public void SaveSettings()
        {
            try
            {
                SettingsService<SheetViewsCombineSettings>.Save(SheetViewsCombineCommand.SettingsFolder, CurrentSettings());
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                // Settings are a convenience; the window closes anyway.
            }
        }

        /// <summary>Releases the Revit session.</summary>
        public void Dispose() => _session.Dispose();
    }
}
