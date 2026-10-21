using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using Revit26_Plugin_Revised.IsolatedSection.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.UI.Views
{
    /// <summary>
    /// Isolated Section window; everything is bound to <see cref="IsolatedSectionViewModel"/>. The code-behind
    /// only closes the window, hides it during pick prompts, copies log lines and keeps the newest log line in view.
    /// </summary>
    public partial class IsolatedSectionWindow : Window
    {
        private readonly IsolatedSectionViewModel _viewModel;
        private bool _scrollPending;

        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public IsolatedSectionWindow(IsolatedSectionViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            Title = ToolCatalog.IsolatedSection.Title;

            // Out of the way while Revit asks the user to pick in the model.
            _viewModel.HideWindow = Hide;
            _viewModel.ShowWindow = () =>
            {
                if (!IsVisible) Show();
                Activate();
            };

            _viewModel.LogEntries.CollectionChanged += OnLogChanged;
            Closed += (_, _) =>
            {
                _viewModel.LogEntries.CollectionChanged -= OnLogChanged;
                _viewModel.HideWindow = null;
                _viewModel.ShowWindow = null;
                _viewModel.Shutdown();
            };
        }

        // Deferred and coalesced: scrolling inside CollectionChanged can make WPF throw when many lines arrive at once.
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
    }
}
