using System.Windows;
using System.Windows.Input;
using Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.UI.Views
{
    public partial class FloorDetailLineIntersectWindow : Window
    {
        public FloorDetailLineIntersectWindow(FloorDetailLineIntersectViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            KeyDown += Window_KeyDown;
        }

        // ── Esc = Close (modeless dialog convention) ─────────────────────────
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void CopySelectedLog_Click(object sender, System.Windows.RoutedEventArgs e) => LogClipboardService.CopySelected(LogList.SelectedItems);
    }
}
