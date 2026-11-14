using System.Windows;
using MahApps.Metro.Controls;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.ViewModels;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.Views
{
    /// <summary>
    /// Code-behind for the Floor Ridge (Voronoi) tool window. Contains no logic beyond
    /// wiring the view model as <see cref="FrameworkElement.DataContext"/> — all
    /// behavior lives in <see cref="FloorRidgeViewModel"/> per the project's MVVM convention.
    /// </summary>
    public partial class FloorRidgeView : MetroWindow
    {
        /// <summary>Initializes the window and binds it to <paramref name="viewModel"/>.</summary>
        /// <param name="viewModel">The view model driving this window.</param>
        public FloorRidgeView(FloorRidgeViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        // Copy Selected: the pipeline log is a read-only TextBox, so copy its text selection.
        private void CopySelectedLog_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(PipelineLogBox.SelectedText))
                LogClipboardService.CopyAll(new[] { PipelineLogBox.SelectedText });
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
