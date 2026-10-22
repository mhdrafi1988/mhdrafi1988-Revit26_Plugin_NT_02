using Revit26_Plugin_Revised.OuterCurveDivider.V006.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;
using System.Windows;

namespace Revit26_Plugin_Revised.OuterCurveDivider.V006.UI.Views
{
    public partial class CurveDividerWindow : Window
    {
        public CurveDividerWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        // Auto-scroll the log to the newest entry as rows are added.
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is CurveDividerViewModel vm)
                vm.LogEntries.CollectionChanged += (s, a) =>
                {
                    // Deferred: scrolling inside CollectionChanged makes WPF throw
                    // "ItemsControl is inconsistent with its items source".
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new System.Action(() =>
                    {
                        if (LogList.Items.Count > 0)
                            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                    }));
                };
        }

        private void CopyAllLog_Click(object sender, RoutedEventArgs e) => LogClipboardService.CopyAll(LogList.Items);

        private void CopySelectedLog_Click(object sender, RoutedEventArgs e) => LogClipboardService.CopySelected(LogList.SelectedItems);

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
