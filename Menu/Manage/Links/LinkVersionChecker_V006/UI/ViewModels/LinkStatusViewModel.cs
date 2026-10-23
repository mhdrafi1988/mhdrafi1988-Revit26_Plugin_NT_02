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
using Revit26_Plugin_Revised.LinkVersionChecker.V006.Core.Models;
using Revit26_Plugin_Revised.LinkVersionChecker.V006.Core.Services;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V006.UI.ViewModels
{
    /// <summary>
    /// View model of the Link Version Checker V006 window: the status of every Revit link that
    /// comes from Desktop Connector, count tiles that double as filters, a folder display
    /// setting, and actions on the selected links (Reload / Unload need exactly one).
    /// </summary>
    public sealed class LinkStatusViewModel : ObservableObject
    {
        /// <summary>Above this many Explorer windows, Open folder asks first.</summary>
        public const int OpenFolderConfirmAbove = 10;

        private readonly Document _doc;
        private readonly LinkStatusSettings _settings;
        private string _tile = "";
        private string _search = "";
        private bool _problemsOnly;
        private LinkStatusRow _selected;
        private IReadOnlyList<LinkStatusRow> _selection = Array.Empty<LinkStatusRow>();
        private string _status = "";
        private string _projectRoot = "";
        private int _otherLinks;
        private DateTime _lastRefresh;

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
            RowsView.Filter = o => o is LinkStatusRow r && Matches(r);

            RefreshCommand = new RelayCommand(() => Refresh());
            ExportCommand = new RelayCommand(Export, () => Rows.Count > 0);
            SetTileCommand = new RelayCommand<string>(t => ActiveTile = ActiveTile == t ? "" : t ?? "");
            ReloadCommand = new RelayCommand(() => RunOnSingle(r => LinkStatusActions.Reload(_doc, r)), () => _selection.Count == 1);
            UnloadCommand = new RelayCommand(() => RunOnSingle(r => LinkStatusActions.Unload(_doc, r)), () => _selection.Count == 1 && _selection[0].IsLoaded);
            AlwaysKeepCommand = new RelayCommand(AlwaysKeep, () => _selection.Any(r => r.Dc is not (DcFileState.AlwaysKept or DcFileState.Missing)));
            OpenFolderCommand = new RelayCommand(OpenFolders, () => _selection.Count > 0);
            CopyPathCommand = new RelayCommand(() => Status = LinkStatusActions.CopyPaths(_selection), () => _selection.Count > 0);
            FolderLessCommand = new RelayCommand(() => FolderDepth--, () => FolderDepth > FolderDisplay.MinDepth);
            FolderMoreCommand = new RelayCommand(() => FolderDepth++, () => FolderDepth < FolderDisplay.MaxDepth);

            Refresh();
        }

        // ---------------------------------------------------------------- data

        /// <summary>Host model title.</summary>
        public string HostName { get; }

        /// <summary>Every Desktop Connector RVT link of the model.</summary>
        public ObservableCollection<LinkStatusRow> Rows { get; } = new();

        /// <summary>Filtered, sortable view of <see cref="Rows"/> the grid binds to.</summary>
        public ICollectionView RowsView { get; }

        /// <summary>The Desktop Connector project folder the links come from.</summary>
        public string ProjectRoot
        {
            get => _projectRoot;
            private set => SetProperty(ref _projectRoot, value);
        }

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

        /// <summary>"13 RVT links from Desktop Connector · 4 other RVT links hidden".</summary>
        public string FooterText =>
            $"{Rows.Count} RVT link{(Rows.Count == 1 ? "" : "s")} from Desktop Connector" +
            (_otherLinks > 0 ? $" · {_otherLinks} other RVT link{(_otherLinks == 1 ? "" : "s")} hidden (not in an ACCDocs folder)" : "") +
            $" · Last refresh {_lastRefresh:HH:mm}";

        /// <summary>"Showing 5 of 13 RVT links · 2 selected".</summary>
        public string ShowingText =>
            $"Showing {RowsView.Cast<object>().Count()} of {Rows.Count} RVT links" +
            (_selection.Count > 1 ? $" · {_selection.Count} selected" : "");

        /// <summary>True when the model has no Desktop Connector links; shows the empty-state message.</summary>
        public bool IsEmpty => Rows.Count == 0;

        // ---- counts (tiles) ----

        /// <summary>Loaded links.</summary>
        public int LoadedCount => Rows.Count(r => r.IsLoaded);
        /// <summary>Unloaded links (for everyone, for me, or in a closed workset).</summary>
        public int UnloadedCount => Rows.Count(r => r.IsUnloaded);
        /// <summary>Links Revit cannot find.</summary>
        public int NotFoundCount => Rows.Count(r => r.IsNotFound);
        /// <summary>Files set to always keep on this device.</summary>
        public int AlwaysKeptCount => Rows.Count(r => r.Dc == DcFileState.AlwaysKept);
        /// <summary>Files downloaded but not pinned.</summary>
        public int AvailableCount => Rows.Count(r => r.Dc == DcFileState.AvailableLocally);
        /// <summary>Online-only files.</summary>
        public int OnlineOnlyCount => Rows.Count(r => r.Dc == DcFileState.OnlineOnly);
        /// <summary>Files Desktop Connector is still syncing.</summary>
        public int SyncPendingCount => Rows.Count(r => r.Dc == DcFileState.SyncPending);
        /// <summary>Files missing, not a cloud file, or of unknown state.</summary>
        public int OtherDcCount => Rows.Count(r => r.Dc is DcFileState.Missing or DcFileState.NotCloudFile or DcFileState.Unknown);
        /// <summary>Loaded links that match the latest file.</summary>
        public int UpToDateCount => Rows.Count(r => r.Latest == LatestState.UpToDate);
        /// <summary>Loaded links with a newer file on this PC.</summary>
        public int ReloadNeededCount => Rows.Count(r => r.Latest == LatestState.ReloadNeeded);
        /// <summary>Loaded links whose latest state is unknown or pending.</summary>
        public int LatestUnknownCount => Rows.Count(r => r.Latest is LatestState.Unknown or LatestState.SyncPending);

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
            string err = _settings.Save();
            if (err != null) Status = "Folder setting not saved — " + err;
        }

        private void ApplyFolderDisplay()
        {
            foreach (LinkStatusRow r in Rows)
                r.FolderShown = FolderDisplay.Trim(r.AccFolder, _settings.FolderMode, _settings.FolderDepth);
        }

        // ---------------------------------------------------------------- filters

        /// <summary>The count tile used as a filter, or empty for none. Clicking the active tile clears it.</summary>
        public string ActiveTile
        {
            get => _tile;
            set
            {
                if (!SetProperty(ref _tile, value ?? "")) return;
                ApplyFilter();
            }
        }

        /// <summary>Name search text.</summary>
        public string SearchText
        {
            get => _search;
            set
            {
                if (!SetProperty(ref _search, value ?? "")) return;
                ApplyFilter();
            }
        }

        /// <summary>Show only links that need attention.</summary>
        public bool ProblemsOnly
        {
            get => _problemsOnly;
            set
            {
                if (!SetProperty(ref _problemsOnly, value)) return;
                ApplyFilter();
            }
        }

        private bool Matches(LinkStatusRow r)
        {
            if (_problemsOnly && !r.IsProblem) return false;
            if (_search.Length > 0 && r.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return _tile switch
            {
                "Loaded" => r.IsLoaded,
                "Unloaded" => r.IsUnloaded,
                "NotFound" => r.IsNotFound,
                "AlwaysKept" => r.Dc == DcFileState.AlwaysKept,
                "Available" => r.Dc == DcFileState.AvailableLocally,
                "OnlineOnly" => r.Dc == DcFileState.OnlineOnly,
                "SyncPending" => r.Dc == DcFileState.SyncPending,
                "OtherDc" => r.Dc is DcFileState.Missing or DcFileState.NotCloudFile or DcFileState.Unknown,
                "UpToDate" => r.Latest == LatestState.UpToDate,
                "ReloadNeeded" => r.Latest == LatestState.ReloadNeeded,
                "LatestUnknown" => r.Latest is LatestState.Unknown or LatestState.SyncPending,
                _ => true
            };
        }

        private void ApplyFilter()
        {
            RowsView.Refresh();
            OnPropertyChanged(nameof(ShowingText));
        }

        // ---------------------------------------------------------------- commands

        /// <summary>Re-reads every link's status.</summary>
        public IRelayCommand RefreshCommand { get; }
        /// <summary>Saves the grid to Excel.</summary>
        public IRelayCommand ExportCommand { get; }
        /// <summary>Toggles a count tile filter (parameter: tile key).</summary>
        public IRelayCommand<string> SetTileCommand { get; }
        /// <summary>Reloads the one selected link.</summary>
        public IRelayCommand ReloadCommand { get; }
        /// <summary>Unloads the one selected link.</summary>
        public IRelayCommand UnloadCommand { get; }
        /// <summary>Marks every selected file "Always keep on this device".</summary>
        public IRelayCommand AlwaysKeepCommand { get; }
        /// <summary>Opens one Explorer window per folder of the selected links.</summary>
        public IRelayCommand OpenFolderCommand { get; }
        /// <summary>Copies the full paths of the selected links.</summary>
        public IRelayCommand CopyPathCommand { get; }
        /// <summary>Shows one folder name fewer.</summary>
        public IRelayCommand FolderLessCommand { get; }
        /// <summary>Shows one folder name more.</summary>
        public IRelayCommand FolderMoreCommand { get; }

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

            ProjectRoot = scan.ProjectRoot.Length > 0 ? scan.ProjectRoot : "No Desktop Connector (ACCDocs) folder found in this model's links";
            _otherLinks = scan.OtherLinks;
            _lastRefresh = DateTime.Now;

            string scanNote = scan.Skipped.Count == 0
                ? "Ready."
                : $"{scan.Skipped.Count} link(s) could not be read: " + string.Join("; ", scan.Skipped.Take(3));
            Status = keepStatus ?? scanNote;

            ApplyFilter();
            foreach (string p in new[]
            {
                nameof(FooterText), nameof(IsEmpty), nameof(LoadedCount), nameof(UnloadedCount), nameof(NotFoundCount),
                nameof(AlwaysKeptCount), nameof(AvailableCount), nameof(OnlineOnlyCount), nameof(SyncPendingCount),
                nameof(OtherDcCount), nameof(UpToDateCount), nameof(ReloadNeededCount), nameof(LatestUnknownCount)
            }) OnPropertyChanged(p);

            var reselect = Rows.Where(r => keepIds.Contains(r.TypeId)).ToList();
            Selection = reselect;
            RestoreSelection?.Invoke(reselect);
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
            ReloadCommand?.NotifyCanExecuteChanged();
            UnloadCommand?.NotifyCanExecuteChanged();
            AlwaysKeepCommand?.NotifyCanExecuteChanged();
            OpenFolderCommand?.NotifyCanExecuteChanged();
            CopyPathCommand?.NotifyCanExecuteChanged();
        }
    }
}
