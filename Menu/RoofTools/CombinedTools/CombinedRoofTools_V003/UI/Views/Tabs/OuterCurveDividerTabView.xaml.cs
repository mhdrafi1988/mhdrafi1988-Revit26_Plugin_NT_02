using Revit26_Plugin_Revised.OuterCurveDivider.V004.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.CombinedRoofTools.V003.UI.Views.Tabs
{
    public partial class OuterCurveDividerTabView : UserControl
    {
        public OuterCurveDividerTabView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        // Auto-scroll the log to the newest entry as rows are added.
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is CurveDividerViewModel vm)
                vm.LogEntries.CollectionChanged += (s, a) => ScrollLogToEnd();
        }

        private void ScrollLogToEnd()
        {
            // Deferred: scrolling inside CollectionChanged makes WPF throw
            // "ItemsControl is inconsistent with its items source".
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new System.Action(() =>
            {
                if (LogList.Items.Count > 0)
                    LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            }));
        }

        private void CopyAllLog_Click(object sender, System.Windows.RoutedEventArgs e) => LogClipboardService.CopyAll(LogList.Items);

        private void CopySelectedLog_Click(object sender, System.Windows.RoutedEventArgs e) => LogClipboardService.CopySelected(LogList.SelectedItems);
    }
}
