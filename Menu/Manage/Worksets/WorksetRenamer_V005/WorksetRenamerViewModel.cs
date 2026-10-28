using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.WorksetRenamer.V005.ViewModels
{
    /// <summary>
    /// Workset Renamer window state. Apply runs through <see cref="RevitApiQueue"/>, because the window is
    /// modeless and Revit refuses transactions started from its button clicks.
    /// </summary>
    public class WorksetRenamerViewModel : INotifyPropertyChanged, IDisposable
    {
        // ── Revit document reference ──────────────────────────────────
        private readonly Document _doc;

        // Runs the rename in Revit's API context. Created by the constructor, which the command runs.
        private readonly RevitApiQueue _revit;

        // ── All rows (full list) ──────────────────────────────────────
        public ObservableCollection<WorksetRowVM> Rows { get; }
            = new ObservableCollection<WorksetRowVM>();

        // ── Filtered rows bound to DataGrid ──────────────────────────
        private ObservableCollection<WorksetRowVM> _filteredRows
            = new ObservableCollection<WorksetRowVM>();
        public ObservableCollection<WorksetRowVM> FilteredRows
        {
            get => _filteredRows;
            private set { _filteredRows = value; OnPropertyChanged(); }
        }

        // ── Filter ────────────────────────────────────────────────────
        private string _filterText = string.Empty;
        public string FilterText
        {
            get => _filterText;
            set
            {
                _filterText = value ?? string.Empty;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        // ── Find & Replace ────────────────────────────────────────────
        private string _findText = string.Empty;
        public string FindText
        {
            get => _findText;
            set { _findText = value; OnPropertyChanged(); }
        }

        private string _replaceText = string.Empty;
        public string ReplaceText
        {
            get => _replaceText;
            set { _replaceText = value; OnPropertyChanged(); }
        }

        // ── Prefix ────────────────────────────────────────────────────
        private string _prefix = string.Empty;
        public string Prefix
        {
            get => _prefix;
            set { _prefix = value; OnPropertyChanged(); }
        }

        // ── Suffix ────────────────────────────────────────────────────
        private string _suffix = string.Empty;
        public string Suffix
        {
            get => _suffix;
            set { _suffix = value; OnPropertyChanged(); }
        }

        // ── Status bar ────────────────────────────────────────────────
        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        // ── ToolWindowShell status strip (CLAUDE.md v1.1) ──────────────
        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(); }
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        // ══════════════════════════════════════════════════════════════
        // Commands
        // ══════════════════════════════════════════════════════════════

        public ICommand ApplyFindReplaceCommand { get; }
        public ICommand AddPrefixCommand { get; }
        public ICommand RemovePrefixCommand { get; }
        public ICommand AddSuffixCommand { get; }
        public ICommand RemoveSuffixCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand RevertCommand { get; }
        public ICommand ApplyCommand { get; }

        // ══════════════════════════════════════════════════════════════
        // Constructor
        // ══════════════════════════════════════════════════════════════

        /// <summary>Loads the worksets of <paramref name="doc"/>. Call from the command (a Revit API context).</summary>
        public WorksetRenamerViewModel(Document doc)
        {
            _doc = doc;
            _revit = new RevitApiQueue("Workset Renamer V005");
            LoadWorksets();

            ApplyFindReplaceCommand = new RelayCommand(_ => ExecuteFindReplace());
            AddPrefixCommand = new RelayCommand(_ => ExecuteAddPrefix());
            RemovePrefixCommand = new RelayCommand(_ => ExecuteRemovePrefix());
            AddSuffixCommand = new RelayCommand(_ => ExecuteAddSuffix());
            RemoveSuffixCommand = new RelayCommand(_ => ExecuteRemoveSuffix());
            SelectAllCommand = new RelayCommand(_ => SetSelectionOnVisible(true));
            SelectNoneCommand = new RelayCommand(_ => SetSelectionOnVisible(false));
            RevertCommand = new RelayCommand(_ => ExecuteRevert());
            ApplyCommand = new RelayCommand(_ => ExecuteApply());
        }

        // ══════════════════════════════════════════════════════════════
        // Load
        // ══════════════════════════════════════════════════════════════

        private void LoadWorksets()
        {
            Rows.Clear();
            var collector = new FilteredWorksetCollector(_doc)
                .OfKind(WorksetKind.UserWorkset);

            foreach (Workset ws in collector)
                Rows.Add(new WorksetRowVM(ws.Id, ws.Name, ws.IsOpen, ws.IsEditable));

            ApplyFilter();
        }

        // ══════════════════════════════════════════════════════════════
        // Filter
        // ══════════════════════════════════════════════════════════════

        private void ApplyFilter()
        {
            var term = _filterText.Trim();
            var result = string.IsNullOrEmpty(term)
                ? Rows.ToList()
                : Rows.Where(r => r.CurrentName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            FilteredRows = new ObservableCollection<WorksetRowVM>(result);
            StatusMessage = $"{FilteredRows.Count} of {Rows.Count} worksets shown";
        }

        // ══════════════════════════════════════════════════════════════
        // Find & Replace  (visible rows only)
        // ══════════════════════════════════════════════════════════════

        private void ExecuteFindReplace()
        {
            if (string.IsNullOrEmpty(FindText)) return;

            int count = 0;
            foreach (var row in FilteredRows)
            {
                var updated = row.NewName.Replace(FindText, ReplaceText ?? string.Empty);
                if (updated != row.NewName) { row.NewName = updated; count++; }
            }
            StatusMessage = $"Find & Replace applied to {count} row(s)";
        }

        // ══════════════════════════════════════════════════════════════
        // Prefix  (visible rows only)
        // ══════════════════════════════════════════════════════════════

        private void ExecuteAddPrefix()
        {
            if (string.IsNullOrEmpty(Prefix)) return;
            int count = 0;
            foreach (var row in FilteredRows)
                if (!row.NewName.StartsWith(Prefix)) { row.NewName = Prefix + row.NewName; count++; }
            StatusMessage = $"Prefix added to {count} row(s)";
        }

        private void ExecuteRemovePrefix()
        {
            if (string.IsNullOrEmpty(Prefix)) return;
            int count = 0;
            foreach (var row in FilteredRows)
                if (row.NewName.StartsWith(Prefix)) { row.NewName = row.NewName.Substring(Prefix.Length); count++; }
            StatusMessage = $"Prefix removed from {count} row(s)";
        }

        // ══════════════════════════════════════════════════════════════
        // Suffix  (visible rows only)
        // ══════════════════════════════════════════════════════════════

        private void ExecuteAddSuffix()
        {
            if (string.IsNullOrEmpty(Suffix)) return;
            int count = 0;
            foreach (var row in FilteredRows)
                if (!row.NewName.EndsWith(Suffix)) { row.NewName = row.NewName + Suffix; count++; }
            StatusMessage = $"Suffix added to {count} row(s)";
        }

        private void ExecuteRemoveSuffix()
        {
            if (string.IsNullOrEmpty(Suffix)) return;
            int count = 0;
            foreach (var row in FilteredRows)
                if (row.NewName.EndsWith(Suffix)) { row.NewName = row.NewName.Substring(0, row.NewName.Length - Suffix.Length); count++; }
            StatusMessage = $"Suffix removed from {count} row(s)";
        }

        // ══════════════════════════════════════════════════════════════
        // Select All / None  (visible rows only)
        // ══════════════════════════════════════════════════════════════

        private void SetSelectionOnVisible(bool value)
        {
            foreach (var row in FilteredRows)
                row.IsSelected = value;
        }

        // ══════════════════════════════════════════════════════════════
        // Revert  (all rows)
        // ══════════════════════════════════════════════════════════════

        private void ExecuteRevert()
        {
            foreach (var row in Rows)
            {
                row.NewName = row.OriginalName;
                row.Status = RenameStatus.Pending;
                row.ErrorMessage = null;
            }
            StatusMessage = "All new names reverted to original";
        }

        // ══════════════════════════════════════════════════════════════
        // Apply  — renames only; open/close/editable are read-only info
        // ══════════════════════════════════════════════════════════════

        private void ExecuteApply()
        {
            if (IsRunning) return;

            if (!_doc.IsValidObject)
            {
                StatusMessage = "The project was closed. Close this window.";
                return;
            }

            var candidates = Rows
                .Where(r => r.IsSelected && r.NewName != r.CurrentName)
                .ToList();

            if (!candidates.Any())
            {
                StatusMessage = "Nothing to rename — no selected rows have a changed name";
                return;
            }

            // ── Duplicate check ───────────────────────────────────────
            string dupError = CheckDuplicates(candidates);
            if (dupError != null)
            {
                TaskDialog.Show("Duplicate Name", dupError);
                return;
            }

            // ── Empty name check ──────────────────────────────────────
            var emptyRow = candidates.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.NewName));
            if (emptyRow != null)
            {
                TaskDialog.Show("Invalid Name", $"New name for '{emptyRow.CurrentName}' cannot be empty.");
                return;
            }

            IsRunning = true;
            StatusMessage = $"Renaming {candidates.Count} workset(s)…";

            // Revit runs this once it is idle; a modeless window cannot start the transaction itself.
            bool queued = _revit.TryRun(_ =>
            {
                try { RenameInRevit(candidates); }
                finally { IsRunning = false; }
            });

            if (!queued)
            {
                IsRunning = false;
                StatusMessage = "Revit is busy. Finish the current command or close any open dialog, then click Apply again.";
            }
        }

        // Runs in the Revit API context (through _revit).
        private void RenameInRevit(List<WorksetRowVM> candidates)
        {
            if (!_doc.IsValidObject)
            {
                StatusMessage = "The project was closed. Close this window.";
                return;
            }

            int renamed = 0, errors = 0;

            using (var tx = new Transaction(_doc, "Bulk Rename Worksets"))
            {
                tx.Start();
                try
                {
                    foreach (var row in candidates)
                    {
                        try
                        {
                            WorksetTable.RenameWorkset(_doc, row.WorksetId, row.NewName);
                            row.CurrentName = row.NewName;
                            row.Status = RenameStatus.Renamed;
                            renamed++;
                        }
                        catch (Exception ex)
                        {
                            row.Status = RenameStatus.Error;
                            row.ErrorMessage = ex.Message;
                            errors++;
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    TaskDialog.Show("Apply Error", $"Transaction failed: {ex.Message}");
                    return;
                }
            }

            // Mark selected rows that needed no rename
            foreach (var row in Rows.Where(r => r.IsSelected && r.Status == RenameStatus.Pending))
                row.Status = RenameStatus.Unchanged;

            StatusMessage = $"Done — {renamed} renamed, {errors} error(s)";
        }

        /// <summary>Releases the Revit external event. Called when the window closes.</summary>
        public void Dispose() => _revit.Dispose();

        // ══════════════════════════════════════════════════════════════
        // Duplicate guard
        // ══════════════════════════════════════════════════════════════

        private string CheckDuplicates(List<WorksetRowVM> candidates)
        {
            // Within the batch itself
            var batchDupes = candidates
                .GroupBy(r => r.NewName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (batchDupes.Any())
                return $"Duplicate new names in selection:\n{string.Join(", ", batchDupes)}";

            // Against existing worksets not being renamed
            var candidateIds = new HashSet<WorksetId>(candidates.Select(r => r.WorksetId));
            var existingNames = new FilteredWorksetCollector(_doc)
                .OfKind(WorksetKind.UserWorkset)
                .Cast<Workset>()
                .Where(ws => !candidateIds.Contains(ws.Id))
                .Select(ws => ws.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var collisions = candidates
                .Where(r => existingNames.Contains(r.NewName))
                .Select(r => r.NewName)
                .ToList();

            if (collisions.Any())
                return $"New name(s) already used by another workset:\n{string.Join(", ", collisions)}";

            return null;
        }

        // ══════════════════════════════════════════════════════════════
        // INotifyPropertyChanged
        // ══════════════════════════════════════════════════════════════

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── Minimal RelayCommand ──────────────────────────────────────────
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object parameter) => _execute(parameter);

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}