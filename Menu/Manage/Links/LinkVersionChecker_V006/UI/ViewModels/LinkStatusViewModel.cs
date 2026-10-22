using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
    /// comes from Desktop Connector, count tiles that double as filters, and actions on the
    /// one selected link.
    /// </summary>
    public sealed class LinkStatusViewModel : ObservableObject
    {
        private readonly Document _doc;
        private string _tile = "";
        private string _search = "";
        private bool _problemsOnly;
        private LinkStatusRow _selected;
        private string _status = "";
        private string _projectRoot = "";
        private int _otherLinks;
        private DateTime _lastRefresh;

        /// <summary>Creates the view model and runs the first scan.</summary>
        public LinkStatusViewModel(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            HostName = string.IsNullOrEmpty(doc.Title) ? "(unsaved model)" : doc.Title;

            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.Filter = o => o is LinkStatusRow r && Matches(r);

            RefreshCommand = new RelayCommand(() => Refresh());
            ExportCommand = new RelayCommand(Export, () => Rows.Count > 0);
            SetTileCommand = new RelayCommand<string>(t => ActiveTile = ActiveTile == t ? "" : t ?? "");
            ReloadCommand = new RelayCommand(() => RunOnSelected(r => LinkStatusActions.Reload(_doc, r), rescan: true), () => Selected != null);
            UnloadCommand = new RelayCommand(() => RunOnSelected(r => LinkStatusActions.Unload(_doc, r), rescan: true), () => Selected != null && Selected.IsLoaded);
            AlwaysKeepCommand = new RelayCommand(() => RunOnSelected(LinkStatusActions.AlwaysKeep, rescan: true), () => Selected != null && Selected.Dc != DcFileState.AlwaysKept && Selected.Dc != DcFileState.Missing);
            OpenFolderCommand = new RelayCommand(() => RunOnSelected(LinkStatusActions.OpenFolder, rescan: false), () => Selected != null);

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

        /// <summary>The link the actions apply to.</summary>
        public LinkStatusRow Selected
        {
            get => _selected;
            set
            {
                if (!SetProperty(ref _selected, value)) return;
                OnPropertyChanged(nameof(HasSelection));
                NotifyCommands();
            }
        }

        /// <summary>True when a link is selected.</summary>
        public bool HasSelection => _selected != null;

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

        /// <summary>"Showing 5 of 13 RVT links".</summary>
        public string ShowingText => $"Showing {RowsView.Cast<object>().Count()} of {Rows.Count} RVT links";

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
        /// <summary>Reloads the selected link.</summary>
        public IRelayCommand ReloadCommand { get; }
        /// <summary>Unloads the selected link.</summary>
        public IRelayCommand UnloadCommand { get; }
        /// <summary>Marks the selected file "Always keep on this device".</summary>
        public IRelayCommand AlwaysKeepCommand { get; }
        /// <summary>Opens the selected file's folder.</summary>
        public IRelayCommand OpenFolderCommand { get; }

        private void Refresh(string keepStatus = null)
        {
            ElementId keep = _selected?.TypeId;
            LinkStatusScan scan;
            Mouse.OverrideCursor = Cursors.Wait;
            try { scan = LinkStatusCollector.Scan(_doc); }
            finally { Mouse.OverrideCursor = null; }

            Rows.Clear();
            foreach (LinkStatusRow r in scan.Rows) Rows.Add(r);

            ProjectRoot = scan.ProjectRoot.Length > 0 ? scan.ProjectRoot : "No Desktop Connector (ACCDocs) folder found in this model's links";
            _otherLinks = scan.OtherLinks;
            _lastRefresh = DateTime.Now;

            Selected = keep == null ? null : Rows.FirstOrDefault(r => r.TypeId == keep);

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
            NotifyCommands();
        }

        private void RunOnSelected(Func<LinkStatusRow, string> action, bool rescan)
        {
            if (_selected == null) { Status = "Select a link first."; return; }
            string msg;
            Mouse.OverrideCursor = Cursors.Wait;
            try { msg = action(_selected); }
            finally { Mouse.OverrideCursor = null; }

            if (rescan) Refresh(msg);
            else Status = msg;
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
        }
    }
}
