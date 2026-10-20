using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Models;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Services;
using Revit26_Plugin_Revised.TrackerId.V002.Infrastructure.Revit;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.TrackerId.V002.UI.ViewModels
{
    // Step 6: read-only audit of every tracked element in the model.
    public partial class TrackerIdViewModel
    {
        private List<HealthIssue> _issues = new();

        /// <summary>Findings.</summary>
        [ObservableProperty] private ObservableCollection<HealthRowViewModel> _healthRows = new();

        /// <summary>Filtered view of <see cref="HealthRows"/>.</summary>
        [ObservableProperty] private ICollectionView _healthView;

        /// <summary>"All", "Duplicates", "Missing", "Moved", "Grid" or "Status".</summary>
        [ObservableProperty] private string _healthFilter = "All";

        /// <summary>One line under the health grid.</summary>
        [ObservableProperty] private string _healthSummaryText = "Click Run health check to audit the whole model.";

        /// <summary>Chip label "Duplicates & copies (3)".</summary>
        public string HealthDuplicatesChip => $"Duplicates & copies ({Count(HealthIssueKind.DuplicateId, HealthIssueKind.Copy)})";

        /// <summary>Chip label "Missing ID (12)".</summary>
        public string HealthMissingChip => $"Missing ID ({Count(HealthIssueKind.MissingId)})";

        /// <summary>Chip label "Moved (2)".</summary>
        public string HealthMovedChip => $"Moved ({Count(HealthIssueKind.Moved)})";

        /// <summary>Chip label "Far from grid (4)".</summary>
        public string HealthGridChip => $"Far from grid ({Count(HealthIssueKind.FarFromGrid)})";

        /// <summary>Chip label "Status (7)".</summary>
        public string HealthStatusChip => $"Status ({Count(HealthIssueKind.NoStatus, HealthIssueKind.IdNotInModel)})";

        /// <summary>Chip label "All (28)".</summary>
        public string HealthAllChip => $"All ({_issues.Count})";

        private void InitHealth()
        {
        }

        partial void OnHealthFilterChanged(string value) => HealthView?.Refresh();

        /// <summary>Picks a health filter chip.</summary>
        [RelayCommand]
        private void SetHealthFilter(string filter) => HealthFilter = filter ?? "All";

        /// <summary>Audits every element of the categories bound to Tracker_ID.</summary>
        [RelayCommand(CanExecute = nameof(CanRunHealthCheck))]
        private void RunHealthCheck()
        {
            var format = CurrentFormat();
            var needs = NeededParameterNames();
            string statusPath = StatusFilePath?.Trim();

            Run("Checking the model…", app =>
            {
                var doc = DocumentFor(app);
                if (doc == null) return;

                var categoryIds = ModelReader.TrackedCategoryIds(doc);
                if (categoryIds.Count == 0)
                {
                    OnUi(() =>
                    {
                        HealthSummaryText = "Tracker_ID is not in this project yet. Write IDs first (Write Tracker IDs).";
                        AddLog(LogLevel.Warning, HealthSummaryText);
                    });
                    return;
                }

                var read = ReadElements(doc, categoryIds, null, needs, ReadScope.WholeModel);

                StatusFileResult statusFile = null;
                string statusProblem = null;
                if (!string.IsNullOrWhiteSpace(statusPath) && File.Exists(statusPath))
                {
                    try { statusFile = StatusFileReader.Read(statusPath); }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                    {
                        statusProblem = $"Status checks skipped: {ex.Message}";
                    }
                }

                var issues = HealthChecker.Check(read.Snapshots, read.Holders, format, new GridLocator(read.Grids), statusFile);
                OnUi(() =>
                {
                    ShowHealth(issues, read.Snapshots.Count, categoryIds.Count);
                    if (statusProblem != null) AddLog(LogLevel.Warning, statusProblem);
                    else if (statusFile == null) AddLog(LogLevel.Info, "No status file chosen in the Status section, so the status checks were skipped.");
                });
            });
        }

        private bool CanRunHealthCheck() => !IsRunning;

        private void ShowHealth(List<HealthIssue> issues, int elementCount, int categoryCount)
        {
            _issues = issues;
            HealthRows = new ObservableCollection<HealthRowViewModel>(issues.Select(i => new HealthRowViewModel(i)));
            var view = CollectionViewSource.GetDefaultView(HealthRows);
            view.Filter = PassesHealthFilter;
            HealthView = view;

            HealthSummaryText = issues.Count == 0
                ? $"No problems found in {elementCount} element(s) of {categoryCount} tracked categor{(categoryCount == 1 ? "y" : "ies")}."
                : $"{issues.Count} finding(s) in {elementCount} element(s) of {categoryCount} tracked categor{(categoryCount == 1 ? "y" : "ies")}.";
            AddLog(issues.Count == 0 ? LogLevel.Success : LogLevel.Info, "Health check: " + HealthSummaryText);
            SummaryText = HealthSummaryText;

            foreach (string chip in new[] { nameof(HealthAllChip), nameof(HealthDuplicatesChip), nameof(HealthMissingChip),
                                            nameof(HealthMovedChip), nameof(HealthGridChip), nameof(HealthStatusChip) })
                OnPropertyChanged(chip);
            RefreshCommands();
        }

        private bool PassesHealthFilter(object item)
        {
            if (item is not HealthRowViewModel r) return false;
            return HealthFilter switch
            {
                "Duplicates" => r.Kind == HealthIssueKind.DuplicateId || r.Kind == HealthIssueKind.Copy,
                "Missing" => r.Kind == HealthIssueKind.MissingId,
                "Moved" => r.Kind == HealthIssueKind.Moved,
                "Grid" => r.Kind == HealthIssueKind.FarFromGrid,
                "Status" => r.Kind == HealthIssueKind.NoStatus || r.Kind == HealthIssueKind.IdNotInModel,
                _ => true
            };
        }

        private int Count(params HealthIssueKind[] kinds) => _issues.Count(i => kinds.Contains(i.Kind));

        /// <summary>Saves the findings as a CSV report.</summary>
        [RelayCommand]
        private void ExportHealthReport()
        {
            if (_issues.Count == 0)
            {
                AddLog(LogLevel.Info, "Nothing to export: run the health check first, or there are no findings.");
                return;
            }

            string suggested = Path.Combine(Path.GetDirectoryName(ExportPath) ?? string.Empty, $"Tracker_Health_{DateTime.Now:yyyy-MM-dd}.csv");
            string path = PickReportPath?.Invoke(suggested);
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                string written = CsvExportService.WriteReport(path, _issues, HealthRowViewModel.KindLabel);
                AddLog(LogLevel.Success, $"Health report saved to {written}.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                AddLog(LogLevel.Error, "Report not saved: " + ex.Message);
            }
        }

        /// <summary>
        /// Opens step 3 on the elements that need IDs (missing, copies, duplicates, moved): the scope
        /// becomes those elements, their categories and types are ticked, and nothing is written until
        /// the user clicks Write.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanFixInStep3))]
        private void FixInStep3()
        {
            var fixable = _issues.Where(i => i.ElementId.HasValue &&
                (i.Kind == HealthIssueKind.MissingId || i.Kind == HealthIssueKind.Copy ||
                 i.Kind == HealthIssueKind.DuplicateId || i.Kind == HealthIssueKind.Moved)).ToList();
            if (fixable.Count == 0) return;

            ScopeMode = ScopeMode.Selection;
            _scope = new ReadScope { Mode = ScopeMode.Selection, ElementIds = new HashSet<long>(fixable.Select(i => i.ElementId.Value)) };
            _scopeViewName = string.Empty;
            _restoreCategoryIds = new HashSet<long>(fixable.Select(i => i.CategoryId));
            foreach (var c in Categories) c.SetSelectedQuietly(false);

            ReloadCategories(captureScope: false, afterShow: () =>
            {
                _afterTypesShown = () =>
                {
                    foreach (var t in Types) t.IsSelected = true;
                    CurrentPage = 1;
                    IsResultExpanded = true;
                    AddLog(LogLevel.Info, $"Result shows {fixable.Count} element(s) from the health check. Review, then click Write Tracker IDs.");
                };
            });
        }

        private bool CanFixInStep3() => !IsRunning && _issues.Any(i => i.ElementId.HasValue &&
            (i.Kind == HealthIssueKind.MissingId || i.Kind == HealthIssueKind.Copy || i.Kind == HealthIssueKind.DuplicateId || i.Kind == HealthIssueKind.Moved));
    }
}
