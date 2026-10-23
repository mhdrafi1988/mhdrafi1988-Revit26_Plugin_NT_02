using Revit26_Plugin_Revised.DetailLineManager.V002.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Revit26_Plugin_Revised.DetailLineManager.V002.UI.Views
{
    /// <summary>
    /// Detail Line Manager window (modeless); everything is bound to <see cref="MainViewModel"/>.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            Title = ToolCatalog.DetailLineManagerV002.Title;
            Closed += (_, _) => _viewModel.OnWindowClosed();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void LogListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => _viewModel.SelectedLogEntries = LogListBox.SelectedItems.OfType<LogEntry>().ToList();
    }
}
