using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revit26_Plugin_Revised.ParaManager.V005.Models;
using Revit26_Plugin_Revised.ParaManager.V005.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.ParaManager.V005.ViewModels
{
    public partial class ParaManagerViewModel : ObservableObject
    {
        private const string ToolFolderName = "ParaManager";

        private readonly ParaManagerExternalEventHandler _eventHandler;
        private readonly ExternalEvent _externalEvent;
        private readonly ParaManagerSettings _settings;

        // â”€â”€ Shared Parameter File â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty]
        private string _sharedParameterFilePath = string.Empty;

        private List<SharedParameterInfo> _allParameters = new();

        // â”€â”€ Categories â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public ObservableCollection<CategoryInfo> AvailableCategories { get; } = new();
        public ObservableCollection<CategoryInfo> SelectedCategories { get; } = new();

        [ObservableProperty]
        private bool _isCategoryPopupOpen;

        // â”€â”€ Parameter popover â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public ObservableCollection<ParameterGroupNode> ParameterGroups { get; } = new();

        [ObservableProperty]
        private bool _isParameterPopoverOpen;

        [ObservableProperty]
        private string _parameterFilterText = string.Empty;

        // â”€â”€ Assignment Queue (Advanced DataGrid) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public ObservableCollection<ParameterAssignmentRow> QueueRows { get; } = new();
        public ICollectionView QueueView { get; }

        [ObservableProperty]
        private string _queueFilterText = string.Empty;

        // â”€â”€ Revit (Properties palette) group mapping â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public IReadOnlyList<RevitGroupOption> RevitGroupOptions => RevitGroupOption.All;

        [ObservableProperty]
        private RevitGroupOption _bulkGroupOption = RevitGroupOption.Default;

        // â”€â”€ Log â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public ObservableCollection<LogEntry> LogEntries { get; } = new();
        public ObservableCollection<LogEntry> SelectedLogEntries { get; } = new();

        [ObservableProperty]
        private bool _isLogExpanded;

        [ObservableProperty]
        private int _unreadLogCount;

        // â”€â”€ Wizard step â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty]
        private int _currentStep = 1;

        public bool IsStep1 => CurrentStep == 1;
        public bool IsStep2 => CurrentStep == 2;
        public bool IsStep3 => CurrentStep == 3;
        public bool IsStep4 => CurrentStep == 4;

        partial void OnCurrentStepChanged(int value)
        {
            OnPropertyChanged(nameof(IsStep1));
            OnPropertyChanged(nameof(IsStep2));
            OnPropertyChanged(nameof(IsStep3));
            OnPropertyChanged(nameof(IsStep4));
            NextStepCommand.NotifyCanExecuteChanged();
            PreviousStepCommand.NotifyCanExecuteChanged();
        }

        partial void OnMetricParamsFileCountChanged(int value) => NextStepCommand.NotifyCanExecuteChanged();
        partial void OnMetricCategoriesSelectedChanged(int value) => NextStepCommand.NotifyCanExecuteChanged();
        partial void OnMetricParametersSelectedChanged(int value) => NextStepCommand.NotifyCanExecuteChanged();

        private bool CanGoNext() => CurrentStep switch
        {
            1 => !string.IsNullOrWhiteSpace(SharedParameterFilePath),
            2 => SelectedCategories.Count > 0,
            3 => QueueRows.Count > 0,
            _ => false
        };

        [RelayCommand(CanExecute = nameof(CanGoNext))]
        private void NextStep()
        {
            if (CurrentStep >= 4) return;
            CurrentStep++;
        }

        private bool CanGoBack() => CurrentStep > 1;

        [RelayCommand(CanExecute = nameof(CanGoBack))]
        private void PreviousStep()
        {
            if (CurrentStep <= 1) return;
            CurrentStep--;
        }

        [RelayCommand]
        private void GoToStep(string stepNumber)
        {
            if (int.TryParse(stepNumber, out var step) && step >= 1 && step <= 4)
                CurrentStep = step;
        }

        [RelayCommand]
        private void ToggleLogExpanded()
        {
            IsLogExpanded = !IsLogExpanded;
            if (IsLogExpanded) UnreadLogCount = 0;
        }

        // â”€â”€ Run state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty]
        private bool _isRunning;

        // ToolWindowShell status strip (CLAUDE.md v1.1)
        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private string _summaryLine = "0 queued | 0 assigned | 0 failed";

        // â”€â”€ Metrics (top cards) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty] private int _metricParamsFileCount;
        [ObservableProperty] private int _metricCategoriesSelected;
        [ObservableProperty] private int _metricParametersSelected;
        [ObservableProperty] private int _metricAssignedThisSession;

        public ParaManagerViewModel(ParaManagerExternalEventHandler eventHandler, ExternalEvent externalEvent)
        {
            _eventHandler = eventHandler;
            _externalEvent = externalEvent;
            _eventHandler.Completed += OnRunCompleted;

            QueueView = CollectionViewSource.GetDefaultView(QueueRows);
            QueueView.Filter = FilterQueueRow;
            QueueRows.CollectionChanged += OnQueueRowsChanged;

            foreach (var cat in CategoryProvider.GetAvailableCategories())
                AvailableCategories.Add(cat);

            _settings = SettingsService<ParaManagerSettings>.Load(ToolFolderName);
            RestoreLastSession();

            Log(LogLevel.Info, "ParaManager ready.");
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Shared Parameter File
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [RelayCommand]
        private void BrowseSharedParameterFile()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Shared Parameter File",
                Filter = "Shared Parameter Files (*.txt)|*.txt|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog() != true) return;

            LoadSharedParameterFile(dlg.FileName);
        }

        [RelayCommand]
        private void ReloadSharedParameterFile()
        {
            if (string.IsNullOrWhiteSpace(SharedParameterFilePath))
            {
                Log(LogLevel.Warning, "No shared parameter file selected yet.");
                return;
            }
            LoadSharedParameterFile(SharedParameterFilePath);
        }

        private void LoadSharedParameterFile(string path)
        {
            try
            {
                _allParameters = SharedParameterFileParser.Parse(path);
                SharedParameterFilePath = path;
                MetricParamsFileCount = 1;

                RebuildParameterGroups();
                Log(LogLevel.Info, $"Shared parameter file loaded â€” {_allParameters.Count} parameters found.");

                _settings.LastSharedParameterFilePath = path;
                SettingsService<ParaManagerSettings>.Save(ToolFolderName, _settings);
            }
            catch (Exception ex)
            {
                MetricParamsFileCount = 0;
                Log(LogLevel.Error, $"Failed to load shared parameter file â€” {ex.Message}");
                TaskDialog.Show("ParaManager", $"Could not load shared parameter file:\n{ex.Message}");
            }
        }

        private void RebuildParameterGroups()
        {
            ParameterGroups.Clear();
            foreach (var group in SharedParameterFileParser.GroupByParameterGroup(_allParameters))
            {
                var node = new ParameterGroupNode(group.Key);
                foreach (var p in group)
                    node.AddParameter(new ParameterLeafNode(p));
                ParameterGroups.Add(node);
            }
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Categories (multi-select)
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [RelayCommand]
        private void ToggleCategoryPopup() => IsCategoryPopupOpen = !IsCategoryPopupOpen;

        [RelayCommand]
        private void ToggleCategory(CategoryInfo category)
        {
            if (SelectedCategories.Contains(category))
                SelectedCategories.Remove(category);
            else
                SelectedCategories.Add(category);

            MetricCategoriesSelected = SelectedCategories.Count;
        }

        [RelayCommand]
        private void RemoveCategory(CategoryInfo category)
        {
            SelectedCategories.Remove(category);
            MetricCategoriesSelected = SelectedCategories.Count;
        }

        [RelayCommand]
        private void ClearCategories()
        {
            SelectedCategories.Clear();
            MetricCategoriesSelected = 0;
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Parameter popover
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [RelayCommand]
        private void OpenParameterPopover()
        {
            if (_allParameters.Count == 0)
            {
                Log(LogLevel.Warning, "Load a shared parameter file before selecting parameters.");
                return;
            }
            IsParameterPopoverOpen = true;
        }

        [RelayCommand]
        private void SelectAllParameters()
        {
            foreach (var group in ParameterGroups)
                group.IsChecked = true;
        }

        [RelayCommand]
        private void ClearParameterSelection()
        {
            foreach (var group in ParameterGroups)
                group.IsChecked = false;
        }

        [RelayCommand]
        private void CancelParameterPopover() => IsParameterPopoverOpen = false;

        [RelayCommand]
        private void ApplyParameterPopover()
        {
            if (SelectedCategories.Count == 0)
            {
                Log(LogLevel.Warning, "Select at least one category before applying parameters.");
                return;
            }

            var checkedParams = ParameterGroups
                .SelectMany(g => g.Parameters)
                .Where(p => p.IsChecked)
                .Select(p => p.Parameter)
                .ToList();

            if (checkedParams.Count == 0)
            {
                Log(LogLevel.Warning, "No parameters selected.");
                return;
            }

            var categoriesSnapshot = SelectedCategories.ToList();

            foreach (var param in checkedParams)
            {
                // Avoid queueing an exact duplicate (same parameter, same category set) twice.
                bool alreadyQueued = QueueRows.Any(r =>
                    r.Parameter.Guid == param.Guid &&
                    r.Categories.Select(c => c.Name).OrderBy(n => n)
                        .SequenceEqual(categoriesSnapshot.Select(c => c.Name).OrderBy(n => n)));

                if (alreadyQueued) continue;

                QueueRows.Add(new ParameterAssignmentRow(param, categoriesSnapshot));
            }

            RecalculateParametersSelectedMetric();
            UpdateSummaryLine();
            IsParameterPopoverOpen = false;

            Log(LogLevel.Info, $"{checkedParams.Count} parameter(s) staged for assignment across {categoriesSnapshot.Count} categor{(categoriesSnapshot.Count == 1 ? "y" : "ies")}.");
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Queue grid (filter / select all / clear / refresh)
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        partial void OnQueueFilterTextChanged(string value) => QueueView.Refresh();

        private bool FilterQueueRow(object obj)
        {
            if (obj is not ParameterAssignmentRow row) return false;
            if (string.IsNullOrWhiteSpace(QueueFilterText)) return true;

            var term = QueueFilterText.Trim();
            return row.ParameterName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.ParameterGroup.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.CategoryDisplay.Contains(term, StringComparison.OrdinalIgnoreCase);
        }

        [RelayCommand]
        private void SelectAllQueueRows()
        {
            // Scoped to visible/filtered rows only, per Advanced DataGrid spec.
            foreach (var item in QueueView.Cast<ParameterAssignmentRow>())
                item.IsSelected = true;
        }

        [RelayCommand]
        private void ClearQueueSelection()
        {
            foreach (var item in QueueView.Cast<ParameterAssignmentRow>())
                item.IsSelected = false;
        }

        [RelayCommand]
        private void RefreshQueue() => QueueView.Refresh();

        /// <summary>Sets the Revit group on every ticked row currently visible in the (filtered) queue.</summary>
        [RelayCommand]
        private void ApplyGroupToSelected()
        {
            if (BulkGroupOption == null) return;

            var targets = QueueView.Cast<ParameterAssignmentRow>().Where(r => r.IsSelected).ToList();
            if (targets.Count == 0)
            {
                Log(LogLevel.Warning, "No rows selected â€” tick the parameters to map first.");
                return;
            }

            foreach (var row in targets)
                row.TargetGroup = BulkGroupOption;

            Log(LogLevel.Info, $"Revit group '{BulkGroupOption.Name}' applied to {targets.Count} parameter(s).");
        }

        [RelayCommand]
        private void RemoveQueueRow(ParameterAssignmentRow row)
        {
            QueueRows.Remove(row);
            RecalculateParametersSelectedMetric();
            UpdateSummaryLine();
        }

        /// <summary>Keeps the "Parameters Selected" metric card in sync with the live
        /// queue (distinct parameters across all rows), rather than just the size of
        /// the last-applied batch.</summary>
        private void RecalculateParametersSelectedMetric()
            => MetricParametersSelected = QueueRows.Select(r => r.Parameter.Guid).Distinct().Count();

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Run
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        // Set once a run completes without a run-level failure; cleared when the queue changes.
        // Keeps Run disabled so the same batch can't be fired twice by accident.
        private bool _hasRun;

        private bool CanRun() => !IsRunning && !_hasRun && QueueRows.Any(r => r.IsSelected);

        // RelayCommand does not re-query CanExecute on its own â€” without this the Run
        // button stays disabled from window-open (empty queue) onwards.
        private void OnQueueRowsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (ParameterAssignmentRow row in e.OldItems)
                    row.PropertyChanged -= OnQueueRowPropertyChanged;

            if (e.NewItems != null)
                foreach (ParameterAssignmentRow row in e.NewItems)
                    row.PropertyChanged += OnQueueRowPropertyChanged;

            _hasRun = false; // queue content changed â€” a new batch may be run
            RunCommand.NotifyCanExecuteChanged();
            NextStepCommand.NotifyCanExecuteChanged();
        }

        private void OnQueueRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ParameterAssignmentRow.IsSelected))
                RunCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            var selectedRows = QueueRows.Where(r => r.IsSelected).ToList();
            if (selectedRows.Count == 0)
            {
                Log(LogLevel.Warning, "No rows selected to run.");
                return;
            }

            // One binding-type prompt per Run click, applied to the whole batch.
            var choice = PromptBindingChoice();
            if (choice == null)
            {
                Log(LogLevel.Info, "Run cancelled â€” no binding type chosen.");
                return;
            }

            IsRunning = true;
            RunCommand.NotifyCanExecuteChanged();
            Log(LogLevel.Info, $"Run started â€” {selectedRows.Count} parameter(s), binding: {choice}.");

            _eventHandler.SharedParameterFilePath = SharedParameterFilePath;
            _eventHandler.RowsToAssign = selectedRows;
            _eventHandler.Binding = choice.Value;
            _externalEvent.Raise();
        }

        private BindingChoice? PromptBindingChoice()
        {
            var td = new TaskDialog("ParaManager â€” Binding Type");
            td.MainContent = "Choose binding type for this batch:\n\nYes = Instance binding\nNo = Type binding\nCancel = abort run";
            td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No | TaskDialogCommonButtons.Cancel;
            var result = td.Show();

            return result switch
            {
                TaskDialogResult.Yes => BindingChoice.Instance,
                TaskDialogResult.No => BindingChoice.Type,
                _ => null
            };
        }

        private void OnRunCompleted(List<RowResult> results, Exception failure)
        {
            // Marshal back if needed â€” Completed is invoked from the API thread's
            // synchronization context set up by ExternalEvent, safe to touch UI-bound
            // collections directly here per standard IExternalEventHandler pattern.
            IsRunning = false;
            RunCommand.NotifyCanExecuteChanged();

            if (failure != null)
            {
                Log(LogLevel.Error, $"Run failed â€” {failure.Message}");
                TaskDialog.Show("ParaManager", $"ParaManager run failed:\n{failure.Message}");
                return;
            }

            _hasRun = true;
            RunCommand.NotifyCanExecuteChanged();

            int assigned = 0, skipped = 0, failedCount = 0;

            foreach (var r in results)
            {
                switch (r.Outcome)
                {
                    case RowOutcome.Assigned:
                        assigned++;
                        Log(LogLevel.Success, r.Message);
                        break;
                    case RowOutcome.SkippedDuplicate:
                        skipped++;
                        Log(LogLevel.Warning, r.Message);
                        break;
                    case RowOutcome.Failed:
                        failedCount++;
                        Log(LogLevel.Error, r.Message);
                        break;
                }
            }

            MetricAssignedThisSession += assigned;
            SummaryLine = $"{QueueRows.Count} queued | {assigned} assigned | {skipped} skipped | {failedCount} failed";
            Log(LogLevel.Success, $"Run complete â€” {assigned} assigned | {skipped} skipped | {failedCount} failed.");

            AutoSaveLog();
        }

        private void UpdateSummaryLine()
            => SummaryLine = $"{QueueRows.Count} queued | 0 assigned | 0 skipped | 0 failed";

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Log
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void Log(LogLevel level, string message)
        {
            LogEntries.Add(new LogEntry(level, message));
            if (!IsLogExpanded) UnreadLogCount++;
        }

        [RelayCommand]
        private void CopyAllLog()
        {
            var text = string.Join(Environment.NewLine, LogEntries.Select(e => e.ToString()));
            SafeSetClipboard(text);
        }

        [RelayCommand]
        private void CopySelectedLog()
        {
            var text = string.Join(Environment.NewLine, SelectedLogEntries.Select(e => e.ToString()));
            SafeSetClipboard(text);
        }

        private void SafeSetClipboard(string text)
        {
            try { System.Windows.Clipboard.SetText(text ?? string.Empty); }
            catch { /* clipboard can be locked by another process â€” non-fatal */ }
        }

        [RelayCommand]
        private void ExportLogs()
        {
            try
            {
                string folder = _settings.LastLogExportFolder;

                if (string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder))
                {
                    var dlg = new OpenFolderDialog { Title = "Select folder to save ParaManager logs" };
                    if (dlg.ShowDialog() != true) return;
                    folder = dlg.FolderName;

                    _settings.LastLogExportFolder = folder;
                    SettingsService<ParaManagerSettings>.Save(ToolFolderName, _settings);
                }

                var path = LogExportService.Export(ToolFolderName, LogEntries, folder);
                Log(LogLevel.Success, $"Log exported to {path}");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Log export failed â€” {ex.Message}");
            }
        }

        private void AutoSaveLog()
        {
            try
            {
                string folder = string.IsNullOrWhiteSpace(_settings.LastLogExportFolder)
                    ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Revit26_Plugin_Revised", "ParaManager", "Logs")
                    : _settings.LastLogExportFolder;

                LogExportService.Export(ToolFolderName, LogEntries, folder);
            }
            catch
            {
                // Auto-save is best-effort; manual Export Logs remains available if this fails.
            }
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Session restore
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void RestoreLastSession()
        {
            if (!string.IsNullOrWhiteSpace(_settings.LastSharedParameterFilePath)
                && System.IO.File.Exists(_settings.LastSharedParameterFilePath))
            {
                LoadSharedParameterFile(_settings.LastSharedParameterFilePath);
            }

            if (_settings.LastSelectedCategoryNames?.Length > 0)
            {
                foreach (var name in _settings.LastSelectedCategoryNames)
                {
                    var match = AvailableCategories.FirstOrDefault(c => c.Name == name);
                    if (match != null) SelectedCategories.Add(match);
                }
                MetricCategoriesSelected = SelectedCategories.Count;
            }
        }

        /// <summary>Call from the Window's Closing event to persist category selection.</summary>
        public void OnWindowClosing()
        {
            _settings.LastSelectedCategoryNames = SelectedCategories.Select(c => c.Name).ToArray();
            SettingsService<ParaManagerSettings>.Save(ToolFolderName, _settings);
        }
    }
}

