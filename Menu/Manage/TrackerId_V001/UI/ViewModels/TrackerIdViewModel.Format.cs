using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.TrackerId.V001.Core.Models;
using Revit26_Plugin_Revised.TrackerId.V001.Core.Services;
using Revit26_Plugin_Revised.TrackerId.V001.Infrastructure.Revit;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows.Data;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.TrackerId.V001.UI.ViewModels
{
    /// <summary>A choice in the separator drop-down.</summary>
    public sealed class SeparatorOption
    {
        /// <summary>Creates the option.</summary>
        public SeparatorOption(string label, string value)
        {
            Label = label;
            Value = value;
        }

        /// <summary>Display text.</summary>
        public string Label { get; }

        /// <summary>Text put between segments.</summary>
        public string Value { get; }

        /// <inheritdoc/>
        public override string ToString() => Label;
    }

    // Step 3: the ID format editor, the element snapshot, the plan and the write.
    public partial class TrackerIdViewModel
    {
        private List<ElementSnapshot> _snapshots;
        private List<TrackerIdHolder> _holders = new();
        private GridLocator _grids = new(Array.Empty<GridLine2D>());
        private ModelIdentity _model;
        private List<PlanRow> _plan;
        private string _loadedKey;
        private bool _suspendTypeLoad;
        private DispatcherTimer _replanTimer;
        private DispatcherTimer _reloadTimer;

        /// <summary>Segments in ID order.</summary>
        public ObservableCollection<SegmentViewModel> Segments { get; } = new();

        /// <summary>Separator choices.</summary>
        public IReadOnlyList<SeparatorOption> Separators { get; } = new[]
        {
            new SeparatorOption("Dash  -", "-"), new SeparatorOption("Underscore  _", "_"),
            new SeparatorOption("Dot  .", "."), new SeparatorOption("None", string.Empty)
        };

        /// <summary>Text between segments.</summary>
        [ObservableProperty] private SeparatorOption _selectedSeparator;

        /// <summary>Zone value when none is found.</summary>
        [ObservableProperty] private string _noZoneText = "ZX";

        /// <summary>Value for any other segment with nothing to show.</summary>
        [ObservableProperty] private string _missingText = "NA";

        /// <summary>Grid distance that flags an element for checking, metres.</summary>
        [ObservableProperty] private double _gridToleranceM = 3.0;

        /// <summary>Write Tracker_Zone, Tracker_Level and Tracker_Grid.</summary>
        [ObservableProperty] private bool _writeLocationParameters = true;

        /// <summary>Write Tracker_SourceGuid.</summary>
        [ObservableProperty] private bool _writeSourceGuid = true;

        /// <summary>The full ID of the first element, as a preview of the format.</summary>
        [ObservableProperty] private string _sampleId = "—";

        /// <summary>Result rows.</summary>
        [ObservableProperty] private ObservableCollection<PlanRowViewModel> _planRows = new();

        /// <summary>Filtered view of <see cref="PlanRows"/> bound to the grid.</summary>
        [ObservableProperty] private ICollectionView _planView;

        /// <summary>"All", "New", "Kept" or "Problems".</summary>
        [ObservableProperty] private string _planFilter = "All";

        /// <summary>Elements in the run.</summary>
        [ObservableProperty] private int _inScopeCount;

        /// <summary>Elements that keep their Tracker_ID.</summary>
        [ObservableProperty] private int _haveIdCount;

        /// <summary>Elements that get a new Tracker_ID (copies included).</summary>
        [ObservableProperty] private int _newCount;

        /// <summary>Copied elements that get a new Tracker_ID.</summary>
        [ObservableProperty] private int _copyCount;

        /// <summary>Elements that moved since their ID was written.</summary>
        [ObservableProperty] private int _movedCount;

        /// <summary>Duplicates plus locked elements.</summary>
        [ObservableProperty] private int _problemCount;

        /// <summary>New IDs that would repeat one in use.</summary>
        [ObservableProperty] private int _duplicateCount;

        /// <summary>One line under the result grid.</summary>
        [ObservableProperty] private string _planSummaryText = string.Empty;

        /// <summary>Banner text when IDs repeat; empty hides the banner.</summary>
        [ObservableProperty] private string _duplicateWarning = string.Empty;

        private void InitFormat()
        {
            foreach (var definition in _settings.Segments)
                Segments.Add(new SegmentViewModel(definition, OnSegmentChanged));

            SelectedSeparator = Separators.FirstOrDefault(s => s.Value == _settings.Separator) ?? Separators[0];
            NoZoneText = _settings.NoZoneText ?? "ZX";
            MissingText = _settings.MissingText ?? "NA";
            GridToleranceM = _settings.GridToleranceM > 0 ? _settings.GridToleranceM : 3.0;
            WriteLocationParameters = _settings.WriteLocationParameters;
            WriteSourceGuid = _settings.WriteSourceGuid;

            _replanTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) =>
            {
                _replanTimer.Stop();
                Replan();
            }, _dispatcher);
            _replanTimer.Stop();

            _reloadTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) =>
            {
                _reloadTimer.Stop();
                if (IsStep3 || IsStep4) EnsureSnapshots();
            }, _dispatcher);
            _reloadTimer.Stop();
        }

        /// <summary>The format as currently edited.</summary>
        public TrackerFormat CurrentFormat() => new()
        {
            Segments = Segments.Select(s => s.ToDefinition()).ToList(),
            Separator = SelectedSeparator?.Value ?? "-",
            NoZoneText = NoZoneText ?? string.Empty,
            MissingText = MissingText ?? string.Empty,
            GridToleranceM = GridToleranceM > 0 ? GridToleranceM : 3.0
        };

        // ── Segment editing ───────────────────────────────────────────────────

        /// <summary>Moves a segment one place earlier in the ID.</summary>
        [RelayCommand]
        private void MoveSegmentUp(SegmentViewModel segment) => MoveSegment(segment, -1);

        /// <summary>Moves a segment one place later in the ID.</summary>
        [RelayCommand]
        private void MoveSegmentDown(SegmentViewModel segment) => MoveSegment(segment, +1);

        private void MoveSegment(SegmentViewModel segment, int step)
        {
            int i = Segments.IndexOf(segment);
            int j = i + step;
            if (i < 0 || j < 0 || j >= Segments.Count) return;
            Segments.Move(i, j);
            ScheduleReplan();
        }

        private void OnSegmentChanged(SegmentViewModel segment, bool parameterNameChanged)
        {
            if (parameterNameChanged && _loadedKey != null && _loadedKey != SnapshotKey())
            {
                _replanTimer?.Stop();
                _reloadTimer?.Stop();
                _reloadTimer?.Start();
                return;
            }
            ScheduleReplan();
        }

        partial void OnSelectedSeparatorChanged(SeparatorOption value) => ScheduleReplan();
        partial void OnNoZoneTextChanged(string value) => ScheduleReplan();
        partial void OnMissingTextChanged(string value) => ScheduleReplan();
        partial void OnGridToleranceMChanged(double value) => ScheduleReplan();
        partial void OnPlanFilterChanged(string value) => PlanView?.Refresh();

        /// <summary>Picks a result filter chip.</summary>
        [RelayCommand]
        private void SetPlanFilter(string filter) => PlanFilter = filter ?? "All";

        private void ScheduleReplan()
        {
            if (_replanTimer == null) return;
            _replanTimer.Stop();
            _replanTimer.Start();
        }

        // ── Reading the elements ──────────────────────────────────────────────

        private void InvalidateSnapshots()
        {
            _loadedKey = null;
            _snapshots = null;
            _plan = null;
        }

        // Identifies what the snapshot was read for: the types and every parameter name the format reads.
        private string SnapshotKey()
        {
            var needs = NeededParameterNames();
            var typeIds = Types.Where(t => t.IsSelected).Select(t => t.Info.Id).OrderBy(id => id);
            var categoryIds = TickedCategories.Select(c => c.Info.Id).OrderBy(id => id);
            return $"{ScopeKey()}|{string.Join(",", categoryIds)}|{string.Join(",", typeIds)}|{needs.Key}";
        }

        /// <summary>Parameter names the format reads, by where they live.</summary>
        private sealed class ParameterNeeds
        {
            public List<string> Element { get; } = new();
            public List<string> Level { get; } = new();
            public List<string> Project { get; } = new();

            public string Key => $"{string.Join(",", Element)}|{string.Join(",", Level)}|{string.Join(",", Project)}";
        }

        // Disabled segments count too: the zone parameter also feeds Tracker_Zone, and the model code
        // also fills the export's Model_Code column.
        private ParameterNeeds NeededParameterNames()
        {
            var needs = new ParameterNeeds();
            foreach (var s in Segments)
            {
                if (s.SelectedSource == null || string.IsNullOrWhiteSpace(s.Option)) continue;
                var source = s.SelectedSource.Value;
                if (!SegmentCatalog.OptionIsParameterName(source)) continue;

                var list = source == SegmentSource.LevelParameter ? needs.Level
                         : SegmentCatalog.OptionIsProjectParameter(source) ? needs.Project
                         : needs.Element;
                string name = s.Option.Trim();
                if (!list.Contains(name)) list.Add(name);
            }
            needs.Element.Sort(StringComparer.Ordinal);
            needs.Level.Sort(StringComparer.Ordinal);
            needs.Project.Sort(StringComparer.Ordinal);
            return needs;
        }

        private void EnsureSnapshots()
        {
            if (TickedCategories.Count == 0 || SelectedTypeCount == 0) return;

            string key = SnapshotKey();
            if (key == _loadedKey && _snapshots != null)
            {
                Replan();
                return;
            }

            var categoryIds = new HashSet<long>(TickedCategories.Select(c => c.Info.Id));
            var typeIds = new HashSet<long>(Types.Where(t => t.IsSelected).Select(t => t.Info.Id));
            var needs = NeededParameterNames();
            var scope = _scope;

            Run($"Reading {SelectedElementCount} elements…", app =>
            {
                var doc = DocumentFor(app);
                if (doc == null) return;
                var read = ReadElements(doc, categoryIds, typeIds, needs, scope);
                OnUi(() => ApplyElements(read, key, logCounts: true));
            });
        }

        private sealed class ElementRead
        {
            public List<ElementSnapshot> Snapshots;
            public List<GridLine2D> Grids;
            public int ScopeBoxCount;
            public List<TrackerIdHolder> Holders;
            public ModelIdentity Model;
        }

        // Runs in the Revit API context.
        // typeIds null = every type (the health check reads all tracked elements this way).
        private static ElementRead ReadElements(Document doc, ISet<long> categoryIds, ISet<long> typeIds, ParameterNeeds needs, ReadScope scope)
        {
            var boxes = ModelReader.ReadScopeBoxes(doc);
            var model = ModelReader.ReadModelIdentity(doc, needs.Project);
            var snapshots = ModelReader.ReadSnapshots(doc, categoryIds, typeIds, needs.Element, needs.Level, new ZoneLocator(boxes), scope);
            foreach (var s in snapshots) s.Model = model;

            return new ElementRead
            {
                Grids = ModelReader.ReadGrids(doc),
                ScopeBoxCount = boxes.Count,
                Snapshots = snapshots,
                Holders = ModelReader.ReadIdHolders(doc),
                Model = model
            };
        }

        private void ApplyElements(ElementRead read, string key, bool logCounts)
        {
            _snapshots = read.Snapshots;
            _holders = read.Holders;
            _grids = new GridLocator(read.Grids);
            _model = read.Model;
            _loadedKey = key;

            if (logCounts)
            {
                AddLog(LogLevel.Info, $"Read {read.Snapshots.Count} elements, {_grids.GridCount} grids, {read.ScopeBoxCount} scope boxes; {read.Holders.Count} Tracker_IDs in the model.");
                if (_grids.GridCount == 0)
                    AddLog(LogLevel.Warning, $"No grids found. The Grid segment will show '{MissingText}'.");
                var zone = Segments.FirstOrDefault(s => s.Kind == SegmentKind.Zone)?.SelectedSource?.Value;
                if (read.ScopeBoxCount == 0 && zone != SegmentSource.ZoneParameter)
                    AddLog(LogLevel.Warning, "No scope boxes found. Zone comes from the Zone parameter only.");
                LogModelCode();
            }
            Replan();
        }

        // Says which model code this file gets and warns when it may not be unique across models.
        private void LogModelCode()
        {
            var segment = Segments.FirstOrDefault(s => s.Kind == SegmentKind.Model);
            if (segment == null || _model == null) return;

            var definition = segment.ToDefinition();
            string code = TrackerIdPlanner.ModelCode(definition, _model, out bool guessed);
            if (!segment.IsEnabled)
            {
                AddLog(LogLevel.Warning, "The Model segment is off. IDs may repeat IDs in other Revit models; turn it on before writing.");
                return;
            }

            if (string.IsNullOrWhiteSpace(code))
                AddLog(LogLevel.Warning, $"No model code found for '{_model.Title}'. Fill Project Information ▸ '{definition.Option}' (e.g. ARC, STR, MEP); until then the Model segment shows '{MissingText}'.");
            else if (guessed)
                AddLog(LogLevel.Warning, $"Model code '{code}' was made from the file name '{_model.Title}', which has no discipline code. Set Project Information ▸ '{definition.Option}' (e.g. ARC) so every model has its own short code.");
            else
                AddLog(LogLevel.Info, $"Model code for this file: {code}.");
        }

        // ── Planning ──────────────────────────────────────────────────────────

        private void Replan()
        {
            if (_snapshots == null) return;

            var format = CurrentFormat();
            _plan = TrackerIdPlanner.Plan(_snapshots, format, _grids, _holders);

            PlanRows = new ObservableCollection<PlanRowViewModel>(_plan.Select(r => new PlanRowViewModel(r)));
            var view = CollectionViewSource.GetDefaultView(PlanRows);
            view.Filter = PassesPlanFilter;
            PlanView = view;

            InScopeCount = _plan.Count;
            HaveIdCount = _plan.Count(r => r.Action == PlanAction.Keep || r.Action == PlanAction.KeepMoved || r.Action == PlanAction.Adopt);
            NewCount = _plan.Count(r => r.GetsNewId);
            CopyCount = _plan.Count(r => r.Action == PlanAction.Copy);
            MovedCount = _plan.Count(r => r.Action == PlanAction.KeepMoved);
            DuplicateCount = _plan.Count(r => r.Action == PlanAction.Duplicate);
            int locked = _plan.Count(r => r.Action == PlanAction.Locked);
            ProblemCount = DuplicateCount + locked;
            int gridChecks = _plan.Count(r => r.GridCheck);

            var summary = new StringBuilder($"{InScopeCount} elements · {HaveIdCount} keep · {NewCount} new");
            if (CopyCount > 0) summary.Append($" (of which {CopyCount} copies)");
            if (MovedCount > 0) summary.Append($" · {MovedCount} moved");
            if (locked > 0) summary.Append($" · {locked} locked");
            if (gridChecks > 0) summary.Append($" · {gridChecks} to check (grid over {format.GridToleranceM:0.#} m)");
            PlanSummaryText = summary.ToString();

            DuplicateWarning = DuplicateCount == 0 ? string.Empty
                : $"{DuplicateCount} new ID(s) repeat an ID already in use. Turn on a Running number segment, or add segments, so every element gets its own ID.";

            var first = _snapshots.FirstOrDefault();
            foreach (var s in Segments)
                s.SampleValue = first == null ? "—" : s.IsEnabled ? TrackerIdPlanner.SegmentValue(s.ToDefinition(), first, format, _grids, 1) : "(off)";
            SampleId = first == null ? "—" : TrackerIdPlanner.BuildId(first, format, _grids, 1);
            if (SampleId.Length == 0) SampleId = "Turn on at least one segment.";

            SummaryText = PlanSummaryText;
            RefreshCommands();
        }

        private bool PassesPlanFilter(object item)
        {
            if (item is not PlanRowViewModel r) return false;
            return PlanFilter switch
            {
                "New" => r.Row.GetsNewId,
                "Kept" => r.Action == PlanAction.Keep || r.Action == PlanAction.KeepMoved || r.Action == PlanAction.Adopt,
                "Problems" => r.Action == PlanAction.Duplicate || r.Action == PlanAction.Locked || r.GridCheck,
                _ => true
            };
        }

        // ── Writing ───────────────────────────────────────────────────────────

        /// <summary>Confirms, then writes the plan in one transaction.</summary>
        [RelayCommand(CanExecute = nameof(CanWrite))]
        private void Write()
        {
            var rows = _plan?.ToList();
            var ticked = TickedCategories;
            if (rows == null || ticked.Count == 0) return;

            int locked = rows.Count(r => r.Action == PlanAction.Locked);
            string what = ticked.Count == 1 ? ticked[0].Name : $"{ticked.Count} categories ({string.Join(", ", ticked.Select(c => c.Name))})";
            var question = new StringBuilder($"Write {NewCount} new Tracker ID(s) to {what}?\n\n");
            question.Append($"• {HaveIdCount} element(s) keep their ID\n");
            if (CopyCount > 0) question.Append($"• {CopyCount} copied element(s) get a new ID\n");
            if (locked > 0) question.Append($"• {locked} locked element(s) are skipped\n");
            if (WriteLocationParameters) question.Append("• Tracker_Zone, Tracker_Level and Tracker_Grid are refreshed\n");
            if (Segments.FirstOrDefault(s => s.Kind == SegmentKind.Model)?.IsEnabled == false)
                question.Append("\nWARNING: the Model segment is off, so these IDs may repeat IDs in other Revit models.\n");
            question.Append("\nMissing Tracker parameters are added to the project. Ctrl+Z in Revit undoes the whole run.");
            if (!Confirm(question.ToString())) return;

            bool writeLocation = WriteLocationParameters;
            bool writeGuid = WriteSourceGuid;
            var categoryIds = new HashSet<long>(ticked.Select(c => c.Info.Id));
            var typeIds = new HashSet<long>(Types.Where(t => t.IsSelected).Select(t => t.Info.Id));
            var needs = NeededParameterNames();
            var scope = _scope;
            string key = SnapshotKey();
            string typesKey = TypesKey(ticked);

            AddLog(LogLevel.Info, $"Writing: {NewCount} new ID(s), {rows.Count} element(s) in scope…");
            Run("Writing Tracker IDs…", app =>
            {
                var doc = DocumentFor(app);
                if (doc == null) return;

                var revitCategories = categoryIds.Select(id => Category.GetCategory(doc, new ElementId(id))).Where(c => c != null).ToList();
                if (revitCategories.Count != categoryIds.Count)
                {
                    OnUi(() => AddLog(LogLevel.Error, "A ticked category no longer exists in the model. Click Re-read model."));
                    return;
                }

                var result = TrackerWriter.Write(doc, revitCategories, rows, writeLocation, writeGuid);

                // Read everything again so the grids and counts show the model as it now is.
                var categories = ModelReader.ReadCategories(doc, scope);
                var types = ModelReader.ReadTypes(doc, categoryIds, scope);
                var read = ReadElements(doc, categoryIds, typeIds, needs, scope);

                OnUi(() =>
                {
                    ReportWrite(result);
                    ApplyRefreshedLists(categories, typesKey, types, typeIds);
                    ApplyElements(read, key, logCounts: false);
                    if (result.Committed) SaveSettings();
                });
            });
        }

        private bool CanWrite() =>
            IsStep3 && !IsRunning && _plan != null && DuplicateCount == 0 && _plan.Any(r => r.Action != PlanAction.Locked);

        private void ReportWrite(WriteResult result)
        {
            if (result.Setup != null)
            {
                if (result.Setup.Created.Count > 0) AddLog(LogLevel.Info, "Added to the project: " + string.Join(", ", result.Setup.Created));
                if (result.Setup.Extended.Count > 0) AddLog(LogLevel.Info, "Bound to the ticked categories: " + string.Join(", ", result.Setup.Extended));
                foreach (var w in result.Setup.Warnings) AddLog(LogLevel.Warning, w);
            }

            if (result.Committed)
            {
                AddLog(LogLevel.Success, $"Wrote {result.IdsWritten} Tracker ID(s); stamped {result.GuidsStamped} source GUID(s); updated the location of {result.LocationsUpdated} element(s)." +
                                         (result.Skipped > 0 ? $" Skipped {result.Skipped} locked element(s)." : string.Empty));
                SummaryText = $"Wrote {result.IdsWritten} Tracker ID(s).";
                return;
            }

            foreach (var e in result.Errors) AddLog(result.Stale ? LogLevel.Warning : LogLevel.Error, e);
            SummaryText = result.Stale
                ? "The model changed since the preview, so nothing was written. The preview is refreshed; check it and write again."
                : "Nothing was written: " + (result.Errors.FirstOrDefault() ?? "unknown problem.");
        }

        // Shows the re-read categories and types without losing the user's category and type choice.
        private void ApplyRefreshedLists(List<CategoryInfo> categories, string typesKey, List<TypeInfo> types, ISet<long> tickedTypeIds)
        {
            _suspendTypeLoad = true;
            try { ShowCategories(categories); }
            finally { _suspendTypeLoad = false; }

            _typeCache.Clear();
            _typeCache[typesKey] = types;
            ShowTypes(types);
            foreach (var t in Types.Where(t => tickedTypeIds.Contains(t.Info.Id))) t.IsSelected = true;
        }
    }
}
