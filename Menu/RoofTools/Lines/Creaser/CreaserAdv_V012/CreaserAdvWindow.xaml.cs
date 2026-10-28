// ==================================
// File: CreaserAdvWindow.xaml.cs
// Namespace: Revit26_Plugin_Revised.CreaserAdv.V012.Views
// ==================================

using System.Collections.Specialized;
using System.Windows;
using Revit26_Plugin_Revised.CreaserAdv.V012.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.CreaserAdv.V012.Views
{
    /// <summary>Modeless Creaser Adv window; the run itself goes through <c>CreaserAdvHandler</c>.</summary>
    public partial class CreaserAdvWindow : Window
    {
        /// <summary>True while a scroll to the newest log line is queued.</summary>
        private bool _scrollQueued;

        /// <summary>Creates the window bound to <paramref name="viewModel"/>.</summary>
        public CreaserAdvWindow(CreaserAdvViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Keep the newest log line in view as a run streams its progress.
            viewModel.LogEntries.CollectionChanged += OnLogChanged;
            Closed += (_, _) => viewModel.LogEntries.CollectionChanged -= OnLogChanged;
        }

        private void OnLogChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || _scrollQueued)
                return;

            // Deferred: scrolling inside CollectionChanged makes WPF throw
            // "ItemsControl is inconsistent with its items source".
            // One scroll per burst: a large run posts thousands of lines at once,
            // and a scroll per line keeps the window busy after the run ends.
            _scrollQueued = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new System.Action(() =>
            {
                _scrollQueued = false;
                if (LogList.Items.Count > 0)
                    LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            }));
        }

        private void CopySelectedLog_Click(object sender, RoutedEventArgs e) => LogClipboardService.CopySelected(LogList.SelectedItems);

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
