using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.Views
{
    /// <summary>Modeless window of Base and Nearest Level; it stays open after a run.</summary>
    public partial class BaseAndNearestLevelWindow : Window
    {
        private readonly ViewModel_BaseAndNearestLevel _viewModel;

        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public BaseAndNearestLevelWindow(ViewModel_BaseAndNearestLevel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _viewModel.OwnerWindow = this;
            _viewModel.ShowNewParameterDialog = ShowNewParameterDialog;
            _viewModel.ConfirmRehost = ConfirmRehost;
            _viewModel.ConfirmRewrite = ConfirmRewrite;
            DataContext = viewModel;
        }

        private bool ShowNewParameterDialog(NewParameterViewModel dialogViewModel)
        {
            var dialog = new NewParameterDialog(dialogViewModel) { Owner = this };
            return dialog.ShowDialog() == true;
        }

        private RehostConfirmation ConfirmRehost(RehostConfirmation confirmation)
        {
            var dialog = new RehostConfirmDialog(confirmation) { Owner = this };
            return dialog.ShowDialog() == true ? dialog.Confirmation : null;
        }

        private bool ConfirmRewrite(string message)
        {
            MessageBoxResult answer = MessageBox.Show(this, message, "Replace existing values?",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            return answer == MessageBoxResult.Yes;
        }

        private void LogListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _viewModel.SelectedLogEntries.Clear();
            if (sender is not ListBox list) return;
            foreach (object item in list.SelectedItems)
                if (item is LogEntry entry) _viewModel.SelectedLogEntries.Add(entry);
        }

        private void ElementsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not DataGrid grid) return;
            _viewModel.SelectedRows = grid.SelectedItems.OfType<ElementRowViewModel>().ToList();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closing(object sender, CancelEventArgs e) => _viewModel.OnWindowClosing();
    }
}
