using Revit26_Plugin.BulkRename.V001.UI.ViewModels;
using Revit26_Plugin.Shared.Services;
using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Revit26_Plugin.BulkRename.V001.UI.Views
{
    /// <summary>
    /// Bulk Rename window; everything is bound to <see cref="BulkRenameViewModel"/>. The code-behind only
    /// closes the window, copies log lines, keeps the newest log line in view and asks the view model's
    /// questions in dialogs owned by this window.
    /// </summary>
    public partial class BulkRenameWindow : Window
    {
        private readonly BulkRenameViewModel _viewModel;
        private bool _scrollPending;

        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public BulkRenameWindow(BulkRenameViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            Title = ToolCatalog.BulkRename.Title;

            // Owned by this window, so a question never opens behind it.
            _viewModel.Confirm = question =>
                MessageBox.Show(this, question, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

            _viewModel.LogEntries.CollectionChanged += OnLogChanged;
            Closed += (_, _) =>
            {
                _viewModel.LogEntries.CollectionChanged -= OnLogChanged;
                _viewModel.Shutdown();
            };
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
