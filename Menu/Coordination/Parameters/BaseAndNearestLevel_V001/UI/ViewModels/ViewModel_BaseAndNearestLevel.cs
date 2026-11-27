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
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Infrastructure;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.ViewModels
{
    /// <summary>
    /// View model of the Base and Nearest Level window: the roof / floor / wall list, the four target
    /// parameter slots, the nearest-level rule, the pre-checks a–g, the metrics, and the two actions
    /// (write parameters, re-host). Revit work is queued on the API thread through
    /// <see cref="BaseAndNearestEventHandler"/>; this file holds the state and the start-up, and
    /// <c>ViewModel_BaseAndNearestLevel.Actions.cs</c> holds the commands.
    /// </summary>
    public sealed partial class ViewModel_BaseAndNearestLevel : ToolViewModelBase
    {
        private readonly BaseAndNearestEventHandler _handler;
        private readonly ExternalEvent _event;
        private readonly DispatcherTimer _recheckTimer;
        private readonly List<ProjectParameterOption> _allParameters = new();
        private BaseAndNearestLevelSettings _settings;
        private string _revitSharedParamFile = string.Empty;
        private string _tileFilter = string.Empty;
        private bool _levelsRead;
        private bool _bulkTick;
        private bool _bulkChoice;

        /// <summary>Creates the view model; call <see cref="Initialize"/> on the Revit API thread before showing the window.</summary>
        public ViewModel_BaseAndNearestLevel(BaseAndNearestEventHandler handler, ExternalEvent externalEvent, string documentTitle)
        {
            _handler = handler;
            _event = externalEvent;
            DocumentTitle = documentTitle ?? string.Empty;

            foreach (TargetSlot slot in TargetSlots.All)
                Slots.Add(new SlotViewModel(slot, OnSlotNameChanged, OpenNewParameterDialog));

            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ElementRowViewModel.Category)));
            RowsView.SortDescriptions.Add(new SortDescription(nameof(ElementRowViewModel.Category), ListSortDirection.Ascending));
            RowsView.Filter = FilterRow;

            // The log starts collapsed; an error opens it so it is never missed.
            LogEntries.CollectionChanged += (_, e) =>
            {
                if (e.NewItems == null || LogExpanded) return;
                foreach (object item in e.NewItems)
                    if (item is LogEntry entry && entry.Level == LogLevel.Error) { LogExpanded = true; break; }
            };

            _recheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _recheckTimer.Tick += (_, _) =>
            {
                _recheckTimer.Stop();
                if (IsRunning) { _recheckTimer.Start(); return; }
                if (_levelsRead) ReadAndCheck(reread: false);
            };
        }

        // ── bound state ───────────────────────────────────────────────────────

        /// <summary>Title of the open document, shown under the window title.</summary>
        public string DocumentTitle { get; }

        /// <summary>Every listed roof, floor and wall.</summary>
        public ObservableCollection<ElementRowViewModel> Rows { get; } = new();

        /// <summary>Rows grouped by category, filtered by the search box and the metric tiles.</summary>
        public ICollectionView RowsView { get; }

        /// <summary>The four target parameter slots, in window order.</summary>
        public ObservableCollection<SlotViewModel> Slots { get; } = new();

        /// <summary>Pre-check lines a–g.</summary>
        public ObservableCollection<CheckRow> Checks { get; } = new();

        /// <summary>Opens the New Parameter dialog; set by the window. True when the user pressed Create.</summary>
        public Func<NewParameterViewModel, bool> ShowNewParameterDialog { get; set; }

        /// <summary>
        /// Asks before a re-host changes the model; set by the window. Returns the confirmation with
        /// <see cref="RehostConfirmation.IncludeShapeEdited"/> as the user left it, or null when cancelled.
        /// </summary>
        public Func<RehostConfirmation, RehostConfirmation> ConfirmRehost { get; set; }

        /// <summary>Asks before existing values are rewritten; set by the window. Null means go ahead.</summary>
        public Func<string, bool> ConfirmRewrite { get; set; }

        /// <summary>Rows the user highlighted in the grid; set by the window's code-behind.</summary>
        public List<ElementRowViewModel> SelectedRows { get; set; } = new();

        [ObservableProperty] private string _filterText = string.Empty;
        [ObservableProperty] private string _sourceText = string.Empty;
        [ObservableProperty] private string _subtitle = string.Empty;
        [ObservableProperty] private string _checksSummary = "Press Read Levels to read the ticked elements and run the pre-checks.";
        [ObservableProperty] private bool _checksBlocked = true;
        [ObservableProperty] private string _runSummary = string.Empty;
        [ObservableProperty] private string _levelSummary = string.Empty;
        [ObservableProperty] private bool _overwrite;

        [ObservableProperty] private int _listedCount;
        [ObservableProperty] private int _checkedCount;
        [ObservableProperty] private int _readyCount;
        [ObservableProperty] private int _upToDateCount;
        [ObservableProperty] private int _hasValueCount;
        [ObservableProperty] private int _warningCount;
        [ObservableProperty] private int _blockedCount;
        [ObservableProperty] private int _changedCount;
        [ObservableProperty] private int _rehostableCount;

        /// <summary>Roofs are listed.</summary>
        [ObservableProperty] private bool _includeRoofs = true;

        /// <summary>Floors are listed.</summary>
        [ObservableProperty] private bool _includeFloors = true;

        /// <summary>Walls are listed.</summary>
        [ObservableProperty] private bool _includeWalls = true;

        /// <summary>Only levels whose "Building Story" parameter is ticked are candidates.</summary>
        [ObservableProperty] private bool _buildingStoreysOnly = true;

        /// <summary>Differences below this many millimetres count as zero.</summary>
        [ObservableProperty] private double _toleranceMm = 1.0;

        /// <summary>Re-host shape-edited floors and roofs as well. Not remembered; always starts off.</summary>
        [ObservableProperty] private bool _rehostShapeEdited;

        /// <summary>The "Target parameters" card is open (remembered).</summary>
        [ObservableProperty] private bool _targetExpanded = true;

        /// <summary>The "Nearest level" card is open (remembered).</summary>
        [ObservableProperty] private bool _nearestExpanded = true;

        /// <summary>The "Pre-checks" card is open (remembered).</summary>
        [ObservableProperty] private bool _checksExpanded = true;

        /// <summary>The activity log is open (collapsed by default, remembered).</summary>
        [ObservableProperty] private bool _logExpanded;

        private NearestRule _rule = NearestRule.ClosestEitherWay;

        /// <summary>Which level the nearest-level search prefers.</summary>
        public NearestRule Rule
        {
            get => _rule;
            set
            {
                if (_rule == value) return;
                _rule = value;
                OnPropertyChanged(nameof(Rule));
                OnPropertyChanged(nameof(RuleEitherWay));
                OnPropertyChanged(nameof(RuleAtOrBelow));
                OnPropertyChanged(nameof(RuleAtOrAbove));
                OnPropertyChanged(nameof(RuleLabel));
                ScheduleRecheck();
            }
        }

        /// <summary>"Closest in either direction" radio button. Setting false is ignored.</summary>
        public bool RuleEitherWay
        {
            get => Rule == NearestRule.ClosestEitherWay;
            set { if (value) Rule = NearestRule.ClosestEitherWay; }
        }

        /// <summary>"Closest at or below" radio button. Setting false is ignored.</summary>
        public bool RuleAtOrBelow
        {
            get => Rule == NearestRule.ClosestAtOrBelow;
            set { if (value) Rule = NearestRule.ClosestAtOrBelow; }
        }

        /// <summary>"Closest at or above" radio button. Setting false is ignored.</summary>
        public bool RuleAtOrAbove
        {
            get => Rule == NearestRule.ClosestAtOrAbove;
            set { if (value) Rule = NearestRule.ClosestAtOrAbove; }
        }

        /// <summary>The rule, as the card's collapsed header shows it.</summary>
        public string RuleLabel => NearestLevelCalculator.RuleLabel(Rule);

        /// <summary>"Existing values: Skip" radio button. Setting false is ignored.</summary>
        public bool SkipExisting
        {
            get => !Overwrite;
            set { if (value) Overwrite = false; }
        }

        /// <summary>"Existing values: Rewrite" radio button. Setting false is ignored.</summary>
        public bool RewriteExisting
        {
            get => Overwrite;
            set { if (value) Overwrite = true; }
        }

        /// <summary>True when Write Parameters can start.</summary>
        public bool CanWrite => !IsRunning && _levelsRead && !ChecksBlocked && ReadyCount > 0;

        /// <summary>True when Re-host can start: levels read and at least one element can move.</summary>
        public bool CanRehost => !IsRunning && _levelsRead && RehostableCount > 0;

        /// <summary>True when the levels have been read, so the grid holds facts.</summary>
        public bool LevelsRead => _levelsRead;

        // ── reactions ─────────────────────────────────────────────────────────

        partial void OnFilterTextChanged(string value) => RowsView.Refresh();

        partial void OnOverwriteChanged(bool value)
        {
            OnPropertyChanged(nameof(SkipExisting));
            OnPropertyChanged(nameof(RewriteExisting));
            Reevaluate();
        }

        partial void OnBuildingStoreysOnlyChanged(bool value) => ScheduleRecheck();
        partial void OnToleranceMmChanged(double value) => ScheduleRecheck();
        partial void OnRehostShapeEditedChanged(bool value) => Reevaluate();
        partial void OnChecksBlockedChanged(bool value) => NotifyActions();
        partial void OnReadyCountChanged(int value) => NotifyActions();
        partial void OnRehostableCountChanged(int value) => NotifyActions();
        partial void OnIncludeRoofsChanged(bool value) => RowsView.Refresh();
        partial void OnIncludeFloorsChanged(bool value) => RowsView.Refresh();
        partial void OnIncludeWallsChanged(bool value) => RowsView.Refresh();

        /// <inheritdoc/>
        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.PropertyName == nameof(IsRunning)) NotifyActions();
        }

        private void NotifyActions()
        {
            OnPropertyChanged(nameof(CanWrite));
            OnPropertyChanged(nameof(CanRehost));
            WriteParametersCommand.NotifyCanExecuteChanged();
            RehostCommand.NotifyCanExecuteChanged();
            ReadLevelsCommand.NotifyCanExecuteChanged();
        }

        private void OnSlotNameChanged(SlotViewModel slot)
        {
            slot.Describe(_allParameters);
            ScheduleRecheck();
        }

        private void ScheduleRecheck()
        {
            if (!_levelsRead) return;
            _recheckTimer.Stop();
            _recheckTimer.Start();
        }

        // ── filtering ─────────────────────────────────────────────────────────

        private bool FilterRow(object item)
        {
            if (item is not ElementRowViewModel row) return false;

            if (!CategoryAllowed(row.Category)) return false;

            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                string needle = FilterText.Trim();
                bool hit = row.FamilyAndType.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0
                           || row.Id.ToString().Contains(needle)
                           || row.BaseLevel.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0
                           || row.NearestLevel.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0;
                if (!hit) return false;
            }

            return _tileFilter switch
            {
                "Checked" => row.IsChecked,
                "Ready" => row.State == RowState.Ready,
                "UpToDate" => row.State == RowState.UpToDate,
                "HasValue" => row.HasExistingValue,
                "Warning" => row.State is RowState.Warning or RowState.Skipped,
                "Blocked" => row.State is RowState.Blocked or RowState.Failed,
                "Changed" => row.IsChanged,
                _ => true
            };
        }

        private bool CategoryAllowed(string category) => category switch
        {
            "Roofs" => IncludeRoofs,
            "Floors" => IncludeFloors,
            "Walls" => IncludeWalls,
            _ => true
        };

        // ── start-up and shutdown ─────────────────────────────────────────────

        /// <summary>
        /// Lists the active view's roofs, floors and walls and the project parameters. Runs inside the
        /// command, on the Revit API thread.
        /// </summary>
        public void Initialize(Document doc, View activeView)
        {
            _settings = SettingsService<BaseAndNearestLevelSettings>.Load(ToolCatalog.BaseAndNearestLevel.Name);
            _revitSharedParamFile = doc.Application.SharedParametersFilename ?? string.Empty;
            RestoreSettings();
            LoadParameters(doc);
            ListFromView(doc, activeView);
            SummaryText = "Ready.";
            AddLog(LogLevel.Info, $"{ToolCatalog.BaseAndNearestLevel.Title} ready. Roofs, floors and walls of the host model only.");
        }

        /// <summary>Saves the remembered settings; called when the window closes.</summary>
        public void OnWindowClosing()
        {
            _recheckTimer.Stop();
            _settings ??= new BaseAndNearestLevelSettings();
            _settings.SlotNames = Slots.Select(s => s.Name ?? string.Empty).ToArray();
            _settings.NearestRule = Rule.ToString();
            _settings.BuildingStoreysOnly = BuildingStoreysOnly;
            _settings.ToleranceMm = ToleranceMm;
            _settings.TargetExpanded = TargetExpanded;
            _settings.NearestExpanded = NearestExpanded;
            _settings.ChecksExpanded = ChecksExpanded;
            _settings.LogExpanded = LogExpanded;
            _settings.Version = 1;
            SettingsService<BaseAndNearestLevelSettings>.Save(ToolCatalog.BaseAndNearestLevel.Name, _settings);
        }

        private void RestoreSettings()
        {
            if (Enum.TryParse(_settings.NearestRule, out NearestRule saved) && Enum.IsDefined(typeof(NearestRule), saved))
                _rule = saved;
            BuildingStoreysOnly = _settings.BuildingStoreysOnly;
            ToleranceMm = _settings.ToleranceMm > 0 ? _settings.ToleranceMm : 1.0;
            TargetExpanded = _settings.TargetExpanded;
            NearestExpanded = _settings.NearestExpanded;
            ChecksExpanded = _settings.ChecksExpanded;
            LogExpanded = _settings.LogExpanded;
            OnPropertyChanged(nameof(Rule));
            OnPropertyChanged(nameof(RuleEitherWay));
            OnPropertyChanged(nameof(RuleAtOrBelow));
            OnPropertyChanged(nameof(RuleAtOrAbove));
            OnPropertyChanged(nameof(RuleLabel));
        }

        private void LoadParameters(Document doc)
        {
            _allParameters.Clear();
            _allParameters.AddRange(TargetParameterService.ListProjectParameters(doc));

            string[] remembered = _settings?.SlotNames ?? Array.Empty<string>();
            foreach (SlotViewModel slot in Slots)
            {
                slot.SetOptions(_allParameters);
                int i = (int)slot.Slot;
                string name = i < remembered.Length ? remembered[i] : string.Empty;
                if (!string.IsNullOrWhiteSpace(name) && slot.Options.Any(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)))
                    slot.Name = name;
                else
                    slot.Describe(_allParameters);
            }
        }

        // ── helpers shared with the commands ──────────────────────────────────

        private void Log(LogLevel level, string message) => AddLog(level, message);

        private RunRequest BuildRequest() => new()
        {
            ElementIds = Rows.Where(r => r.IsChecked && CategoryAllowed(r.Category)).Select(r => r.Id).ToList(),
            SlotNames = Slots.Select(s => (s.Name ?? string.Empty).Trim()).ToArray(),
            Pending = Slots.Select(s => s.Pending).ToArray(),
            Rule = Rule,
            BuildingStoreysOnly = BuildingStoreysOnly,
            ToleranceMm = ToleranceMm <= 0 ? 1.0 : ToleranceMm,
            Overwrite = Overwrite,
            Choices = Rows.Where(r => r.Choice != ExistingChoice.Default).ToDictionary(r => r.Id, r => r.Choice),
            RehostShapeEdited = RehostShapeEdited
        };

        private void FillRows(IEnumerable<ElementListItem> items)
        {
            Rows.Clear();
            foreach (ElementListItem item in items)
                Rows.Add(new ElementRowViewModel(item.Id, item.Category, item.FamilyAndType, OnRowTicked, OnRowChoiceChanged));
            _levelsRead = false;
            Checks.Clear();
            ChecksSummary = "Press Read Levels to read the ticked elements and run the pre-checks.";
            ChecksBlocked = true;
            LevelSummary = string.Empty;
            RunSummary = string.Empty;
            UpdateMetrics();
            RowsView.Refresh();
            NotifyActions();
        }

        private void OnRowTicked(ElementRowViewModel row)
        {
            if (_bulkTick) return;
            UpdateMetrics();
            ScheduleRecheck();
        }

        private void OnRowChoiceChanged(ElementRowViewModel row)
        {
            if (_bulkChoice) return;
            Reevaluate();
        }

        /// <summary>Re-runs the row verdicts over the facts already read, with no Revit call.</summary>
        private void Reevaluate()
        {
            if (!_levelsRead) return;
            RunRequest request = BuildRequest();
            foreach (ElementRowViewModel row in Rows)
            {
                if (row.Facts == null) continue;
                row.Facts.RehostBlock = BaseAndNearestRules.RehostBlockFor(row.Facts, RehostShapeEdited);
                row.Apply(row.Facts, BaseAndNearestRules.EvaluateRow(row.Facts, request.Overwrite, request.ChoiceFor(row.Id)));
            }
            UpdateMetrics();
            RowsView.Refresh();
        }

        private void UpdateMetrics()
        {
            ListedCount = Rows.Count(r => CategoryAllowed(r.Category));
            var live = Rows.Where(r => CategoryAllowed(r.Category)).ToList();
            CheckedCount = live.Count(r => r.IsChecked);
            ReadyCount = live.Count(r => r.IsChecked && r.State == RowState.Ready);
            UpToDateCount = live.Count(r => r.IsChecked && r.State == RowState.UpToDate);
            HasValueCount = live.Count(r => r.IsChecked && r.HasExistingValue);
            WarningCount = live.Count(r => r.IsChecked && r.State is RowState.Warning or RowState.Skipped);
            BlockedCount = live.Count(r => r.IsChecked && r.State is RowState.Blocked or RowState.Failed);
            ChangedCount = live.Count(r => r.IsChecked && r.IsChanged);
            RehostableCount = live.Count(r => r.IsChecked && r.Facts != null &&
                BaseAndNearestRules.RehostBlockFor(r.Facts, RehostShapeEdited) == RehostSkip.None);
        }
    }
}
