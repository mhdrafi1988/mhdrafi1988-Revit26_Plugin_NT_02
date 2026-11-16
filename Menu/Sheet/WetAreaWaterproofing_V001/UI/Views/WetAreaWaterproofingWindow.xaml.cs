using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.UI.Views
{
    /// <summary>
    /// Wet Area Waterproofing window. The code-behind only does what binding cannot: header sort
    /// and filter clicks, the column chooser, remembering the column layout, and the folder picker.
    /// </summary>
    public partial class WetAreaWaterproofingWindow : Window
    {
        private readonly ViewModel_WetAreaWaterproofing _viewModel;
        private readonly Dictionary<DataGridColumn, string> _headers = new();
        private bool _closeRefusedOnce;

        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public WetAreaWaterproofingWindow(ViewModel_WetAreaWaterproofing viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            _viewModel.OwnerWindow = this;
            Title = ToolCatalog.WetAreaWaterproofing.Title;
            AllRoomsRadio.IsChecked = !_viewModel.WetOnly;

            foreach (DataGridColumn column in RoomsGrid.Columns.Where(c => !string.IsNullOrEmpty(c.SortMemberPath)))
                _headers[column] = column.Header as string ?? column.SortMemberPath;

            _viewModel.SortChanged += OnSortChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Loaded += (_, _) =>
            {
                ApplyColumnLayout();
                UpdateHeaders();
            };
            Closing += OnClosing;
            Closed += (_, _) =>
            {
                _viewModel.SortChanged -= OnSortChanged;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.Dispose();
            };
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_viewModel.IsBusy)
            {
                // First attempt: wait. A later attempt may close anyway, so the window can never be stuck open.
                bool closeAnyway = _closeRefusedOnce && MessageBox.Show(this,
                    "Revit is still working on the last request.\n\nClose the window anyway? Work already started in Revit is not undone.",
                    ToolCatalog.WetAreaWaterproofing.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
                if (!closeAnyway)
                {
                    e.Cancel = true;
                    if (!_closeRefusedOnce)
                        MessageBox.Show(this, "Revit is still working on the last request. Close the window when it has finished.",
                            ToolCatalog.WetAreaWaterproofing.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                    _closeRefusedOnce = true;
                    return;
                }
            }
            SaveColumnLayout();
            _viewModel.SaveSettings();
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel_WetAreaWaterproofing.FilteredColumns)) UpdateHeaders();
            else if (e.PropertyName == nameof(ViewModel_WetAreaWaterproofing.ActiveFilter) && _viewModel.ActiveFilter == null)
                FilterPopup.IsOpen = false;
        }

        private void OnSortChanged(object sender, EventArgs e) => UpdateHeaders();

        /// <summary>Header arrows for the sort, and a dot on columns that have a filter on.</summary>
        private void UpdateHeaders()
        {
            IReadOnlyCollection<string> filtered = _viewModel.FilteredColumns;
            foreach (var pair in _headers)
            {
                string name = pair.Key.SortMemberPath;
                pair.Key.SortDirection = _viewModel.SortDirectionOf(name);
                pair.Key.Header = filtered.Contains(name) ? pair.Value + " ●" : pair.Value;
            }
        }

        private void RoomsGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true; // sorting is done by the view model so filters, groups and ticks stay in step
            bool additive = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            _viewModel.SortBy(e.Column.SortMemberPath, additive);
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            DependencyObject current = sender as DependencyObject;
            while (current != null && current is not DataGridColumnHeader) current = VisualTreeHelper.GetParent(current);
            if (current is not DataGridColumnHeader header || header.Column == null) return;

            _viewModel.OpenFilter(header.Column.SortMemberPath);
            FilterPopup.PlacementTarget = header;
            FilterPopup.IsOpen = true;
        }

        private void FilterPopup_Closed(object sender, EventArgs e)
        {
            if (_viewModel.ActiveFilter != null) _viewModel.ActiveFilter = null; // closed by clicking away: nothing applied
        }

        private void LevelButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not LevelButtonViewModel level) return;
            bool solo = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            _viewModel.ToggleLevel(level.Level, solo);
        }

        private void LinkCheckBox_Click(object sender, RoutedEventArgs e) => _viewModel.LinkSelectionChanged();

        /// <summary>Column chooser: a tick per column.</summary>
        private void ColumnsButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
            foreach (var pair in _headers)
            {
                DataGridColumn column = pair.Key;
                var item = new MenuItem
                {
                    Header = pair.Value, IsCheckable = true, StaysOpenOnClick = true,
                    IsChecked = column.Visibility == Visibility.Visible
                };
                item.Click += (_, _) => column.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        /// <summary>Puts the columns back the way the user left them.</summary>
        private void ApplyColumnLayout()
        {
            GridState state = _viewModel.Settings.Grid;
            foreach (var pair in _headers)
            {
                string name = pair.Key.SortMemberPath;
                if (state.HiddenColumns.Contains(name)) pair.Key.Visibility = Visibility.Collapsed;
                if (state.ColumnWidths.TryGetValue(name, out double width) && width >= 30 && width <= 800 && name != "Name")
                    pair.Key.Width = new DataGridLength(width);
            }
            int index = 1; // the tick column stays first
            foreach (string name in state.ColumnOrder)
            {
                DataGridColumn column = _headers.Keys.FirstOrDefault(c => c.SortMemberPath == name);
                if (column != null && index < RoomsGrid.Columns.Count) column.DisplayIndex = index++;
            }
        }

        private void SaveColumnLayout()
        {
            GridState state = _viewModel.Settings.Grid;
            state.HiddenColumns = _headers.Keys.Where(c => c.Visibility != Visibility.Visible).Select(c => c.SortMemberPath).ToList();
            state.ColumnOrder = _headers.Keys.OrderBy(c => c.DisplayIndex).Select(c => c.SortMemberPath).ToList();
            state.ColumnWidths = _headers.Keys.ToDictionary(c => c.SortMemberPath, c => c.ActualWidth);
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Folder for the Excel reports" };
            try
            {
                if (System.IO.Directory.Exists(_viewModel.ReportFolder)) dialog.InitialDirectory = _viewModel.ReportFolder;
            }
            catch (ArgumentException) { /* a half-typed path: open the dialog at its default place */ }
            if (dialog.ShowDialog(this) == true) _viewModel.ReportFolder = dialog.FolderName;
        }

        private void CopySelectedLogs_Click(object sender, RoutedEventArgs e)
            => LogClipboardService.CopySelected(LogListBox.SelectedItems);

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
