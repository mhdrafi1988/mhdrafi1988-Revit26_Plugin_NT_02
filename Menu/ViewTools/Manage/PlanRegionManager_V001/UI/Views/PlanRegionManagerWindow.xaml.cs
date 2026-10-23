using Revit26_Plugin_Revised.PlanRegionManager.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Windows;

namespace Revit26_Plugin_Revised.PlanRegionManager.V001.UI.Views
{
    /// <summary>
    /// Plan Region Manager window; everything is bound to <see cref="PlanRegionManagerViewModel"/>.
    /// </summary>
    public partial class PlanRegionManagerWindow : Window
    {
        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public PlanRegionManagerWindow(PlanRegionManagerViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Title = ToolCatalog.PlanRegionManager.Title;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
