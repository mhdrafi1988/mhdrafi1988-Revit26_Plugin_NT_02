using System.Windows;
using Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.ViewModels;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Views
{
    /// <summary>Code-behind for the EPS Stepped Insulation Layout window.</summary>
    public partial class EpsSteppedInsulationWindow : Window
    {
        public EpsSteppedInsulationWindow(ViewModel_EpsSteppedInsulation viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
