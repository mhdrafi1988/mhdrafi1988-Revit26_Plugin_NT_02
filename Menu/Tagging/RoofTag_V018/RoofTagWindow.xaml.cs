using Autodesk.Revit.UI;
using System.Windows;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofTag.V018
{
    public partial class RoofTagWindow : Window
    {
        public RoofTagWindow(UIApplication uiApp)
        {
            InitializeComponent();
            DataContext = new RoofTagViewModel(uiApp);
        }

        // Shown modeless (Show), so DialogResult must not be set; just close.
        private void OnOK(object sender, RoutedEventArgs e) => Close();

        private void OnCancel(object sender, RoutedEventArgs e) => Close();

        private void CopySelectedLog_Click(object sender, System.Windows.RoutedEventArgs e) => LogClipboardService.CopySelected(LogList.SelectedItems);
    }
}
