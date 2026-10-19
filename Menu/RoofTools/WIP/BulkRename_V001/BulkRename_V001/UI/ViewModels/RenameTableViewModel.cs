using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.BulkRename.V001.Core.Models;
using Revit26_Plugin_Revised.BulkRename.V001.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.BulkRename.V001.UI.ViewModels
{
    /// <summary>
    /// The names grid: its rows, the filter, and the validation that keeps every row's status and the four
    /// counters up to date. Rows are checked together, because a duplicate name depends on the whole list.
    /// </summary>
    public sealed partial class RenameTableViewModel : ObservableObject
    {
        private readonly Action _validated;

        // Above zero while rows are changed in a batch: validation runs once, when the batch ends.
        private int _deferDepth;

        /// <summary>Rows of the selected kind of item.</summary>
        public ObservableCollection<RenameRowViewModel> Rows { get; } = new();

        /// <summary>Rows that match the filter, in grid order (including the user's column sort).</summary>
        public ICollectionView RowsView { get; }

        /// <summary>Shows only rows whose current name contains this text.</summary>
        [ObservableProperty] private string filterText = string.Empty;

        /// <summary>Items listed.</summary>
        [ObservableProperty] private int totalCount;

        /// <summary>Rows that are ticked and can be renamed.</summary>
        [ObservableProperty] private int tickedCount;

        /// <summary>Rows that will be renamed on Apply.</summary>
        [ObservableProperty] private int changeCount;

        /// <summary>Ticked rows with an invalid or duplicate new name; Apply waits until there are none.</summary>
        [ObservableProperty] private int problemCount;

        /// <summary>Creates an empty table; <paramref name="validated"/> runs after every validation.</summary>
        public RenameTableViewModel(Action validated)
        {
            _validated = validated;

            var view = CollectionViewSource.GetDefaultView(Rows);
            view.Filter = obj => obj is RenameRowViewModel row && PassesFilter(row);
            RowsView = view;
        }

        partial void OnFilterTextChanged(string value) => RowsView?.Refresh();

        /// <summary>True when any row has a new name that has not been applied.</summary>
        public bool HasUnappliedEdits => Rows.Any(r => !string.Equals(r.NewName, r.CurrentName, StringComparison.Ordinal));

        /// <summary>Replaces the rows with <paramref name="rows"/> and validates them.</summary>
        public void Show(IReadOnlyList<RenameRowViewModel> rows)
        {
            using (DeferValidation())
            {
                foreach (var row in Rows) row.PropertyChanged -= OnRowPropertyChanged;
                Rows.Clear();
                foreach (var row in rows ?? Array.Empty<RenameRowViewModel>())
                {
                    row.PropertyChanged -= OnRowPropertyChanged;
                    row.PropertyChanged += OnRowPropertyChanged;
                    Rows.Add(row);
                }
            }
        }

        /// <summary>Ticked rows that can be renamed and match the filter, in grid order. The rules act on these.</summary>
        public List<RenameRowViewModel> VisibleTargets()
            => RowsView.Cast<RenameRowViewModel>().Where(r => r.IsSelected && !r.IsLocked).ToList();

        /// <summary>Rows that Apply will rename.</summary>
        public List<RenameRowViewModel> ReadyRows()
            => Rows.Where(r => r.Status == RenameStatus.Ready).ToList();

        /// <summary>
        /// Gives each row in <paramref name="rows"/> the new name at the same position in
        /// <paramref name="names"/>, then validates once. Returns how many names actually changed.
        /// </summary>
        public int Assign(IReadOnlyList<RenameRowViewModel> rows, IReadOnlyList<string> names)
        {
            if (rows.Count != names.Count)
                throw new ArgumentException("There must be one new name per row.", nameof(names));

            int changed = 0;
            using (DeferValidation())
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (string.Equals(rows[i].NewName, names[i], StringComparison.Ordinal)) continue;
                    rows[i].NewName = names[i];
                    changed++;
                }
            }
            return changed;
        }

        /// <summary>Ticks (or unticks) every visible row that can be renamed.</summary>
        public void SetTicks(bool ticked)
        {
            using (DeferValidation())
            {
                foreach (var row in RowsView.Cast<RenameRowViewModel>().Where(r => !r.IsLocked))
                    row.IsSelected = ticked;
            }
        }

        /// <summary>Puts every visible row's new name back to its current name. Returns how many changed.</summary>
        public int RevertVisible()
        {
            int reverted = 0;
            using (DeferValidation())
            {
                foreach (var row in RowsView.Cast<RenameRowViewModel>().Where(r => !r.IsLocked))
                {
                    if (string.Equals(row.NewName, row.CurrentName, StringComparison.Ordinal)) continue;
                    row.NewName = row.CurrentName;
                    reverted++;
                }
            }
            return reverted;
        }

        /// <summary>
        /// Holds back validation while rows are changed in a batch, so a rule over 500 rows validates once.
        /// Validation runs when the outermost scope is disposed. Scopes can nest.
        /// </summary>
        public IDisposable DeferValidation()
        {
            _deferDepth++;
            return new DeferScope(this);
        }

        private bool PassesFilter(RenameRowViewModel row)
            => string.IsNullOrWhiteSpace(FilterText)
               || row.CurrentName.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) >= 0;

        private void OnRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_deferDepth > 0 || sender is not RenameRowViewModel row) return;

            if (e.PropertyName == nameof(RenameRowViewModel.NewName))
            {
                using (DeferValidation())
                {
                    // Typing a new name into a row is a request to rename it, so tick it.
                    if (!row.IsSelected && !row.IsLocked
                        && !string.Equals(row.NewName, row.CurrentName, StringComparison.Ordinal))
                        row.IsSelected = true;
                }
            }
            else if (e.PropertyName == nameof(RenameRowViewModel.IsSelected))
            {
                Validate();
            }
        }

        // Checks all rows together and refreshes their status, their final name and the counters.
        private void Validate()
        {
            var entries = Rows.Select(r => new NameEntry(r.CurrentName, r.NewName, r.IsSelected, r.IsLocked)).ToList();
            var checks = NameValidator.Check(entries);

            // The status updates below must not start another validation.
            _deferDepth++;
            try
            {
                for (int i = 0; i < Rows.Count; i++)
                {
                    var row = Rows[i];
                    row.Status = checks[i].Status;
                    row.StatusMessage = row.IsLocked ? row.LockReason : checks[i].Message ?? string.Empty;
                    row.FinalName = checks[i].FinalName;
                }
            }
            finally
            {
                _deferDepth--;
            }

            TotalCount = Rows.Count;
            TickedCount = Rows.Count(r => r.IsSelected && !r.IsLocked);
            ChangeCount = Rows.Count(r => r.Status == RenameStatus.Ready);
            ProblemCount = Rows.Count(r => r.Status == RenameStatus.Invalid || r.Status == RenameStatus.Duplicate);
            _validated?.Invoke();
        }

        private sealed class DeferScope : IDisposable
        {
            private RenameTableViewModel _owner;

            public DeferScope(RenameTableViewModel owner) => _owner = owner;

            public void Dispose()
            {
                var owner = _owner;
                if (owner == null) return;
                _owner = null;

                if (--owner._deferDepth == 0) owner.Validate();
            }
        }
    }
}
