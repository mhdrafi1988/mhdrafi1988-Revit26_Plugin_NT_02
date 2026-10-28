using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.BulkDelete.V001.UI.ViewModels
{
    /// <summary>
    /// The items grid: its rows, the filter, and the counters. Only rows that are visible (match the filter)
    /// count as ticked and are deleted, so a ticked row hidden by the filter is never deleted by surprise.
    /// </summary>
    public sealed partial class DeleteTableViewModel : ObservableObject
    {
        private readonly Action _changed;

        /// <summary>Rows of the selected kind of item.</summary>
        public ObservableCollection<DeleteRowViewModel> Rows { get; } = new();

        /// <summary>Rows that match the filter, in grid order (including the user's column sort).</summary>
        public ICollectionView RowsView { get; }

        /// <summary>Shows only rows whose name contains this text.</summary>
        [ObservableProperty] private string filterText = string.Empty;

        /// <summary>Items listed.</summary>
        [ObservableProperty] private int totalCount;

        /// <summary>Items that can be deleted (not locked).</summary>
        [ObservableProperty] private int deletableCount;

        /// <summary>Visible rows that are ticked and will be deleted on Delete.</summary>
        [ObservableProperty] private int tickedCount;

        /// <summary>Items that are locked.</summary>
        [ObservableProperty] private int lockedCount;

        /// <summary>Creates an empty table; <paramref name="changed"/> runs after every counter update.</summary>
        public DeleteTableViewModel(Action changed)
        {
            _changed = changed;

            var view = CollectionViewSource.GetDefaultView(Rows);
            view.Filter = obj => obj is DeleteRowViewModel row && PassesFilter(row);
            RowsView = view;
        }

        partial void OnFilterTextChanged(string value)
        {
            RowsView?.Refresh();
            UpdateCounts();
        }

        /// <summary>Replaces the rows with <paramref name="rows"/>.</summary>
        public void Show(IReadOnlyList<DeleteRowViewModel> rows)
        {
            foreach (var row in Rows) row.PropertyChanged -= OnRowPropertyChanged;
            Rows.Clear();
            foreach (var row in rows ?? Array.Empty<DeleteRowViewModel>())
            {
                row.PropertyChanged -= OnRowPropertyChanged;
                row.PropertyChanged += OnRowPropertyChanged;
                Rows.Add(row);
            }
            UpdateCounts();
        }

        /// <summary>Visible rows that are ticked and can be deleted.</summary>
        public List<DeleteRowViewModel> TickedRows()
            => RowsView.Cast<DeleteRowViewModel>().Where(r => r.IsSelected && !r.IsLocked).ToList();

        /// <summary>Ticks (or unticks) every visible row that can be deleted.</summary>
        public void SetTicks(bool ticked)
        {
            foreach (var row in RowsView.Cast<DeleteRowViewModel>().Where(r => !r.IsLocked))
                row.IsSelected = ticked;
        }

        /// <summary>
        /// Ticks every visible row that is known to be unused and unticks every other visible row.
        /// Returns how many rows are ticked.
        /// </summary>
        public int TickUnused()
        {
            int ticked = 0;
            foreach (var row in RowsView.Cast<DeleteRowViewModel>().Where(r => !r.IsLocked))
            {
                row.IsSelected = row.Item.IsKnownUnused;
                if (row.IsSelected) ticked++;
            }
            return ticked;
        }

        private bool PassesFilter(DeleteRowViewModel row)
            => string.IsNullOrWhiteSpace(FilterText)
               || row.Name.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) >= 0;

        private void OnRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeleteRowViewModel.IsSelected)) UpdateCounts();
        }

        private void UpdateCounts()
        {
            TotalCount = Rows.Count;
            LockedCount = Rows.Count(r => r.IsLocked);
            DeletableCount = TotalCount - LockedCount;
            TickedCount = RowsView == null ? 0 : TickedRows().Count;
            _changed?.Invoke();
        }
    }
}
