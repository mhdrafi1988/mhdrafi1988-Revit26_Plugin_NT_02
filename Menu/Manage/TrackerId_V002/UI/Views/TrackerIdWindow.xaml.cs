using Microsoft.Win32;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.TrackerId.V002.UI.ViewModels;
using System;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.TrackerId.V002.UI.Views
{
    /// <summary>
    /// Tracker ID window; everything is bound to <see cref="TrackerIdViewModel"/>. The code-behind only
    /// closes the window, copies log lines, keeps the newest log line in view, opens the file dialogs,
    /// passes the grids' highlighted rows to the view model, zooms on a row double-click and asks the
    /// view model's questions in dialogs owned by this window.
    /// </summary>
    public partial class TrackerIdWindow : Window
    {
        private readonly TrackerIdViewModel _viewModel;
        private bool _scrollPending;

        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public TrackerIdWindow(TrackerIdViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            Title = ToolCatalog.TrackerIdV002.Title;

            // Owned by this window, so a question never opens behind it.
            _viewModel.Confirm = question =>
                MessageBox.Show(this, question, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            _viewModel.PickExportPath = PickExportPath;
            _viewModel.PickStatusFile = PickStatusFile;
            _viewModel.PickTemplatePath = PickTemplatePath;
            _viewModel.PickReportPath = PickReportPath;

            _viewModel.LogEntries.CollectionChanged += OnLogChanged;
            Closed += (_, _) =>
            {
                _viewModel.LogEntries.CollectionChanged -= OnLogChanged;
                _viewModel.Shutdown();
            };
        }

        private string PickExportPath(string current)
        {
            bool excel = string.Equals(Path.GetExtension(current ?? string.Empty), ".xlsx", StringComparison.OrdinalIgnoreCase);
            var dialog = new SaveFileDialog
            {
                Title = "Export Tracker IDs for Power BI",
                Filter = excel ? "Excel workbook (*.xlsx)|*.xlsx" : "CSV file (*.csv)|*.csv",
                DefaultExt = excel ? ".xlsx" : ".csv",
                AddExtension = true,
                OverwritePrompt = false,
                FileName = string.IsNullOrWhiteSpace(current) ? (excel ? "Tracker_Elements.xlsx" : "Tracker_Elements.csv") : Path.GetFileName(current)
            };
            SetFolder(dialog, current);
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        private string PickStatusFile(string current)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose the status file",
                Filter = "Status file (*.xlsx;*.xlsm;*.csv)|*.xlsx;*.xlsm;*.csv|All files (*.*)|*.*",
                CheckFileExists = true,
                FileName = string.IsNullOrWhiteSpace(current) ? string.Empty : Path.GetFileName(current)
            };
            SetFolder(dialog, current);
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        // An existing template is extended, never replaced, so there is no overwrite prompt.
        private string PickTemplatePath(string current)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Status template (new or existing)",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                AddExtension = true,
                OverwritePrompt = false,
                FileName = string.IsNullOrWhiteSpace(current) ? "Tracker_Status.xlsx" : Path.GetFileName(current)
            };
            SetFolder(dialog, current);
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        private string PickReportPath(string suggested)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save the health report",
                Filter = "CSV file (*.csv)|*.csv",
                DefaultExt = ".csv",
                AddExtension = true,
                FileName = string.IsNullOrWhiteSpace(suggested) ? "Tracker_Health.csv" : Path.GetFileName(suggested)
            };
            SetFolder(dialog, suggested);
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        private static void SetFolder(FileDialog dialog, string current)
        {
            try
            {
                string folder = string.IsNullOrWhiteSpace(current) ? null : Path.GetDirectoryName(current);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) dialog.InitialDirectory = folder;
            }
            catch (ArgumentException)
            {
                // A typed path with invalid characters: open the dialog at its default folder.
            }
        }

        // Scrolling inside a CollectionChanged handler makes WPF throw "ItemsControl is inconsistent with
        // its items source" when many lines arrive at once, so the scroll is deferred and coalesced.
        private void OnLogChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || _scrollPending) return;

            _scrollPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _scrollPending = false;
                if (LogList.Items.Count > 0)
                    LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            }));
        }

        // Double-click on a result, status or health row selects that element in Revit and zooms to it.
        private void ElementGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid grid) return;
            var row = ItemsControl.ContainerFromElement(grid, e.OriginalSource as DependencyObject) as DataGridRow;
            if (row == null) return;

            long? id = row.Item switch
            {
                PlanRowViewModel p => p.ElementId,
                StatusRowViewModel s => s.ElementId,
                HealthRowViewModel h => h.Issue.ElementId,
                _ => null
            };
            if (id.HasValue) _viewModel.ZoomToElement(id.Value);
        }

        private void PlanGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => _viewModel.SelectedPlanItems = PlanGrid.SelectedItems;

        private void StatusGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => _viewModel.SelectedStatusItems = StatusGrid.SelectedItems;

        private void HealthGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => _viewModel.SelectedHealthItems = HealthGrid.SelectedItems;

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void CopyAllLog_Click(object sender, RoutedEventArgs e) => LogClipboardService.CopyAll(LogList.Items);

        private void CopySelectedLog_Click(object sender, RoutedEventArgs e) => LogClipboardService.CopySelected(LogList.SelectedItems);

        private void ExportLog_Click(object sender, RoutedEventArgs e) => LogExportService.ExportWithPicker(LogList.Items, this, Title);

        private void CheckBoxCell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (sender is System.Windows.Controls.CheckBox cb && cb.IsEnabled)
                cb.IsChecked = !cb.IsChecked;
        }
    }
}
