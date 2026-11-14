using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Core;
using Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Infrastructure;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AnnotationOverlapReport.V001.UI
{
    /// <summary>
    /// View model of the Annotation Overlap Report window. It never touches the Revit API: scans and zooms
    /// are requested through external events and their results come back on the Revit (UI) thread.
    /// </summary>
    public sealed partial class AnnotationOverlapReportViewModel : ObservableObject
    {
        private const string AllTypes = "All types";
        private const string AllViews = "All views";

        private readonly ExternalEvent _scanEvent;
        private readonly ScanEventHandler _scanHandler;
        private readonly ExternalEvent _zoomEvent;
        private readonly ZoomEventHandler _zoomHandler;
        private readonly ReportSettings _settings;
        private readonly HashSet<string> _uncheckedTypes;
        private readonly DispatcherTimer _countTimer;
        private readonly List<ReportRow> _allRows = new List<ReportRow>();
        private ReportRunInfo _lastRun;
        private bool _countPending;

        /// <summary>Creates the view model for the given view list and wires the two external events.</summary>
        public AnnotationOverlapReportViewModel(IEnumerable<ViewEntry> views, ExternalEvent scanEvent,
            ScanEventHandler scanHandler, ExternalEvent zoomEvent, ZoomEventHandler zoomHandler)
        {
            _scanEvent = scanEvent;
            _scanHandler = scanHandler;
            _zoomEvent = zoomEvent;
            _zoomHandler = zoomHandler;
            _scanHandler.Completed = OnScanCompleted;
            _zoomHandler.Failed = message => ShowError(message);

            _settings = ReportSettings.Load();
            _uncheckedTypes = new HashSet<string>(_settings.UncheckedTypes, StringComparer.OrdinalIgnoreCase);
            _acrossTypes = _settings.AcrossTypes;

            foreach (ViewEntry entry in views ?? Enumerable.Empty<ViewEntry>())
            {
                var row = new ViewRow { Id = entry.Id, Name = entry.Name, Group = entry.Group };
                row.PropertyChanged += OnViewRowChanged;
                Views.Add(row);
            }

            ViewsView = CollectionViewSource.GetDefaultView(Views);
            ViewsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ViewRow.Group)));
            ViewsView.Filter = o => o is ViewRow row && (string.IsNullOrWhiteSpace(SearchText)
                || row.Name.IndexOf(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);

            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ReportRow.ViewGroup)));

            TypeFilterOptions.Add(AllTypes);
            ViewFilterOptions.Add(AllViews);
            _selectedTypeFilter = AllTypes;
            _selectedViewFilter = AllViews;

            _countTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _countTimer.Tick += (_, _) => { _countTimer.Stop(); RequestCount(); };

            UpdateScopeText();
            RequestCount();
        }

        // ─── Scope ───────────────────────────────────────────────────

        /// <summary>Every sheet and supported view of the model.</summary>
        public ObservableCollection<ViewRow> Views { get; } = new ObservableCollection<ViewRow>();

        /// <summary>The Scope list: <see cref="Views"/> grouped by Sheets / Views and filtered by <see cref="SearchText"/>.</summary>
        public ICollectionView ViewsView { get; }

        /// <summary>True checks the active view; false checks the ticked views and sheets.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsChosenScope))]
        private bool _activeViewOnly = true;

        /// <summary>True when the ticked views and sheets are checked.</summary>
        public bool IsChosenScope
        {
            get => !ActiveViewOnly;
            set => ActiveViewOnly = !value;
        }

        /// <summary>Filter text of the Scope list.</summary>
        [ObservableProperty]
        private string _searchText = "";

        /// <summary>"4 of 86".</summary>
        [ObservableProperty]
        private string _scopeCountText = "";

        // ─── Types ───────────────────────────────────────────────────

        /// <summary>Every annotation type found in the scope; head types are listed greyed out.</summary>
        public ObservableCollection<TypeRow> Types { get; } = new ObservableCollection<TypeRow>();

        /// <summary>"20 types found · 859 annotations".</summary>
        [ObservableProperty]
        private string _typesSummary = "Reading annotation types…";

        /// <summary>Also compare annotations of different types (on by default).</summary>
        [ObservableProperty]
        private bool _acrossTypes;

        // ─── Results ─────────────────────────────────────────────────

        /// <summary>The pairs currently shown (after the Show filters).</summary>
        public ObservableCollection<ReportRow> Rows { get; } = new ObservableCollection<ReportRow>();

        /// <summary><see cref="Rows"/> grouped by view.</summary>
        public ICollectionView RowsView { get; }

        /// <summary>Choices of the type filter.</summary>
        public ObservableCollection<string> TypeFilterOptions { get; } = new ObservableCollection<string>();

        /// <summary>Choices of the view filter.</summary>
        public ObservableCollection<string> ViewFilterOptions { get; } = new ObservableCollection<string>();

        /// <summary>Selected type filter.</summary>
        [ObservableProperty]
        private string _selectedTypeFilter;

        /// <summary>Selected view filter.</summary>
        [ObservableProperty]
        private string _selectedViewFilter;

        /// <summary>The selected pair.</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ZoomToPairCommand))]
        private ReportRow _selectedRow;

        /// <summary>Tile: number of overlapping pairs.</summary>
        [ObservableProperty]
        private int _totalOverlaps;

        /// <summary>Tile: "3 of 4".</summary>
        [ObservableProperty]
        private string _viewsWithOverlapsText = "–";

        /// <summary>Tile: annotations compared.</summary>
        [ObservableProperty]
        private int _totalScanned;

        /// <summary>Tile: types that took part.</summary>
        [ObservableProperty]
        private int _typesChecked;

        /// <summary>"23 overlapping pairs in 3 views".</summary>
        [ObservableProperty]
        private string _resultSummary = "Press Check overlaps to start.";

        // ─── State ───────────────────────────────────────────────────

        /// <summary>True while a check is running.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsIdle))]
        [NotifyCanExecuteChangedFor(nameof(CheckOverlapsCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportToExcelCommand))]
        private bool _isBusy;

        /// <summary>True when no check is running.</summary>
        public bool IsIdle => !IsBusy;

        /// <summary>Progress 0–100, driven by the shell while a check runs.</summary>
        [ObservableProperty]
        private double _progress;

        /// <summary>Progress text, driven by the shell while a check runs.</summary>
        [ObservableProperty]
        private string _summaryText = "";

        /// <summary>Error banner text.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        private string _errorMessage = "";

        /// <summary>Info banner text.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasInfo))]
        private string _infoMessage = "";

        /// <summary>True when the error banner shows.</summary>
        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        /// <summary>True when the info banner shows.</summary>
        public bool HasInfo => !string.IsNullOrEmpty(InfoMessage);

        // ─── Commands ────────────────────────────────────────────────

        /// <summary>Runs the overlap check on the scope.</summary>
        [RelayCommand(CanExecute = nameof(IsIdle))]
        private void CheckOverlaps()
        {
            ScanRequest request = BuildRequest(countOnly: false);
            if (!request.ActiveViewOnly && request.ViewIds.Count == 0)
            {
                ShowError("Tick at least one view or sheet, or choose Active view.");
                return;
            }
            if (Types.Count > 0 && !Types.Any(t => t.CanCheck && t.IsSelected))
            {
                ShowError("Tick at least one annotation type.");
                return;
            }

            ErrorMessage = "";
            InfoMessage = "";
            IsBusy = true;
            _scanHandler.Request = request;
            if (_scanEvent.Raise() != ExternalEventRequest.Accepted)
            {
                IsBusy = false;
                ShowError("Revit is busy. Finish the current command in Revit, then press Check overlaps again.");
            }
        }

        /// <summary>Opens the pair's view, zooms to both annotations and selects them.</summary>
        [RelayCommand(CanExecute = nameof(CanZoom))]
        private void ZoomToPair()
        {
            ReportRow row = SelectedRow;
            if (row == null)
                return;

            ErrorMessage = "";
            _zoomHandler.ViewId = row.ViewId;
            _zoomHandler.IdA = row.IdA;
            _zoomHandler.IdB = row.IdB;
            if (_zoomEvent.Raise() != ExternalEventRequest.Accepted)
                ShowError("Revit is busy. Finish the current command in Revit, then try again.");
        }

        private bool CanZoom() => SelectedRow != null;

        /// <summary>Saves the pairs currently shown to an .xlsx file the user chooses.</summary>
        [RelayCommand(CanExecute = nameof(CanExport))]
        private void ExportToExcel()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export overlaps to Excel",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = "Annotation Overlap Report " + DateTime.Now.ToString("yyyy-MM-dd HHmm") + ".xlsx"
            };
            if (!string.IsNullOrWhiteSpace(_settings.LastExportFolder) && Directory.Exists(_settings.LastExportFolder))
                dialog.InitialDirectory = _settings.LastExportFolder;

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                ExcelReportWriter.Write(dialog.FileName, Rows.ToList(), _lastRun);
                _settings.LastExportFolder = Path.GetDirectoryName(dialog.FileName);
                ErrorMessage = "";
                InfoMessage = "Saved " + Rows.Count + " pair(s) to " + dialog.FileName;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ShowError("Could not save the file: " + ex.Message +
                          " If it is open in Excel, close it and export again. The list is still here.");
            }
        }

        private bool CanExport() => IsIdle && Rows.Count > 0;

        /// <summary>Ticks every checkable type.</summary>
        [RelayCommand]
        private void SelectAllTypes() => SetAllTypes(true);

        /// <summary>Unticks every type.</summary>
        [RelayCommand]
        private void SelectNoTypes() => SetAllTypes(false);

        /// <summary>Saves the options; called when the window closes. Never throws.</summary>
        public void SaveSettings()
        {
            _countTimer.Stop();
            _settings.AcrossTypes = AcrossTypes;
            _settings.UncheckedTypes = _uncheckedTypes.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            _settings.TrySave(out _);
        }

        // ─── Change handlers ─────────────────────────────────────────

        partial void OnActiveViewOnlyChanged(bool value)
        {
            UpdateScopeText();
            ScheduleCount();
        }

        partial void OnSearchTextChanged(string value) => ViewsView.Refresh();

        partial void OnSelectedTypeFilterChanged(string value) => ApplyFilters();

        partial void OnSelectedViewFilterChanged(string value) => ApplyFilters();

        private void OnViewRowChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ViewRow.IsSelected))
                return;
            UpdateScopeText();
            if (!ActiveViewOnly)
                ScheduleCount();
        }

        private void OnTypeRowChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TypeRow.IsSelected) || !(sender is TypeRow row))
                return;
            if (row.IsSelected) _uncheckedTypes.Remove(row.Name);
            else _uncheckedTypes.Add(row.Name);
        }

        // ─── Scan plumbing ───────────────────────────────────────────

        private ScanRequest BuildRequest(bool countOnly) => new ScanRequest
        {
            CountOnly = countOnly,
            ActiveViewOnly = ActiveViewOnly,
            ViewIds = Views.Where(v => v.IsSelected).Select(v => v.Id).ToList(),
            ExcludedTypes = new HashSet<string>(_uncheckedTypes, StringComparer.OrdinalIgnoreCase),
            AcrossTypes = AcrossTypes
        };

        private void ScheduleCount()
        {
            _countTimer.Stop();
            _countTimer.Start();
        }

        /// <summary>Asks Revit for the annotation types in the scope (no overlap check).</summary>
        private void RequestCount()
        {
            if (IsBusy)
            {
                _countPending = true;
                return;
            }

            ScanRequest request = BuildRequest(countOnly: true);
            if (!request.ActiveViewOnly && request.ViewIds.Count == 0)
            {
                Types.Clear();
                TypesSummary = "Tick views or sheets to list their annotation types.";
                return;
            }

            _scanHandler.Request = request;
            if (_scanEvent.Raise() != ExternalEventRequest.Accepted)
                TypesSummary = "Revit is busy; the type list will refresh on the next check.";
        }

        private void OnScanCompleted(ScanOutcome outcome)
        {
            if (outcome == null)
                return;

            bool wasCheck = outcome.Request != null && !outcome.Request.CountOnly;
            if (wasCheck)
                IsBusy = false;

            if (outcome.Cancelled)
            {
                ShowError("Check cancelled. Nothing in the model was changed.");
            }
            else if (!string.IsNullOrEmpty(outcome.Error))
            {
                if (wasCheck) ShowError(outcome.Error);
                else TypesSummary = outcome.Error;
            }
            else
            {
                FillTypes(outcome);
                if (wasCheck)
                    FillResults(outcome);
            }

            if (_countPending && !IsBusy)
            {
                _countPending = false;
                ScheduleCount();
            }
        }

        private void FillTypes(ScanOutcome outcome)
        {
            foreach (TypeRow old in Types)
                old.PropertyChanged -= OnTypeRowChanged;
            Types.Clear();

            foreach (KeyValuePair<string, int> pair in outcome.TypeCounts.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                var row = new TypeRow { Name = pair.Key, Count = pair.Value, IsSelected = !_uncheckedTypes.Contains(pair.Key) };
                row.PropertyChanged += OnTypeRowChanged;
                Types.Add(row);
            }
            foreach (KeyValuePair<string, int> pair in outcome.DeferredCounts.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                Types.Add(new TypeRow { Name = pair.Key, Count = pair.Value, IsDeferred = true, IsSelected = false });

            int total = outcome.TypeCounts.Values.Sum();
            TypesSummary = outcome.TypeCounts.Count == 0
                ? "No annotations found in the scope."
                : outcome.TypeCounts.Count + " types found · " + total + " annotations";
            if (outcome.Info.NotMeasured > 0)
                TypesSummary += " · " + outcome.Info.NotMeasured + " not measured";
        }

        private void FillResults(ScanOutcome outcome)
        {
            _lastRun = outcome.Info;
            _allRows.Clear();
            _allRows.AddRange(outcome.Rows);

            TotalOverlaps = outcome.Rows.Count;
            TotalScanned = outcome.Info.AnnotationsScanned;
            ViewsWithOverlapsText = outcome.ViewsWithOverlaps + " of " + outcome.Info.ViewsScanned;
            TypesChecked = outcome.TypeCounts.Keys.Count(name => !outcome.Request.ExcludedTypes.Contains(name));

            Refill(TypeFilterOptions, AllTypes,
                outcome.Rows.SelectMany(r => new[] { r.TypeA, r.TypeB }).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            Refill(ViewFilterOptions, AllViews, outcome.Rows.Select(r => r.View).Distinct());
            SelectedTypeFilter = AllTypes;
            SelectedViewFilter = AllViews;
            ApplyFilters();

            var notes = new List<string>
            {
                outcome.Rows.Count == 0
                    ? "No overlaps found in " + outcome.Info.ViewsScanned + " view(s)."
                    : outcome.Rows.Count + " overlapping pair(s) in " + outcome.ViewsWithOverlaps + " view(s)."
            };
            if (outcome.Info.NotMeasured > 0)
                notes.Add(outcome.Info.NotMeasured + " annotation(s) could not be measured (Revit gave no box in their view).");
            notes.Add("Nothing in the model was changed.");
            InfoMessage = string.Join(" ", notes);

            ErrorMessage = outcome.Info.SkippedViews.Count == 0
                ? ""
                : "Skipped " + outcome.Info.SkippedViews.Count + " view(s): " + string.Join("; ", outcome.Info.SkippedViews.Take(3))
                  + (outcome.Info.SkippedViews.Count > 3 ? "; …" : "");
        }

        private void ApplyFilters()
        {
            string type = SelectedTypeFilter;
            string view = SelectedViewFilter;
            IEnumerable<ReportRow> rows = _allRows;
            if (!string.IsNullOrEmpty(type) && type != AllTypes)
                rows = rows.Where(r => r.TypeA == type || r.TypeB == type);
            if (!string.IsNullOrEmpty(view) && view != AllViews)
                rows = rows.Where(r => r.View == view);

            Rows.Clear();
            foreach (ReportRow row in rows)
                Rows.Add(row);

            int viewCount = Rows.Select(r => r.ViewId).Distinct().Count();
            ResultSummary = _lastRun == null
                ? "Press Check overlaps to start."
                : Rows.Count + " overlapping pair(s) in " + viewCount + " view(s)"
                  + (Rows.Count != _allRows.Count ? " (filtered from " + _allRows.Count + ")" : "");
            ExportToExcelCommand.NotifyCanExecuteChanged();
        }

        private static void Refill(ObservableCollection<string> target, string first, IEnumerable<string> values)
        {
            target.Clear();
            target.Add(first);
            foreach (string value in values)
                target.Add(value);
        }

        private void SetAllTypes(bool selected)
        {
            foreach (TypeRow row in Types.Where(t => t.CanCheck))
                row.IsSelected = selected;
        }

        private void UpdateScopeText() =>
            ScopeCountText = Views.Count(v => v.IsSelected) + " of " + Views.Count;

        private void ShowError(string message)
        {
            InfoMessage = "";
            ErrorMessage = message ?? "";
        }
    }
}
