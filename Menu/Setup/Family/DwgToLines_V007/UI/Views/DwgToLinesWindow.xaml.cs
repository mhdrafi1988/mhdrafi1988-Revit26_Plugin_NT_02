// ==============================================
// File: DwgToLinesWindow.xaml.cs
// Layer: UI/Views
// ==============================================

using Autodesk.Revit.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Revit26_Plugin_Revised.DwgToLines.V007.UI.ViewModels;

namespace Revit26_Plugin_Revised.DwgToLines.V007.UI.Views
{
    public partial class DwgToLinesWindow : Window
    {
        public DwgToLinesWindow(UIApplication uiApp)
        {
            InitializeComponent();

            var vm = new DwgToLinesViewModel(uiApp);
            vm.RequestClose += Close;
            DataContext = vm;
            Closed += (_, _) => vm.Dispose();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }

        private void LogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is DwgToLinesViewModel vm && sender is ListBox list)
                vm.SelectedLogEntries = list.SelectedItems;
        }
    }
}
