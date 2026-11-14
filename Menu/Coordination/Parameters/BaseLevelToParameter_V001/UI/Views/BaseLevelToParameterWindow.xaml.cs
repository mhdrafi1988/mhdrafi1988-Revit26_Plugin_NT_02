using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.Views
{
    /// <summary>Modeless window of Base Level to Parameter; it stays open after a run.</summary>
    public partial class BaseLevelToParameterWindow : Window
    {
        private readonly BaseLevelToParameterViewModel _viewModel;

        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public BaseLevelToParameterWindow(BaseLevelToParameterViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _viewModel.OwnerWindow = this;
            _viewModel.ShowNewParameterDialog = ShowNewParameterDialog;
            _viewModel.ConfirmRewrite = ConfirmRewrite;
            DataContext = viewModel;
        }

        private bool ShowNewParameterDialog(NewParameterViewModel dialogViewModel)
        {
            var dialog = new NewParameterDialog(dialogViewModel) { Owner = this };
            return dialog.ShowDialog() == true;
        }

        private RewriteAnswer ConfirmRewrite(RewriteConfirmation confirmation)
        {
            var dialog = new RewriteConfirmDialog(confirmation) { Owner = this };
            return dialog.ShowDialog() == true ? dialog.Answer : RewriteAnswer.Cancel;
        }

        private void LogListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _viewModel.SelectedLogEntries.Clear();
            if (sender is not ListBox lb) return;
            foreach (var item in lb.SelectedItems)
                if (item is LogEntry entry) _viewModel.SelectedLogEntries.Add(entry);
        }

        private void ElementsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not DataGrid grid) return;
            _viewModel.SelectedRows = grid.SelectedItems.OfType<ElementRowViewModel>().ToList();
        }

        private void ExpandAll_Click(object sender, RoutedEventArgs e) => SetGroupsExpanded(true);

        private void CollapseAll_Click(object sender, RoutedEventArgs e) => SetGroupsExpanded(false);

        private void SetGroupsExpanded(bool expanded)
        {
            foreach (Expander expander in FindChildren<Expander>(ElementsGrid))
                expander.IsExpanded = expanded;
        }

        private static IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) yield return match;
                foreach (T nested in FindChildren<T>(child)) yield return nested;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closing(object sender, CancelEventArgs e) => _viewModel.OnWindowClosing();
    }
}
