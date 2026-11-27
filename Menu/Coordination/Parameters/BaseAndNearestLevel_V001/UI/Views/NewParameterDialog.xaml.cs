using System.Windows;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.ViewModels;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.Views
{
    /// <summary>
    /// Defines the new shared parameter for one slot. Nothing is created here: the main window creates it
    /// when Write Parameters runs.
    /// </summary>
    public partial class NewParameterDialog : Window
    {
        /// <summary>Creates the dialog for <paramref name="viewModel"/>.</summary>
        public NewParameterDialog(NewParameterViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Create_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
