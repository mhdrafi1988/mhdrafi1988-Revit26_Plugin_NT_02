using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.DetailLineManager.V001.Core.Models;
using Revit26_Plugin_Revised.DetailLineManager.V001.Core.Services;
using Revit26_Plugin_Revised.DetailLineManager.V001.Infrastructure;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.DetailLineManager.V001.UI.ViewModels
{
    /// <summary>
    /// View model of the Detail Line Manager window. Holds the scanned rows and every filter and
    /// option; all Revit reads and writes go through one external event (<see cref="DetailLineManagerHandler"/>).
    /// This part: state, settings, search and Revit selection.
    /// </summary>
    public sealed partial class MainViewModel : ToolViewModelBase
    {
        /// <summary>Folder name under %AppData%\Revit26_Plugin_Revised for settings.json.</summary>
        public const string SettingsFolder = "DetailLineManager";

        private const string AllViewTypes = "All view types";
        private const string AllWorksets = "All worksets";

        private readonly Document _doc;
        private readonly DetailLineManagerHandler _handler = new();
        private readonly ExternalEvent _externalEvent;
        private readonly Dispatcher _dispatcher;
        private bool _loadingSettings;
        private bool _suspendRefresh;

        /// <summary>Detail lines from the last Find lines run.</summary>
        public ObservableCollection<DetailLineRow> Rows { get; } = new();

        /// <summary>Filtered, sortable view of <see cref="Rows"/> bound to the grid.</summary>
        public ICollectionView RowsView { get; }

        /// <summary>Every line style; ticked ones are shown in the grid. Also the Assign line style list.</summary>
        public ObservableCollection<LineStyleItem> Styles { get; } = new();

        /// <summary>Unused, non-built-in line styles offered for deletion.</summary>
        public ObservableCollection<LineStyleItem> UnusedStyles { get; } = new();

        /// <summary>View type filter options; the first means all.</summary>
        public ObservableCollection<string> ViewTypeOptions { get; } = new() { AllViewTypes };

        /// <summary>Workset filter options; the first means all.</summary>
        public ObservableCollection<string> WorksetOptions { get; } = new() { AllWorksets };

        /// <summary>Line weights 1–16 for the weight picker.</summary>
        public int[] WeightOptions { get; } = Enumerable.Range(1, 16).ToArray();

        /// <summary>Row selected (highlighted) in the grid.</summary>
        [ObservableProperty] private DetailLineRow selectedRow;

        /// <summary>Scope text of the last search.</summary>
        [ObservableProperty] private string lastScopeText = "Not searched yet";

        /// <summary>Creates the view model for the active document and starts the first search.</summary>
        public MainViewModel(UIApplication uiApp)
        {
            _doc = uiApp.ActiveUIDocument.Document;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _externalEvent = ExternalEvent.Create(_handler);

            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.Filter = o => o is DetailLineRow r && _filter.Matches(r);

            LoadSettings();
            SummaryText = "Ready";
            Log(LogLevel.Info, $"{ToolCatalog.DetailLineManager.Title} opened on '{_doc.Title}'.");
            FindLines();
        }

        /// <summary>Document this window works on.</summary>
        public string DocumentTitle => _doc.Title + (_doc.IsWorkshared ? " · Workshared" : string.Empty);

        // ── Scope (segmented control) ─────────────────────────────────────

        /// <summary>Search scope.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ScopeSelected), nameof(ScopeActiveView), nameof(ScopeViewsOnSheets), nameof(ScopeWholeProject))]
        private SearchScope scope = SearchScope.ActiveView;

        /// <summary>Scope = Selected; bound to its segment.</summary>
        public bool ScopeSelected { get => Scope == SearchScope.Selected; set { if (value) Scope = SearchScope.Selected; } }

        /// <summary>Scope = Active view; bound to its segment.</summary>
        public bool ScopeActiveView { get => Scope == SearchScope.ActiveView; set { if (value) Scope = SearchScope.ActiveView; } }

        /// <summary>Scope = Views on sheets; bound to its segment.</summary>
        public bool ScopeViewsOnSheets { get => Scope == SearchScope.ViewsOnSheets; set { if (value) Scope = SearchScope.ViewsOnSheets; } }

        /// <summary>Scope = Whole project; bound to its segment.</summary>
        public bool ScopeWholeProject { get => Scope == SearchScope.WholeProject; set { if (value) Scope = SearchScope.WholeProject; } }

        // ── Commands: search and Revit selection ─────────────────────────

        /// <summary>Collects detail lines for the scope and refreshes the grid, styles and counts.</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void FindLines()
        {
            var scope = Scope;
            double tol = ToleranceMm;
            SaveSettings();
            HideConfirm();
            Run($"Searching ({scope})…", app =>
            {
                var uiDoc = app.ActiveUIDocument;
                var result = DetailLineScanner.Scan(uiDoc, scope, tol, Log);
                _dispatcher.Invoke(() => ApplyScan(result));
            });
        }

        /// <summary>Selects the checked lines (or the highlighted row) in Revit.</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void SelectInRevit()
        {
            var ids = CheckedVisibleRows().Select(r => r.Id).ToList();
            if (ids.Count == 0 && SelectedRow != null) ids.Add(SelectedRow.Id);
            if (ids.Count == 0) { Log(LogLevel.Warning, "Check or highlight lines in the grid first."); return; }

            Run("Selecting…", app =>
            {
                app.ActiveUIDocument.Selection.SetElementIds(ids.Where(id => _doc.GetElement(id) != null).ToList());
                Log(LogLevel.Success, $"Selected {ids.Count} line(s) in Revit.");
            });
        }

        /// <summary>Zooms Revit to the highlighted row (or the first checked line), opening its view if needed.</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void ShowElement()
        {
            var row = SelectedRow ?? CheckedVisibleRows().FirstOrDefault();
            if (row == null) { Log(LogLevel.Warning, "Highlight or check a line in the grid first."); return; }

            Run("Showing element…", app =>
            {
                if (_doc.GetElement(row.Id) == null)
                {
                    Log(LogLevel.Warning, $"Line {row.IdValue} no longer exists. Click Find lines to refresh.");
                    return;
                }
                app.ActiveUIDocument.ShowElements(row.Id);
                Log(LogLevel.Info, $"Showing line {row.IdValue} in '{row.ViewName}'.");
            });
        }

        // ── Scan result ──────────────────────────────────────────────────

        private void ApplyScan(ScanResult result)
        {
            _suspendRefresh = true;
            try
            {
                foreach (var s in Styles) s.PropertyChanged -= OnStyleCheckedChanged;
                foreach (var r in Rows) r.PropertyChanged -= OnRowChanged;

                Rows.Clear();
                foreach (var r in result.Rows)
                {
                    r.PropertyChanged += OnRowChanged;
                    Rows.Add(r);
                }

                var keepAssign = SelectedAssignStyle?.Name ?? _pendingAssignStyleName;
                Styles.Clear();
                foreach (var s in result.Styles)
                {
                    s.IsChecked = !_excludedStyles.Contains(s.Name);
                    s.PropertyChanged += OnStyleCheckedChanged;
                    Styles.Add(s);
                }

                UnusedStyles.Clear();
                foreach (var s in result.Styles.Where(s => s.IsUnused))
                {
                    UnusedStyles.Add(new LineStyleItem
                    {
                        CategoryId = s.CategoryId,
                        GraphicsStyleId = s.GraphicsStyleId,
                        Name = s.Name,
                        Weight = s.Weight,
                        UseCount = s.UseCount,
                        IsBuiltIn = s.IsBuiltIn
                    });
                }

                SelectedAssignStyle = Styles.FirstOrDefault(s => s.Name == keepAssign && s.GraphicsStyleId != ElementId.InvalidElementId)
                    ?? Styles.FirstOrDefault(s => s.Name == "Thin Lines")
                    ?? Styles.FirstOrDefault();

                ResetOptions(ViewTypeOptions, AllViewTypes, Rows.Select(r => r.ViewTypeName), SelectedViewType, v => SelectedViewType = v);
                ResetOptions(WorksetOptions, AllWorksets, Rows.Select(r => r.WorksetName), SelectedWorkset, v => SelectedWorkset = v);

                LastScopeText = result.ScopeText;
            }
            finally
            {
                _suspendRefresh = false;
            }
            RebuildFilter();
        }

        private void ResetOptions(ObservableCollection<string> options, string all, System.Collections.Generic.IEnumerable<string> values,
                                  string current, Action<string> restore)
        {
            options.Clear();
            options.Add(all);
            foreach (var v in values.Where(v => !string.IsNullOrEmpty(v)).Distinct().OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase))
                options.Add(v);
            // A filter saved last time stays selectable even when no found line matches it.
            if (!string.IsNullOrEmpty(current) && !options.Contains(current)) options.Add(current);
            // Clearing the list can reset the bound ComboBox selection; put the choice back.
            restore(string.IsNullOrEmpty(current) ? all : current);
        }

        // ── Running actions in the Revit API context ─────────────────────

        private bool CanRun() => !IsRunning;

        private void SetBusy(bool busy)
        {
            IsRunning = busy;
            FindLinesCommand.NotifyCanExecuteChanged();
            SelectInRevitCommand.NotifyCanExecuteChanged();
            ShowElementCommand.NotifyCanExecuteChanged();
            PreviewDeleteCommand.NotifyCanExecuteChanged();
            DeleteCheckedCommand.NotifyCanExecuteChanged();
            DeleteAllFoundCommand.NotifyCanExecuteChanged();
            ConfirmDeleteCommand.NotifyCanExecuteChanged();
            ApplyCheckedCommand.NotifyCanExecuteChanged();
            ApplyAllFoundCommand.NotifyCanExecuteChanged();
        }

        // Queues work for the external event. The body runs in the Revit API context; expected
        // failures are logged there, anything else is logged, reported and leaves the model unchanged.
        private void Run(string busyText, Action<UIApplication> body)
        {
            if (IsRunning) return;
            SetBusy(true);
            SummaryText = busyText;

            _handler.Queue(app =>
            {
                try
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null || !doc.Equals(_doc))
                    {
                        Log(LogLevel.Error, $"The active document is not '{_doc.Title}'. Switch back to it, or close this window and open the tool again.");
                        return;
                    }
                    body(app);
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.OperationCanceledException or OperationCanceledException)
                {
                    Log(LogLevel.Info, "Cancelled.");
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Error, $"Stopped: {ex.Message} The change was rolled back; nothing was changed.");
                    ToolGuard.Report(GetType(), ex);
                }
                finally
                {
                    _dispatcher.Invoke(() =>
                    {
                        SetBusy(false);
                        UpdateCounts();
                    });
                }
            });

            if (_externalEvent.Raise() is ExternalEventRequest.Denied or ExternalEventRequest.TimedOut)
            {
                SetBusy(false);
                Log(LogLevel.Error, "Revit is busy (a dialog or edit mode is open). Finish it and try again.");
            }
        }

        // ── Settings ──────────────────────────────────────────────────────

        private void LoadSettings()
        {
            var s = SettingsService<ToolSettings>.Load(SettingsFolder);
            if (s.Version > ToolSettings.CurrentVersion)
                Log(LogLevel.Warning, $"Settings were saved by a newer version (format {s.Version}); unknown options are ignored.");
            s.Migrate();

            _loadingSettings = true;
            try
            {
                Scope = s.Scope;
                SearchText = s.SearchText;
                UseRegex = s.UseRegex;
                _excludedStyles.Clear();
                foreach (var n in s.ExcludedStyles) _excludedStyles.Add(n);
                SelectedViewType = string.IsNullOrEmpty(s.ViewType) ? AllViewTypes : s.ViewType;
                MinWeight = s.MinWeight;
                MaxWeight = s.MaxWeight;
                MinLengthMm = s.MinLengthMm;
                MaxLengthMm = s.MaxLengthMm;
                IncludeLines = s.IncludeLines;
                IncludeArcs = s.IncludeArcs;
                IncludeSplines = s.IncludeSplines;
                IncludeEllipses = s.IncludeEllipses;
                SelectedWorkset = string.IsNullOrEmpty(s.Workset) ? AllWorksets : s.Workset;
                OnlyDuplicates = s.OnlyDuplicates;
                OnlyShort = s.OnlyShort;
                OnlyInGroups = s.OnlyInGroups;
                OnlyOwnedByOthers = s.OnlyOwnedByOthers;
                ToleranceMm = s.ToleranceMm;
                DeleteUnusedStyles = s.DeleteUnusedStyles;
                DeleteDuplicates = s.DeleteDuplicates;
                DeleteShort = s.DeleteShort;
                SkipBlocked = s.SkipBlocked;
                KeepRule = s.KeepRule;
                ThicknessMode = s.ThicknessMode;
                Weight = s.Weight;
                ClearOverrides = s.ClearOverrides;
                _pendingAssignStyleName = s.AssignStyleName;
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        /// <summary>Saves the current filters and options. Called after each search and when the window closes.</summary>
        public void SaveSettings()
        {
            if (_loadingSettings) return;
            var s = new ToolSettings
            {
                Version = ToolSettings.CurrentVersion,
                Scope = Scope,
                SearchText = SearchText ?? string.Empty,
                UseRegex = UseRegex,
                ExcludedStyles = Styles.Count > 0
                    ? Styles.Where(x => !x.IsChecked).Select(x => x.Name).ToList()
                    : _excludedStyles.ToList(),
                ViewType = SelectedViewType == AllViewTypes ? string.Empty : SelectedViewType ?? string.Empty,
                MinWeight = MinWeight,
                MaxWeight = MaxWeight,
                MinLengthMm = MinLengthMm,
                MaxLengthMm = MaxLengthMm,
                IncludeLines = IncludeLines,
                IncludeArcs = IncludeArcs,
                IncludeSplines = IncludeSplines,
                IncludeEllipses = IncludeEllipses,
                Workset = SelectedWorkset == AllWorksets ? string.Empty : SelectedWorkset ?? string.Empty,
                OnlyDuplicates = OnlyDuplicates,
                OnlyShort = OnlyShort,
                OnlyInGroups = OnlyInGroups,
                OnlyOwnedByOthers = OnlyOwnedByOthers,
                ToleranceMm = ToleranceMm,
                DeleteUnusedStyles = DeleteUnusedStyles,
                DeleteDuplicates = DeleteDuplicates,
                DeleteShort = DeleteShort,
                SkipBlocked = SkipBlocked,
                KeepRule = KeepRule,
                ThicknessMode = ThicknessMode,
                Weight = Weight,
                AssignStyleName = SelectedAssignStyle?.Name ?? _pendingAssignStyleName ?? string.Empty,
                ClearOverrides = ClearOverrides
            };
            SettingsService<ToolSettings>.Save(SettingsFolder, s);
        }

        /// <summary>Saves settings and releases the external event. Called when the window closes.</summary>
        public void OnWindowClosed()
        {
            SaveSettings();
            _externalEvent.Dispose();
        }

        // ── Log ───────────────────────────────────────────────────────────

        /// <summary>Adds a log line; safe to call from the Revit API context.</summary>
        public void Log(LogLevel level, string message)
        {
            if (_dispatcher.CheckAccess()) AddLog(level, message);
            else _dispatcher.Invoke(() => AddLog(level, message));
        }
    }
}
