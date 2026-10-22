using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.DetailLineManager.V001.Core.Models;
using Revit26_Plugin_Revised.DetailLineManager.V001.Core.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Revit26_Plugin_Revised.DetailLineManager.V001.UI.ViewModels
{
    /// <summary>This part: the filter rail, the grid filter and the live counts.</summary>
    public sealed partial class MainViewModel
    {
        private static readonly HashSet<string> FilterProperties = new()
        {
            nameof(SearchText), nameof(UseRegex), nameof(SelectedViewType), nameof(MinWeight), nameof(MaxWeight),
            nameof(MinLengthMm), nameof(MaxLengthMm), nameof(IncludeLines), nameof(IncludeArcs), nameof(IncludeSplines),
            nameof(IncludeEllipses), nameof(SelectedWorkset), nameof(OnlyDuplicates), nameof(OnlyShort),
            nameof(OnlyInGroups), nameof(OnlyOwnedByOthers)
        };

        private LineFilter _filter = new();
        private readonly HashSet<string> _excludedStyles = new(StringComparer.CurrentCultureIgnoreCase);
        private string _pendingAssignStyleName;

        /// <summary>Search text: line style, view name, element ID or comment.</summary>
        [ObservableProperty] private string searchText = string.Empty;

        /// <summary>Treat <see cref="SearchText"/> as a regular expression.</summary>
        [ObservableProperty] private bool useRegex;

        /// <summary>Why the regex is invalid, or null.</summary>
        [ObservableProperty] private string regexError;

        /// <summary>View type filter ("All view types" for all).</summary>
        [ObservableProperty] private string selectedViewType;

        /// <summary>Lowest effective line weight shown (1–16).</summary>
        [ObservableProperty] private int minWeight = 1;

        /// <summary>Highest effective line weight shown (1–16).</summary>
        [ObservableProperty] private int maxWeight = 16;

        /// <summary>Shortest length shown in mm; empty for no limit.</summary>
        [ObservableProperty] private double? minLengthMm;

        /// <summary>Longest length shown in mm; empty for no limit.</summary>
        [ObservableProperty] private double? maxLengthMm;

        /// <summary>Show straight lines.</summary>
        [ObservableProperty] private bool includeLines = true;

        /// <summary>Show arcs.</summary>
        [ObservableProperty] private bool includeArcs = true;

        /// <summary>Show splines.</summary>
        [ObservableProperty] private bool includeSplines = true;

        /// <summary>Show ellipses.</summary>
        [ObservableProperty] private bool includeEllipses = true;

        /// <summary>Workset filter ("All worksets" for all).</summary>
        [ObservableProperty] private string selectedWorkset;

        /// <summary>Show only duplicate or overlapping lines.</summary>
        [ObservableProperty] private bool onlyDuplicates;

        /// <summary>Show only lines shorter than the tolerance.</summary>
        [ObservableProperty] private bool onlyShort;

        /// <summary>Show only lines in groups.</summary>
        [ObservableProperty] private bool onlyInGroups;

        /// <summary>Show only lines owned by other users.</summary>
        [ObservableProperty] private bool onlyOwnedByOthers;

        /// <summary>Tolerance in mm for short lines (live) and duplicate geometry (next Find lines).</summary>
        [ObservableProperty] private double toleranceMm = 5.0;

        // ── Live counts ───────────────────────────────────────────────────

        /// <summary>Lines passing the filters.</summary>
        [ObservableProperty] private int foundCount;

        /// <summary>Checked lines among those found.</summary>
        [ObservableProperty] private int checkedCount;

        /// <summary>Duplicate or overlapping lines among those found.</summary>
        [ObservableProperty] private int duplicateCount;

        /// <summary>Short lines among those found.</summary>
        [ObservableProperty] private int shortCount;

        /// <summary>Unused line styles that can be deleted.</summary>
        [ObservableProperty] private int unusedStyleCount;

        /// <summary>
        /// Header checkbox: true when every found line is checked, false when none, null otherwise.
        /// Setting it checks or unchecks every found line.
        /// </summary>
        public bool? AllFoundChecked
        {
            get
            {
                var visible = VisibleRows().ToList();
                if (visible.Count == 0) return false;
                int n = visible.Count(r => r.IsChecked);
                return n == 0 ? false : n == visible.Count ? true : null;
            }
            set
            {
                bool check = value == true;
                _suspendRefresh = true;
                try { foreach (var r in VisibleRows()) r.IsChecked = check; }
                finally { _suspendRefresh = false; }
                UpdateCounts();
            }
        }

        /// <summary>Restores every filter and Show only toggle to its default.</summary>
        [RelayCommand]
        private void ResetFilters()
        {
            _suspendRefresh = true;
            try
            {
                SearchText = string.Empty;
                UseRegex = false;
                SelectedViewType = ViewTypeOptions.FirstOrDefault();
                SelectedWorkset = WorksetOptions.FirstOrDefault();
                MinWeight = 1;
                MaxWeight = 16;
                MinLengthMm = null;
                MaxLengthMm = null;
                IncludeLines = IncludeArcs = IncludeSplines = IncludeEllipses = true;
                OnlyDuplicates = OnlyShort = OnlyInGroups = OnlyOwnedByOthers = false;
                foreach (var s in Styles) s.IsChecked = true;
            }
            finally
            {
                _suspendRefresh = false;
            }
            RebuildFilter();
            Log(Shared.Models.LogLevel.Info, "Filters reset.");
        }

        /// <summary>Ticks every line style in the search checklist.</summary>
        [RelayCommand]
        private void CheckAllStyles() => SetAllStyles(true);

        /// <summary>Unticks every line style in the search checklist.</summary>
        [RelayCommand]
        private void UncheckAllStyles() => SetAllStyles(false);

        /// <inheritdoc/>
        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (_loadingSettings) return;

            if (e.PropertyName == nameof(KeepStylePatterns))
            {
                ApplyKeepPatterns();
            }
            else if (e.PropertyName == nameof(SelectedTabIndex))
            {
                OnTabChanged();
            }
            else if (e.PropertyName == nameof(ToleranceMm))
            {
                double tol = Math.Max(0, ToleranceMm);
                foreach (var r in Rows) r.ApplyTolerance(tol);
                RebuildFilter();
            }
            else if (FilterProperties.Contains(e.PropertyName))
            {
                RebuildFilter();
            }
        }

        private void SetAllStyles(bool check)
        {
            _suspendRefresh = true;
            try { foreach (var s in Styles) s.IsChecked = check; }
            finally { _suspendRefresh = false; }
            RebuildFilter();
        }

        private void RebuildFilter()
        {
            if (_suspendRefresh) return;

            System.Text.RegularExpressions.Regex pattern = null;
            string error = null;
            if (UseRegex) pattern = LineFilter.TryCompile(SearchText, out error);
            RegexError = error == null ? null : "Not a valid regular expression; searching as plain text.";

            var excluded = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            if (Styles.Count > 0)
                foreach (var s in Styles.Where(s => !s.IsChecked)) excluded.Add(s.Name);
            else
                excluded.UnionWith(_excludedStyles);

            _filter = new LineFilter
            {
                Text = SearchText ?? string.Empty,
                Pattern = pattern,
                ExcludedStyles = excluded,
                ViewType = SelectedViewType == AllViewTypes ? string.Empty : SelectedViewType ?? string.Empty,
                MinWeight = Math.Min(MinWeight, MaxWeight),
                MaxWeight = Math.Max(MinWeight, MaxWeight),
                MinLengthMm = MinLengthMm,
                MaxLengthMm = MaxLengthMm,
                Lines = IncludeLines,
                Arcs = IncludeArcs,
                Splines = IncludeSplines,
                Ellipses = IncludeEllipses,
                Workset = SelectedWorkset == AllWorksets ? string.Empty : SelectedWorkset ?? string.Empty,
                OnlyDuplicates = OnlyDuplicates,
                OnlyShort = OnlyShort,
                OnlyInGroups = OnlyInGroups,
                OnlyOwnedByOthers = OnlyOwnedByOthers
            };

            RowsView.Refresh();
            HideConfirm();
            UpdateCounts();
        }

        private void UpdateCounts()
        {
            var visible = VisibleRows().ToList();
            FoundCount = visible.Count;
            CheckedCount = visible.Count(r => r.IsChecked);
            DuplicateCount = visible.Count(r => r.IsDuplicate || r.IsOverlapping);
            ShortCount = visible.Count(r => r.IsShort);
            UnusedStyleCount = UnusedStyles.Count;
            OnPropertyChanged(nameof(AllFoundChecked));
            UpdateThicknessWarning();
            UpdateUnusedHint();
            if (!IsRunning)
                SummaryText = $"{FoundCount} of {Rows.Count} detail line(s) shown · {CheckedCount} checked · {LastScopeText}";
        }

        private IEnumerable<DetailLineRow> VisibleRows() => Rows.Where(_filter.Matches);

        private IEnumerable<DetailLineRow> CheckedVisibleRows() => VisibleRows().Where(r => r.IsChecked);

        private void OnStyleCheckedChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LineStyleItem.IsChecked)) RebuildFilter();
        }

        private void OnRowChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DetailLineRow.IsChecked) && !_suspendRefresh)
            {
                HideConfirm();
                UpdateCounts();
            }
        }
    }
}
