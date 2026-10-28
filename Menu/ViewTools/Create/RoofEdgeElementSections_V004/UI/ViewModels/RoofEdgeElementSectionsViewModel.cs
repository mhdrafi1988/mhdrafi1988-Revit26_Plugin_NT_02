using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofEdgeElementSections.V004
{
    /// <summary>
    /// Presents one enabled/disabled, reorderable naming token as a bindable chip
    /// for the Section Naming Pattern card. Same pattern as
    /// RoofEdgeAroundSections_V004's NamingTokenChipViewModel.
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

    public partial class RoofEdgeElementSectionsViewModel : ObservableObject
    {
        private readonly Document _doc;
        private readonly UIDocument _uiDoc;
        private readonly ExternalEvent _externalEvent;
        private readonly RoofEdgeElementSectionsEventHandler _handler;

        // Runs Pick Roofs' PickObjects in Revit's API context (a modeless window's click is outside it).
        private readonly RevitApiQueue _pickQueue;
        private readonly RoofEdgeElementSectionsSettings _settings;
        private readonly LinkService _linkService = new();

        private List<LinkedElementRow> _allElementRows = new();
        private ICollectionView _elementRowsView;

        /// <summary>
        /// Flat, sortable/searchable/filterable view over every Category/Family/Type
        /// leaf across the checked links — the grid's ItemsSource. Click a column
        /// header to sort (e.g. Type, Count); ElementSearchText and
        /// SelectedCategoryFilter narrow which rows are visible without altering
        /// which are checked.
        /// </summary>
        public ICollectionView ElementRowsView => _elementRowsView;

        public ObservableCollection<SelectedRoofItem> SelectedRoofs { get; } = new();
        public ObservableCollection<LinkedModelItem> LinkedModels { get; } = new();
        public ObservableCollection<LinkTreeNode> ElementTree { get; } = new();

        /// <summary>
        /// Every distinct category currently in the tree, each with its own checkbox for the
        /// Category Filter popover. An empty selection (nothing checked) means "no filter" —
        /// all categories show. <see cref="CategoryFilterOptionsView"/> narrows this list live
        /// as the user types in the popover's own search box.
        /// </summary>
        public ObservableCollection<CategoryFilterOption> CategoryFilterOptions { get; } = new();
        private ICollectionView _categoryFilterOptionsView;
        public ICollectionView CategoryFilterOptionsView => _categoryFilterOptionsView;
        public ObservableCollection<PlannedSection> PlannedSections { get; } = new();
        public ObservableCollection<LogEntry> LogEntries { get; } = new();
        public ObservableCollection<string> ViewTemplateNames { get; } = new() { "None" };
        public ObservableCollection<NamingTokenChipViewModel> NamingTokenChips { get; } = new();

        public int SelectedLinkCount => LinkedModels.Count(l => l.IsSelected);

        [ObservableProperty]
        private double edgeProximityToleranceMm;

        [ObservableProperty]
        private double sameSideDedupToleranceMm;

        [ObservableProperty]
        private double belowRoofMm;

        [ObservableProperty]
        private double aboveRoofMm;

        [ObservableProperty]
        private double farClipMm;

        [ObservableProperty]
        private double marginOutwardMm;

        [ObservableProperty]
        private double lengthInsideRoofMm;

        [ObservableProperty]
        private string selectedViewTemplateName;

        [ObservableProperty]
        private string openViewsMode; // "AskMe" / "OpenAll" / "DontOpen"

        [ObservableProperty]
        private string namingSeparator;

        [ObservableProperty]
        private string namingPreviewText = "";

        [ObservableProperty]
        private string elementSearchText = "";

        [ObservableProperty]
        private bool isCategoryFilterOpen;

        [ObservableProperty]
        private string categoryFilterSearchText = "";

        [ObservableProperty]
        private string categoryFilterSummaryText = "All Categories";

        // Selection Summary metrics
        [ObservableProperty]
        private int totalRoofsCount;

        [ObservableProperty]
        private int detectedElementCount;

        [ObservableProperty]
        private int suggestedSectionCount;

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

        public RoofEdgeElementSectionsViewModel(
            Document doc,
            UIDocument uiDoc,
            ExternalEvent externalEvent,
            RoofEdgeElementSectionsEventHandler handler)
        {
            _doc = doc;
            _uiDoc = uiDoc;
            _externalEvent = externalEvent;
            _handler = handler;
            _pickQueue = new RevitApiQueue("Roof Edge Element Sections V004 — Pick Roofs");

            _settings = SettingsService.Load();
            edgeProximityToleranceMm = _settings.EdgeProximityToleranceMm;
            sameSideDedupToleranceMm = _settings.SameSideDedupToleranceMm;
            belowRoofMm = _settings.BelowRoofMm;
            aboveRoofMm = _settings.AboveRoofMm;
            farClipMm = _settings.FarClipMm;
            marginOutwardMm = _settings.MarginOutwardMm;
            lengthInsideRoofMm = _settings.LengthInsideRoofMm;
            selectedViewTemplateName = _settings.ViewTemplateName;
            openViewsMode = _settings.OpenViewsMode;
            namingSeparator = _settings.NamingSeparator;

            LoadViewTemplates();
            LoadNamingTokenChips();
            UpdateNamingPreview();

            // Every handler action refuses to run while another project is active.
            _handler.TargetDocument = _doc;
            _handler.OnPlanBuilt = OnPlanBuilt;
            _handler.OnRunComplete = OnRunComplete;

            LinkedModels.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SelectedLinkCount));

            LoadLinkedModels();
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
            [NamingTokenType.Category] = "Category",
            [NamingTokenType.Area] = "Area",
            [NamingTokenType.Number] = "Number"
        };

        private void LoadNamingTokenChips()
        {
            NamingTokenChips.Clear();

            List<NamingToken> tokensToLoad = _settings.NamingTokens != null && _settings.NamingTokens.Count > 0
                ? _settings.NamingTokens
                : NamingToken.Defaults();

            foreach (NamingToken token in tokensToLoad.OrderBy(t => t.Order))
            {
                string label = TokenLabels.TryGetValue(token.Type, out string knownLabel)
                    ? knownLabel
                    : token.Type.ToString();

                var chip = new NamingTokenChipViewModel(token.Type, label, token.IsEnabled, token.Order);
                chip.PropertyChanged += (s, e) => UpdateNamingPreview();
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
                    NamingTokenType.Category => "Walls",
                    NamingTokenType.Area => "120m2",
                    NamingTokenType.Number => "01",
                    _ => ""
                };
                parts.Add(sample);
            }

            NamingPreviewText = string.Join(string.IsNullOrEmpty(NamingSeparator) ? "_" : NamingSeparator, parts)
                .Replace(" ", string.IsNullOrEmpty(NamingSeparator) ? "_" : NamingSeparator);
        }

        partial void OnNamingSeparatorChanged(string value) => UpdateNamingPreview();

        private RoofEdgeElementSectionsSettings BuildSettingsSnapshot()
        {
            return new RoofEdgeElementSectionsSettings
            {
                EdgeProximityToleranceMm = EdgeProximityToleranceMm,
                SameSideDedupToleranceMm = SameSideDedupToleranceMm,
                BelowRoofMm = BelowRoofMm,
                AboveRoofMm = AboveRoofMm,
                FarClipMm = FarClipMm,
                MarginOutwardMm = MarginOutwardMm,
                LengthInsideRoofMm = LengthInsideRoofMm,
                ViewTemplateName = SelectedViewTemplateName,
                OpenViewsMode = OpenViewsMode,
                NamingSeparator = NamingSeparator,
                NamingTokens = NamingTokenChips
                    .Select(c => new NamingToken { Type = c.Type, IsEnabled = c.IsEnabled, Order = c.Order })
                    .ToList(),
                LastLogExportFolder = _settings.LastLogExportFolder
            };
        }

        // ── 1. Roofs ─────────────────────────────────────────────────────

        /// <summary>Wired by the View so PickRoofs can hide/show the (modeless) window
        /// around the blocking Selection.PickObjects call. The pick itself runs through
        /// _pickQueue: Revit only allows it inside its API context, which a button click is not.</summary>
        public Action HideWindow { get; set; }
        public Action ShowWindow { get; set; }

        [RelayCommand]
        private void PickRoofs()
        {
            HideWindow?.Invoke();
            if (!_pickQueue.TryRun(PickRoofsInRevit))
            {
                ShowWindow?.Invoke();
                LogEntries.Add(new LogEntry(LogLevel.Warning,
                    "Pick Roofs: Revit is busy. Finish the current command or close any open dialog, then try again."));
            }
        }

        // Runs in the Revit API context (through _pickQueue).
        private void PickRoofsInRevit(UIApplication app)
        {
            try
            {
                UIDocument active = app.ActiveUIDocument;
                if (active == null || !_doc.IsValidObject || !active.Document.Equals(_doc))
                {
                    LogEntries.Add(new LogEntry(LogLevel.Error,
                        "Pick Roofs: switch back to the project this window was opened for, then try again."));
                    return;
                }

                IList<Reference> picked = _uiDoc.Selection.PickObjects(
                    ObjectType.Element, new RoofSelectionFilter(), "Select roof elements");

                int added = 0;
                foreach (Reference r in picked)
                {
                    if (_doc.GetElement(r) is not RoofBase roof) continue;
                    if (SelectedRoofs.Any(sr => sr.RoofId == roof.Id)) continue;

                    string displayName = SectionNamingService.GetRoofDisplayName(roof);
                    var item = new SelectedRoofItem(roof, displayName) { RemoveAction = RemoveRoof };
                    SelectedRoofs.Add(item);
                    added++;
                }

                LogEntries.Add(new LogEntry(LogLevel.Info, $"Pick Roofs: {added} new roof(s) added ({SelectedRoofs.Count} total)."));

                if (added > 0)
                    RequestBuildPlan();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                LogEntries.Add(new LogEntry(LogLevel.Info, "Pick Roofs: selection cancelled."));
            }
            catch (Exception ex)
            {
                LogEntries.Add(new LogEntry(LogLevel.Error, $"Pick Roofs: failed — {ex.Message}"));
            }
            finally
            {
                ShowWindow?.Invoke();
            }
        }

        private void RemoveRoof(SelectedRoofItem item)
        {
            SelectedRoofs.Remove(item);
            RequestBuildPlan();
        }

        [RelayCommand]
        private void ClearRoofs()
        {
            SelectedRoofs.Clear();
            RequestBuildPlan();
        }

        // ── 2. Linked Elements ───────────────────────────────────────────

        private void LoadLinkedModels()
        {
            var links = _linkService.GetLinkedModels(_doc, msg => LogEntries.Add(new LogEntry(LogLevel.Info, msg)));
            foreach (var link in links)
            {
                link.PropertyChanged += OnLinkSelectionChanged;
                LinkedModels.Add(link);
            }

            // Auto-select loaded links so the tree has something to show on open.
            foreach (var link in LinkedModels.Where(l => l.IsLoaded))
                link.IsSelected = true;

            RebuildElementTree();
        }

        private void OnLinkSelectionChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LinkedModelItem.IsSelected))
            {
                OnPropertyChanged(nameof(SelectedLinkCount));
                RebuildElementTree();
            }
        }

        private void RebuildElementTree()
        {
            var selectedIds = LinkedModels.Where(l => l.IsSelected).Select(l => l.LinkInstanceId);

            foreach (var node in ElementTree)
                foreach (var cat in node.Categories)
                    foreach (var fam in cat.Families)
                        foreach (var t in fam.Types)
                            t.PropertyChanged -= OnTypeCheckedChanged;

            ElementTree.Clear();

            var nodes = _linkService.BuildElementTree(_doc, selectedIds, msg => LogEntries.Add(new LogEntry(LogLevel.Info, msg)));
            foreach (var node in nodes)
            {
                foreach (var cat in node.Categories)
                    foreach (var fam in cat.Families)
                        foreach (var t in fam.Types)
                            t.PropertyChanged += OnTypeCheckedChanged;

                ElementTree.Add(node);
            }

            LogEntries.Add(new LogEntry(LogLevel.Info,
                $"{LinkedModels.Count(l => l.IsSelected)} linked instance(s) selected — element tree loaded ({nodes.Sum(n => n.Categories.Sum(c => c.Families.Sum(f => f.Types.Count)))} type(s) available)."));

            RebuildElementRows();
            RequestBuildPlan();
        }

        /// <summary>Flattens ElementTree into one row per Type leaf and (re)builds the
        /// sortable/filterable grid view over it. Rows reference the same TypeTreeItem
        /// instances as the tree, so a grid row's checkbox and the tree stay in sync
        /// automatically — there's only ever one IsChecked per type.</summary>
        private void RebuildElementRows()
        {
            _allElementRows = ElementTree
                .SelectMany(link => link.Categories.Select(cat => (link, cat)))
                .SelectMany(x => x.cat.Families.Select(fam => (x.link, x.cat, fam)))
                .SelectMany(x => x.fam.Types.Select(t => new LinkedElementRow
                {
                    LinkDisplayName = x.link.LinkDisplayName,
                    CategoryName = x.cat.CategoryName,
                    FamilyName = x.fam.FamilyName,
                    TypeName = t.TypeName,
                    InstanceCount = t.InstanceCount,
                    Source = t
                }))
                .ToList();

            // Preserve which categories were already checked (by name) across a rebuild —
            // only drop options for categories that no longer exist in the tree.
            var previouslyChecked = CategoryFilterOptions.Where(o => o.IsChecked).Select(o => o.CategoryName).ToHashSet();

            foreach (var opt in CategoryFilterOptions)
                opt.PropertyChanged -= OnCategoryFilterOptionChanged;
            CategoryFilterOptions.Clear();

            foreach (string cat in _allElementRows.Select(r => r.CategoryName).Distinct().OrderBy(n => n))
            {
                var opt = new CategoryFilterOption { CategoryName = cat, IsChecked = previouslyChecked.Contains(cat) };
                opt.PropertyChanged += OnCategoryFilterOptionChanged;
                CategoryFilterOptions.Add(opt);
            }

            _categoryFilterOptionsView = CollectionViewSource.GetDefaultView(CategoryFilterOptions);
            _categoryFilterOptionsView.Filter = FilterCategoryFilterOption;
            OnPropertyChanged(nameof(CategoryFilterOptionsView));

            UpdateCategoryFilterSummary();

            _elementRowsView = CollectionViewSource.GetDefaultView(_allElementRows);
            _elementRowsView.Filter = FilterElementRow;
            OnPropertyChanged(nameof(ElementRowsView));
        }

        private void OnCategoryFilterOptionChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(CategoryFilterOption.IsChecked)) return;
            UpdateCategoryFilterSummary();
            _elementRowsView?.Refresh();
        }

        private void UpdateCategoryFilterSummary()
        {
            var checkedNames = CategoryFilterOptions.Where(o => o.IsChecked).Select(o => o.CategoryName).ToList();
            CategoryFilterSummaryText = checkedNames.Count switch
            {
                0 => "All Categories",
                1 => checkedNames[0],
                _ => $"{checkedNames.Count} categories"
            };
        }

        private bool FilterCategoryFilterOption(object obj)
        {
            if (obj is not CategoryFilterOption opt) return false;
            if (string.IsNullOrWhiteSpace(CategoryFilterSearchText)) return true;
            return opt.CategoryName.Contains(CategoryFilterSearchText.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        partial void OnCategoryFilterSearchTextChanged(string value) => _categoryFilterOptionsView?.Refresh();

        [RelayCommand]
        private void ClearCategoryFilter()
        {
            foreach (var opt in CategoryFilterOptions)
                opt.IsChecked = false;
        }

        private bool FilterElementRow(object obj)
        {
            if (obj is not LinkedElementRow row) return false;

            var checkedCategories = CategoryFilterOptions.Where(o => o.IsChecked).Select(o => o.CategoryName).ToHashSet();
            if (checkedCategories.Count > 0 && !checkedCategories.Contains(row.CategoryName))
                return false;

            if (!string.IsNullOrWhiteSpace(ElementSearchText))
            {
                string needle = ElementSearchText.Trim();
                bool match = row.CategoryName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                          || row.FamilyName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                          || row.TypeName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                          || row.LinkDisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase);
                if (!match) return false;
            }

            return true;
        }

        partial void OnElementSearchTextChanged(string value) => _elementRowsView?.Refresh();

        private void OnTypeCheckedChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TypeTreeItem.IsChecked)) return;
            RunCommand.NotifyCanExecuteChanged();
            RequestBuildPlan();
        }

        [RelayCommand]
        private void CheckAllVisible()
        {
            if (_elementRowsView == null) return;
            foreach (LinkedElementRow row in _elementRowsView.Cast<LinkedElementRow>().ToList())
                row.Source.IsChecked = true;
        }

        [RelayCommand]
        private void UncheckAllVisible()
        {
            if (_elementRowsView == null) return;
            foreach (LinkedElementRow row in _elementRowsView.Cast<LinkedElementRow>().ToList())
                row.Source.IsChecked = false;
        }

        // ── Plan build / run ─────────────────────────────────────────────

        private void RequestBuildPlan()
        {
            if (SelectedRoofs.Count == 0)
            {
                PlannedSections.Clear();
                TotalRoofsCount = 0;
                DetectedElementCount = 0;
                SuggestedSectionCount = 0;
                RunCommand.NotifyCanExecuteChanged();
                return;
            }

            IsBusy = true;
            _handler.RequestedAction = RoofEdgeElementSectionsAction.BuildPlan;
            _handler.SelectedRoofs = SelectedRoofs.Select(r => r.RoofElement).ToList();
            _handler.ElementTree = ElementTree.ToList();
            _handler.Settings = BuildSettingsSnapshot();
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
            DetectedElementCount = result.DetectedElementCount;
            SuggestedSectionCount = result.SuggestedSectionCount;

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
            RequestBuildPlan();
        }

        /// <summary>
        /// Run stays enabled once the user has made a selection — either a checked
        /// planned-section row (the usual path) or a checked element type in the grid
        /// above, even before the plan rebuild round-trip lands. Run itself only ever
        /// acts on Ready/Included PlannedSections rows, so this never creates anything
        /// beyond what the table already shows — it just avoids the button flickering
        /// disabled between a checkbox toggle and the plan refresh that follows it.
        /// </summary>
        private bool CanRun() => !IsBusy && (
            PlannedSections.Any(r => r.IsIncluded && r.Status == PlannedSectionStatus.Ready)
            || _allElementRows.Any(r => r.Source.IsChecked));

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

            _handler.RequestedAction = RoofEdgeElementSectionsAction.RunCreate;
            _handler.Settings = settingsSnapshot;
            _handler.RowsToProcess = PlannedSections.Where(r => r.IsIncluded && r.Status == PlannedSectionStatus.Ready).ToList();
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

            IsBusy = false;
            RunCommand.NotifyCanExecuteChanged();

            RequestOpenViewsIfNeeded?.Invoke(result.CreatedViewIds, OpenViewsMode);

            RequestBuildPlan();
        }

        public Action<List<ElementId>, string> RequestOpenViewsIfNeeded { get; set; }

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

        /// <summary>Releases the pick external event and saves settings. Called from the window's Closing handler.</summary>
        public void OnWindowClosing()
        {
            _pickQueue.Dispose();

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
