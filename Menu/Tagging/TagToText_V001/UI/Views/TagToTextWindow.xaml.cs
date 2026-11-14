using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.TagToText.V001.UI.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Revit26_Plugin_Revised.TagToText.V001.UI.Views
{
    /// <summary>
    /// Tag To Text window; everything is bound to <see cref="TagToTextViewModel"/>.
    /// Saves the options when it closes, and can't be closed while a run is in progress.
    /// </summary>
    public partial class TagToTextWindow : Window
    {
        private readonly TagToTextViewModel _viewModel;

        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public TagToTextWindow(TagToTextViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            // The list can't be regrouped, filtered or reloaded while a text is being edited: end the edit first.
            _viewModel.CommitTableEdit = () => TagGrid.CommitEdit(DataGridEditingUnit.Row, true);
            Title = ToolCatalog.TagToText.Title;
            Closing += OnClosing;
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_viewModel.IsRunning)
            {
                MessageBox.Show(this, "Wait for the run to finish, or click Cancel first.", ToolCatalog.TagToText.Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                e.Cancel = true;
                return;
            }
            _viewModel.SaveSettings();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
