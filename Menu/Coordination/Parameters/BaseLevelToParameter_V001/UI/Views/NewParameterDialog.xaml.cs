using System.Windows;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.ViewModels;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.Views
{
    /// <summary>Modal dialog that defines a new parameter for Base Level to Parameter.</summary>
    public partial class NewParameterDialog : Window
    {
        /// <summary>Creates the dialog for <paramref name="viewModel"/>.</summary>
        public NewParameterDialog(NewParameterViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.CloseRequested += result => DialogResult = result;
        }
    }
}
