using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.RoofEdgeAroundSections.V007
{
    /// <summary>
    /// Presents one enabled/disabled, reorderable naming token as a bindable chip
    /// for the Section Naming Pattern card. Wraps a NamingToken so the UI can toggle
    /// IsEnabled and move order without mutating settings directly on every click.
    /// </summary>
    public partial class NamingTokenChipViewModel : ObservableObject
    {
        public NamingTokenType Type { get; }
        public string DisplayLabel { get; }

        [ObservableProperty]
        private bool isEnabled;

        [ObservableProperty]
        private int order;

        public NamingTokenChipViewModel(NamingTokenType type, string displayLabel, bool isEnabled, int order)
        {
            Type = type;
            DisplayLabel = displayLabel;
            this.isEnabled = isEnabled;
            this.order = order;
        }
    }

    /// <summary>
    /// View model of the Roof Edge Around Sections window: parameters, naming pattern,
    /// planned-sections grid, Run (locked after a run until Refresh or a setting change)
    /// and the "Open created sections" popover.
    /// </summary>
    public partial class RoofEdgeSectionsViewModel : ObservableObject
    {
        private readonly Document _doc;
        private readonly ExternalEvent _externalEvent;
        private readonly RoofEdgeSectionsEventHandler _handler;
        private readonly IList<Element> _selectedRoofs;
        private readonly IList<Element> _skippedNonRoofElements;
        private readonly RoofEdgeSectionsSettings _settings;

        /// <summary>Rows sent to the last Run, used to label the created sections in the popover.</summary>
        private List<PlannedSection> _lastRunRows = new();

        public ObservableCollection<PlannedSection> PlannedSections { get; } = new();
        public ObservableCollection<LogEntry> LogEntries { get; } = new();
        public ObservableCollection<string> ViewTemplateNames { get; } = new() { "None" };
        public ObservableCollection<NamingTokenChipViewModel> NamingTokenChips { get; } = new();

        /// <summary>Sections created by the last Run, listed in the "Open created sections" popover.</summary>
        public ObservableCollection<CreatedSectionItem> CreatedSections { get; } = new();

        /// <summary><see cref="CreatedSections"/> filtered by <see cref="CreatedSectionsFilter"/>, bound to the popover list.</summary>
        public System.ComponentModel.ICollectionView CreatedSectionsView { get; }

        /// <summary>
        /// True after a run that created sections. Run stays disabled until Refresh or a
        /// setting change, so the same edges are not created twice.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private bool isRunLocked;

        /// <summary>True while the "Open created sections" popover is shown.</summary>
        [ObservableProperty]
        private bool isOpenSectionsPopoverOpen;

        /// <summary>Search text for the popover list (matches name, roof or direction).</summary>
        [ObservableProperty]
        private string createdSectionsFilter = "";

        /// <summary>Number of ticked rows in the popover.</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OpenSelectedSectionsCommand))]
        private int selectedCreatedCount;

        [ObservableProperty]
        private double belowRoofMm;

        [ObservableProperty]
        private double aboveRoofMm;

        [ObservableProperty]
        private double sectionFarLengthMm;

        [ObservableProperty]
        private double marginOutwardMm;

        [ObservableProperty]
        private double lengthInsideRoofMm;

        [ObservableProperty]
        private string selectedViewTemplateName;

        [ObservableProperty]
        private string openViewsMode; // "AskMe" / "OpenAll" / "DontOpen"

        [ObservableProperty]
        private bool mergeEnabled;

        [ObservableProperty]
        private double mergeDistanceMm;

        [ObservableProperty]
        private string namingSeparator;

        [ObservableProperty]
        private string namingPreviewText = "";

        // Selection Summary — 5 metrics
        [ObservableProperty]
        private int totalRoofsCount;

        [ObservableProperty]
        private int sectionsDetectedCount;

        [ObservableProperty]
        private int sectionsSuggestedCount;

        [ObservableProperty]
        private int finallyCreatedCount;

        [ObservableProperty]
        private int skippedCount;

        [ObservableProperty]
        private bool isBusy;

        [ObservableProperty]
        private string lastRunSummary = "—";

        // ToolWindowShell status strip (CLAUDE.md v1.1)
        [ObservableProperty]
        private double progress;

        public RoofEdgeSectionsViewModel(
            Document doc,
            ExternalEvent externalEvent,
            RoofEdgeSectionsEventHandler handler,
            IList<Element> selectedRoofs,
            IList<Element> skippedNonRoofElements)
        {
            _doc = doc;
            _externalEvent = externalEvent;
            _handler = handler;
            _selectedRoofs = selectedRoofs;
            _skippedNonRoofElements = skippedNonRoofElements;

            _settings = SettingsService.Load();
            belowRoofMm = _settings.BelowRoofMm;
            aboveRoofMm = _settings.AboveRoofMm;
            sectionFarLengthMm = _settings.SectionFarLengthMm;
            marginOutwardMm = _settings.MarginOutwardMm;
            lengthInsideRoofMm = _settings.LengthInsideRoofMm;
            selectedViewTemplateName = _settings.ViewTemplateName;
            openViewsMode = _settings.OpenViewsMode;
            mergeEnabled = _settings.MergeEnabled;
            mergeDistanceMm = _settings.MergeDistanceMm;
            namingSeparator = _settings.NamingSeparator;

            LoadViewTemplates();
            LoadNamingTokenChips();
            UpdateNamingPreview();

            CreatedSectionsView = System.Windows.Data.CollectionViewSource.GetDefaultView(CreatedSections);
            CreatedSectionsView.Filter = MatchesCreatedSectionsFilter;

            // Every handler action refuses to run while another project is active.
            _handler.TargetDocument = _doc;
            _handler.OnPlanBuilt = OnPlanBuilt;
            _handler.OnRunComplete = OnRunComplete;
            _handler.OnViewsOpened = OnViewsOpened;

            RequestBuildPlan();
        }

        private void LoadViewTemplates()
        {
            ViewTemplateNames.Clear();
            ViewTemplateNames.Add("None");
            var templates = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .Select(v => v.Name)
                .OrderBy(n => n);

            foreach (string name in templates)
                ViewTemplateNames.Add(name);

            if (!ViewTemplateNames.Contains(selectedViewTemplateName))
                selectedViewTemplateName = "None";
        }

        private static readonly Dictionary<NamingTokenType, string> TokenLabels = new()
        {
            [NamingTokenType.Zone] = "Zone",
            [NamingTokenType.Level] = "Level",
            [NamingTokenType.Name] = "Name",
            [NamingTokenType.LineOfDirection] = "Line of Direction",
            [NamingTokenType.Area] = "Area",
            [NamingTokenType.Number] = "Number"
        };

        private void LoadNamingTokenChips()
        {
            NamingTokenChips.Clear();

            // Defensive: settings.NamingTokens comes from a deserialized JSON file that could
            // in principle be hand-edited or stale from a future/older schema. A missing or
            // unrecognized token would otherwise throw here and prevent the window from
            // opening at all — fall back to NamingToken.Defaults() if the loaded set looks
            // invalid, and use a safe label lookup per-token as a second line of defense.
            List<NamingToken> tokensToLoad = _settings.NamingTokens != null && _settings.NamingTokens.Count > 0
                ? _settings.NamingTokens
                : NamingToken.Defaults();

            foreach (NamingToken token in tokensToLoad.OrderBy(t => t.Order))
            {
                string label = TokenLabels.TryGetValue(token.Type, out string knownLabel)
                    ? knownLabel
                    : token.Type.ToString(); // safe fallback — still renders, just less pretty

                var chip = new NamingTokenChipViewModel(token.Type, label, token.IsEnabled, token.Order);
                chip.PropertyChanged += (s, e) =>
                {
                    UpdateNamingPreview();
                    UnlockRun();
                };
                NamingTokenChips.Add(chip);
            }
        }

        [RelayCommand]
        private void ToggleNamingToken(NamingTokenChipViewModel chip)
        {
            if (chip == null) return;
            chip.IsEnabled = !chip.IsEnabled;
            UpdateNamingPreview();
        }

        /// <summary>
        /// Builds a sample preview string from the current token chip state, using
        /// representative placeholder values (not a real roof) so the pattern can be
        /// previewed before a plan is built.
        /// </summary>
        private void UpdateNamingPreview()
        {
            var parts = new List<string>();
            foreach (var chip in NamingTokenChips.Where(c => c.IsEnabled).OrderBy(c => c.Order))
            {
                string sample = chip.Type switch
                {
                    NamingTokenType.Zone => "ZoneA",
                    NamingTokenType.Level => "Level1",
                    NamingTokenType.Name => "Roof123",
                    NamingTokenType.LineOfDirection => "Line of North",
                    NamingTokenType.Area => "120m2",
                    NamingTokenType.Number => "01",
                    _ => ""
                };
                parts.Add(sample);
            }

            NamingPreviewText = string.Join(string.IsNullOrEmpty(NamingSeparator) ? "_" : NamingSeparator, parts)
                .Replace(" ", string.IsNullOrEmpty(NamingSeparator) ? "_" : NamingSeparator);
        }

        partial void OnNamingSeparatorChanged(string value)
        {
            UpdateNamingPreview();
            UnlockRun();
        }

        // Any setting that changes what Run creates turns Run back on after a run.
        // Open Views mode is left out: it does not change the sections.
        partial void OnBelowRoofMmChanged(double value) => UnlockRun();
        partial void OnAboveRoofMmChanged(double value) => UnlockRun();
        partial void OnSectionFarLengthMmChanged(double value) => UnlockRun();
        partial void OnMarginOutwardMmChanged(double value) => UnlockRun();
        partial void OnLengthInsideRoofMmChanged(double value) => UnlockRun();
        partial void OnSelectedViewTemplateNameChanged(string value) => UnlockRun();
        partial void OnMergeEnabledChanged(bool value) => UnlockRun();
        partial void OnMergeDistanceMmChanged(double value) => UnlockRun();

        partial void OnIsBusyChanged(bool value)
        {
            RunCommand.NotifyCanExecuteChanged();
            OpenSelectedSectionsCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Turns Run back on after a run (Refresh or a setting change).</summary>
        private void UnlockRun() => IsRunLocked = false;

        /// <summary>Builds a settings snapshot from current bindable properties, including naming tokens.</summary>
        private RoofEdgeSectionsSettings BuildSettingsSnapshot()
        {
            return new RoofEdgeSectionsSettings
            {
                BelowRoofMm = BelowRoofMm,
                AboveRoofMm = AboveRoofMm,
                SectionFarLengthMm = SectionFarLengthMm,
                MarginOutwardMm = MarginOutwardMm,
                LengthInsideRoofMm = LengthInsideRoofMm,
                ViewTemplateName = SelectedViewTemplateName,
                OpenViewsMode = OpenViewsMode,
                MergeEnabled = MergeEnabled,
                MergeDistanceMm = MergeDistanceMm,
                NamingSeparator = NamingSeparator,
                NamingTokens = NamingTokenChips
                    .Select(c => new NamingToken { Type = c.Type, IsEnabled = c.IsEnabled, Order = c.Order })
                    .ToList(),
                LastLogExportFolder = _settings.LastLogExportFolder
            };
        }

        private void RequestBuildPlan()
        {
            IsBusy = true;
            _handler.RequestedAction = RoofEdgeSectionsAction.BuildPlan;
            _handler.SelectedRoofs = _selectedRoofs;
            _handler.SkippedNonRoofElements = _skippedNonRoofElements;
            _handler.Settings = BuildSettingsSnapshot(); // needed pre-Run too: merge + naming both read settings during plan-build
            _externalEvent.Raise();
        }

        private void OnPlanBuilt(SectionPlanBuilder.PlanBuildResult result, ObservableCollection<LogEntry> log)
        {
            PlannedSections.Clear();
            foreach (var row in result.Plan)
                PlannedSections.Add(row);

            foreach (var entry in log)
                LogEntries.Add(entry);

            TotalRoofsCount = result.TotalRoofsCount;
            SectionsDetectedCount = result.DetectedCount;
            SectionsSuggestedCount = result.SuggestedCount;

            IsBusy = false;
            RunCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void SelectAll()
        {
            foreach (var row in PlannedSections.Where(r => r.Status == PlannedSectionStatus.Ready))
                row.IsIncluded = true;
        }

        [RelayCommand]
        private void ClearAll()
        {
            foreach (var row in PlannedSections)
                row.IsIncluded = false;
        }

        [RelayCommand]
        private void Refresh()
        {
            UnlockRun();
            RequestBuildPlan();
        }

        private bool CanRun() => !IsBusy && !IsRunLocked
            && PlannedSections.Any(r => r.IsIncluded && r.Status == PlannedSectionStatus.Ready);

        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            IsBusy = true;

            var settingsSnapshot = BuildSettingsSnapshot();

            ViewTemplateOption templateOption = new() { Name = SelectedViewTemplateName };
            if (SelectedViewTemplateName != "None")
            {
                var templateView = new FilteredElementCollector(_doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => v.IsTemplate && v.Name == SelectedViewTemplateName);
                if (templateView != null)
                    templateOption.TemplateId = templateView.Id;
            }

            _lastRunRows = PlannedSections.Where(r => r.IsIncluded && r.Status == PlannedSectionStatus.Ready).ToList();

            _handler.RequestedAction = RoofEdgeSectionsAction.RunCreate;
            _handler.Settings = settingsSnapshot;
            _handler.RowsToProcess = _lastRunRows;
            _handler.ViewTemplate = templateOption;

            SettingsService.Save(settingsSnapshot);

            _externalEvent.Raise();
        }

        private void OnRunComplete(RunResult result, ObservableCollection<LogEntry> log)
        {
            foreach (var entry in log)
                LogEntries.Add(entry);

            LastRunSummary = result.SummaryLine;
            FinallyCreatedCount = result.CreatedCount;
            SkippedCount = result.SkippedCount;

            // The plan rebuild below finds the same edges again (renamed, since their views
            // now exist), so Run stays off until Refresh or a setting change. A run that
            // created nothing can be retried straight away.
            if (result.CreatedCount > 0)
                IsRunLocked = true;

            IsBusy = false;
            RunCommand.NotifyCanExecuteChanged();

            if (result.CreatedViewIds.Count > 0)
            {
                if (OpenViewsMode == "OpenAll")
                    RequestOpenViewsIfNeeded?.Invoke(result.CreatedViewIds, OpenViewsMode);
                else if (OpenViewsMode != "DontOpen")
                    ShowOpenSectionsPopover(result.CreatedViewIds); // "AskMe" (and any unknown value)
            }

            // Rebuild the plan so the Selection Summary reflects the post-Run state.
            RequestBuildPlan();
        }

        /// <summary>
        /// Raised in "Open All" mode so the View opens every created section. Runs inside the
        /// external event, so the views can be activated directly.
        /// </summary>
        public Action<List<ElementId>, string> RequestOpenViewsIfNeeded { get; set; }

        /// <summary>
        /// Fills the popover with the sections just created (all unticked) and shows it.
        /// Called before the plan rebuild, while the run's rows are still known.
        /// </summary>
        private void ShowOpenSectionsPopover(List<ElementId> createdViewIds)
        {
            var rowsByName = _lastRunRows
                .Where(r => !string.IsNullOrEmpty(r.SectionViewName))
                .GroupBy(r => r.SectionViewName)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (CreatedSectionItem old in CreatedSections)
                old.PropertyChanged -= OnCreatedSectionChanged;
            CreatedSections.Clear();
            CreatedSectionsFilter = "";

            foreach (ElementId id in createdViewIds)
            {
                string name = (_doc.GetElement(id) as View)?.Name ?? $"View {id.Value}";
                rowsByName.TryGetValue(name, out PlannedSection row);

                var item = new CreatedSectionItem(id, name,
                    row?.RoofDisplayName ?? "—",
                    row?.Direction.ToString() ?? "—");
                item.PropertyChanged += OnCreatedSectionChanged;
                CreatedSections.Add(item);
            }

            UpdateSelectedCreatedCount();
            IsOpenSectionsPopoverOpen = true;
        }

        private void OnCreatedSectionChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CreatedSectionItem.IsSelected))
                UpdateSelectedCreatedCount();
        }

        private void UpdateSelectedCreatedCount() =>
            SelectedCreatedCount = CreatedSections.Count(i => i.IsSelected);

        partial void OnCreatedSectionsFilterChanged(string value) => CreatedSectionsView?.Refresh();

        private bool MatchesCreatedSectionsFilter(object obj)
        {
            if (string.IsNullOrWhiteSpace(CreatedSectionsFilter)) return true;
            if (obj is not CreatedSectionItem item) return false;

            string text = CreatedSectionsFilter.Trim();
            return item.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || item.RoofDisplayName.Contains(text, StringComparison.OrdinalIgnoreCase)
                || item.Direction.Contains(text, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Ticks every row currently shown in the popover (respects the search).</summary>
        [RelayCommand]
        private void SelectAllCreated()
        {
            foreach (CreatedSectionItem item in CreatedSectionsView.Cast<CreatedSectionItem>())
                item.IsSelected = true;
        }

        /// <summary>Unticks every row currently shown in the popover (respects the search).</summary>
        [RelayCommand]
        private void ClearCreated()
        {
            foreach (CreatedSectionItem item in CreatedSectionsView.Cast<CreatedSectionItem>())
                item.IsSelected = false;
        }

        private bool CanOpenSelectedSections() => !IsBusy && SelectedCreatedCount > 0;

        /// <summary>Opens the ticked sections through the external event (Revit API context).</summary>
        [RelayCommand(CanExecute = nameof(CanOpenSelectedSections))]
        private void OpenSelectedSections()
        {
            List<ElementId> ids = CreatedSections.Where(i => i.IsSelected).Select(i => i.ViewId).ToList();
            if (ids.Count == 0) return;

            IsBusy = true;
            _handler.RequestedAction = RoofEdgeSectionsAction.OpenViews;
            _handler.ViewIdsToOpen = ids;
            _externalEvent.Raise();
        }

        /// <summary>Hides the popover without opening anything.</summary>
        [RelayCommand]
        private void CloseOpenSectionsPopover() => IsOpenSectionsPopoverOpen = false;

        private void OnViewsOpened(int opened, int failed, ObservableCollection<LogEntry> log)
        {
            foreach (var entry in log)
                LogEntries.Add(entry);

            IsBusy = false;

            // Keep the popover up when nothing opened, so the user can see what was picked.
            if (opened > 0)
                IsOpenSectionsPopoverOpen = false;
        }

        [RelayCommand]
        private void CopyAllLogs()
        {
            CopyLogsRequested?.Invoke(LogEntries.ToList());
        }

        [RelayCommand]
        private void CopySelectedLogs(IList<object> selectedItems)
        {
            var entries = selectedItems?.Cast<LogEntry>().ToList() ?? new List<LogEntry>();
            CopyLogsRequested?.Invoke(entries);
        }

        [RelayCommand]
        private void ClearLogs()
        {
            LogEntries.Clear();
        }

        public Action<List<LogEntry>> CopyLogsRequested { get; set; }

        [RelayCommand]
        private void ExportLogs()
        {
            ExportLogsRequested?.Invoke(LogEntries.ToList(), _settings.LastLogExportFolder);
        }

        public Action<List<LogEntry>, string> ExportLogsRequested { get; set; }

        public void OnWindowClosing()
        {
            var settingsSnapshot = BuildSettingsSnapshot();
            SettingsService.Save(settingsSnapshot);

            if (LogEntries.Count > 0 && !string.IsNullOrWhiteSpace(_settings.LastLogExportFolder))
            {
                try { LogExportHelper.Export(LogEntries, _settings.LastLogExportFolder); }
                catch { /* auto-save on close is best-effort, never blocks closing */ }
            }
        }
    }
}
