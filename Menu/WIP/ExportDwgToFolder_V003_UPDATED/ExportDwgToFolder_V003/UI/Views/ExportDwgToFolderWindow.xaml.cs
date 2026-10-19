using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Revit26_Plugin.ExportDwgToFolder.V003.UI.ViewModels;
using Revit26_Plugin.Shared.Services;
// GroupRows (flattens a CollectionViewGroup to its leaf rows) lives alongside
// GroupTickStateConverter in UI/ViewModels/GroupTickStateConverter.cs.

namespace Revit26_Plugin.ExportDwgToFolder.V003.UI.Views
{
    /// <summary>
    /// Export DWG to Folder window. Code-behind handles grid-only behaviour: sorting inside the
    /// groups, one-click ticking, the header tick box, a per-group tick box, Space to tick, and
    /// group collapse state.
    /// </summary>
    public partial class ExportDwgToFolderWindow : Window
    {
        private readonly ExportDwgToFolderViewModel _viewModel;

        /// <summary>Collapsed group keys ("Link" or "Link|Plan"), kept while the grid rebuilds its groups.</summary>
        private readonly HashSet<string> _collapsedGroups = new HashSet<string>();
        private bool _allCollapsed;

        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public ExportDwgToFolderWindow(ExportDwgToFolderViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            RowsGrid.GroupStyle.Add((GroupStyle)FindResource("SourceGroupStyle"));
            RowsGrid.GroupStyle.Add((GroupStyle)FindResource("ViewGroupStyle"));

            _viewModel.RowsReloaded += OnRowsReloaded;
            Loaded += (_, _) => ShowSortArrow();
            Closing += (_, _) => _viewModel.SaveSettings();
            Closed += (_, _) => _viewModel.RowsReloaded -= OnRowsReloaded;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void CopySelectedLog_Click(object sender, RoutedEventArgs e)
            => LogClipboardService.CopySelected(LogList.SelectedItems);

        private void ExportLog_Click(object sender, RoutedEventArgs e)
            => LogExportService.ExportWithPicker(LogList.Items, this, Title);

        // ── Sorting (inside the Source → View type groups) ───────────────────

        private void RowsGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            var column = e.Column;
            if (string.IsNullOrEmpty(column.SortMemberPath)) return;

            var direction = column.SortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            foreach (var other in RowsGrid.Columns)
                if (other != column) other.SortDirection = null;
            column.SortDirection = direction;

            _viewModel.ApplySort(column.SortMemberPath, direction);
        }

        /// <summary>Shows the arrow on the column the view model is sorted by.</summary>
        private void ShowSortArrow()
        {
            foreach (var column in RowsGrid.Columns)
                column.SortDirection = column.SortMemberPath == _viewModel.SortMember
                    ? _viewModel.SortDirection
                    : (ListSortDirection?)null;
        }

        // ── Ticking ──────────────────────────────────────────────────────────

        /// <summary>One click toggles the tick without changing the grid's row selection.</summary>
        private void RowTick_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is CheckBox { DataContext: DwgRowViewModel row } box && box.IsEnabled)
            {
                row.IsSelected = !row.IsSelected;
                e.Handled = true;
            }
        }

        /// <summary>Header tick box: tick / untick every shown row.</summary>
        private void HeaderTick_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.ToggleShownCommand.CanExecute(null))
                _viewModel.ToggleShownCommand.Execute(null);

            if (sender is CheckBox box)
                BindingOperations.GetBindingExpression(box, ToggleButton.IsCheckedProperty)?.UpdateTarget();
        }

        /// <summary>Space ticks / unticks the highlighted rows.</summary>
        private void RowsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is TextBox)
                return;

            _viewModel.ToggleRows(RowsGrid.SelectedItems.OfType<DwgRowViewModel>());
            e.Handled = true;
        }

        /// <summary>
        /// A group header's tick box: tick every row in that group (including nested view-type
        /// sub-groups under a source group) if any is unticked, otherwise untick them all — same
        /// "any unticked → select all" rule as <see cref="ExportDwgToFolderViewModel.ToggleRows"/>
        /// uses for Space / multi-row ticking.
        /// </summary>
        private void GroupTick_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox box)
            {
                if (FindAncestor<GroupItem>(box)?.DataContext is CollectionViewGroup group)
                {
                    var rows = new List<DwgRowViewModel>();
                    GroupRows.Collect(group, rows);
                    _viewModel.ToggleRows(rows);
                }

                // The box's IsChecked is a MultiBinding (computed tri-state), not a direct toggle —
                // refresh it from the view model's now-updated selection rather than letting the
                // CheckBox's own click-cycling logic set it.
                BindingOperations.GetBindingExpressionBase(box, CheckBox.IsCheckedProperty)?.UpdateTarget();
            }

            e.Handled = true;
        }

        // ── Group collapse state ─────────────────────────────────────────────

        /// <summary>A new list: every group starts expanded.</summary>
        private void OnRowsReloaded(object sender, EventArgs e)
        {
            _collapsedGroups.Clear();
            _allCollapsed = false;
            CollapseAllButton.Content = "⊟ Collapse All";
        }

        /// <summary>A group header was (re)built: restore its collapsed state.</summary>
        private void GroupToggle_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton toggle) return;
            string key = GroupKey(toggle);
            if (key != null)
                toggle.IsChecked = !_collapsedGroups.Contains(key);
        }

        private void GroupToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton toggle || !toggle.IsLoaded) return;
            string key = GroupKey(toggle);
            if (key == null) return;

            if (toggle.IsChecked == false) _collapsedGroups.Add(key);
            else _collapsedGroups.Remove(key);
        }

        /// <summary>Collapse All ↔ Expand All.</summary>
        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            _allCollapsed = !_allCollapsed;
            _collapsedGroups.Clear();

            if (_allCollapsed && _viewModel.RowsView.Groups != null)
            {
                // Collapsing the top-level groups hides everything beneath them.
                foreach (var group in _viewModel.RowsView.Groups.OfType<CollectionViewGroup>())
                    _collapsedGroups.Add(group.Name?.ToString() ?? string.Empty);
            }

            foreach (var toggle in FindDescendants<ToggleButton>(RowsGrid).Where(t => Equals(t.Tag, "GroupToggle")))
            {
                string key = GroupKey(toggle);
                if (key != null) toggle.IsChecked = !_collapsedGroups.Contains(key);
            }

            CollapseAllButton.Content = _allCollapsed ? "⊞ Expand All" : "⊟ Collapse All";
        }

        /// <summary>"Link" for a top-level group, "Link|Plan" for a view-type group under it.</summary>
        private static string GroupKey(DependencyObject toggle)
        {
            var groupItem = FindAncestor<GroupItem>(toggle);
            if (groupItem?.DataContext is not CollectionViewGroup group) return null;

            var parentItem = FindAncestor<GroupItem>(VisualTreeHelper.GetParent(groupItem));
            string name = group.Name?.ToString() ?? string.Empty;
            return parentItem?.DataContext is CollectionViewGroup parent
                ? $"{parent.Name}|{name}"
                : name;
        }

        private static T FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            var current = start;
            while (current != null)
            {
                if (current is T match) return match;
                current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }
            return null;
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var nested in FindDescendants<T>(child))
                    yield return nested;
            }
        }
    }
}
