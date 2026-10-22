// ==================================
// File: CreaserAdvWindow.xaml.cs
// Namespace: Revit26_Plugin_Revised.CreaserAdv.V010.Views
// ==================================

using System.Collections.Specialized;
using System.Windows;
using Revit26_Plugin_Revised.CreaserAdv.V010.ViewModels;

namespace Revit26_Plugin_Revised.CreaserAdv.V010.Views
{
    public partial class CreaserAdvWindow : Window
    {
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
            if (e.Action != NotifyCollectionChangedAction.Add || LogList.Items.Count == 0)
                return;

            // Deferred: scrolling inside CollectionChanged makes WPF throw
            // "ItemsControl is inconsistent with its items source".
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new System.Action(() =>
            {
                if (LogList.Items.Count > 0)
                    LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            }));
        }
    }
}
