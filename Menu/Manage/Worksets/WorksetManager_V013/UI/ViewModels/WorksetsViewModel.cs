// ==============================================
// File: WorksetsViewModel.cs
// Layer: UI/ViewModels
// Changes vs V011:
//   FIX  OnPatternChanged was never wired up â€” AssembleWorksetNameSilent /
//        UpdateProposedNamesSilent existed but were unreachable, so editing
//        the Pattern textbox did nothing until the next full RefreshData().
//        Restored the hook (present in the WSFL fork this tool split from,
//        dropped somewhere along this fork's own history): validates the
//        pattern and live-updates proposed names on every keystroke.
// ==============================================

using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.WorksetManager.V013.Core.Models;
using Revit26_Plugin_Revised.WorksetManager.V013.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.WorksetManager.V013.UI.ViewModels
{
    public partial class WorksetsViewModel : ObservableObject
    {
        private readonly UIDocument _uidoc;
        private readonly WorksetService _service;
        private readonly Document _doc;
        private readonly WorksetActionHandler _handler = new();
        private readonly ExternalEvent _externalEvent;

        // Captured on the UI thread at construction time. Do NOT use
        // System.Windows.Application.Current.Dispatcher here â€” Revit doesn't
        // guarantee a System.Windows.Application instance exists in-process,
        // so System.Windows.Application.Current can be null and NullReferenceException.
        private readonly Dispatcher _dispatcher;

        public ObservableCollection<WorksetItem> Items { get; } = new();
        public ObservableCollection<LogEntry>    Log   { get; } = new();

        // â”€â”€ Filtered views â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private ICollectionView _grid1AssignedItems;
        public ICollectionView Grid1AssignedItems
        {
            get => _grid1AssignedItems;
            private set => SetProperty(ref _grid1AssignedItems, value);
        }

        private ICollectionView _grid2ActionableItems;
        public ICollectionView Grid2ActionableItems
        {
            get => _grid2ActionableItems;
            private set => SetProperty(ref _grid2ActionableItems, value);
        }

        private ICollectionView _grid3NoInstanceItems;
        public ICollectionView Grid3NoInstanceItems
        {
            get => _grid3NoInstanceItems;
            private set => SetProperty(ref _grid3NoInstanceItems, value);
        }

        // â”€â”€ Observable properties â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [ObservableProperty] private string pattern              = "+Link({name})";
        [ObservableProperty] private string patternErrorMessage  = string.Empty;
        [ObservableProperty] private bool   hasPatternError;
        [ObservableProperty] private bool   isCreateEnabled      = true;
        [ObservableProperty] private bool   isResyncEnabled      = true;
        [ObservableProperty] private int    grid1Count;
        [ObservableProperty] private int    grid2Count;
        [ObservableProperty] private int    grid3Count;
        [ObservableProperty] private int    totalLinksCount;
        [ObservableProperty] private int    totalSelectedCount;
        [ObservableProperty] private int    grid2SelectedCount;
        [ObservableProperty] private int    grid3SelectedCount;
        [ObservableProperty] private int    grid2ResyncableCount;
        [ObservableProperty] private int    grid3ResyncableCount;
        [ObservableProperty] private string searchText1          = string.Empty;
        [ObservableProperty] private string searchText2          = string.Empty;
        [ObservableProperty] private string searchText3          = string.Empty;
        [ObservableProperty] private bool   isLogExpanded        = false;

        public string LogChevron => IsLogExpanded ? "▾" : "▸";

        // â”€â”€ Commands â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public IRelayCommand CreateGrid2Command  { get; }
        public IRelayCommand CreateGrid3Command  { get; }
        public IRelayCommand ResyncCommand        { get; }
        public IRelayCommand ResyncGrid2Command   { get; }
        public IRelayCommand ResyncGrid3Command   { get; }
        public IRelayCommand CloseCommand         { get; }
        public IRelayCommand SelectAllCommand     { get; }
        public IRelayCommand SelectNoneCommand    { get; }
        public IRelayCommand SelectAllG3Command   { get; }
        public IRelayCommand SelectNoneG3Command  { get; }
        public IRelayCommand CopyLogCommand       { get; }
        public IRelayCommand ClearLogCommand      { get; }
        public IRelayCommand ToggleLogCommand     { get; }

        public event Action RequestClose;

        // â”€â”€ Constructor â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public WorksetsViewModel(ExternalCommandData commandData)
        {
            _uidoc      = commandData.Application.ActiveUIDocument;
            _doc        = _uidoc.Document;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _service    = new WorksetService(AddLog);
            _externalEvent = ExternalEvent.Create(_handler);

            RecreateFilteredViews();

            CreateGrid2Command   = new RelayCommand(ExecuteCreateGrid2,    CanCreateGrid2);
            CreateGrid3Command   = new RelayCommand(ExecuteCreateGrid3,    CanCreateGrid3);
            ResyncCommand        = new RelayCommand(ExecuteResync,         CanResync);
            ResyncGrid2Command   = new RelayCommand(
                () => ExecuteResyncFor(WorksetGridCategory.NeedsWorkset, "Grid 2"),
                () => CanResyncGrid(WorksetGridCategory.NeedsWorkset));
            ResyncGrid3Command   = new RelayCommand(
                () => ExecuteResyncFor(WorksetGridCategory.NoInstances, "Grid 3"),
                () => CanResyncGrid(WorksetGridCategory.NoInstances));
            CloseCommand         = new RelayCommand(() => RequestClose?.Invoke());
            SelectAllCommand     = new RelayCommand(() => SetGrid2Selection(true));
            SelectNoneCommand    = new RelayCommand(() => SetGrid2Selection(false));
            SelectAllG3Command   = new RelayCommand(() => SetGrid3Selection(true));
            SelectNoneG3Command  = new RelayCommand(() => SetGrid3Selection(false));
            CopyLogCommand       = new RelayCommand(ExecuteCopyLog);
            ClearLogCommand      = new RelayCommand(() => Log.Clear());
            ToggleLogCommand     = new RelayCommand(() =>
            {
                IsLogExpanded = !IsLogExpanded;
                OnPropertyChanged(nameof(LogChevron));
            });

            Items.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (WorksetItem item in e.NewItems)
                        AttachItemHandler(item);
                RefreshCommandStates();
            };

            LoadData();
            ValidatePattern();
        }

        private void AttachItemHandler(WorksetItem item)
        {
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(WorksetItem.IsSelected))
                    RefreshCommandStates();
            };
        }

        // â”€â”€ Filtered view management â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void RecreateFilteredViews()
        {
            Grid1AssignedItems   = CreateFilteredView(i => i.GridCategory == WorksetGridCategory.AlreadyAssigned,  1);
            Grid2ActionableItems = CreateFilteredView(i => i.GridCategory == WorksetGridCategory.NeedsWorkset,     2);
            Grid3NoInstanceItems = CreateFilteredView(i => i.GridCategory == WorksetGridCategory.NoInstances,      3);
        }

        private ICollectionView CreateFilteredView(Predicate<WorksetItem> categoryFilter, int grid)
        {
            var cvs = new CollectionViewSource { Source = Items };
            cvs.Filter += (s, e) =>
            {
                var item = (WorksetItem)e.Item;
                if (!categoryFilter(item)) { e.Accepted = false; return; }
                string q = grid == 1 ? SearchText1 : grid == 2 ? SearchText2 : SearchText3;
                e.Accepted = string.IsNullOrWhiteSpace(q) ||
                             item.LinkName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            };
            return cvs.View;
        }

        partial void OnSearchText1Changed(string value) => Grid1AssignedItems?.Refresh();
        partial void OnSearchText2Changed(string value) => Grid2ActionableItems?.Refresh();
        partial void OnSearchText3Changed(string value) => Grid3NoInstanceItems?.Refresh();

        // â”€â”€ Data loading â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void LoadData()
        {
            var linkNames = _service.GetLinkedFileNames(_doc);
            int serial    = 1;

            foreach (string linkName in linkNames)
            {
                var item = new WorksetItem
                {
                    SerialNumber  = serial++,
                    LinkName      = linkName,
                    HasInstances  = _service.HasInstances(_doc, linkName),
                    InstanceCount = _service.GetInstanceCount(_doc, linkName)
                };

                string proposedName = AssembleWorksetName(linkName);
                string existingWs   = _service.CheckExistingWorkset(_doc, proposedName);
                item.ExistingWorksetName = existingWs;
                item.IsExistingWorkset   = !string.IsNullOrEmpty(existingWs);

                if (!item.HasInstances)
                {
                    item.GridCategory        = WorksetGridCategory.NoInstances;
                    item.ProposedWorksetName = proposedName;
                    item.IsSelected          = false;
                }
                else
                {
                    item.CurrentWorksetName = _service.GetCurrentWorksetName(_doc, linkName);
                    item.IsMixedWorkset     = item.CurrentWorksetName == "MIXED";

                    if (item.IsExistingWorkset &&
                        _service.IsFullyAssigned(_doc, linkName, existingWs))
                    {
                        item.GridCategory          = WorksetGridCategory.AlreadyAssigned;
                        item.ProposedWorksetName   = existingWs;
                        item.IsSelected            = false;
                        item.IsExactMatchAssigned  = true;
                        item.ProposedWorksetTooltip = $"All instances already assigned to '{existingWs}'";
                    }
                    else
                    {
                        item.GridCategory        = WorksetGridCategory.NeedsWorkset;
                        item.ProposedWorksetName = proposedName;
                        item.IsSelected          = true;
                        if (item.IsExistingWorkset)
                            item.ProposedWorksetTooltip =
                                $"Workset '{existingWs}' exists but assignment is incomplete";
                    }
                }

                Items.Add(item);
            }

            _service.ResolveDuplicates(Items);
            RefreshCounts();
            RefreshCommandStates();

            AddLog(new LogEntry(LogLevel.Info,
                $"Loaded {Items.Count} linked file(s): " +
                $"{Grid1Count} assigned, {Grid2Count} actionable, {Grid3Count} no-instance"));
        }

        private void RefreshData()
        {
            _dispatcher.Invoke(() =>
            {
                AddLog(new LogEntry(LogLevel.Info, "Refreshing UI data..."));
                Items.Clear();
                LoadData();
                RecreateFilteredViews();
                AddLog(new LogEntry(LogLevel.Info, "UI refresh complete."));
            });
        }

        private void ValidatePattern()
        {
            if (string.IsNullOrWhiteSpace(Pattern) || !Pattern.Contains("{name}"))
            {
                HasPatternError      = true;
                PatternErrorMessage  = "Pattern must contain {name}";
                IsCreateEnabled      = false;
            }
            else
            {
                HasPatternError      = false;
                PatternErrorMessage  = string.Empty;
                IsCreateEnabled      = true;
            }
        }

        partial void OnPatternChanged(string value)
        {
            ValidatePattern();
            UpdateProposedNamesSilent();
            RefreshCommandStates();
        }

        private string AssembleWorksetName(string linkName)
        {
            string cleaned   = _service.CleanName(linkName);
            string assembled = Pattern.Replace("{name}", cleaned);
            return _service.CleanName(assembled);
        }

        private string AssembleWorksetNameSilent(string linkName)
        {
            string cleaned   = _service.CleanNameSilent(linkName);
            string assembled = Pattern.Replace("{name}", cleaned);
            return _service.CleanNameSilent(assembled);
        }

        /// <summary>Updates proposed names without writing to the log (live typing).</summary>
        private void UpdateProposedNamesSilent()
        {
            foreach (var item in Items.Where(i =>
                i.GridCategory != WorksetGridCategory.AlreadyAssigned))
            {
                item.ProposedWorksetName = AssembleWorksetNameSilent(item.LinkName);
            }
        }

        // â”€â”€ Selection helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void SetGrid2Selection(bool selected)
        {
            foreach (var item in Items.Where(i =>
                i.GridCategory == WorksetGridCategory.NeedsWorkset))
                item.IsSelected = selected;
            RefreshCommandStates();
        }

        private void SetGrid3Selection(bool selected)
        {
            foreach (var item in Items.Where(i =>
                i.GridCategory == WorksetGridCategory.NoInstances))
                item.IsSelected = selected;
            RefreshCommandStates();
        }

        // â”€â”€ Command guards â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private bool CanResync() =>
            IsResyncEnabled &&
            Items.Any(i => i.IsSelected && i.IsExistingWorkset &&
                           (i.GridCategory == WorksetGridCategory.NeedsWorkset ||
                            i.GridCategory == WorksetGridCategory.NoInstances));

        // â”€â”€ Command executions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€


        private void RunInRevit(
            System.Collections.Generic.List<(string, string, bool)> toProcess,
            string doneMessage, string errorPrefix)
        {
            IsCreateEnabled = false;
            IsResyncEnabled = false;
            RefreshCommandStates();

            _handler.Queue(_ =>
            {
                try
                {
                    _service.CreateAndAssign(_doc, toProcess, this);
                    AddLog(new LogEntry(LogLevel.Info, doneMessage));
                }
                catch (Exception ex)
                {
                    AddLog(new LogEntry(LogLevel.Error, $"{errorPrefix}: {ex.Message}"));
                }
                finally
                {
                    IsCreateEnabled = !HasPatternError;
                    IsResyncEnabled = true;
                    RefreshData();
                    RefreshCommandStates();
                }
            });

            ExternalEventRequest request = _externalEvent.Raise();
            if (request != ExternalEventRequest.Accepted)
            {
                AddLog(new LogEntry(LogLevel.Error, $"Revit rejected the request ({request}). Try again."));
                IsCreateEnabled = !HasPatternError;
                IsResyncEnabled = true;
                RefreshCommandStates();
            }
        }

        private void ExecuteResync() =>
            ResyncExisting(i => i.GridCategory == WorksetGridCategory.NeedsWorkset ||
                                i.GridCategory == WorksetGridCategory.NoInstances, "Grid 2 & 3");

        private bool CanResyncGrid(WorksetGridCategory category) =>
            IsResyncEnabled && Items.Any(i => i.IsSelected && i.IsExistingWorkset && i.GridCategory == category);

        private void ExecuteResyncFor(WorksetGridCategory category, string label) =>
            ResyncExisting(i => i.GridCategory == category, label);

        // Resync never creates worksets — it only reassigns links to worksets that already exist.
        private void ResyncExisting(Func<WorksetItem, bool> scope, string label)
        {
            var selected = Items.Where(i => i.IsSelected && scope(i)).ToList();
            var toProcess = selected
                .Where(i => i.IsExistingWorkset)
                .Select(i => (i.ProposedWorksetName, i.LinkName, false))
                .ToList();

            foreach (var skipped in selected.Where(i => !i.IsExistingWorkset))
                AddLog(new LogEntry(LogLevel.Warning,
                    $"Skipped '{skipped.LinkName}' — workset '{skipped.ProposedWorksetName}' does not exist (use Create)."));

            if (!toProcess.Any())
            {
                AddLog(new LogEntry(LogLevel.Warning, $"Nothing to resync in {label} — no checked link has an existing workset."));
                return;
            }

            AddLog(new LogEntry(LogLevel.Info, $"Resyncing {toProcess.Count} link(s) from {label}..."));
            RunInRevit(toProcess, $"Resync complete for {label}.", "Resync error");
        }

        private bool CanCreateGrid2() =>
            IsCreateEnabled && !HasPatternError &&
            Items.Any(i => i.IsSelected && i.GridCategory == WorksetGridCategory.NeedsWorkset);

        private bool CanCreateGrid3() =>
            IsCreateEnabled && !HasPatternError &&
            Items.Any(i => i.IsSelected && i.GridCategory == WorksetGridCategory.NoInstances);

        private void ExecuteCreateGrid2() => ExecuteCreateFor(WorksetGridCategory.NeedsWorkset, "Grid 2");
        private void ExecuteCreateGrid3() => ExecuteCreateFor(WorksetGridCategory.NoInstances,  "Grid 3");

        private void ExecuteCreateFor(WorksetGridCategory category, string label)
        {
            var toProcess = Items
                .Where(i => i.IsSelected && i.GridCategory == category)
                .Select(i => (i.ProposedWorksetName, i.LinkName, CreateNew: !i.IsExistingWorkset))
                .ToList();

            if (!toProcess.Any())
            {
                AddLog(new LogEntry(LogLevel.Warning, $"No {label} links selected for creation."));
                return;
            }

            RunInRevit(toProcess,
                $"Done — created and assigned {toProcess.Count} workset(s) from {label}.", "Error");
        }

        private void ExecuteCopyLog()
        {
            if (!Log.Any()) return;

            // Log is stored newest-first; reverse so oldest is at top of clipboard text
            var sb = new StringBuilder();
            foreach (var entry in Log.Reverse())
                sb.AppendLine(entry.ToString());

            try
            {
                System.Windows.Clipboard.SetText(sb.ToString());
                AddLog(new LogEntry(LogLevel.Info, "Log copied to System.Windows.Clipboard."));
            }
            catch (Exception ex)
            {
                AddLog(new LogEntry(LogLevel.Error, $"Copy failed: {ex.Message}"));
            }
        }

        /// <summary>Copies only the given entries (used by the "Copy selected" button â€” selection is read from the ListBox in code-behind).</summary>
        public void CopySelectedLog(System.Collections.IList selectedEntries)
        {
            if (selectedEntries == null || selectedEntries.Count == 0)
            {
                AddLog(new LogEntry(LogLevel.Warning, "No log rows selected."));
                return;
            }

            var sb = new StringBuilder();
            foreach (LogEntry entry in selectedEntries.Cast<LogEntry>().Reverse())
                sb.AppendLine(entry.ToString());

            try
            {
                System.Windows.Clipboard.SetText(sb.ToString());
                AddLog(new LogEntry(LogLevel.Info, $"Copied {selectedEntries.Count} selected log row(s)."));
            }
            catch (Exception ex)
            {
                AddLog(new LogEntry(LogLevel.Error, $"Copy failed: {ex.Message}"));
            }
        }

        // â”€â”€ UI helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void RefreshCounts()
        {
            Grid1Count = Items.Count(i => i.GridCategory == WorksetGridCategory.AlreadyAssigned);
            Grid2Count = Items.Count(i => i.GridCategory == WorksetGridCategory.NeedsWorkset);
            Grid3Count = Items.Count(i => i.GridCategory == WorksetGridCategory.NoInstances);
            TotalLinksCount    = Grid1Count + Grid2Count + Grid3Count;
            Grid2SelectedCount = Items.Count(i => i.IsSelected && i.GridCategory == WorksetGridCategory.NeedsWorkset);
            Grid3SelectedCount = Items.Count(i => i.IsSelected && i.GridCategory == WorksetGridCategory.NoInstances);
            TotalSelectedCount = Grid2SelectedCount + Grid3SelectedCount;
            Grid2ResyncableCount = Items.Count(i => i.IsSelected && i.IsExistingWorkset && i.GridCategory == WorksetGridCategory.NeedsWorkset);
            Grid3ResyncableCount = Items.Count(i => i.IsSelected && i.IsExistingWorkset && i.GridCategory == WorksetGridCategory.NoInstances);
        }

        private void RefreshCommandStates()
        {
            RefreshCounts();
            CreateGrid2Command.NotifyCanExecuteChanged();
            CreateGrid3Command.NotifyCanExecuteChanged();
            ResyncCommand.NotifyCanExecuteChanged();
            ResyncGrid2Command.NotifyCanExecuteChanged();
            ResyncGrid3Command.NotifyCanExecuteChanged();
        }

        public void AddLog(LogEntry entry)
        {
            _dispatcher.Invoke(() =>
            {
                Log.Insert(0, entry);
            }, DispatcherPriority.Background);
        }

        public void KeepUIResponsive()
        {
            _dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        }
    }
}

