using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.WorksetRenamer.FX05.ViewModels
{
    /// <summary>
    /// Workset Renamer (From Excel) window state. Update runs through <see cref="RevitApiQueue"/>, because
    /// the window is modeless and Revit refuses transactions started from its button clicks.
    /// </summary>
    public partial class WorksetRenamerFxViewModel : ObservableObject, IDisposable
    {
        private readonly Document _doc;

        // Runs the rename/create in Revit's API context. Created by the constructor, which the command runs.
        private readonly RevitApiQueue _revit;
        private List<ExcelMappingRow> _importedPairs = new List<ExcelMappingRow>();
        private bool _isRevalidating;

        public ObservableCollection<WorksetFxRowVM> Rows { get; } = new ObservableCollection<WorksetFxRowVM>();

        // ── Grouped views over Rows — one per rename-possibility bucket, shown
        // as its own expander. Each wraps the same underlying collection with a
        // different filter, refreshed whenever Rows or a row's classification changes.
        public ICollectionView OkRows { get; }
        public ICollectionView DuplicateRows { get; }
        public ICollectionView NewWorksetRows { get; }
        public ICollectionView UnmatchedRows { get; }
        public ICollectionView InvalidRows { get; }

        private readonly ListCollectionView[] _groupViews;

        [ObservableProperty]
        private string excelFileName = string.Empty;

        [ObservableProperty]
        private string statusMessage = "Load an Excel file to begin — Column A = Old Name, Column B = New Name.";

        // ── ToolWindowShell status strip (CLAUDE.md v1.1) ──
        [ObservableProperty] private bool   isRunning;
        [ObservableProperty] private double progress;

        [ObservableProperty]
        private bool createNewForUnmatched = true;

        [ObservableProperty]
        private string searchText = string.Empty;

        partial void OnSearchTextChanged(string value) => RefreshGroupViews();

        // ── Metric card counts ──────────────────────────────────────────
        [ObservableProperty] private int totalRowsRead;
        [ObservableProperty] private int okCount;
        [ObservableProperty] private int duplicateCount;
        [ObservableProperty] private int noMatchCount;
        [ObservableProperty] private int invalidCount;
        [ObservableProperty] private int newWorksetCount;
        [ObservableProperty] private int selectedCount;

        private bool MatchesSearch(WorksetFxRowVM row)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            var t = SearchText.Trim();
            return (row.OldName?.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                || (row.NewName?.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Creates the window state for <paramref name="doc"/>. Call from the command (a Revit API context).</summary>
        public WorksetRenamerFxViewModel(Document doc)
        {
            _doc = doc;
            _revit = new RevitApiQueue("Workset Renamer FX05");

            var okView = new ListCollectionView(Rows) { Filter = r => { var row = (WorksetFxRowVM)r; return row.Kind == RowKind.Rename && !row.IsDuplicateWarning && MatchesSearch(row); } };
            var dupView = new ListCollectionView(Rows) { Filter = r => { var row = (WorksetFxRowVM)r; return row.IsDuplicateWarning && MatchesSearch(row); } };
            var newView = new ListCollectionView(Rows) { Filter = r => { var row = (WorksetFxRowVM)r; return row.Kind == RowKind.CreateNew && !row.IsDuplicateWarning && MatchesSearch(row); } };
            var unmatchedView = new ListCollectionView(Rows) { Filter = r => { var row = (WorksetFxRowVM)r; return row.Kind == RowKind.Unmatched && MatchesSearch(row); } };
            var invalidView = new ListCollectionView(Rows) { Filter = r => { var row = (WorksetFxRowVM)r; return row.Kind == RowKind.Invalid && MatchesSearch(row); } };

            OkRows = okView;
            DuplicateRows = dupView;
            NewWorksetRows = newView;
            UnmatchedRows = unmatchedView;
            InvalidRows = invalidView;
            _groupViews = new[] { okView, dupView, newView, unmatchedView, invalidView };
        }

        private void RefreshGroupViews()
        {
            foreach (var view in _groupViews)
                view.Refresh();
        }

        // ══════════════════════════════════════════════════════════════
        // Export current worksets to Excel — a starter file the user can
        // edit (New Name column) and re-import via Browse/drag-drop.
        // ══════════════════════════════════════════════════════════════

        [RelayCommand]
        private void ExportWorksets()
        {
            var currentWorksets = new FilteredWorksetCollector(_doc)
                .OfKind(WorksetKind.UserWorkset)
                .Cast<Workset>()
                .OrderBy(ws => ws.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ws => ws.Name)
                .ToList();

            if (!currentWorksets.Any())
            {
                TaskDialog.Show("Export Worksets", "This model has no user worksets to export.");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Export Worksets to Excel",
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = "Worksets_Export.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                ExcelMappingWriter.Write(dlg.FileName, currentWorksets);
                StatusMessage = $"Exported {currentWorksets.Count} workset(s) to {Path.GetFileName(dlg.FileName)}";

                var td = new TaskDialog("Export Complete");
                td.MainContent = $"Exported {currentWorksets.Count} workset(s) to:\n{dlg.FileName}\n\n" +
                    "Yes     — Load the file back now for renaming\n" +
                    "No      — Open the file in Excel\n" +
                    "Cancel  — Dismiss";
                td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No | TaskDialogCommonButtons.Cancel;
                var result = td.Show();

                if (result == TaskDialogResult.Yes)
                    LoadExcelFile(dlg.FileName);
                else if (result == TaskDialogResult.No)
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Export Error", $"Could not write Excel file:\n{ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════
        // Load Excel
        // ══════════════════════════════════════════════════════════════

        [RelayCommand]
        private void BrowseExcel()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Workset Rename Map",
                Filter = "Excel Files (*.xlsx)|*.xlsx"
            };
            if (dlg.ShowDialog() == true)
                LoadExcelFile(dlg.FileName);
        }

        public void LoadExcelFile(string filePath)
        {
            try
            {
                _importedPairs = ExcelMappingReader.Read(filePath);
                ExcelFileName = Path.GetFileName(filePath);
                BuildRows();
                StatusMessage = $"Loaded {_importedPairs.Count} row(s) from {ExcelFileName}";
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Import Error", $"Could not read Excel file:\n{ex.Message}");
            }
        }

        [RelayCommand]
        private void RerunDryRun() => BuildRows();

        partial void OnCreateNewForUnmatchedChanged(bool value) => BuildRows();

        // ══════════════════════════════════════════════════════════════
        // Build dry-run rows from the last imported Excel pairs
        // ══════════════════════════════════════════════════════════════

        private void BuildRows()
        {
            foreach (var row in Rows)
                row.PropertyChanged -= Row_PropertyChanged;
            Rows.Clear();

            if (_importedPairs.Count == 0)
            {
                RecalculateMetrics();
                return;
            }

            var currentWorksets = new FilteredWorksetCollector(_doc)
                .OfKind(WorksetKind.UserWorkset)
                .Cast<Workset>()
                .ToList();

            var matchedIds = new HashSet<WorksetId>();
            int skippedUnmatched = 0;

            foreach (var pair in _importedPairs)
            {
                var match = currentWorksets.FirstOrDefault(ws =>
                    string.Equals(ws.Name, pair.OldName, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    matchedIds.Add(match.Id);
                    var kind = string.IsNullOrWhiteSpace(pair.NewName) ? RowKind.Invalid : RowKind.Rename;
                    AddRow(new WorksetFxRowVM(kind, match.Id, match.Name, pair.NewName));
                }
                else if (CreateNewForUnmatched)
                {
                    var kind = string.IsNullOrWhiteSpace(pair.NewName) ? RowKind.Invalid : RowKind.CreateNew;
                    AddRow(new WorksetFxRowVM(kind, null, pair.OldName, pair.NewName));
                }
                else
                {
                    skippedUnmatched++;
                }
            }

            // Model worksets with no matching Excel row — informational only.
            foreach (var ws in currentWorksets.Where(w => !matchedIds.Contains(w.Id)))
                AddRow(new WorksetFxRowVM(RowKind.Unmatched, ws.Id, ws.Name, ws.Name));

            RevalidateDuplicates();
            RecalculateMetrics();
            RefreshGroupViews();

            StatusMessage = skippedUnmatched > 0
                ? $"{skippedUnmatched} unmatched Excel row(s) skipped — enable \"Create new worksets\" to include them."
                : $"Dry run ready — {Rows.Count(r => r.IsActionable)} row(s) actionable.";
        }

        private void AddRow(WorksetFxRowVM row)
        {
            row.PropertyChanged += Row_PropertyChanged;
            Rows.Add(row);
        }

        private void Row_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isRevalidating) return;

            if (e.PropertyName == nameof(WorksetFxRowVM.NewName))
                RevalidateDuplicates();

            if (e.PropertyName == nameof(WorksetFxRowVM.NewName) || e.PropertyName == nameof(WorksetFxRowVM.IsSelected))
            {
                RecalculateMetrics();
                RefreshGroupViews();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // Duplicate re-validation — auto-suffixes colliding New Names.
        // Re-run on every edit AND again right before the Update transaction.
        // ══════════════════════════════════════════════════════════════

        private void RevalidateDuplicates()
        {
            _isRevalidating = true;
            try
            {
                var existingNames = new FilteredWorksetCollector(_doc)
                    .OfKind(WorksetKind.UserWorkset)
                    .Cast<Workset>()
                    .Select(ws => ws.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var candidates = Rows.Where(r => r.IsActionable && !string.IsNullOrWhiteSpace(r.NewName)).ToList();
                var takenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var row in candidates)
                {
                    row.IsDuplicateWarning = false;

                    // Names this row is allowed to keep without triggering a "collision with existing" check:
                    // its own current name (Rename rows renaming to something already used by themselves — a no-op).
                    string ownCurrentName = row.Kind == RowKind.Rename ? row.OldName : null;

                    bool collidesWithExisting = existingNames.Contains(row.NewName)
                        && !string.Equals(row.NewName, ownCurrentName, StringComparison.OrdinalIgnoreCase);
                    bool collidesWithBatch = takenNames.Contains(row.NewName);

                    if (collidesWithExisting || collidesWithBatch)
                    {
                        row.NewName = MakeUnique(row.NewName, existingNames, takenNames);
                        row.IsDuplicateWarning = true;
                    }

                    takenNames.Add(row.NewName);
                }
            }
            finally
            {
                _isRevalidating = false;
            }
        }

        private static string MakeUnique(string baseName, HashSet<string> existingNames, HashSet<string> takenNames)
        {
            int n = 1;
            string candidate;
            do
            {
                candidate = $"{baseName}_{n:00}";
                n++;
            }
            while (existingNames.Contains(candidate) || takenNames.Contains(candidate));
            return candidate;
        }

        // ══════════════════════════════════════════════════════════════
        // Metrics
        // ══════════════════════════════════════════════════════════════

        private void RecalculateMetrics()
        {
            TotalRowsRead = _importedPairs.Count;
            OkCount = Rows.Count(r => r.Kind == RowKind.Rename && !r.IsDuplicateWarning);
            DuplicateCount = Rows.Count(r => r.IsDuplicateWarning);
            NoMatchCount = Rows.Count(r => r.Kind == RowKind.Unmatched);
            InvalidCount = Rows.Count(r => r.Kind == RowKind.Invalid);
            NewWorksetCount = Rows.Count(r => r.Kind == RowKind.CreateNew && !r.IsDuplicateWarning);
            SelectedCount = Rows.Count(r => r.IsSelected);
        }

        // ══════════════════════════════════════════════════════════════
        // Select All Valid / Clear
        // ══════════════════════════════════════════════════════════════

        [RelayCommand]
        private void ClearSearch() => SearchText = string.Empty;

        [RelayCommand]
        private void SelectAllValid()
        {
            foreach (var row in Rows.Where(r => r.IsActionable))
                row.IsSelected = true;
        }

        [RelayCommand]
        private void ClearAll()
        {
            foreach (var row in Rows)
                row.PropertyChanged -= Row_PropertyChanged;
            Rows.Clear();
            _importedPairs.Clear();
            ExcelFileName = string.Empty;
            RecalculateMetrics();
            RefreshGroupViews();
            StatusMessage = "Cleared. Load an Excel file to begin.";
        }

        // ══════════════════════════════════════════════════════════════
        // Update — rename + create, inside one transaction
        // ══════════════════════════════════════════════════════════════

        [RelayCommand]
        private void Update()
        {
            if (IsRunning) return;

            if (!_doc.IsValidObject)
            {
                StatusMessage = "The project was closed. Close this window.";
                return;
            }

            var candidates = Rows.Where(r => r.IsSelected && r.IsActionable).ToList();
            if (!candidates.Any())
            {
                StatusMessage = "Nothing to update — no selected rows.";
                return;
            }

            var emptyRow = candidates.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.NewName));
            if (emptyRow != null)
            {
                TaskDialog.Show("Invalid Name", $"New name for '{emptyRow.OldName}' cannot be empty.");
                return;
            }

            // Final duplicate re-check right before the transaction — catches any
            // collision introduced by manual edits since the last dry run.
            RevalidateDuplicates();
            RecalculateMetrics();
            RefreshGroupViews();
            var stillColliding = FindRemainingCollisions(candidates);
            if (stillColliding.Any())
            {
                TaskDialog.Show("Duplicate Name",
                    $"Duplicate new name(s) remain after auto-resolve:\n{string.Join(", ", stillColliding)}\n\nAdjust them manually and try again.");
                return;
            }

            IsRunning = true;
            StatusMessage = $"Updating {candidates.Count} workset(s)…";

            // Revit runs this once it is idle; a modeless window cannot start the transaction itself.
            bool queued = _revit.TryRun(_ =>
            {
                try { UpdateInRevit(candidates); }
                finally { IsRunning = false; }
            });

            if (!queued)
            {
                IsRunning = false;
                StatusMessage = "Revit is busy. Finish the current command or close any open dialog, then click Update again.";
            }
        }

        // Runs in the Revit API context (through _revit).
        private void UpdateInRevit(List<WorksetFxRowVM> candidates)
        {
            if (!_doc.IsValidObject)
            {
                StatusMessage = "The project was closed. Close this window.";
                return;
            }

            int renamed = 0, created = 0, errors = 0;

            using (var tx = new Transaction(_doc, "Workset Renamer FX03 — Rename/Create From Excel"))
            {
                tx.Start();
                try
                {
                    foreach (var row in candidates)
                    {
                        try
                        {
                            if (row.Kind == RowKind.Rename)
                            {
                                WorksetTable.RenameWorkset(_doc, row.WorksetId, row.NewName);
                                row.Status = RowRenameStatus.Renamed;
                                renamed++;
                            }
                            else if (row.Kind == RowKind.CreateNew)
                            {
                                Workset.Create(_doc, row.NewName);
                                row.Status = RowRenameStatus.Created;
                                created++;
                            }
                        }
                        catch (Exception ex)
                        {
                            row.Status = RowRenameStatus.Error;
                            row.ErrorMessage = ex.Message;
                            errors++;
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    TaskDialog.Show("Update Error", $"Transaction failed: {ex.Message}");
                    return;
                }
            }

            foreach (var row in Rows.Where(r => r.IsSelected && r.IsActionable && r.Status == RowRenameStatus.Pending))
                row.Status = RowRenameStatus.Unchanged;

            StatusMessage = $"Done — {renamed} renamed, {created} created, {errors} error(s)";
        }

        /// <summary>Releases the Revit external event. Called when the window closes.</summary>
        public void Dispose() => _revit.Dispose();

        private List<string> FindRemainingCollisions(List<WorksetFxRowVM> candidates)
        {
            var existingNames = new FilteredWorksetCollector(_doc)
                .OfKind(WorksetKind.UserWorkset)
                .Cast<Workset>()
                .Where(ws => !candidates.Any(c => c.Kind == RowKind.Rename && c.WorksetId == ws.Id))
                .Select(ws => ws.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var batchDupes = candidates
                .GroupBy(r => r.NewName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            var collisions = candidates
                .Where(r => existingNames.Contains(r.NewName))
                .Select(r => r.NewName);

            return batchDupes.Concat(collisions).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
