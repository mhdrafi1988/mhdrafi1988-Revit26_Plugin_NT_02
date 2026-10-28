using System.Windows;
using Revit26_Plugin_Revised.WorksetRenamer.V005.ViewModels;

namespace Revit26_Plugin_Revised.WorksetRenamer.V005.Views
{
    public partial class WorksetRenamerView : Window
    {
        public WorksetRenamerView(WorksetRenamerViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Closed += (_, _) => viewModel.Dispose();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
