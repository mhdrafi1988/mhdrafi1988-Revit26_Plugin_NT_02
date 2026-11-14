using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Matching;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Report;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Run;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.UI.ViewModels
{
    /// <summary>
    /// View model of the Wet Area Waterproofing window (Part A): the room grid with its filters,
    /// the roof and outline-repair settings, the run, the Excel report and "Remove a run".
    /// </summary>
    public sealed partial class ViewModel_WetAreaWaterproofing : ToolViewModelBase, IDisposable
    {
        /// <summary>Folder under %AppData%\Revit26_Plugin_Revised holding this tool's settings.</summary>
        public const string SettingsFolder = "WetAreaWaterproofing";

        private readonly IWetAreaSession _session;
        private readonly WetAreaSettings _settings;
        private readonly HashSet<string> _ticked = new();
        private List<RoomCandidate> _all = new();
        private RunReport _lastReport;
        private bool _loading;

        /// <summary>Creates the view model on an open session and fills it from the first model read.</summary>
        public ViewModel_WetAreaWaterproofing(IWetAreaSession session, WetAreaSettings settings)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _settings = (settings ?? new WetAreaSettings()).Normalize();
            RowsView = new ListCollectionView(Rows);

            _loading = true;
            _wetOnly = State.WetOnly;
            _searchText = State.Search;
            _groupBy = GroupChoices.Contains(State.GroupBy) ? State.GroupBy : "Level";
            _keywordsText = string.Join(", ", _settings.Keywords);
            _includeHost = _settings.IncludeHost;
            _includeLinks = _settings.IncludeLinks;
            _offsetText = Text(_settings.OffsetMm);
            _snapText = Text(_settings.SnapMm);
            _bridgeText = Text(_settings.BridgeMm);
            _areaToleranceText = Text(_settings.AreaTolerancePct);
            _allowApproximate = _settings.AllowApproximate;
            _writeComments = _settings.WriteComments;
            _autoSaveReport = _settings.AutoSaveReport;
            _reportFolder = _settings.ReportFolder;
            _loading = false;

            ApplySnapshot(session.Initial, firstLoad: true);
            AddLog(LogLevel.Info, $"Read {_all.Count} room(s); {_all.Count(c => !string.IsNullOrEmpty(c.Row.Matched))} match a wet-area keyword.");
        }

        private GridState State => _settings.Grid;

        /// <summary>Settings as they are now, for saving on close.</summary>
        public WetAreaSettings Settings => _settings;

        // ── Rooms ───────────────────────────────────────────────────────────────────────────────

        /// <summary>Rows shown in the grid, after filters and sorting.</summary>
        public ObservableCollection<RoomRowViewModel> Rows { get; } = new();

        /// <summary>Grouped view over <see cref="Rows"/> for the DataGrid.</summary>
        public ListCollectionView RowsView { get; }

        /// <summary>Level buttons above the grid: "All", then one per level, lowest first.</summary>
        public ObservableCollection<LevelButtonViewModel> LevelButtons { get; } = new();

        /// <summary>Loaded links, ticked when their rooms are read.</summary>
        public ObservableCollection<FilterItemViewModel> LinkItems { get; } = new();

        /// <summary>Group-by choices.</summary>
        public IReadOnlyList<string> GroupChoices { get; } = new[] { "None", "Level", "Source", "Matched" };

        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private bool _includeHost;
        [ObservableProperty] private bool _includeLinks;
        [ObservableProperty] private string _keywordsText;
        [ObservableProperty] private bool _wetOnly;
        [ObservableProperty] private string _searchText;
        [ObservableProperty] private string _groupBy;
        [ObservableProperty] private string _statusText = string.Empty;
        [ObservableProperty] private string _attentionText = string.Empty;
        [ObservableProperty] private bool _hasAttention;
        [ObservableProperty] private string _runButtonText = "Run";
        [ObservableProperty] private bool _needsRefresh;

        // ── Roof and repair settings ────────────────────────────────────────────────────────────

        /// <summary>Roof types in the project.</summary>
        public ObservableCollection<string> RoofTypes { get; } = new();

        /// <summary>User worksets; empty when the model is not workshared.</summary>
        public ObservableCollection<string> Worksets { get; } = new();

        [ObservableProperty] private string _selectedRoofType;
        [ObservableProperty] private string _selectedWorkset;
        [ObservableProperty] private bool _isWorkshared;
        [ObservableProperty] private string _offsetText;
        [ObservableProperty] private bool _writeComments;
        [ObservableProperty] private bool _allowApproximate;
        [ObservableProperty] private string _snapText;
        [ObservableProperty] private string _bridgeText;
        [ObservableProperty] private string _areaToleranceText;
        [ObservableProperty] private bool _autoSaveReport;
        [ObservableProperty] private string _reportFolder;

        partial void OnWetOnlyChanged(bool value) { State.WetOnly = value; RebuildRows(); }
        partial void OnSearchTextChanged(string value) { State.Search = value ?? string.Empty; RebuildRows(); }
        partial void OnGroupByChanged(string value) { State.GroupBy = value ?? "None"; RebuildRows(); }
        partial void OnIncludeHostChanged(bool value) => MarkNeedsRefresh();
        partial void OnIncludeLinksChanged(bool value) => MarkNeedsRefresh();
        partial void OnSnapTextChanged(string value) => MarkNeedsRefresh();
        partial void OnBridgeTextChanged(string value) => MarkNeedsRefresh();
        partial void OnAreaToleranceTextChanged(string value) => MarkNeedsRefresh();
        partial void OnAllowApproximateChanged(bool value) => MarkNeedsRefresh();

        private void MarkNeedsRefresh()
        {
            if (!_loading) NeedsRefresh = true;
        }

        /// <summary>Called by the window when a link is ticked or unticked.</summary>
        public void LinkSelectionChanged() => MarkNeedsRefresh();

        // ── Commands: model ─────────────────────────────────────────────────────────────────────

        /// <summary>Reads the model again with the current sources and repair settings.</summary>
        [RelayCommand]
        private void Refresh()
        {
            if (IsBusy || !PushSettings()) return;
            IsBusy = true;
            AddLog(LogLevel.Info, "Reading the model…");
            _session.Refresh(_settings, (snapshot, error) =>
            {
                IsBusy = false;
                if (snapshot == null)
                {
                    AddLog(LogLevel.Error, "The model could not be read: " + error);
                    return;
                }
                ApplySnapshot(snapshot, firstLoad: false);
                NeedsRefresh = false;
                AddLog(LogLevel.Success, $"Read {_all.Count} room(s).");
            });
        }

        /// <summary>Applies the keyword box: marks the wet rooms again and ticks them.</summary>
        [RelayCommand]
        private void ApplyKeywords()
        {
            _settings.Keywords = (KeywordsText ?? string.Empty).Split(new[] { ',', ';' }).ToList();
            _settings.Normalize();
            _loading = true;
            KeywordsText = string.Join(", ", _settings.Keywords);
            _loading = false;

            foreach (RoomCandidate c in _all) c.Row.Matched = KeywordMatcher.Match(c.Row.Name, _settings.Keywords);
            TickDefaults();
            RebuildRows();
            AddLog(LogLevel.Info, $"Keywords applied: {_all.Count(c => !string.IsNullOrEmpty(c.Row.Matched))} wet room(s) found; ticks reset.");
        }

        private void ApplySnapshot(ModelSnapshot snapshot, bool firstLoad)
        {
            _all = snapshot.Rooms.ToList();
            foreach (string note in snapshot.Notes) AddLog(LogLevel.Warning, note);

            _loading = true;
            Replace(RoofTypes, snapshot.RoofTypes);
            SelectedRoofType = RoofTypes.Contains(SelectedRoofType ?? _settings.RoofTypeName)
                ? SelectedRoofType ?? _settings.RoofTypeName : RoofTypes.FirstOrDefault();

            Replace(Worksets, snapshot.Worksets);
            IsWorkshared = Worksets.Count > 0;
            string wanted = SelectedWorkset ?? _settings.WorksetName;
            SelectedWorkset = Worksets.Contains(wanted) ? wanted
                : Worksets.Contains(snapshot.ActiveWorkset) ? snapshot.ActiveWorkset : Worksets.FirstOrDefault();

            var chosen = firstLoad
                ? new HashSet<string>(_settings.IncludedLinks)
                : new HashSet<string>(LinkItems.Where(l => l.IsChecked).Select(l => l.Text));
            bool allLinks = firstLoad ? _settings.IncludedLinks.Count == 0 : LinkItems.Count == 0;
            LinkItems.Clear();
            foreach (string link in snapshot.Links)
                LinkItems.Add(new FilterItemViewModel(link, allLinks || chosen.Contains(link)));
            _loading = false;

            var keys = new HashSet<string>(_all.Select(c => c.Row.RoomKey));
            if (firstLoad) TickDefaults();
            else
            {
                _ticked.RemoveWhere(k => !keys.Contains(k));
                // A room that can no longer run loses its tick; the grid says why.
                foreach (RoomCandidate c in _all.Where(c => c.Row.Status is RoomStatus.Owned or RoomStatus.NotEnclosed or RoomStatus.Failed))
                    _ticked.Remove(c.Row.RoomKey);
            }
            State.Levels.RemoveWhere(l => _all.All(c => (c.Row.Level ?? GridQuery.NoValue) != l));
            RebuildRows();
        }

        /// <summary>Ticks every wet room a run can do that has no roof from an earlier run.</summary>
        private void TickDefaults()
        {
            _ticked.Clear();
            foreach (RoomCandidate c in _all)
                if (!string.IsNullOrEmpty(c.Row.Matched)
                    && c.Row.Status is not (RoomStatus.Owned or RoomStatus.NotEnclosed or RoomStatus.Failed or RoomStatus.AlreadyDone))
                    _ticked.Add(c.Row.RoomKey);
        }

        // ── Settings ────────────────────────────────────────────────────────────────────────────

        /// <summary>Copies the window's values into the settings; false (with a log line) when a number is not valid.</summary>
        private bool PushSettings()
        {
            if (!Number(OffsetText, "Offset above slab", -5000, 5000, out double offset)
                || !Number(SnapText, "Snap gaps up to", 0.1, 100, out double snap)
                || !Number(BridgeText, "Bridge gaps up to", 0.1, 1000, out double bridge)
                || !Number(AreaToleranceText, "Area check", 0.1, 50, out double tolerance)) return false;
            if (bridge < snap)
            {
                AddLog(LogLevel.Error, "\"Bridge gaps up to\" must not be smaller than \"Snap gaps up to\".");
                return false;
            }

            _settings.OffsetMm = offset;
            _settings.SnapMm = snap;
            _settings.BridgeMm = bridge;
            _settings.AreaTolerancePct = tolerance;
            _settings.AllowApproximate = AllowApproximate;
            _settings.WriteComments = WriteComments;
            _settings.RoofTypeName = SelectedRoofType ?? string.Empty;
            _settings.WorksetName = IsWorkshared ? SelectedWorkset ?? string.Empty : string.Empty;
            _settings.IncludeHost = IncludeHost;
            _settings.IncludeLinks = IncludeLinks;
            _settings.IncludedLinks = LinkItems.All(l => l.IsChecked)
                ? new List<string>() : LinkItems.Where(l => l.IsChecked).Select(l => l.Text).ToList();
            if (IncludeLinks && LinkItems.Count > 0 && LinkItems.All(l => !l.IsChecked)) _settings.IncludeLinks = false;
            _settings.AutoSaveReport = AutoSaveReport;
            _settings.ReportFolder = ReportFolder;
            _settings.Normalize();
            return true;
        }

        /// <summary>Saves the settings file; a failure is logged, never thrown.</summary>
        public void SaveSettings()
        {
            try
            {
                PushSettings();
                SettingsService<WetAreaSettings>.Save(SettingsFolder, _settings);
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Warning, "Settings could not be saved: " + ex.Message);
            }
        }

        private bool Number(string text, string label, double min, double max, out double value)
        {
            bool ok = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                   || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            if (ok && value >= min && value <= max) return true;
            AddLog(LogLevel.Error, $"\"{label}\" must be a number from {Text(min)} to {Text(max)}.");
            return false;
        }

        private static string Text(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);

        private static void Replace(ObservableCollection<string> target, IEnumerable<string> items)
        {
            target.Clear();
            foreach (string item in items) target.Add(item);
        }

        /// <summary>Releases the Revit session.</summary>
        public void Dispose() => _session.Dispose();
    }
}
