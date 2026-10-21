using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.SharedParamStudio.V001.UI.ViewModels;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.UI.Views
{
    /// <summary>Modeless window of Shared Parameter Studio; settings are saved when it closes.</summary>
    public partial class SharedParamStudioWindow : Window
    {
        private readonly SharedParamStudioViewModel _viewModel;

        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public SharedParamStudioWindow(SharedParamStudioViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
        }

        private void CategoryCheckBox_Click(object sender, RoutedEventArgs e) => _viewModel.NotifyCategoryTicked();

        private void LogListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _viewModel.SelectedLogEntries.Clear();
            if (sender is not ListBox lb) return;
            foreach (var item in lb.SelectedItems)
                if (item is LogEntry entry) _viewModel.SelectedLogEntries.Add(entry);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closing(object sender, CancelEventArgs e) => _viewModel.OnWindowClosing();
    }
}
