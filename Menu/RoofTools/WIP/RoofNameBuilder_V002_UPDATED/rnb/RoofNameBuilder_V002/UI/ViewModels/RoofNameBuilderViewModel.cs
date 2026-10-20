#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.RoofNameBuilder.V002.Core.Models;
using Revit26_Plugin_Revised.RoofNameBuilder.V002.Core.Services;
using Revit26_Plugin_Revised.RoofNameBuilder.V002.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.RoofNameBuilder.V002.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;

namespace Revit26_Plugin_Revised.RoofNameBuilder.V002.UI.ViewModels
{
    /// <summary>
    /// Roof Name Builder V002 view model.
    ///
    /// Derives from Shared ToolViewModelBase, which supplies IsRunning / Progress / SummaryText (bound
    /// by ToolWindowShell), the LogEntries collection and the ClearLog / CopyAllLog / CopySelectedLog
    /// commands — V001's own Log collection and status properties are gone.
    ///
    /// Filtering follows UI_UX_PATTERNS.md: one ICollectionView, filter state as observable properties,
    /// each one's On…Changed refreshes the view. Scope / Mode chips are bound one-way through
    /// EnumToBoolConverter and changed through SetScope / SetMode commands (the converter has no
    /// ConvertBack, so V001's two-way chip bindings never reached the view model).
    /// </summary>
    public partial class RoofNameBuilderViewModel : ToolViewModelBase
    {
        private const string SettingsKey = "RoofNameBuilder";
        private static readonly Regex ValidCode = new("^[A-Za-z0-9]+$", RegexOptions.Compiled);

        private readonly Document _doc;
        private readonly ICollection<ElementId> _currentSelection;
        private readonly Dispatcher _dispatcher;
        private readonly RoofNameBuilderSettings _settings;

        private List<NameCodeDefinition> _applied = new();
        private List<string> _reservedNames = new();
        private string _lastRunSummary = string.Empty;
        private bool _suppressReload;

        [ObservableProperty] private RoofNameScope scope;
        [ObservableProperty] private RoofNameMode mode;
        [ObservableProperty] private string searchText = string.Empty;
        [ObservableProperty] private bool pendingOnly;
        [ObservableProperty] private bool hideCompliant;
        [ObservableProperty] private bool settingsOpen;

        [ObservableProperty] private string templateSeparator;
        [ObservableProperty] private string instanceTargetParameter;
        [ObservableProperty] private bool parseExistingCodes;

        [ObservableProperty] private int loadedCount;
        [ObservableProperty] private int pendingCount;
        [ObservableProperty] private int conflictCount;
        [ObservableProperty] private int selectedCount;
        [ObservableProperty] private bool hasSelection;

        public ObservableCollection<RoofNameRowViewModel> Rows { get; } = new();
        public ICollectionView RowsView { get; }

        /// <summary>Editable copy shown in the Code Definitions grid. Takes effect on "Apply &amp; reload".</summary>
        public ObservableCollection<NameCodeDefinition> CodeDefinitions { get; }

        /// <summary>The definitions currently driving the grid columns and bulk buttons.</summary>
        public ObservableCollection<NameCodeDefinition> AppliedCodeDefinitions { get; } = new();

        /// <summary>Raised after the applied definitions change, so the window can rebuild its code columns.</summary>
        public event EventHandler? CodeColumnsChanged;

        public bool IsTypesMode => Mode == RoofNameMode.Types;

        public RoofNameBuilderViewModel(Document doc, ICollection<ElementId> currentSelection)
        {
            _doc = doc;
            _currentSelection = currentSelection;
            _dispatcher = Dispatcher.CurrentDispatcher;

            _settings = SettingsService<RoofNameBuilderSettings>.Load(SettingsKey);
            _settings.CodeDefinitions ??= new RoofNameBuilderSettings().CodeDefinitions;

            CodeDefinitions = new ObservableCollection<NameCodeDefinition>(_settings.CodeDefinitions.Select(Clone));
            SetApplied(_settings.CodeDefinitions);

            _suppressReload = true;
            templateSeparator = _settings.TemplateSeparator ?? "_";
            instanceTargetParameter = string.IsNullOrWhiteSpace(_settings.InstanceTargetParameter) ? "Mark" : _settings.InstanceTargetParameter;
            parseExistingCodes = _settings.ParseExistingCodes;
            hideCompliant = _settings.SkipAlreadyCompliant;
            mode = _settings.LastMode;
            // A selection made before launching wins over the remembered scope.
            // With nothing selected, a remembered "Current selection" scope falls back to the whole project.
            scope = _currentSelection.Count > 0 ? RoofNameScope.CurrentSelection
                  : _settings.LastScope == RoofNameScope.CurrentSelection ? RoofNameScope.AllInProject
                  : _settings.LastScope;
            _suppressReload = false;

            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.Filter = PassesFilter;

            RoofNameBuilderEventManager.Init(); // called from the ViewModel — see ARCHITECTURE.md §2

            LoadRows();
        }

        // ── Filter / scope changes ───────────────────────────────────────────
        partial void OnSearchTextChanged(string value) => RowsView.Refresh();
        partial void OnPendingOnlyChanged(bool value) => RowsView.Refresh();
        partial void OnHideCompliantChanged(bool value) => RowsView.Refresh();

        partial void OnScopeChanged(RoofNameScope value)
        {
            if (_suppressReload) return;
            _settings.LastScope = value;
            LoadRows();
        }

        partial void OnModeChanged(RoofNameMode value)
        {
            OnPropertyChanged(nameof(IsTypesMode));
            if (_suppressReload) return;
            _settings.LastMode = value;
            LoadRows();
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.PropertyName == nameof(IsRunning))
                RunCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void SetScope(string? value)
        {
            if (IsRunning) return;
            if (Enum.TryParse<RoofNameScope>(value, out var s)) Scope = s;
        }

        [RelayCommand]
        private void SetMode(string? value)
        {
            if (IsRunning) return;
            if (Enum.TryParse<RoofNameMode>(value, out var m)) Mode = m;
        }

        private bool PassesFilter(object obj)
        {
            if (obj is not RoofNameRowViewModel row) return false;
            if (PendingOnly && !row.IsPending) return false;
            if (HideCompliant && row.IsCompliant) return false;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var s = SearchText.Trim();
                if (row.CurrentName.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0 &&
                    row.BaseName.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0 &&
                    row.NewName.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0 &&
                    row.TypeName.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            return true;
        }

        // ── Load ─────────────────────────────────────────────────────────────
        [RelayCommand]
        private void LoadRows()
        {
            if (IsRunning) return;

            foreach (var old in Rows) old.PropertyChanged -= RowOnPropertyChanged;
            Rows.Clear();
            _reservedNames = new List<string>();

            try
            {
                var defs = _applied;
                var separator = TemplateSeparator ?? string.Empty;

                var sourceRows = Mode == RoofNameMode.Types
                    ? RoofParameterHelper.CollectTypeRows(_doc, Scope, _currentSelection, defs)
                    : RoofParameterHelper.CollectInstanceRows(_doc, Scope, _currentSelection, defs, InstanceTargetParameter);

                if (Mode == RoofNameMode.Types)
                {
                    // Type names outside the loaded rows — a new name must not collide with these either.
                    var loaded = new HashSet<string>(sourceRows.Select(r => r.CurrentName), StringComparer.Ordinal);
                    _reservedNames = RoofParameterHelper.CollectAllRoofTypeNames(_doc).Where(n => !loaded.Contains(n)).ToList();
                }
                else if (!RoofParameterHelper.IsWritableTextParameter(_doc, InstanceTargetParameter))
                {
                    AddLog(LogLevel.Warning, $"'{InstanceTargetParameter}' is not a writable text parameter on roofs — Run will fail for every row.");
                }

                var parsedCount = 0;
                foreach (var r in sourceRows)
                {
                    if (ParseExistingCodes && ApplyExistingCodes(r, defs, separator)) parsedCount++;

                    var vm = new RoofNameRowViewModel(
                        r, defs, separator, _settings.NameLengthWarningThreshold,
                        validateTypeNameCharacters: Mode == RoofNameMode.Types);

                    vm.PropertyChanged += RowOnPropertyChanged;
                    Rows.Add(vm);
                }

                LoadedCount = Rows.Count;
                var what = Mode == RoofNameMode.Types ? "roof types" : "roof instances";
                AddLog(LogLevel.Info, $"Loaded {Rows.Count} {what} — scope: {DescribeScope(Scope)}" +
                                      (parsedCount > 0 ? $"; {parsedCount} already carry codes" : string.Empty));

                if (Scope == RoofNameScope.CurrentSelection && Rows.Count == 0)
                    AddLog(LogLevel.Warning, "No roofs in the selection made before the tool was opened. Pick another scope, or close, select roofs and reopen.");
            }
            catch (Exception ex)
            {
                LoadedCount = Rows.Count;
                AddLog(LogLevel.Error, $"Failed to load roofs: {ex.Message}");
            }

            RecomputeConflictsAndSummary();
            UpdateSelectionCounts();
            RowsView.Refresh();
        }

        /// <summary>Splits existing code segments off the current name; true when any were found.</summary>
        private static bool ApplyExistingCodes(RoofNameRow row, IReadOnlyList<NameCodeDefinition> defs, string separator)
        {
            var paramValues = row.Codes.ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.OrdinalIgnoreCase);
            var parsed = NameBuilderService.ParseName(row.CurrentName, defs, separator, paramValues);
            if (parsed.FoundCodes.Count == 0) return false;

            row.BaseName = parsed.BaseName;
            foreach (var (code, valueInName) in parsed.FoundCodes)
            {
                if (!row.Codes.TryGetValue(code, out var cv)) continue;
                cv.Ticked = true;
                // The parameter's current value wins (that's what makes stale names show as pending);
                // if the parameter is empty/missing, keep what the name already says.
                if (string.IsNullOrWhiteSpace(cv.Value)) cv.Value = valueInName;
            }
            return true;
        }

        private void RowOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(RoofNameRowViewModel.IsSelected):
                    UpdateSelectionCounts();
                    break;
                case nameof(RoofNameRowViewModel.NewName):
                    RecomputeConflictsAndSummary();
                    break;
            }
        }

        private void UpdateSelectionCounts()
        {
            SelectedCount = Rows.Count(r => r.IsSelected);
            HasSelection = SelectedCount > 0;
        }

        private void RecomputeConflictsAndSummary()
        {
            var candidates = Rows.Select(r => new NameCandidate(r.ElementId, r.IsPending ? r.NewName : r.CurrentName, r.IsPending));
            var conflictIds = NameBuilderService.FindConflictingRowIds(candidates, _reservedNames, _settings.CaseInsensitiveDuplicateCheck);

            foreach (var row in Rows)
                row.IsDuplicate = conflictIds.Contains(row.ElementId);

            PendingCount = Rows.Count(r => r.IsPending);
            ConflictCount = Rows.Count(r => r.HasBlockingIssue);

            var status = ConflictCount > 0
                ? $"{PendingCount} of {Rows.Count} will be renamed — fix {ConflictCount} conflict(s) to enable Run"
                : $"{PendingCount} of {Rows.Count} will be renamed on Run";
            SummaryText = string.IsNullOrEmpty(_lastRunSummary) ? status : $"{_lastRunSummary} · {status}";

            RunCommand.NotifyCanExecuteChanged();
        }

        // ── Run ──────────────────────────────────────────────────────────────
        private bool CanRun() => !IsRunning && ConflictCount == 0 && PendingCount > 0;

        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            var pending = Rows.Where(r => r.IsPending).ToList();
            if (pending.Count == 0) return;

            var isType = Mode == RoofNameMode.Types;
            var payload = new RenamePayload();
            foreach (var row in pending)
            {
                payload.Items.Add(new RenameItem
                {
                    ElementId = row.ElementId,
                    NewName = row.NewName,
                    IsType = isType,
                    InstanceParameterName = InstanceTargetParameter
                });
            }

            var labels = pending.ToDictionary(r => r.ElementId, r => string.IsNullOrEmpty(r.CurrentName) ? $"#{r.ElementId}" : r.CurrentName);

            var handler = RoofNameBuilderEventManager.Handler!;
            handler.PendingPayload = payload;
            handler.TargetDocument = _doc;
            handler.OnCompleted = results => _dispatcher.Invoke(() => OnRunCompleted(results, labels));

            IsRunning = true;
            SummaryText = $"Renaming {payload.Items.Count} roof(s)…";
            AddLog(LogLevel.Info, $"Run started — {payload.Items.Count} {(isType ? "type" : "instance")} rename(s).");

            var request = RoofNameBuilderEventManager.Raise();
            if (request != ExternalEventRequest.Accepted)
            {
                handler.PendingPayload = null;
                handler.OnCompleted = null;
                IsRunning = false;
                AddLog(LogLevel.Error, $"Revit did not accept the rename request ({request}). Nothing was changed.");
                RecomputeConflictsAndSummary();
            }
        }

        private void OnRunCompleted(List<RenameRowResult> results, IReadOnlyDictionary<long, string> labels)
        {
            var ok = 0;
            var failed = 0;
            foreach (var r in results)
            {
                var label = labels.TryGetValue(r.ElementId, out var l) ? l : $"#{r.ElementId}";
                if (r.Success) ok++; else failed++;

                AddLog(r.Success ? LogLevel.Success : LogLevel.Error, $"{label}: {r.Message}");
                foreach (var w in r.Warnings)
                    AddLog(LogLevel.Warning, $"{label}: Revit warning — {w}");
            }

            _lastRunSummary = failed == 0 ? $"Last run: {ok} renamed" : $"Last run: {ok} renamed, {failed} failed";
            AddLog(failed == 0 ? LogLevel.Success : LogLevel.Warning, _lastRunSummary.Replace("Last run: ", "Run finished — "));
            LogExportService.AutoSave(LogEntries, "Roof Name Builder");

            IsRunning = false;
            SaveSettings();
            LoadRows(); // refresh from the model post-rename
        }

        // ── Bulk actions ─────────────────────────────────────────────────────
        [RelayCommand]
        private void BulkTick(string? code) => SetTick(code, true);

        [RelayCommand]
        private void BulkUntick(string? code) => SetTick(code, false);

        private void SetTick(string? code, bool ticked)
        {
            if (string.IsNullOrEmpty(code)) return;
            var count = 0;
            foreach (var row in Rows.Where(r => r.IsSelected))
            {
                if (!row.Codes.TryGetValue(code, out var cv) || cv.Ticked == ticked) continue;
                cv.Ticked = ticked;
                count++;
            }
            AddLog(LogLevel.Info, $"{(ticked ? "Ticked" : "Unticked")} {code} on {count} selected row(s).");
        }

        [RelayCommand]
        private void SelectShown()
        {
            foreach (var row in RowsView.Cast<RoofNameRowViewModel>()) row.IsSelected = true;
        }

        [RelayCommand]
        private void ClearSelection()
        {
            foreach (var row in Rows) row.IsSelected = false;
        }

        // ── Code definitions / settings ──────────────────────────────────────
        [RelayCommand]
        private void ApplyDefinitions()
        {
            if (IsRunning) return;

            var defs = CodeDefinitions
                .Where(d => !string.IsNullOrWhiteSpace(d.Code))
                .Select(d => { var c = Clone(d); c.Code = c.Code.Trim(); return c; })
                .ToList();

            var bad = defs.Where(d => !ValidCode.IsMatch(d.Code)).Select(d => d.Code).ToList();
            if (bad.Count > 0)
            {
                AddLog(LogLevel.Error, $"Codes may only contain letters and digits: {string.Join(", ", bad)}. Nothing applied.");
                return;
            }

            var dupes = defs.GroupBy(d => d.Code, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupes.Count > 0)
            {
                AddLog(LogLevel.Error, $"Each code must be unique: {string.Join(", ", dupes)}. Nothing applied.");
                return;
            }

            if (string.IsNullOrWhiteSpace(InstanceTargetParameter))
            {
                AddLog(LogLevel.Error, "Instance target parameter can't be empty. Nothing applied.");
                return;
            }

            if (string.IsNullOrEmpty(TemplateSeparator))
                AddLog(LogLevel.Warning, "Separator is empty — codes will be glued to the base name and existing codes can't be recognised.");

            _settings.CodeDefinitions = defs;
            _settings.TemplateSeparator = TemplateSeparator ?? string.Empty;
            _settings.InstanceTargetParameter = InstanceTargetParameter.Trim();
            _settings.ParseExistingCodes = ParseExistingCodes;
            InstanceTargetParameter = _settings.InstanceTargetParameter;

            SetApplied(defs);
            SaveSettings();
            AddLog(LogLevel.Info, $"Applied {defs.Count} code definition(s): {string.Join(", ", defs.OrderBy(d => d.Order).Select(d => d.Code))}.");
            LoadRows();
        }

        private void SetApplied(IEnumerable<NameCodeDefinition> defs)
        {
            _applied = defs.Where(d => !string.IsNullOrWhiteSpace(d.Code)).Select(Clone).OrderBy(d => d.Order).ToList();
            AppliedCodeDefinitions.Clear();
            foreach (var d in _applied) AppliedCodeDefinitions.Add(d);
            CodeColumnsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SaveSettings()
        {
            _settings.LastScope = Scope;
            _settings.LastMode = Mode;
            _settings.SkipAlreadyCompliant = HideCompliant;
            SettingsService<RoofNameBuilderSettings>.Save(SettingsKey, _settings);
        }

        /// <summary>Called by the window on Closed — persists scope/mode/filter state.</summary>
        public void OnWindowClosed()
        {
            SaveSettings();
            var handler = RoofNameBuilderEventManager.Handler;
            if (handler != null && !IsRunning) handler.OnCompleted = null;
        }

        /// <summary>Writes the log to <paramref name="folder"/> through Shared LogExportService.</summary>
        public void ExportLog(string folder)
        {
            try
            {
                var path = LogExportService.Export(SettingsKey, LogEntries, folder);
                AddLog(LogLevel.Success, $"Log exported to {path}");
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"Log export failed: {ex.Message}");
            }
        }

        private static string DescribeScope(RoofNameScope s) => s switch
        {
            RoofNameScope.AllInProject => "all in project",
            RoofNameScope.ActiveView => "active view",
            RoofNameScope.UsedTypesOnly => "used types only",
            RoofNameScope.CurrentSelection => "current selection",
            _ => s.ToString()
        };

        private static NameCodeDefinition Clone(NameCodeDefinition d) => new()
        {
            Code = d.Code,
            Label = d.Label,
            SourceParameter = d.SourceParameter,
            Unit = d.Unit,
            IncludeCodePrefix = d.IncludeCodePrefix,
            UppercaseValue = d.UppercaseValue,
            Order = d.Order
        };
    }
}
