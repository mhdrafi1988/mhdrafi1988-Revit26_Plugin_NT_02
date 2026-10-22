using Revit26_Plugin_Revised.OuterCurveDivider.V004.UI.ViewModels;
using System.Windows;

namespace Revit26_Plugin_Revised.OuterCurveDivider.V004.UI.Views
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
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                        new System.Action(() => LogScroll?.ScrollToEnd()));
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
