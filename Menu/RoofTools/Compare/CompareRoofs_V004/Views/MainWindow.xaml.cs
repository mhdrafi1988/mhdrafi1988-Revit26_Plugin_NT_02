using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.RoofPointElevationSync.V004
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(UIDocument uiDoc, RoofBase preselectedRoofA = null, RoofBase preselectedRoofB = null)
        {
            InitializeComponent();
            _viewModel = new MainViewModel(uiDoc, preselectedRoofA, preselectedRoofB);
            DataContext = _viewModel;
            Closed += (_, _) => _viewModel.ReleaseRevitQueue();

            LogListBox.SelectionChanged += (s, e) =>
                _viewModel.SelectedLogEntries = LogListBox.SelectedItems;

            // Window stays open after operation completes — no auto-close on Apply.
        }

        private void ChkBox_PreventRowSelect(object sender, MouseButtonEventArgs e)
        {
            // Prevents the checkbox click from cascading into DataGrid row selection.
            e.Handled = false;
            if (sender is FrameworkElement fe)
            {
                var row = ItemsControl.ContainerFromElement((DataGrid)FindDataGridParent(fe), fe) as DataGridRow;
                if (row != null) row.IsSelected = false;
            }
        }

        private static DependencyObject FindDataGridParent(DependencyObject child)
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is DataGrid))
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            return parent;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SaveSettings();
            Close();
        }
    }
}
