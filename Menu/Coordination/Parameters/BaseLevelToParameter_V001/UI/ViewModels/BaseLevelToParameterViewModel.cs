using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.Core;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.Infrastructure;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.ViewModels
{
    /// <summary>
    /// View model of the Base Level to Parameter window: the element list, the target parameter with its
    /// pre-checks a–e, the metrics and the run. Revit work is queued on the API thread through
    /// <see cref="BaseLevelEventHandler"/>.
    /// </summary>
    public sealed partial class BaseLevelToParameterViewModel : ToolViewModelBase
    {
        private readonly BaseLevelEventHandler _handler;
        private readonly ExternalEvent _event;
        private readonly DispatcherTimer _recheckTimer;
        private TargetFacts _lastTarget;
        private bool _levelsLoaded;
        private bool _bulkTick;
        private string _revitSharedParamFile = string.Empty;
        private BaseLevelToParameterSettings _settings;

        /// <summary>Creates the view model; call <see cref="Initialize"/> on the Revit API thread before showing the window.</summary>
        public BaseLevelToParameterViewModel(BaseLevelEventHandler handler, ExternalEvent externalEvent, string documentTitle)
        {
            _handler = handler;
            _event = externalEvent;
            DocumentTitle = documentTitle ?? string.Empty;

            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ElementRowViewModel.Category)));
            RowsView.SortDescriptions.Add(new SortDescription(nameof(ElementRowViewModel.Category), ListSortDirection.Ascending));
            RowsView.Filter = FilterRow;

            _recheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _recheckTimer.Tick += (_, _) =>
            {
                _recheckTimer.Stop();
                if (IsRunning) { _recheckTimer.Start(); return; }
                if (_levelsLoaded) RunPrecheck(readingLevels: false);
            };
        }

        // ── bound state ───────────────────────────────────────────────────────

        /// <summary>Title of the open document, shown under the window title.</summary>
        public string DocumentTitle { get; }

        /// <summary>Every listed element.</summary>
        public ObservableCollection<ElementRowViewModel> Rows { get; } = new();

        /// <summary>Rows grouped by category, filtered by <see cref="FilterText"/>, sortable by column.</summary>
        public ICollectionView RowsView { get; }

        /// <summary>Parameters bound in the project, for the target combo box.</summary>
        public ObservableCollection<ProjectParameterOption> ProjectParameters { get; } = new();

        /// <summary>Pre-check lines a–e.</summary>
        public ObservableCollection<PrecheckItem> Checks { get; } = new();

        /// <summary>Opens the New Parameter dialog; set by the window. Returns true when the user pressed Create.</summary>
        public Func<NewParameterViewModel, bool> ShowNewParameterDialog { get; set; }

        /// <summary>
        /// Asks before Run replaces existing values; set by the window. Null (no window) means Rewrite, as chosen.
        /// </summary>
        public Func<RewriteConfirmation, RewriteAnswer> ConfirmRewrite { get; set; }

        [ObservableProperty] private string _filterText = string.Empty;
        [ObservableProperty] private string _sourceText = string.Empty;
        [ObservableProperty] private string _subtitle = string.Empty;
        [ObservableProperty] private string _targetName = string.Empty;
        [ObservableProperty] private string _targetKind = string.Empty;
        [ObservableProperty] private bool _overwrite;
        [ObservableProperty] private string _checksSummary = "Press Load Base Level to read the ticked elements and run the pre-checks.";
        [ObservableProperty] private bool _checksBlocked = true;
        [ObservableProperty] private string _runSummary = string.Empty;
        [ObservableProperty] private int _listedCount;
        [ObservableProperty] private int _checkedCount;
        [ObservableProperty] private int _readyCount;
        [ObservableProperty] private int _warningCount;
        [ObservableProperty] private int _blockedCount;
        [ObservableProperty] private NewParameterSpec _pendingNew;
        [ObservableProperty] private int _upToDateCount;
        [ObservableProperty] private int _hasValueCount;
        private bool _bulkChoice;

        /// <summary>
        /// "Clear old values, write only new values" (1.3.0): every different value is rewritten (row choices are
        /// ignored) and the value of an element without a base level is cleared. Not remembered; starts off.
        /// </summary>
        [ObservableProperty] private bool _clearOld;

        /// <summary>"Existing values: Skip" radio button. Setting false is ignored (another radio button was picked).</summary>
        public bool SkipExisting
        {
            get => !Overwrite && !ClearOld;
            set { if (value) SetExistingMode(false, false); }
        }

        /// <summary>"Existing values: Rewrite" radio button. Setting false is ignored.</summary>
        public bool RewriteExisting
        {
            get => Overwrite && !ClearOld;
            set { if (value) SetExistingMode(true, false); }
        }

        /// <summary>"Existing values: Clear old values, write only new values" radio button. Setting false is ignored.</summary>
        public bool ClearOldValues
        {
            get => ClearOld;
            set { if (value) SetExistingMode(true, true); }
        }

        /// <summary>True when the per-row "If value exists" choices apply (not in Clear old values mode).</summary>
        public bool RowChoicesEnabled => !ClearOld;

        private void SetExistingMode(bool overwrite, bool clearOld)
        {
            ClearOld = clearOld;
            Overwrite = overwrite;
        }

        // ── value format ──────────────────────────────────────────────────────

        /// <summary>Patterns for "Level name + offset".</summary>
        public IReadOnlyList<ValuePatternOption> PatternOptions => ValueFormatter.OffsetPatterns;

        [ObservableProperty] private bool _withOffset = true;
        [ObservableProperty] private ValuePatternOption _selectedPattern = ValueFormatter.OffsetPatterns[0];
        [ObservableProperty] private string _customPattern = ValueFormatter.DefaultCustomPattern;
        [ObservableProperty] private string _formatSample = string.Empty;
        [ObservableProperty] private string _customError = string.Empty;

        /// <summary>The format in use.</summary>
        public ValueFormat Format => WithOffset ? SelectedPattern?.Format ?? ValueFormat.LevelParenOffset : ValueFormat.LevelOnly;

        /// <summary>True when the custom pattern box is in use.</summary>
        public bool IsCustom => Format == ValueFormat.Custom;

        partial void OnWithOffsetChanged(bool value) => OnFormatChanged();
        partial void OnSelectedPatternChanged(ValuePatternOption value) => OnFormatChanged();
        partial void OnCustomPatternChanged(string value) => OnFormatChanged();

        private void OnFormatChanged()
        {
            OnPropertyChanged(nameof(IsCustom));
            CustomError = IsCustom ? ValueFormatter.ValidateCustom(CustomPattern) ?? string.Empty : string.Empty;
            FormatSample = IsCustom && CustomError.Length > 0 ? CustomError : ValueFormatter.Sample(Format, CustomPattern);
            foreach (var row in Rows) row.RefreshValue(Format, CustomPattern);
            Reevaluate();
            if (ChangedOnly) RowsView.Refresh();
        }

        // ── grid filters ──────────────────────────────────────────────────────

        [ObservableProperty] private bool _changedOnly;
        [ObservableProperty] private string _tileFilter = string.Empty;
        [ObservableProperty] private string _tileFilterText = string.Empty;

        /// <summary>Grid rows highlighted by the user; set by the window.</summary>
        public IList<ElementRowViewModel> SelectedRows { get; set; } = new List<ElementRowViewModel>();

        partial void OnChangedOnlyChanged(bool value) => RowsView.Refresh();

        partial void OnTileFilterChanged(string value)
        {
            TileFilterText = string.IsNullOrEmpty(value) ? string.Empty : $"Showing: {TileLabel(value)} · click the tile again to show all";
            RowsView.Refresh();
        }

        private static string TileLabel(string tile) => tile switch
        {
            "Checked" => "Checked",
            "Ready" => "Ready",
            "UpToDate" => "Up to date",
            "Warning" => "Warnings",
            "Blocked" => "Blocked",
            "HasValue" => "Has a different value",
            _ => "All"
        };

        /// <summary>True when Run can start: levels read, nothing blocked, at least one row ready.</summary>
        public bool CanRun => !IsRunning && _levelsLoaded && !ChecksBlocked && ReadyCount > 0;

        partial void OnFilterTextChanged(string value) => RowsView.Refresh();
        partial void OnOverwriteChanged(bool value) => OnExistingModeChanged();

        partial void OnClearOldChanged(bool value) => OnExistingModeChanged();

        private void OnExistingModeChanged()
        {
            OnPropertyChanged(nameof(SkipExisting));
            OnPropertyChanged(nameof(RewriteExisting));
            OnPropertyChanged(nameof(ClearOldValues));
            OnPropertyChanged(nameof(RowChoicesEnabled));
            SetChoiceCommand.NotifyCanExecuteChanged();
            Reevaluate();
        }
        partial void OnChecksBlockedChanged(bool value) => NotifyRun();
        partial void OnReadyCountChanged(int value) => NotifyRun();

        partial void OnTargetNameChanged(string value)
        {
            var option = ProjectParameters.FirstOrDefault(p => string.Equals(p.Name, value?.Trim(), StringComparison.OrdinalIgnoreCase));
            TargetKind = option != null
                ? option.Kind
                : PendingNew != null && string.Equals(PendingNew.Name, value?.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? "New · Shared · Instance · Text (created on Run)"
                    : string.IsNullOrWhiteSpace(value) ? string.Empty : "Not in the project · use New Parameter…";
            ScheduleRecheck();
        }

        /// <inheritdoc/>
        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.PropertyName == nameof(IsRunning)) NotifyRun();
        }

        private void NotifyRun()
        {
            OnPropertyChanged(nameof(CanRun));
            RunCommand.NotifyCanExecuteChanged();
        }

        // ── start-up ──────────────────────────────────────────────────────────

        /// <summary>
        /// Lists the active view's model elements and the project parameters. Runs inside the command,
        /// on the Revit API thread.
        /// </summary>
        public void Initialize(Document doc, View activeView)
        {
            _settings = SettingsService<BaseLevelToParameterSettings>.Load(ToolCatalog.BaseLevelToParameter.Name);
            _revitSharedParamFile = doc.Application.SharedParametersFilename ?? string.Empty;
            RestoreFormat();
            LoadParameters(doc);
            ListFromView(doc, activeView);
            if (!string.IsNullOrWhiteSpace(_settings.LastTargetName) &&
                ProjectParameters.Any(p => string.Equals(p.Name, _settings.LastTargetName, StringComparison.OrdinalIgnoreCase)))
                TargetName = _settings.LastTargetName;
            SummaryText = "Ready.";
            AddLog(LogLevel.Info, $"{ToolCatalog.BaseLevelToParameter.Title} ready.");
        }

        /// <summary>Saves the remembered settings; called when the window closes.</summary>
        public void OnWindowClosing()
        {
            _recheckTimer.Stop();
            _settings ??= new BaseLevelToParameterSettings();
            _settings.LastTargetName = TargetName ?? string.Empty;
            _settings.ValueFormat = Format.ToString();
            _settings.CustomPattern = CustomPattern ?? ValueFormatter.DefaultCustomPattern;
            _settings.Version = 2;
            SettingsService<BaseLevelToParameterSettings>.Save(ToolCatalog.BaseLevelToParameter.Name, _settings);
        }

        private void RestoreFormat()
        {
            if (!Enum.TryParse(_settings.ValueFormat, out ValueFormat saved) || !Enum.IsDefined(typeof(ValueFormat), saved))
                saved = ValueFormat.LevelParenOffset;
            CustomPattern = string.IsNullOrWhiteSpace(_settings.CustomPattern) ? ValueFormatter.DefaultCustomPattern : _settings.CustomPattern;
            if (saved == ValueFormat.LevelOnly)
            {
                WithOffset = false;
            }
            else
            {
                WithOffset = true;
                SelectedPattern = PatternOptions.First(p => p.Format == saved);
            }
            OnFormatChanged();
        }

        // ── commands: source ──────────────────────────────────────────────────

        [RelayCommand]
        private void LoadSelection() => RunJob(doc =>
        {
            var uidoc = new UIDocument(doc);
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0)
            {
                Log(LogLevel.Warning, "Nothing is selected in Revit. Select elements first, then press Load Selection.");
                return;
            }
            var items = ElementListService.FromIds(doc, ids, out int ignored);
            FillRows(items);
            SourceText = $"Revit selection · {items.Count} elements";
            Subtitle = $"{DocumentTitle} · Revit selection";
            AddLog(LogLevel.Info, $"Listed {items.Count} model elements from the Revit selection" +
                (ignored > 0 ? $"; {ignored} selected item(s) are not model elements of the host model and were left out." : "."));
        }, null);

        [RelayCommand]
        private void LoadActiveView() => RunJob(doc =>
        {
            ListFromView(doc, new UIDocument(doc).ActiveView);
        }, null);

        [RelayCommand]
        private void LoadAll() => RunJob(doc =>
        {
            var items = ElementListService.List(doc, null);
            FillRows(items);
            SourceText = $"All model elements · host model · {items.Count} elements";
            Subtitle = $"{DocumentTitle} · All model elements";
            AddLog(LogLevel.Info, $"Listed {items.Count} model elements in the host model (linked elements are not listed).");
        }, null);

        [RelayCommand]
        private void LoadBaseLevel()
        {
            if (!Rows.Any(r => r.IsChecked))
            {
                Log(LogLevel.Warning, "Tick at least one element first.");
                return;
            }
            RunPrecheck(readingLevels: true);
        }

        [RelayCommand]
        private void Recheck()
        {
            if (!_levelsLoaded)
            {
                LoadBaseLevel();
                return;
            }
            RunPrecheck(readingLevels: false);
        }

        // ── commands: target ──────────────────────────────────────────────────

        [RelayCommand]
        private void NewParameter()
        {
            if (ShowNewParameterDialog == null) return;
            string startName = ProjectParameters.Any(p => string.Equals(p.Name, TargetName?.Trim(), StringComparison.OrdinalIgnoreCase))
                ? string.Empty
                : TargetName?.Trim() ?? string.Empty;
            string file = PendingNew?.FilePath;
            if (string.IsNullOrWhiteSpace(file)) file = string.IsNullOrWhiteSpace(_settings?.SharedParamFilePath) ? _revitSharedParamFile : _settings.SharedParamFilePath;
            var categories = Rows.Where(r => r.IsChecked).Select(r => r.Category).Distinct().OrderBy(c => c);

            var dialog = new NewParameterViewModel(startName, file, PendingNew?.FileGroup ?? _settings?.FileGroup,
                ProjectParameters.Select(p => p.Name), categories);
            if (ShowNewParameterDialog(dialog) != true) return;

            PendingNew = dialog.Result;
            if (_settings != null)
            {
                _settings.SharedParamFilePath = PendingNew.FilePath;
                _settings.FileGroup = PendingNew.FileGroup;
            }
            AddLog(LogLevel.Info, $"New parameter “{PendingNew.Name}” set as target; it is created in {PendingNew.FilePath} / {PendingNew.FileGroup} and the project when you press Run.");
            TargetName = PendingNew.Name;
            OnTargetNameChanged(TargetName); // refresh the kind text even when the name did not change
        }

        // ── commands: grid ────────────────────────────────────────────────────

        [RelayCommand]
        private void SelectAll() => SetVisibleTicks(true);

        [RelayCommand]
        private void SelectNone() => SetVisibleTicks(false);

        [RelayCommand]
        private void TickCategory(string category) => SetCategoryTicks(category, true);

        [RelayCommand]
        private void UntickCategory(string category) => SetCategoryTicks(category, false);

        [RelayCommand]
        private void FilterTile(string tile) => TileFilter = TileFilter == tile ? string.Empty : tile ?? string.Empty;

        /// <summary>
        /// Sets the "If value exists" choice ("Default", "Skip" or "Rewrite") on the highlighted grid rows, or on every
        /// row the grid shows when none is highlighted.
        /// </summary>
        [RelayCommand(CanExecute = nameof(RowChoicesEnabled))]
        private void SetChoice(string choice)
        {
            if (!Enum.TryParse(choice, out ExistingChoice value)) return;
            var targets = SelectedRows.Count > 0
                ? SelectedRows.ToList()
                : RowsView.Cast<ElementRowViewModel>().ToList();
            targets = targets.Where(r => r.HasExistingValue || r.Choice != ExistingChoice.Default).ToList();
            if (targets.Count == 0)
            {
                Log(LogLevel.Info, SelectedRows.Count > 0
                    ? "None of the highlighted rows has a different existing value."
                    : "No shown row has a different existing value.");
                return;
            }
            _bulkChoice = true;
            int n = 0;
            foreach (var r in targets)
            {
                if (r.Choice == value) continue;
                r.Choice = value;
                n++;
            }
            _bulkChoice = false;
            if (n == 0) return;
            Reevaluate();
            string what = value == ExistingChoice.Default ? $"reset to the default ({(Overwrite ? "Rewrite" : "Skip")})" : $"set to {value}";
            Log(LogLevel.Info, $"If value exists: {n} row(s) {what}.");
        }

        [RelayCommand]
        private void SelectInRevit() => ShowInRevit(zoom: false);

        [RelayCommand]
        private void ZoomTo() => ShowInRevit(zoom: true);

        private void ShowInRevit(bool zoom)
        {
            var ids = SelectedRows.Select(r => r.Id).ToList();
            if (ids.Count == 0)
            {
                Log(LogLevel.Warning, "Highlight one or more rows in the grid first.");
                return;
            }
            RunJob(doc =>
            {
                var uidoc = new UIDocument(doc);
                var existing = ids.Select(i => new ElementId(i)).Where(id => doc.GetElement(id) != null).ToList();
                if (existing.Count == 0)
                {
                    Log(LogLevel.Warning, "The highlighted elements no longer exist in the model.");
                    return;
                }
                uidoc.Selection.SetElementIds(existing);
                if (zoom) uidoc.ShowElements(existing);
                Log(LogLevel.Info, $"{(zoom ? "Selected and zoomed to" : "Selected")} {existing.Count} element(s) in Revit.");
            }, null);
        }

        // ── command: run ──────────────────────────────────────────────────────

        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            TargetRequest request = BuildRequest();
            if (!ConfirmRewrites(request)) return;
            RunResult result = null;
            RunSummary = string.Empty;
            RunJob(doc => result = TargetParameterService.Run(doc, request), () =>
            {
                if (result == null) return;
                ApplyFacts(result.Facts);
                foreach (var (level, text) in result.Log) AddLog(level, text);

                if (result.NotStartedReason != null)
                {
                    Reevaluate();
                    Log(LogLevel.Warning, result.NotStartedReason);
                    return;
                }

                var byId = Rows.ToDictionary(r => r.Id);
                foreach (RunRowResult r in result.Rows)
                {
                    if (!byId.TryGetValue(r.Id, out var row)) continue;
                    row.SetStatus(r.State, r.Text);
                    if (r.State == RowState.Assigned) row.MarkWritten(r.Value);
                    if (r.State == RowState.Failed) AddLog(LogLevel.Error, $"{r.Id} {row.Category}: {r.Text}");
                }
                int assigned = result.Rows.Count(r => r.State == RowState.Assigned);
                int skipped = result.Rows.Count(r => r.State == RowState.Skipped);
                int failed = result.Rows.Count(r => r.State == RowState.Failed);
                RunSummary = BaseLevelRules.RunSummary(assigned, skipped, failed);
                Log(failed > 0 ? LogLevel.Warning : LogLevel.Success,
                    $"Run finished: {RunSummary}. Undo with Ctrl+Z in Revit (“Base Level to Parameter”).");

                ReadyCount = 0; // statuses now show the run; a re-check is needed before the next run
                UpToDateCount = 0;
                _lastTarget = null;
                Checks.Clear();
                ChecksSummary = "Run finished. Press Re-check before the next run.";
                if (PendingNew != null && string.Equals(PendingNew.Name, TargetName, StringComparison.OrdinalIgnoreCase))
                    PendingNew = null; // it is in the project now
                RunJob(doc => LoadParameters(doc), () => OnTargetNameChanged(TargetName));
            });
        }

        /// <summary>
        /// Asks before existing values are replaced. Returns false when the user cancels; "Skip these" marks those
        /// elements Skip in <paramref name="request"/> only (the row choices stay as they are).
        /// </summary>
        private bool ConfirmRewrites(TargetRequest request)
        {
            var rewrites = Rows.Where(r => r.IsChecked && r.State == RowState.Ready && r.HasOldValue).ToList();
            if (rewrites.Count == 0 || ConfirmRewrite == null) return true;

            var info = new RewriteConfirmation
            {
                TargetName = request.Name,
                Count = rewrites.Count,
                Total = ReadyCount,
                Rows = rewrites.Take(RewriteConfirmation.MaxRows)
                    .Select(r => new RewriteRow(r.Id, r.Facts?.CurrentValue ?? string.Empty, r.FormattedValue ?? RewriteRow.Cleared))
                    .ToList()
            };
            RewriteAnswer answer = ConfirmRewrite(info);
            switch (answer)
            {
                case RewriteAnswer.Rewrite:
                    return true;
                case RewriteAnswer.Skip:
                    foreach (var r in rewrites) request.Choices[r.Id] = ExistingChoice.Skip;
                    AddLog(LogLevel.Info, $"Run: {rewrites.Count} existing value(s) kept (Skip these).");
                    request.ClearOld = false; // "Skip these" keeps the values Clear old values would have emptied too
                    return true;
                default:
                    Log(LogLevel.Info, "Run cancelled; nothing was changed.");
                    return false;
            }
        }

        // ── Revit jobs ────────────────────────────────────────────────────────

        private void RunPrecheck(bool readingLevels)
        {
            TargetRequest request = BuildRequest();
            if (request.ElementIds.Count == 0)
            {
                Checks.Clear();
                ChecksSummary = "Tick at least one element.";
                ChecksBlocked = true;
                UpdateMetrics();
                return;
            }
            FactsResult facts = null;
            RunJob(doc => facts = TargetParameterService.GatherFacts(doc, request), () =>
            {
                if (facts == null) return;
                _levelsLoaded = true;
                ApplyFacts(facts);
                Reevaluate();
                if (readingLevels)
                {
                    int found = facts.Elements.Count(f => f.HasLevel);
                    AddLog(LogLevel.Success, $"Read base level and offset for {found} of {request.ElementIds.Count} ticked elements.");
                    var noLevel = facts.Elements.Where(f => !f.HasLevel).ToList();
                    if (noLevel.Count > 0)
                        AddLog(LogLevel.Warning, $"{noLevel.Count} element(s) have no base level and are skipped (e.g. {Examples(noLevel)}).");
                    var owned = facts.Elements.Where(f => !string.IsNullOrEmpty(f.OwnedBy)).ToList();
                    if (owned.Count > 0)
                        AddLog(LogLevel.Warning, $"{owned.Count} element(s) are owned by another user and are skipped ({Examples(owned)}).");
                }
                if (facts.Missing.Count > 0)
                    AddLog(LogLevel.Warning, $"{facts.Missing.Count} listed element(s) no longer exist in the model.");
                AddLog(LogLevel.Info,
                    $"Pre-checks: {Checks.Count(c => c.Result == CheckResult.Pass)} pass, {Checks.Count(c => c.Result == CheckResult.Warning)} warnings, " +
                    $"{Checks.Count(c => c.Result == CheckResult.Blocked)} blocked → {ChecksSummary}.");
            });
        }

        /// <summary>Queues <paramref name="job"/> on the Revit API thread; <paramref name="after"/> runs there when it succeeds.</summary>
        private void RunJob(Action<Document> job, Action after)
        {
            if (IsRunning)
            {
                Log(LogLevel.Warning, "Another job is still running.");
                return;
            }
            IsRunning = true;
            _handler.Job = job;
            _handler.Completed = failure =>
            {
                IsRunning = false;
                if (failure is OperationCanceledException || failure is Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    Log(LogLevel.Warning, "Cancelled by user. Model changes made by this run were undone.");
                    return;
                }
                if (failure != null)
                {
                    Log(LogLevel.Error, $"Failed and rolled back — {failure.Message}");
                    ToolGuard.Report(GetType(), failure);
                    return;
                }
                after?.Invoke();
            };
            ExternalEventRequest raised = _event.Raise();
            if (raised != ExternalEventRequest.Accepted && raised != ExternalEventRequest.Pending)
            {
                IsRunning = false;
                Log(LogLevel.Error, "Revit is not ready to run the job (a command or dialog is open). Try again.");
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private void LoadParameters(Document doc)
        {
            string keep = TargetName;
            ProjectParameters.Clear();
            foreach (var p in TargetParameterService.ListProjectParameters(doc)) ProjectParameters.Add(p);
            if (TargetName != keep) TargetName = keep;
        }

        private void ListFromView(Document doc, View view)
        {
            if (!ElementListService.CanListFrom(view))
            {
                Log(LogLevel.Warning, $"The active view “{view?.Name}” cannot list model elements. Open a plan, section, elevation or 3D view, or use Load All Model Elements.");
                FillRows(new List<ElementListItem>());
                SourceText = $"Active view · {view?.Name} · not a model view";
                Subtitle = $"{DocumentTitle} · {view?.Name}";
                return;
            }
            var items = ElementListService.List(doc, view);
            FillRows(items);
            SourceText = $"Active view · {view.Name} · {items.Count} elements";
            Subtitle = $"{DocumentTitle} · Active view: {view.Name}";
            AddLog(LogLevel.Info, $"Listed {items.Count} model elements in the active view “{view.Name}” (host model only).");
        }

        private void FillRows(List<ElementListItem> items)
        {
            _levelsLoaded = false;
            _lastTarget = null;
            _bulkTick = true;
            Rows.Clear();
            foreach (var i in items)
                Rows.Add(new ElementRowViewModel(i.Id, i.Category, i.FamilyAndType, OnRowTicked, OnRowChoiceChanged));
            _bulkTick = false;
            ListedCount = Rows.Count;
            Checks.Clear();
            ChecksSummary = "Press Load Base Level to read the ticked elements and run the pre-checks.";
            ChecksBlocked = true;
            RunSummary = string.Empty;
            Reevaluate();
        }

        private void ApplyFacts(FactsResult facts)
        {
            if (facts == null) return;
            _lastTarget = facts.Target;
            var byId = Rows.ToDictionary(r => r.Id);
            foreach (ElementFacts f in facts.Elements)
                if (byId.TryGetValue(f.Id, out var row)) row.ApplyFacts(f, Format, CustomPattern);
            foreach (long id in facts.Missing)
                if (byId.TryGetValue(id, out var row)) row.MarkMissing();
        }

        /// <summary>Re-applies the rules to the last facts (no Revit call): checks, row statuses, metrics.</summary>
        private void Reevaluate()
        {
            var ticked = Rows.Where(r => r.IsChecked).ToList();
            bool blocked = true;
            if (_lastTarget != null)
            {
                var facts = ticked.Where(r => r.Facts != null).Select(r => r.Facts).ToList();
                var checks = BaseLevelRules.EvaluateChecks(_lastTarget, facts, Overwrite, ClearOld, OwnChoices(), Format, CustomPattern);
                Checks.Clear();
                foreach (var c in checks) Checks.Add(c);
                blocked = BaseLevelRules.AnyBlocked(checks);
            }

            foreach (var r in Rows)
            {
                if (!r.IsChecked) r.SetStatus(RowState.NotChecked, "Not checked");
                else if (r.State == RowState.Warning && r.Facts == null && r.Status.Contains("no longer exists")) { }
                else if (r.Facts == null || _lastTarget == null) r.SetStatus(RowState.Pending, _levelsLoaded ? "Re-check to read" : "Not read");
                else
                {
                    RowVerdict v = BaseLevelRules.EvaluateRow(r.Facts, blocked, Overwrite, r.Choice, ClearOld, _lastTarget.VariesAcrossGroups, r.FormattedValue);
                    r.SetStatus(v.State, v.Text);
                }
            }
            ChecksBlocked = blocked;
            UpdateMetrics();
        }

        private void UpdateMetrics()
        {
            var ticked = Rows.Where(r => r.IsChecked).ToList();
            CheckedCount = ticked.Count;
            ReadyCount = ticked.Count(r => r.State == RowState.Ready);
            WarningCount = ticked.Count(r => r.State == RowState.Warning);
            BlockedCount = ticked.Count(r => r.State == RowState.Blocked);
            UpToDateCount = ticked.Count(r => r.State == RowState.UpToDate);
            HasValueCount = ticked.Count(r => r.HasOldValue);
            if (_lastTarget != null)
                ChecksSummary = BaseLevelRules.Summary(ReadyCount, WarningCount, BlockedCount, UpToDateCount);
            if (!string.IsNullOrEmpty(TileFilter)) RowsView.Refresh();
            SummaryText = _lastTarget == null
                ? $"{CheckedCount} of {ListedCount} elements ticked"
                : $"Ready · {ReadyCount} element(s) will be assigned{RewriteNote(ticked)}, {WarningCount + BlockedCount + UpToDateCount} skipped";
            NotifyRun();
        }

        private static string RewriteNote(List<ElementRowViewModel> ticked)
        {
            int n = ticked.Count(r => r.State == RowState.Ready && r.HasExistingValue);
            int c = ticked.Count(r => r.State == RowState.Ready && !r.HasExistingValue && r.HasOldValue);
            var parts = new List<string>();
            if (n > 0) parts.Add($"{n} rewrite an existing value");
            if (c > 0) parts.Add($"{c} clear an old value");
            return parts.Count == 0 ? string.Empty : $" ({string.Join(", ", parts)})";
        }

        private Dictionary<long, ExistingChoice> OwnChoices() =>
            Rows.Where(r => r.Choice != ExistingChoice.Default).ToDictionary(r => r.Id, r => r.Choice);

        private void OnRowChoiceChanged(ElementRowViewModel row)
        {
            if (_bulkChoice) return;
            Reevaluate();
        }

        private void OnRowTicked(ElementRowViewModel row)
        {
            if (_bulkTick) return;
            Reevaluate();
            ScheduleRecheck();
        }

        private void SetVisibleTicks(bool value)
        {
            _bulkTick = true;
            int n = 0;
            foreach (ElementRowViewModel r in RowsView.Cast<ElementRowViewModel>())
            {
                if (r.IsChecked == value) continue;
                r.IsChecked = value;
                n++;
            }
            _bulkTick = false;
            if (n == 0) return;
            Reevaluate();
            ScheduleRecheck();
        }

        private void SetCategoryTicks(string category, bool value)
        {
            if (string.IsNullOrEmpty(category)) return;
            _bulkTick = true;
            int n = 0;
            foreach (ElementRowViewModel r in RowsView.Cast<ElementRowViewModel>())
            {
                if (r.Category != category || r.IsChecked == value) continue;
                r.IsChecked = value;
                n++;
            }
            _bulkTick = false;
            if (n == 0) return;
            Reevaluate();
            ScheduleRecheck();
        }

        private void ScheduleRecheck()
        {
            if (!_levelsLoaded) return;
            _recheckTimer.Stop();
            _recheckTimer.Start();
        }

        private TargetRequest BuildRequest() => new()
        {
            Name = TargetName?.Trim() ?? string.Empty,
            Pending = PendingNew,
            ElementIds = Rows.Where(r => r.IsChecked).Select(r => r.Id).ToList(),
            Overwrite = Overwrite,
            ClearOld = ClearOld,
            Choices = ClearOld ? new Dictionary<long, ExistingChoice>() : OwnChoices(),
            Format = Format,
            CustomPattern = CustomPattern
        };

        private bool FilterRow(object o)
        {
            if (o is not ElementRowViewModel r) return false;
            if (ChangedOnly && !r.IsChanged) return false;
            switch (TileFilter)
            {
                case "Checked": if (!r.IsChecked) return false; break;
                case "Ready": if (r.State != RowState.Ready) return false; break;
                case "UpToDate": if (r.State != RowState.UpToDate) return false; break;
                case "Warning": if (r.State != RowState.Warning) return false; break;
                case "Blocked": if (r.State != RowState.Blocked) return false; break;
                case "HasValue": if (!r.IsChecked || !r.HasOldValue) return false; break;
            }
            string f = FilterText?.Trim();
            if (string.IsNullOrEmpty(f)) return true;
            return r.Id.ToString().Contains(f)
                || Contains(r.Category, f) || Contains(r.FamilyAndType, f)
                || Contains(r.BaseLevel, f) || Contains(r.Status, f) || Contains(r.NewValue, f);
        }

        private static bool Contains(string s, string f) =>
            s != null && s.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0;

        private static string Examples(List<ElementFacts> list) =>
            string.Join(", ", list.Take(3).Select(f => $"{f.Id} {f.Category}")) + (list.Count > 3 ? ", …" : string.Empty);

        private void Log(LogLevel level, string message)
        {
            AddLog(level, message);
            SummaryText = message;
        }
    }
}
