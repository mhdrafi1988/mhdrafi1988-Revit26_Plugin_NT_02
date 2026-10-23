using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.PlanRegionManager.V001.Core.Models;
using Revit26_Plugin_Revised.PlanRegionManager.V001.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.PlanRegionManager.V001.UI.ViewModels
{
    /// <summary>
    /// View model of the Plan Region Manager window: lists plan regions grouped Visible / Hidden for the
    /// view the tool was opened in (or every plan view) and runs hide / unhide / delete / select through
    /// an external event.
    /// </summary>
    public partial class PlanRegionManagerViewModel : ObservableObject
    {
        private readonly Document _doc;
        private readonly ElementId _viewId;
        private readonly PlanRegionHandler _handler = new();
        private readonly ExternalEvent _externalEvent;
        private readonly Dispatcher _dispatcher;

        /// <summary>Plan regions in scope.</summary>
        public ObservableCollection<PlanRegionRow> Rows { get; } = new();

        /// <summary>Run log, newest first.</summary>
        public ObservableCollection<LogEntry> Log { get; } = new();

        private ICollectionView _rowsView;

        /// <summary>Filtered, grouped view of <see cref="Rows"/>.</summary>
        public ICollectionView RowsView
        {
            get => _rowsView;
            private set => SetProperty(ref _rowsView, value);
        }

        /// <summary>Name of the view the tool was opened in.</summary>
        public string ViewName { get; }

        /// <summary>Filter on view name, level or element id.</summary>
        [ObservableProperty] private string filterText = string.Empty;

        /// <summary>Regions in scope.</summary>
        [ObservableProperty] private int totalCount;

        /// <summary>Regions shown in their view.</summary>
        [ObservableProperty] private int visibleCount;

        /// <summary>Regions hidden in their view.</summary>
        [ObservableProperty] private int hiddenCount;

        /// <summary>Rows ticked.</summary>
        [ObservableProperty] private int selectedCount;

        /// <summary>False (default): only the view the tool was opened in. True: every plan view.</summary>
        [ObservableProperty] private bool allViews;

        /// <summary>True while an action is running.</summary>
        [ObservableProperty] private bool isRunning;

        /// <summary>Log panel expanded.</summary>
        [ObservableProperty] private bool isLogExpanded = true;

        /// <summary>Creates the view model and loads the plan regions of <paramref name="view"/>.</summary>
        public PlanRegionManagerViewModel(ExternalCommandData commandData, View view)
        {
            _doc = commandData.Application.ActiveUIDocument.Document;
            _viewId = view.Id;
            ViewName = view.Name;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _externalEvent = ExternalEvent.Create(_handler);
            Refresh();
        }

        /// <summary>True when "this view" is chosen; bound to the first radio button.</summary>
        public bool ThisViewOnly
        {
            get => !AllViews;
            set { if (value) AllViews = false; }
        }

        partial void OnAllViewsChanged(bool value)
        {
            OnPropertyChanged(nameof(ThisViewOnly));
            Refresh();
        }

        partial void OnFilterTextChanged(string value) => RowsView?.Refresh();

        partial void OnIsRunningChanged(bool value) => NotifyCommands();

        /// <summary>Reloads the plan regions from the document.</summary>
        [RelayCommand]
        private void Refresh()
        {
            if (!_doc.IsValidObject)
            {
                AddLog(new LogEntry(LogLevel.Error, "The document was closed. Close this window."));
                return;
            }

            var rows = new PlanRegionService(AddLog).Load(_doc, AllViews ? null : _viewId);

            foreach (var r in Rows) r.PropertyChanged -= OnRowPropertyChanged;
            Rows.Clear();
            foreach (var r in rows)
            {
                r.PropertyChanged += OnRowPropertyChanged;
                Rows.Add(r);
            }

            var cv = CollectionViewSource.GetDefaultView(Rows);
            cv.Filter = obj => obj is PlanRegionRow row
                && (string.IsNullOrWhiteSpace(FilterText)
                    || row.ViewName.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) >= 0
                    || row.LevelName.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) >= 0
                    || row.Id.Value.ToString().Contains(FilterText.Trim()));
            cv.GroupDescriptions.Clear();
            cv.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PlanRegionRow.GroupName)));
            cv.SortDescriptions.Clear();
            cv.SortDescriptions.Add(new SortDescription(nameof(PlanRegionRow.GroupOrder), ListSortDirection.Ascending));
            cv.SortDescriptions.Add(new SortDescription(nameof(PlanRegionRow.ViewName), ListSortDirection.Ascending));
            RowsView = cv;

            UpdateCounts();
            NotifyCommands();
        }

        /// <summary>Ticks every region that can be changed.</summary>
        [RelayCommand]
        private void SelectAll()
        {
            foreach (var r in Rows.Where(r => !r.IsBlocked)) r.IsSelected = true;
        }

        /// <summary>Ticks only the hidden regions.</summary>
        [RelayCommand]
        private void SelectHidden()
        {
            foreach (var r in Rows) r.IsSelected = r.IsHidden && !r.IsBlocked;
        }

        /// <summary>Unticks every row.</summary>
        [RelayCommand]
        private void ClearSelection()
        {
            foreach (var r in Rows) r.IsSelected = false;
        }

        /// <summary>Hides the ticked regions in their views.</summary>
        [RelayCommand(CanExecute = nameof(HasSelection))]
        private void Hide()
        {
            var selected = Ticked();
            RunAction("Hiding", selected, (doc, svc) => svc.Hide(doc, selected));
        }

        /// <summary>Unhides the ticked regions.</summary>
        [RelayCommand(CanExecute = nameof(HasSelection))]
        private void Unhide()
        {
            var selected = Ticked();
            RunAction("Unhiding", selected, (doc, svc) => svc.Unhide(doc, selected));
        }

        /// <summary>Confirms and deletes the ticked regions.</summary>
        [RelayCommand(CanExecute = nameof(HasSelection))]
        private void Delete()
        {
            var selected = Ticked();
            if (selected.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine($"Delete {selected.Count} plan region(s)?\n");
            foreach (var r in selected.Take(20))
                sb.AppendLine($"  • {r.Id}  in '{r.ViewName}'  ({r.Status})");
            if (selected.Count > 20) sb.AppendLine($"  … and {selected.Count - 20} more");
            sb.AppendLine("\nYou can undo this with Ctrl+Z in Revit.");

            if (MessageBox.Show(sb.ToString(), ToolCatalog.PlanRegionManager.Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            RunAction("Deleting", selected, (doc, svc) => svc.Delete(doc, selected));
        }

        /// <summary>
        /// Confirms and deletes every hidden region in the current scope, whatever is ticked.
        /// Regions owned by another user are skipped.
        /// </summary>
        [RelayCommand(CanExecute = nameof(HasHidden))]
        private void DeleteAllHidden()
        {
            var hidden = Rows.Where(r => r.IsHidden && !r.IsBlocked).ToList();
            if (hidden.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine($"Delete ALL {hidden.Count} hidden plan region(s) in {(AllViews ? "every plan view" : $"'{ViewName}'")}?\n");
            foreach (var r in hidden.Take(20))
                sb.AppendLine($"  • {r.Id}  in '{r.ViewName}'  ({r.Status})");
            if (hidden.Count > 20) sb.AppendLine($"  … and {hidden.Count - 20} more");
            sb.AppendLine("\nYou can undo this with Ctrl+Z in Revit.");

            if (MessageBox.Show(sb.ToString(), ToolCatalog.PlanRegionManager.Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            RunAction("Deleting hidden", hidden, (doc, svc) => svc.Delete(doc, hidden));
        }

        private bool HasHidden() => !IsRunning && Rows.Any(r => r.IsHidden && !r.IsBlocked);

        /// <summary>Opens the view of the first ticked region and selects the ticked regions that belong to it.</summary>
        [RelayCommand(CanExecute = nameof(HasSelection))]
        private void ShowInRevit()
        {
            var selected = Ticked();
            if (selected.Count == 0) return;
            var viewId = selected[0].ViewId;
            var ids = selected.Where(r => r.ViewId == viewId).Select(r => r.Id).ToList();
            if (selected.Any(r => r.ViewId != viewId))
                AddLog(new LogEntry(LogLevel.Info, "Regions in several views are ticked; showing the first view's regions only."));

            IsRunning = true;
            _handler.Queue(app =>
            {
                try
                {
                    var uidoc = app.ActiveUIDocument;
                    if (uidoc == null || !uidoc.Document.Equals(_doc))
                    {
                        AddLog(new LogEntry(LogLevel.Error, "The active document changed. Switch back to the document this window was opened for."));
                        return;
                    }
                    if (_doc.GetElement(viewId) is View view && uidoc.ActiveView.Id != viewId)
                        uidoc.RequestViewChange(view);
                    var existing = ids.Where(i => _doc.GetElement(i) != null).ToList();
                    uidoc.Selection.SetElementIds(existing);
                    AddLog(new LogEntry(LogLevel.Success, $"Selected {existing.Count} region(s) in Revit."));
                }
                catch (Exception ex)
                {
                    AddLog(new LogEntry(LogLevel.Error, $"Could not show the regions: {ex.Message}"));
                    ToolGuard.Report(GetType(), ex);
                }
                finally
                {
                    _dispatcher.Invoke(() => IsRunning = false);
                }
            });
            _externalEvent.Raise();
        }

        private bool HasSelection() => !IsRunning && Rows.Any(r => r.IsSelected);

        private List<PlanRegionRow> Ticked() => Rows.Where(r => r.IsSelected).ToList();

        private void RunAction(string verb, List<PlanRegionRow> selected, Func<Document, PlanRegionService, RegionActionResult> action)
        {
            if (selected.Count == 0) return;
            IsRunning = true;
            _handler.Queue(app =>
            {
                try
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null || !doc.Equals(_doc))
                    {
                        AddLog(new LogEntry(LogLevel.Error,
                            "The active document changed. Switch back to the document this window was opened for."));
                        return;
                    }
                    if (doc.IsReadOnly)
                    {
                        AddLog(new LogEntry(LogLevel.Error, "The document is read-only; nothing was changed."));
                        return;
                    }

                    AddLog(new LogEntry(LogLevel.Info, $"{verb} {selected.Count} plan region(s)…"));
                    var result = action(doc, new PlanRegionService(AddLog));
                    AddLog(new LogEntry(result.Done > 0 ? LogLevel.Success : LogLevel.Warning, "— " + result));
                }
                catch (Exception ex)
                {
                    AddLog(new LogEntry(LogLevel.Error, $"Run aborted, nothing was changed: {ex.Message}"));
                    ToolGuard.Report(GetType(), ex);
                }
                finally
                {
                    _dispatcher.Invoke(() =>
                    {
                        IsRunning = false;
                        Refresh();
                    });
                }
            });
            _externalEvent.Raise();
        }

        /// <summary>Copies the log to the clipboard.</summary>
        [RelayCommand]
        private void CopyLog()
        {
            if (Log.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var e in Log.Reverse()) sb.AppendLine(e.ToString());
            try { Clipboard.SetText(sb.ToString()); }
            catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy */ }
        }

        private void OnRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PlanRegionRow.IsSelected)) return;
            UpdateCounts();
            NotifyCommands();
        }

        private void UpdateCounts()
        {
            TotalCount = Rows.Count;
            VisibleCount = Rows.Count(r => !r.IsHidden);
            HiddenCount = Rows.Count(r => r.IsHidden);
            SelectedCount = Rows.Count(r => r.IsSelected);
        }

        private void NotifyCommands()
        {
            HideCommand.NotifyCanExecuteChanged();
            UnhideCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
            DeleteAllHiddenCommand.NotifyCanExecuteChanged();
            ShowInRevitCommand.NotifyCanExecuteChanged();
        }

        private void AddLog(LogEntry entry)
            => _dispatcher.Invoke(() => Log.Insert(0, entry), DispatcherPriority.Background);
    }
}
