using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.DwgManager.V003.Core.Models;
using Revit26_Plugin_Revised.DwgManager.V003.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;

namespace Revit26_Plugin_Revised.DwgManager.V003.UI.ViewModels
{
    /// <summary>
    /// View model of the DWG Manager window: lists CAD links and imports grouped by link type and view
    /// type, opens up to five of their views, selects instances, and deletes them (optionally purging
    /// types no longer placed anywhere). Every Revit read or write after the first scan goes through
    /// the tool's external event.
    /// </summary>
    public partial class DwgManagerViewModel : ToolViewModelBase
    {
        /// <summary>Most views Open Views opens at once.</summary>
        public const int MaxOpenViews = 5;

        private readonly Document _doc;
        private readonly DwgManagerHandler _handler = new();
        private readonly ExternalEvent _externalEvent;
        private readonly Dispatcher _dispatcher;

        // Queued actions that have not finished; the window shows "running" while above zero.
        private int _busy;

        // True while this view model ticks rows itself, so Select All recounts once.
        private bool _suspend;

        /// <summary>All rows.</summary>
        public ObservableCollection<DwgItemViewModel> Rows { get; } = new();

        /// <summary>Rows that match the filter, grouped Linked/Imported → view type.</summary>
        public ICollectionView RowsView { get; }

        /// <summary>Shows only rows whose file, view or view type contains every word typed.</summary>
        [ObservableProperty] private string filterText = string.Empty;

        /// <summary>Delete also purges a CAD type once no instance of it is left anywhere.</summary>
        [ObservableProperty] private bool purgeOnDelete;

        /// <summary>Distinct CAD files.</summary>
        [ObservableProperty] private int totalFiles;

        /// <summary>Distinct linked CAD files.</summary>
        [ObservableProperty] private int linkedCount;

        /// <summary>Distinct imported CAD files.</summary>
        [ObservableProperty] private int importedCount;

        /// <summary>CAD instances in the model.</summary>
        [ObservableProperty] private int totalInstances;

        /// <summary>Ticked rows.</summary>
        [ObservableProperty] private int selectedCount;

        /// <summary>Distinct views among the ticked rows that can be opened.</summary>
        [ObservableProperty] private int selectedViewCount;

        /// <summary>Name of the view that was active at the last scan or sync.</summary>
        [ObservableProperty] private string activeViewName = string.Empty;

        /// <summary>Label of both Delete Selected buttons (toolbar and footer), with the ticked-row count.</summary>
        public string DeleteSelectedLabel => $"Delete Selected ({SelectedCount})";

        /// <summary>Footer label of the Open Views button.</summary>
        public string OpenViewsLabel => $"Open Views ({SelectedViewCount} / {MaxOpenViews})";

        /// <summary>Footer hint about the current selection.</summary>
        public string SelectionHint
        {
            get
            {
                if (SelectedCount == 0) return "Tick rows to delete them, or up to 5 views to open.";
                if (SelectedViewCount > MaxOpenViews) return $"Open Views takes up to {MaxOpenViews} views — untick some. Delete works on any number.";
                int modelLevel = Rows.Count(r => r.IsSelected && !r.HasView);
                string rows = $"{SelectedCount} row(s) ticked";
                return modelLevel > 0 ? $"{rows} · {modelLevel} model-level row(s) have no view to open." : rows + ".";
            }
        }

        /// <summary>
        /// Creates the view model and lists the CAD files. Called from the command, so the first scan
        /// runs directly in its Revit API context.
        /// </summary>
        public DwgManagerViewModel(UIDocument uidoc)
        {
            _doc = uidoc.Document;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _externalEvent = ExternalEvent.Create(_handler);

            var view = CollectionViewSource.GetDefaultView(Rows);
            view.Filter = obj => obj is DwgItemViewModel row && PassesFilter(row);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DwgItemViewModel.LinkGroup)));
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DwgItemViewModel.ViewCategory)));
            RowsView = view;
            ApplySort(null, ListSortDirection.Ascending);

            ScanInto(uidoc);
        }

        /// <summary>True when this window was opened for <paramref name="doc"/>.</summary>
        public bool IsFor(Document doc) => doc != null && _doc.IsValidObject && doc.Equals(_doc);

        /// <summary>Releases the external event; called when the window closes.</summary>
        public void Shutdown()
        {
            try { _externalEvent.Dispose(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { /* Revit already released it */ }
        }

        partial void OnFilterTextChanged(string value) => RowsView?.Refresh();

        // ── Sorting ───────────────────────────────────────────────────────────

        /// <summary>
        /// Sorts by <paramref name="memberPath"/> inside the groups; the group order (Linked first, then
        /// view type A–Z) never changes. Null restores the default order (file, then view).
        /// </summary>
        public void ApplySort(string memberPath, ListSortDirection direction)
        {
            using (RowsView.DeferRefresh())
            {
                RowsView.SortDescriptions.Clear();
                RowsView.SortDescriptions.Add(new SortDescription(nameof(DwgItemViewModel.IsLinked), ListSortDirection.Descending));
                RowsView.SortDescriptions.Add(new SortDescription(nameof(DwgItemViewModel.ViewCategory), ListSortDirection.Ascending));

                if (!string.IsNullOrEmpty(memberPath))
                {
                    RowsView.SortDescriptions.Add(new SortDescription(memberPath, direction));
                }
                else
                {
                    RowsView.SortDescriptions.Add(new SortDescription(nameof(DwgItemViewModel.FileName), ListSortDirection.Ascending));
                    RowsView.SortDescriptions.Add(new SortDescription(nameof(DwgItemViewModel.ViewName), ListSortDirection.Ascending));
                }
            }
        }

        // ── Loading ───────────────────────────────────────────────────────────

        /// <summary>Scans the model again; ticks are kept for rows that still exist.</summary>
        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private void Refresh()
        {
            CloseOpenPanels();
            RunInRevit("Scanning CAD links and imports…", app =>
            {
                if (!TryGetUiDocument(app, out var uidoc)) return;
                ScanInto(uidoc);
            });
        }

        private bool CanRefresh() => !IsRunning;

        /// <summary>
        /// Re-reads which instances the active view draws (no full scan). Called when the window is
        /// activated, so Active badges and clickable IDs follow the view the user switched to.
        /// </summary>
        public void SyncActiveView()
        {
            if (_busy > 0 || Rows.Count == 0) return;

            RunInRevit(null, app =>
            {
                if (!TryGetUiDocument(app, out var uidoc, quiet: true)) return;
                var visible = DwgScanService.GetVisibleInstanceIds(uidoc.Document, uidoc.ActiveView);
                string viewName = uidoc.ActiveView?.Name ?? string.Empty;
                OnUi(() => ApplyVisibility(visible, viewName));
            });
        }

        // Runs in a Revit API context: scans, then shows the rows on the UI thread.
        private void ScanInto(UIDocument uidoc)
        {
            var models = DwgScanService.Scan(uidoc.Document, LogFromRevit);
            var visible = DwgScanService.GetVisibleInstanceIds(uidoc.Document, uidoc.ActiveView);
            string viewName = uidoc.ActiveView?.Name ?? string.Empty;
            OnUi(() => ShowRows(models, visible, viewName));
        }

        private void ShowRows(List<DwgItemModel> models, HashSet<ElementId> visible, string viewName)
        {
            var ticked = new HashSet<string>(Rows.Where(r => r.IsSelected).Select(r => r.Key));

            _suspend = true;
            try
            {
                foreach (var row in Rows) row.PropertyChanged -= OnRowPropertyChanged;
                Rows.Clear();
                foreach (var model in models)
                {
                    var row = new DwgItemViewModel(model, this);
                    row.IsSelected = ticked.Contains(row.Key);
                    row.PropertyChanged += OnRowPropertyChanged;
                    Rows.Add(row);
                }
            }
            finally
            {
                _suspend = false;
            }

            ApplyVisibility(visible, viewName);

            TotalFiles = Rows.Select(r => r.TypeId).Distinct().Count();
            LinkedCount = Rows.Where(r => r.IsLinked).Select(r => r.TypeId).Distinct().Count();
            ImportedCount = Rows.Where(r => !r.IsLinked).Select(r => r.TypeId).Distinct().Count();
            TotalInstances = Rows.Sum(r => r.InstanceCount);
            Recount();

            string summary = $"Scan complete — {TotalFiles} CAD file(s), {Rows.Count} file/view row(s), {TotalInstances} instance(s).";
            AddLog(LogLevel.Info, summary);
            SummaryText = summary;
        }

        private void ApplyVisibility(HashSet<ElementId> visible, string viewName)
        {
            ActiveViewName = viewName;
            foreach (var row in Rows) row.ApplyVisibility(visible);
        }

        private bool PassesFilter(DwgItemViewModel row)
        {
            if (string.IsNullOrWhiteSpace(FilterText)) return true;

            string haystack = $"{row.FileName} {row.ViewName} {row.ViewCategory} {row.LinkGroup}";
            return FilterText
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .All(word => haystack.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ── Selection ─────────────────────────────────────────────────────────

        /// <summary>Ticks every row that matches the filter.</summary>
        [RelayCommand]
        private void SelectAll() => SetTicks(true);

        /// <summary>Unticks every row that matches the filter.</summary>
        [RelayCommand]
        private void ClearSelection() => SetTicks(false);

        private void SetTicks(bool on)
        {
            _suspend = true;
            try
            {
                foreach (var row in RowsView.Cast<DwgItemViewModel>()) row.IsSelected = on;
            }
            finally
            {
                _suspend = false;
            }
            Recount();
        }

        private void OnRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_suspend) return;
            if (e.PropertyName == nameof(DwgItemViewModel.IsSelected)) Recount();
        }

        private void Recount()
        {
            var ticked = Rows.Where(r => r.IsSelected).ToList();
            SelectedCount = ticked.Count;
            SelectedViewCount = ticked.Where(r => r.HasView).Select(r => r.OwnerViewId).Distinct().Count();
            RefreshSelectionState();
        }

        private void RefreshSelectionState()
        {
            OnPropertyChanged(nameof(OpenViewsLabel));
            OnPropertyChanged(nameof(DeleteSelectedLabel));
            OnPropertyChanged(nameof(SelectionHint));
            DeleteSelectedCommand.NotifyCanExecuteChanged();
            OpenViewsCommand.NotifyCanExecuteChanged();
            RefreshCommand.NotifyCanExecuteChanged();
        }

        private void CloseOpenPanels()
        {
            foreach (var row in Rows) row.IsConfirmingDelete = false;
        }

        // ── Open views / select ───────────────────────────────────────────────

        /// <summary>Opens the views of the ticked rows (1 to 5 distinct views).</summary>
        [RelayCommand(CanExecute = nameof(CanOpenViews))]
        private void OpenViews()
        {
            var viewIds = Rows.Where(r => r.IsSelected && r.HasView).Select(r => r.OwnerViewId).Distinct().ToList();
            if (viewIds.Count == 0 || viewIds.Count > MaxOpenViews) return;

            RunInRevit($"Opening {viewIds.Count} view(s)…", app =>
            {
                if (!TryGetUiDocument(app, out var uidoc)) return;

                int opened = DwgViewService.OpenViews(uidoc, viewIds, LogFromRevit);
                var visible = DwgScanService.GetVisibleInstanceIds(uidoc.Document, uidoc.ActiveView);
                string viewName = uidoc.ActiveView?.Name ?? string.Empty;
                OnUi(() =>
                {
                    ApplyVisibility(visible, viewName);
                    SummaryText = $"{opened} opened | {viewIds.Count - opened} failed";
                });
            });
        }

        private bool CanOpenViews() => !IsRunning && SelectedViewCount > 0 && SelectedViewCount <= MaxOpenViews;

        /// <summary>Selects one instance and zooms to it (only for instances in the active view).</summary>
        [RelayCommand]
        private void SelectInstance(DwgInstanceIdViewModel chip)
        {
            if (chip == null) return;
            if (!chip.IsClickable)
            {
                SummaryText = "That instance is not in the active view — open its view first.";
                return;
            }

            var id = chip.Id;
            RunInRevit($"Selecting #{id.Value}…", app =>
            {
                if (!TryGetUiDocument(app, out var uidoc)) return;
                DwgViewService.SelectAndZoom(uidoc, id, LogFromRevit);
            });
        }

        // ── Delete ────────────────────────────────────────────────────────────

        /// <summary>Confirms, then deletes every ticked row with the toolbar's purge setting.</summary>
        [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
        private void DeleteSelected()
        {
            var rows = Rows.Where(r => r.IsSelected).ToList();
            if (rows.Count == 0) return;

            int instances = rows.Sum(r => r.InstanceCount);
            var sb = new StringBuilder();
            sb.AppendLine($"Delete {instances} CAD instance(s) in {rows.Count} row(s)?\n");
            foreach (var row in rows.Take(12))
                sb.AppendLine($"  • {row.FileName}  —  {row.ViewName}  ({row.InstanceCount})");
            if (rows.Count > 12) sb.AppendLine($"  … and {rows.Count - 12} more");
            sb.AppendLine();
            sb.AppendLine(PurgeOnDelete
                ? "Purge is ON: a CAD type with no instances left is removed from the project."
                : "Purge is OFF: CAD types stay in the project.");
            sb.AppendLine("You can undo this with Ctrl+Z in Revit.");

            if (MessageBox.Show(sb.ToString(), ToolCatalog.DwgManager.Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            Delete(rows, PurgeOnDelete);
        }

        private bool CanDeleteSelected() => !IsRunning && SelectedCount > 0;

        /// <summary>Deletes one row after its inline confirmation, with the row's own purge choice.</summary>
        internal void DeleteRow(DwgItemViewModel row)
        {
            if (row == null) return;
            if (IsRunning)
            {
                SummaryText = "Wait for the current operation to finish.";
                return;
            }

            row.IsConfirmingDelete = false;
            Delete(new List<DwgItemViewModel> { row }, row.PurgeOnConfirm);
        }

        private void Delete(List<DwgItemViewModel> rows, bool purge)
        {
            var requests = rows
                .Select(r => new DwgDeleteRequest(r.TypeId, r.FileName, r.ViewName, r.InstanceIds.ToList(), purge))
                .ToList();
            int instances = requests.Sum(r => r.InstanceIds.Count);

            AddLog(LogLevel.Warning, $"Deleting {instances} instance(s) in {requests.Count} row(s){(purge ? " + purge unused types" : string.Empty)}…");

            RunInRevit($"Deleting {instances} CAD instance(s)…", app =>
            {
                if (!TryGetUiDocument(app, out var uidoc)) return;

                var result = new DwgDeleteService(LogFromRevit).Delete(uidoc.Document, requests);
                OnUi(() =>
                {
                    var level = result.RolledBack || result.Failed > 0 ? LogLevel.Error
                        : result.Skipped > 0 ? LogLevel.Warning
                        : LogLevel.Success;
                    AddLog(level, result.Summary);
                    SummaryText = result.Summary;
                });

                if (result.RolledBack)
                {
                    MessageBox.Show("Revit rolled the delete back; nothing was changed. See the log for details.",
                        ToolCatalog.DwgManager.Title, MessageBoxButton.OK, MessageBoxImage.Error);
                }

                ScanInto(uidoc);
                OnUi(() => SummaryText = result.Summary);
            });
        }

        // ── Revit plumbing ────────────────────────────────────────────────────

        private sealed class Job
        {
            public bool Cancelled;
        }

        // Queues work for the Revit API context. A failure is logged and reported; the run counter always
        // goes back down so the window never stays stuck on "running". summary == null → quiet (no
        // progress bar), used for the active-view sync.
        private void RunInRevit(string summary, Action<UIApplication> body)
        {
            var job = new Job();
            bool quiet = summary == null;
            if (!quiet) SetBusy(+1, summary);

            _handler.Queue(app =>
            {
                if (job.Cancelled) return;

                // Esc / Cancel with a Yes-No confirm; the service rolls its transaction back on cancel.
                using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.DwgManager, app);
                try
                {
                    body(app);
                }
                catch (OperationCanceledException)
                {
                    OnUi(() => AddLog(LogLevel.Warning, "Cancelled by user. Nothing was changed."));
                }
                catch (Exception ex)
                {
                    OnUi(() => AddLog(LogLevel.Error, $"Stopped, changes were rolled back: {ex.Message}"));
                    ToolGuard.Report(GetType(), ex);
                }
                finally
                {
                    if (!quiet) OnUi(() => SetBusy(-1, null));
                }
            });

            if (_externalEvent.Raise() == ExternalEventRequest.Denied)
            {
                job.Cancelled = true;
                if (quiet) return;
                SetBusy(-1, null);
                SummaryText = "Revit is busy. Close any open dialog and try again.";
                AddLog(LogLevel.Warning, SummaryText);
            }
        }

        private void SetBusy(int delta, string summary)
        {
            _busy = Math.Max(0, _busy + delta);
            IsRunning = _busy > 0;
            if (summary != null) SummaryText = summary;
            RefreshSelectionState();
        }

        private bool TryGetUiDocument(UIApplication app, out UIDocument uidoc, bool quiet = false)
        {
            uidoc = app.ActiveUIDocument;
            if (uidoc != null && _doc.IsValidObject && uidoc.Document.Equals(_doc)) return true;

            if (!quiet)
                OnUi(() => AddLog(LogLevel.Error,
                    "The active document changed. Switch back to the document this window was opened for."));
            uidoc = null;
            return false;
        }

        private void LogFromRevit(LogEntry entry) => OnUi(() => AddLog(entry.Level, entry.Message));

        private void OnUi(Action action)
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.Invoke(action);
        }
    }
}
