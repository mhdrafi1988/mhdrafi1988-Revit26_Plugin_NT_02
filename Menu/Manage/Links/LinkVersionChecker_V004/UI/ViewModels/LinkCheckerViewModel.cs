using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkVersionChecker_V004.Core.Models;
using Revit26_Plugin_Revised.LinkVersionChecker_V004.Services;
using Revit26_Plugin_Revised.LinkVersionChecker_V004.UI.Infrastructure;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;
using CollectionViewSource = System.Windows.Data.CollectionViewSource;
using MsgBox = System.Windows.MessageBox;
using MsgButton = System.Windows.MessageBoxButton;
using MsgImage = System.Windows.MessageBoxImage;
using MsgResult = System.Windows.MessageBoxResult;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.UI.ViewModels
{
    public enum ScanMode { PreOpen, InModel, Folder }

    public sealed class LinkCheckerViewModel : ObservableObject
    {
        private const string Title = "Link Version Checker";
        private const string AllHosts = "All hosts";

        private readonly RevitApp _app;
        private readonly Document _doc;
        private readonly LinkScanner _scanner;
        private readonly BackupService _backups = new BackupService();
        private CancellationTokenSource _cts;

        public CheckerSettings Settings { get; }

        // ------------------------------------------------------------ collections
        private ObservableCollection<LinkInfo> _links = new ObservableCollection<LinkInfo>();
        public ObservableCollection<LinkInfo> Links { get => _links; private set => Set(ref _links, value); }

        private ICollectionView _linksView;
        public ICollectionView LinksView { get => _linksView; private set => Set(ref _linksView, value); }

        public ObservableCollection<string> RecentHosts { get; }
        public ObservableCollection<string> HostOptions { get; } = new ObservableCollection<string> { AllHosts };

        /// <summary>Type and Source filter entries, each carrying its count ("Network (UNC) — 3").</summary>
        public ObservableCollection<string> TypeOptions { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> SourceOptions { get; } = new ObservableCollection<string>();

        private static readonly string[] AllTypeNames =
            { "RVT", "IFC", "CAD", "CAD (import)", "Point Cloud", "PDF/Image", "Other" };

        // ------------------------------------------------------------ commands
        public ICommand BrowseCommand { get; }
        public ICommand ScanCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand UpgradeCommand { get; }
        public ICommand RebuildIfcCommand { get; }
        public ICommand SkipLoadCommand { get; }
        public ICommand RepathCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CheckProblemsCommand { get; }
        public ICommand CheckVisibleCommand { get; }
        public ICommand ClearChecksCommand { get; }

        public LinkCheckerViewModel(RevitApp app, Document activeDoc, ScanMode startMode)
        {
            _app = app;
            _doc = activeDoc != null && !activeDoc.IsFamilyDocument ? activeDoc : null;
            _scanner = new LinkScanner(app);
            Settings = SettingsStore.Load();
            RecentHosts = new ObservableCollection<string>(Settings.RecentHosts);

            _hostPath = Settings.LastHostPath ?? "";
            _folderPath = Settings.LastFolder ?? "";
            _includeNested = Settings.IncludeNested;
            _includeSubfolders = Settings.IncludeSubfolders;
            _filterProblems = Settings.FilterProblems;
            _typeFilter = string.IsNullOrWhiteSpace(Settings.TypeFilter) ? "All" : Settings.TypeFilter;
            _sourceFilter = string.IsNullOrWhiteSpace(Settings.SourceFilter) ? "All" : Settings.SourceFilter;
            _availableOnly = Settings.AvailableTypesOnly;
            SourceDetector.DesktopConnectorRoots = Settings.DesktopConnectorRoots;
            SourceDetector.CloudSyncRoots = Settings.CloudSyncRoots;
            SourceDetector.ResetCache();

            _mode = startMode == ScanMode.InModel && _doc == null ? ScanMode.PreOpen : startMode;

            BrowseCommand = new RelayCommand(Browse, () => !IsBusy && !IsInModel);
            ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
            CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
            ExportCommand = new RelayCommand(Export, () => !IsBusy && Links.Count > 0);
            UpgradeCommand = new AsyncRelayCommand(UpgradeAsync, () => !IsBusy && Targets().Any(t => t.CanUpgrade));
            RebuildIfcCommand = new AsyncRelayCommand(RebuildIfcAsync, () => !IsBusy && Targets().Any(t => t.CanRebuildIfc));
            SkipLoadCommand = new AsyncRelayCommand(SkipLoadAsync, () => !IsBusy && Targets().Any(t => t.CanSkipLoad));
            RepathCommand = new AsyncRelayCommand(RepathAsync, () => !IsBusy && Selected != null && !Selected.IsNested && Selected.HasLocalFile
                                                                  && (!IsInModel || Selected.Kind == LinkKind.RVT));
            OpenFolderCommand = new RelayCommand(() => LinkActions.OpenFolder(Selected), () => Selected != null && Selected.HasLocalFile);
            CheckProblemsCommand = new RelayCommand(() => SetChecks(l => l.IsProblem), () => !IsBusy && Links.Count > 0);
            CheckVisibleCommand = new RelayCommand(() => SetChecks(l => true), () => !IsBusy && Links.Count > 0);
            ClearChecksCommand = new RelayCommand(ClearChecks, () => !IsBusy && CheckedCount > 0);

            ApplyNewLinks(new List<LinkInfo>());
        }

        /// <summary>Called by the window once it is shown.</summary>
        public async Task OnLoadedAsync()
        {
            if (IsInModel) await ScanAsync();
        }

        // ------------------------------------------------------------ mode
        private ScanMode _mode;
        public ScanMode Mode
        {
            get => _mode;
            set
            {
                if (value == _mode || IsBusy) { RaiseMode(); return; }
                if (value == ScanMode.InModel && _doc == null)
                {
                    StatusText = "No project is open — In-Model mode needs an open model.";
                    RaiseMode();
                    return;
                }
                _mode = value;
                RaiseMode();
                ApplyNewLinks(new List<LinkInfo>());
                StatusText = "Ready";
                if (value == ScanMode.InModel) ((AsyncRelayCommand)ScanCommand).Execute(null);
            }
        }

        public bool IsPreOpen { get => _mode == ScanMode.PreOpen; set { if (value) Mode = ScanMode.PreOpen; } }
        public bool IsInModel { get => _mode == ScanMode.InModel; set { if (value) Mode = ScanMode.InModel; } }
        public bool IsFolder { get => _mode == ScanMode.Folder; set { if (value) Mode = ScanMode.Folder; } }
        public bool IsNotInModel => !IsInModel;
        public bool HasActiveDoc => _doc != null;
        public string ActiveDocTitle => _doc?.Title ?? "(no project open)";

        private void RaiseMode()
        {
            Raise(nameof(Mode)); Raise(nameof(IsPreOpen)); Raise(nameof(IsInModel)); Raise(nameof(IsFolder));
            Raise(nameof(IsNotInModel)); Raise(nameof(HintText)); Raise(nameof(ModeText)); Raise(nameof(SkipLoadText));
            Raise(nameof(ShowHostColumn));
        }

        public string HintText =>
            IsPreOpen ? "Reads the host's link table and each link's version header WITHOUT opening any model. Fix problems before you open it." :
            IsFolder ? "Audits every .rvt in the folder the same way (no model opened). Backups (.0001.rvt), IFC caches and _LinkCheckerBackup are skipped." :
                       "Scans the open model: links, nested links, imported CAD, linked images, workset state and instance counts.";

        public string ModeText =>
            IsPreOpen ? "Pre-Open · file headers only" :
            IsFolder ? "Folder audit · file headers only" :
                       "In-Model · " + ActiveDocTitle;

        public string SkipLoadText => IsInModel ? "Unload now" : "Don't load on next open";
        public bool ShowHostColumn => IsFolder;

        // ------------------------------------------------------------ inputs
        private string _hostPath;
        public string HostPath { get => _hostPath; set => Set(ref _hostPath, value); }

        private string _folderPath;
        public string FolderPath { get => _folderPath; set => Set(ref _folderPath, value); }

        private bool _includeNested;
        public bool IncludeNested { get => _includeNested; set => Set(ref _includeNested, value); }

        private bool _includeSubfolders;
        public bool IncludeSubfolders { get => _includeSubfolders; set => Set(ref _includeSubfolders, value); }

        private bool _filterProblems;
        public bool FilterProblems { get => _filterProblems; set { if (Set(ref _filterProblems, value)) RefreshView(); } }

        private bool _filterUpgrade;
        public bool FilterUpgrade { get => _filterUpgrade; set { if (Set(ref _filterUpgrade, value)) RefreshView(); } }

        private string _typeFilter;
        public string TypeFilter { get => _typeFilter; set { if (Set(ref _typeFilter, value ?? "All")) RefreshView(); } }

        private string _sourceFilter = "All";
        public string SourceFilter { get => _sourceFilter; set { if (Set(ref _sourceFilter, value ?? "All")) RefreshView(); } }

        private bool _availableOnly = true;
        /// <summary>Ticked: the Source and Type lists show only the values present in this scan.</summary>
        public bool AvailableOnly
        {
            get => _availableOnly;
            set { if (Set(ref _availableOnly, value)) { RebuildFilterOptions(); RefreshView(); } }
        }

        private string _hostFilter = AllHosts;
        public string HostFilter { get => _hostFilter; set { if (Set(ref _hostFilter, value ?? AllHosts)) RefreshView(); } }

        private string _searchText = "";
        public string SearchText { get => _searchText; set { if (Set(ref _searchText, value ?? "")) RefreshView(); } }

        private LinkInfo _selected;
        public LinkInfo Selected
        {
            get => _selected;
            set
            {
                if (!Set(ref _selected, value)) return;
                Raise(nameof(HasSelection)); Raise(nameof(NoSelection)); Raise(nameof(TargetText));
            }
        }
        public bool HasSelection => _selected != null;
        public bool NoSelection => _selected == null;

        // ------------------------------------------------------------ busy / progress
        private bool _isBusy;
        public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) CommandManager.InvalidateRequerySuggested(); } }

        private double _progressValue;
        public double ProgressValue { get => _progressValue; private set => Set(ref _progressValue, value); }

        private double _progressMax = 1;
        public double ProgressMax { get => _progressMax; private set => Set(ref _progressMax, value); }

        private string _progressText = "";
        public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

        private string _statusText = "Ready";
        public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

        // ------------------------------------------------------------ summary
        public int CountTotal => Links.Count;
        public int CountAcc => Links.Count(l => LinkSourceInfo.IsAcc(l.Source));
        public int CountDisk => Links.Count(l => LinkSourceInfo.Group(l.Source) == SourceGroup.Disk);
        public int CountUpgrade => Links.Count(l => l.Findings.Any(f => f.RuleId == "TEMP_UPGRADE"));
        public int CountIfc => Links.Count(l => l.Findings.Any(f => f.RuleId == "IFC_CACHE"));
        public int CountMissing => Links.Count(l => l.Findings.Any(f => f.RuleId == "MISSING"));
        public string EstTotalText => FormatSeconds(Links.Where(l => l.CountsInTotal).Sum(l => l.EstSeconds));

        private string _measuredHostText = "—";
        public string MeasuredHostText { get => _measuredHostText; private set => Set(ref _measuredHostText, value); }

        private string _measuredHostSub = "Last measured full open";
        public string MeasuredHostSub { get => _measuredHostSub; private set => Set(ref _measuredHostSub, value); }

        public int CheckedCount => Links.Count(l => l.IsChecked);

        public string TargetText =>
            CheckedCount > 0 ? $"Actions apply to {CheckedCount} checked row(s)" :
            Selected != null ? "Actions apply to the selected row (tick rows for batch)" :
                               "Select a row, or tick rows for batch actions";

        // =================================================================== SCAN
        private async Task ScanAsync()
        {
            if (IsBusy) return;
            _cts = new CancellationTokenSource();
            IsBusy = true;
            var rep = NewReporter(_cts.Token);
            var sw = Stopwatch.StartNew();
            try
            {
                List<LinkInfo> result;
                switch (_mode)
                {
                    case ScanMode.PreOpen:
                        if (string.IsNullOrWhiteSpace(HostPath) || !File.Exists(HostPath))
                        {
                            StatusText = "Select a host .rvt file first.";
                            return;
                        }
                        result = await _scanner.ScanHostsAsync(new[] { HostPath }, IncludeNested, rep);
                        Settings.LastHostPath = HostPath;
                        Settings.PushRecentHost(HostPath);
                        SyncRecentHosts();
                        break;

                    case ScanMode.Folder:
                        if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
                        {
                            StatusText = "Select a project folder first.";
                            return;
                        }
                        ProgressText = "Finding models in the folder…";
                        var hosts = await LinkScanner.FindHostsAsync(FolderPath, IncludeSubfolders, _cts.Token);
                        if (hosts.Count == 0)
                        {
                            StatusText = "No .rvt host models found in that folder.";
                            ApplyNewLinks(new List<LinkInfo>());
                            return;
                        }
                        result = await _scanner.ScanHostsAsync(hosts, IncludeNested, rep);
                        Settings.LastFolder = FolderPath;
                        break;

                    default:
                        result = await _scanner.ScanDocumentAsync(_doc, rep);
                        break;
                }

                ApplyMeasured(result);
                ApplyNewLinks(result);

                int hostCount = result.Select(l => l.HostPath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                StatusText = $"Scanned {result.Count} links" + (IsFolder ? $" in {hostCount} models" : "") +
                             $" in {sw.Elapsed.TotalSeconds:0.0} s" +
                             (_scanner.HostErrors.Count > 0 ? $" · {_scanner.HostErrors.Count} host(s) unreadable: {string.Join("; ", _scanner.HostErrors.Take(3))}" : "");
            }
            catch (OperationCanceledException)
            {
                StatusText = "Scan cancelled.";
            }
            catch (Exception ex)
            {
                StatusText = "Scan failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                ProgressText = "";
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void ApplyMeasured(List<LinkInfo> links)
        {
            var store = LoadTimeStore.Instance;
            store.Reload();
            foreach (var l in links)
            {
                if (l.IsImport || string.IsNullOrEmpty(l.FullPath)) continue;   // cloud links are logged too
                var e = store.LatestForLink(l.FullPath, string.IsNullOrEmpty(l.IfcPath) ? null : l.IfcPath);
                if (e == null) continue;
                l.MeasuredSeconds = e.Seconds;
                l.MeasuredAt = e.At;
            }

            string host = IsInModel ? _doc?.PathName : IsPreOpen ? HostPath : null;
            var h = host != null ? store.LatestForHost(host) : null;
            if (h != null)
            {
                MeasuredHostText = FormatSeconds((int)Math.Round(h.Seconds));
                MeasuredHostSub = $"Last full open ({h.At:dd-MMM HH:mm})";
            }
            else
            {
                MeasuredHostText = "—";
                MeasuredHostSub = IsFolder ? "Measured per model in the Excel report" : "Last full open (not logged yet)";
            }
        }

        private void ApplyNewLinks(List<LinkInfo> list)
        {
            foreach (var old in Links) old.PropertyChanged -= OnLinkChanged;
            Links = new ObservableCollection<LinkInfo>(list);
            foreach (var l in Links) l.PropertyChanged += OnLinkChanged;

            var view = CollectionViewSource.GetDefaultView(Links);
            view.Filter = FilterRow;
            LinksView = view;

            HostOptions.Clear();
            HostOptions.Add(AllHosts);
            foreach (var h in list.Select(l => l.HostName).Distinct().OrderBy(h => h)) HostOptions.Add(h);
            _hostFilter = AllHosts;
            Raise(nameof(HostFilter));

            RebuildFilterOptions();

            Selected = null;
            RefreshCounts();
        }

        private void OnLinkChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LinkInfo.IsChecked))
            {
                Raise(nameof(CheckedCount));
                Raise(nameof(TargetText));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        // =================================================================== ACTIONS
        /// <summary>Checked rows if any are ticked, otherwise the selected row.</summary>
        private List<LinkInfo> Targets()
        {
            var checkedRows = Links.Where(l => l.IsChecked).ToList();
            if (checkedRows.Count > 0) return checkedRows;
            return Selected != null ? new List<LinkInfo> { Selected } : new List<LinkInfo>();
        }

        private LinkActions NewActions() => new LinkActions(_app, IsInModel ? _doc : null, _backups);

        private async Task UpgradeAsync()
        {
            var targets = Targets();
            var ok = targets.Where(t => t.CanUpgrade).Select(t => t.FullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!Confirm($"Upgrade {ok.Count} file(s) to Revit {_app.VersionNumber}?\n\n" +
                         $"• Each file is backed up to '{BackupService.FolderName}' beside it first.\n" +
                         "• Each file is opened in the background, saved in the new format and closed — that takes about as long as opening it once.\n" +
                         "• Anyone on an older Revit can no longer open the upgraded files.\n" +
                         (targets.Count > ok.Count ? $"• {targets.Count - ok.Count} row(s) are skipped (workshared, current or missing)." : ""))) return;
            await RunActionAsync("Upgrade & Save", rep => NewActions().UpgradeAsync(targets, rep));
        }

        private async Task RebuildIfcAsync()
        {
            var targets = Targets();
            int n = targets.Count(t => t.CanRebuildIfc);
            if (!Confirm($"Rebuild the IFC cache for {n} link(s)?\n\nOld .ifc.RVT files are kept as .bak." +
                         (IsInModel ? "\nLinks are reloaded now to write the new cache." : "\nNew caches are written on the next open of each host (that one open is slow)."))) return;
            await RunActionAsync("Rebuild IFC cache", rep => NewActions().RebuildIfcAsync(targets, rep));
        }

        private async Task SkipLoadAsync()
        {
            var targets = Targets();
            int n = targets.Count(t => t.CanSkipLoad);
            int cad = IsInModel ? targets.Count(t => t.CanSkipLoad && t.Kind == LinkKind.CAD) : 0;
            string q = IsInModel
                ? $"Unload {n - cad} link(s) now? You can reload them from Manage Links." +
                  (cad > 0 ? $"\n\n{cad} CAD link(s) will be skipped — the Revit API can't unload CAD in an open model (use Manage Links, or Pre-Open mode)." : "")
                : $"Keep {n} link(s) UNLOADED the next time their host opens?\n\nEach host is backed up first. Reload any time from Manage Links.";
            if (!Confirm(q)) return;
            await RunActionAsync(SkipLoadText, rep => Task.FromResult(NewActions().SkipLoad(targets, IsInModel)));
        }

        private async Task RepathAsync()
        {
            var li = Selected;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"New location for {li.Name}",
                Filter = "Linked files|*.rvt;*.ifc;*.dwg;*.dxf;*.dgn;*.skp;*.sat;*.rcp;*.rcs;*.pdf;*.png;*.jpg|All files|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true) return;
            if (!IsInModel && !Confirm($"Point '{li.Name}' in {li.HostName} to:\n{dlg.FileName}\n\nThe host is backed up first.")) return;

            await RunActionAsync("Repath", rep =>
            {
                var r = new ActionResult();
                try { r.Done.Add(NewActions().Repath(li, dlg.FileName, IsInModel)); }
                catch (Exception ex) { r.Failed.Add($"{li.Name} — {ex.Message}"); }
                return Task.FromResult(r);
            });
        }

        private async Task RunActionAsync(string verb, Func<ScanReporter, Task<ActionResult>> action)
        {
            _cts = new CancellationTokenSource();
            IsBusy = true;
            ActionResult result = null;
            try
            {
                result = await action(NewReporter(_cts.Token));
            }
            catch (Exception ex)
            {
                result = new ActionResult();
                result.Failed.Add(ex.Message);
            }
            finally
            {
                IsBusy = false;
                ProgressText = "";
                _cts?.Dispose();
                _cts = null;
            }

            MsgBox.Show(result.Summary(verb), Title, MsgButton.OK,
                        result.Failed.Count > 0 ? MsgImage.Warning : MsgImage.Information);

            _scanner.ClearCache();
            await ScanAsync();
            StatusText = result.Summary(verb).Split('\n')[0] + " · " + StatusText;
        }

        // =================================================================== MISC
        private void Browse()
        {
            if (IsFolder)
            {
                var fd = new Microsoft.Win32.OpenFolderDialog { Title = "Select project folder to audit" };
                if (Directory.Exists(FolderPath)) fd.InitialDirectory = FolderPath;
                if (fd.ShowDialog() == true) FolderPath = fd.FolderName;
                return;
            }

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select host Revit model",
                Filter = "Revit project (*.rvt)|*.rvt",
                CheckFileExists = true
            };
            try
            {
                if (!string.IsNullOrEmpty(HostPath) && Directory.Exists(Path.GetDirectoryName(HostPath)))
                    dlg.InitialDirectory = Path.GetDirectoryName(HostPath);
            }
            catch { /* ignore */ }
            if (dlg.ShowDialog() == true)
            {
                HostPath = dlg.FileName;
                ((AsyncRelayCommand)ScanCommand).Execute(null);
            }
        }

        private void Export()
        {
            string baseName = IsFolder ? new DirectoryInfo(FolderPath).Name
                            : IsInModel ? Path.GetFileNameWithoutExtension(_doc?.Title ?? "Model")
                            : Path.GetFileNameWithoutExtension(HostPath);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export link report",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                FileName = $"{baseName}_LinkReport_{DateTime.Now:yyyyMMdd}.xlsx"
            };
            if (Directory.Exists(Settings.LastExportFolder)) dlg.InitialDirectory = Settings.LastExportFolder;
            if (dlg.ShowDialog() != true) return;

            try
            {
                string written = ReportExporter.Export(dlg.FileName, Links.ToList(), h => LoadTimeStore.Instance.LatestForHost(h));
                Settings.LastExportFolder = Path.GetDirectoryName(written);
                StatusText = written.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                    ? "Excel library unavailable — exported CSV instead: " + written
                    : "Exported " + written;
                if (Confirm("Report saved. Open it now?\n\n" + written))
                    Process.Start(new ProcessStartInfo(written) { UseShellExecute = true });
            }
            catch (Exception ex) { StatusText = "Export failed: " + ex.Message; }
        }

        /// <summary>Ticks/unticks the rows currently visible through the filters.</summary>
        private void SetChecks(Func<LinkInfo, bool> rule)
        {
            foreach (LinkInfo l in LinksView.Cast<LinkInfo>()) l.IsChecked = rule(l);
        }

        private void ClearChecks()
        {
            foreach (var l in Links) l.IsChecked = false;   // also clears rows hidden by filters
        }

        private ScanReporter NewReporter(CancellationToken token) => new ScanReporter((d, t, s) =>
        {
            ProgressMax = Math.Max(1, t);
            ProgressValue = Math.Min(d, t);
            ProgressText = s;
        }, token);

        /// <summary>
        /// Rebuilds the Type and Source lists with their counts. With <see cref="AvailableOnly"/>
        /// ticked only the values present in the scan are offered; unticked shows all of them,
        /// zero-count entries included. The current selection is kept when it still exists.
        /// </summary>
        private void RebuildFilterOptions()
        {
            string keepType = LinkSourceInfo.Strip(_typeFilter);
            string keepSource = LinkSourceInfo.Strip(_sourceFilter);

            SourceOptions.Clear();
            foreach (string entry in LinkSourceInfo.BuildOptions(Links.Select(l => l.Source), AvailableOnly))
                SourceOptions.Add(entry);

            TypeOptions.Clear();
            TypeOptions.Add(LinkSourceInfo.WithCount(LinkSourceInfo.All, Links.Count));
            foreach (string t in AllTypeNames)
            {
                int n = Links.Count(l => l.KindText == t);
                if (AvailableOnly && n == 0) continue;
                TypeOptions.Add(LinkSourceInfo.WithCount(t, n));
            }

            _sourceFilter = Pick(SourceOptions, keepSource);
            _typeFilter = Pick(TypeOptions, keepType);
            Raise(nameof(SourceFilter));
            Raise(nameof(TypeFilter));
        }

        private static string Pick(IEnumerable<string> options, string wanted) =>
            options.FirstOrDefault(o => string.Equals(LinkSourceInfo.Strip(o), wanted, StringComparison.OrdinalIgnoreCase))
            ?? options.FirstOrDefault() ?? LinkSourceInfo.All;

        private bool FilterRow(object o)
        {
            if (!(o is LinkInfo l)) return false;
            if (FilterProblems && !l.IsProblem) return false;
            if (FilterUpgrade && !l.Findings.Any(f => f.RuleId == "TEMP_UPGRADE")) return false;
            if (!LinkSourceInfo.Matches(l.Source, SourceFilter)) return false;
            string type = LinkSourceInfo.Strip(TypeFilter);
            if (!string.Equals(type, LinkSourceInfo.All, StringComparison.OrdinalIgnoreCase) && l.KindText != type) return false;
            if (HostFilter != AllHosts && l.HostName != HostFilter) return false;
            if (!string.IsNullOrWhiteSpace(SearchText) &&
                (l.Name + " " + l.FullPath + " " + l.HostName).IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        private void RefreshView()
        {
            LinksView?.Refresh();
            if (LinksView != null) StatusText = $"Showing {LinksView.Cast<object>().Count()} of {Links.Count} links";
        }

        private void RefreshCounts()
        {
            foreach (var n in new[] { nameof(CountTotal), nameof(CountAcc), nameof(CountDisk), nameof(CountUpgrade), nameof(CountIfc), nameof(CountMissing),
                                      nameof(EstTotalText), nameof(CheckedCount), nameof(TargetText) })
                Raise(n);
        }

        private void SyncRecentHosts()
        {
            RecentHosts.Clear();
            foreach (var h in Settings.RecentHosts) RecentHosts.Add(h);
            Raise(nameof(HostPath));
        }

        public void SaveSettings()
        {
            Settings.LastHostPath = HostPath;
            Settings.LastFolder = FolderPath;
            Settings.IncludeNested = IncludeNested;
            Settings.IncludeSubfolders = IncludeSubfolders;
            Settings.FilterProblems = FilterProblems;
            Settings.TypeFilter = LinkSourceInfo.Strip(TypeFilter);
            Settings.SourceFilter = LinkSourceInfo.Strip(SourceFilter);
            Settings.AvailableTypesOnly = AvailableOnly;
            SettingsStore.Save(Settings);
        }

        public void CancelRunning() => _cts?.Cancel();

        private static bool Confirm(string text) =>
            MsgBox.Show(text, Title, MsgButton.OKCancel, MsgImage.Question) == MsgResult.OK;

        private static string FormatSeconds(int s) => s >= 60 ? $"{s / 60}m {s % 60}s" : $"{s}s";
    }
}
