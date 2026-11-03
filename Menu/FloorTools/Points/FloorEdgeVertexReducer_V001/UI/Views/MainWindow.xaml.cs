// =======================================================
// File: MainWindow.xaml.cs
// Location: UI/Views/
// Changes vs V003: added Window_KeyDown for Esc-to-close (modeless
// dialog convention) — Close_Click is unchanged.
// =======================================================

using System.Windows;
using System.Windows.Input;
using Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.UI.ViewModels;

namespace Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
        }

        private void CopySelectedLogs_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.CopySelectedLogs(LogList.SelectedItems);
        }

        // ── Esc = Close (modeless dialog convention) ─────────────────────────
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
