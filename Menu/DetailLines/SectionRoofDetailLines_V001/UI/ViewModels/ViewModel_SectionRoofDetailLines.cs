using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Commands;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Models;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Services;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Infrastructure;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.UI.ViewModels
{
    /// <summary>
    /// Drives the Section Roof Detail Lines window: the sections on the sheet, the elements they
    /// show, and the run that draws the lines.
    /// <para>
    /// The window is modeless, so every call into Revit is queued on
    /// <see cref="SectionRoofLinesHandler"/> and runs in Revit's API context; nothing here
    /// touches a document on the WPF thread. The run itself lives in
    /// <c>ViewModel_SectionRoofDetailLines.Run.cs</c>.
    /// </para>
    /// </summary>
    public partial class ViewModel_SectionRoofDetailLines : ToolViewModelBase
    {
        private readonly UIApplication _uiApp;
        private readonly SectionRoofLinesHandler _handler = new();
        private readonly ExternalEvent _event;
        private readonly CascadeGate _gate = new();
        private readonly ModelReader _reader = new();
        private readonly string _documentTitle;

        private SectionRoofLinesSettings _settings;
        private ModelSnapshot _snapshot = new();

        // Held up while a whole-tree change is in flight, so the header cards are worked out once
        // at the end rather than once per type.
        private bool _cardsHeld;

        /// <summary>
        /// Creates the view model for <paramref name="uiApp"/> and shows
        /// <paramref name="snapshot"/>, the model as the command read it. The external event is
        /// created here — in Revit's API context, where the command still is — never lazily
        /// during a run.
        /// </summary>
        public ViewModel_SectionRoofDetailLines(
            UIApplication uiApp, ModelSnapshot snapshot, SectionRoofLinesSettings settings)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _settings = (settings ?? new SectionRoofLinesSettings()).Normalize();
            _event = ExternalEvent.Create(_handler);
            _documentTitle = uiApp.ActiveUIDocument?.Document?.Title ?? string.Empty;

            _drawRoofProfile = _settings.DrawRoofProfile;
            _roofTop = _settings.RoofTop;
            _roofBottom = _settings.RoofBottom;
            _roofLeft = _settings.RoofLeft;
            _roofRight = _settings.RoofRight;
            _wallFace = _settings.WallFace;
            _drawWallLines = _settings.DrawWallLines;
            _adjacencyToleranceMm = _settings.AdjacencyToleranceMm;
            _skipExistingLines = _settings.SkipExistingLines;
            _showAllCategories = _settings.ShowAllCategories;

            Apply(snapshot ?? new ModelSnapshot());
        }

        /// <summary>The sections the run can cover, in sheet order.</summary>
        public ObservableCollection<SectionRowViewModel> Sections { get; } = new();

        /// <summary>The host model and each link, holding the categories and types they show.</summary>
        public ObservableCollection<SourceNode> Sources { get; } = new();

        /// <summary>The project's detail line styles, for the two pickers.</summary>
        public ObservableCollection<string> LineStyleNames { get; } = new();

        /// <summary>Sheet number and name the sections came from, or a note that there is no sheet.</summary>
        [ObservableProperty]
        private string _sheetLabel = string.Empty;

        /// <summary>One line on where the sections came from and how many the run covers.</summary>
        [ObservableProperty]
        private string _sectionSourceNote = string.Empty;

        /// <summary>Filters the element list by category, family or type name.</summary>
        [ObservableProperty]
        private string _searchText = string.Empty;

        /// <summary>Show every category, not only roofs and walls.</summary>
        [ObservableProperty]
        private bool _showAllCategories = true;

        /// <summary>Line style for the roof cut profile; empty = the view's default.</summary>
        [ObservableProperty]
        private string _roofLineStyleName = string.Empty;

        /// <summary>Line style for the wall lines; empty = the view's default.</summary>
        [ObservableProperty]
        private string _wallLineStyleName = string.Empty;

        /// <summary>Trace the cut profile of every ticked roof.</summary>
        [ObservableProperty]
        private bool _drawRoofProfile = true;

        /// <summary>Draw the roof edges that face up — its top surface.</summary>
        [ObservableProperty]
        private bool _roofTop = true;

        /// <summary>Draw the roof edges that face down — the underside, and the soffit over any
        /// opening the section passes through.</summary>
        [ObservableProperty]
        private bool _roofBottom = true;

        /// <summary>Draw the roof edges that face left across the view.</summary>
        [ObservableProperty]
        private bool _roofLeft = true;

        /// <summary>Draw the roof edges that face right across the view.</summary>
        [ObservableProperty]
        private bool _roofRight = true;

        /// <summary>Draw a vertical line on ticked walls that stand next to a ticked roof.</summary>
        [ObservableProperty]
        private bool _drawWallLines = true;

        /// <summary>How close a wall must come to a roof to count as adjacent, in millimetres.</summary>
        [ObservableProperty]
        private double _adjacencyToleranceMm = 50.0;

        /// <summary>Which face of an adjacent wall gets a line.</summary>
        [ObservableProperty]
        private WallFaceChoice _wallFace = WallFaceChoice.NearerRoof;

        /// <summary>The sides of a roof's profile currently ticked, for the hint under them.</summary>
        public string RoofSidesNote
        {
            get
            {
                var sides = new List<string>();
                if (RoofTop) sides.Add("top");
                if (RoofBottom) sides.Add("bottom");
                if (RoofLeft) sides.Add("left");
                if (RoofRight) sides.Add("right");
                if (sides.Count == 4) return "The whole cut profile.";
                if (sides.Count == 0) return "No side chosen — the whole profile is drawn instead.";
                return "Only the " + string.Join(", ", sides) + " edge(s) of each roof's cut.";
            }
        }

        /// <summary>Leave out a line the view already has, so a second run does not duplicate it.</summary>
        [ObservableProperty]
        private bool _skipExistingLines = true;

        /// <summary>Sections ticked, over sections on offer.</summary>
        [ObservableProperty]
        private string _tickedSectionsText = "0 / 0";

        /// <summary>Roof types ticked.</summary>
        [ObservableProperty]
        private int _roofTypesTicked;

        /// <summary>Wall types ticked.</summary>
        [ObservableProperty]
        private int _wallTypesTicked;

        /// <summary>Lines the last run created, as text — "—" before the first run.</summary>
        [ObservableProperty]
        private string _linesCreatedText = "—";

        /// <summary>How many elements the sections read show in total.</summary>
        [ObservableProperty]
        private string _elementCountText = "0";

        /// <summary>True while Revit is working on a queued request; the shell greys the window.</summary>
        public bool IsBusy => IsRunning;

        /// <summary>The largest number of sections one run covers when none are ticked by hand.</summary>
        public static int MaxSectionsPerRun => SectionRoofLinesSettings.MaxSectionsPerRun;

        // ── Element list ──────────────────────────────────────────────────────

        /// <summary>Reads the model and the sheet's sections again.</summary>
        [RelayCommand(CanExecute = nameof(CanQueue))]
        private void Refresh()
        {
            Queue("Reading the sheet's sections…", app =>
            {
                Document doc = app.ActiveUIDocument?.Document;
                View active = app.ActiveUIDocument?.ActiveView;
                if (!CheckDocument(doc)) return;

                ModelSnapshot snapshot = _reader.Read(doc, active);
                Apply(snapshot);
                AddLog(LogLevel.Info,
                    $"Read {snapshot.Sections.Count} section(s) and {snapshot.Elements.Count} element row(s).");
            });
        }

        /// <summary>Ticks every section that can be drawn in.</summary>
        [RelayCommand]
        private void TickAllSections()
        {
            Batched(() =>
            {
                foreach (SectionRowViewModel row in Sections)
                    if (row.CanDraw) row.IsTicked = true;
            });
        }

        /// <summary>Unticks every section.</summary>
        [RelayCommand]
        private void UntickAllSections()
        {
            Batched(() =>
            {
                foreach (SectionRowViewModel row in Sections) row.IsTicked = false;
            });
        }

        /// <summary>Ticks every roof and wall type that the filters show.</summary>
        [RelayCommand]
        private void TickRoofsAndWalls()
        {
            SetAllTypes(true);
            AddLog(LogLevel.Info, $"Ticked {RoofTypesTicked} roof type(s) and {WallTypesTicked} wall type(s).");
        }

        /// <summary>Unticks every type.</summary>
        [RelayCommand]
        private void UntickAllTypes() => SetAllTypes(false);

        /// <summary>
        /// Writes every element the sections read show to the activity log — one line per element,
        /// with its section, source, category, family, type and id — so the list can be copied out.
        /// </summary>
        [RelayCommand]
        private void ListElementsToLog()
        {
            if (_snapshot.Elements.Count == 0)
            {
                AddLog(LogLevel.Warning, "Nothing to list — no element was found in the sections read.");
                return;
            }

            AddLog(LogLevel.Info, $"Elements visible in the sections read — {_snapshot.Elements.Count} row(s):");
            foreach (VisibleElementRow row in _snapshot.Elements
                         .OrderBy(r => r.ViewName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(r => r.SourceLabel, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(r => r.CategoryName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(r => r.TypeName, StringComparer.OrdinalIgnoreCase))
            {
                AddLog(LogLevel.Debug,
                    $"{row.ViewName} | {row.SourceLabel} | {row.CategoryName} | {row.FamilyName} : {row.TypeName} "
                    + $"| id {row.Key.ElementId} | {RoleText(row.Role)}");
            }
            AddLog(LogLevel.Info, "End of list. Copy All puts it on the clipboard.");
        }

        private static string RoleText(ElementRole role) => role switch
        {
            ElementRole.Roof => "roof — cut profile traced when ticked",
            ElementRole.Wall => "wall — line drawn when ticked and next to a roof",
            _ => "listed only"
        };

        // ── Reacting to the pickers ───────────────────────────────────────────

        partial void OnSearchTextChanged(string value) => ApplyFilters();

        partial void OnShowAllCategoriesChanged(bool value) => ApplyFilters();

        partial void OnDrawRoofProfileChanged(bool value) => GuardAtLeastOnePart(roofChanged: true);

        partial void OnRoofTopChanged(bool value) => GuardAtLeastOneSide(nameof(RoofTop));

        partial void OnRoofBottomChanged(bool value) => GuardAtLeastOneSide(nameof(RoofBottom));

        partial void OnRoofLeftChanged(bool value) => GuardAtLeastOneSide(nameof(RoofLeft));

        partial void OnRoofRightChanged(bool value) => GuardAtLeastOneSide(nameof(RoofRight));

        // Unticking the last side would make the roof part draw nothing and say nothing, so the
        // side just unticked comes back on — the same rule Normalize() applies to a settings file.
        private void GuardAtLeastOneSide(string justChanged)
        {
            if (RoofTop || RoofBottom || RoofLeft || RoofRight)
            {
                OnPropertyChanged(nameof(RoofSidesNote));
                return;
            }

            switch (justChanged)
            {
                case nameof(RoofTop): RoofTop = true; break;
                case nameof(RoofBottom): RoofBottom = true; break;
                case nameof(RoofLeft): RoofLeft = true; break;
                default: RoofRight = true; break;
            }
        }

        partial void OnDrawWallLinesChanged(bool value) => GuardAtLeastOnePart(roofChanged: false);

        // Turning both parts off would make Create do nothing, so the other one comes back on.
        private void GuardAtLeastOnePart(bool roofChanged)
        {
            if (DrawRoofProfile || DrawWallLines) return;
            if (roofChanged) DrawWallLines = true;
            else DrawRoofProfile = true;
        }

        // ── Snapshot → view ───────────────────────────────────────────────────

        private void Apply(ModelSnapshot snapshot)
        {
            _snapshot = snapshot;

            Sections.Clear();
            foreach (SectionTarget target in snapshot.Sections)
                Sections.Add(new SectionRowViewModel(target, RefreshCards));

            // Nothing ticked by hand yet: take the sheet's sections, capped at the run limit.
            int drawable = 0;
            _cardsHeld = true;
            try
            {
                foreach (SectionRowViewModel row in Sections)
                {
                    if (!row.CanDraw) continue;
                    drawable++;
                    row.IsTicked = drawable <= MaxSectionsPerRun;
                }
            }
            finally
            {
                _cardsHeld = false;
            }

            LineStyleNames.Clear();
            LineStyleNames.Add(string.Empty); // the view's default style
            foreach (string name in snapshot.LineStyleNames) LineStyleNames.Add(name);

            RoofLineStyleName = LineStyleNames.Contains(_settings.RoofLineStyleName)
                ? _settings.RoofLineStyleName : string.Empty;
            WallLineStyleName = LineStyleNames.Contains(_settings.WallLineStyleName)
                ? _settings.WallLineStyleName : string.Empty;

            BuildTree(snapshot);

            SheetLabel = snapshot.SectionSource == SectionSourceKind.Sheet && !string.IsNullOrEmpty(snapshot.SheetLabel)
                ? snapshot.SheetLabel
                : "not on a sheet";

            SectionSourceNote = snapshot.SectionSource switch
            {
                SectionSourceKind.Sheet when drawable > MaxSectionsPerRun =>
                    $"{drawable} sections on this sheet. One run covers at most {MaxSectionsPerRun}, so the first "
                    + $"{MaxSectionsPerRun} are ticked — change the ticks to pick others.",
                SectionSourceKind.Sheet =>
                    $"Every section on this sheet is ticked ({drawable}). One run covers at most {MaxSectionsPerRun}.",
                _ => "The active section is not on a sheet, so the run covers that one view."
            };

            ElementCountText = snapshot.Elements.Count.ToString();

            foreach (LogEntry note in snapshot.Notes) AddLog(note.Level, note.Message);
            ApplyFilters();
            RefreshCards();
        }

        private void BuildTree(ModelSnapshot snapshot)
        {
            Sources.Clear();

            // The tree is built with the cascade held off: each node's tick is set as the node is
            // created, and a parent that is still half-built has nothing useful to read back.
            // Every node's state is settled once, at the end, by RefreshFromChildren.
            _gate.Suppressed = true;
            try
            {
                BuildTreeNodes(snapshot);
            }
            finally
            {
                _gate.Suppressed = false;
            }

            foreach (SourceNode source in Sources)
            {
                foreach (CategoryNode category in source.Categories)
                {
                    category.PropertyChanged += OnNodeTickChanged;
                    foreach (TypeNode type in category.Types) type.PropertyChanged += OnNodeTickChanged;
                    category.RefreshFromChildren();
                }
                source.PropertyChanged += OnNodeTickChanged;
                source.RefreshCounts();
            }
        }

        // A tick anywhere in the tree changes what the header cards and Create's CanExecute read.
        private void OnNodeTickChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TypeNode.IsChecked)) RefreshCards();
        }

        /// <summary>Runs <paramref name="change"/> with the header cards held, then works them out once.</summary>
        private void Batched(System.Action change)
        {
            _cardsHeld = true;
            try { change(); }
            finally { _cardsHeld = false; }
            RefreshCards();
        }

        private void BuildTreeNodes(ModelSnapshot snapshot)
        {

            // One node per source, then per category, then per type, with the instances each type
            // has across every section read. Roofs and walls come first: they are what can be
            // ticked, and the rest of the list is there to be read.
            foreach (var sourceGroup in snapshot.Elements
                         .GroupBy(r => new { r.Key.LinkInstanceId, r.SourceLabel, r.Source })
                         .OrderBy(g => g.Key.Source == ElementSourceKind.Host ? 0 : 1)
                         .ThenBy(g => g.Key.SourceLabel, StringComparer.OrdinalIgnoreCase))
            {
                var sourceNode = new SourceNode(_gate)
                {
                    LinkInstanceId = sourceGroup.Key.LinkInstanceId,
                    Label = sourceGroup.Key.SourceLabel,
                    Source = sourceGroup.Key.Source
                };

                foreach (var categoryGroup in sourceGroup
                             .GroupBy(r => r.CategoryName)
                             .OrderBy(g => RoleOrder(g.First().Role))
                             .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                {
                    ElementRole role = categoryGroup.First().Role;
                    var categoryNode = new CategoryNode(_gate, sourceNode)
                    {
                        CategoryName = categoryGroup.Key,
                        Role = role,
                        IsExpanded = role != ElementRole.Listed
                    };

                    foreach (var typeGroup in categoryGroup
                                 .GroupBy(r => r.Type)
                                 .OrderBy(g => g.First().FamilyName, StringComparer.OrdinalIgnoreCase)
                                 .ThenBy(g => g.First().TypeName, StringComparer.OrdinalIgnoreCase))
                    {
                        VisibleElementRow first = typeGroup.First();
                        categoryNode.Types.Add(new TypeNode(_gate, categoryNode)
                        {
                            Key = typeGroup.Key,
                            Label = $"{first.FamilyName} : {first.TypeName}",
                            Role = role,
                            InstanceCount = typeGroup.Select(r => r.Key).Distinct().Count(),
                            SectionCount = typeGroup.Select(r => r.ViewKey).Distinct().Count(),
                            // Roofs and walls start ticked, so a first run needs no setting up.
                            IsChecked = role != ElementRole.Listed
                        });
                    }

                    if (categoryNode.Types.Count > 0) sourceNode.Categories.Add(categoryNode);
                }

                if (sourceNode.Categories.Count > 0) Sources.Add(sourceNode);
            }
        }

        private static int RoleOrder(ElementRole role) => role switch
        {
            ElementRole.Roof => 0,
            ElementRole.Wall => 1,
            _ => 2
        };

        private void SetAllTypes(bool state) => Batched(() => SetAllTypesCore(state));

        private void SetAllTypesCore(bool state)
        {
            _gate.Suppressed = true;
            try
            {
                foreach (SourceNode source in Sources)
                    foreach (CategoryNode category in source.Categories)
                        foreach (TypeNode type in category.Types)
                            if (type.IsSelectable && type.IsVisible) type.IsChecked = state;
            }
            finally
            {
                _gate.Suppressed = false;
            }

            foreach (SourceNode source in Sources)
            {
                foreach (CategoryNode category in source.Categories) category.RefreshFromChildren();
                source.RefreshCounts();
            }
        }

        private void ApplyFilters()
        {
            string needle = (SearchText ?? string.Empty).Trim();
            bool hasNeedle = needle.Length > 0;

            foreach (SourceNode source in Sources)
            {
                foreach (CategoryNode category in source.Categories)
                {
                    bool categoryAllowed = ShowAllCategories || category.Role != ElementRole.Listed;

                    foreach (TypeNode type in category.Types)
                    {
                        bool matches = !hasNeedle
                            || category.CategoryName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                            || type.Label.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                            || source.Label.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
                        type.IsVisible = categoryAllowed && matches;
                    }

                    category.IsVisible = category.Types.Any(t => t.IsVisible);
                }

                source.IsVisible = source.Categories.Any(c => c.IsVisible);
            }
        }

        /// <summary>Updates the header cards from the current ticks.</summary>
        public void RefreshCards()
        {
            if (_cardsHeld) return;

            int ticked = Sections.Count(s => s.IsTicked);
            TickedSectionsText = $"{ticked} / {Sections.Count}";

            var types = Sources
                .SelectMany(s => s.Categories)
                .SelectMany(c => c.Types)
                .Where(t => t.IsChecked)
                .ToList();
            RoofTypesTicked = types.Count(t => t.Role == ElementRole.Roof);
            WallTypesTicked = types.Count(t => t.Role == ElementRole.Wall);

            CreateCommand.NotifyCanExecuteChanged();
            RefreshCommand.NotifyCanExecuteChanged();
        }

        // ── Revit plumbing ────────────────────────────────────────────────────

        /// <summary>True while no request is in flight, so a second one cannot be queued.</summary>
        private bool CanQueue() => !IsRunning;

        /// <summary>
        /// Queues <paramref name="work"/> for Revit's API context and shows
        /// <paramref name="status"/> while it runs. Returns false when a request is already in
        /// flight.
        /// </summary>
        private bool Queue(string status, Action<UIApplication> work)
        {
            if (IsRunning) return false;

            IsRunning = true;
            OnPropertyChanged(nameof(IsBusy));
            SummaryText = status;
            CreateCommand.NotifyCanExecuteChanged();
            RefreshCommand.NotifyCanExecuteChanged();

            _handler.Queue(app =>
            {
                try
                {
                    work(app);
                }
                finally
                {
                    IsRunning = false;
                    OnPropertyChanged(nameof(IsBusy));
                    SummaryText = string.Empty;
                    CreateCommand.NotifyCanExecuteChanged();
                    RefreshCommand.NotifyCanExecuteChanged();
                }
            });
            _event.Raise();
            return true;
        }

        /// <summary>
        /// False, with a line in the log, when the document in front of the user is no longer the
        /// one the window opened on — ids read from the old one would name the wrong elements.
        /// </summary>
        private bool CheckDocument(Document doc)
        {
            if (doc == null)
            {
                AddLog(LogLevel.Error, "No project is open. Open the sheet again and reopen the tool.");
                return false;
            }
            if (!string.Equals(doc.Title, _documentTitle, StringComparison.Ordinal))
            {
                AddLog(LogLevel.Error,
                    $"The active project is now '{doc.Title}', not '{_documentTitle}'. Close this window and run the tool again there.");
                return false;
            }
            if (doc.IsReadOnly)
            {
                AddLog(LogLevel.Error, "The project is read-only — nothing can be created.");
                return false;
            }
            return true;
        }

        /// <summary>Writes the window's choices to %AppData% — called as the window closes.</summary>
        public void SaveSettings()
        {
            _settings.RoofLineStyleName = RoofLineStyleName ?? string.Empty;
            _settings.WallLineStyleName = WallLineStyleName ?? string.Empty;
            _settings.DrawRoofProfile = DrawRoofProfile;
            _settings.DrawWallLines = DrawWallLines;
            _settings.AdjacencyToleranceMm = AdjacencyToleranceMm;
            _settings.RoofTop = RoofTop;
            _settings.RoofBottom = RoofBottom;
            _settings.RoofLeft = RoofLeft;
            _settings.RoofRight = RoofRight;
            _settings.WallFace = WallFace;
            _settings.SkipExistingLines = SkipExistingLines;
            _settings.ShowAllCategories = ShowAllCategories;
            SettingsService<SectionRoofLinesSettings>.Save(
                SectionRoofDetailLinesCommand.SettingsFolder, _settings.Normalize());
        }
    }
}
