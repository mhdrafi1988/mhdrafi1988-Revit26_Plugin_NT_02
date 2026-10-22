using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.WorksetMemory.V001.UI.ViewModels;
using System;
using System.Windows;

namespace Revit26_Plugin_Revised.WorksetMemory.V001.UI.Views
{
    /// <summary>
    /// Workset Memory window (modal); everything is bound to <see cref="WorksetMemoryViewModel"/>.
    /// It closes itself when the view model accepts a reload.
    /// </summary>
    public partial class WorksetMemoryWindow : Window
    {
        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public WorksetMemoryWindow(WorksetMemoryViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Title = ToolCatalog.WorksetMemory.Title;
            viewModel.RequestClose += (_, _) => Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
