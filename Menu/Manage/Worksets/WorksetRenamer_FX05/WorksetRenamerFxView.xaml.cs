using System.IO;
using System.Linq;
using System.Windows;
using Revit26_Plugin_Revised.WorksetRenamer.FX05.ViewModels;

namespace Revit26_Plugin_Revised.WorksetRenamer.FX05.Views
{
    public partial class WorksetRenamerFxView : Window
    {
        private readonly WorksetRenamerFxViewModel _viewModel;

        public WorksetRenamerFxView(WorksetRenamerFxViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            Closed += (_, _) => viewModel.Dispose();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var xlsx = files?.FirstOrDefault(f => Path.GetExtension(f).Equals(".xlsx", System.StringComparison.OrdinalIgnoreCase));
            if (xlsx != null)
                _viewModel.LoadExcelFile(xlsx);
        }
    }
}
