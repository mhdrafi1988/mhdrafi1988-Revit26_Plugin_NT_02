using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using SortKey = Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid.SortKey;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.UI.ViewModels
{
    /// <summary>How a column is filtered.</summary>
    public enum ColumnFilterKind
    {
        /// <summary>Tick list of the column's values.</summary>
        Ticks,
        /// <summary>"Contains" text.</summary>
        Contains,
        /// <summary>Smallest and largest value.</summary>
        Range
    }

    /// <summary>The filter pop-up of one column.</summary>
    public sealed partial class ColumnFilterViewModel : ObservableObject
    {
        /// <summary>Column name as the grid state knows it.</summary>
        public string Column { get; init; }
        /// <summary>"Filter: Source".</summary>
        public string Title => "Filter: " + Column;
        /// <summary>Tick list, contains or range.</summary>
        public ColumnFilterKind Kind { get; init; }
        /// <summary>Values to tick.</summary>
        public ObservableCollection<FilterItemViewModel> Items { get; } = new();
        /// <summary>True for a tick list.</summary>
        public bool IsTicks => Kind == ColumnFilterKind.Ticks;
        /// <summary>True for a contains box.</summary>
        public bool IsContains => Kind == ColumnFilterKind.Contains;
        /// <summary>True for a min–max range.</summary>
        public bool IsRange => Kind == ColumnFilterKind.Range;

        [ObservableProperty] private string _containsText = string.Empty;
        [ObservableProperty] private string _minText = string.Empty;
        [ObservableProperty] private string _maxText = string.Empty;
    }

    public sealed partial class ViewModel_WetAreaWaterproofing
    {
        /// <summary>Raised after the sort changed, so the window can redraw the header arrows.</summary>
        public event EventHandler SortChanged;

        /// <summary>The open column filter; null when none is open.</summary>
        [ObservableProperty] private ColumnFilterViewModel _activeFilter;

        /// <summary>Columns that have a filter on, for the header icons.</summary>
        public IReadOnlyCollection<string> FilteredColumns
        {
            get
            {
                var columns = new HashSet<string>(State.TickFilters.Where(f => f.Value.Count > 0).Select(f => f.Key));
                foreach (var f in State.Contains.Where(f => !string.IsNullOrWhiteSpace(f.Value))) columns.Add(f.Key);
                if (State.AreaMin.HasValue || State.AreaMax.HasValue) columns.Add("Area");
                return columns;
            }
        }

        /// <summary>Sort direction of <paramref name="column"/>, or null when it is not sorted.</summary>
        public ListSortDirection? SortDirectionOf(string column)
        {
            SortKey key = State.Sort.FirstOrDefault(k => k.Column == column);
            return key == null ? null : key.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        }

        /// <summary>
        /// A click on a column header: ascending, then descending, then off. With
        /// <paramref name="additive"/> (Shift) the column is added to the current sort.
        /// </summary>
        public void SortBy(string column, bool additive)
        {
            if (string.IsNullOrEmpty(column)) return;
            SortKey key = State.Sort.FirstOrDefault(k => k.Column == column);
            if (!additive) State.Sort.RemoveAll(k => k.Column != column);

            if (key == null) State.Sort.Add(new SortKey(column, false));
            else if (!key.Descending) key.Descending = true;
            else State.Sort.Remove(key);

            RebuildRows();
            SortChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>A click on a level button; <paramref name="level"/> null is the "All" button.</summary>
        public void ToggleLevel(string level, bool solo)
        {
            if (level == null) State.Levels.Clear();
            else State.ToggleLevel(level, solo, GridQuery.LevelCounts(_all.Select(c => c.Row), State.WetOnly).Select(l => l.Level));
            RebuildRows();
        }

        /// <summary>Opens the filter of <paramref name="column"/> with its current values.</summary>
        public ColumnFilterViewModel OpenFilter(string column)
        {
            ColumnFilterKind kind = column switch
            {
                "Number" or "Name" => ColumnFilterKind.Contains,
                "Area" => ColumnFilterKind.Range,
                _ => ColumnFilterKind.Ticks
            };
            var filter = new ColumnFilterViewModel { Column = column, Kind = kind };
            if (kind == ColumnFilterKind.Ticks)
            {
                State.TickFilters.TryGetValue(column, out HashSet<string> shown);
                IEnumerable<RoomRow> pool = _all.Select(c => c.Row).Where(r => !State.WetOnly || !string.IsNullOrEmpty(r.Matched));
                foreach (string value in pool.Select(r => GridQuery.Value(r, column)).Distinct().OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
                    filter.Items.Add(new FilterItemViewModel(value, shown == null || shown.Count == 0 || shown.Contains(value)));
            }
            else if (kind == ColumnFilterKind.Contains)
            {
                filter.ContainsText = State.Contains.TryGetValue(column, out string text) ? text : string.Empty;
            }
            else
            {
                filter.MinText = State.AreaMin?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
                filter.MaxText = State.AreaMax?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
            }
            ActiveFilter = filter;
            return filter;
        }

        /// <summary>Applies the open column filter.</summary>
        [RelayCommand]
        private void ApplyFilter()
        {
            ColumnFilterViewModel f = ActiveFilter;
            if (f == null) return;
            switch (f.Kind)
            {
                case ColumnFilterKind.Ticks:
                    var shown = new HashSet<string>(f.Items.Where(i => i.IsChecked).Select(i => i.Text));
                    if (shown.Count == 0 || shown.Count == f.Items.Count) State.TickFilters.Remove(f.Column);
                    else State.TickFilters[f.Column] = shown;
                    break;
                case ColumnFilterKind.Contains:
                    if (string.IsNullOrWhiteSpace(f.ContainsText)) State.Contains.Remove(f.Column);
                    else State.Contains[f.Column] = f.ContainsText.Trim();
                    break;
                default:
                    State.AreaMin = ParseOptional(f.MinText);
                    State.AreaMax = ParseOptional(f.MaxText);
                    break;
            }
            ActiveFilter = null;
            RebuildRows();
        }

        /// <summary>Removes the open column's filter.</summary>
        [RelayCommand]
        private void ClearFilter()
        {
            ColumnFilterViewModel f = ActiveFilter;
            if (f == null) return;
            State.TickFilters.Remove(f.Column);
            State.Contains.Remove(f.Column);
            if (f.Kind == ColumnFilterKind.Range) State.AreaMin = State.AreaMax = null;
            ActiveFilter = null;
            RebuildRows();
        }

        /// <summary>Ticks or unticks every value in the open tick-list filter.</summary>
        [RelayCommand]
        private void FilterSelectAll(string check)
        {
            if (ActiveFilter == null) return;
            foreach (FilterItemViewModel item in ActiveFilter.Items) item.IsChecked = check == "1";
        }

        /// <summary>Removes every filter and the search text.</summary>
        [RelayCommand]
        private void ClearFilters()
        {
            State.Clear();
            _loading = true;
            SearchText = string.Empty;
            _loading = false;
            RebuildRows();
        }

        /// <summary>Shows only the rooms that need a look (repaired, warnings, cannot run).</summary>
        [RelayCommand]
        private void ShowAttention()
        {
            State.Clear();
            State.TickFilters["Status"] = new HashSet<string>(new[]
            {
                RoomStatus.Repaired, RoomStatus.AreaWarning, RoomStatus.Approximate, RoomStatus.NoSlab,
                RoomStatus.Owned, RoomStatus.NotEnclosed, RoomStatus.Failed
            }.Select(s => s.ToString()));
            _loading = true;
            SearchText = string.Empty;
            _loading = false;
            RebuildRows();
        }

        /// <summary>Ticks every row left after filtering that a run can do.</summary>
        [RelayCommand]
        private void TickVisible() => SetVisibleTicks(r => r.CanRun);

        /// <summary>Unticks every row left after filtering.</summary>
        [RelayCommand]
        private void UntickVisible() => SetVisibleTicks(_ => false);

        /// <summary>Flips the tick of every row left after filtering.</summary>
        [RelayCommand]
        private void InvertVisible() => SetVisibleTicks(r => !r.IsTicked && r.CanRun);

        private void SetVisibleTicks(Func<RoomRowViewModel, bool> wanted)
        {
            foreach (RoomRowViewModel row in Rows)
            {
                bool tick = wanted(row);
                if (tick) _ticked.Add(row.RoomKey); else _ticked.Remove(row.RoomKey);
                row.SetTickSilently(tick);
            }
            UpdateCounts();
        }

        private void OnRowTickChanged(RoomRowViewModel row)
        {
            if (row.IsTicked && !row.CanRun)
            {
                row.SetTickSilently(false);
                AddLog(Shared.Models.LogLevel.Warning, $"Room {row.Number} {row.Name} cannot be run: {row.StatusDetail}.");
                return;
            }
            if (row.IsTicked) _ticked.Add(row.RoomKey); else _ticked.Remove(row.RoomKey);
            UpdateCounts();
        }

        /// <summary>Fills the grid from the rooms, the filters and the sort.</summary>
        private void RebuildRows()
        {
            if (_loading) return;
            IReadOnlyList<RoomRow> rows = GridQuery.Apply(_all.Select(c => c.Row), State);

            using (RowsView.DeferRefresh())
            {
                RowsView.GroupDescriptions.Clear();
                Rows.Clear();
                foreach (RoomRow row in rows)
                    Rows.Add(new RoomRowViewModel(row, _ticked.Contains(row.RoomKey), GroupKey(row), OnRowTickChanged));
                if (State.GroupBy != "None") RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RoomRowViewModel.GroupKey)));
            }

            LevelButtons.Clear();
            LevelButtons.Add(new LevelButtonViewModel(null, 0, State.Levels.Count == 0));
            foreach (LevelCount level in GridQuery.LevelCounts(_all.Select(c => c.Row), State.WetOnly))
                LevelButtons.Add(new LevelButtonViewModel(level.Level, level.Count, State.Levels.Count == 0 || State.Levels.Contains(level.Level)));

            OnPropertyChanged(nameof(FilteredColumns));
            UpdateCounts();
        }

        private string GroupKey(RoomRow row) => State.GroupBy switch
        {
            "Level" => GridQuery.Value(row, "Level"),
            "Source" => GridQuery.Value(row, "Source"),
            "Matched" => GridQuery.Value(row, "Matched"),
            _ => string.Empty
        };

        private void UpdateCounts()
        {
            var tickedRooms = _all.Where(c => _ticked.Contains(c.Row.RoomKey)).ToList();
            int filters = GridQuery.ActiveFilterCount(State);
            StatusText = $"{Rows.Count} shown · {tickedRooms.Count} ticked · {_all.Count} total"
                + (filters == 0 ? string.Empty : filters == 1 ? " · 1 filter active" : $" · {filters} filters active");
            RunButtonText = tickedRooms.Count == 1 ? "Run for 1 room" : $"Run for {tickedRooms.Count} rooms";
            RunCommand.NotifyCanExecuteChanged();

            var attention = tickedRooms.Where(c => c.Row.Status is RoomStatus.Repaired or RoomStatus.AreaWarning
                or RoomStatus.Approximate or RoomStatus.NoSlab).ToList();
            HasAttention = attention.Count > 0;
            AttentionText = attention.Count == 0 ? string.Empty
                : $"⚠ {attention.Count} ticked room(s) need attention: "
                  + string.Join(" · ", attention.Take(3).Select(c => $"{c.Row.Number} {c.Row.Name} ({c.Row.StatusDetail})"))
                  + (attention.Count > 3 ? " …" : string.Empty);
        }

        private static double? ParseOptional(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : null;
        }
    }
}
