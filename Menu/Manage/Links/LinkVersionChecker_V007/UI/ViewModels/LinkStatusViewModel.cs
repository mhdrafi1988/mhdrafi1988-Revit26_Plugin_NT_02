using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Models;
using Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Services;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V007.UI.ViewModels
{
    /// <summary>One entry of a filter drop-down: the value the filter compares, and the text shown.  [V007]</summary>
    public sealed class FilterOption
    {
        /// <summary>Creates an option; an empty <paramref name="value"/> means "no filter".</summary>
        public FilterOption(string value, string text) { Value = value; Text = text; }

        /// <summary>Value the filter compares; empty means "All".</summary>
        public string Value { get; }

        /// <summary>Text shown in the drop-down.</summary>
        public string Text { get; }
    }

    /// <summary>One active filter shown as a removable chip under the filter bar.  [V007]</summary>
    public sealed class FilterChip
    {
        /// <summary>Creates a chip for filter <paramref name="key"/>.</summary>
        public FilterChip(string key, string text) { Key = key; Text = text; }

        /// <summary>Which filter the chip clears (passed to RemoveFilterCommand).</summary>
        public string Key { get; }

        /// <summary>"Source: Cloud link".</summary>
        public string Text { get; }
    }

    /// <summary>
    /// View model of the Link Version Checker V007 window: the status of every Revit link from
    /// ACC (Desktop Connector files and cloud links), count tiles that double as filters, filter
    /// drop-downs, a folder display setting, and actions on the selected links (Reload / Unload
    /// need exactly one).
    /// </summary>
    public sealed class LinkStatusViewModel : ObservableObject
    {
        /// <summary>Above this many Explorer windows, Open folder asks first.</summary>
        public const int OpenFolderConfirmAbove = 10;

        private static readonly Dictionary<string, string> TileLabels = new()
        {
            ["SrcDc"] = "Desktop Connector", ["SrcCloud"] = "Cloud link", ["SrcOther"] = "Other path",
            ["Loaded"] = "Loaded", ["Unloaded"] = "Unloaded", ["NotFound"] = "Not found",
            ["AlwaysKept"] = "Always kept", ["Available"] = "Available locally", ["OnlineOnly"] = "Online only",
            ["SyncPending"] = "Sync pending", ["OtherDc"] = "Missing / other",
            ["UpToDate"] = "Up to date", ["ReloadNeeded"] = "Reload needed", ["LatestUnknown"] = "Unknown",
            ["NotChecked"] = "Not checked"
        };

        private readonly Document _doc;
        private readonly LinkStatusSettings _settings;
        private string _tile = "";
        private string _search = "";
        private bool _problemsOnly;
        private string _sourceFilter = "";
        private string _projectFilter = "";
        private string _folderFilter = "";
        private string _modelFilter = "";
        private string _latestFilter = "";
        private LinkStatusRow _selected;
        private IReadOnlyList<LinkStatusRow> _selection = Array.Empty<LinkStatusRow>();
        private string _status = "";
        private string _projectRoot = "";
        private string _hostProject = "";
        private string _hostAccount = "";
        private DateTime _lastRefresh;
        private bool _rebuilding;   // the drop-downs clear their selection while their lists are rebuilt; ignore that

        /// <summary>
        /// Raised after a refresh with the rows that should be selected again (the grid's
        /// multi-selection cannot be bound, so the window applies it).
        /// </summary>
        public event Action<IReadOnlyList<LinkStatusRow>> RestoreSelection;

        /// <summary>Creates the view model and runs the first scan.</summary>
        public LinkStatusViewModel(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _settings = LinkStatusSettings.Load();
            HostName = string.IsNullOrEmpty(doc.Title) ? "(unsaved model)" : doc.Title;

            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.Filter = o => o is LinkStatusRow r && Matches(r, withTile: true);

            RefreshCommand = new RelayCommand(() => Refresh());
            CheckCloudCommand = new RelayCommand(CheckCloud, () => Rows.Any(CloudVersionChecker.CanCheck));
            ExportCommand = new RelayCommand(Export, () => Rows.Count > 0);
            SetTileCommand = new RelayCommand<string>(t => ActiveTile = ActiveTile == t ? "" : t ?? "");
            ReloadCommand = new RelayCommand(() => RunOnSingle(r => LinkStatusActions.Reload(_doc, r)), () => _selection.Count == 1);
            UnloadCommand = new RelayCommand(() => RunOnSingle(r => LinkStatusActions.Unload(_doc, r)), () => _selection.Count == 1 && _selection[0].IsLoaded);
            AlwaysKeepCommand = new RelayCommand(AlwaysKeep,
                () => _selection.Any(r => r.IsDesktopConnector && r.Dc is not (DcFileState.AlwaysKept or DcFileState.Missing)));
            OpenFolderCommand = new RelayCommand(OpenFolders, () => _selection.Any(r => !r.IsCloud));
            CopyPathCommand = new RelayCommand(() => Status = LinkStatusActions.CopyPaths(_selection), () => _selection.Count > 0);
            FolderLessCommand = new RelayCommand(() => FolderDepth--, () => FolderDepth > FolderDisplay.MinDepth);
            FolderMoreCommand = new RelayCommand(() => FolderDepth++, () => FolderDepth < FolderDisplay.MaxDepth);
            ClearFiltersCommand = new RelayCommand(ClearFilters);
            RemoveFilterCommand = new RelayCommand<string>(RemoveFilter);
            FilterToFolderCommand = new RelayCommand(
                () => { ShowFolder(_selection[0].AccFolder); },
                () => _selection.Count == 1 && _selection[0].Source != LinkSourceKind.Other && _selection[0].AccFolder.Length > 0);
            FilterToProjectCommand = new RelayCommand(
                () => ProjectFilter = _selection[0].AccProject,
                () => _selection.Count == 1 && _selection[0].AccProject.Length > 0);

            Refresh();
        }

        // ---------------------------------------------------------------- data

        /// <summary>Host model title.</summary>
        public string HostName { get; }

        /// <summary>Every top-level RVT link of the model (other paths too; the filter hides them by default).</summary>
        public ObservableCollection<LinkStatusRow> Rows { get; } = new();

        /// <summary>Filtered, sortable view of <see cref="Rows"/> the grid binds to.</summary>
        public ICollectionView RowsView { get; }

        /// <summary>The Desktop Connector project folder the links come from.</summary>
        public string ProjectRoot
        {
            get => _projectRoot;
            private set => SetProperty(ref _projectRoot, value);
        }

        /// <summary>"Wynn Marjan Island (ALEC …)", or a note that the open model is not in ACC.  [V007]</summary>
        public string HostProjectText =>
            _hostProject.Length == 0 ? "this model is not in ACC"
                                     : _hostProject + (_hostAccount.Length > 0 ? $" ({_hostAccount})" : "");

        /// <summary>The grid's current row (bound to SelectedItem); the detail panel shows it when one link is selected.</summary>
        public LinkStatusRow Selected
        {
            get => _selected;
            set => SetProperty(ref _selected, value);
        }

        /// <summary>All selected rows; set by the window from the grid's SelectedItems.</summary>
        public IReadOnlyList<LinkStatusRow> Selection
        {
            get => _selection;
            set
            {
                _selection = value ?? Array.Empty<LinkStatusRow>();
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(IsMultiSelection));
                OnPropertyChanged(nameof(SelectionTitle));
                OnPropertyChanged(nameof(ShowingText));
                NotifyCommands();
            }
        }

        /// <summary>True when exactly one link is selected (shows its details).</summary>
        public bool HasSelection => _selection.Count == 1;

        /// <summary>True when more than one link is selected (shows the list of paths).</summary>
        public bool IsMultiSelection => _selection.Count > 1;

        /// <summary>"3 links selected".</summary>
        public string SelectionTitle => $"{_selection.Count} links selected — Reload and Unload work on one link at a time.";

        /// <summary>Status-bar message from the last scan or action.</summary>
        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        private int DcLinks => Rows.Count(r => r.IsDesktopConnector);
        private int CloudLinks => Rows.Count(r => r.IsCloud);
        private int OtherLinks => Rows.Count(r => r.Source == LinkSourceKind.Other);

        /// <summary>Rows the grid can show at all: ACC links, plus other links when they are shown.</summary>
        private int ListedCount => Rows.Count(r => ShowOtherLinks || r.Source != LinkSourceKind.Other);

        /// <summary>"9 from Desktop Connector · 5 cloud links · 2 other RVT links hidden (not ACC)".</summary>
        public string FooterText =>
            $"{DcLinks} from Desktop Connector · {CloudLinks} cloud link{(CloudLinks == 1 ? "" : "s")}" +
            (OtherLinks == 0 ? "" :
             ShowOtherLinks ? $" · {OtherLinks} other" :
             $" · {OtherLinks} other RVT link{(OtherLinks == 1 ? "" : "s")} hidden (not ACC)") +
            $" · Last refresh {_lastRefresh:HH:mm}";

        /// <summary>"Showing 5 of 13 RVT links · 2 selected".</summary>
        public string ShowingText =>
            $"Showing {RowsView.Cast<object>().Count()} of {ListedCount} RVT links" +
            (_selection.Count > 1 ? $" · {_selection.Count} selected" : "");

        /// <summary>True when the grid shows nothing; shows <see cref="EmptyText"/>.</summary>
        public bool IsEmpty => !RowsView.Cast<object>().Any();

        /// <summary>Why the grid is empty: no ACC links at all, or the filters hide them.  [V007]</summary>
        public string EmptyText => ListedCount == 0
            ? "This model has no Revit links from ACC: none under an ACCDocs (Desktop Connector) folder, and no Autodesk Docs:// cloud links." +
              (OtherLinks > 0 ? " Tick Show other links to see the others." : "")
            : "No links match the filters. Remove a filter above, or press Clear filters.";

        // ---- counts (tiles): every filter but the tile, so a count is what clicking it shows ----

        private IEnumerable<LinkStatusRow> Base => Rows.Where(r => Matches(r, withTile: false));

        /// <summary>Links from Desktop Connector.  [V007]</summary>
        public int SrcDcCount => Base.Count(r => r.IsDesktopConnector);
        /// <summary>Cloud links.  [V007]</summary>
        public int SrcCloudCount => Base.Count(r => r.IsCloud);
        /// <summary>Links that are not in ACC (counted even while hidden).  [V007]</summary>
        public int SrcOtherCount => Rows.Count(r => r.Source == LinkSourceKind.Other && Matches(r, withTile: false, forceOther: true));
        /// <summary>Loaded links.</summary>
        public int LoadedCount => Base.Count(r => r.IsLoaded);
        /// <summary>Unloaded links (for everyone, for me, or in a closed workset).</summary>
        public int UnloadedCount => Base.Count(r => r.IsUnloaded);
        /// <summary>Links Revit cannot find.</summary>
        public int NotFoundCount => Base.Count(r => r.IsNotFound);
        /// <summary>Files set to always keep on this device.</summary>
        public int AlwaysKeptCount => Base.Count(r => r.Dc == DcFileState.AlwaysKept);
        /// <summary>Files downloaded but not pinned.</summary>
        public int AvailableCount => Base.Count(r => r.Dc == DcFileState.AvailableLocally);
        /// <summary>Online-only files.</summary>
        public int OnlineOnlyCount => Base.Count(r => r.Dc == DcFileState.OnlineOnly);
        /// <summary>Files Desktop Connector is still syncing.</summary>
        public int SyncPendingCount => Base.Count(r => r.Dc == DcFileState.SyncPending);
        /// <summary>Desktop Connector files missing, not a cloud file, or of unknown state.</summary>
        public int OtherDcCount => Base.Count(IsOtherDc);
        /// <summary>Loaded links that are the latest.</summary>
        public int UpToDateCount => Base.Count(r => r.Latest == LatestState.UpToDate);
        /// <summary>Loaded links with a newer file on this PC or a newer version in ACC.</summary>
        public int ReloadNeededCount => Base.Count(r => r.Latest is LatestState.ReloadNeeded or LatestState.NewerInAcc);
        /// <summary>Loaded links whose latest state is unknown or pending.</summary>
        public int LatestUnknownCount => Base.Count(r => r.Latest is LatestState.Unknown or LatestState.SyncPending);
        /// <summary>Loaded cloud links not yet checked with ACC.  [V007]</summary>
        public int NotCheckedCount => Base.Count(r => r.Latest == LatestState.NotChecked);

        private static bool IsOtherDc(LinkStatusRow r) =>
            r.IsDesktopConnector && r.Dc is DcFileState.Missing or DcFileState.NotCloudFile or DcFileState.Unknown;

        // ---------------------------------------------------------------- folder display

        /// <summary>Folder display mode as a combo-box index (0 last N, 1 first N, 2 full). Saved per user.</summary>
        public int FolderModeIndex
        {
            get => (int)_settings.FolderMode;
            set
            {
                var mode = Enum.IsDefined(typeof(FolderDisplayMode), value) ? (FolderDisplayMode)value : FolderDisplayMode.LastN;
                if (mode == _settings.FolderMode) return;
                _settings.FolderMode = mode;
                FolderSettingChanged();
            }
        }

        /// <summary>N for the folder display. Saved per user.</summary>
        public int FolderDepth
        {
            get => _settings.FolderDepth;
            set
            {
                int n = Math.Clamp(value, FolderDisplay.MinDepth, FolderDisplay.MaxDepth);
                if (n == _settings.FolderDepth) return;
                _settings.FolderDepth = n;
                FolderSettingChanged();
            }
        }

        /// <summary>True unless the mode is Full (shows the − N + control).</summary>
        public bool ShowFolderDepth => _settings.FolderMode != FolderDisplayMode.Full;

        /// <summary>Column header, e.g. "Folder in ACC (last 2)".</summary>
        public string FolderHeader => _settings.FolderMode switch
        {
            FolderDisplayMode.Full => "Folder in ACC (full)",
            FolderDisplayMode.FirstN => $"Folder in ACC (first {_settings.FolderDepth})",
            _ => $"Folder in ACC (last {_settings.FolderDepth})"
        };

        private void FolderSettingChanged()
        {
            ApplyFolderDisplay();
            OnPropertyChanged(nameof(FolderModeIndex));
            OnPropertyChanged(nameof(FolderDepth));
            OnPropertyChanged(nameof(ShowFolderDepth));
            OnPropertyChanged(nameof(FolderHeader));
            FolderLessCommand.NotifyCanExecuteChanged();
            FolderMoreCommand.NotifyCanExecuteChanged();
            SaveSettings();
        }

        private void ApplyFolderDisplay()
        {
            foreach (LinkStatusRow r in Rows)
                r.FolderShown = FolderDisplay.Trim(r.AccFolder, _settings.FolderMode, _settings.FolderDepth);
        }

        private void SaveSettings()
        {
            string err = _settings.Save();
            if (err != null) Status = "Setting not saved — " + err;
        }

        // ---------------------------------------------------------------- filters

        /// <summary>Source drop-down entries.  [V007]</summary>
        public IReadOnlyList<FilterOption> SourceOptions { get; } = new[]
        {
            new FilterOption("", "All"),
            new FilterOption(nameof(LinkSourceKind.DesktopConnector), "Desktop Connector"),
            new FilterOption(nameof(LinkSourceKind.Cloud), "Cloud link"),
            new FilterOption(nameof(LinkSourceKind.Other), "Other path")
        };

        /// <summary>In model drop-down entries (values are the column texts).  [V007]</summary>
        public IReadOnlyList<FilterOption> ModelOptions { get; } =
            new[] { "Loaded", "Unloaded", "Unloaded (for me)", "Closed workset", "Not found", "Other" }
                .Select(t => new FilterOption(t, t)).Prepend(new FilterOption("", "All")).ToList();

        /// <summary>Latest drop-down entries (values are the column texts).  [V007]</summary>
        public IReadOnlyList<FilterOption> LatestOptions { get; } = new[]
        {
            new FilterOption("", "All"),
            new FilterOption("Yes", "Yes"),
            new FilterOption("Reload needed", "Reload needed"),
            new FilterOption("Newer in ACC", "Newer in ACC"),
            new FilterOption("Sync pending", "Sync pending"),
            new FilterOption("Unknown", "Unknown"),
            new FilterOption("Not checked", "Not checked"),
            new FilterOption("—", "— (not loaded / not ACC)")
        };

        /// <summary>ACC project drop-down entries, rebuilt on every refresh.  [V007]</summary>
        public ObservableCollection<FilterOption> ProjectOptions { get; } = new();

        /// <summary>Folder drop-down entries (every folder level of the ACC links), rebuilt on every refresh.  [V007]</summary>
        public ObservableCollection<FilterOption> FolderOptions { get; } = new();

        /// <summary>Active filters as removable chips.  [V007]</summary>
        public ObservableCollection<FilterChip> Chips { get; } = new();

        /// <summary>True when any filter is active (shows the chip row).  [V007]</summary>
        public bool HasChips => Chips.Count > 0;

        /// <summary>The count tile used as a filter, or empty for none. Clicking the active tile clears it.</summary>
        public string ActiveTile
        {
            get => _tile;
            set { if (SetProperty(ref _tile, value ?? "")) ApplyFilter(); }
        }

        /// <summary>Search text, matched against the link name and its full path.</summary>
        public string SearchText
        {
            get => _search;
            set { if (SetProperty(ref _search, value ?? "")) ApplyFilter(); }
        }

        /// <summary>Show only links that need attention.</summary>
        public bool ProblemsOnly
        {
            get => _problemsOnly;
            set { if (SetProperty(ref _problemsOnly, value)) ApplyFilter(); }
        }

        /// <summary>Source filter: a <see cref="LinkSourceKind"/> name, or empty for all.  [V007]</summary>
        public string SourceFilter
        {
            get => _sourceFilter;
            set { if (SetProperty(ref _sourceFilter, value ?? "")) ApplyFilter(); }
        }

        /// <summary>ACC project filter, or empty for all.  [V007]</summary>
        public string ProjectFilter
        {
            get => _projectFilter;
            set { if (!_rebuilding && SetProperty(ref _projectFilter, value ?? "")) ApplyFilter(); }
        }

        /// <summary>Folder filter: the folder and everything below it, or empty for all.  [V007]</summary>
        public string FolderFilter
        {
            get => _folderFilter;
            set { if (!_rebuilding && SetProperty(ref _folderFilter, value ?? "")) ApplyFilter(); }
        }

        /// <summary>In model filter (an "In model" column text), or empty for all.  [V007]</summary>
        public string ModelFilter
        {
            get => _modelFilter;
            set { if (SetProperty(ref _modelFilter, value ?? "")) ApplyFilter(); }
        }

        /// <summary>Latest filter (a "Latest from ACC?" column text), or empty for all.  [V007]</summary>
        public string LatestFilter
        {
            get => _latestFilter;
            set { if (SetProperty(ref _latestFilter, value ?? "")) ApplyFilter(); }
        }

        /// <summary>Also list links that are not in ACC. Saved per user; off by default.  [V007]</summary>
        public bool ShowOtherLinks
        {
            get => _settings.ShowOtherLinks;
            set
            {
                if (_settings.ShowOtherLinks == value) return;
                _settings.ShowOtherLinks = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FooterText));
                SaveSettings();
                ApplyFilter();
            }
        }

        private bool Matches(LinkStatusRow r, bool withTile, bool forceOther = false)
        {
            if (r.Source == LinkSourceKind.Other && !forceOther && !ShowOtherLinks &&
                _sourceFilter != nameof(LinkSourceKind.Other) && _tile != "SrcOther") return false;
            if (_search.Length > 0 &&
                r.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0 &&
                r.FullPath.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (_sourceFilter.Length > 0 && r.Source.ToString() != _sourceFilter) return false;
            if (_projectFilter.Length > 0 && !string.Equals(r.AccProject, _projectFilter, StringComparison.OrdinalIgnoreCase)) return false;
            if (_folderFilter.Length > 0 && (r.Source == LinkSourceKind.Other || !InFolder(r.AccFolder, _folderFilter))) return false;
            if (_modelFilter.Length > 0 && r.ModelText != _modelFilter) return false;
            if (_latestFilter.Length > 0 && r.LatestText != _latestFilter) return false;
            if (_problemsOnly && !r.IsProblem) return false;
            if (!withTile) return true;

            return _tile switch
            {
                "SrcDc" => r.IsDesktopConnector,
                "SrcCloud" => r.IsCloud,
                "SrcOther" => r.Source == LinkSourceKind.Other,
                "Loaded" => r.IsLoaded,
                "Unloaded" => r.IsUnloaded,
                "NotFound" => r.IsNotFound,
                "AlwaysKept" => r.Dc == DcFileState.AlwaysKept,
                "Available" => r.Dc == DcFileState.AvailableLocally,
                "OnlineOnly" => r.Dc == DcFileState.OnlineOnly,
                "SyncPending" => r.Dc == DcFileState.SyncPending,
                "OtherDc" => IsOtherDc(r),
                "UpToDate" => r.Latest == LatestState.UpToDate,
                "ReloadNeeded" => r.Latest is LatestState.ReloadNeeded or LatestState.NewerInAcc,
                "LatestUnknown" => r.Latest is LatestState.Unknown or LatestState.SyncPending,
                "NotChecked" => r.Latest == LatestState.NotChecked,
                _ => true
            };
        }

        private static bool InFolder(string folder, string filter) =>
            string.Equals(folder, filter, StringComparison.OrdinalIgnoreCase) ||
            (folder ?? "").StartsWith(filter + "\\", StringComparison.OrdinalIgnoreCase);

        private void ApplyFilter()
        {
            RowsView.Refresh();
            RebuildChips();
            foreach (string p in new[]
            {
                nameof(ShowingText), nameof(IsEmpty), nameof(EmptyText), nameof(FooterText),
                nameof(SrcDcCount), nameof(SrcCloudCount), nameof(SrcOtherCount),
                nameof(LoadedCount), nameof(UnloadedCount), nameof(NotFoundCount),
                nameof(AlwaysKeptCount), nameof(AvailableCount), nameof(OnlineOnlyCount), nameof(SyncPendingCount),
                nameof(OtherDcCount), nameof(UpToDateCount), nameof(ReloadNeededCount), nameof(LatestUnknownCount),
                nameof(NotCheckedCount)
            }) OnPropertyChanged(p);
        }

        private void RebuildChips()
        {
            Chips.Clear();
            if (_tile.Length > 0) Chips.Add(new FilterChip("tile", "Tile: " + (TileLabels.TryGetValue(_tile, out string t) ? t : _tile)));
            if (_search.Length > 0) Chips.Add(new FilterChip("search", $"Search: \"{_search}\""));
            if (_sourceFilter.Length > 0) Chips.Add(new FilterChip("source", "Source: " + SourceOptions.First(o => o.Value == _sourceFilter).Text));
            if (_projectFilter.Length > 0) Chips.Add(new FilterChip("project", "ACC project: " + _projectFilter));
            if (_folderFilter.Length > 0) Chips.Add(new FilterChip("folder", "Folder: " + _folderFilter));
            if (_modelFilter.Length > 0) Chips.Add(new FilterChip("model", "In model: " + _modelFilter));
            if (_latestFilter.Length > 0) Chips.Add(new FilterChip("latest", "Latest: " + _latestFilter));
            if (_problemsOnly) Chips.Add(new FilterChip("problems", "Problems only"));
            OnPropertyChanged(nameof(HasChips));
        }

        private void RemoveFilter(string key)
        {
            switch (key)
            {
                case "tile": ActiveTile = ""; break;
                case "search": SearchText = ""; break;
                case "source": SourceFilter = ""; break;
                case "project": ProjectFilter = ""; break;
                case "folder": FolderFilter = ""; break;
                case "model": ModelFilter = ""; break;
                case "latest": LatestFilter = ""; break;
                case "problems": ProblemsOnly = false; break;
            }
        }

        private void ClearFilters()
        {
            foreach (string k in new[] { "tile", "search", "source", "project", "folder", "model", "latest", "problems" })
                RemoveFilter(k);
        }

        private void ShowFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            if (!FolderOptions.Any(o => o.Value == folder)) FolderOptions.Add(new FilterOption(folder, folder));
            FolderFilter = folder;
        }

        /// <summary>Rebuilds the project and folder drop-downs from the rows; clears a filter whose value is gone.</summary>
        private void RebuildOptions()
        {
            _rebuilding = true;
            try { FillOptions(); }
            finally { _rebuilding = false; }

            if (!ProjectOptions.Any(o => o.Value == _projectFilter)) _projectFilter = "";
            if (!FolderOptions.Any(o => o.Value == _folderFilter)) _folderFilter = "";
            OnPropertyChanged(nameof(ProjectFilter));
            OnPropertyChanged(nameof(FolderFilter));
        }

        private void FillOptions()
        {
            ProjectOptions.Clear();
            ProjectOptions.Add(new FilterOption("", "All projects"));
            foreach (string p in Rows.Select(r => r.AccProject).Where(p => p.Length > 0)
                                     .Distinct(StringComparer.OrdinalIgnoreCase)
                                     .OrderBy(p => string.Equals(p, _hostProject, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                                     .ThenBy(p => p, StringComparer.OrdinalIgnoreCase))
                ProjectOptions.Add(new FilterOption(p, string.Equals(p, _hostProject, StringComparison.OrdinalIgnoreCase) ? p + " (this model)" : p));

            // Every level of every ACC folder, indented, so a parent folder filters everything below it.
            var folders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LinkStatusRow r in Rows.Where(r => r.Source != LinkSourceKind.Other && r.AccFolder.Length > 0))
            {
                string[] parts = r.AccFolder.Split('\\');
                for (int i = 1; i <= parts.Length; i++) folders.Add(string.Join("\\", parts, 0, i));
            }
            FolderOptions.Clear();
            FolderOptions.Add(new FilterOption("", "All folders"));
            foreach (string f in folders)
            {
                int depth = f.Count(ch => ch == '\\');
                FolderOptions.Add(new FilterOption(f, new string(' ', depth * 3) + f.Substring(f.LastIndexOf('\\') + 1)));
            }
        }

        // ---------------------------------------------------------------- commands

        /// <summary>Re-reads every link's status.</summary>
        public IRelayCommand RefreshCommand { get; }
        /// <summary>Asks ACC whether each loaded cloud link is the current version.  [V007]</summary>
        public IRelayCommand CheckCloudCommand { get; }
        /// <summary>Saves the grid to Excel.</summary>
        public IRelayCommand ExportCommand { get; }
        /// <summary>Toggles a count tile filter (parameter: tile key).</summary>
        public IRelayCommand<string> SetTileCommand { get; }
        /// <summary>Reloads the one selected link.</summary>
        public IRelayCommand ReloadCommand { get; }
        /// <summary>Unloads the one selected link.</summary>
        public IRelayCommand UnloadCommand { get; }
        /// <summary>Marks every selected Desktop Connector file "Always keep on this device".</summary>
        public IRelayCommand AlwaysKeepCommand { get; }
        /// <summary>Opens one Explorer window per folder of the selected file links.</summary>
        public IRelayCommand OpenFolderCommand { get; }
        /// <summary>Copies the full paths of the selected links.</summary>
        public IRelayCommand CopyPathCommand { get; }
        /// <summary>Shows one folder name fewer.</summary>
        public IRelayCommand FolderLessCommand { get; }
        /// <summary>Shows one folder name more.</summary>
        public IRelayCommand FolderMoreCommand { get; }
        /// <summary>Clears every filter and the tile.  [V007]</summary>
        public IRelayCommand ClearFiltersCommand { get; }
        /// <summary>Clears one filter (parameter: the chip's key).  [V007]</summary>
        public IRelayCommand<string> RemoveFilterCommand { get; }
        /// <summary>Filters to the selected link's folder.  [V007]</summary>
        public IRelayCommand FilterToFolderCommand { get; }
        /// <summary>Filters to the selected link's ACC project.  [V007]</summary>
        public IRelayCommand FilterToProjectCommand { get; }

        private void Refresh(string keepStatus = null)
        {
            var keepIds = new HashSet<ElementId>(_selection.Select(r => r.TypeId));
            LinkStatusScan scan;
            Mouse.OverrideCursor = Cursors.Wait;
            try { scan = LinkStatusCollector.Scan(_doc); }
            finally { Mouse.OverrideCursor = null; }

            Rows.Clear();
            foreach (LinkStatusRow r in scan.Rows) Rows.Add(r);
            ApplyFolderDisplay();

            ProjectRoot = scan.ProjectRoot.Length > 0 ? scan.ProjectRoot : "none of this model's links are in an ACCDocs folder";
            _hostProject = scan.HostProject;
            _hostAccount = scan.HostAccount;
            OnPropertyChanged(nameof(HostProjectText));
            _lastRefresh = DateTime.Now;
            RebuildOptions();

            string scanNote = scan.Skipped.Count == 0
                ? "Ready." + (CloudLinks > 0 ? " Press Check cloud versions to ask ACC about the cloud links." : "")
                : $"{scan.Skipped.Count} link(s) could not be read: " + string.Join("; ", scan.Skipped.Take(3));
            Status = keepStatus ?? scanNote;

            ApplyFilter();

            var reselect = Rows.Where(r => keepIds.Contains(r.TypeId)).ToList();
            Selection = reselect;
            RestoreSelection?.Invoke(reselect);
        }

        private void CheckCloud()
        {
            Mouse.OverrideCursor = Cursors.Wait;
            try { Status = CloudVersionChecker.Check(_doc, Rows); }
            finally { Mouse.OverrideCursor = null; }
            ApplyFilter();
        }

        private void RunOnSingle(Func<LinkStatusRow, string> action)
        {
            if (_selection.Count != 1) { Status = "Select one link for this action."; return; }
            string msg;
            Mouse.OverrideCursor = Cursors.Wait;
            try { msg = action(_selection[0]); }
            finally { Mouse.OverrideCursor = null; }
            Refresh(msg);
        }

        private void AlwaysKeep()
        {
            string msg = LinkStatusActions.AlwaysKeep(_selection);
            Refresh(msg);
        }

        private void OpenFolders()
        {
            int folders = LinkStatusActions.FolderCount(_selection);
            if (folders > OpenFolderConfirmAbove &&
                MessageBox.Show($"This opens {folders} Explorer windows. Continue?", "Open folder",
                                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                Status = "Open folder cancelled.";
                return;
            }
            Status = LinkStatusActions.OpenFolders(_selection);
        }

        private void Export()
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export link status",
                    Filter = "Excel workbook (*.xlsx)|*.xlsx",
                    FileName = $"{Path.GetFileNameWithoutExtension(HostName)}_LinkStatus_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
                };
                if (dlg.ShowDialog() != true) return;

                LinkStatusExporter.Save(dlg.FileName, HostName, RowsView.Cast<LinkStatusRow>().ToList());
                Status = $"Exported {RowsView.Cast<object>().Count()} rows to {dlg.FileName}";
            }
            catch (Exception ex)
            {
                Status = "Export failed — " + ex.Message + " (is the file open in Excel?)";
            }
        }

        private void NotifyCommands()
        {
            ExportCommand?.NotifyCanExecuteChanged();
            CheckCloudCommand?.NotifyCanExecuteChanged();
            ReloadCommand?.NotifyCanExecuteChanged();
            UnloadCommand?.NotifyCanExecuteChanged();
            AlwaysKeepCommand?.NotifyCanExecuteChanged();
            OpenFolderCommand?.NotifyCanExecuteChanged();
            CopyPathCommand?.NotifyCanExecuteChanged();
            FilterToFolderCommand?.NotifyCanExecuteChanged();
            FilterToProjectCommand?.NotifyCanExecuteChanged();
        }
    }
}
