using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorPointComparison.V001.UI.ViewModels;
using System.Windows;
using System.Windows.Input;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorPointComparison.V001.UI.Views
{
    public partial class FloorComparisonWindow : Window
    {
        public FloorComparisonWindow(UIDocument uidoc, UIApplication app, ElementId floorAId, ElementId floorBId)
        {
            InitializeComponent();

            DataContext = new FloorComparisonViewModel(uidoc, app, floorAId, floorBId);
            Focus();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void CopyAllLog_Click(object sender, System.Windows.RoutedEventArgs e) => LogClipboardService.CopyAll(LogListBox.Items);

        private void CopySelectedLog_Click(object sender, System.Windows.RoutedEventArgs e) => LogClipboardService.CopySelected(LogListBox.SelectedItems);
    }
}
