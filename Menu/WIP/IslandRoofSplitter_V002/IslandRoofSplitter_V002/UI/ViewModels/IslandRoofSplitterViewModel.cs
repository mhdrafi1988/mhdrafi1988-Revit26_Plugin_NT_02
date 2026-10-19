// IslandRoofSplitter V002 — window ViewModel (CommunityToolkit.Mvvm).
// Preview = pure Core, run on open and re-run live when an option changes. Commit / highlight / re-pick go
// through the ExternalEvent; results come back on Revit's thread and are marshalled to this Dispatcher.
//
// Refactor (2026-10-14): now derives from Shared ToolViewModelBase, which owns IsRunning / Progress / SummaryText
// (bound by ToolWindowShell), the LogEntries collection, AddLog and the Clear / Copy All / Copy Selected log
// commands. Settings go through Shared SettingsService; the local SettingsStore and SharedAdapters are gone.
//
// Update (2026-10-15, per Anas): level and roof type are never assumed. Levels carry their elevation
// (LevelOption.Display), the default comes from Core LevelResolver (source roof's level, else nearest to the
// roof's top Z, flagged as a guess), SetupMessage drives the NEW ROOFS banner and Commit is blocked until both
// pickers hold a value. The launch prompt (LevelTypePromptWindow) can seed both through the constructor.
// Re-pick cannot show a modal — it runs on Revit's API thread — so a re-picked roof with no level re-raises
// the banner and blocks Commit instead.
//
// V002: Output (Roof only / Roof then Floor / Floor only) plus a floor type picker. Output does not change the
// preview geometry, so changing it saves settings without re-running the preview. For Roof-then-Floor a missing
// floor type only loses the fallback — Commit stays enabled and the banner says so; for Floor-only it blocks.
// Per-attempt lines from the handler are logged under the island's name, so a fallback always shows what the
// roof attempts refused.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin.IslandRoofSplitter.V002.Core.Models;
using Revit26_Plugin.IslandRoofSplitter.V002.Core.Services;
using Revit26_Plugin.IslandRoofSplitter.V002.Infrastructure.ExternalEvents;
using Revit26_Plugin.Shared.Models;
using Revit26_Plugin.Shared.Services;
using Revit26_Plugin.Shared.ViewModels;

namespace Revit26_Plugin.IslandRoofSplitter.V002.UI.ViewModels
{
    public partial class IslandRoofSplitterViewModel : ToolViewModelBase
    {
        /// <summary>Settings folder name: %AppData%\Revit26_Plugin\IslandRoofSplitter\settings.json.</summary>
        private const string SettingsKey = "IslandRoofSplitter";

        private readonly Dispatcher _dispatcher;
        private SplitterContext _ctx;
        private bool _loading;

        /// <summary>Ids confirmed in the launch prompt; they win over the source roof's own values once.</summary>
        private long? _presetLevelId;
        private long? _presetRoofTypeId;

        public event Action? RequestClose;
        public event Action<bool>? RequestHide; // true = hide for picking, false = show again

        public IslandRoofSplitterViewModel(SplitterContext context,
                                           long? presetLevelId = null, long? presetRoofTypeId = null)
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _ctx = context;
            _presetLevelId = presetLevelId;
            _presetRoofTypeId = presetRoofTypeId;
            IslandRoofSplitterEventManager.Init();

            IslandsView = CollectionViewSource.GetDefaultView(Islands);
            IslandsView.Filter = o => o is IslandRowViewModel r && PassesFilter(r);

            _loading = true;
            ApplySettings(SettingsService<IslandSplitterSettings>.Load(SettingsKey).Clamp());
            ApplyContext(context, resetOutputs: true);
            _loading = false;

            AddLog(LogLevel.Info, $"Window opened: roof {_ctx.RoofId}, {_ctx.Segments.Count} detail curves");
            RunPreview();
        }

        // ---------------- selection summary ----------------
        [ObservableProperty] private string _roofTitle = "";
        [ObservableProperty] private string _roofDetail = "";
        [ObservableProperty] private string _linesTitle = "";
        [ObservableProperty] private string _linesDetail = "";

        // ---------------- options ----------------
        [ObservableProperty] private SplitMode _mode;
        [ObservableProperty] private NestingMode _nesting;
        [ObservableProperty] private CrossingMode _crossings;
        [ObservableProperty] private ArcOutput _arcs;
        [ObservableProperty] private InteriorPoints _interior;
        [ObservableProperty] private double _chordToleranceMm = 5;

        /// <summary>V002: roof, roof-with-floor-fallback, or floor. Does not affect the preview geometry.</summary>
        [ObservableProperty] private OutputElement _output;

        public bool IsFlat => Mode == SplitMode.Flat;
        public bool IsSloped => Mode == SplitMode.Sloped;

        /// <summary>The floor type picker is live for the fallback and for Floor-only.</summary>
        public bool NeedsFloorType => Output != OutputElement.Roof;

        public string OutputHint => Output switch
        {
            OutputElement.Roof => "Roof only. An island Revit refuses is reported as Failed.",
            OutputElement.Floor => "Floor only. No roof is attempted.",
            _ => "Each island tries the roof first (twice, the second time with arcs tessellated); " +
                 "only if that fails is a floor created with the same footprint."
        };

        partial void OnOutputChanged(OutputElement value)
        {
            OnPropertyChanged(nameof(NeedsFloorType));
            OnPropertyChanged(nameof(OutputHint));
            if (_loading) return;
            RefreshSetupMessage();
            CommitCommand.NotifyCanExecuteChanged();
            SaveSettings(); // output does not change the preview, so no re-run
        }

        partial void OnModeChanged(SplitMode value)
        {
            OnPropertyChanged(nameof(IsFlat));
            OnPropertyChanged(nameof(IsSloped));
            OptionChanged();
        }
        partial void OnNestingChanged(NestingMode value) => OptionChanged();
        partial void OnCrossingsChanged(CrossingMode value) => OptionChanged();
        partial void OnArcsChanged(ArcOutput value) => OptionChanged();
        partial void OnInteriorChanged(InteriorPoints value) => OptionChanged();
        partial void OnChordToleranceMmChanged(double value)
        {
            double c = Math.Clamp(double.IsNaN(value) ? 5 : value,
                IslandSplitterSettings.MinChordToleranceMm, IslandSplitterSettings.MaxChordToleranceMm);
            if (Math.Abs(c - value) > 1e-9) { ChordToleranceMm = c; return; } // re-enters with the clamped value
            OptionChanged();
        }

        // Chip commands: the chips bind IsChecked one-way through EnumToBoolConverter (UI_UX_PATTERNS.md).
        [RelayCommand] private void SetMode(string v) => Mode = Enum.Parse<SplitMode>(v);
        [RelayCommand] private void SetNesting(string v) => Nesting = Enum.Parse<NestingMode>(v);
        [RelayCommand] private void SetCrossings(string v) => Crossings = Enum.Parse<CrossingMode>(v);
        [RelayCommand] private void SetArcs(string v) => Arcs = Enum.Parse<ArcOutput>(v);
        [RelayCommand] private void SetInterior(string v) => Interior = Enum.Parse<InteriorPoints>(v);
        [RelayCommand] private void SetOutput(string v) => Output = Enum.Parse<OutputElement>(v);

        private void ApplySettings(IslandSplitterSettings s)
        {
            Mode = s.Mode;
            Nesting = s.Nesting;
            Crossings = s.Crossings;
            Arcs = s.Arcs;
            Interior = s.Interior;
            ChordToleranceMm = s.ChordToleranceMm;
            Output = s.Output;
        }

        private IslandSplitterSettings CurrentSettings() => new()
        {
            Mode = Mode, Nesting = Nesting, Crossings = Crossings, Arcs = Arcs, Interior = Interior,
            ChordToleranceMm = ChordToleranceMm, Output = Output
        };

        private void SaveSettings() => SettingsService<IslandSplitterSettings>.Save(SettingsKey, CurrentSettings());

        private void OptionChanged()
        {
            if (_loading) return;
            SaveSettings();
            // live refresh only while showing a preview; after Commit the user starts a new run explicitly
            if (Phase == RunPhase.Preview && !IsRunning) RunPreview();
        }

        // ---------------- new element outputs ----------------
        public ObservableCollection<NamedId> RoofTypes { get; } = new();
        public ObservableCollection<LevelOption> Levels { get; } = new();

        /// <summary>V002: types for the floor fallback / Floor-only output.</summary>
        public ObservableCollection<NamedId> FloorTypes { get; } = new();

        [ObservableProperty] private NamedId? _selectedRoofType;
        [ObservableProperty] private NamedId? _selectedFloorType;
        [ObservableProperty] private LevelOption? _selectedLevel;
        [ObservableProperty] private double _baseOffsetMm;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(MarkExample))] private string _markPrefix = "";

        /// <summary>NEW ROOFS banner: what was not detected, or which level was guessed. Empty = no banner.</summary>
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSetupWarning))] private string _setupMessage = "";

        public bool HasSetupWarning => !string.IsNullOrEmpty(SetupMessage);

        /// <summary>Level elevation + base offset — the elevation the new roofs will sit at.</summary>
        public string NewRoofElevationText => SelectedLevel == null
            ? "Pick a level to see the new roof elevation."
            : $"New roofs at {SelectedLevel.ElevationMm + BaseOffsetMm:0} mm " +
              $"({SelectedLevel.Name} {SelectedLevel.ElevationMm:0} mm + offset {BaseOffsetMm:0} mm)";

        partial void OnSelectedLevelChanged(LevelOption? value)
        {
            OnPropertyChanged(nameof(NewRoofElevationText));
            CommitCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedRoofTypeChanged(NamedId? value) => CommitCommand.NotifyCanExecuteChanged();

        partial void OnSelectedFloorTypeChanged(NamedId? value)
        {
            if (!_loading) RefreshSetupMessage();
            CommitCommand.NotifyCanExecuteChanged();
        }

        partial void OnBaseOffsetMmChanged(double value) => OnPropertyChanged(nameof(NewRoofElevationText));

        public string MarkExample => string.IsNullOrEmpty(MarkPrefix)
            ? "Marks: Island_01, Island_02 … (no prefix — reruns will repeat Marks)"
            : $"Marks: {MarkPrefix}Island_01, {MarkPrefix}Island_02 … · clear the prefix for plain Island_01";

        // ---------------- results grid ----------------
        public ObservableCollection<IslandRowViewModel> Islands { get; } = new();
        public ICollectionView IslandsView { get; }
        [ObservableProperty] private IslandFilter _activeFilter = IslandFilter.All;
        [ObservableProperty] private IslandRowViewModel? _selectedIsland;

        /// <summary>Set by the view from DataGrid.SelectedItems (multi-select for Copy island log).</summary>
        public IList<IslandRowViewModel> SelectedIslands { get; set; } = new List<IslandRowViewModel>();

        partial void OnActiveFilterChanged(IslandFilter value) => IslandsView.Refresh();
        [RelayCommand] private void SetFilter(string v) => ActiveFilter = Enum.Parse<IslandFilter>(v);

        private bool PassesFilter(IslandRowViewModel r) => ActiveFilter switch
        {
            IslandFilter.Ready => r.Status == IslandStatus.Ready,
            IslandFilter.Skipped => r.Status == IslandStatus.Skipped,
            IslandFilter.Created => r.Status == IslandStatus.Created,
            IslandFilter.Failed => r.Status == IslandStatus.Failed,
            _ => true
        };

        // counts for chips and header tiles
        [ObservableProperty] private int _allCount;
        [ObservableProperty] private int _readyCount;
        [ObservableProperty] private int _skippedCount;
        [ObservableProperty] private int _createdCount;
        [ObservableProperty] private int _failedCount;
        [ObservableProperty] private int _verticesTotal;
        [ObservableProperty] private string _createdTile = "–";
        [ObservableProperty] private string _failedTile = "–";

        private void RecalculateCounts()
        {
            AllCount = Islands.Count;
            ReadyCount = Islands.Count(i => i.Status == IslandStatus.Ready);
            SkippedCount = Islands.Count(i => i.Status == IslandStatus.Skipped);
            CreatedCount = Islands.Count(i => i.Status == IslandStatus.Created);
            FailedCount = Islands.Count(i => i.Status == IslandStatus.Failed);
            VerticesTotal = Islands.Where(i => i.Status != IslandStatus.Skipped).Sum(i => i.VertexCount);
            bool post = Phase is RunPhase.Committing or RunPhase.Committed;
            CreatedTile = post ? CreatedCount.ToString() : "–";
            FailedTile = post ? FailedCount.ToString() : "–";
            CommitCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedIslandChanged(IslandRowViewModel? value)
        {
            CopyIslandLogCommand.NotifyCanExecuteChanged();
            if (value == null || IsRunning) return;

            var ids = value.Island.SourceIds.ToList();
            if (value.Island.CreatedElementId is long rid) ids.Add(rid);
            IslandRoofSplitterEventManager.Raise(new SplitterRequest
            {
                Kind = SplitterRequestKind.Highlight,
                HighlightIds = ids,
                Error = msg => _dispatcher.InvokeAsync(() => AddLog(LogLevel.Warning, $"Highlight {value.Name}: {msg}"))
            });
        }

        // ---------------- log (collection + Clear / Copy All / Copy Selected live in ToolViewModelBase) ----------------

        /// <summary>Copies the log lines that belong to the selected island rows.</summary>
        [RelayCommand(CanExecute = nameof(CanCopyIslandLog))]
        private void CopyIslandLog()
        {
            var names = SelectedIslands.Select(r => r.Name).ToHashSet();
            if (names.Count == 0 && SelectedIsland != null) names.Add(SelectedIsland.Name);

            var lines = LogEntries
                .Where(l => names.Any(n => (l.Message ?? "").StartsWith(n, StringComparison.Ordinal)))
                .ToList();
            LogClipboardService.CopySelected(lines);
            SummaryText = lines.Count > 0
                ? $"Copied {lines.Count} log lines for {names.Count} island(s)."
                : "No log lines for the selected islands.";
        }
        private bool CanCopyIslandLog() => SelectedIsland != null;

        private static LogLevel ToLogLevel(MessageSeverity s) => s switch
        {
            MessageSeverity.Debug => LogLevel.Debug,
            MessageSeverity.Warning => LogLevel.Warning,
            MessageSeverity.Error => LogLevel.Error,
            MessageSeverity.Success => LogLevel.Success,
            _ => LogLevel.Info
        };

        // ---------------- run state (IsRunning / Progress / SummaryText come from ToolViewModelBase) ----------------
        public enum RunPhase { Idle, Preview, Committing, Committed }

        [ObservableProperty] private RunPhase _phase = RunPhase.Idle;
        [ObservableProperty] private string _phaseText = "Not run";

        partial void OnPhaseChanged(RunPhase value)
        {
            PhaseText = value switch
            {
                RunPhase.Preview => "Preview · nothing created",
                RunPhase.Committing => "Committing…",
                RunPhase.Committed => "Committed",
                _ => "Not run"
            };
            CommitCommand.NotifyCanExecuteChanged();
        }

        /// <summary>IsRunning is declared on the base class, so its generated change hook isn't reachable here.</summary>
        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.PropertyName != nameof(IsRunning)) return;
            CommitCommand.NotifyCanExecuteChanged();
            RunPreviewCommand.NotifyCanExecuteChanged();
            RepickRoofCommand.NotifyCanExecuteChanged();
            RepickLinesCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanRun))]
        private void RunPreview()
        {
            PreviewResult result;
            try
            {
                result = IslandPreviewService.Run(_ctx.Segments, _ctx.Mesh, CurrentSettings(), _ctx.ShortCurveTolFt);
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, "Preview failed: " + ex.Message);
                SummaryText = "Preview failed — see log.";
                return;
            }

            SelectedIsland = null;
            Islands.Clear();
            foreach (var i in result.Islands) Islands.Add(new IslandRowViewModel(i));
            foreach (var m in result.Messages) AddLog(ToLogLevel(m.Severity), m.Text);

            Phase = RunPhase.Preview;
            Progress = 0;
            RecalculateCounts();
            IslandsView.Refresh();
            SummaryText = $"Preview: {ReadyCount} island(s) will be created, {SkippedCount} skipped" +
                          (FailedCount > 0 ? $", {FailedCount} will fail" : "") + ". Commit to create.";
        }
        private bool CanRun() => !IsRunning;

        [RelayCommand(CanExecute = nameof(CanCommit))]
        private void Commit()
        {
            if (SelectedLevel == null)
            {
                SummaryText = "Pick the level for the new elements first — it was not detected on the source roof.";
                return;
            }
            if (Output != OutputElement.Floor && SelectedRoofType == null)
            {
                SummaryText = "Pick the roof type for the new roofs first — it was not detected on the source roof.";
                return;
            }
            if (Output == OutputElement.Floor && SelectedFloorType == null)
            {
                SummaryText = "Pick the floor type for the new floors first.";
                return;
            }

            var ready = Islands.Where(r => r.Status == IslandStatus.Ready).ToList();
            var request = new CommitRequest
            {
                SourceRoofId = _ctx.RoofId,
                RoofTypeId = SelectedRoofType?.Id ?? 0,
                LevelId = SelectedLevel.Id,
                BaseOffsetFt = LengthUnits.MmToFt(BaseOffsetMm),
                MarkPrefix = MarkPrefix ?? "",
                Mode = Mode,
                Output = Output,
                FloorTypeId = NeedsFloorType ? SelectedFloorType?.Id ?? 0 : 0,
                Islands = ready.Select(r => r.Island).ToList()
            };
            var byName = Islands.ToDictionary(r => r.Name);

            Phase = RunPhase.Committing;
            IsRunning = true;
            Progress = 0;
            SummaryText = $"Creating {ready.Count} element(s)…";
            AddLog(LogLevel.Info, $"Commit: {ready.Count} island(s), output {Output}, mode {Mode}, " +
                                  $"roof type {SelectedRoofType?.Name ?? "–"}, " +
                                  $"floor type {(NeedsFloorType ? SelectedFloorType?.Name ?? "–" : "–")}, " +
                                  $"level {SelectedLevel.Display}, offset {BaseOffsetMm:0} mm " +
                                  $"→ new elements at {SelectedLevel.ElevationMm + BaseOffsetMm:0} mm");

            bool raised = IslandRoofSplitterEventManager.Raise(new SplitterRequest
            {
                Kind = SplitterRequestKind.Commit,
                Commit = request,
                IslandDone = (r, done, total) => _dispatcher.InvokeAsync(() => OnIslandCommitted(r, done, total, byName)),
                CommitFinished = (results, kept) => _dispatcher.InvokeAsync(() => OnCommitFinished(results, kept)),
                Error = msg => _dispatcher.InvokeAsync(() =>
                {
                    AddLog(LogLevel.Error, "Commit error: " + msg);
                    EndCommit(RunPhase.Preview, "Commit failed — see log.");
                })
            });
            if (!raised) EndCommit(RunPhase.Preview, "Revit is busy — try Commit again.");
        }
        /// <summary>
        /// V002: Commit needs a level, a roof type whenever a roof will be attempted, and a floor type whenever
        /// a floor will be attempted — with one exception: for Roof-then-Floor a missing floor type only loses
        /// the fallback, so Commit stays available and the banner says so.
        /// </summary>
        private bool CanCommit()
        {
            if (IsRunning || Phase != RunPhase.Preview || ReadyCount == 0) return false;
            if (SelectedLevel == null) return false;
            if (Output != OutputElement.Floor && SelectedRoofType == null) return false;
            if (Output == OutputElement.Floor && SelectedFloorType == null) return false;
            return true;
        }

        private void OnIslandCommitted(IslandCommitResult r, int done, int total, Dictionary<string, IslandRowViewModel> byName)
        {
            byName.TryGetValue(r.Name, out var row);
            row?.ApplyResult(r);
            AddLog(r.Status == IslandStatus.Created ? LogLevel.Success : LogLevel.Error,
                r.Status == IslandStatus.Created
                    ? $"{r.Name}  Created  {r.Kind}  {Mode}  {row?.VertexCount} vertices  Id {r.ElementId}"
                    : $"{r.Name}  Failed  {r.Reason}");

            // one line per attempt, so a floor fallback always says what the roof refused
            if (r.Attempts.Count > 1 || r.Status == IslandStatus.Failed)
                foreach (var a in r.Attempts)
                    AddLog(r.Status == IslandStatus.Created ? LogLevel.Info : LogLevel.Warning, $"{r.Name}  · {a}");

            Progress = total == 0 ? 100 : 100.0 * done / total;
            RecalculateCounts();
        }

        private void OnCommitFinished(List<IslandCommitResult> results, bool kept)
        {
            int created = results.Count(x => x.Status == IslandStatus.Created);
            int failed = results.Count(x => x.Status == IslandStatus.Failed);
            int roofs = results.Count(x => x.Kind == ElementKind.Roof);
            int floors = results.Count(x => x.Kind == ElementKind.Floor);
            string breakdown = $"{roofs} roof{(roofs == 1 ? "" : "s")}, {floors} floor{(floors == 1 ? "" : "s")}";

            AddLog(created > 0 ? LogLevel.Success : LogLevel.Error,
                $"Done: {created} created ({breakdown}), {failed} failed, {SkippedCount} skipped. " +
                $"Transaction group {(kept ? "kept" : "rolled back")}.");
            if (floors > 0)
                AddLog(LogLevel.Warning, $"{floors} island(s) fell back to a floor — the roof attempts are logged above.");
            Progress = 100;
            try
            {
                string folder = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                    "Revit26_Plugin", "Logs");
                string path = LogExportService.Export("IslandRoofSplitter", LogEntries, folder);
                AddLog(LogLevel.Info, $"Log saved: {path}");
            }
            catch (System.Exception ex)
            {
                AddLog(LogLevel.Warning, $"Log auto-save failed: {ex.Message}");
            }
            EndCommit(RunPhase.Committed, $"Done: {created} created ({breakdown}), {failed} failed.");
            IslandsView.Refresh();
        }

        private void EndCommit(RunPhase phase, string summary)
        {
            IsRunning = false;
            Phase = phase;
            RecalculateCounts();
            SummaryText = summary;
        }

        // ---------------- re-pick ----------------
        [RelayCommand(CanExecute = nameof(CanRun))] private void RepickRoof() => Repick(SplitterRequestKind.RepickRoof);
        [RelayCommand(CanExecute = nameof(CanRun))] private void RepickLines() => Repick(SplitterRequestKind.RepickLines);

        private void Repick(SplitterRequestKind kind)
        {
            bool roof = kind == SplitterRequestKind.RepickRoof;
            RequestHide?.Invoke(true);
            bool raised = IslandRoofSplitterEventManager.Raise(new SplitterRequest
            {
                Kind = kind,
                CurrentRoofId = _ctx.RoofId,
                CurrentLineIds = _ctx.Segments.Select(s => s.SourceId).Distinct().ToList(),
                RepickFinished = ctx => _dispatcher.InvokeAsync(() =>
                {
                    RequestHide?.Invoke(false);
                    ApplyContext(ctx, resetOutputs: roof);
                    AddLog(LogLevel.Info, roof ? $"Roof re-picked: {ctx.RoofId}" : $"Detail lines re-picked: {ctx.Segments.Count}");
                    RunPreview();
                }),
                Error = msg => _dispatcher.InvokeAsync(() =>
                {
                    RequestHide?.Invoke(false);
                    SummaryText = $"Re-pick {(roof ? "roof" : "lines")}: {msg}";
                })
            });
            if (!raised) { RequestHide?.Invoke(false); SummaryText = "Revit is busy — try again."; }
        }

        private void ApplyContext(SplitterContext ctx, bool resetOutputs)
        {
            _ctx = ctx;
            RoofTitle = ctx.RoofTitle; RoofDetail = ctx.RoofDetail;
            LinesTitle = ctx.LinesTitle; LinesDetail = ctx.LinesDetail;
            if (!resetOutputs) return;

            RoofTypes.Clear(); foreach (var t in ctx.RoofTypes) RoofTypes.Add(t);
            Levels.Clear(); foreach (var l in ctx.Levels) Levels.Add(l);
            FloorTypes.Clear(); foreach (var f in ctx.FloorTypes) FloorTypes.Add(f);

            // Floor type: the document default, else the first available. Never persisted.
            SelectedFloorType = FloorTypes.FirstOrDefault(f => f.Id == ctx.DefaultFloorTypeId)
                                ?? FloorTypes.FirstOrDefault();

            // A level / type confirmed in the launch prompt wins once; after that the context decides.
            LevelOption? preset = _presetLevelId is long pl ? Levels.FirstOrDefault(l => l.Id == pl) : null;
            NamedId? presetType = _presetRoofTypeId is long pt ? RoofTypes.FirstOrDefault(t => t.Id == pt) : null;
            _presetLevelId = null;
            _presetRoofTypeId = null;

            bool guessed = false;
            SelectedLevel = preset
                ?? LevelResolver.Resolve(ctx.Levels, ctx.LevelId, ctx.LevelDetected, ctx.RoofTopZFt, out guessed);
            SelectedRoofType = presetType
                ?? LevelResolver.ResolveType(ctx.RoofTypes, ctx.RoofTypeId, ctx.RoofTypeDetected);

            _levelConfirmedAtLaunch = preset != null;
            _typeConfirmedAtLaunch = presetType != null;
            _levelWasGuessed = guessed;
            RefreshSetupMessage();
            if (HasSetupWarning) AddLog(LogLevel.Warning, SetupMessage);

            BaseOffsetMm = Math.Round(LengthUnits.FtToMm(ctx.BaseOffsetFt), 1);
            MarkPrefix = $"R{ctx.RoofId}_";
        }

        // what ApplyContext found, kept so the banner can be rebuilt when Output or the floor type changes
        private bool _levelConfirmedAtLaunch;
        private bool _typeConfirmedAtLaunch;
        private bool _levelWasGuessed;

        private void RefreshSetupMessage() =>
            SetupMessage = BuildSetupMessage(_ctx, _levelConfirmedAtLaunch, _typeConfirmedAtLaunch, _levelWasGuessed);

        /// <summary>What the NEW ELEMENTS banner says. Empty when everything came straight off the source roof.</summary>
        private string BuildSetupMessage(SplitterContext ctx, bool levelConfirmed, bool typeConfirmed, bool guessed)
        {
            var parts = new List<string>();

            if (!ctx.HasLevels)
                parts.Add("No levels in this document — create a level in Revit, then re-run.");
            else if (SelectedLevel == null)
                parts.Add("Level not detected on the source roof — pick the level for the new roofs.");
            else if (levelConfirmed && !ctx.LevelDetected)
                parts.Add($"Level not detected on the source roof — using {SelectedLevel.Display} as confirmed at launch.");
            else if (guessed)
                parts.Add($"Level not detected on the source roof — pre-selected {SelectedLevel.Display}, " +
                          "the level nearest the roof's top Z. Confirm before Commit.");

            if (Output != OutputElement.Floor)
            {
                if (!ctx.HasRoofTypes)
                    parts.Add("No roof types are loaded — load a roof family, then re-run.");
                else if (SelectedRoofType == null)
                    parts.Add("Roof type not detected on the source roof — pick the type for the new roofs.");
                else if (typeConfirmed && !ctx.RoofTypeDetected)
                    parts.Add($"Roof type not detected on the source roof — using {SelectedRoofType.Name} as confirmed at launch.");
            }

            if (NeedsFloorType)
            {
                if (!ctx.HasFloorTypes)
                    parts.Add(Output == OutputElement.Floor
                        ? "No floor types are loaded — load a floor family, then re-run."
                        : "No floor types are loaded — the floor fallback is unavailable. Load a floor family, or set the output to Roof only.");
                else if (SelectedFloorType == null)
                    parts.Add("Pick the floor type for the floor " +
                              (Output == OutputElement.Floor ? "output." : "fallback."));
            }

            return string.Join("  ·  ", parts);
        }

        // ---------------- close ----------------
        [RelayCommand] private void Close() => RequestClose?.Invoke();

        public void OnWindowClosed() => SaveSettings();
    }
}
